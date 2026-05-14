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
[ComponentIcon("\uf6fc")] // Mountain
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
    public int MeshResolution = 32;

    /// <summary>
    /// LOD quality multiplier. Controls the distance at which the quadtree subdivides.
    /// 1.0 = default, higher = more detail at distance, lower = less detail.
    /// </summary>
    public float LODQuality = 1f;

    /// <summary>Grass material override. If null, uses built-in Grass material.</summary>
    public AssetRef<Material> GrassMaterial;

    /// <summary>Maximum render distance for grass.</summary>
    public float GrassDistance = 150f;

    /// <summary>
    /// Normalized start of the grass blade fade-out (0..1, fraction of <see cref="GrassDistance"/>).
    /// Blades inside this radius are full size; between it and GrassDistance they shrink to zero,
    /// hiding the hard pop that the per-patch cutoff used to show.
    /// </summary>
    public float GrassFadeStart = 0.6f;

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

    // Material instances (cloned from assets each frame so edits propagate immediately)
    [NonSerialized] private Material? _materialInstance;
    [NonSerialized] private Material? _grassMaterialInstance;

    // Vegetation renderers
    [NonSerialized] private TerrainGrassRenderer? _grassRenderer;
    [NonSerialized] private TerrainTreeRenderer? _treeRenderer;

    // Cached default materials and textures (avoid LoadDefault every frame)
    [NonSerialized] private static Material? s_defaultTerrainMat;
    [NonSerialized] private static Material? s_defaultGrassMat;
    [NonSerialized] private static Texture2D? s_defaultWhite;
    [NonSerialized] private static Texture2D? s_defaultNormal;

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

    /// <summary>Transform a point from terrain-local space to world space.</summary>
    public Float3 TerrainToWorld(Float3 localPoint) =>
        Float4x4.TransformPoint(localPoint, Transform.LocalToWorldMatrix);

    /// <summary>Transform a point from world space to terrain-local space.</summary>
    public Float3 WorldToTerrain(Float3 worldPoint) =>
        Float4x4.TransformPoint(worldPoint, Transform.WorldToLocalMatrix);

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        var terrainData = Data.Res;
        if (terrainData == null) return;

        float terrainSize = terrainData.Size;
        Float4x4 terrainToWorld = Transform.LocalToWorldMatrix;
        Float4x4 worldToTerrain = Transform.WorldToLocalMatrix;

        // Camera position in terrain-local space for LOD
        Float3 camLocal = WorldToTerrain(camera.Transform.Position);
        camLocal.Y = 0;

        if (_quadtree == null
            || MathF.Abs(_quadtree.ChunkSize - terrainSize) > 0.01f
            || _quadtree.MaxLODLevel != MaxLODLevel)
        {
            _quadtree = new TerrainQuadtree(Float3.Zero, terrainSize, MaxLODLevel);
        }

        _quadtree.Update(camLocal, LODQuality);
        UpdateInstanceData(terrainToWorld);

        var mat = GetMaterialInstance();
        if (mat == null || _transforms.Length == 0 || _baseMesh == null) return;

        // Enable 8-layer keyword when more than 4 layers are active
        mat.SetKeyword("TERRAIN_8_LAYERS", terrainData.LayerCount > 4);
        mat.SetKeyword("TERRAIN_BICUBIC", terrainData.Interpolation == TerrainInterpolation.Bicubic);

        _properties.Clear();
        _properties.SetInt("_ObjectID", InstanceID);

        // GPU textures from TerrainData
        var heightmapTex = terrainData.GetHeightmapTexture();
        var splatmapTextures = terrainData.GetSplatmapTextures();

        var holesTex = terrainData.GetHolesTexture();

        if (heightmapTex != null) _properties.SetTexture("_Heightmap", heightmapTex);
        for (int si = 0; si < splatmapTextures.Count; si++)
            _properties.SetTexture($"_Splatmap{si}", splatmapTextures[si]);
        if (holesTex != null) _properties.SetTexture("_HolesMap", holesTex);
        _properties.SetInt("_HasHoles", holesTex != null ? 1 : 0);
        _properties.SetInt("_LayerCount", terrainData.LayerCount);

        // Per-layer textures and settings
        for (int i = 0; i < terrainData.LayerCount; i++)
        {
            var layer = terrainData.Layers[i];
            string prefix = $"_Layer{i}";

            s_defaultWhite ??= Texture2D.LoadDefault(DefaultTexture.White);
            s_defaultNormal ??= Texture2D.LoadDefault(DefaultTexture.Normal);
            _properties.SetTexture(prefix, layer.Albedo.Res ?? s_defaultWhite);
            _properties.SetTexture(prefix + "Normal", layer.NormalMap.Res ?? s_defaultNormal);

            _properties.SetFloat(prefix + "Tiling", layer.Tiling);
            _properties.SetFloat(prefix + "Roughness", layer.Roughness);
            _properties.SetFloat(prefix + "Metallic", layer.Metallic);
        }

        _properties.SetFloat("_TerrainSize", terrainSize);
        _properties.SetFloat("_TerrainHeight", terrainData.Height);
        _properties.SetVector("_TerrainOffset", this.Transform.Position);
        _properties.SetMatrix("_TerrainWorldToLocal", worldToTerrain);
        _properties.SetMatrix("_TerrainLocalToWorld", terrainToWorld);

        // Brush preview
        mat.SetVector("_BrushPosition", BrushPosition);
        mat.SetFloat("_BrushRadius", BrushRadius);
        mat.SetFloat("_BrushFalloff", BrushFalloff);
        mat.SetFloat("_BrushVisible", BrushVisible ? 1f : 0f);

        // World-space bounds (transform terrain AABB corners to world)
        float height = terrainData.Height;
        Float3 localMin = new(0, -height * 2f, 0);
        Float3 localMax = new(terrainSize, height * 2f, terrainSize);
        var bounds = TransformAABB(localMin, localMax, terrainToWorld);

        InstancedMeshRenderable.CreateBatched(
            renderables, _baseMesh, mat, _transforms,
            (bounds.Min + bounds.Max) * 0.5f,
            layer: GameObject.LayerIndex,
            properties: _properties, bounds: bounds);

        // Grass
        var grassMat = GetGrassMaterialInstance();
        if (grassMat != null && terrainData.DetailPrototypes.Count > 0)
        {
            // Pass terrain transform info to grass shader
            Float3 terrainUp = Float3.Normalize(Float4x4.TransformPoint(Float3.UnitY, terrainToWorld) - Float4x4.TransformPoint(Float3.Zero, terrainToWorld));
            grassMat.SetVector("_TerrainUp", terrainUp);
            grassMat.SetMatrix("_TerrainWorldToLocal", worldToTerrain);
            grassMat.SetMatrix("_TerrainLocalToWorld", terrainToWorld);
            grassMat.SetFloat("_TerrainSize", terrainSize);
            grassMat.SetFloat("_TerrainHeight", terrainData.Height);
            // Distance fade: blades scale from 1 at FadeStart*Distance down to 0 at Distance,
            // smoothing the per-patch cutoff in TerrainGrassRenderer.
            float fadeStartWorld = GrassDistance * Math.Clamp(GrassFadeStart, 0f, 0.99f);
            grassMat.SetFloat("_GrassDistance", GrassDistance);
            grassMat.SetFloat("_GrassFadeStart", fadeStartWorld);
            var htex = terrainData.GetHeightmapTexture();
            if (htex != null) grassMat.SetTexture("_Heightmap", htex);

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
        {
            s_defaultTerrainMat ??= Resources.Material.LoadDefault(DefaultMaterial.Terrain);
            sourceMat = s_defaultTerrainMat;
        }
        if (sourceMat == null) return null;

        // Re-clone from source each frame so edits to the material asset
        // are reflected immediately without requiring reimport or restart.
        _materialInstance = sourceMat.Clone();
        return _materialInstance;
    }

    private Material? GetGrassMaterialInstance()
    {
        var sourceMat = GrassMaterial.Res;
        if (sourceMat == null)
        {
            s_defaultGrassMat ??= Resources.Material.LoadDefault(DefaultMaterial.Grass);
            sourceMat = s_defaultGrassMat;
        }
        if (sourceMat == null) return null;

        // Re-clone from source each frame so edits to the material asset
        // are reflected immediately.
        _grassMaterialInstance = sourceMat.Clone();
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

        float size = terrainData.Size;
        Float4x4 worldToLocal = Transform.WorldToLocalMatrix;

        // Transform ray into terrain-local space
        Float3 localOrigin = Float4x4.TransformPoint(ray.Origin, worldToLocal);
        Float3 localDir = Float3.Normalize(Float4x4.TransformPoint(ray.Origin + ray.Direction, worldToLocal) - localOrigin);

        float stepSize = size / terrainData.HeightmapResolution * 0.5f;
        float maxDist = size * 2f;
        float t = 0f;

        float prevDiff = GetHeightDiffLocal(localOrigin, terrainData);
        bool prevAbove = prevDiff > 0;

        while (t < maxDist)
        {
            t += stepSize;
            Float3 localPoint = localOrigin + localDir * t;
            float diff = GetHeightDiffLocal(localPoint, terrainData);
            bool above = diff > 0;

            if (!above && prevAbove)
            {
                float tLo = t - stepSize;
                float tHi = t;
                for (int i = 0; i < 8; i++)
                {
                    float tMid = (tLo + tHi) * 0.5f;
                    Float3 mid = localOrigin + localDir * tMid;
                    if (GetHeightDiffLocal(mid, terrainData) > 0)
                        tLo = tMid;
                    else
                        tHi = tMid;
                }

                Float3 localHit = localOrigin + localDir * ((tLo + tHi) * 0.5f);
                terrainUV = new Float2((float)(localHit.X / size), (float)(localHit.Z / size));
                hitPoint = TerrainToWorld(localHit);
                return true;
            }

            prevDiff = diff;
            prevAbove = above;
        }

        return false;
    }

    private static float GetHeightDiffLocal(Float3 localPoint, TerrainData data)
    {
        float u = (float)(localPoint.X / data.Size);
        float v = (float)(localPoint.Z / data.Size);
        if (u < 0 || u > 1 || v < 0 || v > 1) return 1f;
        float terrainY = data.GetInterpolatedHeight(u, v);
        return (float)localPoint.Y - terrainY;
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

    private void UpdateInstanceData(Float4x4 terrainToWorld)
    {
        var visibleChunks = _quadtree.GetVisibleChunks();

        if (_transforms.Length != visibleChunks.Count)
            _transforms = new Float4x4[visibleChunks.Count];

        for (int i = 0; i < visibleChunks.Count; i++)
        {
            var chunk = visibleChunks[i];
            // Chunk position is in terrain-local space; transform to world via terrain matrix
            Float4x4 localChunk = Float4x4.CreateTranslation(chunk.Position) * Float4x4.CreateScale((float)chunk.Size, 1.0f, (float)chunk.Size);
            _transforms[i] = terrainToWorld * localChunk;
        }
    }

    private static AABB TransformAABB(Float3 localMin, Float3 localMax, Float4x4 matrix)
    {
        // Transform all 8 corners and find new AABB
        Float3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Float3 corner = new(
                (i & 1) == 0 ? localMin.X : localMax.X,
                (i & 2) == 0 ? localMin.Y : localMax.Y,
                (i & 4) == 0 ? localMin.Z : localMax.Z);
            Float3 world = Float4x4.TransformPoint(corner, matrix);
            min = new Float3(MathF.Min(min.X, world.X), MathF.Min(min.Y, world.Y), MathF.Min(min.Z, world.Z));
            max = new Float3(MathF.Max(max.X, world.X), MathF.Max(max.Y, world.Y), MathF.Max(max.Z, world.Z));
        }
        return new AABB(min, max);
    }

    #endregion
}
