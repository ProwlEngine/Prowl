using System;
using System.Threading.Tasks;

using Prowl.Graphite.Rendering;
using Prowl.Vector;

namespace Prowl.Graphite.Samples.PBRRenderer;

/// <summary>Tiling grass, dirt, rock and snow layers made from noise, so the terrain sample needs no texture files.</summary>
internal static class TerrainTextures
{
    public const int Grass = 0, Dirt = 1, Rock = 2, Snow = 3;

    public static TerrainLayerDesc[] Generate(int size)
    {
        TerrainLayerDesc[] layers =
        [
            Layer(size, 3f, 0.6f, 0.85f, (u, v) =>
            {
                double blades = Fbm(u * 64, v * 64, 4, 64, 11);
                double clumps = Fbm(u * 6, v * 6, 3, 6, 12);
                double h = 0.5 + blades * 0.35 + clumps * 0.25;
                return (h, Mix((0.05, 0.10, 0.02), (0.13, 0.20, 0.05), clumps * 0.5 + 0.5 + blades * 0.3));
            }),
            Layer(size, 3f, 0.8f, 0.9f, (u, v) =>
            {
                double pebbles = 1 - Cells(u * 14, v * 14, 14, 21);
                double grain = Fbm(u * 48, v * 48, 3, 48, 22);
                double h = Math.Clamp(Math.Pow(pebbles, 3) * 0.9 + grain * 0.15 + 0.1, 0, 1);
                return (h, Mix((0.17, 0.12, 0.08), (0.32, 0.25, 0.18), pebbles * 0.8 + grain * 0.3));
            }),
            Layer(size, 6f, 1.0f, 0.7f, (u, v) =>
            {
                double ridges = 0, amplitude = 0.5;
                for (int o = 0; o < 5; o++)
                {
                    int period = 4 << o;
                    double n = 1 - Math.Abs(Noise(u * period, v * period * 2.5, period, (int)(period * 2.5), 31 + o));
                    ridges += n * n * amplitude;
                    amplitude *= 0.5;
                }
                double cracks = Math.Pow(Cells(u * 6, v * 6, 6, 32), 0.4);
                double h = Math.Clamp(ridges * 0.8 * cracks + 0.1, 0, 1);
                double tone = Fbm(u * 3, v * 3, 3, 3, 33) * 0.5 + 0.5;
                return (h, Mix((0.10, 0.095, 0.09), (0.26, 0.245, 0.23), h * 0.7 + tone * 0.3));
            }),
            Layer(size, 8f, 0.3f, 0.5f, (u, v) =>
            {
                double drifts = Fbm(u * 4, v * 4, 4, 4, 41);
                double h = 0.5 + drifts * 0.4;
                return (h, Mix((0.78, 0.82, 0.88), (0.93, 0.95, 0.97), h));
            }),
        ];
        layers[Grass] = Grows(layers[Grass], 1f);
        layers[Dirt] = Grows(layers[Dirt], 0.25f);
        return layers;
    }

    private static TerrainLayerDesc Grows(TerrainLayerDesc layer, float density) => new()
    {
        Size = layer.Size,
        Albedo = layer.Albedo,
        Surface = layer.Surface,
        TileSize = layer.TileSize,
        HeightBlend = layer.HeightBlend,
        Grass = 0,
        GrassDensity = density,
    };

    /// <summary>Cards of thin blades out to 100 m.</summary>
    public static TerrainGrassType[] GrassTypes =>
    [
        new TerrainGrassType { Density = 16, Range = 100, MinHeight = 0.25f, MaxHeight = 0.6f, Width = 0.7f, Card = 0, NoiseSize = 1.5f },
    ];

    /// <summary>A card of thin tapering blades in light grey, lit and tinted by the grass type, with its mips.</summary>
    public static Texture GrassCard(Renderer renderer, int size)
    {
        float[] cover = new float[size * size];
        float[] tone = new float[size * size];
        Random random = new(5);
        for (int blade = 0; blade < 32; blade++)
        {
            float root = 0.08f + random.NextSingle() * 0.84f, lean = (random.NextSingle() - 0.5f) * 0.35f;
            float top = 0.6f + random.NextSingle() * 0.4f, width = 0.01f + random.NextSingle() * 0.008f;
            float shade = 0.7f + random.NextSingle() * 0.3f;
            for (int y = 0; y < size; y++)
            {
                float t = 1f - (y + 0.5f) / size;
                if (t > top)
                    continue;
                float along = t / top;
                float centre = root + lean * along * along;
                float half = width * (1f - along * 0.95f);
                for (int x = 0; x < size; x++)
                {
                    float d = MathF.Abs((x + 0.5f) / size - centre);
                    if (d > half)
                        continue;
                    int i = y * size + x;
                    cover[i] = 1f;
                    tone[i] = shade * (0.75f + 0.25f * along);
                }
            }
        }

        uint mips = (uint)Math.Log2(size) + 1;
        Texture texture = renderer.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)size, (uint)size, mips, 1, PixelFormat.R8_G8_B8_A8_UNorm_SRgb, TextureUsage.Sampled));
        for (uint mip = 0; mip < mips; mip++)
        {
            int side = Math.Max(size >> (int)mip, 1), step = size / side;
            byte[] pixels = new byte[side * side * 4];
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    float c = 0f, s = 0f;
                    for (int j = 0; j < step; j++)
                        for (int k = 0; k < step; k++)
                        {
                            int i = (y * step + j) * size + x * step + k;
                            c += cover[i];
                            s += tone[i];
                        }
                    float n = step * step;
                    float alpha = MathF.Min(c / n * (1f + mip * 0.6f), 1f);
                    float value = c > 0 ? s / c : 0.8f;
                    int o = (y * side + x) * 4;
                    pixels[o] = pixels[o + 1] = pixels[o + 2] = Srgb(value);
                    pixels[o + 3] = (byte)MathF.Round(alpha * 255f);
                }
            renderer.Device.UpdateTexture<byte>(texture, pixels, TextureRegion.Whole(texture, mip, 0));
        }
        return texture;
    }

    private static TerrainLayerDesc Layer(int size, float tile, float bumpiness, float roughness, Func<double, double, (double Height, (double R, double G, double B) Color)> texel)
    {
        double[] heights = new double[size * size];
        byte[] albedo = new byte[size * size * 4];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                (double h, (double r, double g, double b)) = texel(x / (double)size, y / (double)size);
                int i = y * size + x;
                heights[i] = h;
                albedo[i * 4] = Srgb(r);
                albedo[i * 4 + 1] = Srgb(g);
                albedo[i * 4 + 2] = Srgb(b);
                albedo[i * 4 + 3] = (byte)Math.Round(Math.Clamp(h, 0, 1) * 255);
            }
        });

        // Normals from the wrapped height slopes, the height's tile size in texels across bumpiness metres of relief
        byte[] surface = new byte[size * size * 4];
        double strength = bumpiness * size / 64.0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = heights[y * size + (x + 1) % size] - heights[y * size + (x + size - 1) % size];
                double dy = heights[(y + 1) % size * size + x] - heights[(y + size - 1) % size * size + x];
                double nx = -dx * strength, ny = -dy * strength, length = Math.Sqrt(nx * nx + ny * ny + 1);
                int i = (y * size + x) * 4;
                double occlusion = 0.6 + 0.4 * heights[y * size + x];
                surface[i] = (byte)Math.Round((nx / length * 0.5 + 0.5) * 255);
                surface[i + 1] = (byte)Math.Round((ny / length * 0.5 + 0.5) * 255);
                surface[i + 2] = (byte)Math.Round(roughness * 255);
                surface[i + 3] = (byte)Math.Round(Math.Clamp(occlusion, 0, 1) * 255);
            }
        return new TerrainLayerDesc { Size = size, Albedo = albedo, Surface = surface, TileSize = tile, HeightBlend = 0.6f };
    }

    private static (double, double, double) Mix((double R, double G, double B) a, (double R, double G, double B) b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    }

    private static byte Srgb(double linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        double c = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
        return (byte)Math.Round(c * 255);
    }

    // Fractal value noise that wraps every period cells
    private static double Fbm(double x, double y, int octaves, int period, int seed)
    {
        double sum = 0, amplitude = 0.5;
        for (int o = 0; o < octaves; o++)
        {
            sum += Noise(x, y, period, period, seed + o) * amplitude;
            x *= 2;
            y *= 2;
            period *= 2;
            amplitude *= 0.5;
        }
        return sum;
    }

    private static double Noise(double x, double y, int periodX, int periodY, int seed)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        double tx = x - fx, ty = y - fy;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        int ix = (int)fx, iy = (int)fy;
        double a = Hash(ix, iy, periodX, periodY, seed), b = Hash(ix + 1, iy, periodX, periodY, seed);
        double c = Hash(ix, iy + 1, periodX, periodY, seed), d = Hash(ix + 1, iy + 1, periodX, periodY, seed);
        return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
    }

    // Distance to the nearest of one jittered point a cell, wrapping every period cells, about 0 to 1
    private static double Cells(double x, double y, int period, int seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double nearest = 9;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                double px = ix + i + Hash(ix + i, iy + j, period, period, seed) * 0.4 + 0.5;
                double py = iy + j + Hash(ix + i, iy + j, period, period, seed + 7) * 0.4 + 0.5;
                nearest = Math.Min(nearest, Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)));
            }
        return Math.Clamp(nearest, 0, 1);
    }

    private static double Hash(int x, int y, int periodX, int periodY, int seed)
    {
        x = ((x % periodX) + periodX) % periodX;
        y = ((y % periodY) + periodY) % periodY;
        uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
        h ^= h >> 13;
        h *= 0x85EBCA6Bu;
        h ^= h >> 16;
        return h / (double)uint.MaxValue * 2 - 1;
    }
}
