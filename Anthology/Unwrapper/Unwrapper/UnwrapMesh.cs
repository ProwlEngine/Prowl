using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Thrown when the unwrapper can't produce a result -- usually because the input geometry
/// fully collapses during cleanup, or because UV inputs are inconsistent with the triangle count.
/// </summary>
public sealed class UnwrapException : System.Exception
{
    public UnwrapException(string message) : base(message) { }
    public UnwrapException(string message, System.Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Fluent builder for an unwrap call. Construct with positions + triangles, optionally chain
/// <see cref="WithNormals"/> and <see cref="WithMaterialUVs"/>, then call <see cref="Unwrap"/>.
///
/// Triangles are flat unsigned indices (three per triangle). Per-corner UVs, if provided,
/// are 3 × triangleCount Double2 values — they are only used as a hint when welding vertices.
/// </summary>
public sealed class UnwrapMesh
{
    private readonly Double3[] _positions;
    private readonly int[] _triangles;
    private Double3[]? _normals;
    private Double2[]? _perCornerUVs;
    private System.Action<string>? _progressSink;

    /// <param name="positions">One Double3 per vertex.</param>
    /// <param name="triangles">Flat triangle list: indices into <paramref name="positions"/>, three per face.</param>
    public UnwrapMesh(Double3[] positions, int[] triangles)
    {
        if (positions is null) throw new System.ArgumentNullException(nameof(positions));
        if (triangles is null) throw new System.ArgumentNullException(nameof(triangles));
        if (triangles.Length % 3 != 0) throw new System.ArgumentException("triangles length must be a multiple of 3.", nameof(triangles));

        int vertexCount = positions.Length;
        for (int i = 0; i < triangles.Length; ++i)
        {
            int idx = triangles[i];
            if ((uint)idx >= (uint)vertexCount)
                throw new System.ArgumentOutOfRangeException(nameof(triangles),
                    $"triangle index {idx} at position {i} is out of range for {vertexCount} vertices.");
        }

        _positions = positions;
        _triangles = triangles;
    }

    /// <summary>Per-vertex normals. Used as a welding guard — coincident points with opposing normals are kept apart.</summary>
    public UnwrapMesh WithNormals(Double3[] normals)
    {
        if (normals.Length != _positions.Length)
            throw new System.ArgumentException("normals must have one entry per vertex.", nameof(normals));
        _normals = normals;
        return this;
    }

    /// <summary>
    /// Per-corner material UVs (three per triangle). Disagreement across an edge gets marked as a UV seam
    /// during segmentation, which biases chart growth to follow existing seams.
    /// </summary>
    public UnwrapMesh WithMaterialUVs(Double2[] perCornerUVs)
    {
        if (perCornerUVs.Length != _triangles.Length)
            throw new System.ArgumentException("perCornerUVs must have one entry per triangle corner (3 * triangleCount).", nameof(perCornerUVs));
        _perCornerUVs = perCornerUVs;
        return this;
    }

    /// <summary>
    /// Receive per-phase progress messages (cleanup, segmentation, parametrisation, packing).
    /// Handy while diagnosing slow unwraps; ignored when null.
    /// </summary>
    public UnwrapMesh WithProgress(System.Action<string> sink)
    {
        _progressSink = sink;
        return this;
    }

    /// <summary>Run the unwrap with the supplied options.</summary>
    public UnwrapResult Unwrap(UnwrapOptions? options = null)
    {
        options ??= new UnwrapOptions();
        ValidateOptions(options);

        if (!GeometryPrep.TryPrepare(_positions, _normals, _triangles, _perCornerUVs, out var cleaned, out string? error, _progressSink))
            throw new UnwrapException(error ?? "Geometry preparation failed.");

        int triangleCount = _triangles.Length / 3;
        var perCornerOut = new Double2[3 * triangleCount];
        UnwrapPipeline.Run(cleaned, options, perCornerOut, _progressSink);
        return new UnwrapResult(perCornerOut, cleaned.DegenerateTriangleIndices);
    }

    /// <summary>One-shot convenience: equivalent to <c>new UnwrapMesh(positions, triangles).Unwrap(options)</c>.</summary>
    public static UnwrapResult Unwrap(Double3[] positions, int[] triangles, UnwrapOptions? options = null)
        => new UnwrapMesh(positions, triangles).Unwrap(options);

    private static void ValidateOptions(UnwrapOptions o)
    {
        static void Positive(double v, string name)
        {
            if (!(v > 0) || double.IsNaN(v) || double.IsInfinity(v))
                throw new System.ArgumentOutOfRangeException(name, v, $"{name} must be a positive finite value.");
        }
        static void NonNegative(double v, string name)
        {
            if (!(v >= 0) || double.IsNaN(v) || double.IsInfinity(v))
                throw new System.ArgumentOutOfRangeException(name, v, $"{name} must be a non-negative finite value.");
        }

        Positive(o.AngleDistortionThreshold, nameof(o.AngleDistortionThreshold));
        Positive(o.AreaDistortionThreshold, nameof(o.AreaDistortionThreshold));

        if (!(o.HardAngle > 0 && o.HardAngle <= 180))
            throw new System.ArgumentOutOfRangeException(nameof(o.HardAngle), o.HardAngle, "HardAngle must be in (0, 180].");

        NonNegative(o.PackMargin, nameof(o.PackMargin));
        if (o.PackMargin >= 0.5)
            throw new System.ArgumentOutOfRangeException(nameof(o.PackMargin), o.PackMargin, "PackMargin must be less than 0.5.");

        NonNegative(o.ChartAreaThreshold, nameof(o.ChartAreaThreshold));
        NonNegative(o.ChartFacetCountThreshold, nameof(o.ChartFacetCountThreshold));
        NonNegative(o.CompactnessPower, nameof(o.CompactnessPower));
        NonNegative(o.StraightnessPower, nameof(o.StraightnessPower));
        NonNegative(o.LloydChangePrevThreshold, nameof(o.LloydChangePrevThreshold));
        NonNegative(o.LloydChangePrev2Threshold, nameof(o.LloydChangePrev2Threshold));

        if (o.MaxDegreeOfParallelism == 0 || o.MaxDegreeOfParallelism < -1)
            throw new System.ArgumentOutOfRangeException(nameof(o.MaxDegreeOfParallelism), o.MaxDegreeOfParallelism,
                "MaxDegreeOfParallelism must be -1 or a positive integer.");

        if (o.MergeTimeBudgetMs < 0)
            throw new System.ArgumentOutOfRangeException(nameof(o.MergeTimeBudgetMs), o.MergeTimeBudgetMs,
                "MergeTimeBudgetMs must be non-negative.");
    }
}

/// <summary>Output of an unwrap call.</summary>
public sealed class UnwrapResult
{
    /// <summary>
    /// One UV per triangle corner, indexed as <c>3*triangleIndex + cornerIndex</c>.
    /// Triangle indices align with the original input — degenerate triangles get zero UVs.
    /// </summary>
    public Double2[] PerCornerUVs { get; }

    /// <summary>Indices of input triangles that were dropped as degenerate during cleanup, or null if none.</summary>
    public int[]? DegenerateTriangleIndices { get; }

    internal UnwrapResult(Double2[] perCornerUVs, int[]? degenerateTriangleIndices)
    {
        PerCornerUVs = perCornerUVs;
        DegenerateTriangleIndices = degenerateTriangleIndices;
    }
}
