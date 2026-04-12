// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Grass rendering architecture based on Unity's DetailDatabase/DetailRenderer pattern:
// - Terrain divided into patches (default 8x8 detail cells per patch)
// - Each patch generates a cached mesh (instance data) on demand
// - Density value (0-1) maps to instance count per cell (up to ~16)
// - Dithered LOD using ordered 8x8 Bayer matrix for smooth transitions
// - Deterministic seeded random per cell for stable positions frame-to-frame
// - Patch meshes cached and only regenerated when density map changes

using System;
using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Generates and caches GPU-instanced grass billboard quads from the terrain's grass density map.
/// Uses a patch-based system with per-patch mesh caching for performance.
/// Owned by TerrainComponent, not a standalone component.
/// </summary>
internal class TerrainGrassRenderer
{
    // How many detail cells per patch edge (Unity default: 8)
    private const int CellsPerPatch = 8;

    // Max instances per density cell at full density (density 1.0 = this many blades)
    private const int MaxInstancesPerCell = 16;

    // Vertex budget per patch to prevent frame drops
    private const int MaxVerticesPerPatch = 50000;
    private const int VerticesPerBlade = 4;

    private Mesh? _quadMesh;

    // Cached patch data: key = patchX + patchY * patchCountX
    private readonly Dictionary<int, CachedPatch> _patchCache = [];
    private int _cacheGeneration;
    private bool _cacheInvalid = true;

    // 8x8 ordered dither table (Bayer matrix) for smooth LOD transitions
    private static readonly float[] s_ditherTable =
    [
        0/64f,  32/64f,  8/64f, 40/64f,  2/64f, 34/64f, 10/64f, 42/64f,
        48/64f, 16/64f, 56/64f, 24/64f, 50/64f, 18/64f, 58/64f, 26/64f,
        12/64f, 44/64f,  4/64f, 36/64f, 14/64f, 46/64f,  6/64f, 38/64f,
        60/64f, 28/64f, 52/64f, 20/64f, 62/64f, 30/64f, 54/64f, 22/64f,
        3/64f,  35/64f, 11/64f, 43/64f,  1/64f, 33/64f,  9/64f, 41/64f,
        51/64f, 19/64f, 59/64f, 27/64f, 49/64f, 17/64f, 57/64f, 25/64f,
        15/64f, 47/64f,  7/64f, 39/64f, 13/64f, 45/64f,  5/64f, 37/64f,
        63/64f, 31/64f, 55/64f, 23/64f, 61/64f, 29/64f, 53/64f, 21/64f,
    ];

    private struct CachedPatch
    {
        public Float4x4[] Transforms;
        public Float4[] Colors;
        public Float4[] CustomData;
        public int LastUsedGeneration;
        public AABB Bounds;
    }

    // Simple seeded RNG (xorshift32) for deterministic per-cell variation
    private struct SeededRandom
    {
        private uint _state;
        public SeededRandom(uint seed) => _state = seed == 0 ? 1 : seed;
        public float NextFloat()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state & 0xFFFF) / 65535f;
        }
    }

    public void Initialize()
    {
        _quadMesh = CreateQuadMesh();
    }

    public void Dispose()
    {
        _quadMesh?.Dispose();
        _quadMesh = null;
        _patchCache.Clear();
    }

    /// <summary>Invalidate all cached patches (call when density or height map changes).</summary>
    public void InvalidateCache()
    {
        _patchCache.Clear();
        _cacheInvalid = true;
    }

    public void CollectRenderables(
        TerrainData data,
        TerrainComponent terrain,
        Camera camera,
        Material grassMaterial,
        float maxDistance,
        float densityMultiplier,
        List<IRenderable> renderables)
    {
        if (_quadMesh == null || data.GrassDensity == null || data.GrassTypes.Length == 0)
            return;

        Float3 terrainPos = terrain.Transform.Position;
        Float3 cameraPos = camera.Transform.Position;
        float terrainSize = data.Size;
        int grassRes = data.GrassmapResolution;

        // How many patches across the terrain
        int patchCount = Math.Max(1, grassRes / CellsPerPatch);
        float patchWorldSize = terrainSize / patchCount;
        float maxDistSq = maxDistance * maxDistance;

        _cacheGeneration++;
        _cacheInvalid = false;

        // Camera position in patch coordinates
        float camPatchX = ((float)cameraPos.X - (float)terrainPos.X) / patchWorldSize;
        float camPatchZ = ((float)cameraPos.Z - (float)terrainPos.Z) / patchWorldSize;
        int halfRange = (int)MathF.Ceiling(maxDistance / patchWorldSize) + 1;

        int minPX = Math.Max(0, (int)camPatchX - halfRange);
        int maxPX = Math.Min(patchCount - 1, (int)camPatchX + halfRange);
        int minPZ = Math.Max(0, (int)camPatchZ - halfRange);
        int maxPZ = Math.Min(patchCount - 1, (int)camPatchZ + halfRange);

        for (int pz = minPZ; pz <= maxPZ; pz++)
        {
            for (int px = minPX; px <= maxPX; px++)
            {
                // Patch center in world space
                float patchCenterX = (float)terrainPos.X + (px + 0.5f) * patchWorldSize;
                float patchCenterZ = (float)terrainPos.Z + (pz + 0.5f) * patchWorldSize;

                float dx = patchCenterX - (float)cameraPos.X;
                float dz = patchCenterZ - (float)cameraPos.Z;
                float distSq = dx * dx + dz * dz;

                if (distSq > maxDistSq) continue;

                // Get or build cached patch
                int patchKey = px + pz * patchCount;
                if (!_patchCache.TryGetValue(patchKey, out var cached))
                {
                    cached = BuildPatch(data, terrainPos, terrainSize, grassRes, patchCount, px, pz, densityMultiplier);
                }

                cached.LastUsedGeneration = _cacheGeneration;
                _patchCache[patchKey] = cached;

                if (cached.Transforms.Length == 0) continue;

                InstancedMeshRenderable.CreateBatched(
                    renderables,
                    _quadMesh,
                    grassMaterial,
                    cached.Transforms,
                    new Float3(patchCenterX, (float)terrainPos.Y, patchCenterZ),
                    cached.Colors,
                    cached.CustomData,
                    layer: terrain.GameObject.LayerIndex,
                    bounds: cached.Bounds
                );
            }
        }

        // Evict stale patches (not used for 2+ generations)
        List<int>? evict = null;
        foreach (var (key, patch) in _patchCache)
        {
            if (_cacheGeneration - patch.LastUsedGeneration > 2)
            {
                evict ??= [];
                evict.Add(key);
            }
        }
        if (evict != null)
            foreach (int key in evict)
                _patchCache.Remove(key);
    }

    private CachedPatch BuildPatch(
        TerrainData data, Float3 terrainPos, float terrainSize,
        int grassRes, int patchCount, int patchX, int patchZ, float densityMultiplier)
    {
        var grassType = data.GrassTypes[0];

        var transforms = new List<Float4x4>();
        var colors = new List<Float4>();
        var customData = new List<Float4>();

        float cellWorldSize = terrainSize / grassRes;
        int cellStartX = patchX * CellsPerPatch;
        int cellStartZ = patchZ * CellsPerPatch;
        int cellEndX = Math.Min(cellStartX + CellsPerPatch, grassRes);
        int cellEndZ = Math.Min(cellStartZ + CellsPerPatch, grassRes);

        float patchMinY = float.MaxValue;
        float patchMaxY = float.MinValue;
        int vertexCount = 0;

        for (int cz = cellStartZ; cz < cellEndZ; cz++)
        {
            for (int cx = cellStartX; cx < cellEndX; cx++)
            {
                float rawDensity = data.GrassDensity[cz * grassRes + cx] * densityMultiplier;
                if (rawDensity < 0.01f) continue;

                // Dithered instance count (Unity-style: dither contributes ±0.5/64 for smooth transitions)
                int localX = cx - cellStartX;
                int localZ = cz - cellStartZ;
                float dither = s_ditherTable[(localX & 7) + (localZ & 7) * 8];
                float rawCount = rawDensity * MaxInstancesPerCell;
                int instanceCount = (int)(rawCount + (dither - 0.5f) * (1f / 64f) * MaxInstancesPerCell);
                instanceCount = Math.Clamp(instanceCount, 0, MaxInstancesPerCell);

                // Vertex budget
                int remaining = (MaxVerticesPerPatch - vertexCount) / VerticesPerBlade;
                instanceCount = Math.Min(instanceCount, remaining);
                if (instanceCount <= 0) continue;

                // Normalized terrain position for this cell
                float cellU = cx / (float)(grassRes - 1);
                float cellV = cz / (float)(grassRes - 1);

                // Seed RNG from spatial position (deterministic)
                uint seed = (uint)(cx * 73856093 ^ cz * 19349663);
                var rng = new SeededRandom(seed);

                for (int k = 0; k < instanceCount; k++)
                {
                    // Random position within this cell
                    float offsetU = rng.NextFloat() / (grassRes - 1);
                    float offsetV = rng.NextFloat() / (grassRes - 1);
                    float u = cellU + offsetU;
                    float v = cellV + offsetV;
                    if (u > 1f || v > 1f) continue;

                    float worldX = (float)terrainPos.X + u * terrainSize;
                    float worldZ = (float)terrainPos.Z + v * terrainSize;
                    float worldY = (float)terrainPos.Y + data.GetInterpolatedHeight(u, v);

                    patchMinY = MathF.Min(patchMinY, worldY);
                    patchMaxY = MathF.Max(patchMaxY, worldY);

                    // Noise for size/color variation (Unity uses Perlin, we use a cheap hash-based noise)
                    float noiseSpread = grassType.NoiseSpread;
                    float noise = NoiseAt(worldX * noiseSpread, worldZ * noiseSpread);

                    // Size variation (Unity: lerp between min/max based on noise)
                    float scaleW = grassType.MinWidth + noise * (grassType.MaxWidth - grassType.MinWidth);
                    float scaleH = grassType.MinHeight + noise * (grassType.MaxHeight - grassType.MinHeight);

                    // Build transform: scale in columns, position in translation
                    var transform = Float4x4.CreateScale(scaleW, scaleH, 1f);
                    transform[0, 3] = worldX;
                    transform[1, 3] = worldY;
                    transform[2, 3] = worldZ;
                    transforms.Add(transform);

                    // Color: lerp between healthy and dry based on noise
                    Color c = Color.Lerp(grassType.Tint, grassType.DryTint, 1f - noise);
                    colors.Add(new Float4(c.R, c.G, c.B, c.A));

                    // Custom data: wind phase offset + bend factor
                    float windPhase = rng.NextFloat() * MathF.PI * 2f;
                    customData.Add(new Float4(windPhase, grassType.BendFactor, 0, 0));

                    vertexCount += VerticesPerBlade;
                }
            }
        }

        // Compute patch bounds
        float patchWorldSize = terrainSize / patchCount;
        float maxGrassHeight = grassType.MaxHeight;
        float maxGrassWidth = grassType.MaxWidth * 0.5f;

        Float3 boundsMin = new(
            (float)terrainPos.X + patchX * patchWorldSize - maxGrassWidth,
            patchMinY == float.MaxValue ? (float)terrainPos.Y : patchMinY,
            (float)terrainPos.Z + patchZ * patchWorldSize - maxGrassWidth);
        Float3 boundsMax = new(
            (float)terrainPos.X + (patchX + 1) * patchWorldSize + maxGrassWidth,
            (patchMaxY == float.MinValue ? (float)terrainPos.Y : patchMaxY) + maxGrassHeight,
            (float)terrainPos.Z + (patchZ + 1) * patchWorldSize + maxGrassWidth);

        return new CachedPatch
        {
            Transforms = [.. transforms],
            Colors = [.. colors],
            CustomData = [.. customData],
            Bounds = new AABB(boundsMin, boundsMax),
            LastUsedGeneration = _cacheGeneration,
        };
    }

    /// <summary>Cheap 2D value noise in [0..1] range (not Perlin, but good enough for grass variation).</summary>
    private static float NoiseAt(float x, float z)
    {
        // Hash-based value noise with bilinear interpolation
        int ix = (int)MathF.Floor(x);
        int iz = (int)MathF.Floor(z);
        float fx = x - ix;
        float fz = z - iz;

        float n00 = HashNorm(ix, iz);
        float n10 = HashNorm(ix + 1, iz);
        float n01 = HashNorm(ix, iz + 1);
        float n11 = HashNorm(ix + 1, iz + 1);

        // Smoothstep interpolation
        float sx = fx * fx * (3f - 2f * fx);
        float sz = fz * fz * (3f - 2f * fz);

        float n0 = n00 + (n10 - n00) * sx;
        float n1 = n01 + (n11 - n01) * sx;
        return n0 + (n1 - n0) * sz;
    }

    private static float HashNorm(int x, int z)
    {
        uint h = (uint)(x * 73856093 ^ z * 19349663);
        h ^= h >> 16;
        h *= 0x45d9f3b;
        h ^= h >> 16;
        return (h & 0xFFFF) / 65535f;
    }

    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh();
        mesh.Vertices =
        [
            new Float3(-0.5f, 0f, 0f),
            new Float3( 0.5f, 0f, 0f),
            new Float3( 0.5f, 1f, 0f),
            new Float3(-0.5f, 1f, 0f),
        ];
        mesh.UV =
        [
            new Float2(0, 0),
            new Float2(1, 0),
            new Float2(1, 1),
            new Float2(0, 1),
        ];
        mesh.Normals =
        [
            Float3.UnitY, Float3.UnitY, Float3.UnitY, Float3.UnitY,
        ];
        mesh.Indices = [0, 2, 1, 0, 3, 2];
        mesh.RecalculateBounds();
        mesh.Upload();
        return mesh;
    }
}
