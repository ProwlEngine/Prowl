// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Per-layer texture settings for terrain rendering.
/// </summary>
public class TerrainLayer
{
    public AssetRef<Texture2D> Albedo;
    public AssetRef<Texture2D> NormalMap;
    public float Tiling = 10f;
    public float Roughness = 1f;
    public float Metallic = 0f;
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

    // GPU textures generated from the raw data
    [NonSerialized] private Texture2D? _heightmapTexture;
    [NonSerialized] private Texture2D? _splatmapTexture;
    [NonSerialized] private bool _heightmapDirty = true;
    [NonSerialized] private bool _splatmapDirty = true;

    public TerrainData() : base("New TerrainData")
    {
        Heights = new float[HeightmapResolution * HeightmapResolution];
        Splats = new float[SplatmapResolution * SplatmapResolution * 4];

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

    /// <summary>Mark the heightmap as dirty so the GPU texture is regenerated.</summary>
    public void SetHeightmapDirty() => _heightmapDirty = true;

    /// <summary>Mark the splatmap as dirty so the GPU texture is regenerated.</summary>
    public void SetSplatmapDirty() => _splatmapDirty = true;

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

        _heightmapDirty = true;
        _splatmapDirty = true;
    }
}
