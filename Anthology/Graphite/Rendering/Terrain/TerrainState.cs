using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>A terrain's edits at one moment, its edited pages, quarter bounds and trees, kept by reference to put back whole.</summary>
public sealed class TerrainState
{
    internal TerrainState(Dictionary<TerrainPageKey, TerrainPage> edited, Dictionary<TerrainPageKey, ushort[]> bounds, TerrainTree[] trees)
    {
        Edited = edited;
        Bounds = bounds;
        Trees = trees;
    }

    internal Dictionary<TerrainPageKey, TerrainPage> Edited { get; }
    internal Dictionary<TerrainPageKey, ushort[]> Bounds { get; }
    internal TerrainTree[] Trees { get; }
}

/// <summary>The finest level of a terrain as the importer takes it, to change what the package cannot and build it again.</summary>
public sealed class TerrainExport
{
    public required TerrainHeightmap Heights { get; init; }
    public required TerrainImportSettings Settings { get; init; }
}

public sealed partial class TerrainData
{
    /// <summary>Writes the package as it stands, edits and trees included, to a seekable stream.</summary>
    public void Save(Stream stream)
    {
        TerrainPackage.Writer writer = TerrainPackage.Begin(stream, Header);
        foreach (TerrainPageKey key in SortedKeys())
            writer.Add(ReadPage(key)!, _entries[key].Bounds);
        writer.Finish(Trees);
    }

    /// <summary>The edits as they stand, for <see cref="Restore"/>.</summary>
    public TerrainState Capture()
    {
        lock (_editLock)
        {
            Dictionary<TerrainPageKey, TerrainPage> edited = new();
            foreach (TerrainPageKey key in _edited.Keys)
                edited[key] = _pages[key]!;
            Dictionary<TerrainPageKey, ushort[]> bounds = _entries.ToDictionary(e => e.Key, e => (ushort[])e.Value.Bounds.Clone());
            return new TerrainState(edited, bounds, Trees.ToArray());
        }
    }

    /// <summary>Puts the edits back as they were when captured, raising <see cref="Edited"/> for every page that changes.</summary>
    public void Restore(TerrainState state)
    {
        TerrainEdited? result = null;
        lock (_editLock)
        {
            HashSet<TerrainPageKey> changed = new(_edited.Keys);
            changed.UnionWith(state.Edited.Keys);
            changed.RemoveWhere(key => state.Edited.TryGetValue(key, out TerrainPage? page) && _pages.TryGetValue(key, out TerrainPage? now) && ReferenceEquals(page, now));
            foreach (TerrainPageKey key in changed)
            {
                if (state.Edited.TryGetValue(key, out TerrainPage? page))
                {
                    _edited[key] = 0;
                    _pages[key] = page;
                }
                else
                {
                    _edited.TryRemove(key, out _);
                    _pages.TryRemove(key, out _);
                }
            }
            foreach ((TerrainPageKey key, ushort[] bounds) in state.Bounds)
                Array.Copy(bounds, _entries[key].Bounds, 8);
            if (changed.Count > 0)
                result = Region(changed);
        }
        if (result != null)
            Edited?.Invoke(result);
        if (!Trees.SequenceEqual(state.Trees))
            SetTrees(state.Trees);
    }

    /// <summary>The finest level's heights, holes, layer weights and detail densities, one value per sample, and the trees.</summary>
    public TerrainExport Export()
    {
        int width = Header.QuadsX + 1, depth = Header.QuadsZ + 1, samples = width * depth;
        ushort[] heights = new ushort[samples];
        bool[]? holes = null;
        int layers = Header.SplatMaps > 0 ? Header.Layers : 0;
        byte[][] weights = Enumerable.Range(0, layers).Select(_ => new byte[samples]).ToArray();
        byte[][] details = Enumerable.Range(0, Header.Details).Select(_ => new byte[samples]).ToArray();
        (int pagesX, int pagesZ) = PagesAt(0);
        for (int pz = 0; pz < pagesZ; pz++)
            for (int px = 0; px < pagesX; px++)
            {
                if (ReadPage(new TerrainPageKey(0, px, pz)) is not { } page)
                    continue;
                for (int z = 0; z <= TerrainPage.Quads; z++)
                    for (int x = 0; x <= TerrainPage.Quads; x++)
                    {
                        int i = px * TerrainPage.Quads + x, j = pz * TerrainPage.Quads + z;
                        if (i >= width || j >= depth)
                            continue;
                        int at = j * width + i, index = TerrainPage.Index(x, z);
                        heights[at] = page.Heights[index];
                        if (page.IsHole(index))
                            (holes ??= new bool[samples])[at] = true;
                        for (int l = 0; l < layers; l++)
                            weights[l][at] = (byte)Math.Round(page.Weight(l, index) * 255);
                        for (int d = 0; d < details.Length; d++)
                            details[d][at] = page.Details?[d * TerrainPage.Samples + index] ?? 0;
                    }
            }
        return new TerrainExport
        {
            Heights = new TerrainHeightmap(width, depth, heights) { Holes = holes },
            Settings = new TerrainImportSettings
            {
                Spacing = Header.Spacing,
                Bottom = Header.Bottom,
                Range = Header.Range,
                SplatMaxSpacing = Header.Spacing * (1 << (Header.SplatLevels - 1)),
                Seed = Header.Seed,
                Layers = weights.Select(w => new TerrainWeightMap(width, depth, w)).ToArray(),
                Details = details.Select(d => new TerrainWeightMap(width, depth, d)).ToArray(),
                Trees = Trees.ToArray(),
            },
        };
    }

    private List<TerrainPageKey> SortedKeys()
    {
        List<TerrainPageKey> keys = new(_entries.Keys);
        keys.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        return keys;
    }

    // The world box the pages cover, with the reach of a bake around it as an edit reports
    private TerrainEdited Region(IEnumerable<TerrainPageKey> keys)
    {
        double minX = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxZ = double.MinValue;
        List<TerrainPageKey> list = keys.ToList();
        foreach (TerrainPageKey key in list)
        {
            double size = TerrainPage.Quads * SpacingAt(key.Level);
            minX = Math.Min(minX, key.X * size);
            minZ = Math.Min(minZ, key.Z * size);
            maxX = Math.Max(maxX, (key.X + 1) * size);
            maxZ = Math.Max(maxZ, (key.Z + 1) * size);
        }
        Double2 min = new(Position.X + Math.Max(minX, 0), Position.Z + Math.Max(minZ, 0));
        Double2 max = new(Position.X + Math.Min(maxX, SizeX), Position.Z + Math.Min(maxZ, SizeZ));
        list.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        return new TerrainEdited(min, max, list);
    }
}
