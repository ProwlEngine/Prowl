// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Prowl.Cli;

public static class Program
{
    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitCancelled = 130;

    private static readonly string[] s_verbs = ["status", "command", "eval", "eval_file"];

    private const string Help = """
        prowl: drive a running Prowl editor from the terminal.

        Usage:
          prowl status                     Open project, scene and play state
          prowl command                    List the commands the editor exposes
          prowl command <name> [args]      Run a command
          prowl eval <code>                Run C# in the editor and print the result (use - to read stdin)
          prowl eval_file <path>           Run the C# in a file

        Command arguments are positional, --name value or --name=value. A bool flag needs no value.
        Everything after -- is positional.

        Options (anywhere before --):
          --project <path>   Project folder. Defaults to the nearest parent folder with a running editor
          --json             Print the raw JSON response
          --timeout <secs>   How long a command may run (default 120)

        Exit codes: 0 success, 1 error, 130 cancelled.
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        Console.CancelKeyPress += (_, _) => Environment.Exit(ExitCancelled);

        try
        {
            return await RunAsync(args);
        }
        catch (CliUsageException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitError;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        string? projectPath = null;
        bool json = false;
        int timeout = 120;
        var rest = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--")
            {
                rest.AddRange(args[i..]);
                break;
            }

            string name = arg, value = "";
            int equals = arg.IndexOf('=');
            bool inline = arg.StartsWith("--", StringComparison.Ordinal) && equals > 0;
            if (inline) (name, value) = (arg[..equals], arg[(equals + 1)..]);

            switch (name)
            {
                case "--project": projectPath = inline ? value : Next(args, ref i, name); break;
                case "--json": json = true; break;
                case "--timeout":
                    if (!int.TryParse(inline ? value : Next(args, ref i, name), out timeout) || timeout < 1 || timeout > CliRunRequest.MaxTimeoutSeconds)
                        throw new CliUsageException($"--timeout expects a whole number of seconds from 1 to {CliRunRequest.MaxTimeoutSeconds}.");
                    break;
                default: rest.Add(arg); break;
            }
        }

        if (rest.Count == 0 || rest[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine(Help);
            return ExitOk;
        }

        string verb = rest[0];
        var verbArgs = rest.Skip(1).ToList();
        if (!s_verbs.Contains(verb))
            throw new CliUsageException($"Unknown verb '{verb}'. Run 'prowl help'.");

        CliRunRequest? request = null;
        switch (verb)
        {
            case "status":
                request = new CliRunRequest { Command = "status", Argv = verbArgs };
                break;

            case "command" when verbArgs.Count > 0:
                request = new CliRunRequest { Command = verbArgs[0], Argv = verbArgs.Skip(1).ToList() };
                break;

            case "eval":
                if (verbArgs.Count != 1) throw new CliUsageException("eval takes one argument, the code. Quote it, or pass - to read it from stdin.");
                string code = verbArgs[0] == "-" ? await Console.In.ReadToEndAsync() : verbArgs[0];
                request = new CliRunRequest { Command = "eval", Positional = [code] };
                break;

            case "eval_file":
                if (verbArgs.Count != 1) throw new CliUsageException("eval_file takes one argument, the file path.");
                if (!File.Exists(verbArgs[0])) throw new CliUsageException($"File not found: {verbArgs[0]}");
                request = new CliRunRequest { Command = "eval", Positional = [await File.ReadAllTextAsync(verbArgs[0])] };
                break;
        }

        using var client = Connect(projectPath, timeout);
        if (request == null) return await ListAsync(client, json);

        request.TimeoutSeconds = timeout;
        return await RunCommandAsync(client, request, json);
    }

    private static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length) throw new CliUsageException($"{option} needs a value.");
        return args[++i];
    }

    private static HttpClient Connect(string? projectPath, int timeout)
    {
        var lockFile = FindEditor(projectPath);
        var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{lockFile.Port}"),
            Timeout = TimeSpan.FromSeconds(timeout + 30),
        };
        client.DefaultRequestHeaders.Add(CliProtocol.TokenHeader, lockFile.Token);
        return client;
    }

    private static CliLockFile FindEditor(string? projectPath)
    {
        string? stale = null;
        IEnumerable<DirectoryInfo> candidates = projectPath != null
            ? [new DirectoryInfo(Path.GetFullPath(projectPath))]
            : Ancestors(new DirectoryInfo(Directory.GetCurrentDirectory()));

        foreach (var dir in candidates)
        {
            string path = Path.Combine(dir.FullName, "Library", CliProtocol.LockFileName);
            if (!File.Exists(path)) continue;

            CliLockFile? lockFile;
            try { lockFile = JsonSerializer.Deserialize<CliLockFile>(File.ReadAllText(path), CliProtocol.Json); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { lockFile = null; }

            if (lockFile != null && IsLockWriterAlive(lockFile.ProcessId, File.GetLastWriteTimeUtc(path)))
                return lockFile;
            stale ??= path;
        }

        if (stale != null)
            throw new CliUsageException($"The editor that wrote {stale} is no longer running. Open the project in the Prowl editor.");
        if (projectPath != null)
            throw new CliUsageException($"No running Prowl editor for {Path.GetFullPath(projectPath)}. Open the project in the Prowl editor.");
        throw new CliUsageException("No running Prowl editor found from this folder or its parents. Open the project in the Prowl editor, or pass --project <path>.");
    }

    private static IEnumerable<DirectoryInfo> Ancestors(DirectoryInfo? dir)
    {
        for (; dir != null; dir = dir.Parent) yield return dir;
    }

    // A process started after the lock file was written cannot have written it, which catches a reused process id.
    private static bool IsLockWriterAlive(int pid, DateTime writtenUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return false;
            try { return process.StartTime.ToUniversalTime() <= writtenUtc.AddSeconds(5); }
            catch (Win32Exception) { return true; }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
    }

    private static async Task<int> ListAsync(HttpClient client, bool json)
    {
        var response = await SendAsync(() => client.GetAsync(CliProtocol.CommandsPath));
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return PrintFailure(body, json);

        if (json)
        {
            Console.WriteLine(body);
            return ExitOk;
        }

        var commands = TryDeserialize<List<CliCommandInfo>>(body);
        if (commands == null) return PrintFailure(body, json);

        int width = commands.Count == 0 ? 0 : commands.Max(c => c.Name.Length);
        foreach (var command in commands)
        {
            Console.WriteLine($"{command.Name.PadRight(width)}  {command.Description}");
            foreach (var arg in command.Args)
            {
                string label = arg.Required ? $"<{arg.Name}>" : $"[--{arg.Name}]";
                string extra = arg.Required ? arg.Type : $"{arg.Type}, default {arg.Default ?? "null"}";
                string description = arg.Description.Length > 0 ? $"{arg.Description} " : "";
                Console.WriteLine($"{"".PadRight(width)}    {label}  {description}({extra})");
            }
        }
        return ExitOk;
    }

    private static async Task<int> RunCommandAsync(HttpClient client, CliRunRequest request, bool json)
    {
        var content = new StringContent(JsonSerializer.Serialize(request, CliProtocol.Json), Encoding.UTF8, "application/json");
        var response = await SendAsync(() => client.PostAsync(CliProtocol.RunPath, content));
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return PrintFailure(body, json);

        var result = TryDeserialize<CliRunResponse>(body);
        if (result == null) return PrintFailure(body, json);

        if (json)
        {
            Console.WriteLine(body);
            return result.Ok ? ExitOk : ExitError;
        }

        foreach (var log in result.Logs)
            Console.Error.WriteLine($"[{log.Severity}] {log.Message}");

        if (!result.Ok)
        {
            Console.Error.WriteLine($"error: {result.Error}");
            return ExitError;
        }

        if (result.Result != null)
        {
            bool isString = result.Result.GetValueKind() == JsonValueKind.String;
            Console.WriteLine(isString ? result.Result.GetValue<string>() : result.Result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        return ExitOk;
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try { return await send(); }
        catch (HttpRequestException ex) { throw new CliUsageException($"Could not reach the editor: {ex.Message}"); }
        catch (TaskCanceledException) { throw new CliUsageException("The editor did not answer in time."); }
    }

    private static T? TryDeserialize<T>(string body) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(body, CliProtocol.Json); }
        catch (JsonException) { return null; }
    }

    private static int PrintFailure(string body, bool json)
    {
        if (json)
        {
            Console.WriteLine(body);
            return ExitError;
        }

        string? message = TryDeserialize<CliRunResponse>(body)?.Error;
        Console.Error.WriteLine($"error: {message ?? body}");
        return ExitError;
    }
}

internal sealed class CliUsageException(string message) : Exception(message);
