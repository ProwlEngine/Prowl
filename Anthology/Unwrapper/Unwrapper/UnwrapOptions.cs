// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Unwrapper;

/// <summary>Which algorithm produces the charts.</summary>
public enum UnwrapMethod
{
    /// <summary>
    /// Group triangles by the cube face their normal points at, cut the groups into connected
    /// charts, and flatten each by dropping it onto its projection plane. Near-instant and
    /// overlap-free on well-formed geometry, at the cost of stretch on slanted surfaces and more
    /// charts than a conformal solve would need.
    /// </summary>
    Projection,

    /// <summary>
    /// Lloyd-style chart segmentation, angle-based flattening (LinABF) and a least-squares
    /// conformal map (LSCM), with per-chart overlap validation. Far lower distortion and far
    /// fewer seams; orders of magnitude slower.
    /// </summary>
    Conformal,
}

/// <summary>
/// Tunable parameters for an unwrap operation. Defaults are conservative values that work
/// well across a broad range of meshes; callers with unusual input can override individual knobs.
/// </summary>
public sealed class UnwrapOptions
{
    /// <summary>Which algorithm to run. See <see cref="UnwrapMethod"/> for the trade-off.</summary>
    public UnwrapMethod Method { get; set; } = UnwrapMethod.Projection;

    /// <summary>
    /// Per-chart packing border in UV space. 1/256 places a one-texel margin if the lightmap
    /// is later rasterised at 256x256.
    /// </summary>
    public double PackMargin { get; set; } = 1.0 / 256.0;

    /// <summary>Dihedral-angle threshold (degrees) for marking edges as "hard". Edges sharper
    /// than this are treated as creases and become preferred cut locations.</summary>
    public double HardAngle { get; set; } = 88.0;

    /// <summary>
    /// Cap on worker threads used by the per-region pipeline. <c>-1</c> means
    /// "use as many as the runtime sees fit" (defaults to one per logical CPU).
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;

    /// <summary>Knobs specific to <see cref="UnwrapMethod.Projection"/>.</summary>
    public ProjectionOptions Projection { get; set; } = new();

    /// <summary>Knobs specific to <see cref="UnwrapMethod.Conformal"/>.</summary>
    public ConformalOptions Conformal { get; set; } = new();
}

/// <summary>Settings for the projection unwrapper.</summary>
public sealed class ProjectionOptions
{
    /// <summary>
    /// Project onto the eight cube-corner directions as well as the six face directions.
    /// Drops the worst-case stretch from 1.73x to 1.18x, but splits curved surfaces into
    /// more charts, which costs atlas space in packing margins.
    /// </summary>
    public bool UseDiagonalAxes { get; set; }

    /// <summary>
    /// Charts with fewer triangles than this are folded into the best-fitting adjacent chart
    /// rather than being packed on their own. 0 keeps every chart.
    /// </summary>
    public int MinChartTriangles { get; set; } = 2;
}

/// <summary>Settings for the conformal unwrapper.</summary>
public sealed class ConformalOptions
{
    /// <summary>Maximum allowed mean angular distortion before a chart is rejected (0..1).</summary>
    public double AngleDistortionThreshold { get; set; } = 0.08;

    /// <summary>Maximum allowed mean area distortion before a chart is rejected (0..1).</summary>
    public double AreaDistortionThreshold { get; set; } = 0.15;

    /// <summary>
    /// Minimum fraction of total component area a chart must cover to be kept.
    /// Below this AND below <see cref="ChartFacetCountThreshold"/>, the chart is discarded.
    /// </summary>
    public double ChartAreaThreshold { get; set; } = 0.02;

    /// <summary>Minimum fraction of total triangle count a chart must cover to be kept.</summary>
    public double ChartFacetCountThreshold { get; set; } = 0.01;

    /// <summary>Exponent applied to the 3D distance metric when scoring chart growth candidates.</summary>
    public double CompactnessPower { get; set; } = 0.7;

    /// <summary>Exponent applied to the straightness metric when scoring chart growth candidates.</summary>
    public double StraightnessPower { get; set; } = 1.0;

    /// <summary>
    /// Lloyd early-out: if fewer than this fraction of facets changed chart since the previous iteration,
    /// segmentation is considered stable and the loop exits.
    /// </summary>
    public double LloydChangePrevThreshold { get; set; } = 0.01;

    /// <summary>Same as <see cref="LloydChangePrevThreshold"/> but compared against two iterations ago.</summary>
    public double LloydChangePrev2Threshold { get; set; } = 0.01;

    /// <summary>
    /// Wall-clock budget for the chart-merge pass per region (milliseconds).
    /// The merger walks every adjacent chart pair and trial-flattens the union;
    /// on a dirty mesh that can blow up quadratically. Once this budget elapses
    /// any remaining pairs are accepted as-is. Set high to disable the cap.
    /// </summary>
    public long MergeTimeBudgetMs { get; set; } = 2000;
}
