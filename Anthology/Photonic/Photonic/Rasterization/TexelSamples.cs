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
    public int InstanceIndex;
    public int MaterialGroupIndex;
    public Float2 UV0;        // material UV at this texel (for diffuse texture sampling)
    public float WorldRadius; // approximate world-space half-width of this texel's footprint; the denoiser's position bandwidth scales with it
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

    /// <summary>Copy <see cref="Pixels"/> into the target's float buffer.</summary>
    public void Flush()
    {
        int n = Width * Height;
        for (int i = 0; i < n; i++)
        {
            Target.PixelsRGB[i * 3    ] = Pixels[i].X;
            Target.PixelsRGB[i * 3 + 1] = Pixels[i].Y;
            Target.PixelsRGB[i * 3 + 2] = Pixels[i].Z;
        }
    }
}
