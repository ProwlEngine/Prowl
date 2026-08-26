// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>Height interpolation mode for terrain sampling.</summary>
public enum TerrainInterpolation
{
    Bilinear,
    Bicubic
}

/// <summary>Per-layer texture settings for terrain surface rendering.</summary>
public class TerrainLayer
{
    public AssetRef<Texture2D> Albedo;
    public AssetRef<Texture2D> NormalMap;
    public float Tiling = 10f;
    public float Roughness = 1f;
    public float Metallic = 0f;
}

public enum DetailRenderMode
{
    /// <summary>Billboard quad that always faces the camera.</summary>
    TextureBillboard,
    /// <summary>Texture on a quad that doesn't face the camera (fixed orientation).</summary>
    TextureNonBillboard,
    /// <summary>Instanced 3D mesh.</summary>
    Mesh,
}

/// <summary>Defines a detail/grass prototype for terrain vegetation.</summary>
public class DetailPrototype
{
    public AssetRef<Texture2D> Texture;       // texture for billboard/non-billboard modes
    public AssetRef<Mesh> Mesh;               // mesh for Mesh mode
    /// <summary>
    /// Materials to use for the Mesh render mode, one per submesh of <see cref="Mesh"/>.
    /// When a slot is null (or the list is shorter than the mesh's submesh count), the
    /// default Standard material is used for that submesh.
    /// </summary>
    public List<AssetRef<Material>> Materials = [];
    /// <summary>
    /// Optional grass material override for texture render modes (Billboard / NonBillboard).
    /// When null, the terrain's global GrassMaterial is used.
    /// </summary>
    public AssetRef<Material> GrassMaterial;
    public DetailRenderMode RenderMode = DetailRenderMode.TextureBillboard;
    public float MinWidth = 1f;
    public float MaxWidth = 2f;
    public float MinHeight = 1f;
    public float MaxHeight = 2f;
    public float NoiseSpread = 0.1f;
    public float BendFactor = 0.5f;
    public Color HealthyColor = new(0.26f, 0.97f, 0.16f, 1f);
    public Color DryColor = new(0.80f, 0.73f, 0.10f, 1f);
    /// <summary>If true, grass/detail aligns to the terrain heightmap normal (tilts on slopes).</summary>
    public bool AlignToNormal;
}

/// <summary>A placed tree instance on the terrain.</summary>
public struct TreeInstance
{
    public Float2 Position;        // terrain UV (0-1)
    public int PrototypeIndex;     // index into TerrainData.TreePrototypes
    public float Rotation;         // Y-axis rotation in radians
    public float WidthScale;       // width scale multiplier
    public float HeightScale;      // height scale multiplier
    public Color Tint;             // per-instance color variation
}

/// <summary>Defines a tree type for terrain vegetation.</summary>
public class TreePrototype
{
    public AssetRef<Mesh> Mesh;

    /// <summary>
    /// Materials, one per submesh of <see cref="Mesh"/>. When a slot is null or the list is
    /// shorter than the mesh's submesh count, the missing submeshes fall back to the first
    /// non-null material in this list (or Standard if none).
    /// </summary>
    public List<AssetRef<Material>> Materials = [];

    /// <summary>Legacy single-material accessor reads/writes Materials[0].</summary>
    public AssetRef<Material> Material
    {
        get => Materials.Count > 0 ? Materials[0] : default;
        set { if (Materials.Count == 0) Materials.Add(value); else Materials[0] = value; }
    }

    public float BendFactor = 1f;       // wind bend strength
}

/// <summary>
/// Stores terrain heightmap, splatmap, detail layers, and tree data as a reusable asset.
/// Referenced by TerrainComponent for rendering and TerrainCollider for physics.
/// </summary>
[CreateAssetMenu("Terrain Data", Extension = ".terraindata", Order = 3)]
public sealed class TerrainData : EngineObject, ISerializable
{
    // --- Heightmap & Surface ---

    /// <summary>Max value for 16-bit height storage.</summary>
    public const int kMaxHeight = 32766;

    private int _heightmapResolution = 513;
    private int _splatmapResolution = 512;
    private float _size = 1024f;
    private float _height = 100f;
    private TerrainInterpolation _interpolation = TerrainInterpolation.Bicubic;
    private short[] _heightsField;
    private float[] _splatsField;
    private List<TerrainLayer> _layers = [new(), new(), new(), new()];
    private byte[]? _holesField;
    private int _detailResolution = 1024;
    private List<DetailPrototype> _detailPrototypes = [new()];
    private List<byte[]> _detailLayers = [];
    private List<TreeInstance> _trees = [];
    private List<TreePrototype> _treePrototypes = [];

    /// <summary>
    /// Heightmap samples per side, one more than the number of cells it spans: 513 samples fence off
    /// 512 cells. Vertex grid, use <see cref="HeightmapToUV"/> to convert an index to UV.
    /// Change it with <see cref="ResizeHeightmap"/>, which reallocates to match.
    /// </summary>
    public int HeightmapResolution { get { EnsureNotDisposed(); return _heightmapResolution; } private set => _heightmapResolution = value; }
    /// <summary>
    /// Splatmap texels per side. Cell grid, so a texel sits in the middle of a heightmap cell, which
    /// is why it is one less than the heightmap. Use <see cref="SplatmapToUV"/> to convert an index
    /// to UV, and <see cref="ResizeSplatmap"/> to change it.
    /// </summary>
    public int SplatmapResolution { get { EnsureNotDisposed(); return _splatmapResolution; } private set => _splatmapResolution = value; }
    public float Size { get { EnsureNotDisposed(); return _size; } set { EnsureNotDisposed(); _size = value; } }
    public float Height { get { EnsureNotDisposed(); return _height; } set { EnsureNotDisposed(); _height = value; } }

    /// <summary>Height interpolation mode for both CPU sampling and GPU shader.</summary>
    public TerrainInterpolation Interpolation { get { EnsureNotDisposed(); return _interpolation; } set { EnsureNotDisposed(); _interpolation = value; } }

    /// <summary>
    /// Raw 16-bit heightmap. Values 0..kMaxHeight map to normalized 0..1.
    /// Use GetHeight/SetHeight for float access. Halves memory vs float[].
    /// </summary>
    public short[] Heights { get { EnsureNotDisposed(); return _heightsField; } set { EnsureNotDisposed(); _heightsField = value; } }
    /// <summary>
    /// Interleaved splatmap weights. For N layers, each pixel has N floats.
    /// Layout: [pixel0_layer0, pixel0_layer1, ..., pixel0_layerN-1, pixel1_layer0, ...].
    /// Length = SplatmapResolution * SplatmapResolution * LayerCount.
    /// </summary>
    public float[] Splats { get { EnsureNotDisposed(); return _splatsField; } set { EnsureNotDisposed(); _splatsField = value; } }

    /// <summary>Dynamic layer list. Each group of 4 layers maps to one RGBA splatmap texture.</summary>
    public List<TerrainLayer> Layers { get { EnsureNotDisposed(); return _layers; } set { EnsureNotDisposed(); _layers = value; } }

    // --- Holes ---

    /// <summary>
    /// Per-pixel hole map at SplatmapResolution. 0 = hole (not rendered/no collision), 255 = solid.
    /// Null means no holes (all solid).
    /// </summary>
    public byte[]? Holes { get { EnsureNotDisposed(); return _holesField; } set { EnsureNotDisposed(); _holesField = value; } }

    // --- Details/Grass ---

    /// <summary>
    /// Detail cells per side, shared by all detail layers. Cell grid like the splatmap, use
    /// <see cref="DetailToUV"/> to convert an index to UV, and <see cref="ResizeDetailMaps"/> to
    /// change it.
    /// </summary>
    public int DetailResolution { get { EnsureNotDisposed(); return _detailResolution; } private set => _detailResolution = value; }

    /// <summary>Detail prototype definitions.</summary>
    public List<DetailPrototype> DetailPrototypes { get { EnsureNotDisposed(); return _detailPrototypes; } set { EnsureNotDisposed(); _detailPrototypes = value; } }

    /// <summary>
    /// Per-prototype density maps. DetailLayers[protoIndex] = byte[DetailResolution * DetailResolution],
    /// where 0 is bare and 255 is solid. That is the resolution the GPU keeps anyway, and a quarter
    /// the memory of floats. Use GetDetailDensity/SetDetailDensity for 0-1 access.
    /// Array count matches DetailPrototypes.Count.
    /// </summary>
    public List<byte[]> DetailLayers { get { EnsureNotDisposed(); return _detailLayers; } set { EnsureNotDisposed(); _detailLayers = value; } }

    // --- Trees ---

    public List<TreeInstance> Trees { get { EnsureNotDisposed(); return _trees; } set { EnsureNotDisposed(); _trees = value; } }
    public List<TreePrototype> TreePrototypes { get { EnsureNotDisposed(); return _treePrototypes; } set { EnsureNotDisposed(); _treePrototypes = value; } }

    // --- GPU Textures ---

    [NonSerialized] private Texture2D? _heightmapTexture;
    [NonSerialized] private List<Texture2D>? _splatmapTextures;
    [NonSerialized] private bool _heightmapDirty = true;
    [NonSerialized] private bool _splatmapDirty = true;
    [NonSerialized] private bool _holesDirty = true;
    [NonSerialized] private Texture2D? _holesTexture;
    [NonSerialized] private int _heightsVersion;
    [NonSerialized] private int _detailsVersion;

    /// <summary>Bumped on every height change. Renderers watch this to rebuild cached data.</summary>
    public int HeightsVersion { get { EnsureNotDisposed(); return _heightsVersion; } }

    /// <summary>Bumped on every detail density change. Renderers watch this to rebuild cached data.</summary>
    public int DetailsVersion { get { EnsureNotDisposed(); return _detailsVersion; } }

    public TerrainData() : base("New TerrainData")
    {
        Heights = new short[HeightmapResolution * HeightmapResolution];
        int lc = Layers.Count;
        Splats = new float[SplatmapResolution * SplatmapResolution * lc];
        for (int i = 0; i < Splats.Length; i += lc)
            Splats[i] = 1f;

        EnsureDetailLayers();
    }

    /// <summary>Ensure DetailLayers array matches DetailPrototypes count.</summary>
    public void EnsureDetailLayers()
    {
        EnsureNotDisposed();
        while (DetailLayers.Count < DetailPrototypes.Count)
            DetailLayers.Add(new byte[DetailResolution * DetailResolution]);
        while (DetailLayers.Count > DetailPrototypes.Count)
            DetailLayers.RemoveAt(DetailLayers.Count - 1);
    }

    #region Heightmap

    /// <summary>Get normalized height (0-1) at integer coordinates.</summary>
    public float GetHeight(int x, int z)
    {
        EnsureNotDisposed();
        if (Heights == null || x < 0 || x >= HeightmapResolution || z < 0 || z >= HeightmapResolution)
            return 0f;
        return (float)Heights[z * HeightmapResolution + x] / kMaxHeight;
    }

    /// <summary>Set normalized height (0-1) at integer coordinates. Stored as 16-bit.</summary>
    public void SetHeight(int x, int z, float value)
    {
        EnsureNotDisposed();
        if (Heights == null || x < 0 || x >= HeightmapResolution || z < 0 || z >= HeightmapResolution)
            return;
        Heights[z * HeightmapResolution + x] = (short)(Maths.Clamp(value, 0f, 1f) * kMaxHeight);
        _heightmapDirty = true;
        _heightsVersion++;
    }

    /// <summary>Interpolated height in world units at normalized UV coordinates.</summary>
    public float GetInterpolatedHeight(float u, float v)
    {
        EnsureNotDisposed();
        if (Heights == null) return 0f;
        return Interpolation == TerrainInterpolation.Bicubic
            ? GetInterpolatedHeightBicubic(u, v)
            : GetInterpolatedHeightBilinear(u, v);
    }

    private float GetInterpolatedHeightBilinear(float u, float v)
    {
        float px = u * (HeightmapResolution - 1);
        float pz = v * (HeightmapResolution - 1);
        int x0 = Maths.Clamp((int)MathF.Floor(px), 0, HeightmapResolution - 1);
        int z0 = Maths.Clamp((int)MathF.Floor(pz), 0, HeightmapResolution - 1);
        int x1 = Maths.Min(x0 + 1, HeightmapResolution - 1);
        int z1 = Maths.Min(z0 + 1, HeightmapResolution - 1);
        float fx = px - x0, fz = pz - z0;
        float scale = 1f / kMaxHeight;
        float h00 = Heights[z0 * HeightmapResolution + x0] * scale;
        float h10 = Heights[z0 * HeightmapResolution + x1] * scale;
        float h01 = Heights[z1 * HeightmapResolution + x0] * scale;
        float h11 = Heights[z1 * HeightmapResolution + x1] * scale;
        return ((h00 * (1 - fx) + h10 * fx) * (1 - fz) + (h01 * (1 - fx) + h11 * fx) * fz) * Height;
    }

    /// <summary>
    /// Bicubic (Catmull-Rom) interpolation using the same 4-tap bilinear trick as the GPU shader.
    /// This mirrors the GPU path exactly to ensure CPU/GPU height agreement.
    /// </summary>
    private float GetInterpolatedHeightBicubic(float u, float v)
    {
        // Mirror the GPU: sample grid coord = uv * (texSize - 1)
        float texSize = HeightmapResolution;
        float invTexSize = 1f / texSize;
        float scale = 1f / kMaxHeight;

        float coordX = u * (texSize - 1f);
        float coordZ = v * (texSize - 1f);
        float floorX = MathF.Floor(coordX);
        float floorZ = MathF.Floor(coordZ);
        float fx = coordX - floorX;
        float fz = coordZ - floorZ;

        // Catmull-Rom weights (same as GPU)
        float fx2 = fx * fx, fx3 = fx2 * fx;
        float fz2 = fz * fz, fz3 = fz2 * fz;

        float w0x = -0.5f * fx3 + fx2 - 0.5f * fx;
        float w1x = 1.5f * fx3 - 2.5f * fx2 + 1f;
        float w2x = -1.5f * fx3 + 2f * fx2 + 0.5f * fx;
        float w3x = 0.5f * fx3 - 0.5f * fx2;

        float w0z = -0.5f * fz3 + fz2 - 0.5f * fz;
        float w1z = 1.5f * fz3 - 2.5f * fz2 + 1f;
        float w2z = -1.5f * fz3 + 2f * fz2 + 0.5f * fz;
        float w3z = 0.5f * fz3 - 0.5f * fz2;

        // Combine pairs for the bilinear trick. Both sums reach zero on sample-aligned
        // coords, so they are floored to keep the tap positions finite.
        float s0x = MathF.Max(w0x + w1x, 1e-5f), s1x = MathF.Max(w2x + w3x, 1e-5f);
        float s0z = MathF.Max(w0z + w1z, 1e-5f), s1z = MathF.Max(w2z + w3z, 1e-5f);
        float f0x = w1x / s0x, f1x = w3x / s1x;
        float f0z = w1z / s0z, f1z = w3z / s1z;

        // Texel-center UV of the two bilinear taps per axis
        float t0x = (floorX - 0.5f + f0x) * invTexSize;
        float t1x = (floorX + 1.5f + f1x) * invTexSize;
        float t0z = (floorZ - 0.5f + f0z) * invTexSize;
        float t1z = (floorZ + 1.5f + f1z) * invTexSize;

        // Bilinear sample at each of the 4 positions (replicates GPU texture() with linear filtering)
        float h00 = SampleBilinear(t0x, t0z, scale);
        float h10 = SampleBilinear(t1x, t0z, scale);
        float h01 = SampleBilinear(t0x, t1z, scale);
        float h11 = SampleBilinear(t1x, t1z, scale);

        // Blend (same as GPU)
        float blendX = s1x / (s0x + s1x);
        float blendZ = s1z / (s0z + s1z);
        float row0 = h00 + (h10 - h00) * blendX;
        float row1 = h01 + (h11 - h01) * blendX;
        return (row0 + (row1 - row0) * blendZ) * Height;
    }

    /// <summary>Bilinear sample in UV space, matching GPU texture() with linear filtering.</summary>
    private float SampleBilinear(float u, float v, float scale)
    {
        // GPU linear filtering: texel centers at (i+0.5)/N
        // Convert UV to texel space, subtract 0.5 for center offset
        float px = u * HeightmapResolution - 0.5f;
        float pz = v * HeightmapResolution - 0.5f;
        int x0 = Maths.Clamp((int)MathF.Floor(px), 0, HeightmapResolution - 1);
        int z0 = Maths.Clamp((int)MathF.Floor(pz), 0, HeightmapResolution - 1);
        int x1 = Maths.Min(x0 + 1, HeightmapResolution - 1);
        int z1 = Maths.Min(z0 + 1, HeightmapResolution - 1);
        float fx = px - x0, fz = pz - z0;
        fx = Maths.Clamp(fx, 0f, 1f);
        fz = Maths.Clamp(fz, 0f, 1f);
        float h00 = Heights[z0 * HeightmapResolution + x0] * scale;
        float h10 = Heights[z0 * HeightmapResolution + x1] * scale;
        float h01 = Heights[z1 * HeightmapResolution + x0] * scale;
        float h11 = Heights[z1 * HeightmapResolution + x1] * scale;
        return (h00 * (1 - fx) + h10 * fx) * (1 - fz) + (h01 * (1 - fx) + h11 * fx) * fz;
    }

    public void ResizeHeightmap(int newRes)
    {
        EnsureNotDisposed();
        HeightmapResolution = newRes;
        Heights = new short[newRes * newRes];
        _heightmapDirty = true;
        _heightsVersion++;
    }

    public void SetHeightmapDirty() { EnsureNotDisposed(); _heightmapDirty = true; _heightsVersion++; }

    /// <summary>Compute terrain normal at integer heightmap coordinates using Sobel operator.</summary>
    public Float3 CalculateNormalSobel(int x, int z)
    {
        EnsureNotDisposed();
        if (Heights == null) return Float3.UnitY;

        // Sample 3x3 neighborhood (clamped at edges)
        float hL = GetHeight(Math.Max(x - 1, 0), z);
        float hR = GetHeight(Math.Min(x + 1, HeightmapResolution - 1), z);
        float hD = GetHeight(x, Math.Max(z - 1, 0));
        float hU = GetHeight(x, Math.Min(z + 1, HeightmapResolution - 1));

        // Sobel gradient (simplified 2-sample per axis)
        float cellSize = Size / (HeightmapResolution - 1);
        float dX = (hL - hR) * Height;
        float dZ = (hD - hU) * Height;

        var normal = new Float3(dX, 2f * cellSize, dZ);
        float len = Float3.Length(normal);
        return len > 1e-8f ? normal / len : Float3.UnitY;
    }

    /// <summary>Interpolated normal at normalized UV coordinates (0-1).</summary>
    public Float3 GetInterpolatedNormal(float u, float v)
    {
        EnsureNotDisposed();
        if (Heights == null) return Float3.UnitY;

        float px = u * (HeightmapResolution - 1);
        float pz = v * (HeightmapResolution - 1);
        int x = Maths.Clamp((int)MathF.Round(px), 0, HeightmapResolution - 1);
        int z = Maths.Clamp((int)MathF.Round(pz), 0, HeightmapResolution - 1);
        return CalculateNormalSobel(x, z);
    }

    /// <summary>
    /// Get terrain steepness (slope angle in degrees) at normalized UV coordinates.
    /// 0 = flat, 90 = vertical cliff.
    /// </summary>
    public float GetSteepness(float u, float v)
    {
        EnsureNotDisposed();
        var normal = GetInterpolatedNormal(u, v);
        // Angle between normal and up vector
        return MathF.Acos(Maths.Clamp(normal.Y, -1f, 1f)) * (180f / MathF.PI);
    }

    #endregion

    #region Splatmap

    /// <summary>Number of active terrain layers.</summary>
    public int LayerCount { get { EnsureNotDisposed(); return Layers.Count; } }

    public float GetSplat(int x, int z, int channel)
    {
        EnsureNotDisposed();
        int lc = LayerCount;
        if (Splats == null || channel < 0 || channel >= lc ||
            x < 0 || x >= SplatmapResolution || z < 0 || z >= SplatmapResolution)
            return 0f;
        return Splats[(z * SplatmapResolution + x) * lc + channel];
    }

    public void SetSplat(int x, int z, int channel, float value)
    {
        EnsureNotDisposed();
        int lc = LayerCount;
        if (Splats == null || channel < 0 || channel >= lc ||
            x < 0 || x >= SplatmapResolution || z < 0 || z >= SplatmapResolution)
            return;
        Splats[(z * SplatmapResolution + x) * lc + channel] = Maths.Clamp(value, 0f, 1f);
        _splatmapDirty = true;
    }

    public void ResizeSplatmap(int newRes)
    {
        EnsureNotDisposed();
        SplatmapResolution = newRes;
        int lc = LayerCount;
        Splats = new float[newRes * newRes * lc];
        // Default: layer 0 = 1.0, rest = 0.0
        for (int i = 0; i < Splats.Length; i += lc) Splats[i] = 1f;
        _splatmapDirty = true;
    }

    /// <summary>Max supported terrain layers (2 splatmap textures x 4 channels).</summary>
    public const int kMaxLayers = 8;

    /// <summary>Add a new terrain layer. Expands the splat array with zeros for the new channel.</summary>
    public void AddLayer(TerrainLayer layer)
    {
        EnsureNotDisposed();
        if (Layers.Count >= kMaxLayers) return;
        int oldCount = Layers.Count;
        Layers.Add(layer);
        int newCount = Layers.Count;
        RebuildSplatsForLayerCount(oldCount, newCount);
        _splatmapDirty = true;
    }

    /// <summary>Remove a terrain layer at the given index. Shrinks the splat array.</summary>
    public void RemoveLayer(int index)
    {
        EnsureNotDisposed();
        if (index < 0 || index >= Layers.Count || Layers.Count <= 1) return;
        int oldCount = Layers.Count;
        Layers.RemoveAt(index);
        int newCount = Layers.Count;

        // Rebuild splats removing the channel at index
        int res = SplatmapResolution;
        int pixelCount = res * res;
        var newSplats = new float[pixelCount * newCount];
        for (int p = 0; p < pixelCount; p++)
        {
            int dst = 0;
            for (int c = 0; c < oldCount; c++)
            {
                if (c == index) continue;
                newSplats[p * newCount + dst] = Splats[p * oldCount + c];
                dst++;
            }
        }
        Splats = newSplats;
        _splatmapDirty = true;
    }

    private void RebuildSplatsForLayerCount(int oldCount, int newCount)
    {
        if (Splats == null) return;
        int res = SplatmapResolution;
        int pixelCount = res * res;
        var newSplats = new float[pixelCount * newCount];
        int copyChannels = Math.Min(oldCount, newCount);
        for (int p = 0; p < pixelCount; p++)
        {
            for (int c = 0; c < copyChannels; c++)
                newSplats[p * newCount + c] = Splats[p * oldCount + c];
        }
        Splats = newSplats;
    }

    public void SetSplatmapDirty() { EnsureNotDisposed(); _splatmapDirty = true; }

    #endregion

    #region Holes

    /// <summary>Check if a cell is solid (not a hole). Returns true if solid.</summary>
    public bool IsHoleSolid(int x, int z)
    {
        EnsureNotDisposed();
        if (Holes == null) return true; // No holes map = all solid
        if (x < 0 || x >= SplatmapResolution || z < 0 || z >= SplatmapResolution) return true;
        return Holes[z * SplatmapResolution + x] != 0;
    }

    /// <summary>Set a hole at the given splatmap-resolution coordinates. value=0 = hole, value=255 = solid.</summary>
    public void SetHole(int x, int z, byte value)
    {
        EnsureNotDisposed();
        if (x < 0 || x >= SplatmapResolution || z < 0 || z >= SplatmapResolution) return;
        // Lazy-allocate holes map on first use
        if (Holes == null)
        {
            Holes = new byte[SplatmapResolution * SplatmapResolution];
            Array.Fill(Holes, (byte)255);
        }
        Holes[z * SplatmapResolution + x] = value;
        _holesDirty = true;
    }

    /// <summary>Check if a heightmap cell has a hole (any corner is a hole). Used by physics.</summary>
    public bool IsCellHole(int cellX, int cellZ)
    {
        EnsureNotDisposed();
        if (Holes == null) return false;
        // Heightmap cell center to the splat texel covering it
        float scale = (float)SplatmapResolution / (HeightmapResolution - 1);
        int sx = Maths.Clamp((int)((cellX + 0.5f) * scale), 0, SplatmapResolution - 1);
        int sz = Maths.Clamp((int)((cellZ + 0.5f) * scale), 0, SplatmapResolution - 1);
        return !IsHoleSolid(sx, sz);
    }

    public void SetHolesDirty() { EnsureNotDisposed(); _holesDirty = true; }

    #endregion

    #region Details

    public float GetDetailDensity(int layerIndex, int x, int z)
    {
        EnsureNotDisposed();
        if (layerIndex < 0 || layerIndex >= DetailLayers.Count) return 0f;
        var layer = DetailLayers[layerIndex];
        if (layer == null || x < 0 || x >= DetailResolution || z < 0 || z >= DetailResolution) return 0f;
        return layer[z * DetailResolution + x] * (1f / 255f);
    }

    public void SetDetailDensity(int layerIndex, int x, int z, float value)
    {
        EnsureNotDisposed();
        if (layerIndex < 0 || layerIndex >= DetailLayers.Count) return;
        var layer = DetailLayers[layerIndex];
        if (layer == null || x < 0 || x >= DetailResolution || z < 0 || z >= DetailResolution) return;
        layer[z * DetailResolution + x] = (byte)MathF.Round(Maths.Clamp(value, 0f, 1f) * 255f);
        _detailsVersion++;
    }

    /// <summary>
    /// Change the detail map resolution. Painted density is resampled into the new size by default,
    /// so changing resolution is not destructive the way the height and splat resizes are.
    /// </summary>
    public void ResizeDetailMaps(int newRes, bool resample = true)
    {
        EnsureNotDisposed();
        if (newRes < 1) return;

        int oldRes = DetailResolution;
        DetailResolution = newRes;

        for (int i = 0; i < DetailLayers.Count; i++)
        {
            var source = DetailLayers[i];
            DetailLayers[i] = resample && source != null && source.Length == oldRes * oldRes
                ? Resample(source, oldRes, newRes)
                : new byte[newRes * newRes];
        }

        _detailsVersion++;
    }

    /// <summary>Bilinear resample of a square cell grid, sampling on cell centres.</summary>
    private static byte[] Resample(byte[] source, int oldRes, int newRes)
    {
        var result = new byte[newRes * newRes];
        if (oldRes == newRes)
        {
            Array.Copy(source, result, result.Length);
            return result;
        }

        float scale = (float)oldRes / newRes;
        for (int z = 0; z < newRes; z++)
        {
            float sz = (z + 0.5f) * scale - 0.5f;
            int z0 = Maths.Clamp((int)MathF.Floor(sz), 0, oldRes - 1);
            int z1 = Maths.Min(z0 + 1, oldRes - 1);
            float fz = Maths.Clamp(sz - z0, 0f, 1f);

            for (int x = 0; x < newRes; x++)
            {
                float sx = (x + 0.5f) * scale - 0.5f;
                int x0 = Maths.Clamp((int)MathF.Floor(sx), 0, oldRes - 1);
                int x1 = Maths.Min(x0 + 1, oldRes - 1);
                float fx = Maths.Clamp(sx - x0, 0f, 1f);

                float top = source[z0 * oldRes + x0] * (1f - fx) + source[z0 * oldRes + x1] * fx;
                float bottom = source[z1 * oldRes + x0] * (1f - fx) + source[z1 * oldRes + x1] * fx;
                result[z * newRes + x] = (byte)MathF.Round(top * (1f - fz) + bottom * fz);
            }
        }

        return result;
    }

    /// <summary>Add a new detail prototype and its corresponding density layer.</summary>
    public void AddDetailPrototype(DetailPrototype proto)
    {
        EnsureNotDisposed();
        DetailPrototypes.Add(proto);
        DetailLayers.Add(new byte[DetailResolution * DetailResolution]);
        _detailsVersion++;
    }

    /// <summary>Remove a detail prototype and its density layer.</summary>
    public void RemoveDetailPrototype(int index)
    {
        EnsureNotDisposed();
        if (index < 0 || index >= DetailPrototypes.Count) return;
        DetailPrototypes.RemoveAt(index);
        if (index < DetailLayers.Count) DetailLayers.RemoveAt(index);
        _detailsVersion++;
    }

    public void SetDetailsDirty() { EnsureNotDisposed(); _detailsVersion++; }

    #endregion

    #region Coordinate Helpers

    /// <summary>UV of heightmap sample (x, z). Heights are a vertex grid, sample 0 sits on the terrain edge.</summary>
    public Float2 HeightmapToUV(int x, int z) => new((float)x / (HeightmapResolution - 1), (float)z / (HeightmapResolution - 1));

    /// <summary>UV of the center of splatmap texel (x, z). Splats are a cell grid, each covering Size / SplatmapResolution.</summary>
    public Float2 SplatmapToUV(int x, int z) => new((x + 0.5f) / SplatmapResolution, (z + 0.5f) / SplatmapResolution);

    /// <summary>UV of the center of detail cell (x, z). Details are a cell grid, each covering Size / DetailResolution.</summary>
    public Float2 DetailToUV(int x, int z) => new((x + 0.5f) / DetailResolution, (z + 0.5f) / DetailResolution);

    #endregion

    #region GPU Textures


    // Reusable buffer for converting short heights to float for GPU upload
    [NonSerialized] private float[]? _heightmapFloatBuffer;

    public Texture2D? GetHeightmapTexture()
    {
        EnsureNotDisposed();
        if (Heights == null) return null;
        if (_heightmapDirty || _heightmapTexture == null)
        {
            int count = HeightmapResolution * HeightmapResolution;

            // Convert short[] to float[] for GPU
            if (_heightmapFloatBuffer == null || _heightmapFloatBuffer.Length != count)
                _heightmapFloatBuffer = new float[count];
            float scale = 1f / kMaxHeight;
            for (int i = 0; i < count; i++)
                _heightmapFloatBuffer[i] = Heights[i] * scale;

            if (_heightmapTexture.IsValid()) _heightmapTexture.Dispose();
            _heightmapTexture = new Texture2D((uint)HeightmapResolution, (uint)HeightmapResolution, false, TextureImageFormat.Float);
            _heightmapTexture.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            Graphics.SetWrapS(_heightmapTexture.Handle, TextureWrap.ClampToEdge);
            Graphics.SetWrapT(_heightmapTexture.Handle, TextureWrap.ClampToEdge);
            unsafe { fixed (float* ptr = _heightmapFloatBuffer) _heightmapTexture.SetDataPtr(ptr, 0, 0, (uint)HeightmapResolution, (uint)HeightmapResolution); }
            _heightmapDirty = false;
        }
        return _heightmapTexture;
    }

    /// <summary>
    /// Get the splatmap textures. Each texture holds 4 layer weights as RGBA.
    /// Returns a list of textures: index 0 = layers 0-3, index 1 = layers 4-7, etc.
    /// </summary>
    public IReadOnlyList<Texture2D> GetSplatmapTextures()
    {
        EnsureNotDisposed();
        if (Splats == null) return Array.Empty<Texture2D>();

        int lc = LayerCount;
        int texCount = (lc + 3) / 4; // ceil(layerCount / 4)
        int pixelCount = SplatmapResolution * SplatmapResolution;

        if (_splatmapDirty || _splatmapTextures == null || _splatmapTextures.Count != texCount)
        {
            // Dispose old textures
            if (_splatmapTextures != null)
                foreach (var t in _splatmapTextures) if (t.IsValid()) t.Dispose();

            _splatmapTextures = new List<Texture2D>(texCount);
            var buffer = new float[pixelCount * 4];

            for (int ti = 0; ti < texCount; ti++)
            {
                int baseChannel = ti * 4;
                int channels = Math.Min(4, lc - baseChannel);

                // Pack channels into RGBA buffer
                Array.Clear(buffer);
                for (int p = 0; p < pixelCount; p++)
                {
                    for (int c = 0; c < channels; c++)
                        buffer[p * 4 + c] = Splats[p * lc + baseChannel + c];
                }

                var tex = new Texture2D((uint)SplatmapResolution, (uint)SplatmapResolution, false, TextureImageFormat.Float4);
                tex.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
                Graphics.SetWrapS(tex.Handle, TextureWrap.ClampToEdge);
                Graphics.SetWrapT(tex.Handle, TextureWrap.ClampToEdge);
                unsafe { fixed (float* ptr = buffer) tex.SetDataPtr(ptr, 0, 0, (uint)SplatmapResolution, (uint)SplatmapResolution); }
                _splatmapTextures.Add(tex);
            }

            _splatmapDirty = false;
        }
        return _splatmapTextures;
    }

    [NonSerialized] private List<Texture2D>? _detailTextures;
    [NonSerialized] private int _detailTexturesVersion = -1;
    [NonSerialized] private byte[]? _detailBuffer;
    [NonSerialized] private Float4[]? _detailBounds;

    /// <summary>
    /// Detail densities as GPU textures, four prototypes packed per RGBA texture: index 0 holds
    /// prototypes 0-3, index 1 holds 4-7. Rebuilt when <see cref="DetailsVersion"/> moves.
    /// </summary>
    public IReadOnlyList<Texture2D> GetDetailTextures()
    {
        EnsureNotDisposed();

        int layerCount = DetailLayers.Count;
        if (layerCount == 0) return Array.Empty<Texture2D>();

        int texCount = (layerCount + 3) / 4;
        if (_detailTextures != null && _detailTexturesVersion == _detailsVersion && _detailTextures.Count == texCount)
            return _detailTextures;

        if (_detailTextures != null)
            foreach (var t in _detailTextures) if (t.IsValid()) t.Dispose();

        int res = DetailResolution;
        int pixelCount = res * res;
        float cellUV = 1f / res;
        if (_detailBuffer == null || _detailBuffer.Length != pixelCount * 4)
            _detailBuffer = new byte[pixelCount * 4];
        if (_detailBounds == null || _detailBounds.Length != layerCount)
            _detailBounds = new Float4[layerCount];

        _detailTextures = new List<Texture2D>(texCount);
        for (int ti = 0; ti < texCount; ti++)
        {
            Array.Clear(_detailBuffer);
            int baseLayer = ti * 4;
            int channels = Math.Min(4, layerCount - baseLayer);

            for (int c = 0; c < channels; c++)
            {
                int layerIndex = baseLayer + c;
                var layer = DetailLayers[layerIndex];

                // Empty until proven otherwise, so a missing layer reads as unpainted rather than
                // as a stale rect left over from whatever occupied the slot before.
                _detailBounds[layerIndex] = kEmptyBounds;
                if (layer == null || layer.Length < pixelCount) continue;

                // The painted extent falls out of the pack for free, and lets renderers skip a
                // prototype entirely rather than sweeping cells nobody painted.
                int minX = int.MaxValue, minZ = int.MaxValue, maxX = -1, maxZ = -1;

                for (int z = 0; z < res; z++)
                {
                    int row = z * res;
                    for (int x = 0; x < res; x++)
                    {
                        byte value = layer[row + x];
                        _detailBuffer[(row + x) * 4 + c] = value;
                        if (value == 0) continue;

                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (z < minZ) minZ = z;
                        if (z > maxZ) maxZ = z;
                    }
                }

                if (maxX >= 0)
                    _detailBounds[layerIndex] = new Float4(
                        minX * cellUV, minZ * cellUV, (maxX + 1) * cellUV, (maxZ + 1) * cellUV);
            }

            var tex = new Texture2D((uint)res, (uint)res, false, TextureImageFormat.Color4b);
            tex.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            Graphics.SetWrapS(tex.Handle, TextureWrap.ClampToEdge);
            Graphics.SetWrapT(tex.Handle, TextureWrap.ClampToEdge);
            unsafe { fixed (byte* ptr = _detailBuffer) tex.SetDataPtr(ptr, 0, 0, (uint)res, (uint)res); }
            _detailTextures.Add(tex);
        }

        _detailTexturesVersion = _detailsVersion;
        return _detailTextures;
    }

    /// <summary>An unpainted layer, stored so a rect never reads as a zero-area patch at the origin.</summary>
    private static readonly Float4 kEmptyBounds = new(0f, 0f, -1f, -1f);

    /// <summary>
    /// Terrain-local rect a detail layer has any density in, as (minX, minZ, maxX, maxZ). False when
    /// nothing is painted, so a prototype nobody used costs nothing to draw.
    /// </summary>
    public bool TryGetDetailBounds(int layerIndex, out Float4 rect)
    {
        EnsureNotDisposed();
        rect = default;

        GetDetailTextures(); // bounds are gathered by the same sweep that packs the textures
        if (_detailBounds == null || layerIndex < 0 || layerIndex >= _detailBounds.Length) return false;

        Float4 uv = _detailBounds[layerIndex];
        if (uv.Z < uv.X || uv.W < uv.Y) return false;

        // Held in UV so a change to Size does not leave the rect pointing at stale world space
        float size = Size;
        rect = new Float4(uv.X * size, uv.Y * size, uv.Z * size, uv.W * size);
        return true;
    }

    /// <summary>
    /// Get the holes map as an R8 GPU texture. 0 = hole, 255 = solid.
    /// Returns null if no holes have been painted.
    /// </summary>
    [NonSerialized] private float[]? _holesFloatBuffer;

    public Texture2D? GetHolesTexture()
    {
        EnsureNotDisposed();
        if (Holes == null) return null;
        if (_holesDirty || _holesTexture == null)
        {
            int count = SplatmapResolution * SplatmapResolution;
            if (_holesFloatBuffer == null || _holesFloatBuffer.Length != count)
                _holesFloatBuffer = new float[count];
            for (int i = 0; i < count; i++)
                _holesFloatBuffer[i] = Holes[i] > 0 ? 1f : 0f;

            if (_holesTexture.IsValid()) _holesTexture.Dispose();
            _holesTexture = new Texture2D((uint)SplatmapResolution, (uint)SplatmapResolution, false, TextureImageFormat.Float);
            _holesTexture.SetTextureFilters(TextureMin.Nearest, TextureMag.Nearest);
            Graphics.SetWrapS(_holesTexture.Handle, TextureWrap.ClampToEdge);
            Graphics.SetWrapT(_holesTexture.Handle, TextureWrap.ClampToEdge);
            unsafe { fixed (float* ptr = _holesFloatBuffer) _holesTexture.SetDataPtr(ptr, 0, 0, (uint)SplatmapResolution, (uint)SplatmapResolution); }
            _holesDirty = false;
        }
        return _holesTexture;
    }

    #endregion

    protected override void OnDispose()
    {
        if (_heightmapTexture.IsValid()) _heightmapTexture.Dispose();
        if (_holesTexture.IsValid()) _holesTexture.Dispose();
        if (_splatmapTextures != null)
            foreach (var t in _splatmapTextures) if (t.IsValid()) t.Dispose();
        if (_detailTextures != null)
            foreach (var t in _detailTextures) if (t.IsValid()) t.Dispose();

        _heightmapTexture = null;
        _holesTexture = null;
        _splatmapTextures = null;
        _detailTextures = null;
    }

    #region Serialization

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        value.Add("Name", new EchoObject(Name));
        value.Add("HeightmapResolution", new EchoObject(HeightmapResolution));
        value.Add("SplatmapResolution", new EchoObject(SplatmapResolution));
        value.Add("Size", new EchoObject(Size));
        value.Add("Height", new EchoObject(Height));
        value.Add("Interpolation", new EchoObject((int)Interpolation));

        // Layers (dynamic count)
        var layerList = EchoObject.NewList();
        for (int i = 0; i < Layers.Count; i++)
        {
            var l = Layers[i];
            var lo = EchoObject.NewCompound();
            lo.Add("Albedo", Serializer.Serialize(l.Albedo, ctx));
            lo.Add("NormalMap", Serializer.Serialize(l.NormalMap, ctx));
            lo.Add("Tiling", new EchoObject(l.Tiling));
            lo.Add("Roughness", new EchoObject(l.Roughness));
            lo.Add("Metallic", new EchoObject(l.Metallic));
            layerList.ListAdd(lo);
        }
        value.Add("Layers", layerList);

        // Raw data
        SerializeShortArray(value, "Heights16", Heights);
        SerializeFloatArray(value, "Splats", Splats);

        // Holes (only serialize if any holes exist)
        if (Holes != null)
        {
            byte[] holesBytes = Holes;
            value.Add("Holes", new EchoObject(Convert.ToBase64String(holesBytes)));
        }

        // Detail system
        value.Add("DetailResolution", new EchoObject(DetailResolution));

        var detailProtoList = EchoObject.NewList();
        foreach (var dp in DetailPrototypes)
        {
            var dpo = EchoObject.NewCompound();
            dpo.Add("Texture", Serializer.Serialize(dp.Texture, ctx));
            dpo.Add("Mesh", Serializer.Serialize(dp.Mesh, ctx));
            var matList = EchoObject.NewList();
            foreach (var m in dp.Materials) matList.ListAdd(Serializer.Serialize(m, ctx));
            dpo.Add("Materials", matList);
            dpo.Add("RenderMode", new EchoObject((int)dp.RenderMode));
            dpo.Add("MinWidth", new EchoObject(dp.MinWidth));
            dpo.Add("MaxWidth", new EchoObject(dp.MaxWidth));
            dpo.Add("MinHeight", new EchoObject(dp.MinHeight));
            dpo.Add("MaxHeight", new EchoObject(dp.MaxHeight));
            dpo.Add("NoiseSpread", new EchoObject(dp.NoiseSpread));
            dpo.Add("BendFactor", new EchoObject(dp.BendFactor));
            dpo.Add("HealthyColor", Serializer.Serialize(dp.HealthyColor, ctx));
            dpo.Add("DryColor", Serializer.Serialize(dp.DryColor, ctx));
            dpo.Add("AlignToNormal", new EchoObject(dp.AlignToNormal));
            detailProtoList.ListAdd(dpo);
        }
        value.Add("DetailPrototypes", detailProtoList);

        var detailLayersList = EchoObject.NewList();
        foreach (var dl in DetailLayers)
            detailLayersList.ListAdd(new EchoObject(Convert.ToBase64String(dl ?? [])));
        value.Add("DetailLayers", detailLayersList);

        // Trees
        var treeProtoList = EchoObject.NewList();
        foreach (var tp in TreePrototypes)
        {
            var tpo = EchoObject.NewCompound();
            tpo.Add("Mesh", Serializer.Serialize(tp.Mesh, ctx));
            var matList = EchoObject.NewList();
            foreach (var m in tp.Materials) matList.ListAdd(Serializer.Serialize(m, ctx));
            tpo.Add("Materials", matList);
            tpo.Add("BendFactor", new EchoObject(tp.BendFactor));
            treeProtoList.ListAdd(tpo);
        }
        value.Add("TreePrototypes", treeProtoList);

        var treeList = EchoObject.NewList();
        foreach (var ti in Trees)
        {
            var tio = EchoObject.NewCompound();
            tio.Add("PosX", new EchoObject(ti.Position.X));
            tio.Add("PosY", new EchoObject(ti.Position.Y));
            tio.Add("Proto", new EchoObject(ti.PrototypeIndex));
            tio.Add("Rot", new EchoObject(ti.Rotation));
            tio.Add("WS", new EchoObject(ti.WidthScale));
            tio.Add("HS", new EchoObject(ti.HeightScale));
            tio.Add("Tint", Serializer.Serialize(ti.Tint, ctx));
            treeList.ListAdd(tio);
        }
        value.Add("Trees", treeList);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Name = value.Get("Name")?.StringValue ?? "TerrainData";
        HeightmapResolution = value.Get("HeightmapResolution")?.IntValue ?? 513;
        SplatmapResolution = value.Get("SplatmapResolution")?.IntValue ?? 512;
        Size = value.Get("Size")?.FloatValue ?? 1024f;
        Height = value.Get("Height")?.FloatValue ?? 100f;
        Interpolation = (TerrainInterpolation)(value.Get("Interpolation")?.IntValue ?? (int)TerrainInterpolation.Bicubic);

        // Layers
        var layerList = value.Get("Layers");
        Layers = [];
        if (layerList != null)
        {
            for (int i = 0; i < layerList.List.Count; i++)
            {
                var lo = layerList.List[i];
                Layers.Add(new TerrainLayer
                {
                    Albedo = Serializer.Deserialize<AssetRef<Texture2D>>(lo.Get("Albedo"), ctx),
                    NormalMap = Serializer.Deserialize<AssetRef<Texture2D>>(lo.Get("NormalMap"), ctx),
                    Tiling = lo.Get("Tiling")?.FloatValue ?? 10f,
                    Roughness = lo.Get("Roughness")?.FloatValue ?? 1f,
                    Metallic = lo.Get("Metallic")?.FloatValue ?? 0f,
                });
            }
        }
        if (Layers.Count == 0)
            Layers = [new(), new(), new(), new()]; // Default 4 layers

        // Raw data - try new 16-bit format first, fall back to legacy float[]
        Heights = DeserializeShortArray(value, "Heights16");
        if (Heights == null)
        {
            // Migration: convert old float[] heights to short[]
            float[]? oldHeights = DeserializeFloatArray(value, "Heights");
            if (oldHeights != null)
            {
                Heights = new short[oldHeights.Length];
                for (int i = 0; i < oldHeights.Length; i++)
                    Heights[i] = (short)(Maths.Clamp(oldHeights[i], 0f, 1f) * kMaxHeight);
            }
            else
            {
                Heights = new short[HeightmapResolution * HeightmapResolution];
            }
        }
        Splats = DeserializeFloatArray(value, "Splats") ?? CreateDefaultSplats();

        // Holes
        var holesB64 = value.Get("Holes")?.StringValue;
        Holes = holesB64 != null ? Convert.FromBase64String(holesB64) : null;

        // Detail system
        DetailResolution = value.Get("DetailResolution")?.IntValue ?? value.Get("GrassmapResolution")?.IntValue ?? 1024;

        DetailPrototypes = [];
        var dpList = value.Get("DetailPrototypes") ?? value.Get("GrassTypes");
        if (dpList != null)
        {
            foreach (var dpo in dpList.List)
            {
                var dp = new DetailPrototype
                {
                    Texture = Serializer.Deserialize<AssetRef<Texture2D>>(dpo.Get("Texture"), ctx),
                    Mesh = Serializer.Deserialize<AssetRef<Mesh>>(dpo.Get("Mesh"), ctx),
                    RenderMode = (DetailRenderMode)(dpo.Get("RenderMode")?.IntValue ?? (dpo.Get("UseMesh")?.BoolValue == true ? 2 : 0)),
                    MinWidth = dpo.Get("MinWidth")?.FloatValue ?? 1f,
                    MaxWidth = dpo.Get("MaxWidth")?.FloatValue ?? 2f,
                    MinHeight = dpo.Get("MinHeight")?.FloatValue ?? 1f,
                    MaxHeight = dpo.Get("MaxHeight")?.FloatValue ?? 2f,
                    NoiseSpread = dpo.Get("NoiseSpread")?.FloatValue ?? 0.1f,
                    BendFactor = dpo.Get("BendFactor")?.FloatValue ?? 0.5f,
                    HealthyColor = Serializer.Deserialize<Color>(dpo.Get("HealthyColor") ?? dpo.Get("Tint"), ctx),
                    DryColor = Serializer.Deserialize<Color>(dpo.Get("DryColor") ?? dpo.Get("DryTint"), ctx),
                    AlignToNormal = dpo.Get("AlignToNormal")?.BoolValue ?? false,
                };

                var matList = dpo.Get("Materials");
                if (matList != null)
                    foreach (var mat in matList.List)
                        dp.Materials.Add(Serializer.Deserialize<AssetRef<Material>>(mat, ctx));

                DetailPrototypes.Add(dp);
            }
        }
        if (DetailPrototypes.Count == 0) DetailPrototypes.Add(new());

        DetailLayers = [];
        int detailCells = DetailResolution * DetailResolution;
        var dlList = value.Get("DetailLayers");
        if (dlList != null)
        {
            foreach (var dlEntry in dlList.List)
            {
                byte[] arr = dlEntry?.StringValue != null
                    ? Convert.FromBase64String(dlEntry.StringValue)
                    : [];
                // A layer that does not match the resolution is not this asset's data
                DetailLayers.Add(arr.Length == detailCells ? arr : new byte[detailCells]);
            }
        }
        EnsureDetailLayers();

        // Trees
        TreePrototypes = [];
        var tpList = value.Get("TreePrototypes");
        if (tpList != null)
            foreach (var tpo in tpList.List)
            {
                var tp = new TreePrototype
                {
                    Mesh = Serializer.Deserialize<AssetRef<Mesh>>(tpo.Get("Mesh"), ctx),
                    BendFactor = tpo.Get("BendFactor")?.FloatValue ?? 1f,
                };

                var matList = tpo.Get("Materials");
                if (matList != null)
                {
                    foreach (var mat in matList.List)
                        tp.Materials.Add(Serializer.Deserialize<AssetRef<Material>>(mat, ctx));
                }
                else
                {
                    // Back-compat: old single-material field.
                    var legacy = tpo.Get("Material");
                    if (legacy != null)
                        tp.Materials.Add(Serializer.Deserialize<AssetRef<Material>>(legacy, ctx));
                }

                TreePrototypes.Add(tp);
            }

        Trees = [];
        var tiList = value.Get("Trees");
        if (tiList != null)
            foreach (var tio in tiList.List)
                Trees.Add(new TreeInstance
                {
                    Position = new Float2(tio.Get("PosX")?.FloatValue ?? 0, tio.Get("PosY")?.FloatValue ?? 0),
                    PrototypeIndex = tio.Get("Proto")?.IntValue ?? 0,
                    Rotation = tio.Get("Rot")?.FloatValue ?? 0,
                    WidthScale = tio.Get("WS")?.FloatValue ?? tio.Get("Scale")?.FloatValue ?? 1f,
                    HeightScale = tio.Get("HS")?.FloatValue ?? tio.Get("Scale")?.FloatValue ?? 1f,
                    Tint = Serializer.Deserialize<Color>(tio.Get("Tint"), ctx),
                });

        _heightmapDirty = true;
        _splatmapDirty = true;
        _holesDirty = true;
        _heightsVersion++;
        _detailsVersion++;
    }

    #endregion

    #region Helpers

    private static void SerializeShortArray(EchoObject value, string key, short[]? data)
    {
        if (data == null) return;
        byte[] bytes = new byte[data.Length * sizeof(short)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        value.Add(key, new EchoObject(Convert.ToBase64String(bytes)));
    }

    private static short[]? DeserializeShortArray(EchoObject value, string key)
    {
        string? b64 = value.Get(key)?.StringValue;
        if (b64 == null) return null;
        byte[] bytes = Convert.FromBase64String(b64);
        short[] arr = new short[bytes.Length / sizeof(short)];
        Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
        return arr;
    }

    private static void SerializeFloatArray(EchoObject value, string key, float[]? data)
    {
        if (data == null) return;
        byte[] bytes = new byte[data.Length * sizeof(float)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        value.Add(key, new EchoObject(Convert.ToBase64String(bytes)));
    }

    private static float[]? DeserializeFloatArray(EchoObject value, string key)
    {
        string? b64 = value.Get(key)?.StringValue;
        return b64 != null ? DeserializeFloatArrayDirect(b64) : null;
    }

    private static float[] DeserializeFloatArrayDirect(string b64)
    {
        byte[] bytes = Convert.FromBase64String(b64);
        float[] arr = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
        return arr;
    }

    private float[] CreateDefaultSplats()
    {
        int lc = LayerCount;
        var s = new float[SplatmapResolution * SplatmapResolution * lc];
        for (int i = 0; i < s.Length; i += lc) s[i] = 1f;
        return s;
    }

    #endregion
}
