// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

using Prowl.Editor.Core;
using Prowl.Editor.Projects.Scripting;
using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp.Transports.Kcp;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Multiplayer play end to end: this process hosts, a real play instance (a second process, the same
/// editor build, headless) joins over UDP, and the two run generated [Sync] and RPC code between them.
/// </summary>
[Trait("Category", "Build")]
public class MultiplayerPlayTests : EditorTestHarness
{
    // A partial class with its network members in both parts.
    private const string BeaconState = """
        using Prowl.Runtime;

        public partial class Beacon : Component
        {
            [Sync] public int Level { get; set; }
        }
        """;

    private const string BeaconCalls = """
        using Prowl.Runtime;

        public partial class Beacon
        {
            [Rpc.Broadcast]
            public void Ping(string text) => Debug.Log($"beacon {text} level {Level} from {Rpc.Caller?.Name}");
        }
        """;

    [Fact]
    public void AClientInstanceJoinsAndRunsGeneratedNetworking()
    {
        WriteScript("BeaconState.cs", BeaconState);
        WriteScript("BeaconCalls.cs", BeaconCalls);
        ScriptCompiler.CompileResult compiled = ScriptCompiler.CompileAll(Project);
        Assert.True(compiled.Success, compiled.Errors);
        Type beaconType = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath)).GetType("Beacon")!;

        Scene scene = new();
        Scene.Load(scene);
        Scene.ProcessPendingLoad();

        Networking.Host(new KcpTransport(new KcpTransportOptions { Port = 0 }), "Host");
        int port = ((KcpTransport)Networking.Session!.Transport).LocalPort;

        ConcurrentQueue<string> logs = new();
        ConcurrentQueue<string> output = new();
        using Process instance = Process.Start(MultiplayerPlay.InstanceStartInfo(Project.RootPath, $"127.0.0.1:{port}", "Bot", 1, headless: true))!;
        instance.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            output.Enqueue(e.Data);
            if (MultiplayerPlay.ParseInstanceLog(e.Data) is { } log)
                logs.Enqueue(log.Message);
        };
        instance.ErrorDataReceived += (_, e) => { if (e.Data != null) output.Enqueue("ERR " + e.Data); };
        instance.BeginOutputReadLine();
        instance.BeginErrorReadLine();

        GameObject? beaconObject = null;
        try
        {
            Pump(() => Networking.Session!.Peers.Count == 2, "the instance to join", output, instance);

            beaconObject = new GameObject("Beacon");
            Component beacon = beaconObject.AddComponent(beaconType)!;
            scene.Add(beaconObject);
            beaconType.GetProperty("Level")!.SetValue(beacon, 7);
            beaconObject.NetworkSpawn();

            beaconType.GetMethod("Ping")!.Invoke(beacon, ["hello"]);
            Pump(() => logs.Contains("beacon hello level 7 from Host"), "the RPC to run on the instance", output, instance);

            // Ending the session ends the instance.
            Networking.Shutdown();
            Assert.True(instance.WaitForExit(15_000), "The instance kept running after the host shut down.\n" + string.Join("\n", output));
        }
        finally
        {
            Networking.Shutdown();
            if (!instance.HasExited)
                instance.Kill();
            beaconObject?.Dispose();
        }
    }

    [Fact]
    public void InstanceLogLinesRoundTrip()
    {
        Assert.Null(MultiplayerPlay.ParseInstanceLog("plain console output"));
        Assert.Null(MultiplayerPlay.ParseInstanceLog(null));
    }

    private static void Pump(Func<bool> condition, string what, ConcurrentQueue<string> output, Process instance)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(60) || instance.HasExited)
                Assert.Fail($"Timed out waiting for {what} (instance {(instance.HasExited ? $"exited with {instance.ExitCode}" : "still running")}).\n{string.Join("\n", output)}");

            Networking.Receive();
            Networking.Flush();
            Scene.ProcessPendingLoad();
            EngineObject.ProcessDestroyed();
            Thread.Sleep(5);
        }
    }
}
