using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Drives the four phases end-to-end: chart a region into pieces, flatten each, validate the
/// resulting UVs, and either accept, re-flatten a subset, or re-chart from scratch.
/// </summary>
internal static class UnwrapPipeline
{
    // Lloyd loop tuning. Kept as constants the tuner could lift later if needed.
    private const double DiscardThreshold = 0.08;
    private const double SeedThreshold = 1.4 * DiscardThreshold;
    private const int MaxLloydOuterPasses = 50;
    private const int LloydInnerIterations = 4;
    private const int OverlapRasterExtent = 512;

    // Flattener precision: coarse during chart-merging trials, fine on the final pass.
    private const double CoarseTolerance = 1e-4;
    private const double CoarseIterBudget = 1.0;
    private const double FineTolerance = 1e-8;
    private const double FineIterBudget = 5.0;

    /// <summary>Run the full pipeline on a single connected mesh region.</summary>
    public static void ProcessRegion(MeshRegion source, List<UvChart> chartOut, UnwrapOptions options, System.Action<string>? progress = null)
    {
        var stagedRegions = new List<MeshRegion>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        BuildCharts(source, stagedRegions, options, progress);
        progress?.Invoke($"  build-charts: {stagedRegions.Count} sub-regions in {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        int cursor = 0;
        int flattened = 0;
        while (cursor < stagedRegions.Count)
        {
            FlattenRegion(stagedRegions[cursor], chartOut, stagedRegions, options, progress);
            ++cursor; ++flattened;
        }
        progress?.Invoke($"  flatten: {flattened} regions -> {chartOut.Count} charts in {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Chart segmentation + a greedy merge pass: any pair of adjacent charts whose union still
    /// meets the distortion thresholds gets merged.
    /// </summary>
    private static void BuildCharts(MeshRegion source, List<MeshRegion> output, UnwrapOptions options, System.Action<string>? progress)
    {
        var builder = new ChartBuilder(source, options);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        RunLloydLoop(builder, forceSplit: false);
        progress?.Invoke($"    lloyd loop: {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        var boundaries = new List<ChartBuilder.ChartBoundary>();
        builder.CollectChartBoundaries(boundaries);
        progress?.Invoke($"    boundaries: {boundaries.Count} pairs in {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        int attempts = 0, successes = 0;
        const double mergeAcceptanceThreshold = 2.9; // "180-45 degree" cutoff
        // Hard wall-clock budget so the merge phase can't dominate the unwrap on dirty input.
        long mergeBudgetMs = options.MergeTimeBudgetMs;
        for (int bi = 0; bi < boundaries.Count; ++bi)
        {
            if (sw.ElapsedMilliseconds > mergeBudgetMs)
            {
                progress?.Invoke($"    merge: budget exceeded after {attempts} attempts; skipping remaining {boundaries.Count - bi}");
                break;
            }
            var boundary = boundaries[bi];
            if (boundary.FitError < mergeAcceptanceThreshold && boundary.First != boundary.Second)
            {
                ++attempts;
                MeshRegion mergedRegion = builder.BuildMergedRegion(boundary);
                var trialChart = new UvChart(mergedRegion);

                bool ok = TryFlatten(mergedRegion, trialChart, options, CoarseTolerance, CoarseIterBudget, finalPass: false);
                if (ok) { builder.MergeAcross(boundaries, bi); ++successes; }
            }
        }
        progress?.Invoke($"    merge: {successes}/{attempts} succeeded in {sw.ElapsedMilliseconds} ms");

        builder.EmitRegions(output);
    }

    private static void RunLloydLoop(ChartBuilder builder, bool forceSplit)
    {
        if (forceSplit) builder.InitialiseWithForcedSplit(DiscardThreshold);
        else builder.Initialise(DiscardThreshold);

        for (int it = 0; it < MaxLloydOuterPasses; ++it)
        {
            if (!builder.RunLloydPass(LloydInnerIterations, DiscardThreshold)) break;
        }
        builder.ClaimRemainingFaces(SeedThreshold, DiscardThreshold);
    }

    private static void FlattenRegion(MeshRegion source, List<UvChart> chartOut, List<MeshRegion> queue, UnwrapOptions options, System.Action<string>? progress)
    {
        var newChart = new UvChart(source);
        bool angleOk = TryFlatten(source, newChart, options, FineTolerance, FineIterBudget, finalPass: true, angleOnly: true);

        if (!angleOk)
        {
            // Too distorted — force-resegment this region without bothering to check overlaps.
            ResegmentInto(source, queue, options, progress);
            return;
        }

        // Now check for UV overlaps.
        var overlapChecker = new OverlapChecker(OverlapRasterExtent);
        var toFlatten = new List<MeshRegion>();
        var toResegment = new List<MeshRegion>();
        bool chartClean = overlapChecker.Validate(newChart, toFlatten, toResegment);

        bool metricsOk = chartClean && MeetsDistortionLimits(newChart, options);

        // Single-triangle charts are trivially fine.
        if (newChart.Region!.Triangles.Length == 1) metricsOk = true;

        if (metricsOk)
        {
            chartOut.Add(newChart);
            return;
        }

        if (chartClean && !metricsOk)
        {
            // No overlap but distortion's too high — re-segment the whole region.
            toResegment.Add(source);
            newChart.Region = null;
        }

        foreach (var rs in toResegment) ResegmentInto(rs, queue, options, progress);
        foreach (var rf in toFlatten) queue.Add(rf);
    }

    private static void ResegmentInto(MeshRegion region, List<MeshRegion> queue, UnwrapOptions options, System.Action<string>? progress)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var builder = new ChartBuilder(region, options);
        RunLloydLoop(builder, forceSplit: true);
        builder.EmitRegions(queue);
        progress?.Invoke($"    resegment {region.Triangles.Length} tris in {sw.ElapsedMilliseconds} ms");
    }

    private static bool TryFlatten(MeshRegion source, UvChart chart, UnwrapOptions options, double tolerance, double iterBudget, bool finalPass, bool angleOnly = false)
    {
        var flattener = new UvFlattener(source);
        flattener.Setup();
        flattener.BuildAngleSystem();
        flattener.SolveAngleSystem(tolerance, iterBudget);

        flattener.EvaluateAngularDistortion(out var stats);
        if (stats.MeanAngularDistortion >= options.AngleDistortionThreshold) return false;

        flattener.SolveConformalLayout(tolerance, iterBudget);

        if (finalPass) flattener.ExtractAndFinaliseUvs(chart);
        else flattener.ExtractUvs(chart);

        if (angleOnly) return true;
        return MeetsDistortionLimits(chart, options);
    }

    private static bool MeetsDistortionLimits(UvChart chart, UnwrapOptions options)
    {
        DistortionMetrics.EvaluateFull(chart, out var stats);
        return stats.MeanAngularDistortion < options.AngleDistortionThreshold
            && stats.MeanAreaDistortion < options.AreaDistortionThreshold;
    }

    /// <summary>
    /// Entry point: take a cleaned mesh through every connected component, then pack the resulting
    /// charts and write one Double2 per triangle corner into <paramref name="outputUV"/>.
    /// </summary>
    public static void Run(CleanedGeometry geometry, UnwrapOptions options, Double2[] outputUV, System.Action<string>? progress = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        progress?.Invoke($"[mesh] build half-edge graph: {geometry.VertexCount} verts, {geometry.TriangleCount} tris");
        var mesh = new HalfEdgeMesh { ProgressSink = progress is null ? null : s => progress($"  {s}") };
        mesh.Build(geometry.VertexCount, geometry.Positions, geometry.TriangleCount, geometry.Triangles,
                   creaseAngleDegrees: options.HardAngle,
                   cornerUVs: geometry.TriangleUVs);
        progress?.Invoke($"[mesh] build done in {sw.ElapsedMilliseconds} ms; {mesh.Vertices.Count} verts, {mesh.Edges.Count} half-edges");

        sw.Restart();
        var regions = new List<MeshRegion>();
        HalfEdgeMesh.FindConnectedRegions(mesh, regions);
        progress?.Invoke($"[regions] {regions.Count} connected components in {sw.ElapsedMilliseconds} ms");

        // Per-region work is independent: ChartBuilder/UvFlattener both allocate per-instance
        // state, and the shared HalfEdgeMesh is read-only by the time we get here. We collect
        // each region's charts into its own list and concatenate at the end.
        sw.Restart();
        var perRegionCharts = new List<UvChart>[regions.Count];
        int completed = 0;
        var perRegionWatches = new System.Diagnostics.Stopwatch[regions.Count];
        // The progress sink isn't reentrant — synchronise around it.
        object progressLock = new();
        void EmitProgress(string s)
        {
            if (progress is null) return;
            lock (progressLock) progress(s);
        }

        System.Threading.Tasks.Parallel.For(
            0, regions.Count,
            new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism },
            rIdx =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                perRegionWatches[rIdx] = watch;
                var bucket = new List<UvChart>();
                perRegionCharts[rIdx] = bucket;
                ProcessRegion(regions[rIdx], bucket, options, progress is null ? null : EmitProgress);
                int done = System.Threading.Interlocked.Increment(ref completed);
                EmitProgress($"[region {done}/{regions.Count}] tris={regions[rIdx].Triangles.Length}, charts={bucket.Count}, {watch.ElapsedMilliseconds} ms");
            });

        var charts = new List<UvChart>(regions.Count);
        for (int i = 0; i < regions.Count; ++i)
            charts.AddRange(perRegionCharts[i]);
        progress?.Invoke($"[regions] all {regions.Count} processed in {sw.ElapsedMilliseconds} ms -> {charts.Count} charts");

        sw.Restart();
        AtlasPacker.Pack(charts, options.PackMargin);
        progress?.Invoke($"[pack] {charts.Count} charts packed in {sw.ElapsedMilliseconds} ms");

        // Each chart's faces map back to cleaned triangle indices; route UVs into per-corner slots.
        for (int i = 0; i < outputUV.Length; ++i) outputUV[i] = default;
        foreach (var chart in charts)
        {
            for (int f = 0; f < chart.Region!.Triangles.Length; ++f)
            {
                int triIndex = geometry.TriangleRemap[chart.Region.Triangles[f]];
                outputUV[3 * triIndex + 0] = chart.UVs[3 * f + 0];
                outputUV[3 * triIndex + 1] = chart.UVs[3 * f + 1];
                outputUV[3 * triIndex + 2] = chart.UVs[3 * f + 2];
            }
        }
    }
}
