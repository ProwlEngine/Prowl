// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>Per-layer texture settings for terrain rendering.</summary>
public class TerrainLayer
{
    public AssetRef<Texture2D> Albedo;
    public AssetRef<Texture2D> NormalMap;
    public float Tiling = 10f;
    public float Roughness = 1f;
    public float Metallic = 0f;
}

/// <summary>Defines a grass type for terrain vegetation.</summary>
public class TerrainGrassType
{
    public AssetRef<Texture2D> Texture;
    /// <summary>Min width in world units.</summary>
    public float MinWidth = 1f;
    /// <summary>Max width in world units.</summary>
    public float MaxWidth = 2f;
    /// <summary>Min height in world units.</summary>
    public float MinHeight = 1f;
    /// <summary>Max height in world units.</summary>
    public float MaxHeight = 2f;
    /// <summary>Perlin noise spread for size/color variation.</summary>
    public float NoiseSpread = 0.1f;
    /// <summary>Healthy (fully saturated) color.</summary>
    public Color Tint = new Color(0.26f, 0.97f, 0.16f, 1f);
    /// <summary>Dry (desaturated) color.</summary>
    public Color DryTint = new Color(0.80f, 0.73f, 0.10f, 1f);
    /// <summary>How much grass bends in wind (0-1).</summary>
    public float BendFactor = 0.5f;
}

/// <summary>A placed tree instance on the terrain.</summary>
public struct TreeInstance
{
    public Float2 Position;      // terrain UV (0-1)
    public int PrototypeIndex;   // index into TerrainData.TreePrototypes
    public float Rotation;       // Y-axis rotation in radians
    public float Scale;          // uniform scale multiplier
    public Color Tint;           // per-instance color variation
}

/// <summary>Defines a tree type (mesh + material) for terrain vegetation.</summary>
public class TerrainTreePrototype
{
    public AssetRef<Mesh> Mesh;
    public AssetRef<Material> Material; // null = use Standard material
    public float MinScale = 0.8f;
    public float MaxScale = 1.2f;
}

/// <summary>
/// Stores terrain heightmap, splatmap, and layer configuration as a reusable asset.
/// Referenced by TerrainComponent for rendering and TerrainCollider for physics.
/// </summary>
[CreateAssetMenu("Terrain Data", Extension = ".terraindata", Order = 3)]
public sealed class TerrainData : EngineObject, ISerializable
{
    /// <summary>Resolution of the heightmap (width and height in pixels).</summary>
    public int HeightmapResolution = 513;

    /// <summary>Resolution of the splatmap (width and height in pixels).</summary>
    public int SplatmapResolution = 512;

    /// <summary>World size of the terrain in the XZ plane.</summary>
    public float Size = 1024f;

    /// <summary>Maximum height of the terrain.</summary>
    public float Height = 100f;

    /// <summary>
    /// Raw heightmap data stored as normalized floats [0..1].
    /// Indexed as heights[z * HeightmapResolution + x].
    /// </summary>
    public float[] Heights;

    /// <summary>
    /// Splatmap weights for up to 4 layers. Each pixel has 4 floats (RGBA).
    /// Indexed as splats[(z * SplatmapResolution + x) * 4 + channel].
    /// </summary>
    public float[] Splats;

    /// <summary>4 terrain layers with per-layer textures and settings.</summary>
    public TerrainLayer[] Layers = [new(), new(), new(), new()];

    // --- Vegetation ---

    /// <summary>Resolution of the grass density map.</summary>
    public int GrassmapResolution = 1024;

    /// <summary>Grass density map. Single channel [0..1] per pixel.</summary>
    public float[] GrassDensity;

    /// <summary>Grass type definitions (up to 4).</summary>
    public TerrainGrassType[] GrassTypes = [new()];

    /// <summary>Placed tree instances.</summary>
    public List<TreeInstance> Trees = [];

    /// <summary>Tree prototype definitions.</summary>
    public TerrainTreePrototype[] TreePrototypes = [];

    // GPU textures generated from the raw data
    [NonSerialized] private Texture2D? _heightmapTexture;
    [NonSerialized] private Texture2D? _splatmapTexture;
    [NonSerialized] private Texture2D? _grassmapTexture;
    [NonSerialized] private bool _heightmapDirty = true;
    [NonSerialized] private bool _splatmapDirty = true;
    [NonSerialized] private bool _grassmapDirty = true;

    public TerrainData() : base("New TerrainData")
    {
        Heights = new float[HeightmapResolution * HeightmapResolution];
        Splats = new float[SplatmapResolution * SplatmapResolution * 4];
        GrassDensity = new float[GrassmapResolution * GrassmapResolution];

        // Default splatmap: layer 0 fully opaque
        for (int i = 0; i < Splats.Length; i += 4)
            Splats[i] = 1f;
    }

    /// <summary>Get or set the height at a specific grid coordinate (0..1 range).</summary>
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

    /// <summary>Get the interpolated world-space height at a normalized position (0..1).</summary>
    public float GetInterpolatedHeight(float u, float v)
    {
        if (Heights == null) return 0f;

        float px = u * (HeightmapResolution - 1);
        float pz = v * (HeightmapResolution - 1);

        int x0 = Maths.Clamp((int)MathF.Floor(px), 0, HeightmapResolution - 1);
        int z0 = Maths.Clamp((int)MathF.Floor(pz), 0, HeightmapResolution - 1);
        int x1 = Maths.Min(x0 + 1, HeightmapResolution - 1);
        int z1 = Maths.Min(z0 + 1, HeightmapResolution - 1);

        float fx = px - x0;
        float fz = pz - z0;

        float h00 = Heights[z0 * HeightmapResolution + x0];
        float h10 = Heights[z0 * HeightmapResolution + x1];
        float h01 = Heights[z1 * HeightmapResolution + x0];
        float h11 = Heights[z1 * HeightmapResolution + x1];

        float h0 = h00 * (1 - fx) + h10 * fx;
        float h1 = h01 * (1 - fx) + h11 * fx;
        return (h0 * (1 - fz) + h1 * fz) * Height;
    }

    /// <summary>Get splat weight for a specific layer at a grid coordinate.</summary>
    public float GetSplat(int x, int z, int channel)
    {
        if (Splats == null || channel < 0 || channel > 3 ||
            x < 0 || x >= SplatmapResolution || z < 0 || z >= SplatmapResolution)
            return 0f;
        return Splats[(z * SplatmapResolution + x) * 4 + channel];
    }

    /// <summary>Set splat weight for a specific layer at a grid coordinate.</summary>
    public void SetSplat(int x, int z, int channel, float value)
    {
        if (Splats == null || channel < 0 || channel > 3 ||
            x < 0 || x >= SplatmapResolution || z < 0 || z >= SplatmapResolution)
            return;
        Splats[(z * SplatmapResolution + x) * 4 + channel] = Maths.Clamp(value, 0f, 1f);
        _splatmapDirty = true;
    }

    /// <summary>Resize heightmap to a new resolution. Resets all height data to 0.</summary>
    public void ResizeHeightmap(int newResolution)
    {
        HeightmapResolution = newResolution;
        Heights = new float[newResolution * newResolution];
        _heightmapDirty = true;
    }

    /// <summary>Resize splatmap to a new resolution. Resets to layer 0 fully opaque.</summary>
    public void ResizeSplatmap(int newResolution)
    {
        SplatmapResolution = newResolution;
        Splats = new float[newResolution * newResolution * 4];
        for (int i = 0; i < Splats.Length; i += 4)
            Splats[i] = 1f;
        _splatmapDirty = true;
    }

    /// <summary>Get grass density at a grid coordinate.</summary>
    public float GetGrassDensity(int x, int z)
    {
        if (GrassDensity == null || x < 0 || x >= GrassmapResolution || z < 0 || z >= GrassmapResolution)
            return 0f;
        return GrassDensity[z * GrassmapResolution + x];
    }

    /// <summary>Set grass density at a grid coordinate.</summary>
    public void SetGrassDensity(int x, int z, float value)
    {
        if (GrassDensity == null || x < 0 || x >= GrassmapResolution || z < 0 || z >= GrassmapResolution)
            return;
        GrassDensity[z * GrassmapResolution + x] = Maths.Clamp(value, 0f, 1f);
        _grassmapDirty = true;
    }

    /// <summary>Resize grass density map. Resets to zero.</summary>
    public void ResizeGrassmap(int newResolution)
    {
        GrassmapResolution = newResolution;
        GrassDensity = new float[newResolution * newResolution];
        _grassmapDirty = true;
    }

    /// <summary>Mark the heightmap as dirty so the GPU texture is regenerated.</summary>
    public void SetHeightmapDirty() => _heightmapDirty = true;

    /// <summary>Mark the splatmap as dirty so the GPU texture is regenerated.</summary>
    public void SetSplatmapDirty() => _splatmapDirty = true;

    /// <summary>Mark the grass density map as dirty so the GPU texture is regenerated.</summary>
    public void SetGrassmapDirty() => _grassmapDirty = true;

    /// <summary>Get the GPU grass density texture, regenerating if dirty.</summary>
    public Texture2D? GetGrassmapTexture()
    {
        if (GrassDensity == null) return null;

        if (_grassmapDirty || _grassmapTexture == null)
        {
            _grassmapTexture?.Dispose();
            _grassmapTexture = new Texture2D((uint)GrassmapResolution, (uint)GrassmapResolution, false, TextureImageFormat.Float);
            _grassmapTexture.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            Graphics.SetWrapS(_grassmapTexture.Handle, TextureWrap.ClampToEdge);
            Graphics.SetWrapT(_grassmapTexture.Handle, TextureWrap.ClampToEdge);

            unsafe
            {
                fixed (float* ptr = GrassDensity)
                    _grassmapTexture.SetDataPtr(ptr, 0, 0, (uint)GrassmapResolution, (uint)GrassmapResolution);
            }
            _grassmapDirty = false;
        }

        return _grassmapTexture;
    }

    /// <summary>Get the GPU heightmap texture, regenerating if dirty.</summary>
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

            unsafe
            {
                fixed (float* ptr = Heights)
                    _heightmapTexture.SetDataPtr(ptr, 0, 0, (uint)HeightmapResolution, (uint)HeightmapResolution);
            }
            _heightmapDirty = false;
        }

        return _heightmapTexture;
    }

    /// <summary>Get the GPU splatmap texture, regenerating if dirty.</summary>
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

            unsafe
            {
                fixed (float* ptr = Splats)
                    _splatmapTexture.SetDataPtr(ptr, 0, 0, (uint)SplatmapResolution, (uint)SplatmapResolution);
            }
            _splatmapDirty = false;
        }

        return _splatmapTexture;
    }

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        value.Add("Name", new EchoObject(Name));
        value.Add("HeightmapResolution", new EchoObject(HeightmapResolution));
        value.Add("SplatmapResolution", new EchoObject(SplatmapResolution));
        value.Add("Size", new EchoObject(Size));
        value.Add("Height", new EchoObject(Height));

        // Serialize layers
        var layerList = EchoObject.NewList();
        for (int i = 0; i < 4; i++)
        {
            var l = Layers[i];
            var layerObj = EchoObject.NewCompound();
            layerObj.Add("Albedo", Serializer.Serialize(l.Albedo, ctx));
            layerObj.Add("NormalMap", Serializer.Serialize(l.NormalMap, ctx));
            layerObj.Add("Tiling", new EchoObject(l.Tiling));
            layerObj.Add("Roughness", new EchoObject(l.Roughness));
            layerObj.Add("Metallic", new EchoObject(l.Metallic));
            layerList.ListAdd(layerObj);
        }
        value.Add("Layers", layerList);

        // Serialize raw height data as base64
        if (Heights != null)
        {
            byte[] heightBytes = new byte[Heights.Length * sizeof(float)];
            Buffer.BlockCopy(Heights, 0, heightBytes, 0, heightBytes.Length);
            value.Add("Heights", new EchoObject(Convert.ToBase64String(heightBytes)));
        }

        // Serialize raw splat data as base64
        if (Splats != null)
        {
            byte[] splatBytes = new byte[Splats.Length * sizeof(float)];
            Buffer.BlockCopy(Splats, 0, splatBytes, 0, splatBytes.Length);
            value.Add("Splats", new EchoObject(Convert.ToBase64String(splatBytes)));
        }

        // Grass density map
        value.Add("GrassmapResolution", new EchoObject(GrassmapResolution));
        if (GrassDensity != null)
        {
            byte[] grassBytes = new byte[GrassDensity.Length * sizeof(float)];
            Buffer.BlockCopy(GrassDensity, 0, grassBytes, 0, grassBytes.Length);
            value.Add("GrassDensity", new EchoObject(Convert.ToBase64String(grassBytes)));
        }

        // Grass types
        var grassTypeList = EchoObject.NewList();
        foreach (var gt in GrassTypes)
        {
            var gto = EchoObject.NewCompound();
            gto.Add("Texture", Serializer.Serialize(gt.Texture, ctx));
            gto.Add("MinHeight", new EchoObject(gt.MinHeight));
            gto.Add("MaxHeight", new EchoObject(gt.MaxHeight));
            gto.Add("MinWidth", new EchoObject(gt.MinWidth));
            gto.Add("MaxWidth", new EchoObject(gt.MaxWidth));
            gto.Add("Tint", Serializer.Serialize(gt.Tint, ctx));
            gto.Add("DryTint", Serializer.Serialize(gt.DryTint, ctx));
            grassTypeList.ListAdd(gto);
        }
        value.Add("GrassTypes", grassTypeList);

        // Tree prototypes
        var protoList = EchoObject.NewList();
        foreach (var tp in TreePrototypes)
        {
            var tpo = EchoObject.NewCompound();
            tpo.Add("Mesh", Serializer.Serialize(tp.Mesh, ctx));
            tpo.Add("Material", Serializer.Serialize(tp.Material, ctx));
            tpo.Add("MinScale", new EchoObject(tp.MinScale));
            tpo.Add("MaxScale", new EchoObject(tp.MaxScale));
            protoList.ListAdd(tpo);
        }
        value.Add("TreePrototypes", protoList);

        // Tree instances
        var treeList = EchoObject.NewList();
        foreach (var ti in Trees)
        {
            var tio = EchoObject.NewCompound();
            tio.Add("PosX", new EchoObject(ti.Position.X));
            tio.Add("PosY", new EchoObject(ti.Position.Y));
            tio.Add("Proto", new EchoObject(ti.PrototypeIndex));
            tio.Add("Rot", new EchoObject(ti.Rotation));
            tio.Add("Scale", new EchoObject(ti.Scale));
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

        // Deserialize layers
        var layerList = value.Get("Layers");
        Layers = [new(), new(), new(), new()];
        if (layerList != null)
        {
            for (int i = 0; i < Math.Min(4, layerList.List.Count); i++)
            {
                var lo = layerList.List[i];
                Layers[i].Albedo = Serializer.Deserialize<AssetRef<Texture2D>>(lo.Get("Albedo"), ctx);
                Layers[i].NormalMap = Serializer.Deserialize<AssetRef<Texture2D>>(lo.Get("NormalMap"), ctx);
                Layers[i].Tiling = lo.Get("Tiling")?.FloatValue ?? 10f;
                Layers[i].Roughness = lo.Get("Roughness")?.FloatValue ?? 1f;
                Layers[i].Metallic = lo.Get("Metallic")?.FloatValue ?? 0f;
            }
        }

        // Deserialize height data
        string? heightB64 = value.Get("Heights")?.StringValue;
        if (heightB64 != null)
        {
            byte[] heightBytes = Convert.FromBase64String(heightB64);
            Heights = new float[heightBytes.Length / sizeof(float)];
            Buffer.BlockCopy(heightBytes, 0, Heights, 0, heightBytes.Length);
        }
        else
        {
            Heights = new float[HeightmapResolution * HeightmapResolution];
        }

        // Deserialize splat data
        string? splatB64 = value.Get("Splats")?.StringValue;
        if (splatB64 != null)
        {
            byte[] splatBytes = Convert.FromBase64String(splatB64);
            Splats = new float[splatBytes.Length / sizeof(float)];
            Buffer.BlockCopy(splatBytes, 0, Splats, 0, splatBytes.Length);
        }
        else
        {
            Splats = new float[SplatmapResolution * SplatmapResolution * 4];
            for (int i = 0; i < Splats.Length; i += 4)
                Splats[i] = 1f;
        }

        // Grass density
        GrassmapResolution = value.Get("GrassmapResolution")?.IntValue ?? 256;
        string? grassB64 = value.Get("GrassDensity")?.StringValue;
        if (grassB64 != null)
        {
            byte[] grassBytes = Convert.FromBase64String(grassB64);
            GrassDensity = new float[grassBytes.Length / sizeof(float)];
            Buffer.BlockCopy(grassBytes, 0, GrassDensity, 0, grassBytes.Length);
        }
        else
        {
            GrassDensity = new float[GrassmapResolution * GrassmapResolution];
        }

        // Grass types
        var grassTypeList = value.Get("GrassTypes");
        if (grassTypeList != null && grassTypeList.List.Count > 0)
        {
            GrassTypes = new TerrainGrassType[grassTypeList.List.Count];
            for (int i = 0; i < grassTypeList.List.Count; i++)
            {
                var gto = grassTypeList.List[i];
                GrassTypes[i] = new TerrainGrassType
                {
                    Texture = Serializer.Deserialize<AssetRef<Texture2D>>(gto.Get("Texture"), ctx),
                    MinHeight = gto.Get("MinHeight")?.FloatValue ?? 0.5f,
                    MaxHeight = gto.Get("MaxHeight")?.FloatValue ?? 1.2f,
                    MinWidth = gto.Get("MinWidth")?.FloatValue ?? 0.3f,
                    MaxWidth = gto.Get("MaxWidth")?.FloatValue ?? 0.6f,
                    Tint = Serializer.Deserialize<Color>(gto.Get("Tint"), ctx),
                    DryTint = Serializer.Deserialize<Color>(gto.Get("DryTint"), ctx),
                };
            }
        }
        else
        {
            GrassTypes = [new()];
        }

        // Tree prototypes
        var protoList = value.Get("TreePrototypes");
        if (protoList != null && protoList.List.Count > 0)
        {
            TreePrototypes = new TerrainTreePrototype[protoList.List.Count];
            for (int i = 0; i < protoList.List.Count; i++)
            {
                var tpo = protoList.List[i];
                TreePrototypes[i] = new TerrainTreePrototype
                {
                    Mesh = Serializer.Deserialize<AssetRef<Mesh>>(tpo.Get("Mesh"), ctx),
                    Material = Serializer.Deserialize<AssetRef<Material>>(tpo.Get("Material"), ctx),
                    MinScale = tpo.Get("MinScale")?.FloatValue ?? 0.8f,
                    MaxScale = tpo.Get("MaxScale")?.FloatValue ?? 1.2f,
                };
            }
        }
        else
        {
            TreePrototypes = [];
        }

        // Tree instances
        Trees = [];
        var treeList = value.Get("Trees");
        if (treeList != null)
        {
            foreach (var tio in treeList.List)
            {
                Trees.Add(new TreeInstance
                {
                    Position = new Float2(
                        tio.Get("PosX")?.FloatValue ?? 0f,
                        tio.Get("PosY")?.FloatValue ?? 0f),
                    PrototypeIndex = tio.Get("Proto")?.IntValue ?? 0,
                    Rotation = tio.Get("Rot")?.FloatValue ?? 0f,
                    Scale = tio.Get("Scale")?.FloatValue ?? 1f,
                    Tint = Serializer.Deserialize<Color>(tio.Get("Tint"), ctx),
                });
            }
        }

        _heightmapDirty = true;
        _splatmapDirty = true;
        _grassmapDirty = true;
    }
}
