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

    /// <summary>
    /// When true, all bake rays (visibility, bounce, and shadow/occlusion) cull back faces, so light
    /// behaves as it does in a backface-culled rasterizer: it can pass up through a one-sided floor,
    /// the way Prowl's backface-culled shadows allow. When false the tracer is two-sided (historic
    /// behavior). The front-face winding is fixed to the renderer's convention by the integrator.
    /// </summary>
    public bool DoBackfaceCull { get; set; } = false;

    /// <summary>Edge dilation pixels applied after the bake, to prevent bilinear bleed at seams.</summary>
    public int DilatePixels { get; set; } = 2;

    /// <summary>
    /// Run an edge-avoiding wavelet denoiser over the converged lightmap (before dilation), guided by
    /// per-texel normal + position so it removes Monte-Carlo noise without bleeding across chart seams
    /// or geometric / lighting edges. Off by default. Applied once at finalize via <see cref="Job.Denoise"/>.
    /// </summary>
    public bool Denoise { get; set; } = false;

    /// <summary>Denoiser a-trous passes; each doubles the kernel reach (1, 2, 4, ...). 5 ~= a 32px effective radius.</summary>
    public int DenoiseIterations { get; set; } = 5;

    /// <summary>Denoiser normal edge-stop exponent. Higher = sharper preservation of normal discontinuities.</summary>
    public float DenoiseNormalPhi { get; set; } = 64f;

    /// <summary>Denoiser position bandwidth as a multiple of the per-texel world footprint. Lower = preserves finer spatial detail.</summary>
    public float DenoisePositionScale { get; set; } = 2f;

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
