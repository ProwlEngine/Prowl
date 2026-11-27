// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Photonic.Rasterization;

/// <summary>
/// One per atlas-page pixel that any triangle conservatively covers. The integrator reads
/// <see cref="Position"/> + <see cref="Normal"/> to spawn rays; <see cref="MaterialGroupIndex"/>
/// lets it look up the source material for albedo.
/// </summary>
internal struct TexelSample
{
    public Float3 Position;
    public Float3 Normal;
    /// <summary>
    /// Geometric (face) normal of the source triangle, in world space, flipped when needed to agree
    /// with <see cref="Normal"/>. Rays are offset and leak-tested against this rather than the
    /// interpolated normal, which can point far away from the surface it belongs to.
    /// </summary>
    public Float3 FaceNormal;
    /// <summary>
    /// Phong-tessellated position: where this texel would sit if the triangle were curved to match
    /// its vertex normals. Constrained to never fall behind the triangle's own plane. Used instead
    /// of <see cref="Position"/> unless the triangle was rejected as occluded, which is what stops
    /// low-poly smooth surfaces from shadowing themselves in facets.
    /// </summary>
    public Float3 SmoothPosition;
    /// <summary>Stable id of the source triangle across the whole bake; indexes the smooth/flat decision table.</summary>
    public int TriangleId;
    /// <summary>
    /// Distance to the nearest other surface in this texel's own lighting hemisphere, as a fraction of
    /// the detail radius. 1 means nothing is near: the lighting here can only change slowly, so the
    /// texel is safe to interpolate. Anything below 1 is a contact, crease or junction, where lighting
    /// changes faster than a sparse grid can follow.
    /// </summary>
    public float Proximity;
    public int InstanceIndex;
    public int MaterialGroupIndex;
    public Float2 UV0;        // material UV at this texel (for diffuse texture sampling)
    public float WorldRadius; // approximate world-space half-width of this texel's footprint
    /// <summary>
    /// True when the pixel centre lies strictly inside this triangle (all barycentrics >= 0
    /// without clamping). A "strict" writer beats a "conservative-only" writer when multiple
    /// triangles cover the same atlas pixel: that's what prevents a sliver / hidden triangle
    /// from stealing the texel from the surface the user actually sees.
    /// </summary>
    public bool StrictlyInside;
}

/// <summary>
/// Per-target accumulation arrays. Pixel layout is row-major: <c>y * Width + x</c>.
/// </summary>
internal sealed class TargetWorkspace
{
    public LightmapTarget Target;
    public int Width, Height;
    public TexelSample[] Samples;   // length = Width*Height; populated by the rasterizer
    public bool[] Covered;          // true if a triangle covers this texel (-> integrate or interpolate)
    public bool[] Integrated;       // true if we've already integrated/interpolated this texel
    public Float3[] Pixels;         // working buffer; parallel Float3 view of LightmapTarget.PixelsRGB

    // Scratch for the per-iteration post passes (seam stitch, dilate). These run every iteration on
    // every page, so allocating them per call would churn tens of megabytes per iteration.
    /// <summary>
    /// Where an iteration accumulates. Published to the target in one copy when the iteration is
    /// complete, so a host polling from another thread never sees a half-written atlas.
    /// </summary>
    public float[]? WorkingRGB;

    public float[]? PostScratchRGB;
    public bool[]? PostScratchCovered;
    public bool[]? PostScratchSnapshot;

    // Progressive-bake buffers, allocated lazily by the Job at the start of integration.
    public Float3[]? DirectCache;          // deterministic direct lighting per texel; computed once.
    public Float3[]? IndirectSum;          // accumulator for indirect samples; sum / count = current estimate.
    public int[]? IndirectSampleCount;     // number of indirect samples folded into IndirectSum so far.

    public TargetWorkspace(LightmapTarget t)
    {
        Target = t;
        Width = t.Width; Height = t.Height;
        int n = Width * Height;
        Samples = new TexelSample[n];
        Covered = new bool[n];
        Integrated = new bool[n];
        Pixels = new Float3[n];
    }

    public void AllocateContinuousBuffers()
    {
        int n = Width * Height;
        DirectCache = new Float3[n];
        IndirectSum = new Float3[n];
        IndirectSampleCount = new int[n];
    }

    /// <summary>Iteration-local pixel buffer. Same layout as the target's.</summary>
    public float[] Working() => WorkingRGB ??= new float[Width * Height * 3];

    /// <summary>Copy the finished iteration into the target in one pass.</summary>
    public void Publish() => System.Array.Copy(Working(), Target.PixelsRGB, Target.PixelsRGB.Length);

    /// <summary>Scratch copy of the pixel buffer, allocated once and reused by every post pass.</summary>
    public float[] ScratchRGB() => PostScratchRGB ??= new float[Width * Height * 3];

    /// <summary>Second scratch mask, used by dilation as its per-pass snapshot.</summary>
    public bool[] ScratchSnapshot() => PostScratchSnapshot ??= new bool[Width * Height];

    /// <summary>Scratch copy of the coverage mask, so dilation never mutates the bake's own state.</summary>
    public bool[] ScratchCovered()
    {
        PostScratchCovered ??= new bool[Width * Height];
        System.Array.Copy(Covered, PostScratchCovered, Covered.Length);
        return PostScratchCovered;
    }

    /// <summary>Copy <see cref="Pixels"/> into the target's float buffer.</summary>
    public void Flush()
    {
        int n = Width * Height;
        for (int i = 0; i < n; i++)
        {
            Target.PixelsRGB[i * 3] = Pixels[i].X;
            Target.PixelsRGB[i * 3 + 1] = Pixels[i].Y;
            Target.PixelsRGB[i * 3 + 2] = Pixels[i].Z;
        }
    }
}
