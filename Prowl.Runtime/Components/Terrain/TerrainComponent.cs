// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// GPU-instanced terrain component with quadtree LOD.
/// Renders terrain using a single mesh instanced many times.
/// Heightmap is sampled in the vertex shader for displacement.
/// References a TerrainData asset for height/splat/layer configuration.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Terrain/Terrain")]
public class TerrainComponent : MonoBehaviour
{
    #region Configuration

    /// <summary>The terrain data asset containing heightmap, splatmap, and layer configuration.</summary>
    public AssetRef<TerrainData> Data;

    /// <summary>Base material. Terrain clones this internally to set its own properties.</summary>
    public AssetRef<Material> Material;

    /// <summary>Maximum LOD subdivision levels for the quadtree.</summary>
    public int MaxLODLevel = 4;

    /// <summary>Resolution of the base mesh grid (vertices per side).</summary>
    public int MeshResolution = 16;

    /// <summary>Grass material override. If null, uses built-in Grass material.</summary>
    public AssetRef<Material> GrassMaterial;

    /// <summary>Maximum render distance for grass.</summary>
    public float GrassDistance = 150f;

    /// <summary>Global grass density multiplier.</summary>
    public float GrassDensityMultiplier = 1f;

    /// <summary>Maximum render distance for trees.</summary>
    public float TreeDistance = 500f;

    #endregion

    #region State

    private TerrainQuadtree _quadtree;
    private Mesh _baseMesh;
    private Float4x4[] _transforms = Array.Empty<Float4x4>();
    private PropertyState _properties = new();

    // Material instance (cloned from the assigned material so we don't modify the asset)
    [NonSerialized] private Material? _materialInstance;
    [NonSerialized] private Guid _lastMaterialGuid;

    // Vegetation renderers
    [NonSerialized] private TerrainGrassRenderer? _grassRenderer;
    [NonSerialized] private TerrainTreeRenderer? _treeRenderer;
    [NonSerialized] private Material? _grassMaterialInstance;
    [NonSerialized] private Guid _lastGrassMaterialGuid;

    #endregion

    #region Brush Preview (set by editor each frame)

    [NonSerialized] public Float2 BrushPosition;
    [NonSerialized] public float BrushRadius;
    [NonSerialized] public float BrushFalloff;
    [NonSerialized] public bool BrushVisible;

    #endregion

    #region Public Accessors

    /// <summary>Invalidate cached grass patches (call after painting grass density).</summary>
    public void InvalidateGrassCache() => _grassRenderer?.InvalidateCache();

    /// <summary>Shortcut to terrain size from the data asset.</summary>
    public float TerrainSize => Data.Res?.Size ?? 1024f;

    /// <summary>Shortcut to terrain height from the data asset.</summary>
    public float TerrainHeight => Data.Res?.Height ?? 100f;

    #endregion

    #region Lifecycle

    public override void OnEnable()
    {
        base.OnEnable();
        if (_baseMesh == null)
            CreateBaseMesh();
        if (_quadtree == null)
            _quadtree = new TerrainQuadtree(Float3.Zero, TerrainSize, MaxLODLevel);

        _grassRenderer ??= new TerrainGrassRenderer();
        _grassRenderer.Initialize();
        _treeRenderer ??= new TerrainTreeRenderer();
    }

    public override void OnDisable()
    {
        base.OnDisable();
        _baseMesh?.Dispose();
        _baseMesh = null;
        _materialInstance = null;
        _grassMaterialInstance = null;
        _grassRenderer?.Dispose();
        _grassRenderer = null;
        _treeRenderer = null;
    }

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        var terrainData = Data.Res;
        if (terrainData == null) return;

        // Update LOD using the camera that's collecting
        Float3 cameraPos = camera.Transform.Position - this.Transform.Position;
        cameraPos.Y = 0;

        float terrainSize = terrainData.Size;
        if (_quadtree == null || MathF.Abs(_quadtree.ChunkSize - terrainSize) > 0.01f)
            _quadtree = new TerrainQuadtree(Float3.Zero, terrainSize, MaxLODLevel);

        _quadtree.Update(cameraPos);
        UpdateInstanceData();

        var mat = GetMaterialInstance();
        if (mat == null || _transforms.Length == 0 || _baseMesh == null) return;

        _properties.Clear();
        _properties.SetInt("_ObjectID", InstanceID);

        // GPU textures from TerrainData
        var heightmapTex = terrainData.GetHeightmapTexture();
        var splatmapTex = terrainData.GetSplatmapTexture();

        if (heightmapTex != null) _properties.SetTexture("_Heightmap", heightmapTex);
        if (splatmapTex != null) _properties.SetTexture("_Splatmap", splatmapTex);

        // Per-layer textures and settings
        for (int i = 0; i < 4; i++)
        {
            var layer = terrainData.Layers[i];
            string prefix = $"_Layer{i}";

            _properties.SetTexture(prefix, layer.Albedo.Res ?? Texture2D.White);
            _properties.SetTexture(prefix + "Normal", layer.NormalMap.Res ?? Texture2D.Normal);

            _properties.SetFloat(prefix + "Tiling", layer.Tiling);
            _properties.SetFloat(prefix + "Roughness", layer.Roughness);
            _properties.SetFloat(prefix + "Metallic", layer.Metallic);
        }

        _properties.SetFloat("_TerrainSize", terrainSize);
        _properties.SetFloat("_TerrainHeight", terrainData.Height);
        _properties.SetVector("_TerrainOffset", this.Transform.Position);

        // Brush preview — set on material instance so they're applied as material uniforms
        mat.SetVector("_BrushPosition", BrushPosition);
        mat.SetFloat("_BrushRadius", BrushRadius);
        mat.SetFloat("_BrushFalloff", BrushFalloff);
        mat.SetFloat("_BrushVisible", BrushVisible ? 1f : 0f);

        float height = terrainData.Height;
        var bounds = new AABB(
            this.Transform.Position + new Float3(0, -height * 2.0f, 0),
            this.Transform.Position + new Float3(terrainSize, height * 2.0f, terrainSize));

        InstancedMeshRenderable.CreateBatched(
            renderables,
            _baseMesh,
            mat,
            _transforms,
            (bounds.Min + bounds.Max) * 0.5f,
            layer: GameObject.LayerIndex,
            properties: _properties,
            bounds: bounds
        );

        // Grass
        var grassMat = GetGrassMaterialInstance();
        if (grassMat != null && terrainData.DetailPrototypes.Count > 0)
        {
            _grassRenderer?.CollectRenderables(terrainData, this, camera, grassMat, GrassDistance, GrassDensityMultiplier, renderables);
        }

        // Trees
        _treeRenderer?.CollectRenderables(terrainData, this, camera, TreeDistance, renderables);
    }

    public override void DrawGizmos()
    {
        // _quadtree?.DrawGizmos(this.Transform.Position);
    }

    #endregion

    #region Material Instance

    private Material? GetMaterialInstance()
    {
        var sourceMat = Material.Res;
        if (sourceMat == null)
            sourceMat = Resources.Material.LoadDefault(DefaultMaterial.Terrain);
        if (sourceMat == null) return null;

        var sourceGuid = Material.AssetID;

        if (_materialInstance == null || _lastMaterialGuid != sourceGuid)
        {
            // Deep copy via serialization roundtrip
            var echo = Serializer.Serialize(sourceMat);
            _materialInstance = Serializer.Deserialize<Material>(echo);
            if (_materialInstance != null)
                _materialInstance.Name = sourceMat.Name + " (Terrain Instance)";
            _lastMaterialGuid = sourceGuid;
        }

        return _materialInstance;
    }

    private Material? GetGrassMaterialInstance()
    {
        var sourceMat = GrassMaterial.Res;
        if (sourceMat == null)
            sourceMat = Resources.Material.LoadDefault(DefaultMaterial.Grass);
        if (sourceMat == null) return null;

        var sourceGuid = GrassMaterial.AssetID;

        if (_grassMaterialInstance == null || _lastGrassMaterialGuid != sourceGuid)
        {
            var echo = Serializer.Serialize(sourceMat);
            _grassMaterialInstance = Serializer.Deserialize<Material>(echo);
            if (_grassMaterialInstance != null)
                _grassMaterialInstance.Name = sourceMat.Name + " (Grass Instance)";
            _lastGrassMaterialGuid = sourceGuid;
        }

        return _grassMaterialInstance;
    }

    #endregion

    #region Raycast

    /// <summary>
    /// Raycast against the terrain heightmap surface.
    /// Returns true if the ray hits the terrain, with the world-space hit point and terrain UV.
    /// </summary>
    public bool Raycast(Ray ray, out Float3 hitPoint, out Float2 terrainUV)
    {
        hitPoint = Float3.Zero;
        terrainUV = Float2.Zero;

        var terrainData = Data.Res;
        if (terrainData == null || terrainData.Heights == null) return false;

        Float3 terrainPos = Transform.Position;
        float size = terrainData.Size;
        float maxHeight = terrainData.Height;

        // Step along the ray
        float stepSize = size / terrainData.HeightmapResolution * 0.5f;
        float maxDist = size * 2f;
        float t = 0f;

        Float3 prevPoint = ray.Origin;
        float prevDiff = GetHeightDiff(ray.Origin, terrainPos, terrainData);
        bool prevAbove = prevDiff > 0;

        while (t < maxDist)
        {
            t += stepSize;
            Float3 point = ray.Origin + ray.Direction * t;
            float diff = GetHeightDiff(point, terrainPos, terrainData);
            bool above = diff > 0;

            if (!above && prevAbove)
            {
                // Crossed the surface — binary search refinement
                float tLo = t - stepSize;
                float tHi = t;
                for (int i = 0; i < 8; i++)
                {
                    float tMid = (tLo + tHi) * 0.5f;
                    Float3 mid = ray.Origin + ray.Direction * tMid;
                    if (GetHeightDiff(mid, terrainPos, terrainData) > 0)
                        tLo = tMid;
                    else
                        tHi = tMid;
                }

                hitPoint = ray.Origin + ray.Direction * ((tLo + tHi) * 0.5f);
                terrainUV = new Float2(
                    (hitPoint.X - terrainPos.X) / size,
                    (hitPoint.Z - terrainPos.Z) / size);
                return true;
            }

            prevDiff = diff;
            prevAbove = above;
        }

        return false;
    }

    private static float GetHeightDiff(Float3 point, Float3 terrainPos, TerrainData data)
    {
        float u = (float)((point.X - terrainPos.X) / data.Size);
        float v = (float)((point.Z - terrainPos.Z) / data.Size);
        if (u < 0 || u > 1 || v < 0 || v > 1) return 1f; // Above (outside terrain)
        float terrainY = (float)terrainPos.Y + data.GetInterpolatedHeight(u, v);
        return (float)point.Y - terrainY;
    }

    #endregion

    #region Mesh Creation

    private void CreateBaseMesh()
    {
        int resolution = MeshResolution;
        int vertexCount = (resolution + 1) * (resolution + 1);
        int indexCount = resolution * resolution * 6;

        Float3[] vertices = new Float3[vertexCount];
        Float2[] uvs = new Float2[vertexCount];
        uint[] indices = new uint[indexCount];

        for (int z = 0; z <= resolution; z++)
        {
            for (int x = 0; x <= resolution; x++)
            {
                int index = z * (resolution + 1) + x;
                float u = x / (float)resolution;
                float v = z / (float)resolution;
                vertices[index] = new Float3(u, 0, v);
                uvs[index] = new Float2(u, v);
            }
        }

        int triIndex = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int vertIndex = z * (resolution + 1) + x;
                indices[triIndex++] = (uint)(vertIndex);
                indices[triIndex++] = (uint)(vertIndex + resolution + 1);
                indices[triIndex++] = (uint)(vertIndex + 1);
                indices[triIndex++] = (uint)(vertIndex + 1);
                indices[triIndex++] = (uint)(vertIndex + resolution + 1);
                indices[triIndex++] = (uint)(vertIndex + resolution + 2);
            }
        }

        _baseMesh = new Mesh();
        _baseMesh.Vertices = vertices;
        _baseMesh.UV = uvs;
        _baseMesh.Indices = indices;
        _baseMesh.RecalculateBounds();
        _baseMesh.Upload();
    }

    #endregion

    #region Instance Data

    private void UpdateInstanceData()
    {
        var visibleChunks = _quadtree.GetVisibleChunks();

        if (_transforms.Length != visibleChunks.Count)
            _transforms = new Float4x4[visibleChunks.Count];

        for (int i = 0; i < visibleChunks.Count; i++)
        {
            var chunk = visibleChunks[i];
            Float3 position = (Float3)(this.Transform.Position + chunk.Position);
            float scale = (float)chunk.Size;
            _transforms[i] = Float4x4.CreateTranslation(position) * Float4x4.CreateScale(scale, 1.0f, scale);
        }
    }

    #endregion
}
