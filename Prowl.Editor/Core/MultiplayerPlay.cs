// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Warp;
using Prowl.Warp.Transports.Kcp;
using Prowl.Warp.Transports.Batching;

using Debug = Prowl.Runtime.Debug;

namespace Prowl.Editor.Core;

/// <summary>
/// Multiplayer testing from the editor. While the game hosts a session in play mode, the play pill offers to
/// launch client instances, each its own process and window, which join it and get its scene. Leaving play
/// mode closes them.
/// </summary>
/// <remarks>
/// <para>
/// An instance is this same executable run with <c>--play-instance</c>: no editor UI, just the game. It
/// loads the scripts the editor compiled and reads assets straight from the project's import cache,
/// read-only (<see cref="EditorAssetBackend.InitializeReadOnly"/>), so it starts in moments and never
/// touches the project. What the editor hasn't imported, an instance can't load.
/// </para>
/// <para>Each instance's log lines show up in the editor console, prefixed with its name.</para>
/// </remarks>
public static class MultiplayerPlay
{
    // How an instance marks a log line for the editor to pick out of its output: marker, severity, '|', text.
    private const string LogMarker = "\u001f#prowl-log:";

    private static readonly List<Process> s_instances = new();
    private static int s_launched;

    /// <summary>True while another client can be launched: playing, and hosting.</summary>
    public static bool CanLaunchClient => Application.IsPlaying && Networking.IsHost && Networking.Session?.Transport.LocalPort > 0;

    /// <summary>Launches one more client instance into the session the editor is hosting.</summary>
    public static void LaunchClient()
    {
        if (!CanLaunchClient || Project.Current is not { } project)
            return;

        int port = Networking.Session!.Transport.LocalPort;
        int index = ++s_launched;
        string name = $"Client {index}";

        ProcessStartInfo start = InstanceStartInfo(project.RootPath, $"127.0.0.1:{port}", name, index, headless: false);

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Multiplayer] Couldn't launch {name}: {e.Message}");
            return;
        }
        if (process == null)
            return;

        process.OutputDataReceived += (_, e) => ForwardLog(name, e.Data);
        process.ErrorDataReceived += (_, e) => ForwardLog(name, e.Data, LogSeverity.Error);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        s_instances.Add(process);
        Debug.Log($"[Multiplayer] Launched {name}.");
    }

    /// <summary>
    /// Play mode ended. The session's shutdown already told the instances, and they close themselves; any still
    /// running a few seconds later are closed for them.
    /// </summary>
    internal static void OnPlayStopped()
    {
        if (s_instances.Count == 0)
            return;

        Process[] instances = [.. s_instances];
        s_instances.Clear();
        s_launched = 0;

        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            foreach (Process process in instances)
                Close(process);
        });
    }

    /// <summary>The editor is closing: close every instance now.</summary>
    internal static void CloseAll()
    {
        foreach (Process process in s_instances)
            Close(process);
        s_instances.Clear();
    }

    /// <summary>How to start a play instance of the editor build this code belongs to, with its output captured.</summary>
    internal static ProcessStartInfo InstanceStartInfo(string projectRoot, string address, string name, int index, bool headless)
    {
        // The app host beside the editor's assembly if there is one, else the assembly through dotnet.
        string assembly = typeof(MultiplayerPlay).Assembly.Location;
        string appHost = Path.ChangeExtension(assembly, OperatingSystem.IsWindows() ? ".exe" : null);
        ProcessStartInfo start = File.Exists(appHost) ? new(appHost) : new("dotnet") { ArgumentList = { assembly } };

        start.WorkingDirectory = Path.GetDirectoryName(assembly)!;
        foreach (string argument in new[] { "--play-instance", projectRoot, "--join", address, "--name", name, "--instance", index.ToString() })
            start.ArgumentList.Add(argument);
        if (headless)
            start.ArgumentList.Add("--headless");

        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;
        return start;
    }

    /// <summary>The severity and text of a log line an instance printed, or null for any other output.</summary>
    internal static (LogSeverity Severity, string Message)? ParseInstanceLog(string? line)
    {
        if (line == null || !line.StartsWith(LogMarker, StringComparison.Ordinal))
            return null;

        int bar = line.IndexOf('|', LogMarker.Length);
        if (bar < 0 || !int.TryParse(line.AsSpan(LogMarker.Length, bar - LogMarker.Length), out int severity))
            return null;

        return ((LogSeverity)severity, line[(bar + 1)..].Replace("\\n", "\n"));
    }

    private static void ForwardLog(string instance, string? line, LogSeverity fallback = LogSeverity.Normal)
    {
        if (string.IsNullOrEmpty(line))
            return;

        // Only the marked lines: the plain console copy of each log line would show it twice.
        if (ParseInstanceLog(line) is { } log)
            EditorApplication.RunOnMainThread(() => Debug.Log($"[{instance}] {log.Message}", log.Severity));
        else if (fallback != LogSeverity.Normal)
        {
            EditorApplication.RunOnMainThread(() => Debug.Log($"[{instance}] {line}", fallback));
        }
    }

    private static void Close(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch { }
        finally
        {
            process.Dispose();
        }
    }

    // --- The instance side ------------------------------------------------------------------------

    /// <summary>
    /// The whole of an instance's life, for <c>--play-instance</c>: opens the project read-only, joins the
    /// host and runs the game until the session ends or the window closes.
    /// </summary>
    /// <param name="headless">Run without a window or rendering, for automated tests and bots.</param>
    internal static void RunInstance(string projectPath, string address, string name, int index, bool headless = false)
    {
        // Every log line also goes out marked, for the editor that launched this to show.
        Debug.OnLog += (message, _, severity) => Console.Out.WriteLine($"{LogMarker}{(int)severity}|{message.Replace("\n", "\\n")}");

        EditorRegistries.Initialize();
        Project project = Project.Open(projectPath);
        project.SetActive(addToRecent: false);

        // The editor that launched this compiled them before entering play mode.
        ScriptAssemblyManager.LoadAssemblies(project);
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        EditorAssetBackend assets = new(project);
        PlayInstance game = new(assets, address, name, index);
        try
        {
            if (headless)
                game.RunHeadless(new HeadlessRunOptions { TargetFrameRate = 60 });
            else
                game.Run($"{project.Name} - {name}", 1280, 720);
        }
        finally
        {
            assets.Dispose();
        }
    }

    /// <summary>The game loop of a play instance: a client with no editor around it.</summary>
    private sealed class PlayInstance(EditorAssetBackend assets, string address, string name, int index) : Game
    {
        public override void Initialize()
        {
            Application.IsPlaying = true;
            Application.IsEditor = false;

            // Cascaded, so a few instances don't open exactly on top of each other.
            if (!Application.IsHeadless)
                Window.Position = new Silk.NET.Maths.Vector2D<int>(60 + index * 40, 60 + index * 40);

            assets.InitializeReadOnly();

            NetworkConfiguration configuration = Networking.Configuration;
            Transport transport = new KcpTransport(configuration.CreateKcpOptions());
            if (configuration.BatchMessages) transport = new BatchingTransport(transport, configuration.Mtu);
            NetSession session = Networking.Join(transport, address, name);
            session.Ended += (reason, message) =>
            {
                Debug.Log($"[Multiplayer] {name} left the session ({reason}).");
                Application.Quit();
            };
        }
    }
}
