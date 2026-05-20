using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// Linear-RGB texture. The constructor decodes the input bytes through <see cref="InputGamma"/>
/// into a linear <see cref="Float3"/> buffer once; samples after that point are plain bilinear
/// reads with no per-tap <c>Math.Pow</c>. That's the difference between "albedo sampling is
/// negligible" and "albedo sampling is the bake's hottest function".
/// </summary>
/// <remarks>
/// Alpha is intentionally not stored: Photonic's current path tracer doesn't read it. If you
/// need alpha-masked transparency in the future, store a parallel <c>byte[]</c> alpha buffer.
/// </remarks>
public sealed class BakeTexture
{
    /// <summary>Texture name.</summary>
    public string Name { get; }

    /// <summary>Pixel width.</summary>
    public int Width { get; }

    /// <summary>Pixel height.</summary>
    public int Height { get; }

    /// <summary>Source gamma the byte data was decoded with. 2.2 for sRGB, 1.0 for linear.</summary>
    public float InputGamma { get; }

    private readonly Float3[] _linear; // Width*Height, row-major, pre-decoded to linear RGB

    internal BakeTexture(string name, int width, int height, byte[] pixelsRGBA, float inputGamma)
    {
        if (pixelsRGBA.Length != width * height * 4)
            throw new System.ArgumentException("pixel buffer must be width*height*4 bytes (RGBA).", nameof(pixelsRGBA));
        Name = name;
        Width = width;
        Height = height;
        InputGamma = inputGamma;

        _linear = new Float3[width * height];
        bool needsGamma = inputGamma != 1f;
        int n = width * height;
        // Decoding is independent per-pixel, so parallelise: a 4k texture pre-decodes in tens of ms.
        System.Threading.Tasks.Parallel.For(0, n, i =>
        {
            int o = i * 4;
            float r = pixelsRGBA[o    ] / 255f;
            float g = pixelsRGBA[o + 1] / 255f;
            float b = pixelsRGBA[o + 2] / 255f;
            if (needsGamma)
            {
                r = (float)System.Math.Pow(r, inputGamma);
                g = (float)System.Math.Pow(g, inputGamma);
                b = (float)System.Math.Pow(b, inputGamma);
            }
            _linear[i] = new Float3(r, g, b);
        });
    }

    /// <summary>
    /// Point (nearest-neighbour) sample. Use this on the bounce path: the variance washes out
    /// across indirect samples and you save 4 texel fetches + 6 lerps vs bilinear.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public Float3 SampleNearestRGB(float u, float v)
    {
        u = u - (float)System.Math.Floor(u);
        v = v - (float)System.Math.Floor(v);
        int x = (int)(u * Width);  if (x >= Width)  x = Width  - 1;
        int y = (int)(v * Height); if (y >= Height) y = Height - 1;
        return _linear[y * Width + x];
    }

    /// <summary>
    /// Bilinear sample in linear RGB, wrap addressing. Returns <c>(R, G, B, 1)</c>: alpha is
    /// always 1 (see class summary).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public Float4 SampleLinearRGBA(float u, float v)
    {
        u = u - (float)System.Math.Floor(u);
        v = v - (float)System.Math.Floor(v);

        float fx = u * Width  - 0.5f;
        float fy = v * Height - 0.5f;
        int x0 = (int)System.Math.Floor(fx);
        int y0 = (int)System.Math.Floor(fy);
        float tx = fx - x0;
        float ty = fy - y0;

        Float3 c00 = Fetch(x0,     y0    );
        Float3 c10 = Fetch(x0 + 1, y0    );
        Float3 c01 = Fetch(x0,     y0 + 1);
        Float3 c11 = Fetch(x0 + 1, y0 + 1);

        Float3 cx0 = c00 * (1 - tx) + c10 * tx;
        Float3 cx1 = c01 * (1 - tx) + c11 * tx;
        Float3 c = cx0 * (1 - ty) + cx1 * ty;
        return new Float4(c.X, c.Y, c.Z, 1f);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private Float3 Fetch(int x, int y)
    {
        x = Wrap(x, Width);
        y = Wrap(y, Height);
        return _linear[y * Width + x];
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int Wrap(int v, int n)
    {
        int r = v % n;
        return r < 0 ? r + n : r;
    }
}
