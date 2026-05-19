namespace Prowl.Unwrapper;

/// <summary>
/// Tunable parameters for an unwrap operation. Defaults are conservative values that work
/// well across a broad range of meshes; callers with unusual input can override individual knobs.
/// </summary>
public sealed class UnwrapOptions
{
    /// <summary>Maximum allowed mean angular distortion before a chart is rejected (0..1).</summary>
    public double AngleDistortionThreshold { get; set; } = 0.08;

    /// <summary>Maximum allowed mean area distortion before a chart is rejected (0..1).</summary>
    public double AreaDistortionThreshold { get; set; } = 0.15;

    /// <summary>Dihedral-angle threshold (degrees) for marking edges as "hard". Edges sharper
    /// than this are treated as creases and become preferred cut locations.</summary>
    public double HardAngle { get; set; } = 88.0;

    /// <summary>
    /// Per-chart packing border in UV space. 1/256 places a one-texel margin if the lightmap
    /// is later rasterised at 256x256.
    /// </summary>
    public double PackMargin { get; set; } = 1.0 / 256.0;

    // ---- Segmentation tunables ---------------------------------------------------------------
    // Rarely need adjusting, but exposed for callers with unusual geometry (huge sliver charts,
    // very thin meshes, etc.).

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
    /// Cap on worker threads used by the per-region pipeline. <c>-1</c> means
    /// "use as many as the runtime sees fit" (defaults to one per logical CPU).
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;

    /// <summary>
    /// Wall-clock budget for the chart-merge pass per region (milliseconds).
    /// The merger walks every adjacent chart pair and trial-flattens the union;
    /// on a dirty mesh that can blow up quadratically. Once this budget elapses
    /// any remaining pairs are accepted as-is. Set high to disable the cap.
    /// </summary>
    public long MergeTimeBudgetMs { get; set; } = 2000;
}
