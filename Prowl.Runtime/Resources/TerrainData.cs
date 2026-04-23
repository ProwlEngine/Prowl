// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

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

/// <summary>Defines a detail/grass prototype for terrain vegetation (matches Unity's DetailPrototype).</summary>
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

/// <summary>A placed tree instance on the terrain (matches Unity's TreeInstance).</summary>
public struct TreeInstance
{
    public Float2 Position;        // terrain UV (0-1)
    public int PrototypeIndex;     // index into TerrainData.TreePrototypes
    public float Rotation;         // Y-axis rotation in radians
    public float WidthScale;       // width scale multiplier
    public float HeightScale;      // height scale multiplier
    public Color Tint;             // per-instance color variation
}

/// <summary>Defines a tree type for terrain vegetation (matches Unity's TreePrototype).</summary>
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

    public int HeightmapResolution = 513;
    public int SplatmapResolution = 512;
    public float Size = 1024f;
    public float Height = 100f;
    public float[] Heights;
    public float[] Splats;
    public TerrainLayer[] Layers = [new(), new(), new(), new()];

    // --- Details/Grass ---

    /// <summary>Resolution of detail density maps (shared by all detail layers).</summary>
    public int DetailResolution = 1024;

    /// <summary>Detail prototype definitions.</summary>
    public List<DetailPrototype> DetailPrototypes = [new()];

    /// <summary>
    /// Per-prototype density maps. DetailLayers[protoIndex] = float[DetailResolution * DetailResolution].
    /// Density values 0-1. Array count matches DetailPrototypes.Count.
    /// </summary>
    public List<float[]> DetailLayers = [];

    // --- Trees ---

    public List<TreeInstance> Trees = [];
    public List<TreePrototype> TreePrototypes = [];

    // --- GPU Textures ---

    [NonSerialized] private Texture2D? _heightmapTexture;
    [NonSerialized] private Texture2D? _splatmapTexture;
    [NonSerialized] private bool _heightmapDirty = true;
    [NonSerialized] private bool _splatmapDirty = true;
    [NonSerialized] private bool _detailsDirty = true;

    public TerrainData() : base("New TerrainData")
    {
        Heights = new float[HeightmapResolution * HeightmapResolution];
        Splats = new float[SplatmapResolution * SplatmapResolution * 4];
        for (int i = 0; i < Splats.Length; i += 4)
            Splats[i] = 1f;

        EnsureDetailLayers();
    }

    /// <summary>Ensure DetailLayers array matches DetailPrototypes count.</summary>
    public void EnsureDetailLayers()
    {
        while (DetailLayers.Count < DetailPrototypes.Count)
            DetailLayers.Add(new float[DetailResolution * DetailResolution]);
        while (DetailLayers.Count > DetailPrototypes.Count)
            DetailLayers.RemoveAt(DetailLayers.Count - 1);
    }

    #region Heightmap

    public float GetHeight(int x, int z)
    {
        if (Heights == null || x < 0 || x >= HeightmapResolution || z < 0 || z >= HeightmapResolution)
            return 0f;
        return Heights[z * HeightmapResolution + x];
    }

    public void SetHeight(int x, int z, float value)
    {
        if (Heights == null || x < 0 || x >= HeightmapResolution || z < 0 || z >= HeightmapResolution)
            return;
        Heights[z * HeightmapResolution + x] = Maths.Clamp(value, 0f, 1f);
        _heightmapDirty = true;
    }

    public float GetInterpolatedHeight(float u, float v)
    {
        if (Heights == null) return 0f;
        float px = u * (HeightmapResolution - 1);
        float pz = v * (HeightmapResolution - 1);
        int x0 = Maths.Clamp((int)MathF.Floor(px), 0, HeightmapResolution - 1);
        int z0 = Maths.Clamp((int)MathF.Floor(pz), 0, HeightmapResolution - 1);
        int x1 = Maths.Min(x0 + 1, HeightmapResolution - 1);
        int z1 = Maths.Min(z0 + 1, HeightmapResolution - 1);
        float fx = px - x0, fz = pz - z0;
        float h00 = Heights[z0 * HeightmapResolution + x0];
        float h10 = Heights[z0 * HeightmapResolution + x1];
        float h01 = Heights[z1 * HeightmapResolution + x0];
        float h11 = Heights[z1 * HeightmapResolution + x1];
        return ((h00 * (1 - fx) + h10 * fx) * (1 - fz) + (h01 * (1 - fx) + h11 * fx) * fz) * Height;
    }

    public void ResizeHeightmap(int newRes)
    {
        HeightmapResolution = newRes;
        Heights = new float[newRes * newRes];
        _heightmapDirty = true;
    }

    public void SetHeightmapDirty() => _heightmapDirty = true;

    #endregion

    #region Splatmap

    public float GetSplat(int x, int z, int channel)
    {
        if (Splats == null || channel < 0 || channel > 3 ||
            x < 0 || x >= SplatmapResolution || z < 0 || z >= SplatmapResolution)
            return 0f;
        return Splats[(z * SplatmapResolution + x) * 4 + channel];
    }

    public void SetSplat(int x, int z, int channel, float value)
    {
        if (Splats == null || channel < 0 || channel > 3 ||
            x < 0 || x >= SplatmapResolution || z < 0 || z >= SplatmapResolution)
            return;
        Splats[(z * SplatmapResolution + x) * 4 + channel] = Maths.Clamp(value, 0f, 1f);
        _splatmapDirty = true;
    }

    public void ResizeSplatmap(int newRes)
    {
        SplatmapResolution = newRes;
        Splats = new float[newRes * newRes * 4];
        for (int i = 0; i < Splats.Length; i += 4) Splats[i] = 1f;
        _splatmapDirty = true;
    }

    public void SetSplatmapDirty() => _splatmapDirty = true;

    #endregion

    #region Details

    public float GetDetailDensity(int layerIndex, int x, int z)
    {
        if (layerIndex < 0 || layerIndex >= DetailLayers.Count) return 0f;
        var layer = DetailLayers[layerIndex];
        if (layer == null || x < 0 || x >= DetailResolution || z < 0 || z >= DetailResolution) return 0f;
        return layer[z * DetailResolution + x];
    }

    public void SetDetailDensity(int layerIndex, int x, int z, float value)
    {
        if (layerIndex < 0 || layerIndex >= DetailLayers.Count) return;
        var layer = DetailLayers[layerIndex];
        if (layer == null || x < 0 || x >= DetailResolution || z < 0 || z >= DetailResolution) return;
        layer[z * DetailResolution + x] = Maths.Clamp(value, 0f, 1f);
        _detailsDirty = true;
    }

    public void ResizeDetailMaps(int newRes)
    {
        DetailResolution = newRes;
        for (int i = 0; i < DetailLayers.Count; i++)
            DetailLayers[i] = new float[newRes * newRes];
        _detailsDirty = true;
    }

    /// <summary>Add a new detail prototype and its corresponding density layer.</summary>
    public void AddDetailPrototype(DetailPrototype proto)
    {
        DetailPrototypes.Add(proto);
        DetailLayers.Add(new float[DetailResolution * DetailResolution]);
    }

    /// <summary>Remove a detail prototype and its density layer.</summary>
    public void RemoveDetailPrototype(int index)
    {
        if (index < 0 || index >= DetailPrototypes.Count) return;
        DetailPrototypes.RemoveAt(index);
        if (index < DetailLayers.Count) DetailLayers.RemoveAt(index);
    }

    public void SetDetailsDirty() => _detailsDirty = true;

    #endregion

    #region GPU Textures

    public Texture2D? GetHeightmapTexture()
    {
        if (Heights == null) return null;
        if (_heightmapDirty || _heightmapTexture == null)
        {
            _heightmapTexture?.Dispose();
            _heightmapTexture = new Texture2D((uint)HeightmapResolution, (uint)HeightmapResolution, false, TextureImageFormat.Float);
            _heightmapTexture.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            Graphics.SetWrapS(_heightmapTexture.Handle, TextureWrap.ClampToEdge);
            Graphics.SetWrapT(_heightmapTexture.Handle, TextureWrap.ClampToEdge);
            unsafe { fixed (float* ptr = Heights) _heightmapTexture.SetDataPtr(ptr, 0, 0, (uint)HeightmapResolution, (uint)HeightmapResolution); }
            _heightmapDirty = false;
        }
        return _heightmapTexture;
    }

    public Texture2D? GetSplatmapTexture()
    {
        if (Splats == null) return null;
        if (_splatmapDirty || _splatmapTexture == null)
        {
            _splatmapTexture?.Dispose();
            _splatmapTexture = new Texture2D((uint)SplatmapResolution, (uint)SplatmapResolution, false, TextureImageFormat.Float4);
            _splatmapTexture.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            Graphics.SetWrapS(_splatmapTexture.Handle, TextureWrap.ClampToEdge);
            Graphics.SetWrapT(_splatmapTexture.Handle, TextureWrap.ClampToEdge);
            unsafe { fixed (float* ptr = Splats) _splatmapTexture.SetDataPtr(ptr, 0, 0, (uint)SplatmapResolution, (uint)SplatmapResolution); }
            _splatmapDirty = false;
        }
        return _splatmapTexture;
    }

    #endregion

    #region Serialization

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        value.Add("Name", new EchoObject(Name));
        value.Add("HeightmapResolution", new EchoObject(HeightmapResolution));
        value.Add("SplatmapResolution", new EchoObject(SplatmapResolution));
        value.Add("Size", new EchoObject(Size));
        value.Add("Height", new EchoObject(Height));

        // Layers
        var layerList = EchoObject.NewList();
        for (int i = 0; i < 4; i++)
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
        SerializeFloatArray(value, "Heights", Heights);
        SerializeFloatArray(value, "Splats", Splats);

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
            SerializeFloatArrayToList(detailLayersList, dl);
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

        // Layers
        var layerList = value.Get("Layers");
        Layers = [new(), new(), new(), new()];
        if (layerList != null)
            for (int i = 0; i < Math.Min(4, layerList.List.Count); i++)
            {
                var lo = layerList.List[i];
                Layers[i].Albedo = Serializer.Deserialize<AssetRef<Texture2D>>(lo.Get("Albedo"), ctx);
                Layers[i].NormalMap = Serializer.Deserialize<AssetRef<Texture2D>>(lo.Get("NormalMap"), ctx);
                Layers[i].Tiling = lo.Get("Tiling")?.FloatValue ?? 10f;
                Layers[i].Roughness = lo.Get("Roughness")?.FloatValue ?? 1f;
                Layers[i].Metallic = lo.Get("Metallic")?.FloatValue ?? 0f;
            }

        // Raw data
        Heights = DeserializeFloatArray(value, "Heights") ?? new float[HeightmapResolution * HeightmapResolution];
        Splats = DeserializeFloatArray(value, "Splats") ?? CreateDefaultSplats();

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
        var dlList = value.Get("DetailLayers");
        if (dlList != null)
        {
            foreach (var dlEntry in dlList.List)
            {
                var arr = dlEntry?.StringValue != null
                    ? DeserializeFloatArrayDirect(dlEntry.StringValue)
                    : new float[DetailResolution * DetailResolution];
                DetailLayers.Add(arr);
            }
        }
        // Backward compat: old single GrassDensity
        if (DetailLayers.Count == 0)
        {
            var oldGrass = DeserializeFloatArray(value, "GrassDensity");
            DetailLayers.Add(oldGrass ?? new float[DetailResolution * DetailResolution]);
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
        _detailsDirty = true;
    }

    #endregion

    #region Helpers

    private static void SerializeFloatArray(EchoObject value, string key, float[]? data)
    {
        if (data == null) return;
        byte[] bytes = new byte[data.Length * sizeof(float)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        value.Add(key, new EchoObject(Convert.ToBase64String(bytes)));
    }

    private static void SerializeFloatArrayToList(EchoObject list, float[]? data)
    {
        if (data == null) { list.ListAdd(new EchoObject("")); return; }
        byte[] bytes = new byte[data.Length * sizeof(float)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        list.ListAdd(new EchoObject(Convert.ToBase64String(bytes)));
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
        var s = new float[SplatmapResolution * SplatmapResolution * 4];
        for (int i = 0; i < s.Length; i += 4) s[i] = 1f;
        return s;
    }

    #endregion
}
