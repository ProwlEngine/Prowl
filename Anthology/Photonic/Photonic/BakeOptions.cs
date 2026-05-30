using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// Global knobs for a bake. Per-instance / per-target settings live on those objects.
/// </summary>
public sealed class BakeOptions
{
    /// <summary>Indirect diffuse bounces. 0 disables GI; the demo defaults to 2.</summary>
    public int Bounces { get; set; } = 2;

    /// <summary>Russian-roulette termination probability (per bounce). 0 disables RR.</summary>
    public float RussianRoulette { get; set; } = 0.0f;

    /// <summary>Surface offset along the geometric normal for the ray origin, to avoid self-intersection.</summary>
    public float RayBias { get; set; } = 1e-3f;

    /// <summary>Maximum ray distance for visibility tests. Mostly relevant for indirect bounces.</summary>
    public float MaxRayDistance { get; set; } = 1e4f;

    /// <summary>Environment (sky) radiance returned when a ray misses everything. Used as the
    /// constant fallback when <see cref="Environment"/> is not set.</summary>
    public Float3 SkyColor { get; set; } = Float3.Zero;

    /// <summary>
    /// Optional HDR environment: given a normalized ray direction (the direction the ray travels
    /// into the sky), returns the incoming radiance. Lets callers plug a cubemap/equirect sky as a
    /// GI source instead of the flat <see cref="SkyColor"/>. When null, <see cref="SkyColor"/> is used.
    /// <para><b>Thread-safety:</b> invoked concurrently from many bake worker threads; the callback
    /// must be pure / read-only (e.g. sampling immutable cubemap data).</para>
    /// </summary>
    public System.Func<Float3, Float3>? Environment { get; set; }

    /// <summary>
    /// When true, indirect bounce directions are drawn from a precomputed 16k-entry
    /// Halton-distributed cosine-hemisphere LUT (cheap, low-discrepancy). When false, each
    /// bounce builds a fresh direction via Malley's method (sqrt + sin/cos + sqrt). The LUT
    /// is the faster default; flip this off if you suspect it's causing visible structure
    /// in the lightmap from repeated directions.
    /// </summary>
    public bool UseHemisphereLUT { get; set; } = true;

    /// <summary>
    /// When true, every indirect path sample jitters its ray origin inside the texel's
    /// world-space footprint. Without this, a texel whose centre lands in an unlucky spot
    /// (a corner, a contact between surfaces, a sliver near a wall) has every shadow ray fail
    /// the same way, leaving a stuck-dim pixel surrounded by correctly-lit ones, visible as
    /// thin dark lines on flat surfaces. Jitter spreads the per-sample origin across the texel
    /// so the unlucky positions stop dominating.
    /// </summary>
    public bool JitterRayOrigin { get; set; } = true;

    /// <summary>Multiplier on the per-texel world radius used for jitter. 1.0 = jitter across the full texel; 0.5 = half.</summary>
    public float JitterStrength { get; set; } = 1.0f;

    /// <summary>
    /// When true, both the flat material color and the diffuse texture are ignored. Every
    /// surface acts as a white (1, 1, 1) Lambertian. Useful for diagnosing whether visible
    /// lightmap artifacts are caused by texture sampling vs. the path tracer itself.
    /// </summary>
    public bool IgnoreAlbedo { get; set; } = false;

    /// <summary>
    /// When true, the lightmap includes direct lighting at the texel itself (sun rays directly
    /// striking the surface being baked). When false, only the indirect contribution is stored:
    /// direct shadow rays at bounce hit points are <i>still</i> fired (so light bounced off other
    /// surfaces propagates correctly), but the texel's own direct lighting must be added at
    /// runtime by a dynamic-light shader. The standard "indirect-only baked lighting" pipeline.
    /// </summary>
    public bool IncludeDirectLighting { get; set; } = true;

    /// <summary>Which path tracer to run. <see cref="PathTracerKind.PerTexel"/> traces from every covered atlas pixel; <see cref="PathTracerKind.Surfel"/> traces a low-density surfel cloud instead and interpolates to texels.</summary>
    public PathTracerKind PathTracer { get; set; } = PathTracerKind.PerTexel;

    /// <summary>Surfel mode: target surfels per square metre of world surface. Higher = more surfels, finer detail, slower bake.</summary>
    public float SurfelsPerSquareMeter { get; set; } = 2.0f;

    /// <summary>
    /// Surfel mode: per-texel cap on how many of the surfels in the texel's spatial cell are
    /// blended in. Acts as a budget knob; the actual reach of each surfel is set by its own
    /// <see cref="Surfels.Surfel.Radius"/>, computed at generation time from triangle area.
    /// </summary>
    public int SurfelMaxNeighbors { get; set; } = 8;

    /// <summary>Surfel mode: normal-similarity threshold for accepting a surfel during interpolation. Default 0.85.</summary>
    public float SurfelNormalThreshold { get; set; } = 0.85f;

    /// <summary>
    /// Surfel mode: at generation time, reject candidate surfels whose <i>own-orientation</i>
    /// nearest neighbour is too close. Misaligned-normal surfels are allowed to cluster freely
    /// (corners, edges) since they're invisible to each other during interpolation anyway. Only
    /// aligned surfels need even spacing. Cheap normal-aware Poisson disk.
    /// </summary>
    public bool SurfelNormalRejection { get; set; } = true;

    /// <summary>Surfel mode: minimum separation between aligned-normal surfels, as a fraction of <see cref="Surfels.Surfel.Radius"/>. 0.6 = aligned surfels must be 60% of a kernel apart.</summary>
    public float SurfelPoissonRadiusFactor { get; set; } = 0.6f;

    /// <summary>Surfel mode: dot-product threshold above which two surfels count as "aligned" for rejection. 0.85 ~= within 32 degrees.</summary>
    public float SurfelPoissonAlignThreshold { get; set; } = 0.85f;

    /// <summary>Surfel mode: master toggle for the early-termination heuristics below. Lets you A/B-test the optimisation.</summary>
    public bool SurfelEarlyTerminate { get; set; } = true;

    /// <summary>Surfel mode: surfels whose pre-computed direct lighting exceeds this luminance are flagged Inactive before any indirect tracing.</summary>
    public float SurfelDirectLitCutoff { get; set; } = 0.3f;

    /// <summary>Surfel mode: minimum indirect samples a surfel must have before brightness / sky-only checks can deactivate it.</summary>
    public int SurfelMinSamplesBeforeCheck { get; set; } = 16;

    /// <summary>Surfel mode: if a surfel's running indirect average exceeds this per-channel sum, mark it Inactive. Tames runaway fireflies and converges bright surfels early.</summary>
    public float SurfelBrightnessCutoff { get; set; } = 6.0f;

    /// <summary>Surfel mode: if a surfel's running indirect average is within this luminance distance of <see cref="SkyColor"/>, deactivate.</summary>
    public float SurfelSkyDominantCutoff { get; set; } = 0.05f;

    /// <summary>Edge dilation pixels applied after the bake, to prevent bilinear bleed at seams.</summary>
    public int DilatePixels { get; set; } = 2;

    /// <summary>Cap on worker threads. -1 = use the runtime default.</summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;

    /// <summary>Deterministic seed for the bake's PRNG. Two bakes with the same seed produce the same output.</summary>
    public ulong Seed { get; set; } = 0x9E3779B97F4A7C15UL;

    /// <summary>
    /// Indirect samples shot per texel per iteration. The bake runs as a progressive temporal
    /// accumulator: each iteration shoots this many indirect samples per texel and folds them
    /// into a running average. The atlas is updated after each iteration, so the caller can
    /// render a preview while the bake converges. The job keeps running until <see cref="Job.Cancel"/>
    /// is called. Caller-side: emulate "one-shot" by polling until satisfied, then cancelling.
    /// </summary>
    public int SamplesPerIteration { get; set; } = 1;
}

/// <summary>Top-level path tracer architecture.</summary>
public enum PathTracerKind
{
    /// <summary>Trace from every covered texel directly. Simple, predictable, the default.</summary>
    PerTexel,
    /// <summary>Trace from a sparse cloud of "surfels" scattered on the mesh surface, then interpolate the surfels' radiance back to texels. Much cheaper for high-resolution atlases; trades local detail for far fewer rays.</summary>
    Surfel,
}

/// <summary>Final state of a bake.</summary>
public enum JobStatus
{
    /// <summary>Job is still running or has not started.</summary>
    Pending,
    /// <summary>Job was cancelled via <c>Cancel()</c>.</summary>
    Cancelled,
    /// <summary>Job threw an exception. See <see cref="Job.Error"/>.</summary>
    Failed,
}
