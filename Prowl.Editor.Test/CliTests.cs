// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

using Prowl.Cli;
using Prowl.Runtime.Tasks;

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
        CliCommands.Clear();
        foreach (var type in new[] { typeof(CliCommands), typeof(CliTests) })
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                CliCommands.Scan(method);
    }

    public override void Dispose()
    {
        CliServer.Stop();
        CliCommands.Clear();
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
