using System;
using System.Threading.Tasks;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>A generated RGBA8 texture on the CPU, ready to upload with its mips.</summary>
public sealed class PropImage
{
    public PropImage(int size, bool srgb, byte[] pixels, bool cutout = false, bool normals = false)
    {
        Size = size;
        Srgb = srgb;
        Pixels = pixels;
        Cutout = cutout;
        Normals = normals;
    }

    /// <summary>Pixels along each side, a power of two.</summary>
    public int Size { get; }

    /// <summary>Whether red, green and blue are sRGB colour, else linear data such as a normal map.</summary>
    public bool Srgb { get; }

    /// <summary>Whether alpha is cut out coverage, kept from thinning as the mips shrink.</summary>
    public bool Cutout { get; }

    /// <summary>Whether it is a normal map, each mip renormalized.</summary>
    public bool Normals { get; }

    /// <summary>Rows of RGBA8, the top first.</summary>
    public byte[] Pixels { get; }

    /// <summary>Fills an image from a function of u and v, 0 to 1, giving linear colour and alpha.</summary>
    public static PropImage Generate(int size, bool srgb, Func<double, double, Float4> pixel, bool cutout = false)
    {
        byte[] pixels = new byte[size * size * 4];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                Float4 c = pixel((x + 0.5) / size, (y + 0.5) / size);
                int o = (y * size + x) * 4;
                pixels[o] = Encode(c.X, srgb);
                pixels[o + 1] = Encode(c.Y, srgb);
                pixels[o + 2] = Encode(c.Z, srgb);
                pixels[o + 3] = Encode(c.W, false);
            }
        });
        return new PropImage(size, srgb, pixels, cutout);
    }

    /// <summary>A tangent space normal map from a tiling height field of 0 to 1, steeper the larger the strength.</summary>
    public static PropImage NormalMap(int size, Func<double, double, double> height, double strength)
    {
        double[] h = new double[size * size];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
                h[y * size + x] = height((x + 0.5) / size, (y + 0.5) / size);
        });
        PropImage map = Generate(size, false, (u, v) =>
        {
            int x = (int)(u * size), y = (int)(v * size);
            double dx = h[y * size + (x + 1) % size] - h[y * size + (x + size - 1) % size];
            double dy = h[(y + 1) % size * size + x] - h[(y + size - 1) % size * size + x];
            Float3 n = Float3.Normalize(new Float3((float)(-dx * strength), (float)(dy * strength), 1f));
            return new Float4(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f, 1f);
        });
        return new PropImage(size, false, map.Pixels, normals: true);
    }

    /// <summary>Uploads the image with a full mip chain. Host thread.</summary>
    public Texture Create(Renderer renderer)
    {
        uint mips = (uint)Math.Log2(Size) + 1;
        PixelFormat format = Srgb ? PixelFormat.R8_G8_B8_A8_UNorm_SRgb : PixelFormat.R8_G8_B8_A8_UNorm;
        Texture texture = renderer.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)Size, (uint)Size, mips, 1, format, TextureUsage.Sampled));
        byte[] level = Pixels;
        int side = Size;
        for (uint mip = 0; mip < mips; mip++)
        {
            renderer.Device.UpdateTexture<byte>(texture, level, TextureRegion.Whole(texture, mip, 0));
            if (side > 1)
                (level, side) = (Halve(level, side, mip + 1), side / 2);
        }
        return texture;
    }

    // Averages each 2x2 block in linear, alpha weighting the colour, and with cutout lifts alpha so coverage holds
    private byte[] Halve(byte[] source, int side, uint mip)
    {
        int half = side / 2;
        byte[] result = new byte[half * half * 4];
        for (int y = 0; y < half; y++)
            for (int x = 0; x < half; x++)
            {
                float r = 0, g = 0, b = 0, a = 0, w = 0;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int o = ((y * 2 + j) * side + x * 2 + i) * 4;
                        float alpha = source[o + 3] / 255f, weight = Cutout ? alpha : 1f;
                        r += Decode(source[o], Srgb) * weight;
                        g += Decode(source[o + 1], Srgb) * weight;
                        b += Decode(source[o + 2], Srgb) * weight;
                        a += alpha;
                        w += weight;
                    }
                int d = (y * half + x) * 4;
                if (w > 0)
                {
                    result[d] = Encode(r / w, Srgb);
                    result[d + 1] = Encode(g / w, Srgb);
                    result[d + 2] = Encode(b / w, Srgb);
                }
                else
                {
                    int o = (y * 2 * side + x * 2) * 4;
                    (result[d], result[d + 1], result[d + 2]) = (source[o], source[o + 1], source[o + 2]);
                }
                result[d + 3] = Encode(Cutout ? MathF.Min(a / 4f * (1f + mip * 0.35f), 1f) : a / 4f, false);
            }
        if (Normals)
            Renormalize(result);
        return result;
    }

    private static void Renormalize(byte[] pixels)
    {
        for (int o = 0; o < pixels.Length; o += 4)
        {
            Float3 n = new(pixels[o] / 127.5f - 1f, pixels[o + 1] / 127.5f - 1f, pixels[o + 2] / 127.5f - 1f);
            float length = Float3.Length(n);
            if (length < 1e-4f)
                continue;
            n /= length;
            pixels[o] = Encode(n.X * 0.5f + 0.5f, false);
            pixels[o + 1] = Encode(n.Y * 0.5f + 0.5f, false);
            pixels[o + 2] = Encode(n.Z * 0.5f + 0.5f, false);
        }
    }

    private static float Decode(byte value, bool srgb)
    {
        float c = value / 255f;
        return srgb ? (c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f)) : c;
    }

    private static byte Encode(float value, bool srgb)
    {
        float c = Math.Clamp(value, 0f, 1f);
        if (srgb)
            c = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
        return (byte)MathF.Round(c * 255f);
    }
}
