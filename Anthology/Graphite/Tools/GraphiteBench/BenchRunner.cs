using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Graphite;

namespace Prowl.Graphite.Bench;

/// <summary>Tick totals accumulated by the passes of one measured repetition.</summary>
public sealed class BenchStats
{
    public long RecordTicks;
    public long SubmitTicks;

    public void Reset()
    {
        RecordTicks = 0;
        SubmitTicks = 0;
    }
}

public sealed record BenchResult(
    string Name,
    int Passes,
    int DrawsPerPass,
    int Frames,
    int Repetitions,
    double RecordNsPerDraw,
    double BestRecordNsPerDraw,
    double RecordUsPerFrame,
    double SubmitUsPerFrame,
    double OtherUsPerFrame,
    double AllocBytesPerFrame,
    double DrawsPerFrame,
    double SetBindsPerFrame,
    double BoundSetsPerFrame,
    double PipelineSwitchesPerFrame,
    double SubmitsPerFrame);

public static class BenchRunner
{
    private static readonly double NsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Runs one scenario and reports it.
    /// </summary>
    public static BenchResult Run(
        GraphicsDevice gd,
        BenchScene scene,
        BenchProfiler profiler,
        string name,
        int passes,
        int drawsPerPass,
        int warmupFrames,
        int frames,
        int repetitions,
        Action<CommandBuffer, int> record)
    {
        BenchStats stats = new();
        using BenchPipeline pipeline = new(scene, passes, record, stats);
        BenchView[] views = [new BenchView()];

        for (int i = 0; i < warmupFrames; i++)
            gd.DispatchGraph(pipeline, views);

        gd.WaitForIdle();

        double totalDraws = (double)frames * passes * drawsPerPass;
        List<double> recordNsPerDraw = new(repetitions);
        List<double> recordUs = new(repetitions);
        List<double> submitUs = new(repetitions);
        List<double> otherUs = new(repetitions);
        List<double> allocBytes = new(repetitions);
        List<double> draws = new(repetitions);
        List<double> setBinds = new(repetitions);
        List<double> boundSets = new(repetitions);
        List<double> pipelineSwitches = new(repetitions);
        List<double> submits = new(repetitions);

        for (int rep = 0; rep < repetitions; rep++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            stats.Reset();
            profiler.Reset();

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();

            for (int i = 0; i < frames; i++)
                gd.DispatchGraph(pipeline, views);

            long elapsed = Stopwatch.GetTimestamp() - start;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            gd.WaitForIdle();

            double recordNs = stats.RecordTicks * NsPerTick;
            double submitNs = stats.SubmitTicks * NsPerTick;
            double otherNs = Math.Max(0.0, elapsed * NsPerTick - recordNs - submitNs);

            recordNsPerDraw.Add(totalDraws > 0 ? recordNs / totalDraws : 0.0);
            recordUs.Add(recordNs / frames / 1000.0);
            submitUs.Add(submitNs / frames / 1000.0);
            otherUs.Add(otherNs / frames / 1000.0);
            allocBytes.Add(allocated / (double)frames);
            draws.Add(profiler.Draws / (double)frames);
            setBinds.Add(profiler.SetBinds / (double)frames);
            boundSets.Add(profiler.BoundSets / (double)frames);
            pipelineSwitches.Add(profiler.PipelineSwitches / (double)frames);
            submits.Add(profiler.Submits / (double)frames);
        }

        return new BenchResult(
            name,
            passes,
            drawsPerPass,
            frames,
            repetitions,
            RecordNsPerDraw: Median(recordNsPerDraw),
            BestRecordNsPerDraw: Min(recordNsPerDraw),
            RecordUsPerFrame: Median(recordUs),
            SubmitUsPerFrame: Median(submitUs),
            OtherUsPerFrame: Median(otherUs),
            AllocBytesPerFrame: Median(allocBytes),
            DrawsPerFrame: Median(draws),
            SetBindsPerFrame: Median(setBinds),
            BoundSetsPerFrame: Median(boundSets),
            PipelineSwitchesPerFrame: Median(pipelineSwitches),
            SubmitsPerFrame: Median(submits));
    }

    private static double Median(List<double> samples)
    {
        double[] sorted = samples.ToArray();
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return (sorted.Length & 1) != 0 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5;
    }

    private static double Min(List<double> samples)
    {
        double best = double.MaxValue;
        foreach (double sample in samples)
            best = Math.Min(best, sample);

        return best;
    }
}
