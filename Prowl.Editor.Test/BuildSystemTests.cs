// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

using Prowl.Editor.Build;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The project building system: the stage graph and the executor that walks it, targets and their
/// capabilities, asset chunking and variant caching, the generated project file, and the contracts a
/// built player depends on.
/// </summary>
public class BuildSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prowl-buildsys", Guid.NewGuid().ToString("N"));

    public BuildSystemTests()
    {
        Directory.CreateDirectory(_root);
        EditorRegistries.Initialize();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    // ---------------------------------------------------------------- BuildProgressTests

    [Fact]
    public void Cancel_SignalsTheTokenThePipelineIsGiven()
    {
        var progress = new BuildProgress();

        Assert.False(progress.Token.IsCancellationRequested);
        Assert.False(progress.IsCancelled);

        progress.Cancel();

        Assert.True(progress.Token.IsCancellationRequested);
        Assert.True(progress.IsCancelled);
    }

    // The window drops its reference on completion, but a click already in flight must not signal a
    // token nothing is listening to.
    [Fact]
    public void Cancel_AfterCompletion_DoesNothing()
    {
        var progress = new BuildProgress();
        progress.Complete(new BuildResult { Success = true });

        progress.Cancel();

        Assert.False(progress.IsCancelled);
    }

    [Fact]
    public void Cancel_IsSafeToRepeat()
    {
        var progress = new BuildProgress();

        progress.Cancel();
        progress.Cancel();

        Assert.True(progress.IsCancelled);
    }

    [Fact]
    public void ACancelledResult_IsNotAFailureToReport()
    {
        var result = new BuildResult { Success = false, Cancelled = true };

        Assert.True(result.Cancelled);
        Assert.Equal("", result.Errors);
    }

    // ---------------------------------------------------------------- StageGraphTests

    private static StageNode Node(BuildStage stage, params BuildStage[] dependsOn)
        => new() { Stage = stage, DependsOn = dependsOn };

    [Fact]
    public void TopologicalOrder_RespectsDependencies()
    {
        var graph = new StageGraph(
        [
            Node(BuildStage.PackAssets, BuildStage.CompilePlayer),
            Node(BuildStage.CompilePlayer, BuildStage.GeneratePlayer),
            Node(BuildStage.GeneratePlayer, BuildStage.Validate),
            Node(BuildStage.Validate),
        ]);

        var order = graph.TopologicalOrder();

        Assert.NotNull(order);
        Assert.Equal(
        [
            BuildStage.Validate,
            BuildStage.GeneratePlayer,
            BuildStage.CompilePlayer,
            BuildStage.PackAssets,
        ], order);
    }

    // Validating after the script compile means a build with an unusable output directory pays for a
    // full compile before it is told so.
    [Fact]
    public void DesktopGraph_ValidatesBeforeCompiling()
    {
        var order = new DesktopBuildPipeline().CreateStageGraph(Request()).TopologicalOrder()?.ToList();

        Assert.NotNull(order);
        Assert.True(order!.IndexOf(BuildStage.Validate) < order.IndexOf(BuildStage.CompileCode),
            "The desktop graph compiles before it validates.");
    }

    // A cycle must be a construction error naming the graph, not a build that does real work and then
    // wedges with nothing left to run.
    [Fact]
    public void Cycle_ThrowsAtConstruction()
    {
        var ex = Assert.Throws<ArgumentException>(() => new StageGraph(
        [
            Node(BuildStage.Validate, BuildStage.PackAssets),
            Node(BuildStage.PackAssets, BuildStage.Validate),
        ]));

        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DependencyOutsideGraph_ThrowsAtConstruction()
    {
        var ex = Assert.Throws<ArgumentException>(() => new StageGraph(
        [
            Node(BuildStage.PackAssets, BuildStage.CompilePlayer),
        ]));

        Assert.Contains("not in the graph", ex.Message);
    }

    [Fact]
    public void DuplicateStage_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new StageGraph(
        [
            Node(BuildStage.Validate),
            Node(BuildStage.Validate),
        ]));
    }

    // Independent stages are both ready at once, which is what lets the executor overlap them.
    [Fact]
    public void IndependentStages_AreReadyTogether()
    {
        var graph = new StageGraph(
        [
            Node(BuildStage.Validate),
            Node(BuildStage.CompileShaders, BuildStage.Validate),
            Node(BuildStage.CopyPlugins, BuildStage.Validate),
        ]);

        var ready = graph.Ready(new HashSet<BuildStage> { BuildStage.Validate }).ToList();

        Assert.Equal(2, ready.Count);
        Assert.Contains(ready, n => n.Stage == BuildStage.CompileShaders);
        Assert.Contains(ready, n => n.Stage == BuildStage.CopyPlugins);
    }

    // An identical graph has to produce an identical order, or the build stops being reproducible.
    [Fact]
    public void TopologicalOrder_IsStableAcrossDeclarationOrder()
    {
        StageNode[] nodes =
        [
            Node(BuildStage.Validate),
            Node(BuildStage.CompileShaders, BuildStage.Validate),
            Node(BuildStage.CopyPlugins, BuildStage.Validate),
            Node(BuildStage.PackAssets, BuildStage.CompileShaders, BuildStage.CopyPlugins),
        ];

        var forward = new StageGraph(nodes).TopologicalOrder();
        var reversed = new StageGraph(nodes.Reverse()).TopologicalOrder();

        Assert.Equal(forward, reversed);
    }

    // ---------------------------------------------------------------- BuildExecutorTests

    /// <summary>A pipeline described by a lambda per stage, so a test says only what it is testing.</summary>
    private sealed class ScriptedPipeline : StagedTestPipeline
    {
        private readonly StageGraph _graph;
        private readonly Dictionary<BuildStage, Func<IBuildContext, IEnumerable<BuildOperation>>> _plans;

        public ScriptedPipeline(StageGraph graph, Dictionary<BuildStage, Func<IBuildContext, IEnumerable<BuildOperation>>> plans)
        {
            _graph = graph;
            _plans = plans;
        }

        public override StageGraph CreateStageGraph(BuildRequest request) => _graph;

        public override async IAsyncEnumerable<BuildOperation> PlanStageAsync(
            BuildStage stage, IBuildContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            if (!_plans.TryGetValue(stage, out var plan)) yield break;
            foreach (var operation in plan(context))
                yield return operation;
        }
    }

    private sealed class Lambda(Func<IBuildContext, CancellationToken, Task> body) : IOperationHandler
    {
        public Task ExecuteAsync(IBuildContext context, CancellationToken ct) => body(context, ct);
    }

    private static StageNode Node(BuildStage stage, StageFailurePolicy policy, params BuildStage[] dependsOn)
        => new() { Stage = stage, DependsOn = dependsOn, OnFailure = policy };

    // Stages must run in dependency order, and a later stage must see what an earlier one published.
    [Fact]
    public async Task Stages_RunInDependencyOrder_AndSeeEarlierOutputs()
    {
        var order = new List<string>();

        var graph = new StageGraph(
        [
            Node(BuildStage.Validate, StageFailurePolicy.FailFast),
            Node(BuildStage.PackAssets, StageFailurePolicy.FailFast, BuildStage.Validate),
        ]);

        var pipeline = new ScriptedPipeline(graph, new()
        {
            [BuildStage.Validate] = _ =>
            [
                new BuildOperation.Custom(new Lambda((ctx, _) =>
                {
                    order.Add("validate");
                    ctx.SetOutput(new List<string> { "from-validate" });
                    return Task.CompletedTask;
                }), "validate"),
            ],
            [BuildStage.PackAssets] = _ =>
            [
                new BuildOperation.Custom(new Lambda((ctx, _) =>
                {
                    order.Add("pack:" + ctx.GetOutput<List<string>>()[0]);
                    return Task.CompletedTask;
                }), "pack"),
            ],
        });

        var context = new BuildContext(Request());
        var outcome = await new BuildExecutor().RunAsync(pipeline, context);

        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Issues));
        Assert.Equal(["validate", "pack:from-validate"], order);
    }

    // FailFast has to stop the build, or a broken compile is followed by hours of pointless packaging.
    [Fact]
    public async Task FailFastStage_AbortsLaterStages()
    {
        bool laterRan = false;

        var graph = new StageGraph(
        [
            Node(BuildStage.CompileCode, StageFailurePolicy.FailFast),
            Node(BuildStage.PackAssets, StageFailurePolicy.FailFast, BuildStage.CompileCode),
        ]);

        var pipeline = new ScriptedPipeline(graph, new()
        {
            [BuildStage.CompileCode] = _ =>
            [
                new BuildOperation.Custom(new Lambda((_, _) => throw new InvalidOperationException("compile blew up")), "compile"),
            ],
            [BuildStage.PackAssets] = _ =>
            [
                new BuildOperation.Custom(new Lambda((_, _) => { laterRan = true; return Task.CompletedTask; }), "pack"),
            ],
        });

        var context = new BuildContext(Request());
        var outcome = await new BuildExecutor().RunAsync(pipeline, context);

        Assert.False(outcome.Succeeded);
        Assert.False(laterRan);
        Assert.Contains(outcome.Issues, i => i.Message.Contains("compile blew up"));
    }

    // The opposite policy: one broken asset must not hide the other nineteen.
    [Fact]
    public async Task CollectAndContinue_ReportsEveryFailureInTheStage()
    {
        var graph = new StageGraph([Node(BuildStage.ProcessAssets, StageFailurePolicy.CollectAndContinue)]);

        var pipeline = new ScriptedPipeline(graph, new()
        {
            [BuildStage.ProcessAssets] = _ => Enumerable.Range(0, 20).Select(i =>
                (BuildOperation)new BuildOperation.Custom(
                    new Lambda((_, _) => i % 2 == 0 ? throw new InvalidOperationException($"bad {i}") : Task.CompletedTask),
                    $"asset {i}")),
        });

        var context = new BuildContext(Request());
        var outcome = await new BuildExecutor().RunAsync(pipeline, context);

        Assert.False(outcome.Succeeded);
        Assert.Equal(10, outcome.Issues.Count(i => i.Severity == BuildSeverity.Error));
        Assert.Equal(20, outcome.OperationsRun);
    }

    // Operations in a stage are independent by contract, so the executor is free to overlap them.
    [Fact]
    public async Task OperationsWithinAStage_RunConcurrently()
    {
        int running = 0;
        int peak = 0;

        var graph = new StageGraph(
        [
            new StageNode { Stage = BuildStage.ProcessAssets, Resources = StageResources.CpuBound },
        ]);

        var pipeline = new ScriptedPipeline(graph, new()
        {
            [BuildStage.ProcessAssets] = _ => Enumerable.Range(0, 16).Select(_ =>
                (BuildOperation)new BuildOperation.Custom(new Lambda(async (_, ct) =>
                {
                    int now = Interlocked.Increment(ref running);
                    InterlockedMax(ref peak, now);
                    await Task.Delay(30, ct);
                    Interlocked.Decrement(ref running);
                }), "work")),
        });

        var context = new BuildContext(Request());
        var outcome = await new BuildExecutor(new ExecutionLimits { CpuBound = 4 }).RunAsync(pipeline, context);

        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Issues));
        Assert.True(peak > 1, $"expected overlap, peak concurrency was {peak}");
        Assert.True(peak <= 4, $"limit of 4 exceeded, peak was {peak}");
    }

    // Exclusive means alone: a step that owns the output directory cannot have company.
    [Fact]
    public async Task ExclusiveStage_NeverSharesWithAnother()
    {
        var graph = new StageGraph(
        [
            new StageNode { Stage = BuildStage.Validate },
            new StageNode { Stage = BuildStage.CompilePlayer, DependsOn = [BuildStage.Validate], Resources = StageResources.Exclusive },
            new StageNode { Stage = BuildStage.CopyPlugins, DependsOn = [BuildStage.Validate] },
        ]);

        var seen = new List<BuildStage>();
        var pipeline = new ScriptedPipeline(graph, new()
        {
            [BuildStage.Validate] = _ => [],
            [BuildStage.CompilePlayer] = _ => [Record(seen, BuildStage.CompilePlayer)],
            [BuildStage.CopyPlugins] = _ => [Record(seen, BuildStage.CopyPlugins)],
        });

        var context = new BuildContext(Request());
        var outcome = await new BuildExecutor().RunAsync(pipeline, context);

        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Issues));
        Assert.Equal(2, seen.Count);
    }

    // A build that dies partway must not leave a half written file a later run would trust.
    [Fact]
    public async Task WriteFile_ThatFails_LeavesNoPartialOutput()
    {
        string destination = Path.Combine(_root, "out", "artifact.bin");

        var graph = new StageGraph([Node(BuildStage.WriteManifest, StageFailurePolicy.CollectAndContinue)]);

        var pipeline = new ScriptedPipeline(graph, new()
        {
            [BuildStage.WriteManifest] = _ =>
            [
                new BuildOperation.WriteFile(_ => throw new IOException("disk went away"), destination),
            ],
        });

        var context = new BuildContext(Request());
        var outcome = await new BuildExecutor().RunAsync(pipeline, context);

        Assert.False(outcome.Succeeded);
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task WriteFile_ThatSucceeds_ProducesTheContent()
    {
        string destination = Path.Combine(_root, "out", "nested", "artifact.txt");

        var graph = new StageGraph([Node(BuildStage.WriteManifest, StageFailurePolicy.FailFast)]);

        var pipeline = new ScriptedPipeline(graph, new()
        {
            [BuildStage.WriteManifest] = _ =>
            [
                new BuildOperation.WriteFile(
                    _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("hello"))),
                    destination),
            ],
        });

        var context = new BuildContext(Request());
        var outcome = await new BuildExecutor().RunAsync(pipeline, context);

        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Issues));
        Assert.Equal("hello", File.ReadAllText(destination));
    }

    // Asking for an output nobody published is a wiring mistake, and has to say so.
    [Fact]
    public void MissingOutput_ThrowsNamingTheType()
    {
        var context = new BuildContext(Request());

        var ex = Assert.Throws<InvalidOperationException>(() => context.GetOutput<List<string>>());
        Assert.Contains("List`1", ex.Message);
    }

    private static BuildOperation Record(List<BuildStage> seen, BuildStage stage)
        => new BuildOperation.Custom(new Lambda((_, _) =>
        {
            lock (seen) seen.Add(stage);
            return Task.CompletedTask;
        }), stage.Id);

    private static void InterlockedMax(ref int target, int value)
    {
        int current = Volatile.Read(ref target);
        while (value > current)
        {
            int previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current) break;
            current = previous;
        }
    }

    // ---------------------------------------------------------------- ExecutorRobustnessTests

    // Cancelling has to stop the build where it is, not run the remaining stages to completion.
    [Fact]
    public async Task Cancelling_StopsTheBuildAndSkipsWhatFollows()
    {
        using var source = new CancellationTokenSource();
        bool lastRan = false;

        var graph = new StageGraph(
        [
            new StageNode { Stage = BuildStage.CompileCode },
            new StageNode { Stage = BuildStage.Finalize, DependsOn = [BuildStage.CompileCode] },
        ]);

        var pipeline = new ScriptedStages(graph, new()
        {
            [BuildStage.CompileCode] = () => source.Cancel(),
            [BuildStage.Finalize] = () => lastRan = true,
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new BuildExecutor().RunAsync(pipeline, new BuildContext(Request()), source.Token));

        Assert.False(lastRan, "A cancelled build carried on to the next stage.");
    }

    // Nothing at all should run when the token is already cancelled.
    [Fact]
    public async Task AnAlreadyCancelledToken_RunsNothing()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        bool ran = false;
        var graph = new StageGraph([new StageNode { Stage = BuildStage.Validate }]);
        var pipeline = new ScriptedStages(graph, new() { [BuildStage.Validate] = () => ran = true });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new BuildExecutor().RunAsync(pipeline, new BuildContext(Request()), source.Token));

        Assert.False(ran);
    }

    // An error a tolerant stage collected on purpose must not abort a fail fast stage that ran fine.
    [Fact]
    public async Task ErrorFromATolerantStage_DoesNotAbortTheRest()
    {
        bool lastRan = false;

        var graph = new StageGraph(
        [
            new StageNode { Stage = BuildStage.ProcessAssets, OnFailure = StageFailurePolicy.CollectAndContinue },
            new StageNode { Stage = BuildStage.CopyRuntime, OnFailure = StageFailurePolicy.FailFast },
            new StageNode { Stage = BuildStage.Finalize, DependsOn = [BuildStage.ProcessAssets, BuildStage.CopyRuntime] },
        ]);

        var pipeline = new ScriptedStages(graph, new()
        {
            [BuildStage.ProcessAssets] = () => throw new InvalidOperationException("one bad asset"),
            [BuildStage.CopyRuntime] = () => { },
            [BuildStage.Finalize] = () => lastRan = true,
        });

        var outcome = await new BuildExecutor().RunAsync(pipeline, new BuildContext(Request()));

        Assert.True(lastRan, "A collected asset error stopped the stages that follow it.");
        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Issues, i => i.Message.Contains("one bad asset"));
    }

    // And a fail fast stage that does fail still stops everything after it.
    [Fact]
    public async Task FailureInAFailFastStage_StopsTheRest()
    {
        bool lastRan = false;

        var graph = new StageGraph(
        [
            new StageNode { Stage = BuildStage.CompileCode, OnFailure = StageFailurePolicy.FailFast },
            new StageNode { Stage = BuildStage.Finalize, DependsOn = [BuildStage.CompileCode] },
        ]);

        var pipeline = new ScriptedStages(graph, new()
        {
            [BuildStage.CompileCode] = () => throw new InvalidOperationException("compile failed"),
            [BuildStage.Finalize] = () => lastRan = true,
        });

        var outcome = await new BuildExecutor().RunAsync(pipeline, new BuildContext(Request()));

        Assert.False(lastRan);
        Assert.False(outcome.Succeeded);
    }

    /// <summary>One synchronous body per stage, for tests about ordering rather than about operations.</summary>
    private sealed class ScriptedStages(StageGraph graph, Dictionary<BuildStage, Action> bodies) : StagedTestPipeline
    {
        public override StageGraph CreateStageGraph(BuildRequest request) => graph;

        public override async IAsyncEnumerable<BuildOperation> PlanStageAsync(
            BuildStage stage, IBuildContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            if (!bodies.TryGetValue(stage, out var body)) yield break;

            yield return new BuildOperation.Custom(new Handler((_, _) =>
            {
                body();
                return Task.CompletedTask;
            }), stage.Id);
        }
    }

    private sealed class Handler(Func<IBuildContext, CancellationToken, Task> body) : IOperationHandler
    {
        public Task ExecuteAsync(IBuildContext context, CancellationToken ct) => body(context, ct);
    }

    /// <summary>Yields a few slow operations, then throws while still planning.</summary>
    private sealed class PlanThrowsPipeline : StagedTestPipeline
    {
        public int Started;
        public int Finished;

        public override StageGraph CreateStageGraph(BuildRequest request)
            => new([new StageNode { Stage = BuildStage.ProcessAssets, OnFailure = StageFailurePolicy.CollectAndContinue }]);

        public override async IAsyncEnumerable<BuildOperation> PlanStageAsync(
            BuildStage stage, IBuildContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < 4; i++)
            {
                await Task.Yield();
                yield return new BuildOperation.Custom(new Handler(async (_, token) =>
                {
                    Interlocked.Increment(ref Started);
                    await Task.Delay(80, token);
                    Interlocked.Increment(ref Finished);
                }), $"slow {i}");
            }

            throw new InvalidOperationException("planning fell over");
        }
    }

    /// <summary>
    /// Planning that throws leaves operations in flight. They have to be drained before the stage's
    /// semaphore is disposed, or they release a disposed semaphore on a thread pool thread.
    /// </summary>
    [Fact]
    public async Task PlanningThatThrows_DrainsInFlightOperations()
    {
        var pipeline = new PlanThrowsPipeline();
        var context = new BuildContext(Request());

        var unobserved = new List<Exception>();
        void OnUnobserved(object? _, UnobservedTaskExceptionEventArgs e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += OnUnobserved;

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await new BuildExecutor().RunAsync(pipeline, context));

            // Everything that started also finished, rather than being abandoned mid-flight.
            Assert.Equal(pipeline.Started, pipeline.Finished);
            Assert.True(pipeline.Started > 0, "expected operations to have been in flight when planning threw");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        Assert.Empty(unobserved);
    }

    /// <summary>
    /// Two operations writing the same key at once is legal: operations in a stage are independent by
    /// contract and nothing stops a pipeline producing the same variant twice.
    /// </summary>
    [Fact]
    public async Task ConcurrentWritesOfTheSameKey_BothSucceed()
    {
        var cache = new LocalVariantCache(Path.Combine(_root, "cache"));
        var key = new VariantKey("hash", "windows-x64", "proc", 1);

        byte[] payload = Encoding.UTF8.GetBytes(new string('x', 512 * 1024));

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            cache.WriteAsync(key, new MemoryStream(payload)).AsTask()));

        Assert.True(await cache.ExistsAsync(key));

        await using var stored = await cache.OpenAsync(key);
        using var buffer = new MemoryStream();
        await stored!.CopyToAsync(buffer);

        // A shared temporary name would have let one writer truncate what another was serving.
        Assert.Equal(payload.Length, buffer.Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "cache"), "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ConcurrentWriteFileOperations_ToDistinctPaths_LeaveNoPartials()
    {
        var graph = new StageGraph([new StageNode { Stage = BuildStage.WriteManifest }]);

        var pipeline = new ScriptedWrites(graph, Path.Combine(_root, "out"), 16);
        var context = new BuildContext(Request());

        var outcome = await new BuildExecutor().RunAsync(pipeline, context);

        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Issues));
        Assert.Equal(16, Directory.GetFiles(Path.Combine(_root, "out")).Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "out"), "*.partial"));
    }

    /// <summary>
    /// The reason the graph is a graph. Two stages that share no dependency must overlap, or the
    /// ordering information is being used only to serialise them.
    /// </summary>
    [Fact]
    public async Task IndependentStages_RunConcurrently()
    {
        var graph = new StageGraph(
        [
            new StageNode { Stage = BuildStage.Validate },
            new StageNode { Stage = BuildStage.CompileShaders, DependsOn = [BuildStage.Validate] },
            new StageNode { Stage = BuildStage.CopyPlugins, DependsOn = [BuildStage.Validate] },
        ]);

        int running = 0;
        int peak = 0;

        var pipeline = new OverlapProbe(graph, async ct =>
        {
            int now = Interlocked.Increment(ref running);
            int observed = Volatile.Read(ref peak);
            while (now > observed && Interlocked.CompareExchange(ref peak, now, observed) != observed)
                observed = Volatile.Read(ref peak);

            await Task.Delay(60, ct);
            Interlocked.Decrement(ref running);
        });

        var outcome = await new BuildExecutor().RunAsync(pipeline, new BuildContext(Request()));

        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Issues));
        Assert.True(peak > 1, $"independent stages did not overlap, peak was {peak}");
    }

    // An exclusive stage still has to run alone even when a sibling is ready.
    [Fact]
    public async Task ExclusiveStage_DoesNotOverlapASibling()
    {
        var graph = new StageGraph(
        [
            new StageNode { Stage = BuildStage.Validate },
            new StageNode { Stage = BuildStage.CompilePlayer, DependsOn = [BuildStage.Validate], Resources = StageResources.Exclusive },
            new StageNode { Stage = BuildStage.CopyPlugins, DependsOn = [BuildStage.Validate] },
        ]);

        int running = 0;
        int peak = 0;

        var pipeline = new OverlapProbe(graph, async ct =>
        {
            int now = Interlocked.Increment(ref running);
            int observed = Volatile.Read(ref peak);
            while (now > observed && Interlocked.CompareExchange(ref peak, now, observed) != observed)
                observed = Volatile.Read(ref peak);

            await Task.Delay(40, ct);
            Interlocked.Decrement(ref running);
        });

        var outcome = await new BuildExecutor().RunAsync(pipeline, new BuildContext(Request()));

        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Issues));
        Assert.Equal(1, peak);
    }

    private sealed class OverlapProbe(StageGraph graph, Func<CancellationToken, Task> body) : StagedTestPipeline
    {
        public override StageGraph CreateStageGraph(BuildRequest request) => graph;
        public override async IAsyncEnumerable<BuildOperation> PlanStageAsync(
            BuildStage stage, IBuildContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            if (stage == BuildStage.Validate) yield break;
            yield return new BuildOperation.Custom(new Handler((_, token) => body(token)), stage.Id);
        }
    }

    private sealed class ScriptedWrites(StageGraph graph, string output, int count) : StagedTestPipeline
    {
        public override StageGraph CreateStageGraph(BuildRequest request) => graph;
        public override async IAsyncEnumerable<BuildOperation> PlanStageAsync(
            BuildStage stage, IBuildContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            for (int i = 0; i < count; i++)
            {
                int index = i;
                yield return new BuildOperation.WriteFile(
                    _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes($"file {index}"))),
                    Path.Combine(output, $"file{index}.bin"));
            }
        }
    }

    // ---------------------------------------------------------------- TargetRegistryTests

    [Fact]
    public void BuiltInTargets_AreRegistered()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();

        Assert.True(registry.TryGet("windows-x64", out _));
        Assert.True(registry.TryGet("linux-x64", out _));
        Assert.True(registry.TryGet("macos-arm64", out _));
    }

    // Ids are matched case insensitively because they travel through settings files and command lines.
    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();
        Assert.True(registry.TryGet("Windows-X64", out var target));
        Assert.Equal("windows-x64", target!.Id);
    }

    [Fact]
    public void UnknownId_ThrowsNamingIt()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();
        var ex = Assert.Throws<KeyNotFoundException>(() => registry.Get("nintendo-something"));
        Assert.Contains("nintendo-something", ex.Message);
    }

    // The whole reason targets are data: a platform under NDA registers from a private assembly, and
    // nothing about it appears in this repository.
    [Fact]
    public void OutOfTreeProvider_CanAddATarget()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();
        registry.RegisterFrom(new PrivateConsoleProvider());

        Assert.True(registry.TryGet("private-console", out var target));
        Assert.Equal("console", target!.Family);
        Assert.False(target.Capabilities.Has(TargetFlags.Jit));
        Assert.Equal(["vendor_swizzled"], target.Capabilities.TextureFormats);
    }

    // Every build indexes into this list, so an empty one has to be refused where it is registered
    // rather than throwing an index error out of the middle of a build.
    [Fact]
    public void TargetWithNoRuntimeIdentifier_IsRefused()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();

        var ex = Assert.Throws<ArgumentException>(() => registry.Register(BuiltInTargets.WindowsX64 with
        {
            Id = "broken",
            RuntimeIdentifiers = [],
        }));

        Assert.Contains("broken", ex.Message);
    }

    // Targets come from scanned types, so a reload has to discard them like every other registry does.
    [Fact]
    public void ResetToBuiltIns_DropsWhatAProviderAdded()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();
        registry.RegisterFrom(new PrivateConsoleProvider());

        registry.ResetToBuiltIns();

        Assert.False(registry.TryGet("private-console", out _));
        Assert.True(registry.TryGet("windows-x64", out _));
    }

    // A provider's later targets must not be lost because an earlier one was malformed.
    [Fact]
    public void OneBadTarget_DoesNotDropTheRest()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();

        registry.RegisterFrom(new PartlyBrokenProvider());

        Assert.False(registry.TryGet("broken", out _));
        Assert.True(registry.TryGet("after-the-broken-one", out _));
    }

    private sealed class PartlyBrokenProvider : IBuildTargetProvider
    {
        public IEnumerable<PlatformTarget> GetTargets()
        {
            yield return BuiltInTargets.WindowsX64 with { Id = "broken", RuntimeIdentifiers = [] };
            yield return BuiltInTargets.WindowsX64 with { Id = "after-the-broken-one" };
        }
    }

    // Android bundles several ABIs into one artifact and macOS universal merges two architectures, so a
    // target that could only name one runtime identifier could express neither.
    [Fact]
    public void ATargetCanCarryMoreThanOneRuntimeIdentifier()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();
        var universal = registry.Get("macos-universal");

        Assert.Equal(["osx-x64", "osx-arm64"], universal.RuntimeIdentifiers);
    }

    // arm64 desktop is the gap the old three value enum could not express at all.
    [Fact]
    public void Arm64DesktopTargets_Exist()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();

        Assert.Equal(["win-arm64"], registry.Get("windows-arm64").RuntimeIdentifiers);
        Assert.Equal(["linux-arm64"], registry.Get("linux-arm64").RuntimeIdentifiers);
        Assert.Equal(["osx-arm64"], registry.Get("macos-arm64").RuntimeIdentifiers);
    }

    [Fact]
    public void Registering_SameId_Replaces()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();
        int before = registry.All.Count;

        registry.Register(BuiltInTargets.WindowsX64 with { DisplayName = "Renamed" });

        Assert.Equal(before, registry.All.Count);
        Assert.Equal("Renamed", registry.Get("windows-x64").DisplayName);
    }

    // A menu built from this has to look the same on every machine.
    [Fact]
    public void All_IsInAStableOrder()
    {
        var first = TargetRegistry.CreateWithBuiltIns().All.Select(t => t.Id).ToList();
        var second = TargetRegistry.CreateWithBuiltIns().All.Select(t => t.Id).ToList();

        Assert.Equal(first, second);
        Assert.Equal(first.OrderBy(id => id, StringComparer.Ordinal), first);
    }

    [Fact]
    public void EveryDesktopTarget_HasARuntimeIdentifier()
    {
        foreach (var target in TargetRegistry.Shared.ByFamily(BuiltInTargets.DesktopFamily))
            Assert.NotEmpty(target.RuntimeIdentifiers);
    }

    // The profile's platform enum has to resolve to a registered target, or the shim is broken.
    [Fact]
    public void ProfilePlatform_ResolvesToARegisteredTarget()
    {
        var profile = new DesktopBuildProfile();

        foreach (var platform in Enum.GetValues<Prowl.Editor.Projects.Settings.BuildTarget>())
        {
            profile.Platform = platform;

            Assert.True(TargetRegistry.Shared.TryGet(profile.TargetId, out _), $"{platform} maps to an unregistered target.");
            Assert.NotEmpty(profile.RuntimeIdentifier);
        }
    }

    // How the Build window finds a pipeline: the profile names its type and constructs it directly.
    [Fact]
    public void ProfilesPipelineType_IsConstructible()
    {
        var type = new DesktopBuildProfile().GetPipelineType();

        Assert.Equal(typeof(DesktopBuildPipeline), type);
        Assert.IsType<DesktopBuildPipeline>(Activator.CreateInstance(type));
    }

    [Fact]
    public void ByFamily_FiltersWithoutCaringAboutCase()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();
        var desktop = registry.ByFamily("DESKTOP");

        Assert.Equal(7, desktop.Count);
        Assert.All(desktop, t => Assert.Equal("desktop", t.Family));
    }

    private sealed class PrivateConsoleProvider : IBuildTargetProvider
    {
        public IEnumerable<PlatformTarget> GetTargets()
        {
            yield return new PlatformTarget
            {
                Id = "private-console",
                DisplayName = "Private Console",
                Family = "console",
                RuntimeIdentifiers = ["console-arm64"],
                Capabilities = new TargetCapabilities
                {
                    // A format the engine has never heard of, which is the point of registered strings.
                    TextureFormats = ["vendor_swizzled"],
                    GraphicsApis = ["vendor_gfx"],
                    Flags = new HashSet<string> { TargetFlags.Threads, TargetFlags.Filesystem },
                },
            };
        }
    }

    // ---------------------------------------------------------------- ChunkPlannerTests

    /// <summary>
    /// The real graph, built inline. It needs no project or database, so there is nothing to gain from a
    /// stand-in, and using it means these tests exercise the traversal the build actually runs.
    /// </summary>
    private sealed class Graph
    {
        public DependencyGraph Dependencies { get; } = new();

        public void DependsOn(Guid asset, params Guid[] dependencies)
            => Dependencies.SetDependencies(asset, dependencies);
    }

    private static Guid G(int n) => new($"{n:D8}-0000-0000-0000-000000000000");

    private static AssetChunk Chunk(IReadOnlyList<AssetChunk> chunks, string name)
        => Assert.Single(chunks, c => c.Name == name);

    private static readonly Dictionary<Guid, IReadOnlyList<Guid>> NoSubAssets = [];

    private static IReadOnlyList<AssetChunk> Plan(Graph source, IReadOnlyList<Guid> scenes,
        IReadOnlyCollection<Guid> resources, HashSet<Guid> shipped,
        Dictionary<Guid, IReadOnlyList<Guid>>? subAssets = null)
        => ChunkPlanner.Plan(source.Dependencies, scenes, resources, shipped, subAssets ?? NoSubAssets);

    // An asset only one scene needs ships with that scene, which is what makes streaming possible.
    [Fact]
    public void AssetReachedByOneScene_JoinsThatScene()
    {
        var source = new Graph();
        Guid sceneA = G(1), sceneB = G(2), onlyA = G(10), onlyB = G(20);
        source.DependsOn(sceneA, onlyA);
        source.DependsOn(sceneB, onlyB);

        var chunks = Plan(source, [sceneA, sceneB], [], new HashSet<Guid> { sceneA, sceneB, onlyA, onlyB });

        Assert.Contains(onlyA, Chunk(chunks, ChunkPlanner.SceneChunkName(sceneA)).Assets);
        Assert.Contains(onlyB, Chunk(chunks, ChunkPlanner.SceneChunkName(sceneB)).Assets);
    }

    // Duplicating a shared asset into every scene's archive is how a build doubles in size.
    [Fact]
    public void AssetReachedByTwoScenes_GoesToShared()
    {
        var source = new Graph();
        Guid sceneA = G(1), sceneB = G(2), shared = G(50);
        source.DependsOn(sceneA, shared);
        source.DependsOn(sceneB, shared);

        var chunks = Plan(source, [sceneA, sceneB], [], new HashSet<Guid> { sceneA, sceneB, shared });

        Assert.Contains(shared, Chunk(chunks, ChunkPlanner.SharedChunk).Assets);
        Assert.DoesNotContain(shared, Chunk(chunks, ChunkPlanner.SceneChunkName(sceneA)).Assets);
    }

    [Fact]
    public void ResourcesAreTheirOwnEntryPoint()
    {
        var source = new Graph();
        Guid scene = G(1), resource = G(30), resourceDependency = G(31);
        source.DependsOn(resource, resourceDependency);

        var chunks = Plan(source, [scene], [resource],
            new HashSet<Guid> { scene, resource, resourceDependency });

        var resources = Chunk(chunks, ChunkPlanner.ResourcesChunk);
        Assert.Contains(resource, resources.Assets);
        Assert.Contains(resourceDependency, resources.Assets);
    }

    // A resource a scene also uses is shared, not duplicated.
    [Fact]
    public void AssetUsedByBothASceneAndResources_GoesToShared()
    {
        var source = new Graph();
        Guid scene = G(1), resource = G(30), both = G(60);
        source.DependsOn(scene, both);
        source.DependsOn(resource, both);

        var chunks = Plan(source, [scene], [resource],
            new HashSet<Guid> { scene, resource, both });

        Assert.Contains(both, Chunk(chunks, ChunkPlanner.SharedChunk).Assets);
    }

    // A sprite reaches no scene through the dependency graph, so without the sub-asset map it lands in
    // the common chunk while the texture it lives inside ships with the scene.
    [Fact]
    public void SubAssetOfAReachedParent_JoinsThatParentsChunk()
    {
        var source = new Graph();
        Guid scene = G(1), texture = G(40), sprite = G(41);
        source.DependsOn(scene, texture);

        var chunks = Plan(source, [scene], [], new HashSet<Guid> { scene, texture, sprite },
            new Dictionary<Guid, IReadOnlyList<Guid>> { [texture] = [sprite] });

        Assert.Contains(sprite, Chunk(chunks, ChunkPlanner.SceneChunkName(scene)).Assets);
    }

    // And what a sub-asset itself references has to come along, or the chunk ships a broken sprite.
    [Fact]
    public void WhatASubAssetReferences_JoinsItToo()
    {
        var source = new Graph();
        Guid scene = G(1), texture = G(40), sprite = G(41), material = G(42);
        source.DependsOn(scene, texture);
        source.DependsOn(sprite, material);

        var chunks = Plan(source, [scene], [], new HashSet<Guid> { scene, texture, sprite, material },
            new Dictionary<Guid, IReadOnlyList<Guid>> { [texture] = [sprite] });

        Assert.Contains(material, Chunk(chunks, ChunkPlanner.SceneChunkName(scene)).Assets);
    }

    // Shipping every asset means some are reachable from nothing, and they still have to go somewhere.
    [Fact]
    public void UnreachableShippedAsset_GoesToCommon()
    {
        var source = new Graph();
        Guid scene = G(1), orphan = G(99);

        var chunks = Plan(source, [scene], [], new HashSet<Guid> { scene, orphan });

        Assert.Contains(orphan, Chunk(chunks, ChunkPlanner.CommonChunk).Assets);
    }

    // Anything not shipping must not be dragged in by being a dependency of something that is.
    [Fact]
    public void DependencyNotInTheShippedSet_IsExcluded()
    {
        var source = new Graph();
        Guid scene = G(1), editorOnly = G(70);
        source.DependsOn(scene, editorOnly);

        var chunks = Plan(source, [scene], [], new HashSet<Guid> { scene });

        Assert.DoesNotContain(editorOnly, chunks.SelectMany(c => c.Assets));
    }

    // An archive whose contents shuffle between builds is a patch shipping bytes nobody changed.
    [Fact]
    public void Output_IsOrderedAndStable()
    {
        var source = new Graph();
        Guid sceneA = G(1), sceneB = G(2);
        source.DependsOn(sceneA, G(11), G(12), G(13));
        source.DependsOn(sceneB, G(21), G(22));

        var shipped = new HashSet<Guid> { sceneA, sceneB, G(11), G(12), G(13), G(21), G(22) };

        var first = Plan(source, [sceneA, sceneB], [], shipped);
        var second = Plan(source, [sceneA, sceneB], [], shipped);

        Assert.Equal(first.Select(c => c.Name), second.Select(c => c.Name));
        Assert.Equal(first.Select(c => c.Assets), second.Select(c => c.Assets));

        foreach (var chunk in first)
            Assert.Equal(chunk.Assets.OrderBy(g => g), chunk.Assets);
    }

    [Fact]
    public void EveryShippedAsset_LandsInExactlyOneChunk()
    {
        var source = new Graph();
        Guid sceneA = G(1), sceneB = G(2), shared = G(50);
        source.DependsOn(sceneA, shared, G(11));
        source.DependsOn(sceneB, shared, G(21));

        var shipped = new HashSet<Guid> { sceneA, sceneB, shared, G(11), G(21), G(99) };
        var chunks = Plan(source, [sceneA, sceneB], [], shipped);

        var placed = chunks.SelectMany(c => c.Assets).ToList();

        Assert.Equal(shipped.Count, placed.Count);
        Assert.Equal(shipped.OrderBy(g => g), placed.OrderBy(g => g));
    }

    // ---------------------------------------------------------------- VariantCacheTests

    private LocalVariantCache Cache() => new(Path.Combine(_root, "cache"));

    private static VariantKey Key(string hash = "abcdef", string target = "windows-x64", int version = 1)
        => new(hash, target, "test-processor", version);

    private static Stream Bytes(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task WriteThenOpen_RoundTrips()
    {
        var cache = Cache();
        await cache.WriteAsync(Key(), Bytes("payload"));

        Assert.True(await cache.ExistsAsync(Key()));

        await using var stream = await cache.OpenAsync(Key());
        Assert.NotNull(stream);
        Assert.Equal("payload", new StreamReader(stream!).ReadToEnd());
    }

    // A cache keyed by content grows forever otherwise: every edit leaves the previous variant behind.
    [Fact]
    public async Task Prune_KeepsWhatFitsAndDiscardsTheRest()
    {
        var cache = new LocalVariantCache(Path.Combine(_root, "cache"), maxBytes: 20);

        await cache.WriteAsync(Key("aaaa"), Bytes("0123456789"));
        await cache.WriteAsync(Key("bbbb"), Bytes("0123456789"));
        await cache.WriteAsync(Key("cccc"), Bytes("0123456789"));

        Assert.Equal(1, await cache.PruneAsync());
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, "cache"), "*", SearchOption.AllDirectories).Length);
    }

    // An asset that never changes is written once and used by every build. Going by age of production
    // would make it the first thing discarded, so reading an entry has to be what protects it.
    [Fact]
    public async Task Prune_KeepsTheEntryReadMostRecently()
    {
        string root = Path.Combine(_root, "cache");
        var cache = new LocalVariantCache(root, maxBytes: 10);

        await cache.WriteAsync(Key("keep"), Bytes("0123456789"));
        await cache.WriteAsync(Key("drop"), Bytes("0123456789"));

        // Both aged deliberately rather than by waiting, so the only thing separating them is the read
        // below and the test does not depend on how coarse the filesystem's timestamps are.
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-10));

        Assert.True(await cache.ExistsAsync(Key("keep")));

        await cache.PruneAsync();

        Assert.True(await cache.ExistsAsync(Key("keep")));
        Assert.False(await cache.ExistsAsync(Key("drop")));
    }

    [Fact]
    public async Task Prune_DiscardsAnythingOlderThanTheAgeLimit()
    {
        var cache = new LocalVariantCache(Path.Combine(_root, "cache"), maxAge: TimeSpan.Zero);

        await cache.WriteAsync(Key(), Bytes("payload"));

        Assert.Equal(1, await cache.PruneAsync());
        Assert.False(await cache.ExistsAsync(Key()));
    }

    [Fact]
    public async Task Prune_OnAnEmptyCache_DoesNothing()
        => Assert.Equal(0, await Cache().PruneAsync());

    [Fact]
    public async Task Miss_ReturnsNull()
    {
        var cache = Cache();
        Assert.False(await cache.ExistsAsync(Key()));
        Assert.Null(await cache.OpenAsync(Key()));
    }

    // Every part of the key has to actually discriminate, or a stale entry ships.
    [Theory]
    [InlineData("different-hash", "windows-x64", 1)]
    [InlineData("abcdef", "android-arm64", 1)]
    [InlineData("abcdef", "windows-x64", 2)]
    public async Task ChangingAnyPartOfTheKey_Misses(string hash, string target, int version)
    {
        var cache = Cache();
        await cache.WriteAsync(Key(), Bytes("original"));

        Assert.False(await cache.ExistsAsync(Key(hash, target, version)));
    }

    // Two targets must not evict each other, so several platforms can be built without a full rebuild.
    [Fact]
    public async Task TwoTargets_Coexist()
    {
        var cache = Cache();
        await cache.WriteAsync(Key(target: "windows-x64"), Bytes("windows"));
        await cache.WriteAsync(Key(target: "android-arm64"), Bytes("android"));

        await using var windows = await cache.OpenAsync(Key(target: "windows-x64"));
        await using var android = await cache.OpenAsync(Key(target: "android-arm64"));

        Assert.Equal("windows", new StreamReader(windows!).ReadToEnd());
        Assert.Equal("android", new StreamReader(android!).ReadToEnd());
    }

    // A build killed mid-write must not leave something a later build serves as a hit.
    [Fact]
    public async Task FailedWrite_LeavesNoEntryAndNoPartial()
    {
        var cache = Cache();

        await Assert.ThrowsAsync<IOException>(async () =>
            await cache.WriteAsync(Key(), new ThrowingStream()));

        Assert.False(await cache.ExistsAsync(Key()));
        Assert.Empty(Directory.Exists(Path.Combine(_root, "cache"))
            ? Directory.GetFiles(Path.Combine(_root, "cache"), "*.partial", SearchOption.AllDirectories)
            : []);
    }

    // The key is the content, not the path. Two assets with identical bytes share one entry, which is
    // also what makes a renamed or moved asset still hit.
    [Fact]
    public async Task TwoAssetsWithIdenticalContent_ShareOneCacheEntry()
    {
        var processor = new CountingProcessor("bc7");
        var resolver = new AssetVariantResolver([processor], new LocalVariantCache(Path.Combine(_root, "cache")));
        var target = TargetPreferring("bc7");

        var first = await resolver.ResolveAsync(Asset("Textures/A.png"), WriteAsset("A.png", "same pixels"), target);
        var second = await resolver.ResolveAsync(Asset("Textures/B.png"), WriteAsset("B.png", "same pixels"), target);

        Assert.Equal(VariantOrigin.Processed, first.Origin);
        Assert.Equal(VariantOrigin.Cached, second.Origin);
        Assert.Equal(1, processor.Calls);
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("disk went away");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ---------------------------------------------------------------- AssetVariantResolverTests

    private string WriteAsset(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static AssetEntry Asset(string path = "Textures/Grass.png") => new()
    {
        Guid = Guid.NewGuid(),
        Path = path,
        SubAssets = [],
    };

    private static PlatformTarget TargetPreferring(params string[] formats) => new()
    {
        Id = "test-target",
        DisplayName = "Test",
        Family = "desktop",
        RuntimeIdentifiers = ["win-x64"],
        Capabilities = new TargetCapabilities { TextureFormats = formats },
    };

    /// <summary>Counts its own calls, which is how the tests observe a cache hit.</summary>
    private sealed class CountingProcessor(string format, int version = 1) : IAssetVariantProcessor
    {
        public int Calls;
        public string Id => "counting";
        public int Version { get; } = version;
        public string Format { get; } = format;
        public bool AppliesTo(AssetEntry asset) => asset.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

        public async Task ProcessAsync(AssetEntry asset, Stream source, Stream destination, PlatformTarget target, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            byte[] payload = Encoding.UTF8.GetBytes($"{Format}:{new StreamReader(source).ReadToEnd()}");
            await destination.WriteAsync(payload, ct);
        }
    }

    // Nothing registered means today's behaviour: the imported form ships untouched.
    [Fact]
    public async Task NoProcessor_ShipsTheUniversalForm()
    {
        var resolver = new AssetVariantResolver([], new LocalVariantCache(Path.Combine(_root, "cache")));
        string path = WriteAsset("Grass.png", "pixels");

        var resolved = await resolver.ResolveAsync(Asset(), path, TargetPreferring("bc7"));

        Assert.Equal(VariantOrigin.Universal, resolved.Origin);
        Assert.Null(resolved.Key);
    }

    // The whole point: unchanged content and processor means the work is not redone.
    [Fact]
    public async Task SecondResolve_HitsTheCacheAndDoesNotReprocess()
    {
        var processor = new CountingProcessor("bc7");
        var resolver = new AssetVariantResolver([processor], new LocalVariantCache(Path.Combine(_root, "cache")));
        string path = WriteAsset("Grass.png", "pixels");
        var target = TargetPreferring("bc7");

        var first = await resolver.ResolveAsync(Asset(), path, target);
        var second = await resolver.ResolveAsync(Asset(), path, target);

        Assert.Equal(VariantOrigin.Processed, first.Origin);
        Assert.Equal(VariantOrigin.Cached, second.Origin);
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task EditingTheAsset_Reprocesses()
    {
        var processor = new CountingProcessor("bc7");
        var resolver = new AssetVariantResolver([processor], new LocalVariantCache(Path.Combine(_root, "cache")));
        string path = WriteAsset("Grass.png", "pixels");
        var target = TargetPreferring("bc7");

        await resolver.ResolveAsync(Asset(), path, target);
        File.WriteAllText(path, "different pixels");
        var again = await resolver.ResolveAsync(Asset(), path, target);

        Assert.Equal(VariantOrigin.Processed, again.Origin);
        Assert.Equal(2, processor.Calls);
    }

    // A fixed compressor must invalidate its own output, and only its own.
    [Fact]
    public async Task BumpingTheProcessorVersion_Reprocesses()
    {
        string cacheRoot = Path.Combine(_root, "cache");
        string path = WriteAsset("Grass.png", "pixels");
        var target = TargetPreferring("bc7");

        var v1 = new CountingProcessor("bc7", version: 1);
        await new AssetVariantResolver([v1], new LocalVariantCache(cacheRoot)).ResolveAsync(Asset(), path, target);

        var v2 = new CountingProcessor("bc7", version: 2);
        var resolved = await new AssetVariantResolver([v2], new LocalVariantCache(cacheRoot)).ResolveAsync(Asset(), path, target);

        Assert.Equal(VariantOrigin.Processed, resolved.Origin);
        Assert.Equal(1, v2.Calls);
    }

    // Selection follows what the target asks for, so a processor needs no knowledge of platforms.
    [Fact]
    public async Task TargetPreference_ChoosesAmongProcessors()
    {
        var astc = new CountingProcessor("astc_6x6");
        var bc7 = new CountingProcessor("bc7");
        var resolver = new AssetVariantResolver([astc, bc7], new LocalVariantCache(Path.Combine(_root, "cache")));
        string path = WriteAsset("Grass.png", "pixels");

        var chosen = resolver.SelectProcessor(Asset(), TargetPreferring("astc_6x6", "bc7"));
        Assert.Same(astc, chosen);

        var other = resolver.SelectProcessor(Asset(), TargetPreferring("bc7", "astc_6x6"));
        Assert.Same(bc7, other);
    }

    // A target that wants nothing this processor makes falls back to shipping the imported form.
    [Fact]
    public async Task NoFormatInCommon_ShipsTheUniversalForm()
    {
        var resolver = new AssetVariantResolver([new CountingProcessor("bc7")], new LocalVariantCache(Path.Combine(_root, "cache")));
        string path = WriteAsset("Grass.png", "pixels");

        var resolved = await resolver.ResolveAsync(Asset(), path, TargetPreferring("etc2"));

        Assert.Equal(VariantOrigin.Universal, resolved.Origin);
    }

    [Fact]
    public async Task ProcessorThatDoesNotApply_IsNotUsed()
    {
        var resolver = new AssetVariantResolver([new CountingProcessor("bc7")], new LocalVariantCache(Path.Combine(_root, "cache")));
        string path = WriteAsset("Level.scene", "not a texture");

        var resolved = await resolver.ResolveAsync(Asset("Scenes/Level.scene"), path, TargetPreferring("bc7"));

        Assert.Equal(VariantOrigin.Universal, resolved.Origin);
    }

    [Fact]
    public async Task Open_ReturnsTheProcessedBytes()
    {
        var resolver = new AssetVariantResolver([new CountingProcessor("bc7")], new LocalVariantCache(Path.Combine(_root, "cache")));
        string path = WriteAsset("Grass.png", "pixels");

        var resolved = await resolver.ResolveAsync(Asset(), path, TargetPreferring("bc7"));
        await using var stream = await resolver.OpenAsync(resolved);

        Assert.Equal("bc7:pixels", new StreamReader(stream).ReadToEnd());
    }

    // Two targets each get their own entry rather than one overwriting the other.
    [Fact]
    public async Task DifferentTargets_EachGetTheirOwnVariant()
    {
        var processor = new CountingProcessor("bc7");
        var resolver = new AssetVariantResolver([processor], new LocalVariantCache(Path.Combine(_root, "cache")));
        string path = WriteAsset("Grass.png", "pixels");

        await resolver.ResolveAsync(Asset(), path, TargetPreferring("bc7") with { Id = "windows-x64" });
        await resolver.ResolveAsync(Asset(), path, TargetPreferring("bc7") with { Id = "linux-x64" });

        Assert.Equal(2, processor.Calls);
    }

    // ---------------------------------------------------------------- MSBuildProjectSpecTests

    private static XDocument Render(MSBuildProjectSpec spec) => XDocument.Parse(spec.ToXml());

    private static MSBuildProjectSpec Minimal() => new() { TargetFramework = "net10.0" };

    // Whatever else changes, the output has to remain a valid project file.
    [Fact]
    public void Minimal_IsValidXmlWithAnSdk()
    {
        var doc = Render(Minimal());

        Assert.Equal("Project", doc.Root!.Name.LocalName);
        Assert.Equal("Microsoft.NET.Sdk", doc.Root.Attribute("Sdk")!.Value);
        Assert.Equal("net10.0", doc.Descendants("TargetFramework").Single().Value);
    }

    // MSBuild reads %xx as an escape, so a project under "100%Done" resolves to a path that does not exist.
    [Fact]
    public void PercentAndDollarInAPath_AreEscapedForMSBuild()
    {
        var doc = Render(Minimal() with
        {
            References = [new AssemblyRef("Prowl.Runtime", @"C:\100%Done\$(Weird)\Prowl.Runtime.dll")],
        });

        string hint = doc.Descendants("HintPath").Single().Value;

        Assert.Equal(@"C:\100%25Done\%24(Weird)\Prowl.Runtime.dll", hint);
    }

    // Semicolons separate the values in DefineConstants and RuntimeIdentifiers, so escaping them would
    // turn a list into one long define.
    [Fact]
    public void SemicolonSeparatedValues_StaySeparated()
    {
        var doc = Render(Minimal() with
        {
            RuntimeIdentifiers = ["osx-x64", "osx-arm64"],
            Properties = new Dictionary<string, string> { ["DefineConstants"] = "PROWL;PROWL_MACOS" },
        });

        Assert.Equal("osx-x64;osx-arm64", doc.Descendants("RuntimeIdentifiers").Single().Value);
        Assert.Equal("PROWL;PROWL_MACOS", doc.Descendants("DefineConstants").Single().Value);
    }

    // A property name lands in the XML as a tag, so it cannot be arbitrary text.
    [Fact]
    public void UnusablePropertyName_Throws()
    {
        var spec = Minimal() with { Properties = new Dictionary<string, string> { ["Not A Name"] = "x" } };

        Assert.Throws<InvalidOperationException>(() => spec.ToXml());
    }

    // An unchanged build has to produce an unchanged file whatever order the caller collected things in.
    [Fact]
    public void ItemGroups_AreOrderedRegardlessOfInputOrder()
    {
        var forwards = Minimal() with { Packages = [new PackageRef("A.Pkg", "1.0"), new PackageRef("B.Pkg", "2.0")] };
        var backwards = Minimal() with { Packages = [new PackageRef("B.Pkg", "2.0"), new PackageRef("A.Pkg", "1.0")] };

        Assert.Equal(forwards.ToXml(), backwards.ToXml());
    }

    // One identifier is the singular property; several is the plural one. The SDK treats them
    // differently, and multi ABI targets depend on getting the plural form.
    [Fact]
    public void SingleRuntimeIdentifier_EmitsTheSingularProperty()
    {
        var doc = Render(Minimal() with { RuntimeIdentifiers = ["win-x64"] });

        Assert.Equal("win-x64", doc.Descendants("RuntimeIdentifier").Single().Value);
        Assert.Empty(doc.Descendants("RuntimeIdentifiers"));
    }

    [Fact]
    public void SeveralRuntimeIdentifiers_EmitThePluralProperty()
    {
        var doc = Render(Minimal() with { RuntimeIdentifiers = ["android-arm64", "android-x64"] });

        Assert.Equal("android-arm64;android-x64", doc.Descendants("RuntimeIdentifiers").Single().Value);
        Assert.Empty(doc.Descendants("RuntimeIdentifier"));
    }

    [Fact]
    public void NoRuntimeIdentifier_EmitsNeither()
    {
        var doc = Render(Minimal());

        Assert.Empty(doc.Descendants("RuntimeIdentifier"));
        Assert.Empty(doc.Descendants("RuntimeIdentifiers"));
    }

    // A path with an ampersand in it is not exotic, and it makes the whole project file unparseable.
    [Fact]
    public void PathsAreEscaped()
    {
        var spec = Minimal() with
        {
            References = [new AssemblyRef("Engine", @"C:\Games\Cloak & Dagger\Engine.dll")],
        };

        var doc = Render(spec);
        Assert.Equal(@"C:\Games\Cloak & Dagger\Engine.dll", doc.Descendants("HintPath").Single().Value);
    }

    [Fact]
    public void Properties_AreEmittedAndOrdered()
    {
        var spec = Minimal() with
        {
            Properties = new Dictionary<string, string>
            {
                ["PublishTrimmed"] = "true",
                ["AssemblyName"] = "MyGame",
                ["OutputType"] = "Exe",
            },
        };

        var names = Render(spec).Descendants("PropertyGroup").Single()
            .Elements().Select(e => e.Name.LocalName).Where(n => n != "TargetFramework").ToList();

        Assert.Equal(["AssemblyName", "OutputType", "PublishTrimmed"], names);
    }

    // An identical spec has to produce an identical file, or every build looks like a change.
    [Fact]
    public void Output_IsStableAcrossDictionaryOrder()
    {
        var first = Minimal() with
        {
            Properties = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2", ["C"] = "3" },
        };
        var second = Minimal() with
        {
            Properties = new Dictionary<string, string> { ["C"] = "3", ["A"] = "1", ["B"] = "2" },
        };

        Assert.Equal(first.ToXml(), second.ToXml());
    }

    [Fact]
    public void References_CarryHintPathAndArePrivate()
    {
        var doc = Render(Minimal() with { References = [new AssemblyRef("Prowl.Runtime", @"C:\e\Prowl.Runtime.dll")] });
        var reference = doc.Descendants("Reference").Single();

        Assert.Equal("Prowl.Runtime", reference.Attribute("Include")!.Value);
        Assert.Equal("true", reference.Element("Private")!.Value);
        Assert.Equal("false", reference.Element("SpecificVersion")!.Value);
    }

    [Fact]
    public void PackagesAndCompileItems_AreEmitted()
    {
        var spec = Minimal() with
        {
            Packages = [new PackageRef("Some.Package", "1.2.3")],
            Compile = ["Program.cs"],
        };

        var doc = Render(spec);
        var package = doc.Descendants("PackageReference").Single();

        Assert.Equal("Some.Package", package.Attribute("Include")!.Value);
        Assert.Equal("1.2.3", package.Attribute("Version")!.Value);
        Assert.Equal("Program.cs", doc.Descendants("Compile").Single().Attribute("Include")!.Value);
    }

    [Fact]
    public void EmbeddedResources_CarryTheirLogicalName()
    {
        var spec = Minimal() with
        {
            EmbeddedResources = [new EmbeddedResourceRef(@"Assets\thing.asset", "Assets.thing.asset")],
        };

        var resource = Render(spec).Descendants("EmbeddedResource").Single();

        Assert.Equal(@"Assets\thing.asset", resource.Attribute("Include")!.Value);
        Assert.Equal("Assets.thing.asset", resource.Element("LogicalName")!.Value);
    }

    [Fact]
    public void EmptyCollections_EmitNoItemGroups()
    {
        Assert.Empty(Render(Minimal()).Descendants("ItemGroup"));
    }

    // ---------------------------------------------------------------- NewPlatformIntegrationTests

    // ---- everything below is what a platform author would write, outside the engine ----

    private static readonly PlatformTarget HandheldTarget = new()
    {
        Id = "handheld-arm64",
        DisplayName = "Handheld (arm64)",
        Family = "handheld",
        RuntimeIdentifiers = ["handheld-arm64", "handheld-arm"],
        AssemblyPlatform = "Handheld",
        Defines = ["PROWL_HANDHELD"],
        Capabilities = new TargetCapabilities
        {
            TextureFormats = ["vendor_astc", TextureFormats.Rgba8],
            GraphicsApis = ["vendor_gfx"],
            Flags = new HashSet<string> { TargetFlags.Threads, TargetFlags.Filesystem },
            MaxTextureSize = 2048,
        },
    };

    private sealed class HandheldProvider : IBuildTargetProvider
    {
        public IEnumerable<PlatformTarget> GetTargets() => [HandheldTarget];
    }

    /// <summary>A pipeline with a stage the engine does not define, and a packaging step of its own.</summary>
    private sealed class HandheldPipeline(string output) : BuildPipeline
    {
        private static readonly BuildStage SignCartridge = new("handheld-sign-cartridge");

        public override string DisplayName => "Handheld";
        public List<string> Ran { get; } = [];

        public override Task<BuildResult> BuildAsync(string projectPath, BuildSettings settings,
            string? outputDirectory = null, BuildProgress? progress = null, CancellationToken cancellation = default)
            => throw new NotSupportedException("Driven through the executor directly.");

        public override string GetExecutablePath(string outputPath, BuildSettings settings)
            => Path.Combine(outputPath, "game.cartridge");

        public override StageGraph CreateStageGraph(BuildRequest request) => new(
        [
            new StageNode { Stage = BuildStage.Validate, Resources = StageResources.Exclusive },
            new StageNode { Stage = BuildStage.ProcessAssets, DependsOn = [BuildStage.Validate], Resources = StageResources.CpuBound, OnFailure = StageFailurePolicy.CollectAndContinue },
            new StageNode { Stage = BuildStage.WriteManifest, DependsOn = [BuildStage.ProcessAssets] },
            new StageNode { Stage = SignCartridge, DependsOn = [BuildStage.WriteManifest], Resources = StageResources.Network },
        ]);

        public override async IAsyncEnumerable<BuildOperation> PlanStageAsync(
            BuildStage stage, IBuildContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;

            if (stage == BuildStage.Validate)
            {
                yield return Custom("validate", (ctx, _) =>
                {
                    Ran.Add("validate");
                    if (ctx.Request.Scenes.Count == 0)
                        throw new InvalidOperationException("A handheld build needs at least one scene.");
                    return Task.CompletedTask;
                });
            }
            else if (stage == BuildStage.ProcessAssets)
            {
                // The engine's own project spec, describing a project for a platform it has never seen.
                yield return Custom("write project", async (ctx, token) =>
                {
                    Ran.Add("process");
                    var spec = new MSBuildProjectSpec
                    {
                        TargetFramework = "net10.0-handheld",
                        RuntimeIdentifiers = HandheldTarget.RuntimeIdentifiers,
                        Properties = new Dictionary<string, string>
                        {
                            ["AssemblyName"] = ctx.Request.ProjectName,
                            ["DefineConstants"] = string.Join(';', HandheldTarget.Defines),
                        },
                        Compile = ["Program.cs"],
                    };

                    await File.WriteAllTextAsync(Path.Combine(output, "Game.csproj"), spec.ToXml(), token);
                });
            }
            else if (stage == BuildStage.WriteManifest)
            {
                yield return new BuildOperation.WriteFile(
                    _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("cartridge"))),
                    Path.Combine(output, "cartridge.manifest"));
            }
            else if (stage == SignCartridge)
            {
                yield return Custom("sign", (_, _) => { Ran.Add("sign"); return Task.CompletedTask; });
            }
        }

        private static BuildOperation Custom(string what, Func<IBuildContext, CancellationToken, Task> body)
            => new BuildOperation.Custom(new Handler(body), what);

        private sealed class Handler(Func<IBuildContext, CancellationToken, Task> body) : IOperationHandler
        {
            public Task ExecuteAsync(IBuildContext context, CancellationToken ct) => body(context, ct);
        }
    }

    // ---- the tests ----

    /// <summary>
    /// A request pointing at this test's own temp directory. Scenes default to one, since validation
    /// rejects a build with none and most tests are not about that.
    /// </summary>
    private BuildRequest Request(params Guid[] scenes) => new()
    {
        ProjectName = "Test",
        ProjectRoot = _root,
        AssetCachePath = Path.Combine(_root, "cache"),
        TempPath = Path.Combine(_root, "temp"),
        OutputDirectory = Path.Combine(_root, "out"),
        Scenes = scenes.Length > 0 ? scenes : [Guid.NewGuid()],
        Configuration = BuildConfiguration.Release,
        Packaging = AssetPackagingMode.LooseFiles,
        DependenciesOnly = true,
    };

    /// <summary>For the validation that refuses a build with nothing to ship.</summary>
    private BuildRequest RequestWithoutScenes() => Request() with { Scenes = [] };
    [Fact]
    public async Task ANewPlatform_RegistersAndBuildsEndToEnd()
    {
        var registry = TargetRegistry.CreateWithBuiltIns();
        registry.RegisterFrom(new HandheldProvider());

        var target = registry.Get("handheld-arm64");
        var pipeline = new HandheldPipeline(_root);

        var context = new BuildContext(Request(Guid.NewGuid()));
        var outcome = await new BuildExecutor().RunAsync(pipeline, context);

        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Issues));
        Assert.Equal(["validate", "process", "sign"], pipeline.Ran);

        // Its own stage ran, its manifest was written, and its project used the plural identifier form.
        Assert.Equal("cartridge", await File.ReadAllTextAsync(Path.Combine(_root, "cartridge.manifest")));
        string csproj = await File.ReadAllTextAsync(Path.Combine(_root, "Game.csproj"));
        Assert.Contains("<RuntimeIdentifiers>handheld-arm64;handheld-arm</RuntimeIdentifiers>", csproj);
        Assert.Contains("net10.0-handheld", csproj);

        // And it did not disturb the platforms that shipped with the engine.
        Assert.True(registry.TryGet("windows-x64", out _));
        Assert.Equal(2048, target.Capabilities.MaxTextureSize);
    }

    // A platform's own validation failure has to stop the build, not be swallowed.
    [Fact]
    public async Task ItsOwnValidation_FailsTheBuild()
    {
        var pipeline = new HandheldPipeline(_root);
        var context = new BuildContext(RequestWithoutScenes());

        var outcome = await new BuildExecutor().RunAsync(pipeline, context);

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Issues, i => i.Message.Contains("at least one scene"));
        Assert.DoesNotContain("sign", pipeline.Ran);
    }

    // A processor for a format the engine has never heard of, selected by the platform's own preference.
    [Fact]
    public void ItsOwnAssetFormat_IsSelectable()
    {
        var resolver = new AssetVariantResolver([new VendorProcessor()], new LocalVariantCache(Path.Combine(_root, "cache")));

        var asset = new AssetEntry
        {
            Guid = Guid.NewGuid(),
            Path = "Textures/Grass.png",
            SubAssets = [],
        };

        Assert.NotNull(resolver.SelectProcessor(asset, HandheldTarget));
        Assert.Null(resolver.SelectProcessor(asset, BuiltInTargets.WindowsX64));
    }

    private sealed class VendorProcessor : IAssetVariantProcessor
    {
        public string Id => "vendor-astc";
        public int Version => 1;
        public string Format => "vendor_astc";
        public bool AppliesTo(AssetEntry asset) => asset.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        public Task ProcessAsync(AssetEntry asset, Stream source, Stream destination, PlatformTarget target, CancellationToken ct)
            => source.CopyToAsync(destination, ct);
    }

    // ---------------------------------------------------------------- PluginPlatformTests

    private static PluginInfo Native(string cpu = "x64") => new()
    {
        AbsolutePath = "/plugins/libthing.dylib",
        IsNative = true,
        EditorOnly = false,
        Cpu = cpu,
    };

    private static PluginInfo NativeNamed(string fileName) => new()
    {
        AbsolutePath = "/plugins/" + fileName,
        IsNative = true,
        EditorOnly = false,
    };

    // A native library's extension states its platform, so the default "any platform" cannot mean a
    // Windows .dll belongs in a Linux build.
    [Theory]
    [InlineData("thing.dll", "Windows", true)]
    [InlineData("thing.dll", "Linux", false)]
    [InlineData("thing.dll", "macOS", false)]
    [InlineData("libthing.so", "Linux", true)]
    [InlineData("libthing.so", "Windows", false)]
    [InlineData("libthing.dylib", "macOS", true)]
    [InlineData("libthing.dylib", "Windows", false)]
    public void NativePlugin_ShipsOnlyWhereItsExtensionApplies(string fileName, string platform, bool applies)
        => Assert.Equal(applies, NativeNamed(fileName).AppliesToBuild(platform));

    // A managed plugin really is any platform, and an explicit list still wins over the extension.
    [Fact]
    public void ManagedPlugin_ShipsEverywhere()
    {
        var managed = new PluginInfo { AbsolutePath = "/plugins/Managed.dll", IsNative = false, EditorOnly = false };

        Assert.True(managed.AppliesToBuild(BuildPlatforms.Linux));
        Assert.True(managed.AppliesToBuild(BuildPlatforms.MacOS));
    }

    [Fact]
    public void AnExplicitPlatformList_OverridesTheExtension()
    {
        var plugin = new PluginInfo
        {
            AbsolutePath = "/plugins/thing.dll",
            IsNative = true,
            EditorOnly = false,
            AnyPlatform = false,
            Platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { BuildPlatforms.Linux },
        };

        Assert.True(plugin.AppliesToBuild(BuildPlatforms.Linux));
        Assert.False(plugin.AppliesToBuild(BuildPlatforms.Windows));
    }

    [Fact]
    public void EditorOnlyPlugin_NeverShips()
    {
        var plugin = new PluginInfo { AbsolutePath = "/plugins/thing.dll", IsNative = true, EditorOnly = true };

        Assert.False(plugin.AppliesToBuild(BuildPlatforms.Windows));
    }

    [Theory]
    [InlineData("Windows", "win-x64")]
    [InlineData("Linux", "linux-x64")]
    [InlineData("macOS", "osx-x64")]
    // The spelling the target registry uses, which differs in case from the constant.
    [InlineData("MacOS", "osx-x64")]
    [InlineData("linux", "linux-x64")]
    public void RuntimeIdentifier_IgnoresCase(string platform, string expected)
        => Assert.Equal(expected, Native().RuntimeIdentifierFor(platform));

    [Fact]
    public void Arm64Plugin_GetsAnArm64Identifier()
        => Assert.Equal("osx-arm64", Native("arm64").RuntimeIdentifierFor(BuildPlatforms.MacOS));

    // Every desktop target's platform name has to be one the plugin copier recognises.
    [Theory]
    [InlineData("windows-x64", "win")]
    [InlineData("linux-x64", "linux")]
    [InlineData("macos-x64", "osx")]
    [InlineData("macos-arm64", "osx")]
    [InlineData("macos-universal", "osx")]
    public void EveryDesktopTarget_MapsToTheRightRuntimePrefix(string targetId, string prefix)
    {
        var target = TargetRegistry.Shared.Get(targetId);

        Assert.StartsWith(prefix + "-", Native().RuntimeIdentifierFor(target.AssemblyPlatform!));
    }

    // ---------------------------------------------------------------- PlayerSettingsContractTests

    [Theory]
    [InlineData(PlayerSettingsFiles.Physics)]
    [InlineData(PlayerSettingsFiles.Audio)]
    [InlineData(PlayerSettingsFiles.Time)]
    [InlineData(PlayerSettingsFiles.Assets)]
    [InlineData(PlayerSettingsFiles.TagsAndLayers)]
    [InlineData(PlayerSettingsFiles.Navigation)]
    public void EveryFileThePlayerReads_IsExportedByATypeOfThatName(string expected)
    {
        var entry = Assert.Single(EditorRegistries.SettingsEntries.Where(e => e.Type.Name == expected));

        Assert.True(entry.ExportToBuild, $"'{expected}' is read by the player but is not exported to builds.");
    }

    // Renaming a category's label is UI text. It must not change what the build writes.
    [Fact]
    public void TheFileNames_DoNotDependOnTheDisplayLabel()
    {
        var tags = Assert.Single(EditorRegistries.SettingsEntries.Where(e => e.Type.Name == PlayerSettingsFiles.TagsAndLayers));

        Assert.NotEqual(tags.Name, tags.Type.Name);
    }

    // ---------------------------------------------------------------- BuildTargetProviderScanTests

    public const string Family = "test-only";

    private static readonly PlatformTarget s_target = new()
    {
        Id = "test-only-target",
        DisplayName = "Test Only",
        Family = Family,
        RuntimeIdentifiers = ["test-arm64"],
        Capabilities = new TargetCapabilities
        {
            TextureFormats = [TextureFormats.Rgba8],
            GraphicsApis = ["vendor_gfx"],
            Flags = new HashSet<string>(),
        },
    };

    /// <summary>Discovered by the editor's type scan, exactly as an out of tree platform's would be.</summary>
    public sealed class Provider : IBuildTargetProvider
    {
        public IEnumerable<PlatformTarget> GetTargets() => [s_target];
    }

    [Fact]
    public void AProviderInThisAssembly_ReachesTheSharedRegistry()
    {
        EditorRegistries.Initialize();

        Assert.True(TargetRegistry.Shared.TryGet(s_target.Id, out var found),
            "Nothing scans for IBuildTargetProvider, so an out of tree platform can never register.");
        Assert.Equal(Family, found!.Family);
    }

    // ---------------------------------------------------------------- MSBuildDiagnosticsTests

    private static BuildIssue Parse(string line)
    {
        Assert.True(MSBuildDiagnostics.TryParse(line, BuildStage.CompilePlayer, out var issue), $"Did not parse: {line}");
        return issue;
    }

    [Fact]
    public void Error_WithFileLineAndColumn()
    {
        var issue = Parse(@"C:\game\Assets\Player.cs(12,34): error CS1002: ; expected [C:\game\Game.csproj]");

        Assert.Equal(BuildSeverity.Error, issue.Severity);
        Assert.Equal("CS1002", issue.Code);
        Assert.Equal("; expected", issue.Message);
        Assert.Equal(@"C:\game\Assets\Player.cs", issue.File);
        Assert.Equal(12, issue.Line);
    }

    [Fact]
    public void Warning_WithLineOnly()
    {
        var issue = Parse(@"/home/dev/Player.cs(7): warning CS0168: variable declared but never used");

        Assert.Equal(BuildSeverity.Warning, issue.Severity);
        Assert.Equal("CS0168", issue.Code);
        Assert.Equal(@"/home/dev/Player.cs", issue.File);
        Assert.Equal(7, issue.Line);
    }

    // A Windows drive letter is a colon in the middle of the origin, which is exactly what a naive split
    // on ':' gets wrong.
    [Fact]
    public void DriveLetter_DoesNotConfuseTheOrigin()
    {
        var issue = Parse(@"C:\a\b\C.cs(1,1): error CS0103: The name 'x' does not exist");

        Assert.Equal(@"C:\a\b\C.cs", issue.File);
        Assert.Equal(1, issue.Line);
        Assert.Equal("CS0103", issue.Code);
    }

    [Fact]
    public void ToolLevelError_HasNoFile()
    {
        var issue = Parse("MSBUILD : error MSB1009: Project file does not exist.");

        Assert.Equal("MSB1009", issue.Code);
        Assert.Null(issue.File);
        Assert.Null(issue.Line);
    }

    [Fact]
    public void ProjectLevelError_KeepsTheProjectAsTheFile()
    {
        var issue = Parse(@"C:\game\Game.csproj : error NU1101: Unable to find package Foo");

        Assert.Equal("NU1101", issue.Code);
        Assert.Equal(@"C:\game\Game.csproj", issue.File);
        Assert.Null(issue.Line);
    }

    // Codes are letters then digits, and some SDK ones are long.
    [Fact]
    public void LongSdkCode_Parses()
    {
        var issue = Parse(@"C:\g\G.csproj : warning NETSDK1138: The target framework is out of support");
        Assert.Equal("NETSDK1138", issue.Code);
    }

    [Theory]
    [InlineData("Build succeeded.")]
    [InlineData("  Determining projects to restore...")]
    [InlineData("")]
    [InlineData("    0 Warning(s)")]
    [InlineData("Time Elapsed 00:00:03.42")]
    // A path that merely contains the word error must not be mistaken for one.
    [InlineData(@"  Restored C:\game\error-handling\Game.csproj (in 42 ms).")]
    public void NonDiagnosticLines_AreIgnored(string line)
    {
        Assert.False(MSBuildDiagnostics.TryParse(line, BuildStage.CompilePlayer, out _));
    }

    // The old substring check treated any line containing ": error " as an error, wherever it appeared.
    [Fact]
    public void MessageMentioningErrorElsewhere_IsStillClassifiedByItsRealSeverity()
    {
        var issue = Parse(@"C:\g\G.cs(3,5): warning CS0219: assigned to 'error' but never used");

        Assert.Equal(BuildSeverity.Warning, issue.Severity);
        Assert.Equal("CS0219", issue.Code);
    }

    [Fact]
    public void Parse_DeduplicatesRepeatsAcrossProjects()
    {
        string output = string.Join('\n',
            @"C:\g\G.cs(1,1): error CS0103: nope [C:\g\A.csproj]",
            @"C:\g\G.cs(1,1): error CS0103: nope [C:\g\B.csproj]",
            @"C:\g\G.cs(2,1): error CS0104: other [C:\g\A.csproj]");

        var issues = MSBuildDiagnostics.Parse(output, BuildStage.CompilePlayer);

        Assert.Equal(2, issues.Count);
        Assert.Equal(["CS0103", "CS0104"], issues.Select(i => i.Code));
    }

    [Fact]
    public void Parse_HandlesWindowsLineEndings()
    {
        string output = "C:\\g\\G.cs(1,1): error CS0103: nope\r\nBuild FAILED.\r\n";
        var issues = MSBuildDiagnostics.Parse(output, BuildStage.CompilePlayer);

        Assert.Single(issues);
        Assert.Equal("CS0103", issues[0].Code);
    }

    // Severity words are localised, so the tool has to be pinned to English for the parse to hold.
    [Fact]
    public void InvariantEnvironment_PinsToolLanguage()
    {
        Assert.Equal("en", MSBuildDiagnostics.InvariantEnvironment["DOTNET_CLI_UI_LANGUAGE"]);
        Assert.Equal("1033", MSBuildDiagnostics.InvariantEnvironment["VSLANG"]);
    }

    // The project a diagnostic came from is captured, and has to survive into the issue.
    [Fact]
    public void ProjectIsCaptured()
    {
        var issue = Parse(@"C:\g\G.cs(1,1): error CS0103: nope [C:\g\Game.csproj]");
        Assert.Equal(@"C:\g\Game.csproj", issue.Project);
    }

    [Fact]
    public void NoProjectSuffix_LeavesProjectNull()
    {
        Assert.Null(Parse("MSBUILD : error MSB1009: Project file does not exist.").Project);
    }

    /// <summary>
    /// Fills in the editor entry points for a pipeline driven straight through <see cref="BuildExecutor"/>,
    /// so a stub below says only what its test is about.
    /// </summary>
    private abstract class StagedTestPipeline : BuildPipeline
    {
        public override string DisplayName => GetType().Name;

        public override Task<BuildResult> BuildAsync(string projectPath, BuildSettings settings,
            string? outputDirectory = null, BuildProgress? progress = null, CancellationToken cancellation = default)
            => throw new NotSupportedException();

        public override string GetExecutablePath(string outputPath, BuildSettings settings)
            => throw new NotSupportedException();
    }
}
