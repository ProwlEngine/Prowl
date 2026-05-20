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

    /// <summary>Returns a fresh HDR snapshot of the current pixel data. Read after the job has succeeded.</summary>
    public float[] ReadHDR()
    {
        var copy = new float[PixelsRGB.Length];
        System.Array.Copy(PixelsRGB, copy, copy.Length);
        return copy;
    }

    /// <summary>Returns 8-bit/channel RGB pixels gamma-encoded with <paramref name="gamma"/>. Optional exposure pre-multiply.</summary>
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
