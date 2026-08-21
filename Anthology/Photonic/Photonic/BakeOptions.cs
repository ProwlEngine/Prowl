// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
    /// Move texel samples that landed inside solid geometry back out into the open, by probing four
    /// tangential directions one texel-diagonal long and stepping past any back face they hit.
    /// Removes the dark halos where a texel straddles a wall base or a floor junction: without it,
    /// the sample point sits behind the surface and every shadow ray from it is blocked.
    /// </summary>
    public bool FixShadowLeaks { get; set; } = true;

    /// <summary>
    /// Evaluate lighting at a Phong-tessellated position instead of the flat triangle position, so a
    /// low-poly curved surface is shaded as the round surface its normals describe rather than the
    /// faceted one its triangles describe. Triangles whose smooth position is occluded fall back to
    /// flat automatically, which keeps samples from sliding inside neighbouring geometry.
    /// </summary>
    public bool SmoothShadowTerminator { get; set; } = true;

    /// <summary>
    /// Blend the two sides of every UV seam together after each iteration, so edges that are
    /// continuous on the model but split in the atlas stop showing a visible lighting discontinuity.
    /// </summary>
    public bool FixSeams { get; set; } = true;

    /// <summary>Seam blending passes. Each pass pulls both sides half-way to their shared average.</summary>
    public int SeamFixPasses { get; set; } = 4;

    /// <summary>How far each seam texel moves toward the average of the two sides per pass.</summary>
    public float SeamFixStrength { get; set; } = 0.5f;

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

    /// <summary>
    /// Trace one texel per <c>SparseStride</c> x <c>SparseStride</c> cell of the atlas and interpolate
    /// the rest from the traced points around them. 1 traces every texel. 8 traces roughly one texel
    /// in sixty-four, so a bake converges dramatically faster at the cost of detail: lighting that
    /// varies faster than the cell size, which mostly means direct shadow edges, is smoothed away.
    /// Texels that no traced point can reach are traced anyway, so thin charts and chart borders stay
    /// correct at any stride.
    /// </summary>
    public int SparseStride { get; set; } = 1;

    /// <summary>
    /// Let sparse sampling cover direct lighting as well as indirect. Off by default: direct light is
    /// where the fast-changing detail lives, and interpolating it blurs every shadow edge by about one
    /// cell. It is also the cheap half of the bake, one shadow ray per light computed once, so leaving
    /// it dense costs little and keeps shadows sharp while indirect still traces sparsely.
    /// </summary>
    public bool SparseIncludesDirect { get; set; } = false;

    /// <summary>
    /// Width of the contact line, in texels: how close another surface has to be before a texel is
    /// treated as sitting on a contact rather than on open surface. A couple of texels is enough to
    /// carry the junction; wider only costs time.
    /// </summary>
    public float SparseContactWidth { get; set; } = 2f;

    /// <summary>
    /// Spacing of traced points along the contact line, in texels. 0 follows <see cref="SparseStride"/>,
    /// so contacts are sampled at the same rate as open surface. Corners of the line are always traced
    /// whatever this is set to.
    /// </summary>
    public int SparseContactStride { get; set; } = 0;

    /// <summary>
    /// Check that a texel can actually see a traced point before interpolating from it. Without this,
    /// two texels that share a plane and a normal are treated as the same surface even when a wall
    /// stands between them, which carries light straight through the wall. Costs a one-off ray per
    /// texel-point pair during setup.
    /// </summary>
    public bool SparseCheckVisibility { get; set; } = true;

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
