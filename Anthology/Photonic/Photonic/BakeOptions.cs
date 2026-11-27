// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// Global knobs for a bake. Per-instance / per-target settings live on those objects.
/// </summary>
public sealed class BakeOptions
{
    // ---- scene ---------------------------------------------------------------------------------

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
    /// When true, the lightmap includes direct lighting at the texel itself. When false, only the
    /// indirect contribution is stored: direct shadow rays at bounce hit points are <i>still</i> fired
    /// (so bounced light propagates correctly), but the texel's own direct lighting must be added at
    /// runtime by a dynamic-light shader. The standard "indirect-only baked lighting" pipeline.
    /// <para>This is the scene-wide form of <see cref="Scene.Lights.Light.BakeDirect"/>, which does the
    /// same thing for one light. Setting this false is equivalent to clearing that flag on every light.</para>
    /// </summary>
    public bool IncludeDirectLighting { get; set; } = true;

    /// <summary>
    /// When true, all bake rays cull back faces, so light behaves as it does in a backface-culled
    /// rasterizer: it can pass up through a one-sided floor, the way Prowl's backface-culled shadows
    /// allow. When false the tracer is two-sided. The front-face winding is fixed to the renderer's
    /// convention by the integrator.
    /// </summary>
    public bool DoBackfaceCull { get; set; } = false;

    // ---- quality -------------------------------------------------------------------------------

    /// <summary>Indirect diffuse bounces. 0 disables GI.</summary>
    public int Bounces { get; set; } = 2;

    /// <summary>
    /// Indirect samples shot per texel per iteration. The bake is a progressive accumulator, so this
    /// paces it rather than deciding quality: total samples are iterations x this. Larger values mean
    /// coarser progress steps and slightly less per-iteration overhead.
    /// </summary>
    public int SamplesPerIteration { get; set; } = 1;

    /// <summary>
    /// Trace one texel per <c>SparseStride</c> x <c>SparseStride</c> cell of the atlas and interpolate
    /// the rest from the traced points around them. 1 traces every texel. 8 traces a few percent of an
    /// atlas, which converges dramatically faster at the cost of fine indirect detail. Contacts,
    /// corners and shadow boundaries keep their own points automatically, whatever this is set to.
    /// </summary>
    public int SparseStride { get; set; } = 1;

    /// <summary>
    /// Let sparse sampling cover direct lighting as well as indirect. Off by default: direct light is
    /// where the fast-changing detail lives, and interpolating it blurs every shadow edge by about one
    /// cell. It is also the cheap half of the bake, so leaving it dense costs little.
    /// </summary>
    public bool SparseIncludesDirect { get; set; } = false;

    /// <summary>
    /// Edge dilation pixels applied after the bake, so filtering at a chart edge has something valid to
    /// read. 2 is the floor rather than a preference: bicubic lightmap sampling reads two texels past
    /// the chart, and anything less means it samples texels the bake never wrote.
    /// </summary>
    public int DilatePixels { get; set; } = 2;

    /// <summary>
    /// Blend the two sides of every UV seam together after each iteration, so edges that are
    /// continuous on the model but split in the atlas stop showing a visible lighting discontinuity.
    /// Costs a pass over the seam texels per iteration.
    /// </summary>
    public bool FixSeams { get; set; } = true;

    // ---- machinery -----------------------------------------------------------------------------

    /// <summary>
    /// Surface offset for ray origins, in world units. 0 derives it from the scene's size, which is the
    /// only way to be right at every scale: a millimetre is generous on a character, invisible on a
    /// terrain, and catastrophic on a scene modelled in centimetres. Ray origins additionally carry a
    /// magnitude-relative epsilon, so geometry far from the world origin stays separated regardless.
    /// </summary>
    public float RayBias { get; set; } = 0f;

    /// <summary>
    /// Maximum ray distance for visibility tests. 0 derives it from the scene bounds, which is what a
    /// fixed number can never track.
    /// </summary>
    public float MaxRayDistance { get; set; } = 0f;

    /// <summary>Cap on worker threads. -1 = use the runtime default.</summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;

    /// <summary>Deterministic seed for the bake's PRNG. Two bakes with the same seed produce the same output.</summary>
    public ulong Seed { get; set; } = 0x9E3779B97F4A7C15UL;

    /// <summary>Switches for diagnosing a bake. None of them improve output; they isolate causes.</summary>
    public BakeDiagnostics Diagnostics { get; } = new();

    // ---- derived, resolved once per bake --------------------------------------------------------

    internal float EffectiveRayBias { get; private set; } = 1e-3f;
    internal float EffectiveMaxRayDistance { get; private set; } = 1e4f;

    /// <summary>
    /// Fill in whatever was left automatic, from the world-space extent of the scene being baked.
    /// </summary>
    internal void Resolve(float sceneDiagonal)
    {
        if (!(sceneDiagonal > 0f) || float.IsNaN(sceneDiagonal)) sceneDiagonal = 1f;

        EffectiveRayBias = RayBias > 0f
            ? RayBias
            : System.MathF.Max(sceneDiagonal * 1e-5f, 1e-6f);

        EffectiveMaxRayDistance = MaxRayDistance > 0f
            ? MaxRayDistance
            : sceneDiagonal * 2f;
    }
}

/// <summary>
/// Escape hatches for working out where an artifact comes from. Every one of these makes the bake
/// worse or slower; they exist so a bad result can be attributed to Photonic or to the content.
/// </summary>
public sealed class BakeDiagnostics
{
    /// <summary>Treat every surface as white. Isolates whether an artifact comes from texture sampling or the tracer.</summary>
    public bool IgnoreAlbedo { get; set; }

    /// <summary>
    /// Stop pushing texel samples out of solid geometry. Leaving it on shows the dark halos where a
    /// texel straddles a wall base and its sample ends up behind the surface.
    /// </summary>
    public bool DisableShadowLeakFix { get; set; }

    /// <summary>
    /// Shade at the flat triangle position rather than the Phong-tessellated one. Leaving it on shows
    /// low-poly curved surfaces self-shadowing in facets.
    /// </summary>
    public bool DisableSmoothTerminator { get; set; }

    /// <summary>
    /// Draw bounce directions per sample instead of from the precomputed low-discrepancy table. Slower,
    /// and only interesting when checking whether the table is causing visible structure.
    /// </summary>
    public bool DisableHemisphereLUT { get; set; }
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
