using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// One lightmap atlas page. Holds the HDR output buffer and the list of instance placements
/// that contribute pixels into it.
/// </summary>
public sealed class LightmapTarget
{
    /// <summary>Target name (informational; used in logs and debug images).</summary>
    public string Name { get; }

    /// <summary>Atlas page width in pixels.</summary>
    public int Width { get; }

    /// <summary>Atlas page height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Backing buffer for <see cref="Pixels"/>. Internal so the bake worker can mutate it in
    /// place; external consumers should use the <see cref="Pixels"/> span (read-only) or
    /// the <see cref="ReadHDR"/> / <see cref="ReadLDR"/> helpers. Demos / tooling that need
    /// the raw array (e.g. for zero-copy GL upload) gain access via InternalsVisibleTo.
    /// </summary>
    internal float[] PixelsRGB { get; }

    /// <summary>Read-only view over the live HDR buffer in (R, G, B, R, G, B, ...) layout, length <c>Width*Height*3</c>.</summary>
    public System.ReadOnlySpan<float> Pixels => PixelsRGB;

    private readonly System.Collections.Generic.List<BakeInstance> _instances = new();

    /// <summary>Instances whose UV1 maps into this target.</summary>
    public System.Collections.Generic.IReadOnlyList<BakeInstance> Instances => _instances;

    /// <summary>
    /// Per-texel coverage: 1 where a triangle covers the texel (a real baked value), 0 where the
    /// texel is empty atlas space or was only filled by the seam-dilation pass. Populated by the
    /// bake once the rasterise phase completes; empty before then. Length <c>Width*Height</c>,
    /// row-major (<c>y*Width + x</c>). Useful for runtime seam handling, re-bakes, and debugging.
    /// </summary>
    internal byte[]? CoverageMask;
    public System.ReadOnlySpan<byte> Coverage => CoverageMask;

    internal LightmapTarget(string name, int width, int height)
    {
        Name = name;
        Width = width;
        Height = height;
        PixelsRGB = new float[width * height * 3];
    }

    /// <summary>
    /// Place a mesh instance into this atlas page. <paramref name="uvOffset"/> + <paramref name="uvScale"/>
    /// transform the mesh's bake-UV layer into [0,1]^2 of the target. Default: identity (whole page).
    /// </summary>
    /// <param name="mesh">The mesh to instance into this target.</param>
    /// <param name="worldTransform">Object-to-world transform applied to the mesh's vertices.</param>
    /// <param name="uvOffset">Translation applied to the bake-UV layer when sampling the atlas.</param>
    /// <param name="uvScale">Scale applied to the bake-UV layer when sampling the atlas.</param>
    /// <param name="bakeUVLayer">UV layer used for lightmap atlas placement (defaults to <c>"UV1"</c>).</param>
    public BakeInstance AddBakeInstance(BakeMesh mesh, Float4x4 worldTransform,
                                        Float2? uvOffset = null, Float2? uvScale = null,
                                        string bakeUVLayer = "UV1")
    {
        var inst = new BakeInstance(mesh, worldTransform,
                                    uvOffset ?? Float2.Zero,
                                    uvScale ?? Float2.One,
                                    bakeUVLayer, this);
        _instances.Add(inst);
        return inst;
    }

    /// <summary>Returns a fresh HDR snapshot of the current pixel data (R,G,B interleaved). Read after the job has succeeded.</summary>
    public float[] ReadHDR()
    {
        var copy = new float[PixelsRGB.Length];
        System.Array.Copy(PixelsRGB, copy, copy.Length);
        return copy;
    }

    /// <summary>
    /// HDR snapshot as RGBA float (alpha = 1), length <c>Width*Height*4</c>. This is the correct
    /// thing to upload to an <c>Rgba16f</c>/<c>Rgba32f</c> lightmap texture: the values stay
    /// linear HDR so the runtime multiplies them by albedo and tonemaps the whole scene afterward.
    /// Do NOT use <see cref="ReadLDR"/> for storage — it tonemaps, which is only correct for a preview.
    /// </summary>
    public float[] ReadHDRRGBA()
    {
        int n = Width * Height;
        var rgba = new float[n * 4];
        for (int i = 0; i < n; i++)
        {
            rgba[i * 4    ] = PixelsRGB[i * 3    ];
            rgba[i * 4 + 1] = PixelsRGB[i * 3 + 1];
            rgba[i * 4 + 2] = PixelsRGB[i * 3 + 2];
            rgba[i * 4 + 3] = 1f;
        }
        return rgba;
    }

    /// <summary>
    /// Encode the HDR lightmap as 8-bit RGBM (RGBA8) without tonemapping: a compact lossy HDR
    /// format for shipped builds. The stored alpha carries a shared multiplier so values up to
    /// <paramref name="maxRange"/> survive in 8 bits per channel.
    /// <para>Decode in the shader as <c>rgb * a * maxRange</c> (with the same <paramref name="maxRange"/>).
    /// Sample the texture in <b>linear</b> space (no sRGB) — the data is linear radiance, not colour.</para>
    /// </summary>
    public byte[] ReadRGBM(float maxRange = 8f)
    {
        if (maxRange <= 0f) maxRange = 8f;
        int n = Width * Height;
        var bytes = new byte[n * 4];
        float invMax = 1f / maxRange;
        for (int i = 0; i < n; i++)
        {
            float r = System.Math.Max(0f, PixelsRGB[i * 3    ]);
            float g = System.Math.Max(0f, PixelsRGB[i * 3 + 1]);
            float b = System.Math.Max(0f, PixelsRGB[i * 3 + 2]);

            // Shared multiplier = brightest channel normalized into [0,1], quantized UP so the
            // reconstructed value never clips below the original.
            float m = System.Math.Max(r, System.Math.Max(g, b)) * invMax;
            m = System.Math.Clamp(m, 1f / 255f, 1f);
            float a = (float)System.Math.Ceiling(m * 255f) / 255f;

            float inv = 1f / (maxRange * a);
            bytes[i * 4    ] = (byte)System.Math.Clamp((int)(r * inv * 255f + 0.5f), 0, 255);
            bytes[i * 4 + 1] = (byte)System.Math.Clamp((int)(g * inv * 255f + 0.5f), 0, 255);
            bytes[i * 4 + 2] = (byte)System.Math.Clamp((int)(b * inv * 255f + 0.5f), 0, 255);
            bytes[i * 4 + 3] = (byte)System.Math.Clamp((int)(a * 255f + 0.5f), 0, 255);
        }
        return bytes;
    }

    /// <summary>
    /// Tonemapped + gamma-encoded 8-bit RGB, for <b>previews only</b>. This bakes in Reinhard
    /// tonemapping and gamma, which is wrong for storing an actual lightmap (the lightmap must stay
    /// linear HDR and be tonemapped with the rest of the scene at runtime). For storage use
    /// <see cref="ReadHDRRGBA"/> (float) or <see cref="ReadRGBM"/> (compact 8-bit HDR).
    /// </summary>
    public byte[] ReadLDR(float exposure = 1f, float gamma = 1f / 2.2f)
    {
        int n = Width * Height;
        var bytes = new byte[n * 3];
        for (int i = 0; i < n; i++)
        {
            float r = PixelsRGB[i * 3    ] * exposure;
            float g = PixelsRGB[i * 3 + 1] * exposure;
            float b = PixelsRGB[i * 3 + 2] * exposure;
            // tonemap (Reinhard) + gamma
            r = r / (1f + r);
            g = g / (1f + g);
            b = b / (1f + b);
            r = (float)System.Math.Pow(System.Math.Max(0f, r), gamma);
            g = (float)System.Math.Pow(System.Math.Max(0f, g), gamma);
            b = (float)System.Math.Pow(System.Math.Max(0f, b), gamma);
            bytes[i * 3    ] = (byte)System.Math.Clamp((int)(r * 255f + 0.5f), 0, 255);
            bytes[i * 3 + 1] = (byte)System.Math.Clamp((int)(g * 255f + 0.5f), 0, 255);
            bytes[i * 3 + 2] = (byte)System.Math.Clamp((int)(b * 255f + 0.5f), 0, 255);
        }
        return bytes;
    }
}
