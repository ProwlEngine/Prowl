// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using GTerrain = Prowl.Graphite.Rendering.TerrainData;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Draws a <see cref="TerrainData"/> through Graphite's terrain: the ground streamed and level of detail picked per view,
/// its layers from the data's textures, grass cards grown on the GPU, and mesh details and trees placed around the
/// camera. The terrain stands at its GameObject's position, unturned and unscaled.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Terrain/Terrain")]
[ComponentIcon("")] // Mountain
public class TerrainComponent : Component, IRenderSync
{
    public TerrainData? Data;

    /// <summary>A material whose shader has Surface "Terrain", or null for the ground's own.</summary>
    public Material? Material;

    /// <summary>How far out finer levels reach, at least 1.5. Higher keeps more detail in the distance.</summary>
    public float LODQuality = 2f;

    /// <summary>The metres around the camera mesh details are placed.</summary>
    public float DetailDistance = 150f;

    /// <summary>The metres around the camera trees are placed.</summary>
    public float TreeDistance = 500f;

    /// <summary>Shadow casting of the details and trees.</summary>
    public ShadowCasting ShadowCasting = ShadowCasting.On;
    public bool ReceiveShadows = true;

    /// <summary>The renderable flags the details and trees draw with.</summary>
    internal RenderableFlags RenderFlags => RenderableState.FlagsFor(GameObject, false, ReceiveShadows) | ShadowCasting.ToFlags() | RenderableFlags.Static;

    #region Brush Preview (set by editor each frame)

    /// <summary>The brush's middle and radius across the terrain from 0 to 1, and the share of the radius it softens over.</summary>
    [NonSerialized] public Float2 BrushPosition;
    [NonSerialized] public float BrushRadius;
    [NonSerialized] public float BrushFalloff;
    [NonSerialized] public bool BrushVisible;

    #endregion

    [NonSerialized, NotHeld] private RenderWorld? _world;
    [NonSerialized, NotHeld] private TerrainSurface? _surface;
    [NonSerialized, NotHeld] private GTerrain? _built;
    [NonSerialized, NotHeld] private TerrainLayers? _layers;
    [NonSerialized, NotHeld] private TerrainCards? _cards;
    [NonSerialized, NotHeld] private TerrainDetailInstances? _detailInstances;
    [NonSerialized, NotHeld] private TerrainProps? _details;
    [NonSerialized, NotHeld] private TerrainTreeInstances? _treeInstances;
    [NonSerialized, NotHeld] private TerrainProps? _trees;
    [NonSerialized] private int _layerSignature, _detailSignature, _treeSignature;

    public float TerrainSize { get { TerrainData? d = Data; return d.IsValid() ? d.Size : 1024f; } }
    public float TerrainHeight { get { TerrainData? d = Data; return d.IsValid() ? d.Height : 100f; } }

    public Float3 TerrainToWorld(Float3 localPoint) => localPoint + Transform.Position;
    public Float3 WorldToTerrain(Float3 worldPoint) => worldPoint - Transform.Position;

    /// <summary>The ground the terrain draws, null before it first syncs.</summary>
    public TerrainSurface? Surface => _surface;

    #region Rendering

    bool IRenderSync.Sync(RenderWorld world)
    {
        TerrainData? data = Data;
        Renderer? renderer = Graphics.Renderer;
        if (data.IsNotValid() || renderer == null)
        {
            Release(world);
            return false;
        }

        GTerrain terrain = data.Terrain;
        Float3 position = Transform.Position;
        terrain.Position = new Double3(position.X, position.Y, position.Z);
        if (_surface == null || _world != world || !ReferenceEquals(_built, terrain))
        {
            Release(world);
            _world = world;
            _built = terrain;
            _surface = new TerrainSurface(renderer, terrain);
            world.Providers.Add(_surface);
        }

        if (Material.IsValid() && GpuMaterialOf(Material) is { } material && !ReferenceEquals(_surface.Material, material))
            _surface.Material = material;
        _surface.LodQuality = Math.Max(LODQuality, 1.5);
        _surface.Brush = BrushVisible && BrushRadius > 0
            ? new TerrainBrushOverlay(new Double2(terrain.Position.X + BrushPosition.X * data.Size, terrain.Position.Z + BrushPosition.Y * data.Size), BrushRadius * data.Size, BrushFalloff, new Float3(0.2f, 0.8f, 0.6f))
            : null;

        SyncLayers(renderer, data);
        SyncDetails(renderer, world, data, terrain);
        SyncTrees(world, data, terrain);

        IReadOnlyList<Double3> viewers = _surface.Viewers;
        Double3 viewer = viewers.Count > 0 ? viewers[0] : terrain.Position;
        _details?.Update(viewer);
        _trees?.Update(viewer);
        return false;
    }

    void IRenderSync.Detach(RenderWorld world) => Release(world);

    private void Release(RenderWorld world)
    {
        if (_surface != null)
        {
            (_world ?? world).Providers.Remove(_surface);
            Graphics.DeferDispose(_surface);
        }
        _details?.Dispose();
        _detailInstances?.Dispose();
        _trees?.Dispose();
        _treeInstances?.Dispose();
        Graphics.DeferDispose(_layers);
        Graphics.DeferDispose(_cards);
        (_surface, _built, _world, _layers, _cards) = (null, null, null, null, null);
        (_details, _detailInstances, _trees, _treeInstances) = (null, null, null, null);
        (_layerSignature, _detailSignature, _treeSignature) = (0, 0, 0);
    }

    private void SyncLayers(Renderer renderer, TerrainData data)
    {
        HashCode hash = new();
        bool ready = true;
        foreach (TerrainLayer layer in data.Layers)
        {
            ready &= Ready(layer.Albedo, ref hash) & Ready(layer.NormalMap, ref hash);
            hash.Add((layer.Tiling, layer.Roughness, layer.HeightBlend, layer.HexTiling, layer.MacroVariation));
        }
        int signature = hash.ToHashCode() | 1;
        if (!ready || signature == _layerSignature)
            return;

        int size = 4;
        foreach (TerrainLayer layer in data.Layers)
            size = Math.Max(size, TerrainTextures.SizeFor(1024, layer.Albedo, layer.NormalMap));
        List<TerrainLayerDesc> descs = new(data.Layers.Count);
        foreach (TerrainLayer layer in data.Layers)
        {
            byte rough = (byte)Math.Clamp(MathF.Round(layer.Roughness * 255f), 0, 255);
            byte[] albedo = layer.Albedo.IsValid() ? TerrainTextures.Read(layer.Albedo, size) ?? TerrainTextures.Solid(size, 128, 128, 128, 128) : TerrainTextures.Solid(size, 128, 128, 128, 128);
            byte[] surface = TerrainTextures.Solid(size, 128, 128, rough, 255);
            if (layer.NormalMap.IsValid() && TerrainTextures.Read(layer.NormalMap, size) is { } normal)
                for (int i = 0; i < size * size; i++)
                    (surface[i * 4], surface[i * 4 + 1]) = (normal[i * 4], normal[i * 4 + 1]);
            descs.Add(new TerrainLayerDesc
            {
                Size = size,
                Albedo = albedo,
                Surface = surface,
                TileSize = layer.Tiling,
                HeightBlend = layer.HeightBlend,
                HexTiling = layer.HexTiling,
                MacroVariation = layer.MacroVariation,
            });
        }
        TerrainLayers? old = _layers;
        _layers = descs.Count > 0 ? new TerrainLayers(renderer, descs) : null;
        _surface!.Layers = _layers;
        Graphics.DeferDispose(old);
        _layerSignature = signature;
    }

    private void SyncDetails(Renderer renderer, RenderWorld world, TerrainData data, GTerrain terrain)
    {
        HashCode hash = new();
        hash.Add(DetailDistance);
        hash.Add(RenderFlags);
        bool ready = true;
        foreach (DetailPrototype p in data.DetailPrototypes)
        {
            hash.Add((p.RenderMode, p.Density, p.Range, p.MinWidth, p.MaxWidth, p.MinHeight, p.MaxHeight, p.NoiseSize));
            hash.Add((p.HealthyColor, p.DryColor, p.RootColor, p.TipColor, p.GroundTint, p.Stiffness, p.AlignToNormal));
            if (p.RenderMode == DetailRenderMode.Grass)
                ready &= Ready(p.Texture, ref hash);
            else
                hash.Add(KindOf(p.Mesh, p.Materials) != null);
        }
        int signature = hash.ToHashCode() | 1;
        if (!ready || signature == _detailSignature)
            return;

        List<byte[]> cards = [];
        int cardSize = 4;
        foreach (DetailPrototype p in data.DetailPrototypes)
            if (p.RenderMode == DetailRenderMode.Grass)
                cardSize = Math.Max(cardSize, TerrainTextures.SizeFor(512, p.Texture));

        TerrainDetailPrototype[] prototypes = new TerrainDetailPrototype[data.DetailPrototypes.Count];
        TerrainPropKind?[] kinds = new TerrainPropKind?[prototypes.Length];
        for (int i = 0; i < prototypes.Length; i++)
        {
            DetailPrototype p = data.DetailPrototypes[i];
            bool grass = p.RenderMode == DetailRenderMode.Grass;
            kinds[i] = grass ? null : KindOf(p.Mesh, p.Materials);
            if (grass)
                cards.Add(p.Texture.IsValid() ? TerrainTextures.Read(p.Texture, cardSize) ?? TerrainTextures.Solid(cardSize, 255, 255, 255, 255) : TerrainTextures.Solid(cardSize, 255, 255, 255, 255));
            prototypes[i] = new TerrainDetailPrototype
            {
                Mesh = kinds[i],
                Density = grass || kinds[i] != null ? p.Density : 0f,
                Range = p.Range,
                MinWidth = p.MinWidth,
                MaxWidth = p.MaxWidth,
                MinHeight = p.MinHeight,
                MaxHeight = p.MaxHeight,
                NoiseSize = p.NoiseSize,
                HealthyColor = p.HealthyColor.ToFloat4(),
                DryColor = p.DryColor.ToFloat4(),
                RootColor = new Float3(p.RootColor.R, p.RootColor.G, p.RootColor.B),
                TipColor = new Float3(p.TipColor.R, p.TipColor.G, p.TipColor.B),
                GroundTint = p.GroundTint,
                Stiffness = p.Stiffness,
                AlignToGround = p.AlignToNormal ? 1f : 0f,
                Card = grass ? cards.Count - 1 : 0,
            };
        }

        TerrainCards? oldCards = _cards;
        _cards = cards.Count > 0 ? new TerrainCards(renderer, cardSize, cards) : null;
        _surface!.GrassCards = _cards?.Texture;
        _surface.Details = prototypes;
        Graphics.DeferDispose(oldCards);

        _details?.Dispose();
        _detailInstances?.Dispose();
        _detailInstances = new TerrainDetailInstances(terrain, prototypes);
        _details = new TerrainProps(world, _detailInstances, kinds) { Range = DetailDistance, CellsPerUpdate = 16 };
        _detailSignature = signature;
    }

    private void SyncTrees(RenderWorld world, TerrainData data, GTerrain terrain)
    {
        HashCode hash = new();
        hash.Add(TreeDistance);
        hash.Add(RenderFlags);
        TerrainPropKind?[] kinds = new TerrainPropKind?[data.TreePrototypes.Count];
        for (int i = 0; i < kinds.Length; i++)
        {
            kinds[i] = KindOf(data.TreePrototypes[i].Mesh, data.TreePrototypes[i].Materials);
            hash.Add(kinds[i]?.Mesh);
            foreach (GMaterial m in kinds[i]?.Materials ?? [])
                hash.Add(m);
        }
        int signature = hash.ToHashCode() | 1;
        if (signature == _treeSignature)
            return;
        _trees?.Dispose();
        _treeInstances?.Dispose();
        _treeInstances = new TerrainTreeInstances(terrain);
        _trees = new TerrainProps(world, _treeInstances, kinds) { Range = TreeDistance, CellsPerUpdate = 64 };
        _treeSignature = signature;
    }

    // Whether a texture is ready to read, loading it when it is not, adding what it holds to the hash
    private static bool Ready(Texture2D? texture, ref HashCode hash)
    {
        if (texture.IsNotValid())
        {
            hash.Add(0);
            return true;
        }
        if (!texture.IsLoaded)
        {
            if (texture.State == AssetState.Unloaded)
                _ = texture.LoadAsync();
            return false;
        }
        hash.Add(RuntimeHelpers.GetHashCode(texture));
        hash.Add(texture.ContentVersion);
        return true;
    }

    // What a prop draws as: the mesh with a material for each submesh, null while any of it loads
    private TerrainPropKind? KindOf(Mesh? mesh, List<Material?> materials)
    {
        if (GpuMeshOf(mesh) is not { } gpu)
            return null;
        int count = mesh!.SubMeshCount;
        GMaterial[] resolved = new GMaterial[count];
        for (int s = 0; s < count; s++)
        {
            Material? material = s < materials.Count ? materials[s] : null;
            if (material.IsNotValid())
                material = materials.Find(m => m.IsValid());
            if ((material.IsValid() ? GpuMaterialOf(material) : RenderableState.DefaultGpuMaterial) is not { } ready)
                return null;
            resolved[s] = ready;
        }
        return new TerrainPropKind { Mesh = gpu, Materials = resolved, Flags = RenderFlags };
    }

    /// <summary>The Graphite material of a Prowl material ready to draw, loading it when it is not. Null while it cannot.</summary>
    internal static GMaterial? GpuMaterialOf(Material? material)
    {
        if (material.IsNotValid()) return null;
        if (!material.IsLoaded)
        {
            if (material.State == AssetState.Unloaded) _ = material.LoadAsync();
            return null;
        }
        return material.GpuMaterial;
    }

    /// <summary>The Graphite mesh of a Prowl mesh, uploaded and ready to draw, loading it when it is not.</summary>
    internal static GpuMesh? GpuMeshOf(Mesh? mesh)
    {
        if (mesh.IsNotValid()) return null;
        if (!mesh.IsLoaded)
        {
            if (mesh.State == AssetState.Unloaded) _ = mesh.LoadAsync();
            return null;
        }
        mesh.Upload();
        return mesh.GpuIfLoaded;
    }

    #endregion

    /// <summary>Casts a ray at the ground, giving where it hits in the world and across the terrain from 0 to 1.</summary>
    public bool Raycast(Ray ray, out Float3 hitPoint, out Float2 terrainUV)
    {
        hitPoint = Float3.Zero;
        terrainUV = Float2.Zero;
        TerrainData? data = Data;
        if (data.IsNotValid())
            return false;
        GTerrain terrain = data.Terrain;
        Float3 position = Transform.Position;
        terrain.Position = new Double3(position.X, position.Y, position.Z);
        Float3 direction = Float3.Normalize(ray.Direction);
        if (!terrain.Raycast(new Double3(ray.Origin.X, ray.Origin.Y, ray.Origin.Z), direction, data.Size * 4.0 + data.Height, out TerrainHit hit))
            return false;
        hitPoint = new Float3((float)hit.Position.X, (float)hit.Position.Y, (float)hit.Position.Z);
        terrainUV = new Float2((hitPoint.X - position.X) / data.Size, (hitPoint.Z - position.Z) / data.Size);
        return true;
    }
}
