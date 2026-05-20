// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Patch-based detail renderer with per-prototype density layers.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Terrain;

internal class TerrainGrassRenderer
{
    private const int CellsPerPatch = 8;
    private const int MaxInstancesPerCell = 16;
    private const int MaxVerticesPerPatch = 50000;
    private const int VerticesPerBlade = 4;

    private Mesh? _quadMesh;
    private static Material? s_defaultStandardMat;
    private static readonly PropertyState s_emptyProps = new();
    private static Texture2D? s_defaultWhite;

    // Cache key = protoIndex * maxPatches + patchX + patchZ * patchCountX
    private readonly Dictionary<long, CachedPatch> _patchCache = [];
    private int _cacheGeneration;

    private static readonly float[] s_ditherTable =
    [
        0/64f, 32/64f, 8/64f, 40/64f, 2/64f, 34/64f, 10/64f, 42/64f,
        48/64f, 16/64f, 56/64f, 24/64f, 50/64f, 18/64f, 58/64f, 26/64f,
        12/64f, 44/64f, 4/64f, 36/64f, 14/64f, 46/64f, 6/64f, 38/64f,
        60/64f, 28/64f, 52/64f, 20/64f, 62/64f, 30/64f, 54/64f, 22/64f,
        3/64f, 35/64f, 11/64f, 43/64f, 1/64f, 33/64f, 9/64f, 41/64f,
        51/64f, 19/64f, 59/64f, 27/64f, 49/64f, 17/64f, 57/64f, 25/64f,
        15/64f, 47/64f, 7/64f, 39/64f, 13/64f, 45/64f, 5/64f, 37/64f,
        63/64f, 31/64f, 55/64f, 23/64f, 61/64f, 29/64f, 53/64f, 21/64f,
    ];

    private struct CachedPatch
    {
        public Float4x4[] Transforms;
        public Float4[] Colors;
        public Float4[] CustomData;
        public Rendering.InstanceData[] PrebuiltInstances; // cached to avoid per-frame allocation
        public int LastUsedGeneration;
        public AABB Bounds;
    }

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

    public void Initialize() => _quadMesh = CreateQuadMesh();

    public void Dispose()
    {
        _quadMesh?.Dispose();
        _quadMesh = null;
        _patchCache.Clear();
    }

    public void InvalidateCache() => _patchCache.Clear();

    public void CollectRenderables(
        TerrainData data, TerrainComponent terrain, Camera camera,
        Material baseMaterial, float maxDistance, float densityMultiplier,
        List<IRenderable> renderables)
    {
        if (_quadMesh == null || data.DetailPrototypes.Count == 0) return;

        float terrainSize = data.Size;
        int detailRes = data.DetailResolution;
        int patchCount = Math.Max(1, detailRes / CellsPerPatch);
        float patchLocalSize = terrainSize / patchCount;

        // Convert maxDistance from world to terrain-local units (approximate via terrain scale)
        Float3 terrainScale = terrain.Transform.LocalScale;
        float avgScale = MathF.Max(0.001f, ((float)terrainScale.X + (float)terrainScale.Z) * 0.5f);
        float maxDistLocal = maxDistance / avgScale;
        float maxDistLocalSq = maxDistLocal * maxDistLocal;

        _cacheGeneration++;

        // Camera in terrain-local space for distance/patch calculations (project onto XZ plane)
        Float3 camLocal = terrain.WorldToTerrain(camera.Transform.Position);
        camLocal.Y = 0; // Project to terrain XZ plane for 2D patch distance
        float camPX = (float)camLocal.X / patchLocalSize;
        float camPZ = (float)camLocal.Z / patchLocalSize;
        int halfRange = (int)MathF.Ceiling(maxDistLocal / patchLocalSize) + 1;

        int minPX = Math.Max(0, (int)camPX - halfRange);
        int maxPX = Math.Min(patchCount - 1, (int)camPX + halfRange);
        int minPZ = Math.Max(0, (int)camPZ - halfRange);
        int maxPZ = Math.Min(patchCount - 1, (int)camPZ + halfRange);

        // Iterate each detail prototype
        for (int protoIdx = 0; protoIdx < data.DetailPrototypes.Count; protoIdx++)
        {
            var proto = data.DetailPrototypes[protoIdx];
            if (protoIdx >= data.DetailLayers.Count) continue;

            // Determine mesh and (for texture modes) single material based on render mode.
            // Mesh mode picks per-submesh materials below.
            Mesh renderMesh;
            Material renderMat;
            int subMeshCount;
            if (proto.RenderMode == DetailRenderMode.Mesh)
            {
                var meshRes = proto.Mesh.Res;
                if (meshRes == null) continue;
                renderMesh = meshRes;
                subMeshCount = meshRes.SubMeshCount;
                s_defaultStandardMat ??= Resources.Material.LoadDefault(DefaultMaterial.Standard);
                renderMat = s_defaultStandardMat; // unused in mesh mode per-submesh lookup handles it
            }
            else
            {
                renderMesh = _quadMesh!;
                subMeshCount = 1;

                // Per-prototype grass material override, falls back to the terrain's global grass material.
                // baseMaterial already has terrain uniforms set by TerrainComponent.
                // Per-prototype overrides need those same uniforms applied.
                var protoMat = proto.GrassMaterial.Res;
                Material grassMat;
                if (protoMat != null)
                {
                    grassMat = protoMat.Clone();
                    // Apply the same terrain uniforms that TerrainComponent sets on the base material
                    Float4x4 tw = terrain.Transform.LocalToWorldMatrix;
                    Float3 tUp = Float3.Normalize(Float4x4.TransformPoint(Float3.UnitY, tw) - Float4x4.TransformPoint(Float3.Zero, tw));
                    grassMat.SetVector("_TerrainUp", tUp);
                    grassMat.SetMatrix("_TerrainWorldToLocal", terrain.Transform.WorldToLocalMatrix);
                    grassMat.SetMatrix("_TerrainLocalToWorld", tw);
                    grassMat.SetFloat("_TerrainSize", data.Size);
                    grassMat.SetFloat("_TerrainHeight", data.Height);
                    grassMat.SetFloat("_GrassDistance", terrain.GrassDistance);
                    float fadeStartWorld = terrain.GrassDistance * Math.Clamp(terrain.GrassFadeStart, 0f, 0.99f);
                    grassMat.SetFloat("_GrassFadeStart", fadeStartWorld);
                    var htex = data.GetHeightmapTexture();
                    if (htex != null) grassMat.SetTexture("_Heightmap", htex);
                }
                else
                {
                    grassMat = baseMaterial;
                }

                s_defaultWhite ??= Texture2D.LoadDefault(DefaultTexture.White);
                var tex = proto.Texture.Res ?? s_defaultWhite;
                grassMat.SetTexture("_MainTex", tex);
                // Pass billboard flag to shader via uniform
                grassMat.SetFloat("_Billboard", proto.RenderMode == DetailRenderMode.TextureBillboard ? 1f : 0f);
                grassMat.SetFloat("_AlignToNormal", proto.AlignToNormal ? 1f : 0f);
                renderMat = grassMat;
            }

            for (int pz = minPZ; pz <= maxPZ; pz++)
            {
                for (int px = minPX; px <= maxPX; px++)
                {
                    // Patch center in terrain-local space
                    float localPcx = (px + 0.5f) * patchLocalSize;
                    float localPcz = (pz + 0.5f) * patchLocalSize;
                    float dx = localPcx - (float)camLocal.X;
                    float dz = localPcz - (float)camLocal.Z;
                    if (dx * dx + dz * dz > maxDistLocalSq) continue;

                    // Sort position in world space for depth ordering
                    Float3 worldPatchCenter = terrain.TerrainToWorld(new Float3(localPcx, 0, localPcz));

                    long patchKey = (long)protoIdx * patchCount * patchCount + px + pz * patchCount;
                    if (!_patchCache.TryGetValue(patchKey, out var cached))
                        cached = BuildPatch(data, terrain, terrainSize, detailRes, patchCount, px, pz, protoIdx, proto, densityMultiplier);

                    cached.LastUsedGeneration = _cacheGeneration;
                    _patchCache[patchKey] = cached;

                    if (cached.PrebuiltInstances == null || cached.PrebuiltInstances.Length == 0) continue;

                    // One instanced draw per submesh so multi-material detail meshes render
                    // correctly. Texture modes use a fabricated single-submesh quad.
                    for (int sub = 0; sub < subMeshCount; sub++)
                    {
                        Material subMat;
                        if (proto.RenderMode == DetailRenderMode.Mesh)
                        {
                            subMat = null!;
                            if (sub < proto.Materials.Count) subMat = proto.Materials[sub].Res!;
                            subMat ??= renderMat; // default Standard
                        }
                        else
                        {
                            subMat = renderMat;
                        }

                        renderables.Add(new InstancedMeshRenderable(
                            renderMesh, subMat, cached.PrebuiltInstances,
                            worldPatchCenter,
                            terrain.GameObject.LayerIndex,
                            s_emptyProps,
                            cached.Bounds,
                            subMeshIndex: subMeshCount > 1 ? sub : -1));
                    }
                }
            }
        }

        // Evict stale patches
        List<long>? evict = null;
        foreach (var (key, patch) in _patchCache)
            if (_cacheGeneration - patch.LastUsedGeneration > 2)
                (evict ??= []).Add(key);
        if (evict != null) foreach (long k in evict) _patchCache.Remove(k);
    }

    private CachedPatch BuildPatch(
        TerrainData data, TerrainComponent terrain, float terrainSize,
        int detailRes, int patchCount, int patchX, int patchZ,
        int protoIdx, DetailPrototype proto, float densityMultiplier)
    {
        var densityMap = data.DetailLayers[protoIdx];
        if (densityMap == null) return new CachedPatch { Transforms = [], Colors = [], CustomData = [], PrebuiltInstances = [], Bounds = default };

        var transforms = new List<Float4x4>();
        var colors = new List<Float4>();
        var customData = new List<Float4>();

        int cellStartX = patchX * CellsPerPatch;
        int cellStartZ = patchZ * CellsPerPatch;
        int cellEndX = Math.Min(cellStartX + CellsPerPatch, detailRes);
        int cellEndZ = Math.Min(cellStartZ + CellsPerPatch, detailRes);

        float patchMinY = float.MaxValue, patchMaxY = float.MinValue;
        int vertexCount = 0;

        for (int cz = cellStartZ; cz < cellEndZ; cz++)
        {
            for (int cx = cellStartX; cx < cellEndX; cx++)
            {
                float rawDensity = densityMap[cz * detailRes + cx] * densityMultiplier;
                if (rawDensity < 0.01f) continue;

                int lx = cx - cellStartX, lz = cz - cellStartZ;
                float dither = s_ditherTable[(lx & 7) + (lz & 7) * 8];
                int count = Math.Clamp((int)(rawDensity * MaxInstancesPerCell + (dither - 0.5f) * (1f / 64f) * MaxInstancesPerCell), 0, MaxInstancesPerCell);
                count = Math.Min(count, (MaxVerticesPerPatch - vertexCount) / VerticesPerBlade);
                if (count <= 0) continue;

                float cellU = cx / (float)(detailRes - 1);
                float cellV = cz / (float)(detailRes - 1);
                var rng = new SeededRandom((uint)(cx * 73856093 ^ cz * 19349663 ^ protoIdx * 83492791));

                for (int k = 0; k < count; k++)
                {
                    float u = cellU + rng.NextFloat() / (detailRes - 1);
                    float v = cellV + rng.NextFloat() / (detailRes - 1);
                    if (u > 1f || v > 1f) continue;

                    // Position in terrain-local space (shader handles terrain transform)
                    float wx = u * terrainSize;
                    float wz = v * terrainSize;
                    float wy = data.GetInterpolatedHeight(u, v);
                    patchMinY = MathF.Min(patchMinY, wy);
                    patchMaxY = MathF.Max(patchMaxY, wy);

                    float noise = NoiseAt(wx * proto.NoiseSpread, wz * proto.NoiseSpread);
                    // Density influences size low density areas get shorter/narrower grass
                    float densityScale = MathF.Min(1f, rawDensity * 2f); // 0-0.5 density ramps size, 0.5+ is full
                    float sizeT = noise * densityScale;
                    float sw = proto.MinWidth + sizeT * (proto.MaxWidth - proto.MinWidth);
                    float sh = proto.MinHeight + sizeT * (proto.MaxHeight - proto.MinHeight);

                    Float4x4 t;
                    if (proto.RenderMode == DetailRenderMode.Mesh)
                    {
                        // Mesh mode: proper TRS with random Y rotation
                        float rotY = rng.NextFloat() * MathF.PI * 2f;
                        t = Float4x4.CreateTranslation(new Float3(wx, wy, wz))
                            * Float4x4.FromAxisAngle(new Float3(0, 1, 0), rotY)
                            * Float4x4.CreateScale(new Float3(sw, sh, sw));
                    }
                    else if (proto.RenderMode == DetailRenderMode.TextureNonBillboard)
                    {
                        // Non-billboard texture: random Y rotation, shader reads orientation from matrix
                        float rotY = rng.NextFloat() * MathF.PI * 2f;
                        t = Float4x4.CreateTranslation(new Float3(wx, wy, wz))
                            * Float4x4.FromAxisAngle(new Float3(0, 1, 0), rotY)
                            * Float4x4.CreateScale(sw, sh, 1f);
                    }
                    else
                    {
                        // Billboard mode: scale in columns, position in translation
                        t = Float4x4.CreateScale(sw, sh, 1f);
                        t[0, 3] = wx; t[1, 3] = wy; t[2, 3] = wz;
                    }
                    transforms.Add(t);

                    Color c = Color.Lerp(proto.HealthyColor, proto.DryColor, 1f - noise);
                    colors.Add(new Float4(c.R, c.G, c.B, c.A));
                    customData.Add(new Float4(rng.NextFloat() * MathF.PI * 2f, proto.BendFactor, 0, 0));
                    vertexCount += VerticesPerBlade;
                }
            }
        }

        // Bounds in terrain-local space, padded for grass size
        float mgh = proto.MaxHeight, mgw = proto.MaxWidth * 0.5f;
        float pws = terrainSize / patchCount;
        Float3 localMin = new(patchX * pws - mgw, patchMinY == float.MaxValue ? 0 : patchMinY, patchZ * pws - mgw);
        Float3 localMax = new((patchX + 1) * pws + mgw, (patchMaxY == float.MinValue ? 0 : patchMaxY) + mgh, (patchZ + 1) * pws + mgw);
        // Transform to world for frustum culling
        Float3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
        for (int ci = 0; ci < 8; ci++)
        {
            Float3 corner = new(
                (ci & 1) == 0 ? localMin.X : localMax.X,
                (ci & 2) == 0 ? localMin.Y : localMax.Y,
                (ci & 4) == 0 ? localMin.Z : localMax.Z);
            Float3 w = terrain.TerrainToWorld(corner);
            bmin = new Float3(MathF.Min(bmin.X, w.X), MathF.Min(bmin.Y, w.Y), MathF.Min(bmin.Z, w.Z));
            bmax = new Float3(MathF.Max(bmax.X, w.X), MathF.Max(bmax.Y, w.Y), MathF.Max(bmax.Z, w.Z));
        }

        // Prebuild InstanceData so we don't allocate per-frame
        var tArr = transforms.ToArray();
        var cArr = colors.ToArray();
        var dArr = customData.ToArray();
        var instances = new Rendering.InstanceData[tArr.Length];
        for (int i = 0; i < instances.Length; i++)
        {
            Float4 col = i < cArr.Length ? cArr[i] : new Float4(1, 1, 1, 1);
            Float4 cust = i < dArr.Length ? dArr[i] : Float4.Zero;
            instances[i] = new Rendering.InstanceData(tArr[i], col, cust);
        }

        return new CachedPatch
        {
            Transforms = tArr, Colors = cArr, CustomData = dArr,
            PrebuiltInstances = instances,
            Bounds = new AABB(bmin, bmax), LastUsedGeneration = _cacheGeneration,
        };
    }

    private static float NoiseAt(float x, float z)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = x - ix, fz = z - iz;
        float sx = fx * fx * (3f - 2f * fx), sz = fz * fz * (3f - 2f * fz);
        float n0 = HashN(ix, iz) + (HashN(ix + 1, iz) - HashN(ix, iz)) * sx;
        float n1 = HashN(ix, iz + 1) + (HashN(ix + 1, iz + 1) - HashN(ix, iz + 1)) * sx;
        return n0 + (n1 - n0) * sz;
    }

    private static float HashN(int x, int z)
    {
        uint h = (uint)(x * 73856093 ^ z * 19349663);
        h ^= h >> 16; h *= 0x45d9f3b; h ^= h >> 16;
        return (h & 0xFFFF) / 65535f;
    }


    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh();
        mesh.Vertices = [new(-0.5f, 0, 0), new(0.5f, 0, 0), new(0.5f, 1, 0), new(-0.5f, 1, 0)];
        mesh.UV = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        mesh.Normals = [Float3.UnitY, Float3.UnitY, Float3.UnitY, Float3.UnitY];
        mesh.Indices = [0, 2, 1, 0, 3, 2];
        mesh.RecalculateBounds();
        mesh.Upload();
        return mesh;
    }
}
