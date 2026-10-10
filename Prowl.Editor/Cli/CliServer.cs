// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Cli;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.Tasks;

using Debug = Prowl.Runtime.Debug;

namespace Prowl.Editor;

/// <summary>
/// Loopback HTTP server the prowl CLI talks to. Listens on 127.0.0.1 on a random port and writes the port and a
/// per session token to Library/Cli.lock. Every request must carry that token. Commands run on the main thread.
/// </summary>
public static class CliServer
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxBodyBytes = 16 * 1024 * 1024;
    private const int MaxConnections = 32;
    private const int MaxLogLines = 500;
    private static readonly TimeSpan s_readTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_lockCheckInterval = TimeSpan.FromSeconds(2);

    private sealed class Server(TcpListener listener, string token, string projectRoot, string libraryPath)
    {
        public readonly TcpListener Listener = listener;
        public readonly CancellationTokenSource Cts = new();
        public readonly string Token = token;
        public readonly string ProjectRoot = projectRoot;
        public readonly string LockPath = Path.Combine(libraryPath, CliProtocol.LockFileName);
        public int Port => ((IPEndPoint)Listener.LocalEndpoint).Port;
        public bool WarnedAboutOtherEditor;
    }

    private static volatile Server? s_server;
    private static string? s_handledRoot;
    private static bool s_shutDown;
    private static int s_connections;
    private static readonly Stopwatch s_lockCheck = Stopwatch.StartNew();

    private static readonly CliSynchronizationContext s_context = new();
    private static readonly AsyncLocal<LogCapture?> s_capture = new();

    static CliServer()
    {
        Debug.OnLog += CaptureLog;
    }

    private static readonly TimeSpan s_activeLinger = TimeSpan.FromSeconds(10);
    private static readonly Stopwatch s_sinceActivity = new();
    private static int s_running;

    /// <summary>
    /// Whether the CLI is driving the editor: a command is running or one finished in the last few seconds. While true the
    /// editor imports, compiles and paces frames as if focused, since nobody is at the window.
    /// </summary>
    public static bool IsActive
    {
        get
        {
            if (Volatile.Read(ref s_running) > 0) return true;
            lock (s_sinceActivity) return s_sinceActivity.IsRunning && s_sinceActivity.Elapsed < s_activeLinger;
        }
    }

    private static void BeginActivity() => Interlocked.Increment(ref s_running);

    private static void EndActivity()
    {
        lock (s_sinceActivity) s_sinceActivity.Restart();
        Interlocked.Decrement(ref s_running);
    }

    public static bool IsRunning => s_server != null;
    public static int Port => s_server?.Port ?? 0;
    public static string Token => s_server?.Token ?? "";

    /// <summary> Keeps the server pointed at the open project, keeps its lock file current, and resumes awaiting commands. Called once per editor frame. </summary>
    internal static void Update()
    {
        PumpContinuations();
        if (s_shutDown) return;

        var project = Project.Current;
        string? root = project?.RootPath;
        if (root != s_handledRoot)
        {
            Stop();
            s_handledRoot = root;
            if (project == null) return;

            try { Start(project.RootPath, project.LibraryPath); }
            catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
            {
                Debug.LogError($"[CLI] Could not start the command server, the prowl CLI will not reach this editor: {ex.Message}");
            }
            return;
        }

        if (s_server is { } server && s_lockCheck.Elapsed > s_lockCheckInterval)
        {
            s_lockCheck.Restart();
            EnsureLockFile(server);
        }
    }

    /// <summary> Runs command continuations that were waiting on the main thread. Independent of the gameplay session, so a command survives play mode changes and script reloads. </summary>
    internal static void PumpContinuations() => s_context.Pump();

    public static void Start(string projectRoot, string libraryPath)
    {
        Stop();

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = new Server(listener, System.Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), projectRoot, libraryPath);
        s_server = server;

        EnsureLockFile(server);
        _ = Task.Run(() => AcceptLoopAsync(server));
    }

    public static void Stop()
    {
        var server = s_server;
        if (server == null) return;
        s_server = null;

        server.Cts.Cancel();
        server.Listener.Stop();

        try
        {
            if (ReadLockFile(server.LockPath)?.Token == server.Token)
                File.Delete(server.LockPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary> Stops the server for good, for when the editor closes. </summary>
    public static void Shutdown()
    {
        s_shutDown = true;
        Stop();
    }

    private static void EnsureLockFile(Server server)
    {
        var existing = ReadLockFile(server.LockPath);
        if (existing?.Token == server.Token) return;

        if (existing != null && existing.ProcessId != Environment.ProcessId && IsProcessAlive(existing.ProcessId))
        {
            if (!server.WarnedAboutOtherEditor)
                Debug.LogWarning($"[CLI] Another editor (process {existing.ProcessId}) already serves this project, so the prowl CLI talks to that one.");
            server.WarnedAboutOtherEditor = true;
            return;
        }

        var lockFile = new CliLockFile
        {
            Port = server.Port,
            Token = server.Token,
            ProcessId = Environment.ProcessId,
            ProjectPath = server.ProjectRoot,
            EditorVersion = typeof(CliServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "",
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(server.LockPath)!);
            string temp = server.LockPath + ".tmp";
            File.Delete(temp);

            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options))
                JsonSerializer.Serialize(stream, lockFile, CliProtocol.Json);

            File.Move(temp, server.LockPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.LogWarningOnce("CliServer.LockFile", $"[CLI] Could not write {CliProtocol.LockFileName}, the prowl CLI will not find this editor: {ex.Message}");
        }
    }

    private static CliLockFile? ReadLockFile(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<CliLockFile>(File.ReadAllText(path), CliProtocol.Json) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
    }

    private static async Task AcceptLoopAsync(Server server)
    {
        var ct = server.Cts.Token;
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await server.Listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { return; }
            catch (SocketException) when (!ct.IsCancellationRequested) { continue; }
            catch (SocketException) { return; }

            if (Interlocked.Increment(ref s_connections) > MaxConnections)
            {
                Interlocked.Decrement(ref s_connections);
                client.Dispose();
                continue;
            }

            _ = Task.Run(async () =>
            {
                try { await HandleAsync(client, server).ConfigureAwait(false); }
                finally { Interlocked.Decrement(ref s_connections); }
            });
        }
    }

    private static async Task HandleAsync(TcpClient client, Server server)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                var (status, body) = await ReadAndRouteAsync(stream, server).ConfigureAwait(false);
                await WriteResponseAsync(stream, status, body, server.Cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private static async Task<(int Status, string Body)> ReadAndRouteAsync(NetworkStream stream, Server server)
    {
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(server.Cts.Token);
        readTimeout.CancelAfter(s_readTimeout);

        var head = await ReadHeadAsync(stream, readTimeout.Token).ConfigureAwait(false);
        if (head == null) return (400, Error("Malformed request."));
        var (method, path, headers, buffered) = head.Value;

        if (headers.TryGetValue("Host", out string? host) && !IsLoopbackHost(host, server.Port))
            return (403, Error("Requests must be addressed to the loopback interface."));
        if (!headers.TryGetValue(CliProtocol.TokenHeader, out string? token) || !TokenMatches(token, server.Token))
            return (401, Error("Missing or wrong token."));
        if (headers.ContainsKey("Transfer-Encoding"))
            return (411, Error("Send the body with a Content-Length, chunked bodies are not supported."));

        int length = 0;
        if (headers.TryGetValue("Content-Length", out string? lengthText) && !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out length))
            return (400, Error("Bad Content-Length."));
        if (length > MaxBodyBytes)
            return (413, Error($"Bodies are limited to {MaxBodyBytes} bytes."));

        var body = new byte[length];
        int have = Math.Min(length, buffered.Length);
        Array.Copy(buffered, body, have);
        while (have < length)
        {
            int read = await stream.ReadAsync(body.AsMemory(have), readTimeout.Token).ConfigureAwait(false);
            if (read == 0) return (400, Error("The body ended early."));
            have += read;
        }

        try { return Route(server, method, path, body); }
        catch (Exception ex) { return (500, Error(ex.ToString())); }
    }

    private static (int Status, string Body) Route(Server server, string method, string path, byte[] body)
    {
        if (method == "GET" && path == CliProtocol.CommandsPath)
        {
            List<CliCommandInfo> commands = [];
            RunOnMain(server, () => commands = CliCommands.Describe(), TimeSpan.FromSeconds(30));
            return (200, JsonSerializer.Serialize(commands, CliProtocol.Json));
        }

        if (method == "POST" && path == CliProtocol.RunPath)
        {
            CliRunRequest? run;
            try { run = JsonSerializer.Deserialize<CliRunRequest>(body, CliProtocol.Json); }
            catch (JsonException ex) { return (400, Error($"Bad request body: {ex.Message}")); }
            if (run == null) return (400, Error("Empty request body."));

            return (200, JsonSerializer.Serialize(Execute(run, server), CliProtocol.Json));
        }

        return (404, Error($"No route for {method} {path}."));
    }

    /// <summary> Runs a command on the main thread, waits for it if it returned a task, and captures the logs written while it ran. </summary>
    public static CliRunResponse Execute(CliRunRequest request) => Execute(request, null);

    private static CliRunResponse Execute(CliRunRequest request, Server? server)
    {
        var response = new CliRunResponse();
        var capture = new LogCapture();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, CliRunRequest.MaxTimeoutSeconds));
        var clock = Stopwatch.StartNew();

        BeginActivity();
        try
        {
            object? result = null;
            RunOnMain(server, () => result = InvokeInCliContext(request, capture), timeout);

            if (result is Task task)
            {
                if (!task.Wait(Remaining(timeout, clock)))
                    throw new CliException($"Command '{request.Command}' did not finish within {timeout.TotalSeconds:0} seconds. It may still be running in the editor.");
                result = TaskResult(task);
            }

            JsonNode? json = null;
            RunOnMain(server, () => json = CliCommands.ToJson(result), Remaining(timeout, clock));
            response.Result = json;
            response.Ok = true;
        }
        catch (Exception ex)
        {
            while (ex is AggregateException { InnerExceptions.Count: 1 } aggregate) ex = aggregate.InnerException!;
            response.Error = ex switch
            {
                CliException => ex.Message,
                TaskCanceledException => $"Command '{request.Command}' was cancelled. Entering or leaving play mode and script reloads cancel waits tied to the game session, such as GameTask.NextFrame.",
                _ => ex.ToString(),
            };
        }
        finally
        {
            EndActivity();
        }

        response.Logs = capture.Close();
        return response;
    }

    private static object? InvokeInCliContext(CliRunRequest request, LogCapture capture)
    {
        // A fresh execution context, so the request's log capture and session detachment do not leak into the main thread's.
        object? result = null;
        ExecutionContext.Run(ExecutionContext.Capture()!, _ =>
        {
            var previous = SynchronizationContext.Current;
            s_capture.Value = capture;
            MainThreadContext.LeaveSession();
            if (MainThreadContext.Current != null)
                SynchronizationContext.SetSynchronizationContext(s_context);
            try { result = CliCommands.Invoke(request); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }, null);
        return result;
    }

    /// <summary>
    /// Runs work on the main thread, giving up if it is not picked up in time, for example while a modal is open or the
    /// editor is closing. Work given up on never runs.
    /// </summary>
    private static void RunOnMain(Server? server, Action work, TimeSpan timeout)
    {
        if (s_shutDown) throw new CliException("The editor is closing.");

        var gate = new object();
        bool started = false, abandoned = false;
        var dispatch = Task.Run(() => GameTask.Run(() =>
        {
            lock (gate)
            {
                if (abandoned) return;
                started = true;
            }
            if (server != null && s_server != server)
                throw new CliException("The editor switched projects while this command was waiting.");
            work();
        }));

        if (dispatch.Wait(timeout)) return;

        lock (gate)
        {
            if (!started)
            {
                abandoned = true;
                throw new CliException($"The editor's main thread did not pick this up within {timeout.TotalSeconds:0} seconds. A modal dialog may be open, or the editor is busy.");
            }
        }
        throw new CliException($"Still running on the editor's main thread after {timeout.TotalSeconds:0} seconds.");
    }

    private static TimeSpan Remaining(TimeSpan timeout, Stopwatch clock)
    {
        var left = timeout - clock.Elapsed;
        return left > TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1);
    }

    private static object? TaskResult(Task task)
    {
        var type = task.GetType();
        if (!type.IsGenericType || type.GetGenericArguments()[0].Name == "VoidTaskResult") return null;
        return type.GetProperty("Result")!.GetValue(task);
    }

    private static void CaptureLog(string message, DebugStackTrace? trace, LogSeverity severity) => s_capture.Value?.Add(severity, message);

    private sealed class LogCapture
    {
        private readonly List<CliLogLine> _lines = [];
        private int _dropped;
        private bool _closed;

        public void Add(LogSeverity severity, string message)
        {
            lock (_lines)
            {
                if (_closed) return;
                if (_lines.Count >= MaxLogLines) _dropped++;
                else _lines.Add(new CliLogLine { Severity = severity.ToString(), Message = message });
            }
        }

        public List<CliLogLine> Close()
        {
            lock (_lines)
            {
                _closed = true;
                var lines = new List<CliLogLine>(_lines);
                if (_dropped > 0) lines.Add(new CliLogLine { Severity = "Warning", Message = $"{_dropped} more log lines were dropped." });
                return lines;
            }
        }
    }

    /// <summary> Resumes command continuations on the main thread once per editor frame, outside any gameplay session. </summary>
    private sealed class CliSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => GameTask.Run(() => d(state));

        public override SynchronizationContext CreateCopy() => this;

        public void Pump()
        {
            int pending = _queue.Count;
            if (pending == 0) return;

            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                for (int i = 0; i < pending && _queue.TryDequeue(out var entry); i++)
                {
                    try { entry.Callback(entry.State); }
                    catch (Exception ex) { Debug.LogError($"[CLI] A command continuation threw: {ex}"); }
                }
            }
            finally { SetSynchronizationContext(previous); }
        }
    }

    private static bool IsLoopbackHost(string host, int port)
        => host == $"127.0.0.1:{port}" || host == $"localhost:{port}" || host == "127.0.0.1" || host == "localhost";

    private static bool TokenMatches(string token, string expected)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected));

    private static string Error(string message)
        => JsonSerializer.Serialize(new CliRunResponse { Ok = false, Error = message }, CliProtocol.Json);

    private static async Task<(string Method, string Path, Dictionary<string, string> Headers, byte[] Buffered)?> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int headerEnd = -1;

        while (headerEnd < 0)
        {
            int read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (read == 0) return null;
            buffer.Write(chunk, 0, read);
            headerEnd = IndexOfHeaderEnd(buffer.GetBuffer(), (int)buffer.Length);
            if (headerEnd < 0 && buffer.Length > MaxHeaderBytes) return null;
        }

        string head = Encoding.ASCII.GetString(buffer.GetBuffer(), 0, headerEnd);
        string[] lines = head.Split("\r\n");
        string[] requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon > 0) headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        int bodyStart = headerEnd + 4;
        byte[] buffered = buffer.GetBuffer().AsSpan(bodyStart, (int)buffer.Length - bodyStart).ToArray();

        string path = requestLine[1];
        int query = path.IndexOf('?');
        if (query >= 0) path = path[..query];
        return (requestLine[0].ToUpperInvariant(), path, headers, buffered);
    }

    private static int IndexOfHeaderEnd(byte[] data, int length)
    {
        for (int i = 0; i + 3 < length; i++)
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n') return i;
        return -1;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string body, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        string reason = status switch
        {
            200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found",
            411 => "Length Required", 413 => "Content Too Large", _ => "Internal Server Error",
        };
        string head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
    }
}
