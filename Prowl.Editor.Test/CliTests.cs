// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prowl.Cli;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Tasks;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

public class CliTests : EditorTestHarness
{
    public enum Speed { Fast, Slow }

    [CliCommand("test_add", "Adds two numbers")]
    public static int Add(int a, [CliArg("b", "Second number")] int bee = 10) => a + bee;

    [CliCommand("test_speed")]
    public static string SpeedOf(Speed speed, bool loud = false) => loud ? speed.ToString().ToUpperInvariant() : speed.ToString();

    [CliCommand("test_throw")]
    public static void Throw() => throw new InvalidOperationException("boom");

    [CliCommand("test_echo")]
    public static string Echo(string text) => text;

    public CliTests()
    {
        EditorSettings.Instance = new EditorSettings();
        CliCommands.Clear();
        foreach (var type in new[] { typeof(CliCommands), typeof(CliEditorCommands), typeof(CliTests) })
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                CliCommands.Scan(method);
    }

    public override void Dispose()
    {
        CliServer.Stop();
        CliCommands.Clear();
        EditorSettings.Instance = null!;
        base.Dispose();
    }

    private static CliRunResponse Run(string command, Dictionary<string, string>? named = null, params string[] positional)
        => CliServer.Execute(new CliRunRequest { Command = command, Args = named ?? [], Positional = [.. positional] });

    private static CliRunResponse Eval(string code) => Run("eval", null, code);

    private static CliRunResponse Argv(string command, params string[] argv)
        => CliServer.Execute(new CliRunRequest { Command = command, Argv = [.. argv] });

    [Fact]
    public void ArgvBindsUsingParameterTypes()
    {
        Assert.Equal("SLOW", Argv("test_speed", "--loud", "slow").Result!.GetValue<string>());
        Assert.Equal("Fast", Argv("test_speed", "fast", "--loud", "false").Result!.GetValue<string>());
        Assert.Equal(7, Argv("test_add", "--b=4", "3").Result!.GetValue<long>());
        Assert.Equal("--x", Argv("test_echo", "--", "--x").Result!.GetValue<string>());
        Assert.Contains("needs a value", Argv("test_add", "1", "--b").Error);
        Assert.Contains("more than once", Argv("test_add", "--a", "1", "--A", "2").Error);
        Assert.Contains("more than once", CliServer.Execute(new CliRunRequest { Command = "test_add", Args = new() { ["a"] = "1" }, Argv = ["--a", "2"] }).Error);
    }

    [Fact]
    public void UndefinedEnumValuesAreRejected()
    {
        Assert.Contains("expects Fast|Slow", Run("test_speed", null, "5").Error);
    }

    [Fact]
    public void EvalCanBeTurnedOffInPreferences()
    {
        EditorSettings.Instance.AllowCliEval = false;
        Assert.Contains("turned off", Eval("1").Error);
    }

    [Fact]
    public void ACommandMakesTheCliActive()
    {
        Eval("1");
        Assert.True(CliServer.IsActive);
    }

    [Fact]
    public void EvalTellsExpressionsFromStatementsBySyntax()
    {
        Assert.Equal(3, Eval("\"a;b\".Length").Result!.GetValue<long>());
        Assert.Equal(6, Eval("new[] { 1, 2, 3 }.Select(x => { return x; }).Sum()").Result!.GetValue<long>());
        Assert.Equal(3, Eval("1 + 2;").Result!.GetValue<long>());
        Assert.Equal("ab", Eval("new System.Text.StringBuilder(\"a\").Append('b')").Result!.GetValue<string>());
        Assert.Equal(4, Eval("await Task.FromResult(4)").Result!.GetValue<long>());
    }

    [Fact]
    public void LargeAndUnusualResultsSerialize()
    {
        Assert.Equal(ulong.MaxValue, Eval("ulong.MaxValue").Result!.GetValue<ulong>());

        var items = Eval("Enumerable.Range(0, 5000)").Result!.AsArray();
        Assert.Equal(CliCommands.MaxResultItems + 1, items.Count);
        Assert.Contains("truncated", items[^1]!.GetValue<string>());

        var lone = Eval("\"a\\uD800b\"");
        Assert.True(lone.Ok, lone.Error);
        Assert.Equal("a�b", lone.Result!.GetValue<string>());
    }

    [Fact]
    public void ConcurrentCommandsOnlyCaptureTheirOwnLogs()
    {
        static string Code(string tag) => $"for (int i = 0; i < 5; i++) {{ Debug.Log(\"{tag}\" + i); await Task.Delay(10); }} return 1;";
        var a = Task.Run(() => Eval(Code("A")));
        var b = Task.Run(() => Eval(Code("B")));

        foreach (var (task, tag) in new[] { (a, "A"), (b, "B") })
        {
            var response = task.Result;
            Assert.True(response.Ok, response.Error);
            Assert.Equal(5, response.Logs.Count);
            Assert.All(response.Logs, l => Assert.StartsWith(tag, l.Message));
        }
    }

    [Fact]
    public void EvalAwaitingAcrossSessionRestartsStillFinishes()
    {
        var previous = SynchronizationContext.Current;
        MainThreadContext.Install();
        try
        {
            var run = Task.Run(() => CliServer.Execute(new CliRunRequest { Command = "eval", Positional = ["await Task.Delay(50); await Task.Yield(); return 7;"], TimeoutSeconds = 30 }));

            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!run.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(40))
            {
                MainThreadContext.Current!.Pump();
                CliServer.PumpContinuations();
                MainThreadContext.Restart();
                Thread.Sleep(5);
            }

            Assert.True(run.IsCompleted);
            Assert.True(run.Result.Ok, run.Result.Error);
            Assert.Equal(7, run.Result.Result!.GetValue<long>());
        }
        finally
        {
            MainThreadContext.Uninstall();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task UnauthenticatedRequestsAreRefusedBeforeTheBodyIsRead()
    {
        CliServer.Start(Project.RootPath, Project.LibraryPath);

        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, CliServer.Port);
        var stream = tcp.GetStream();
        byte[] head = System.Text.Encoding.ASCII.GetBytes($"POST /run HTTP/1.1\r\nHost: 127.0.0.1:{CliServer.Port}\r\nContent-Length: 10000000\r\n\r\n");
        await stream.WriteAsync(head);

        using var reader = new StreamReader(stream);
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.StartsWith("HTTP/1.1 401", line);
    }

    [Fact]
    public async Task RequestsForAnotherHostAreRefused()
    {
        CliServer.Start(Project.RootPath, Project.LibraryPath);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{CliServer.Port}") };
        client.DefaultRequestHeaders.Add(CliProtocol.TokenHeader, CliServer.Token);
        client.DefaultRequestHeaders.Host = "evil.example";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(CliProtocol.CommandsPath)).StatusCode);
    }

    [Fact]
    public void ArgumentsBindPositionallyByNameAndFromDefaults()
    {
        Assert.Equal(11, Run("test_add", null, "1").Result!.GetValue<long>());
        Assert.Equal(3, Run("test_add", null, "1", "2").Result!.GetValue<long>());
        Assert.Equal(7, Run("test_add", new() { ["b"] = "4", ["A"] = "3" }).Result!.GetValue<long>());
    }

    [Fact]
    public void EnumsIgnoreCaseAndAValuelessFlagIsTrue()
    {
        Assert.Equal("SLOW", Run("test_speed", new() { ["loud"] = "" }, "slow").Result!.GetValue<string>());
    }

    [Fact]
    public void BadArgumentsFailWithAPlainMessage()
    {
        Assert.Contains("needs argument 'a'", Run("test_add").Error);
        Assert.Contains("no argument 'c'", Run("test_add", new() { ["c"] = "1" }, "1").Error);
        Assert.Contains("expects int", Run("test_add", null, "one").Error);
        Assert.Contains("more argument", Run("test_add", null, "1", "2", "3").Error);
        Assert.Contains("Unknown command", Run("nope").Error);
    }

    [Fact]
    public void AThrowingCommandReportsTheException()
    {
        var response = Run("test_throw");
        Assert.False(response.Ok);
        Assert.Contains("boom", response.Error);
        Assert.Contains(nameof(Throw), response.Error);
    }

    [Fact]
    public void EvalReturnsAnExpressionsValue()
    {
        Assert.Equal(3, Eval("1 + 2").Result!.GetValue<long>());
        Assert.Equal("GameObject", Eval("typeof(GameObject).Name").Result!.GetValue<string>());
    }

    [Fact]
    public void EvalRunsStatementsUsingsAndAwait()
    {
        Assert.Equal(6, Eval("var x = new List<int> { 1, 2, 3 }; return x.Sum();").Result!.GetValue<long>());
        Assert.Equal("ab", Eval("using System.Text;\nreturn new StringBuilder(\"a\").Append('b').ToString();").Result!.GetValue<string>());
        Assert.Equal(5, Eval("await Task.Yield(); return 5;").Result!.GetValue<long>());
    }

    [Fact]
    public void EvalOfAVoidCallReturnsNothingAndCapturesItsLog()
    {
        var response = Eval("Debug.Log(\"from eval\")");
        Assert.True(response.Ok, response.Error);
        Assert.Null(response.Result);
        Assert.Contains(response.Logs, l => l.Message == "from eval" && l.Severity == "Normal");
    }

    [Fact]
    public void EvalCompileErrorsPointAtTheSnippetLine()
    {
        var response = Eval("var a = 1;\nreturn missingThing;");
        Assert.False(response.Ok);
        Assert.Contains("(2,", response.Error);
        Assert.Contains("CS0103", response.Error);
    }

    [Fact]
    public void ResultsKeepTheShapeOfAnonymousObjectsAndCollections()
    {
        var result = Eval("new { name = \"a\", items = new[] { 1, 2 }, map = new Dictionary<string, float> { [\"k\"] = 0.5f } }").Result!;
        Assert.Equal("a", result["name"]!.GetValue<string>());
        Assert.Equal(2, result["items"]![1]!.GetValue<long>());
        Assert.Equal(0.5f, result["map"]!["k"]!.GetValue<float>());
    }

    [Fact]
    public async Task ServerNeedsTheLockFileTokenAndRunsCommands()
    {
        CliServer.Start(Project.RootPath, Project.LibraryPath);
        string lockPath = Path.Combine(Project.LibraryPath, CliProtocol.LockFileName);
        var lockFile = JsonSerializer.Deserialize<CliLockFile>(File.ReadAllText(lockPath), CliProtocol.Json)!;
        Assert.Equal(CliServer.Port, lockFile.Port);
        Assert.Equal(Environment.ProcessId, lockFile.ProcessId);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{lockFile.Port}") };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(CliProtocol.CommandsPath)).StatusCode);

        client.DefaultRequestHeaders.Add(CliProtocol.TokenHeader, lockFile.Token);
        var commands = await client.GetFromJsonAsync<List<CliCommandInfo>>(CliProtocol.CommandsPath, CliProtocol.Json);
        var add = commands!.Single(c => c.Name == "test_add");
        Assert.Equal(["a", "b"], add.Args.Select(a => a.Name));
        Assert.False(add.Args[1].Required);

        var chunked = await client.PostAsJsonAsync(CliProtocol.RunPath, new CliRunRequest { Command = "status" }, CliProtocol.Json);
        Assert.Equal(HttpStatusCode.LengthRequired, chunked.StatusCode);

        string json = JsonSerializer.Serialize(new CliRunRequest { Command = "eval", Positional = ["40 + 2"] }, CliProtocol.Json);
        var post = await client.PostAsync(CliProtocol.RunPath, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        var response = await post.Content.ReadFromJsonAsync<CliRunResponse>(CliProtocol.Json);
        Assert.True(response!.Ok, response.Error);
        Assert.Equal(42, response.Result!.GetValue<long>());

        CliServer.Stop();
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task ServerStartedOnTheMainThreadKeepsAnsweringAfterTheSessionRestarts()
    {
        var previous = SynchronizationContext.Current;
        MainThreadContext.Install();
        try
        {
            CliServer.Start(Project.RootPath, Project.LibraryPath);
            MainThreadContext.Restart();
            SynchronizationContext.SetSynchronizationContext(previous);

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{CliServer.Port}"), Timeout = TimeSpan.FromSeconds(5) };
            for (int i = 0; i < 3; i++)
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(CliProtocol.CommandsPath)).StatusCode);
        }
        finally
        {
            CliServer.Stop();
            MainThreadContext.Uninstall();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}

public sealed class CliProbe : MonoBehaviour
{
    public int Count;
    public float Speed = 1;
    public string Label = "";
    public Float3 Offset;
    public List<int> Numbers = [1, 2, 3];
    public GameObject? Target;
    public CliProbeSettings Settings = new();
    public List<GameObject?> Targets = [];
    public CliProbeMode Mode;
    public List<CliProbeSettings> Shapes = [];

    [System.NonSerialized] public int Validated;
    public override void OnValidate() => Validated++;
}

public sealed class CliProbeExtra : MonoBehaviour
{
    public int Level;
}

public class CliProbeSettings
{
    public float Radius = 0.5f;
}

public sealed class CliProbeBox : CliProbeSettings
{
    public float Depth;
}

public enum CliProbeMode { Slow, Fast }

/// <summary> The built in editor commands, run through the same path the CLI uses, against a live scene. </summary>
public class CliEditorCommandTests : EditorTestHarness
{
    private readonly Scene _scene;

    public CliEditorCommandTests()
    {
        EditorSettings.Instance = new EditorSettings();
        CliCommands.Clear();
        foreach (var type in new[] { typeof(CliCommands), typeof(CliEditorCommands) })
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                CliCommands.Scan(method);

        Undo.Clear();
        _scene = new Scene();
        Scene.Load(_scene);
        Scene.ProcessPendingLoad();
    }

    public override void Dispose()
    {
        Application.IsPlaying = false;
        CliCommands.Clear();
        EditorSettings.Instance = null!;
        base.Dispose();
    }

    private static JsonNode? Ok(string command, params string[] argv)
    {
        var response = CliServer.Execute(new CliRunRequest { Command = command, Argv = [.. argv] });
        Assert.True(response.Ok, $"{command} failed: {response.Error}");
        return response.Result == null ? null : JsonNode.Parse(response.Result.ToJsonString());
    }

    private static string Fails(string command, params string[] argv)
    {
        var response = CliServer.Execute(new CliRunRequest { Command = command, Argv = [.. argv] });
        Assert.False(response.Ok, $"{command} should have failed");
        return response.Error!;
    }

    [Fact]
    public void GoCreatesUnderAParentAtALocalPositionAndUndoRemovesIt()
    {
        Ok("go", "create", "--name", "Root");
        var child = Ok("go", "create", "--name", "Gun", "--parent", "/Root", "--position", "[1, 2, 3]", "--components", "CliProbe")!;

        Assert.Equal("/Root/Gun", child["path"]!.GetValue<string>());
        var gun = _scene.AllObjects.Single(g => g.Name == "Gun");
        Assert.Equal(new Float3(1, 2, 3), gun.Transform.LocalPosition);
        Assert.NotNull(gun.GetComponent<CliProbe>());

        Ok("undo");
        Assert.DoesNotContain(_scene.AllObjects, g => g.Name == "Gun");
        Ok("undo", "--redo");
        Assert.NotNull(_scene.AllObjects.Single(g => g.Name == "Gun").GetComponent<CliProbe>());
    }

    [Fact]
    public void RefsResolvePathsIndexedSiblingsComponentsAndIds()
    {
        Ok("go", "create", "--name", "A");
        Ok("go", "create", "--name", "A");
        var probe = Ok("go", "create", "--name", "P", "--parent", "/A[1]", "--components", "CliProbe")!;

        Assert.Contains("A[0] to A[1]", Fails("get", "/A"));
        Assert.Equal("/A[1]/P", Ok("get", "/A[1]/P")!["path"]!.GetValue<string>());
        Assert.Equal("CliProbe", Ok("get", "/A[1]/P:CliProbe")!["type"]!.GetValue<string>());
        Assert.Equal("/A[1]/P", Ok("get", probe["id"]!.GetValue<string>())!["path"]!.GetValue<string>());
        Assert.Contains("There is: A", Fails("get", "/Nope"));
        Assert.Contains("has no Camera", Fails("get", "/A[1]/P:Camera"));
    }

    [Fact]
    public void SetWritesFieldsLikeTheInspectorAndUndoRestoresThem()
    {
        Ok("go", "create", "--name", "Other");
        Ok("go", "create", "--name", "P", "--components", "CliProbe");
        var probe = _scene.AllObjects.Single(g => g.Name == "P").GetComponent<CliProbe>()!;
        int validatedBefore = probe.Validated;

        Ok("set", "/P:CliProbe", "--values", """{"Count": 5, "Speed": 2, "Label": "hello", "Offset": [1, 2, 3], "Numbers[1]": 9, "Settings.Radius": 2.5, "Target": "/Other"}""");

        Assert.Equal(5, probe.Count);
        Assert.Equal(2f, probe.Speed);
        Assert.Equal("hello", probe.Label);
        Assert.Equal(new Float3(1, 2, 3), probe.Offset);
        Assert.Equal([1, 9, 3], probe.Numbers);
        Assert.Equal(2.5f, probe.Settings.Radius);
        Assert.Equal("Other", probe.Target!.Name);
        Assert.True(probe.Validated > validatedBefore);

        Assert.Equal("/Other", Ok("get", "/P:CliProbe", "Target")!["ref"]!.GetValue<string>());
        Assert.Equal(9, Ok("get", "/P:CliProbe", "Numbers[1]")!.GetValue<long>());

        Ok("undo");
        Assert.Equal(0, probe.Count);
        Assert.Null(probe.Target);
    }

    [Fact]
    public void SetConvertsListsOfRefsEnumNamesAndFieldsOfSubtypes()
    {
        Ok("go", "create", "--name", "Other");
        Ok("go", "create", "--name", "P", "--components", "CliProbe");
        var probe = _scene.AllObjects.Single(g => g.Name == "P").GetComponent<CliProbe>()!;
        probe.Shapes.Add(new CliProbeBox());

        Ok("set", "/P:CliProbe", "--values", """{"Targets": ["/Other", "/P"], "Mode": "Fast", "Shapes[0].Depth": 4}""");

        Assert.Equal(["Other", "P"], probe.Targets.Select(t => t!.Name));
        Assert.Equal(CliProbeMode.Fast, probe.Mode);
        Assert.Equal(4f, Assert.IsType<CliProbeBox>(probe.Shapes[0]).Depth);
        Assert.Contains("Slow, Fast", Fails("set", "/P:CliProbe", "Mode", "Medium"));
    }

    [Fact]
    public void SetRejectsUnknownFieldsBeforeWritingAnything()
    {
        Ok("go", "create", "--name", "P", "--components", "CliProbe");
        var probe = _scene.AllObjects.Single(g => g.Name == "P").GetComponent<CliProbe>()!;

        Assert.Contains("no field 'Nope'", Fails("set", "/P:CliProbe", "--values", """{"Count": 5, "Nope": 1}"""));
        Assert.Equal(0, probe.Count);
    }

    [Fact]
    public void ComponentAddSetsValuesInOneUndoStepAndRemoveIsUndoable()
    {
        Ok("go", "create", "--name", "P");
        var added = Ok("component", "add", "/P", "--type", "CliProbe", "--values", """{"Count": 7}""")!;
        Assert.Equal(7, added["fields"]!["Count"]!.GetValue<long>());

        var go = _scene.AllObjects.Single(g => g.Name == "P");
        Ok("component", "remove", "/P:CliProbe");
        Assert.Null(go.GetComponent<CliProbe>());

        Ok("undo");
        Assert.Equal(7, go.GetComponent<CliProbe>()!.Count);
        Ok("undo");
        Assert.Null(go.GetComponent<CliProbe>());
        Ok("undo", "--redo");
        Assert.Equal(7, go.GetComponent<CliProbe>()!.Count);
    }

    [Fact]
    public void GoRenamesReparentsDeactivatesDuplicatesAndDeletes()
    {
        Ok("go", "create", "--name", "A");
        Ok("go", "create", "--name", "B");
        Ok("go", "rename", "--target", "/B", "--name", "C");
        Ok("go", "parent", "--target", "/C", "--parent", "/A");
        Assert.Equal("/A/C", Ok("get", "/A/C")!["path"]!.GetValue<string>());

        Ok("go", "active", "--target", "/A/C", "--active", "false");
        Assert.False(_scene.AllObjects.Single(g => g.Name == "C").Enabled);

        Ok("go", "duplicate", "--target", "/A");
        Assert.Equal(2, _scene.RootObjects.Count(g => g.Name.StartsWith('A')));

        Ok("go", "parent", "--target", "/A[0]/C", "--parent", "/");
        Assert.Contains("is A or inside it", Fails("go", "parent", "--target", "/A[0]", "--parent", "/A[0]"));

        Ok("go", "delete", "--target", "/C");
        Assert.DoesNotContain(_scene.RootObjects, g => g.Name == "C");
        Ok("undo");
        Assert.Contains(_scene.RootObjects, g => g.Name == "C");
    }

    [Fact]
    public void TreeNestsAndFilters()
    {
        Ok("go", "create", "--name", "Root");
        Ok("go", "create", "--name", "Gun", "--parent", "/Root", "--components", "CliProbe");

        var tree = Ok("tree")!.AsArray();
        Assert.True(tree.Count == 1, tree.ToJsonString());
        Assert.Equal("Gun", tree.Single()!["children"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(1, Ok("tree", "--depth", "0")!.AsArray().Single()!["childCount"]!.GetValue<long>());
        Assert.Equal("/Root/Gun", Ok("tree", "--filter", "CliProbe")!["matches"]![0]!["path"]!.GetValue<string>());
    }

    [Fact]
    public void LogsReturnOnlyWhatIsNewSinceTheLastCall()
    {
        long seq = Ok("status")!["logSeq"]!.GetValue<long>();
        Debug.Log("cli first");
        Debug.LogError("cli second");

        var result = Ok("logs", "--since", seq.ToString())!;
        Assert.Equal(["cli first", "cli second"], result["logs"]!.AsArray().Select(l => l!["message"]!.GetValue<string>()));
        Assert.Single(Ok("logs", "--since", seq.ToString(), "--level", "error")!["logs"]!.AsArray());
        Assert.Empty(Ok("logs", "--since", result["nextSeq"]!.ToJsonString())!["logs"]!.AsArray());
    }

    [Fact]
    public void PlayModeEditsWarnOncePerPlaySession()
    {
        Ok("go", "create", "--name", "P", "--components", "CliProbe");
        Application.IsPlaying = true;

        var first = CliServer.Execute(new CliRunRequest { Command = "set", Argv = ["/P:CliProbe", "Count", "1"] });
        var second = CliServer.Execute(new CliRunRequest { Command = "set", Argv = ["/P:CliProbe", "Count", "2"] });
        Assert.Contains(first.Logs, l => l.Message.Contains("lost when play stops"));
        Assert.DoesNotContain(second.Logs, l => l.Message.Contains("lost when play stops"));
        Assert.Equal(2, _scene.AllObjects.Single(g => g.Name == "P").GetComponent<CliProbe>()!.Count);

        Application.IsPlaying = false;
        CliServer.Execute(new CliRunRequest { Command = "set", Argv = ["/P:CliProbe", "Count", "3"] });
        Application.IsPlaying = true;
        var nextSession = CliServer.Execute(new CliRunRequest { Command = "set", Argv = ["/P:CliProbe", "Count", "4"] });
        Assert.Contains(nextSession.Logs, l => l.Message.Contains("lost when play stops"));
    }

    [Fact]
    public void SceneOpenRefusesToDropUnsavedChanges()
    {
        EditorSceneManager.MarkDirty();
        Assert.Contains("unsaved changes", Fails("scene", "--action", "new"));
        Assert.True(Ok("scene")!["dirty"]!.GetValue<bool>());
    }

    [Fact]
    public void SceneNewReportsTheNewScene()
    {
        Ok("go", "create", "--name", "OnlyInTheOldScene");
        EditorSceneManager.IsDirty = false;

        var created = Ok("scene", "--action", "new")!;

        Assert.DoesNotContain(created["roots"]!.AsArray(), r => r!.GetValue<string>() == "OnlyInTheOldScene");
        Assert.DoesNotContain(Scene.Current.AllObjects, g => g.Name == "OnlyInTheOldScene");
    }
}

/// <summary> Asset, prefab, script and import settings commands against a throwaway project. </summary>
public class CliAssetCommandTests : EditorTestHarness
{
    private readonly Scene _scene;

    public CliAssetCommandTests()
    {
        EditorSettings.Instance = new EditorSettings();
        EditorRegistries.Initialize();
        CliCommands.Clear();
        foreach (var type in new[] { typeof(CliCommands), typeof(CliEditorCommands) })
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                CliCommands.Scan(method);

        Undo.Clear();
        _scene = new Scene();
        Scene.Load(_scene);
        Scene.ProcessPendingLoad();
    }

    public override void Dispose()
    {
        CliCommands.Clear();
        EditorSettings.Instance = null!;
        base.Dispose();
    }

    private static JsonNode? Ok(string command, params string[] argv)
    {
        var response = CliServer.Execute(new CliRunRequest { Command = command, Argv = [.. argv] });
        Assert.True(response.Ok, $"{command} failed: {response.Error}");
        return response.Result == null ? null : JsonNode.Parse(response.Result.ToJsonString());
    }

    private static string Fails(string command, params string[] argv)
    {
        var response = CliServer.Execute(new CliRunRequest { Command = command, Argv = [.. argv] });
        Assert.False(response.Ok, $"{command} should have failed");
        return response.Error!;
    }

    [Fact]
    public void AssetsAreCreatedFoundEditedMovedAndDeleted()
    {
        Assert.Contains(Ok("asset", "types")!.AsArray(), t => t!["type"]!.GetValue<string>() == "AvatarMask");

        var created = Ok("asset", "create", "--path", "Masks/Upper", "--type", "AvatarMask")!;
        Assert.Equal("Masks/Upper.mask", created["path"]!.GetValue<string>());
        Assert.Contains("already exists", Fails("asset", "create", "--path", "Masks/Upper.mask", "--type", "AvatarMask"));

        Assert.Equal("Masks/Upper.mask", Ok("asset", "find", "--type", "AvatarMask")!["results"]![0]!["path"]!.GetValue<string>());

        Ok("set", "Masks/Upper.mask", "--values", """{"DefaultWeight": 0.5, "Bones": [{"Bone": "Spine", "Weight": 1, "IncludeChildren": true}]}""");
        var mask = AssetDatabase.Load<AvatarMask>(Assets.PathToGuid("Masks/Upper.mask"))!;
        Assert.Equal(0.5f, mask.DefaultWeight);
        Assert.Equal("Spine", mask.Bones.Single().Bone);
        Assert.Contains("Spine", File.ReadAllText(AssetAbsolutePath("Masks/Upper.mask")));

        Ok("asset", "move", "--path", "Masks/Upper.mask", "--to", "Masks/Top.mask");
        Assert.True(File.Exists(AssetAbsolutePath("Masks/Top.mask")));

        Assert.Contains("--confirm", Fails("asset", "delete", "--path", "Masks/Top.mask"));
        Ok("asset", "delete", "--path", "Masks/Top.mask", "--confirm");
        Assert.False(File.Exists(AssetAbsolutePath("Masks/Top.mask")));
    }

    [Fact]
    public void ScriptsAreWrittenFromATemplate()
    {
        var result = Ok("script", "Scripts/Gun")!;
        Assert.Equal("Scripts/Gun.cs", result["path"]!.GetValue<string>());
        Assert.Contains("class Gun", File.ReadAllText(AssetAbsolutePath("Scripts/Gun.cs")));
        Assert.Contains("already exists", Fails("script", "Scripts/Gun.cs"));
        Assert.Contains("not a valid class name", Fails("script", "Scripts/1Bad.cs"));
        Assert.Contains(Ok("script", "--list")!.AsArray(), t => t!["name"]!.GetValue<string>() == "MonoBehaviour");
    }

    [Fact]
    public void PrefabsAreCreatedAndTheirOverridesListedAndReverted()
    {
        Ok("go", "create", "--name", "Probe", "--components", "CliProbe");
        var made = Ok("prefab", "create", "/Probe", "--path", "Prefabs/Probe")!;
        Assert.Equal("Prefabs/Probe.prefab", made["prefab"]!["path"]!.GetValue<string>());

        var instance = _scene.AllObjects.Single(g => g.Name == "Probe");
        Assert.True(instance.IsPrefabInstance);

        Ok("set", "/Probe:CliProbe", "Count", "4");
        Assert.Contains(Ok("prefab", "overrides", "/Probe")!["overrides"]!.AsArray(), o => o!["MemberName"]!.GetValue<string>() == "Count");

        Ok("prefab", "revert", "/Probe");
        Assert.Equal(0, _scene.AllObjects.Single(g => g.Name == "Probe").GetComponent<CliProbe>()!.Count);
    }

    [Fact]
    public void ApplyingAPrefabWritesAddedComponentsIntoIt()
    {
        Ok("go", "create", "--name", "Probe", "--components", "CliProbe");
        Ok("prefab", "create", "/Probe", "--path", "Prefabs/Probe");
        Ok("component", "add", "/Probe", "--type", "CliProbeExtra", "--values", """{"Level": 3}""");

        var listed = Ok("prefab", "overrides", "/Probe")!;
        Assert.Contains(listed["additions"]!.AsArray(), a => a!["component"]!.GetValue<string>() == "CliProbeExtra");

        var applied = Ok("prefab", "apply", "/Probe")!;
        Assert.Single(applied["appliedAdditions"]!.AsArray());
        Assert.Contains("CliProbeExtra", File.ReadAllText(AssetAbsolutePath("Prefabs/Probe.prefab")));
        Assert.Empty(Ok("prefab", "overrides", "/Probe")!["additions"]!.AsArray());
    }

    [Fact]
    public void ImportSettingsMergeNestedKeysIntoTheMetaFile()
    {
        Ok("asset", "create", "--path", "Masks/M", "--type", "AvatarMask");
        var result = Ok("importer", "Masks/M.mask", "--values", """{"clips.Fire.loop": true, "scale": 2}""")!;

        Assert.True(result["settings"]!["clips"]!["Fire"]!["loop"]!.GetValue<bool>());
        string meta = File.ReadAllText(AssetAbsolutePath("Masks/M.mask") + ".meta");
        Assert.Contains("Fire", meta);
    }
}

/// <summary> The graph command building a small state machine, the way an agent would. </summary>
public class CliGraphCommandTests : EditorTestHarness
{
    public CliGraphCommandTests()
    {
        EditorSettings.Instance = new EditorSettings();
        EditorRegistries.Initialize();
        CliCommands.Clear();
        foreach (var type in new[] { typeof(CliCommands), typeof(CliEditorCommands) })
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                CliCommands.Scan(method);
        Undo.Clear();
        var scene = new Scene();
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
    }

    public override void Dispose()
    {
        CliCommands.Clear();
        EditorSettings.Instance = null!;
        base.Dispose();
    }

    private static CliRunResponse Run(string command, params string[] argv) => CliServer.Execute(new CliRunRequest { Command = command, Argv = [.. argv] });

    private static JsonNode Ok(string command, params string[] argv)
    {
        var response = Run(command, argv);
        Assert.True(response.Ok, $"{command} failed: {response.Error}");
        return JsonNode.Parse(response.Result!.ToJsonString())!;
    }

    private const string Machine = """
        [
          {"op": "param", "name": "Speed", "kind": "Number", "value": 0},
          {"op": "add", "type": "motion.stateMachine", "as": "sm"},
          {"op": "state", "machine": "$sm", "name": "Idle", "as": "idle"},
          {"op": "state", "machine": "$sm", "name": "Run", "as": "run"},
          {"op": "add", "type": "motion.clip", "owner": "$idle", "as": "idleClip"},
          {"op": "connect", "from": "$idleClip", "to": "$idle.output", "pin": "Pose"},
          {"op": "add", "type": "motion.constBool", "owner": "$run", "as": "go"},
          {"op": "connect", "from": "$go", "to": "$run.output", "pin": "Enter"},
          {"op": "transition", "machine": "$sm", "from": "Idle", "to": "Run", "duration": 0.3},
          {"op": "gate", "state": "$idle", "exit": true}
        ]
        """;

    [Fact]
    public void AStateMachineIsBuiltFromOpsAndSaved()
    {
        Assert.Contains(Ok("graph", "--action", "types", "--filter", "stateMachine").AsArray(), t => t!["id"]!.GetValue<string>() == "motion.stateMachine");
        Ok("asset", "create", "--path", "Anim/Player", "--type", "AnimationGraph");

        var described = Ok("graph", "Anim/Player.animgraph", "--action", "edit", "--ops", Machine)["graph"]!;
        var machine = described["nodes"]!.AsArray().Single(n => n!["type"]!.GetValue<string>() == "motion.stateMachine")!;
        Assert.Equal(machine["id"]!.GetValue<string>(), described["root"]!.GetValue<string>());

        var states = machine["states"]!.AsArray();
        Assert.Equal(["Idle", "Run"], states.Select(s => s!["name"]!.GetValue<string>()));
        Assert.True(states[0]!["default"]!.GetValue<bool>());
        Assert.Equal(0.3f, states[0]!["transitions"]![0]!["duration"]!.GetValue<float>());

        string runOutput = states[1]!["output"]!.GetValue<string>();
        var output = described["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == runOutput)!;
        Assert.Contains(output["inputs"]!.AsArray(), i => i!["name"]!.GetValue<string>() == "Enter" && i["from"] != null);

        Assert.Contains("Speed", File.ReadAllText(AssetAbsolutePath("Anim/Player.animgraph")));
    }

    [Fact]
    public void EveryGraphExampleInTheSkillsApplies()
    {
        var examples = AgentSkillExampleTests.SkillBlocks("json").Where(j => j.TrimStart().StartsWith('[') && j.Contains("\"op\"")).ToList();
        Assert.NotEmpty(examples);
        for (int i = 0; i < examples.Count; i++)
        {
            Ok("asset", "create", "--path", $"Anim/Example{i}", "--type", "AnimationGraph");
            var described = Ok("graph", $"Anim/Example{i}.animgraph", "--action", "edit", "--ops", examples[i])["graph"]!;
            Assert.False(string.IsNullOrEmpty(described["root"]?.GetValue<string>()), $"Example {i} has no root.");
        }
    }

    [Fact]
    public void ABadOpChangesNothing()
    {
        Ok("asset", "create", "--path", "Anim/G", "--type", "AnimationGraph");
        var response = Run("graph", "Anim/G.animgraph", "--action", "edit", "--ops", """[{"op": "add", "type": "motion.clip"}, {"op": "connect", "from": "nope", "to": "x"}]""");
        Assert.False(response.Ok);
        Assert.Contains("no node 'x'", response.Error);
        Assert.Empty(Ok("graph", "Anim/G.animgraph")["nodes"]!.AsArray());
    }

    [Fact]
    public void GraphEditsUndoBackOnDisk()
    {
        Ok("asset", "create", "--path", "Anim/U", "--type", "AnimationGraph");
        Ok("graph", "Anim/U.animgraph", "--action", "edit", "--ops", """[{"op": "param", "name": "Jump", "kind": "Flag", "trigger": true}]""");
        Assert.Contains("Jump", File.ReadAllText(AssetAbsolutePath("Anim/U.animgraph")));

        Ok("undo");
        Assert.DoesNotContain("Jump", File.ReadAllText(AssetAbsolutePath("Anim/U.animgraph")));
        Assert.Empty(Ok("graph", "Anim/U.animgraph")["parameters"]!.AsArray());
    }
}

/// <summary> The api command, finding real signatures and their docs. </summary>
public class CliApiCommandTests : EditorTestHarness
{
    public CliApiCommandTests()
    {
        EditorSettings.Instance = new EditorSettings();
        CliCommands.Clear();
        foreach (var method in typeof(CliEditorCommands).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            CliCommands.Scan(method);
    }

    public override void Dispose()
    {
        CliCommands.Clear();
        EditorSettings.Instance = null!;
        base.Dispose();
    }

    private static JsonNode Ok(params string[] argv)
    {
        var response = CliServer.Execute(new CliRunRequest { Command = "api", Argv = [.. argv] });
        Assert.True(response.Ok, response.Error);
        return JsonNode.Parse(response.Result!.ToJsonString())!;
    }

    [Fact]
    public void SearchFindsMembersWithTheirSignatureAndSummary()
    {
        var hit = Ok("--search", "LogOnce")["members"]!.AsArray().First(m => m!["member"]!.GetValue<string>().Contains("LogOnce("))!;
        Assert.Equal("public static void LogOnce(string id, string message)", hit["member"]!.GetValue<string>());
        Assert.Contains("first time", hit["summary"]!.GetValue<string>());
    }

    [Fact]
    public void ATypeListsItsMembersAndAMemberItsParameterDocs()
    {
        var type = Ok("--type", "AssetRef");
        Assert.Equal("Prowl.Runtime.AssetRef<T>", type["name"]!.GetValue<string>());
        Assert.Contains("asset", type["summary"]!.GetValue<string>());
        Assert.Contains(type["members"]!.AsArray(), m => m!["signature"]!.GetValue<string>() == "public T Load()");

        var member = Ok("--type", "Prowl.Runtime.Debug", "--member", "EnsureMainThread")["members"]![0]!;
        Assert.Contains(member["parameters"]!.AsArray(), p => p!.GetValue<string>().StartsWith("member: Defaults to the calling member"));
        Assert.Contains("main thread", member["returns"]!.GetValue<string>());
    }

    [Fact]
    public void AShortNameSharedWithALibraryPrefersTheEngineType()
    {
        var camera = Ok("--type", "Camera");
        Assert.Equal("Prowl.Runtime.Camera", camera["name"]!.GetValue<string>());
        Assert.NotEmpty(camera["alsoNamed"]!.AsArray());
    }

    [Fact]
    public void UserScriptsAndInheritedMembersAreIncluded()
    {
        var probe = Ok("--type", "CliProbe");
        Assert.Contains(probe["members"]!.AsArray(), m => m!["signature"]!.GetValue<string>() == "public float Speed");
        Assert.Contains(probe["members"]!.AsArray(), m => m!["signature"]!.GetValue<string>() == "public override void OnValidate()");

        var inherited = Ok("--type", "CliProbe", "--member", "GetComponent", "--inherited");
        Assert.Equal("MonoBehaviour", inherited["members"]![0]!["declaredOn"]!.GetValue<string>());
    }
}

/// <summary> The files an agent reads, written into a project. </summary>
public class AgentFilesTests : EditorTestHarness
{
    private string ClaudeMd => Path.Combine(Project.RootPath, "CLAUDE.md");
    private string Skill => Path.Combine(Project.RootPath, ".claude", "skills", "prowl-cli", "SKILL.md");

    // A file as an older editor version would have left it: different content, with a marker that matches it.
    private static string OldVersion(string content) => content + "\n" + AgentFiles.Marker(content) + "\n";

    [Fact]
    public void FilesAreWrittenIntoTheProjectWithTheLauncher()
    {
        AgentFiles.Write(Project.RootPath, Project.LibraryPath);

        Assert.True(File.Exists(ClaudeMd));
        Assert.True(File.Exists(Path.Combine(Project.RootPath, "AGENTS.md")));
        Assert.StartsWith("---\nname: prowl-cli", File.ReadAllText(Skill));
        Assert.Contains("prowl:generated", File.ReadAllText(Skill));

        string launcher = Path.Combine(Project.LibraryPath, "prowl.cmd");
        File.WriteAllText(launcher, "stale");
        AgentFiles.Write(Project.RootPath, Project.LibraryPath);
        Assert.Contains("Prowl.Cli", File.ReadAllText(launcher));

        // Git Bash would otherwise turn a ref like /Player into C:/Program Files/Git/Player.
        Assert.Contains("MSYS_NO_PATHCONV=1", File.ReadAllText(Path.Combine(Project.LibraryPath, "prowl")));
    }

    [Fact]
    public void UntouchedFilesFromAnOlderVersionAreUpdated()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Skill)!);
        File.WriteAllText(Skill, OldVersion("---\nname: prowl-cli\n---\nold advice\n"));

        AgentFiles.Write(Project.RootPath, Project.LibraryPath);
        Assert.DoesNotContain("old advice", File.ReadAllText(Skill));
        Assert.Contains("# Driving the Prowl editor", File.ReadAllText(Skill));
    }

    [Fact]
    public void EditedOrOptedOutFilesAreLeftAlone()
    {
        AgentFiles.Write(Project.RootPath, Project.LibraryPath);

        string edited = File.ReadAllText(ClaudeMd).Replace("This is a game", "This is MY game");
        File.WriteAllText(ClaudeMd, edited);
        string optedOut = OldVersion("old skill text\n").Split("<!--")[0];
        File.WriteAllText(Skill, optedOut);

        AgentFiles.Write(Project.RootPath, Project.LibraryPath);
        Assert.Equal(edited, File.ReadAllText(ClaudeMd));
        Assert.Equal(optedOut, File.ReadAllText(Skill));
    }

    [Fact]
    public void ACurrentFileIsNotRewritten()
    {
        AgentFiles.Write(Project.RootPath, Project.LibraryPath);
        var written = File.GetLastWriteTimeUtc(ClaudeMd);
        File.SetLastWriteTimeUtc(ClaudeMd, written.AddDays(-1));

        AgentFiles.Write(Project.RootPath, Project.LibraryPath);
        Assert.Equal(written.AddDays(-1), File.GetLastWriteTimeUtc(ClaudeMd));
    }
}

/// <summary> Every complete script example in the agent skills compiles against the engine, so the docs can not drift from the code. </summary>
public class AgentSkillExampleTests : EditorTestHarness
{
    /// <summary>The body of every fenced block of this language in the embedded agent guides.</summary>
    public static IEnumerable<string> SkillBlocks(string language)
    {
        var assembly = typeof(AgentFiles).Assembly;
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".md", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            string text = reader.ReadToEnd().ReplaceLineEndings("\n");
            foreach (System.Text.RegularExpressions.Match block in System.Text.RegularExpressions.Regex.Matches(text, $"```{language}\n(.*?)```", System.Text.RegularExpressions.RegexOptions.Singleline))
                yield return block.Groups[1].Value;
        }
    }

    [Fact]
    public void EveryScriptExampleInTheSkillsCompiles()
    {
        int examples = 0;
        foreach (string code in SkillBlocks("csharp").Where(c => c.Contains("class ")))
            WriteScript($"SkillExample{examples++}.cs", code);

        Assert.True(examples >= 4, $"Expected the skill examples, found {examples}.");
        var result = Prowl.Editor.Projects.Scripting.ScriptCompiler.CompileAll(Project);
        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == "Warning");
    }

    [Fact]
    public void EveryEvalRecipeInTheSkillsCompiles()
    {
        var assemblies = Prowl.Editor.Projects.Scripting.ScriptAssemblyManager.LiveAssemblies().Where(a => !a.IsDynamic).ToList();
        var recipes = SkillBlocks("csharp").Where(c => c.StartsWith("// prowl eval", StringComparison.Ordinal)).ToList();

        Assert.True(recipes.Count >= 3, $"Expected the eval recipes, found {recipes.Count}.");
        foreach (string recipe in recipes)
        {
            var error = Record.Exception(() => CliEval.Compile(recipe, assemblies));
            Assert.True(error == null, $"{recipe}\n{error?.Message}");
        }
    }
}
