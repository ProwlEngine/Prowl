// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GTerrain = Prowl.Graphite.Rendering.TerrainData;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime;

/// <summary>A material layer the splat weights paint: its textures, how far one tile spans, and how it blends.</summary>
public class TerrainLayer
{
    /// <summary>Colour, with the surface's height in alpha for the height blend.</summary>
    public Texture2D? Albedo;
    public Texture2D? NormalMap;

    /// <summary>The metres one tile of the textures spans.</summary>
    public float Tiling = 10f;
    public float Roughness = 1f;

    /// <summary>How far the layer's height lets it show through its neighbours in a blend, 0 for a plain fade.</summary>
    public float HeightBlend = 0.5f;

    /// <summary>Samples the textures through turned and moved hexagons so their tiling never shows.</summary>
    public bool HexTiling = true;

    /// <summary>How strongly broad light and dark patches vary the colour, 0 for none.</summary>
    public float MacroVariation = 0.3f;
}

public enum DetailRenderMode
{
    /// <summary>Crossed cards of <see cref="DetailPrototype.Texture"/>, grown on the GPU.</summary>
    Grass,

    /// <summary>Instances of <see cref="DetailPrototype.Mesh"/>.</summary>
    Mesh,
}

/// <summary>Something the detail densities grow over the ground: grass cards or a mesh.</summary>
public class DetailPrototype
{
    public DetailRenderMode RenderMode = DetailRenderMode.Grass;

    /// <summary>The card a grass detail shows, cut out by its alpha.</summary>
    public Texture2D? Texture;
    public Mesh? Mesh;

    /// <summary>One per submesh of <see cref="Mesh"/>, the default material where one is missing.</summary>
    public List<Material?> Materials = [];

    /// <summary>How many grow per square metre where the density is full.</summary>
    public float Density = 6f;

    /// <summary>The metres from the camera grass reaches.</summary>
    public float Range = 90f;

    /// <summary>The smallest and largest size, in metres for grass and as a scale for meshes.</summary>
    public float MinWidth = 0.7f;
    public float MaxWidth = 0.7f;
    public float MinHeight = 0.25f;
    public float MaxHeight = 0.45f;

    /// <summary>The metres across the patches in which sizes and tints rise and fall.</summary>
    public float NoiseSize = 6f;

    /// <summary>A mesh's tint where the noise is lowest and highest.</summary>
    public Color HealthyColor = Color.White;
    public Color DryColor = Color.White;

    /// <summary>Grass colour at the root and the tip, multiplying its card.</summary>
    public Color RootColor = Color.White;
    public Color TipColor = Color.White;

    /// <summary>How far grass leans toward the ground's colour under it, 0 to 1.</summary>
    public float GroundTint = 0.5f;

    /// <summary>How little the wind moves grass, 0 for fully.</summary>
    public float Stiffness = 0.3f;

    /// <summary>Tilts meshes with the slope of the ground under them.</summary>
    public bool AlignToNormal;
}

/// <summary>A tree placed on the terrain, standing on the ground wherever it is.</summary>
public struct TreeInstance
{
    /// <summary>Where it stands across the terrain, 0 to 1 along x and z.</summary>
    public Float2 Position;
    public int PrototypeIndex;

    /// <summary>Its turn about up in radians.</summary>
    public float Rotation;
    public float WidthScale = 1f;
    public float HeightScale = 1f;

    /// <summary>Multiplies the tree's base colour and coverage. White leaves it as its materials are.</summary>
    public Color Tint = Color.White;

    public TreeInstance() { }
}

public class TreePrototype
{
    public Mesh? Mesh;

    /// <summary>One per submesh of <see cref="Mesh"/>, the first one standing in where one is missing.</summary>
    public List<Material?> Materials = [];
}

/// <summary>
/// A terrain: its layers and prototypes, and a Graphite terrain package holding its heights, splat weights, holes, detail
/// densities and trees. The package is kept inside the asset and edited in place by brushes, see <see cref="Edit"/>.
/// Changing the resolution, size, height, layer count or detail count builds it again from its finest samples.
/// </summary>
[CreateAssetMenu("Terrain Data", Extension = ".terraindata", Order = 3)]
public sealed class TerrainData : Asset, ISerializable
{
    private const int FormatVersion = 2;

    private int _resolution = 513;
    private float _size = 1024f;
    private float _height = 100f;
    private List<TerrainLayer> _layers = [new(), new(), new(), new()];
    private List<DetailPrototype> _detailPrototypes = [new()];
    private List<TreePrototype> _treePrototypes = [];
    private byte[]? _package;
    private GTerrain? _terrain;

    [NotContent] private readonly object _gate = new();
    [NotContent] private int _version;
    [NotContent] private bool _changed;
    [NotContent] private long _bytes;

    /// <summary>The terrain drawing this data, which places it. One terrain draws a data at a time.</summary>
    [NotContent] internal object? Owner;

    /// <summary>Raised after the package is built again or loaded anew, so renderers and physics take the new one.</summary>
    public event Action? Rebuilt;

    public TerrainData()
    {
        Name = "New TerrainData";
    }

    /// <summary>Heightmap samples a side, one more than the quads it spans.</summary>
    public int HeightmapResolution { get { EnsureLoaded(); return _resolution; } }

    /// <summary>The metres the terrain spans along x and z.</summary>
    public float Size
    {
        get { EnsureLoaded(); return _size; }
        set { EnsureLoaded(); if (value != _size) Rebuild(_resolution, MathF.Max(value, 1f), _height); }
    }

    /// <summary>The metres from the lowest height to the highest.</summary>
    public float Height
    {
        get { EnsureLoaded(); return _height; }
        set { EnsureLoaded(); if (value != _height) Rebuild(_resolution, _size, MathF.Max(value, 0.01f)); }
    }

    /// <summary>The metres between neighbouring samples.</summary>
    public float Spacing => Size / (HeightmapResolution - 1);

    public List<TerrainLayer> Layers { get { EnsureLoaded(); return _layers; } }
    public int LayerCount => Layers.Count;
    public List<DetailPrototype> DetailPrototypes { get { EnsureLoaded(); return _detailPrototypes; } }
    public List<TreePrototype> TreePrototypes { get { EnsureLoaded(); return _treePrototypes; } }

    /// <summary>Counts every change to the package, edits and rebuilds alike, for anything caching what it read.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>
    /// The live Graphite terrain, its position set by the terrain drawing it. Built flat when the asset has none. Any
    /// thread, physics queries reaching it from workers.
    /// </summary>
    public GTerrain Terrain
    {
        get
        {
            EnsureLoaded();
            if (_terrain is { } terrain)
                return terrain;
            lock (_gate)
            {
                if (_terrain == null)
                {
                    if (_package != null)
                        Attach(GTerrain.Load(new MemoryStream(_package, writable: false)), _package.Length, false);
                    else
                        Build(Flat(), FlatSettings());
                }
                return _terrain!;
            }
        }
    }

    #region Edits

    /// <summary>Applies a brush at a point in metres from the terrain's corner, see <see cref="TerrainBrush"/>. Flatten's height is in metres above the base.</summary>
    public TerrainEdited Edit(TerrainBrush brush, Float2 local, float radius, float strength, float height = 0f, int layer = 0)
    {
        GTerrain terrain = Terrain;
        Double3 at = terrain.Position;
        return terrain.Apply(new TerrainEdit(brush, new Double2(at.X + local.X, at.Z + local.Y), radius, strength, at.Y + height, layer));
    }

    /// <summary>The package's edits as they stand, for undo.</summary>
    public TerrainSnapshot Capture()
    {
        GTerrain terrain = Terrain;
        return new TerrainSnapshot(terrain, terrain.Capture());
    }

    /// <summary>Puts a capture back, false when the package was built again since and it no longer applies.</summary>
    public bool Restore(TerrainSnapshot snapshot)
    {
        if (_terrain == null || !ReferenceEquals(snapshot.Terrain, _terrain))
            return false;
        _terrain.Restore(snapshot.State);
        return true;
    }

    #endregion

    #region Samples

    /// <summary>The height in metres above the terrain's base at a heightmap sample.</summary>
    public float GetHeight(int x, int z)
    {
        (TerrainPage page, int index) = Sample(x, z);
        return (float)(page.Heights[index] / 65535.0 * Terrain.Header.Range);
    }

    /// <summary>Whether the quad whose lowest corner is the sample is cut out.</summary>
    public bool IsCellHole(int x, int z)
    {
        (TerrainPage page, int index) = Sample(x, z);
        return page.IsHole(index);
    }

    /// <summary>A layer's weight at a heightmap sample, 0 to 1.</summary>
    public float GetSplat(int x, int z, int layer)
    {
        (TerrainPage page, int index) = Sample(x, z);
        return page.Weight(layer, index);
    }

    /// <summary>A detail prototype's density at a heightmap sample, 0 to 1.</summary>
    public float GetDetailDensity(int prototype, int x, int z)
    {
        (TerrainPage page, int index) = Sample(x, z);
        return page.Density(prototype, index);
    }

    /// <summary>The ground height in metres above the base at u and v across the terrain, between samples as it is drawn, holes included.</summary>
    public float GetInterpolatedHeight(float u, float v)
    {
        float fx = Math.Clamp(u, 0f, 1f) * (HeightmapResolution - 1), fz = Math.Clamp(v, 0f, 1f) * (HeightmapResolution - 1);
        int x = Math.Min((int)fx, _resolution - 2), z = Math.Min((int)fz, _resolution - 2);
        return (float)TerrainTriangles.Height(GetHeight(x, z), GetHeight(x + 1, z), GetHeight(x, z + 1), GetHeight(x + 1, z + 1), fx - x, fz - z);
    }

    /// <summary>The ground's normal at u and v across the terrain, between samples as it is drawn.</summary>
    public Float3 GetInterpolatedNormal(float u, float v)
    {
        float fx = Math.Clamp(u, 0f, 1f) * (HeightmapResolution - 1), fz = Math.Clamp(v, 0f, 1f) * (HeightmapResolution - 1);
        int x = Math.Min((int)fx, _resolution - 2), z = Math.Min((int)fz, _resolution - 2);
        (double nx, double ny, double nz) = TerrainTriangles.Normal(GetHeight(x, z), GetHeight(x + 1, z), GetHeight(x, z + 1), GetHeight(x + 1, z + 1), fx - x, fz - z, Spacing);
        return new Float3((float)nx, (float)ny, (float)nz);
    }

    /// <summary>Whether the ground at u and v across the terrain is cut out.</summary>
    public bool IsHoleAt(float u, float v)
    {
        int x = Math.Min((int)(Math.Clamp(u, 0f, 1f) * (HeightmapResolution - 1)), _resolution - 2);
        int z = Math.Min((int)(Math.Clamp(v, 0f, 1f) * (HeightmapResolution - 1)), _resolution - 2);
        return IsCellHole(x, z);
    }

    private (TerrainPage Page, int Index) Sample(int x, int z)
    {
        GTerrain terrain = Terrain;
        x = Math.Clamp(x, 0, _resolution - 1);
        z = Math.Clamp(z, 0, _resolution - 1);
        (int pagesX, int pagesZ) = terrain.PagesAt(0);
        int px = Math.Min(x / TerrainPage.Quads, pagesX - 1), pz = Math.Min(z / TerrainPage.Quads, pagesZ - 1);
        TerrainPage page = terrain.GetPage(new TerrainPageKey(0, px, pz))!;
        return (page, TerrainPage.Index(x - px * TerrainPage.Quads, z - pz * TerrainPage.Quads));
    }

    /// <summary>Every sample's height from 0 at the base to 1 at the top, row by row from z 0.</summary>
    public float[] GetHeights() => Array.ConvertAll(Terrain.Export().Heights.Samples, s => s / 65535f);

    /// <summary>Replaces every sample's height, from 0 at the base to 1 at the top, row by row from z 0.</summary>
    public void SetHeights(float[] heights)
    {
        EnsureLoaded();
        if (heights.Length != _resolution * _resolution)
            throw new ArgumentException($"A {_resolution} heightmap takes {_resolution * _resolution} heights.", nameof(heights));
        TerrainExport export = Terrain.Export();
        ushort[] samples = Array.ConvertAll(heights, h => (ushort)Math.Round(Math.Clamp(h, 0f, 1f) * 65535f));
        Build(new TerrainHeightmap(_resolution, _resolution, samples) { Holes = export.Heights.Holes }, export.Settings);
    }

    #endregion

    #region Trees

    /// <summary>The trees, positions across the terrain from 0 to 1.</summary>
    public IReadOnlyList<TreeInstance> Trees => Terrain.Trees.Select(ToInstance).ToArray();

    public void SetTrees(IEnumerable<TreeInstance> trees) => Terrain.SetTrees(trees.Select(ToTree).ToArray());

    public void AddTree(TreeInstance tree) => Terrain.AddTree(ToTree(tree));

    public void AddTrees(IEnumerable<TreeInstance> trees) => Terrain.AddTrees(trees.Select(ToTree).ToArray());

    /// <summary>Removes the trees within a radius of a point across the terrain, both from 0 to 1, returning how many.</summary>
    public int RemoveTrees(Float2 center, float radius)
    {
        GTerrain terrain = Terrain;
        return terrain.RemoveTrees(new Double2(terrain.Position.X + center.X * _size, terrain.Position.Z + center.Y * _size), radius * _size);
    }

    private TerrainTree ToTree(TreeInstance tree) =>
        new(tree.PrototypeIndex, new Double3(tree.Position.X * _size, 0, tree.Position.Y * _size), tree.Rotation, tree.WidthScale, tree.HeightScale, Pack(tree.Tint));

    private TreeInstance ToInstance(TerrainTree tree)
    {
        Float4 tint = tree.Tint;
        return new TreeInstance
        {
            Position = new Float2((float)(tree.Position.X / _size), (float)(tree.Position.Z / _size)),
            PrototypeIndex = tree.Prototype,
            Rotation = tree.Yaw,
            WidthScale = tree.WidthScale,
            HeightScale = tree.HeightScale,
            Tint = new Color(tint.X, tint.Y, tint.Z, tint.W),
        };
    }

    private static uint Pack(Color c)
    {
        static uint Byte(float v) => (uint)Math.Clamp((int)Math.Round(v * 255f), 0, 255);
        return Byte(c.R) | Byte(c.G) << 8 | Byte(c.B) << 16 | Byte(c.A) << 24;
    }

    #endregion

    #region Rebuilds

    /// <summary>Changes the samples a side, resampling the heights, weights, holes and densities onto the new grid.</summary>
    public void ResizeHeightmap(int resolution)
    {
        EnsureLoaded();
        resolution = Math.Clamp(resolution, 33, 8193);
        if (resolution != _resolution)
            Rebuild(resolution, _size, _height);
    }

    /// <summary>Adds a layer, weighing nothing anywhere until painted.</summary>
    public void AddLayer(TerrainLayer layer)
    {
        EnsureLoaded();
        if (_layers.Count >= TerrainLayers.MaxLayers)
            return;
        TerrainExport export = Terrain.Export();
        List<TerrainWeightMap> weights = [.. Weights(export)];
        _layers.Add(layer);
        while (weights.Count < _layers.Count)
            weights.Add(Empty(export));
        Build(export.Heights, With(export.Settings, weights, null));
    }

    /// <summary>Removes a layer, its weight shared among the rest, keeping one.</summary>
    public void RemoveLayer(int index)
    {
        EnsureLoaded();
        if (_layers.Count <= 1 || (uint)index >= (uint)_layers.Count)
            return;
        TerrainExport export = Terrain.Export();
        List<TerrainWeightMap> weights = [.. Weights(export)];
        weights.RemoveAt(index);
        _layers.RemoveAt(index);
        Build(export.Heights, With(export.Settings, weights, null));
    }

    public void AddDetailPrototype(DetailPrototype prototype)
    {
        EnsureLoaded();
        TerrainExport export = Terrain.Export();
        _detailPrototypes.Add(prototype);
        Build(export.Heights, With(export.Settings, null, [.. export.Settings.Details, Empty(export)]));
    }

    public void RemoveDetailPrototype(int index)
    {
        EnsureLoaded();
        if ((uint)index >= (uint)_detailPrototypes.Count)
            return;
        TerrainExport export = Terrain.Export();
        List<TerrainWeightMap> details = [.. export.Settings.Details];
        details.RemoveAt(index);
        _detailPrototypes.RemoveAt(index);
        Build(export.Heights, With(export.Settings, null, details));
    }

    /// <summary>Removes a tree prototype with its trees, the trees of later prototypes following their prototype down.</summary>
    public void RemoveTreePrototype(int index)
    {
        EnsureLoaded();
        if ((uint)index >= (uint)_treePrototypes.Count)
            return;
        _treePrototypes.RemoveAt(index);
        Terrain.SetTrees(Terrain.Trees.Where(t => t.Prototype != index).Select(t => t.Prototype > index ? t with { Prototype = t.Prototype - 1 } : t).ToArray());
    }

    private void Rebuild(int resolution, float size, float height)
    {
        if (_terrain == null && _package == null)
        {
            (_resolution, _size, _height) = (resolution, size, height);
            return;
        }
        TerrainExport export = Terrain.Export();
        int from = _resolution;
        float oldSize = _size;
        (_resolution, _size, _height) = (resolution, size, height);
        TerrainHeightmap map = from == resolution ? export.Heights : Resample(export.Heights, resolution);
        IReadOnlyList<TerrainWeightMap> layers = from == resolution ? export.Settings.Layers : export.Settings.Layers.Select(m => Resample(m, resolution)).ToArray();
        IReadOnlyList<TerrainWeightMap> details = from == resolution ? export.Settings.Details : export.Settings.Details.Select(m => Resample(m, resolution)).ToArray();
        IReadOnlyList<TerrainTree> trees = export.Settings.Trees;
        if (size != oldSize)
            trees = trees.Select(t => t with { Position = t.Position * (size / (double)oldSize) }).ToArray();
        Build(map, new TerrainImportSettings
        {
            Spacing = size / (resolution - 1),
            Range = height,
            Seed = export.Settings.Seed,
            Layers = layers,
            Details = details,
            Trees = trees,
        });
    }

    private TerrainImportSettings With(TerrainImportSettings settings, IReadOnlyList<TerrainWeightMap>? layers, IReadOnlyList<TerrainWeightMap>? details) => new()
    {
        Spacing = _size / (_resolution - 1),
        Range = _height,
        Seed = settings.Seed,
        Layers = layers ?? settings.Layers,
        Details = details ?? settings.Details,
        Trees = settings.Trees,
    };

    // The layer weights an export holds, every sample on layer 0 when the package keeps none
    private IReadOnlyList<TerrainWeightMap> Weights(TerrainExport export)
    {
        if (export.Settings.Layers.Count > 0)
            return export.Settings.Layers.Take(_layers.Count).ToArray();
        byte[] full = new byte[export.Heights.Width * export.Heights.Height];
        Array.Fill(full, (byte)255);
        return [new TerrainWeightMap(export.Heights.Width, export.Heights.Height, full)];
    }

    private static TerrainWeightMap Empty(TerrainExport export) =>
        new(export.Heights.Width, export.Heights.Height, new byte[export.Heights.Width * export.Heights.Height]);

    private static TerrainHeightmap Resample(TerrainHeightmap map, int resolution)
    {
        ushort[] samples = new ushort[resolution * resolution];
        bool[]? holes = map.Holes != null ? new bool[resolution * resolution] : null;
        double scale = (map.Width - 1) / (double)(resolution - 1);
        for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                double fx = x * scale, fz = z * scale;
                int x0 = Math.Min((int)fx, map.Width - 2), z0 = Math.Min((int)fz, map.Height - 2);
                double tx = fx - x0, tz = fz - z0;
                double a = map.Samples[z0 * map.Width + x0], b = map.Samples[z0 * map.Width + x0 + 1];
                double c = map.Samples[(z0 + 1) * map.Width + x0], d = map.Samples[(z0 + 1) * map.Width + x0 + 1];
                samples[z * resolution + x] = (ushort)Math.Round((a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz);
                if (holes != null)
                    holes[z * resolution + x] = map.Holes![(int)Math.Round(fz) * map.Width + (int)Math.Round(fx)];
            }
        return new TerrainHeightmap(resolution, resolution, samples) { Holes = holes };
    }

    // A map of one value per sample onto a grid of the given samples a side, its corners staying on the terrain's corners
    private static TerrainWeightMap Resample(TerrainWeightMap map, int resolution)
    {
        byte[] values = new byte[resolution * resolution];
        double scale = (map.Width - 1) / (double)(resolution - 1);
        for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                double fx = x * scale, fz = z * scale;
                int x0 = Math.Min((int)fx, map.Width - 2), z0 = Math.Min((int)fz, map.Height - 2);
                double tx = fx - x0, tz = fz - z0;
                double a = map.Values[z0 * map.Width + x0], b = map.Values[z0 * map.Width + x0 + 1];
                double c = map.Values[(z0 + 1) * map.Width + x0], d = map.Values[(z0 + 1) * map.Width + x0 + 1];
                values[z * resolution + x] = (byte)Math.Round((a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz);
            }
        return new TerrainWeightMap(resolution, resolution, values);
    }

    private TerrainHeightmap Flat() => new(_resolution, _resolution, new ushort[_resolution * _resolution]);

    private TerrainImportSettings FlatSettings()
    {
        int samples = _resolution * _resolution;
        List<TerrainWeightMap> layers = [];
        if (_layers.Count > 1)
        {
            byte[] full = new byte[samples];
            Array.Fill(full, (byte)255);
            layers.Add(new TerrainWeightMap(_resolution, _resolution, full));
            for (int l = 1; l < _layers.Count; l++)
                layers.Add(new TerrainWeightMap(_resolution, _resolution, new byte[samples]));
        }
        return new TerrainImportSettings
        {
            Spacing = _size / (_resolution - 1),
            Range = _height,
            Layers = layers,
            Details = _detailPrototypes.Select(_ => new TerrainWeightMap(_resolution, _resolution, new byte[samples])).ToArray(),
        };
    }

    private void Build(TerrainHeightmap map, TerrainImportSettings settings)
    {
        if (settings.Layers.Count == 1)
            settings = new TerrainImportSettings { Spacing = settings.Spacing, Range = settings.Range, Seed = settings.Seed, Details = settings.Details, Trees = settings.Trees };
        MemoryStream stream = new();
        TerrainImporter.Import(map, settings, stream);
        stream.Position = 0;
        lock (_gate)
        {
            _package = null;
            Attach(GTerrain.Load(stream), stream.Length, true);
        }
    }

    // Takes a new package, keeping where the old one stood. The old one reads from memory, so it needs no disposing and
    // whatever still streams from it finishes on what it held.
    private void Attach(GTerrain terrain, long bytes, bool changed)
    {
        GTerrain? old = _terrain;
        if (old != null)
        {
            terrain.Position = old.Position;
            Detach(old);
        }
        terrain.Edited += OnEdited;
        terrain.TreesChanged += OnTreesChanged;
        _terrain = terrain;
        _bytes = bytes;
        _changed = changed;
        Interlocked.Increment(ref _version);
        Rebuilt?.Invoke();
    }

    private void Detach(GTerrain terrain)
    {
        terrain.Edited -= OnEdited;
        terrain.TreesChanged -= OnTreesChanged;
    }

    private void OnEdited(TerrainEdited edited)
    {
        _changed = true;
        Interlocked.Increment(ref _version);
    }

    private void OnTreesChanged()
    {
        _changed = true;
        Interlocked.Increment(ref _version);
    }

    #endregion

    protected override void TakeContent(Asset staging)
    {
        base.TakeContent(staging);
        _terrain = null;
        _changed = false;
        _bytes = _package?.Length ?? 0;
        Interlocked.Increment(ref _version);
        Rebuilt?.Invoke();
    }

    // Lets go of the package without disposing it, as a surface still drawing it may read from it until it is replaced
    protected override void OnUnload()
    {
        if (_terrain != null)
            Detach(_terrain);
        _terrain = null;
    }

    protected internal override long EstimateBytes() => _bytes;

    #region Serialization

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        value.Add("Name", new EchoObject(Name));
        value.Add("Format", new EchoObject(FormatVersion));
        value.Add("HeightmapResolution", new EchoObject(HeightmapResolution));
        value.Add("Size", new EchoObject(Size));
        value.Add("Height", new EchoObject(Height));

        EchoObject layers = EchoObject.NewList();
        foreach (TerrainLayer l in Layers)
        {
            EchoObject o = EchoObject.NewCompound();
            o.Add("Albedo", Serializer.Serialize(l.Albedo, ctx));
            o.Add("NormalMap", Serializer.Serialize(l.NormalMap, ctx));
            o.Add("Tiling", new EchoObject(l.Tiling));
            o.Add("Roughness", new EchoObject(l.Roughness));
            o.Add("HeightBlend", new EchoObject(l.HeightBlend));
            o.Add("HexTiling", new EchoObject(l.HexTiling));
            o.Add("MacroVariation", new EchoObject(l.MacroVariation));
            layers.ListAdd(o);
        }
        value.Add("Layers", layers);

        EchoObject details = EchoObject.NewList();
        foreach (DetailPrototype d in DetailPrototypes)
        {
            EchoObject o = EchoObject.NewCompound();
            o.Add("RenderMode", new EchoObject((int)d.RenderMode));
            o.Add("Texture", Serializer.Serialize(d.Texture, ctx));
            o.Add("Mesh", Serializer.Serialize(d.Mesh, ctx));
            EchoObject materials = EchoObject.NewList();
            foreach (Material? m in d.Materials)
                materials.ListAdd(Serializer.Serialize(m, ctx));
            o.Add("Materials", materials);
            o.Add("Density", new EchoObject(d.Density));
            o.Add("Range", new EchoObject(d.Range));
            o.Add("MinWidth", new EchoObject(d.MinWidth));
            o.Add("MaxWidth", new EchoObject(d.MaxWidth));
            o.Add("MinHeight", new EchoObject(d.MinHeight));
            o.Add("MaxHeight", new EchoObject(d.MaxHeight));
            o.Add("NoiseSize", new EchoObject(d.NoiseSize));
            o.Add("HealthyColor", Serializer.Serialize(d.HealthyColor, ctx));
            o.Add("DryColor", Serializer.Serialize(d.DryColor, ctx));
            o.Add("RootColor", Serializer.Serialize(d.RootColor, ctx));
            o.Add("TipColor", Serializer.Serialize(d.TipColor, ctx));
            o.Add("GroundTint", new EchoObject(d.GroundTint));
            o.Add("Stiffness", new EchoObject(d.Stiffness));
            o.Add("AlignToNormal", new EchoObject(d.AlignToNormal));
            details.ListAdd(o);
        }
        value.Add("DetailPrototypes", details);

        EchoObject trees = EchoObject.NewList();
        foreach (TreePrototype t in TreePrototypes)
        {
            EchoObject o = EchoObject.NewCompound();
            o.Add("Mesh", Serializer.Serialize(t.Mesh, ctx));
            EchoObject materials = EchoObject.NewList();
            foreach (Material? m in t.Materials)
                materials.ListAdd(Serializer.Serialize(m, ctx));
            o.Add("Materials", materials);
            trees.ListAdd(o);
        }
        value.Add("TreePrototypes", trees);

        value.Add("Package", new EchoObject(PackageBytes()));
    }

    // The package as saved: the bytes it was loaded from while nothing has changed them, else written afresh
    private byte[] PackageBytes()
    {
        if (!_changed && _package != null)
            return _package;
        MemoryStream package = new();
        Terrain.Save(package);
        return package.ToArray();
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Name = value.Get("Name")?.StringValue ?? "TerrainData";
        _resolution = value.Get("HeightmapResolution")?.IntValue ?? 513;
        _size = value.Get("Size")?.FloatValue ?? 1024f;
        _height = value.Get("Height")?.FloatValue ?? 100f;

        _layers = [];
        if (value.Get("Layers") is { } layers)
            foreach (EchoObject o in layers.List)
                _layers.Add(new TerrainLayer
                {
                    Albedo = Serializer.Deserialize<Texture2D>(o.Get("Albedo"), ctx),
                    NormalMap = Serializer.Deserialize<Texture2D>(o.Get("NormalMap"), ctx),
                    Tiling = o.Get("Tiling")?.FloatValue ?? 10f,
                    Roughness = o.Get("Roughness")?.FloatValue ?? 1f,
                    HeightBlend = o.Get("HeightBlend")?.FloatValue ?? 0.5f,
                    HexTiling = o.Get("HexTiling")?.BoolValue ?? true,
                    MacroVariation = o.Get("MacroVariation")?.FloatValue ?? 0.3f,
                });
        if (_layers.Count == 0)
            _layers = [new(), new(), new(), new()];

        _detailPrototypes = [];
        if ((value.Get("DetailPrototypes") ?? value.Get("GrassTypes")) is { } details)
            foreach (EchoObject o in details.List)
                _detailPrototypes.Add(ReadDetail(o, ctx));
        if (_detailPrototypes.Count == 0)
            _detailPrototypes.Add(new());

        _treePrototypes = [];
        if (value.Get("TreePrototypes") is { } trees)
            foreach (EchoObject o in trees.List)
            {
                TreePrototype prototype = new() { Mesh = Serializer.Deserialize<Mesh>(o.Get("Mesh"), ctx) };
                if (o.Get("Materials") is { } materials)
                    foreach (EchoObject m in materials.List)
                        prototype.Materials.Add(Serializer.Deserialize<Material>(m, ctx));
                else if (o.Get("Material") is { } single)
                    prototype.Materials.Add(Serializer.Deserialize<Material>(single, ctx));
                _treePrototypes.Add(prototype);
            }

        _terrain = null;
        _package = value.Get("Package")?.ByteArrayValue;
        if (_package == null && (value.Get("Heights16") != null || value.Get("Heights") != null))
            _package = TerrainLegacy.Package(value, ctx, _resolution, _size, _height, _layers.Count, _detailPrototypes.Count, Name);
    }

    private static DetailPrototype ReadDetail(EchoObject o, SerializationContext ctx)
    {
        int mode = o.Get("RenderMode")?.IntValue ?? (o.Get("UseMesh")?.BoolValue == true ? 2 : 0);
        bool legacy = o.Get("Density") == null;
        DetailPrototype d = new()
        {
            RenderMode = legacy ? (mode == 2 ? DetailRenderMode.Mesh : DetailRenderMode.Grass) : (DetailRenderMode)mode,
            Texture = Serializer.Deserialize<Texture2D>(o.Get("Texture"), ctx),
            Mesh = Serializer.Deserialize<Mesh>(o.Get("Mesh"), ctx),
            Density = o.Get("Density")?.FloatValue ?? 6f,
            Range = o.Get("Range")?.FloatValue ?? 90f,
            MinWidth = o.Get("MinWidth")?.FloatValue ?? 0.7f,
            MaxWidth = o.Get("MaxWidth")?.FloatValue ?? 0.7f,
            MinHeight = o.Get("MinHeight")?.FloatValue ?? 0.25f,
            MaxHeight = o.Get("MaxHeight")?.FloatValue ?? 0.45f,
            NoiseSize = o.Get("NoiseSize")?.FloatValue ?? 6f,
            GroundTint = o.Get("GroundTint")?.FloatValue ?? 0.5f,
            Stiffness = o.Get("Stiffness")?.FloatValue ?? 1f - Math.Clamp(o.Get("BendFactor")?.FloatValue ?? 0.7f, 0f, 1f),
            AlignToNormal = o.Get("AlignToNormal")?.BoolValue ?? false,
        };
        if ((o.Get("HealthyColor") ?? o.Get("Tint")) is { } healthy)
            d.HealthyColor = Serializer.Deserialize<Color>(healthy, ctx);
        if ((o.Get("DryColor") ?? o.Get("DryTint")) is { } dry)
            d.DryColor = Serializer.Deserialize<Color>(dry, ctx);
        d.RootColor = o.Get("RootColor") is { } root ? Serializer.Deserialize<Color>(root, ctx) : legacy ? d.HealthyColor : Color.White;
        d.TipColor = o.Get("TipColor") is { } tip ? Serializer.Deserialize<Color>(tip, ctx) : legacy ? d.HealthyColor : Color.White;
        if (o.Get("Materials") is { } materials)
            foreach (EchoObject m in materials.List)
                d.Materials.Add(Serializer.Deserialize<Material>(m, ctx));
        return d;
    }

    #endregion
}

/// <summary>A terrain's edits at one moment, see <see cref="TerrainData.Capture"/>.</summary>
public sealed record TerrainSnapshot(GTerrain Terrain, TerrainState State);
