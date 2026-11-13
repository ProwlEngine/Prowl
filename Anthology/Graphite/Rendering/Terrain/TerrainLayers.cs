using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>One material layer of a terrain: two square RGBA8 textures of the same size and how it tiles and blends.</summary>
public sealed class TerrainLayerDesc
{
    /// <summary>Texels a side, a power of two.</summary>
    public required int Size { get; init; }

    /// <summary>Albedo in sRGB, and the surface's height in alpha, 0 low to 255 high.</summary>
    public required byte[] Albedo { get; init; }

    /// <summary>The tangent space normal's x and y, roughness, and occlusion.</summary>
    public required byte[] Surface { get; init; }

    /// <summary>The metres one tile of the textures spans.</summary>
    public float TileSize { get; init; } = 4f;

    /// <summary>How far the layer's height lets it show through its neighbours in a blend, 0 for a plain fade.</summary>
    public float HeightBlend { get; init; } = 0.5f;

    /// <summary>Samples the textures through randomly turned and moved hexagons so their tiling never shows, at three times the samples.</summary>
    public bool HexTiling { get; init; } = true;

    /// <summary>How strongly broad light and dark patches vary the albedo, 0 for none.</summary>
    public float MacroVariation { get; init; } = 0.3f;

    /// <summary>The grass group it grows, every <see cref="TerrainGrassType"/> of that group, -1 for none, and how densely at full weight, 0 to 1.</summary>
    public int Grass { get; init; } = -1;
    public float GrassDensity { get; init; } = 1f;
}

/// <summary>The terrain's material layers on the GPU: texture arrays with mips, one layer a slice, a texel of data each, and each one's grass under.</summary>
public sealed class TerrainLayers : IDisposable
{
    public const int MaxLayers = 64;

    public TerrainLayers(Renderer renderer, IReadOnlyList<TerrainLayerDesc> layers)
    {
        if (layers.Count == 0 || layers.Count > MaxLayers)
            throw new ArgumentOutOfRangeException(nameof(layers), $"A terrain takes 1 to {MaxLayers} layers.");
        int size = layers[0].Size;
        foreach (TerrainLayerDesc layer in layers)
        {
            if (layer.Size != size || (size & (size - 1)) != 0 || layer.Albedo.Length != size * size * 4 || layer.Surface.Length != size * size * 4)
                throw new ArgumentException("Every layer needs two RGBA8 textures of one power of two size.", nameof(layers));
        }

        GraphicsDevice device = renderer.Device;
        uint mips = (uint)Math.Log2(size) + 1;
        uint count = (uint)layers.Count;
        Albedo = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)size, (uint)size, mips, count, PixelFormat.R8_G8_B8_A8_UNorm_SRgb, TextureUsage.Sampled));
        Surface = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)size, (uint)size, mips, count, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        Data = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(MaxLayers, 2, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        Albedo.Name = "Graphite Terrain Albedo";
        Surface.Name = "Graphite Terrain Surface";

        float[] data = new float[MaxLayers * 2 * 4];
        for (int i = 0; i < MaxLayers; i++)
            data[(MaxLayers + i) * 4] = -1f;
        for (int i = 0; i < layers.Count; i++)
        {
            TerrainLayerDesc layer = layers[i];
            Upload(device, Albedo, (uint)i, layer.Albedo, size, srgb: true);
            Upload(device, Surface, (uint)i, layer.Surface, size, srgb: false);
            data[i * 4] = 1f / MathF.Max(layer.TileSize, 1e-3f);
            data[i * 4 + 1] = MathF.Max(layer.HeightBlend, 0f);
            data[i * 4 + 2] = layer.HexTiling ? 1f : 0f;
            data[i * 4 + 3] = MathF.Max(layer.MacroVariation, 0f);
            data[(MaxLayers + i) * 4] = layer.Grass;
            data[(MaxLayers + i) * 4 + 1] = Math.Clamp(layer.GrassDensity, 0f, 1f);
        }
        device.UpdateTexture<float>(Data, data, TextureRegion.Whole(Data, 0, 0));
        Count = layers.Count;
    }

    public int Count { get; }

    public Texture Albedo { get; }
    public Texture Surface { get; }
    public Texture Data { get; }

    // Uploads a layer and its mips, each a box filter of the one above, colour averaged in linear light
    private static void Upload(GraphicsDevice device, Texture texture, uint layer, byte[] pixels, int size, bool srgb)
    {
        byte[] level = pixels;
        for (uint mip = 0; ; mip++)
        {
            device.UpdateTexture<byte>(texture, level, TextureRegion.Whole(texture, mip, layer));
            if (size == 1)
                break;
            int half = size / 2;
            byte[] next = new byte[half * half * 4];
            for (int y = 0; y < half; y++)
                for (int x = 0; x < half; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        float sum = 0f;
                        for (int k = 0; k < 4; k++)
                        {
                            byte v = level[(((y * 2 + (k >> 1)) * size) + x * 2 + (k & 1)) * 4 + c];
                            sum += srgb && c < 3 ? ToLinear(v) : v / 255f;
                        }
                        sum *= 0.25f;
                        next[(y * half + x) * 4 + c] = (byte)MathF.Round((srgb && c < 3 ? ToSrgb(sum) : sum) * 255f);
                    }
            level = next;
            size = half;
        }
    }

    private static float ToLinear(byte v)
    {
        float c = v / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private static float ToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    public void Dispose()
    {
        Albedo.Dispose();
        Surface.Dispose();
        Data.Dispose();
    }
}
