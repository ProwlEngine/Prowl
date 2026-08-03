// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.Editor.Build;

/// <summary>How many operations of each resource class may run at once.</summary>
public sealed record ExecutionLimits
{
    public int CpuBound { get; init; } = Environment.ProcessorCount;
    public int IoBound { get; init; } = 8;
    public int Network { get; init; } = 4;

    public int For(StageResources resources) => resources switch
    {
        StageResources.CpuBound => Math.Max(1, CpuBound),
        StageResources.IoBound => Math.Max(1, IoBound),
        StageResources.Network => Math.Max(1, Network),
        _ => 1,
    };
}

public sealed record BuildOutcome
{
    public required bool Succeeded { get; init; }
    public required IReadOnlyList<BuildIssue> Issues { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int OperationsRun { get; init; }
}

/// <summary>
/// Walks a <see cref="StageGraph"/>, planning each stage as its dependencies complete and running the
/// operations it yields.
/// </summary>
public sealed class BuildExecutor
{
    private readonly ExecutionLimits _limits;
    private readonly Action<BuildStage, int, int>? _onProgress;

    /// <param name="onProgress">
    /// Receives the stage that just finished, how many stages are done and how many there are. Stages
    /// rather than operations, because that is the only count known before the work is planned.
    /// </param>
    public BuildExecutor(ExecutionLimits? limits = null, Action<BuildStage, int, int>? onProgress = null)
    {
        _limits = limits ?? new ExecutionLimits();
        _onProgress = onProgress;
    }

    public async Task<BuildOutcome> RunAsync(
        BuildPipeline pipeline, BuildContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();
        var graph = pipeline.CreateStageGraph(context.Request);

        var completed = new HashSet<BuildStage>();
        int operationsRun = 0;
        bool aborted = false;

        while (!aborted && completed.Count < graph.Nodes.Count)
        {
            var ready = graph.Ready(completed).ToList();
            if (ready.Count == 0) break; // construction proves this cannot be a cycle

            // An exclusive stage owns the machine, so it never shares a wave with anything else.
            var wave = ready.Any(n => n.Resources == StageResources.Exclusive)
                ? [ready.First(n => n.Resources == StageResources.Exclusive)]
                : ready;

            ct.ThrowIfCancellationRequested();

            // Stages in a wave share no dependency, so they overlap. This is the reason the graph is a
            // graph rather than a list: running them one after another would order them correctly and
            // waste the machine doing it.
            var results = await Task.WhenAll(wave.Select(node =>
                RunStageAsync(pipeline, node, context, ct))).ConfigureAwait(false);

            operationsRun += results.Sum(r => r.Operations);

            foreach (var node in wave)
            {
                completed.Add(node.Stage);
                _onProgress?.Invoke(node.Stage, completed.Count, graph.Nodes.Count);
            }

            // Only a fail fast stage that itself failed stops the build. Testing the shared issue list
            // instead would let an error a tolerant stage deliberately collected abort the next stage.
            aborted = wave.Zip(results).Any(pair =>
                pair.First.OnFailure == StageFailurePolicy.FailFast && pair.Second.Failed);
        }

        stopwatch.Stop();

        return new BuildOutcome
        {
            Succeeded = !aborted && !context.HasErrors,
            Issues = context.Issues.ToList(),
            Duration = stopwatch.Elapsed,
            OperationsRun = operationsRun,
        };
    }

    /// <summary>What one stage did, so the caller can tell a failed stage from a merely noisy build.</summary>
    private readonly record struct StageResult(int Operations, bool Failed);

    private async Task<StageResult> RunStageAsync(
        BuildPipeline pipeline, StageNode node, BuildContext context, CancellationToken ct)
    {
        int done = 0;
        int failed = 0;

        using var gate = new SemaphoreSlim(_limits.For(node.Resources));
        var running = new List<Task>();

        try
        {
            await PlanAndRunAsync().ConfigureAwait(false);
            await Task.WhenAll(running).ConfigureAwait(false);
        }
        finally
        {
            // Planning can throw partway, leaving operations in flight. They must be drained before the
            // gate is disposed, or they release a disposed semaphore on a background thread. Failures are
            // already reported per operation, and the original exception must be the one that surfaces.
            if (running.Count > 0)
                try { await Task.WhenAll(running).ConfigureAwait(false); } catch { }
        }

        return new StageResult(done, Volatile.Read(ref failed) > 0);

        async Task PlanAndRunAsync()
        {
        await foreach (var operation in pipeline.PlanStageAsync(node.Stage, context, ct).WithCancellation(ct))
        {
            if (node.OnFailure == StageFailurePolicy.FailFast && Volatile.Read(ref failed) > 0)
                break;

            await gate.WaitAsync(ct).ConfigureAwait(false);

            running.Add(Task.Run(async () =>
            {
                try
                {
                    await ExecuteAsync(operation, context, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Interlocked.Increment(ref failed);
                    context.Report(Issue(node.Stage, "PB1001", $"{operation.Description}: {e.Message}", BuildSeverity.Error));
                }
                finally
                {
                    Interlocked.Increment(ref done);
                    gate.Release();
                }
            }, ct));
        }
        }
    }

    private static async Task ExecuteAsync(BuildOperation operation, IBuildContext context, CancellationToken ct)
    {
        switch (operation)
        {
            case BuildOperation.CopyFile copy:
                EnsureDirectory(copy.Destination);
                await AtomicAsync(copy.Destination, async (temp, token) =>
                {
                    await using var source = File.OpenRead(copy.Source);
                    await using var destination = File.Create(temp);
                    await source.CopyToAsync(destination, token).ConfigureAwait(false);
                }, ct).ConfigureAwait(false);
                break;

            case BuildOperation.WriteFile write:
                EnsureDirectory(write.Destination);
                await AtomicAsync(write.Destination, async (temp, token) =>
                {
                    await using var content = await write.Open(token).ConfigureAwait(false);
                    await using var destination = File.Create(temp);
                    await content.CopyToAsync(destination, token).ConfigureAwait(false);
                }, ct).ConfigureAwait(false);
                break;

            case BuildOperation.Custom custom:
                await custom.Handler.ExecuteAsync(context, ct).ConfigureAwait(false);
                break;

            default:
                throw new NotSupportedException($"Unknown operation '{operation.GetType().Name}'.");
        }
    }

    private static void EnsureDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// Produce into a temporary file and move it into place, so a build that dies partway leaves no half
    /// written output that a later run would mistake for finished work.
    /// </summary>
    private static async Task AtomicAsync(string destination, Func<string, CancellationToken, Task> produce, CancellationToken ct)
    {
        // Unique per writer. Operations within a stage run concurrently by contract, so a fixed suffix
        // lets two writers for the same destination truncate and delete each other's temporary file.
        string temp = $"{destination}.{Guid.NewGuid():N}.partial";

        try
        {
            await produce(temp, ct).ConfigureAwait(false);
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                try { File.Delete(temp); } catch { }
        }
    }

    private static BuildIssue Issue(BuildStage stage, string code, string message, BuildSeverity severity)
        => new() { Severity = severity, Code = code, Message = message, Stage = stage };
}
