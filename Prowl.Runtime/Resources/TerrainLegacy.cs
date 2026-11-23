// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Builds a terrain package from a terrain saved before terrains were packages: 16 bit heights over the heightmap, float
/// splat weights, holes and detail densities over their own grids, and trees placed across the terrain from 0 to 1.
/// </summary>
internal static class TerrainLegacy
{
    private const float MaxHeight = 32766f;

    public static byte[] Package(EchoObject value, SerializationContext ctx, int resolution, float size, float height, int layerCount, int detailCount, string name)
    {
        int samples = resolution * resolution;
        ushort[] heights = new ushort[samples];
        if (Base64(value, "Heights16") is { } raw && raw.Length == samples * 2)
        {
            for (int i = 0; i < samples; i++)
                heights[i] = (ushort)Math.Round(Math.Clamp(BitConverter.ToInt16(raw, i * 2) / MaxHeight, 0f, 1f) * 65535f);
        }
        else if (Base64(value, "Heights") is { } floats && floats.Length == samples * 4)
        {
            for (int i = 0; i < samples; i++)
                heights[i] = (ushort)Math.Round(Math.Clamp(BitConverter.ToSingle(floats, i * 4), 0f, 1f) * 65535f);
        }
        else
            Debug.LogWarning($"TerrainData '{name}' had heights that disagree with its {resolution} heightmap, so its heights were reset.");

        int splat = value.Get("SplatmapResolution")?.IntValue ?? 512;
        TerrainHeightmap map = new(resolution, resolution, heights);
        if (Base64(value, "Holes") is { } holes && holes.Length == splat * splat && Array.IndexOf(holes, (byte)0) >= 0)
        {
            map.Holes = new bool[samples];
            for (int z = 0; z < resolution - 1; z++)
                for (int x = 0; x < resolution - 1; x++)
                    map.Holes[z * resolution + x] = holes[Cell(z, resolution, splat) * splat + Cell(x, resolution, splat)] == 0;
        }

        List<TerrainWeightMap> layers = [];
        if (layerCount > 1 && Base64(value, "Splats") is { } weights && weights.Length == splat * splat * layerCount * 4)
        {
            for (int l = 0; l < layerCount; l++)
            {
                byte[] bytes = new byte[splat * splat];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = (byte)Math.Round(Math.Clamp(BitConverter.ToSingle(weights, (i * layerCount + l) * 4), 0f, 1f) * 255f);
                layers.Add(new TerrainWeightMap(splat, splat, bytes));
            }
        }
        else if (layerCount > 1)
        {
            for (int l = 0; l < layerCount; l++)
            {
                byte[] bytes = new byte[samples];
                if (l == 0)
                    Array.Fill(bytes, (byte)255);
                layers.Add(new TerrainWeightMap(resolution, resolution, bytes));
            }
        }

        int detail = value.Get("DetailResolution")?.IntValue ?? value.Get("GrassmapResolution")?.IntValue ?? 1024;
        List<TerrainWeightMap> details = [];
        EchoObject? stored = value.Get("DetailLayers");
        for (int d = 0; d < detailCount; d++)
        {
            string? text = stored != null && d < stored.List.Count ? stored.List[d]?.StringValue : null;
            byte[] bytes = text != null ? Convert.FromBase64String(text) : [];
            details.Add(bytes.Length == detail * detail ? new TerrainWeightMap(detail, detail, bytes) : new TerrainWeightMap(1, 1, [0]));
        }

        List<TerrainTree> trees = [];
        if (value.Get("Trees") is { } list)
            foreach (EchoObject t in list.List)
            {
                float scale = t.Get("Scale")?.FloatValue ?? 1f;
                Color tint = t.Get("Tint") is { } stored2 ? Serializer.Deserialize<Color>(stored2, ctx) : Color.White;
                if (tint == default)
                    tint = Color.White;
                uint packed = Byte(tint.R) | Byte(tint.G) << 8 | Byte(tint.B) << 16 | Byte(tint.A) << 24;
                trees.Add(new TerrainTree(t.Get("Proto")?.IntValue ?? 0,
                    new Double3((t.Get("PosX")?.FloatValue ?? 0f) * size, 0, (t.Get("PosY")?.FloatValue ?? 0f) * size),
                    t.Get("Rot")?.FloatValue ?? 0f, t.Get("WS")?.FloatValue ?? scale, t.Get("HS")?.FloatValue ?? scale, packed));
            }

        MemoryStream stream = new();
        TerrainImporter.Import(map, new TerrainImportSettings
        {
            Spacing = size / (resolution - 1),
            Range = height,
            Layers = layers,
            Details = details,
            Trees = trees,
        }, stream);
        return stream.ToArray();
    }

    // The texel of a grid of the given size over the terrain's cells that the cell's middle falls in
    private static int Cell(int cell, int resolution, int grid) => Math.Min((int)((cell + 0.5f) / (resolution - 1) * grid), grid - 1);

    private static uint Byte(float v) => (uint)Math.Clamp((int)Math.Round(v * 255f), 0, 255);

    private static byte[]? Base64(EchoObject value, string key) => value.Get(key)?.StringValue is { } text ? Convert.FromBase64String(text) : null;
}
