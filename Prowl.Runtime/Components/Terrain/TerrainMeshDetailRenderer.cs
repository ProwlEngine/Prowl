// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Camera-relative scatter for mesh detail prototypes.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Scatters mesh detail prototypes across the terrain, on the CPU.
///
/// Textured details are placed in the vertex shader by <see cref="TerrainDetailRenderer"/>, but a
/// mesh prototype renders with its own user-authored materials, which know nothing about procedural
/// placement. Those get instance buffers built here instead.
///
/// One buffer covers the whole draw distance around the camera rather than one per patch, so a
/// prototype costs a draw per submesh no matter how far it reaches. The buffer is built with a
/// margin of slack and only rebuilt once the camera has spent it, and placement is seeded from the
/// world cell index, so a rebuild puts every instance back exactly where it was.
/// </summary>
internal class TerrainMeshDetailRenderer
{
    private const int MaxInstancesPerCell = 16;

    /// <summary>Instances one prototype may place at once, so a solid paint cannot stall a frame.</summary>
    private const int MaxInstancesPerPrototype = 20000;

    /// <summary>
    /// Slack around the draw distance, as a fraction of it. The buffer reaches this much further
    /// than it needs to and is rebuilt once the camera has moved that far, which means anything
    /// inside the draw distance was already in the previous build: instances never pop in.
    /// </summary>
    private const float kRebuildMargin = 0.15f;

    private static Material? s_defaultStandardMat;
    private static readonly PropertyState s_emptyProps = new();

    private readonly Dictionary<int, Build> _builds = [];

    private sealed class Build
    {
        public InstanceData[] Instances = [];
        public IRenderable[] Renderables = [];
        public Float2 Centre;
        public float Distance;
        public int DetailsVersion = -1;
        public int HeightsVersion = -1;
    }

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

    public void Dispose() => _builds.Clear();

    public void InvalidateCache() => _builds.Clear();

    /// <summary>Whether a build still covers everything the camera can see from where it now stands.</summary>
    public static bool BuildIsStale(Float2 buildCentre, float buildDistance, Float2 camera, float distance)
        => buildDistance != distance || Float2.Distance(buildCentre, camera) > distance * kRebuildMargin;

    /// <summary>Radius a build reaches: the draw distance plus the slack it is allowed to spend.</summary>
    public static float BuildRadius(float distance) => distance * (1f + kRebuildMargin);

    public void CollectRenderables(
        TerrainData data, TerrainComponent terrain, Camera camera, List<IRenderable> renderables)
    {
        if (data.DetailPrototypes.Count == 0) return;

        Float3 camLocal = terrain.WorldToTerrain(camera.Transform.Position);
        Float3 terrainScale = terrain.Transform.LocalScale;
        float avgScale = MathF.Max(0.001f, (terrainScale.X + terrainScale.Z) * 0.5f);
        float distance = MathF.Max(terrain.DetailDistance, 0.01f) / avgScale;
        var centre = new Float2(camLocal.X, camLocal.Z);

        for (int protoIdx = 0; protoIdx < data.DetailPrototypes.Count; protoIdx++)
        {
            var proto = data.DetailPrototypes[protoIdx];
            if (proto.RenderMode != DetailRenderMode.Mesh) continue;
            if (protoIdx >= data.DetailLayers.Count) continue;

            var mesh = proto.Mesh.Res;
            if (mesh == null) continue;

            // Nothing painted, nothing to place
            if (!data.TryGetDetailBounds(protoIdx, out Float4 painted)) continue;

            if (!_builds.TryGetValue(protoIdx, out Build? build))
                _builds[protoIdx] = build = new Build();

            if (build.DetailsVersion != data.DetailsVersion
                || build.HeightsVersion != data.HeightsVersion
                || BuildIsStale(build.Centre, build.Distance, centre, distance))
            {
                Rebuild(build, data, terrain, proto, protoIdx, mesh, centre, distance, painted);
            }

            for (int i = 0; i < build.Renderables.Length; i++)
                renderables.Add(build.Renderables[i]);
        }
    }

    private static void Rebuild(Build build, TerrainData data, TerrainComponent terrain, DetailPrototype proto,
        int protoIdx, Mesh mesh, Float2 centre, float distance, Float4 painted)
    {
        var densityMap = data.DetailLayers[protoIdx];
        int detailRes = data.DetailResolution;
        float terrainSize = data.Size;
        float cellSize = terrainSize / detailRes;
        float radius = BuildRadius(distance);

        build.Centre = centre;
        build.Distance = distance;
        build.DetailsVersion = data.DetailsVersion;
        build.HeightsVersion = data.HeightsVersion;

        var instances = new List<InstanceData>();
        float minY = float.MaxValue, maxY = float.MinValue;

        // Only the cells that are both in range and actually painted
        int startX = Math.Max(0, (int)MathF.Floor(MathF.Max(centre.X - radius, painted.X) / cellSize));
        int startZ = Math.Max(0, (int)MathF.Floor(MathF.Max(centre.Y - radius, painted.Y) / cellSize));
        int endX = Math.Min(detailRes - 1, (int)MathF.Ceiling(MathF.Min(centre.X + radius, painted.Z) / cellSize));
        int endZ = Math.Min(detailRes - 1, (int)MathF.Ceiling(MathF.Min(centre.Y + radius, painted.W) / cellSize));

        float radiusSq = radius * radius;

        for (int cz = startZ; cz <= endZ && instances.Count < MaxInstancesPerPrototype; cz++)
        {
            for (int cx = startX; cx <= endX; cx++)
            {
                float rawDensity = densityMap[cz * detailRes + cx] * (1f / 255f);
                if (rawDensity < 0.01f) continue;

                float cellCentreX = (cx + 0.5f) * cellSize;
                float cellCentreZ = (cz + 0.5f) * cellSize;
                float dx = cellCentreX - centre.X, dz = cellCentreZ - centre.Y;
                if (dx * dx + dz * dz > radiusSq) continue;

                float dither = s_ditherTable[(cx & 7) + (cz & 7) * 8];
                int count = Math.Clamp((int)(rawDensity * MaxInstancesPerCell + (dither - 0.5f) * (1f / 64f) * MaxInstancesPerCell), 0, MaxInstancesPerCell);
                count = Math.Min(count, MaxInstancesPerPrototype - instances.Count);
                if (count <= 0) break;

                // Seeded from the world cell, so a rebuild reproduces the same scatter exactly
                var rng = new SeededRandom((uint)(cx * 73856093 ^ cz * 19349663 ^ protoIdx * 83492791));

                for (int k = 0; k < count; k++)
                {
                    float u = (cx + rng.NextFloat()) / detailRes;
                    float v = (cz + rng.NextFloat()) / detailRes;

                    float wx = u * terrainSize;
                    float wz = v * terrainSize;
                    float wy = data.GetInterpolatedHeight(u, v);
                    minY = MathF.Min(minY, wy);
                    maxY = MathF.Max(maxY, wy);

                    float noise = NoiseAt(wx * proto.NoiseSpread, wz * proto.NoiseSpread);
                    float densityScale = MathF.Min(1f, rawDensity * 2f);
                    float sizeT = noise * densityScale;
                    float sw = proto.MinWidth + sizeT * (proto.MaxWidth - proto.MinWidth);
                    float sh = proto.MinHeight + sizeT * (proto.MaxHeight - proto.MinHeight);

                    float rotY = rng.NextFloat() * MathF.PI * 2f;
                    Float4x4 transform = Float4x4.CreateTranslation(new Float3(wx, wy, wz))
                        * Float4x4.FromAxisAngle(new Float3(0, 1, 0), rotY)
                        * Float4x4.CreateScale(new Float3(sw, sh, sw));

                    Color tint = Color.Lerp(proto.HealthyColor, proto.DryColor, 1f - noise);
                    instances.Add(new InstanceData(transform,
                        new Float4(tint.R, tint.G, tint.B, tint.A),
                        new Float4(rng.NextFloat() * MathF.PI * 2f, proto.BendFactor, 0, 0)));
                }
            }
        }

        if (instances.Count >= MaxInstancesPerPrototype)
        {
            Debug.LogWarningOnce("terrain_mesh_detail_budget",
                $"Mesh detail prototype {protoIdx} hit its {MaxInstancesPerPrototype} instance budget. " +
                "Paint it more sparsely or shorten Detail Distance, or the far side will be bare.");
        }

        build.Instances = instances.ToArray();
        build.Renderables = build.Instances.Length == 0
            ? []
            : BuildRenderables(build.Instances, terrain, proto, mesh, centre, radius,
                minY == float.MaxValue ? 0f : minY, maxY == float.MinValue ? 0f : maxY);
    }

    private static IRenderable[] BuildRenderables(InstanceData[] instances, TerrainComponent terrain,
        DetailPrototype proto, Mesh mesh, Float2 centre, float radius, float minY, float maxY)
    {
        float pad = MathF.Max(proto.MaxWidth, proto.MaxHeight);
        Float3 localMin = new(centre.X - radius - pad, minY - pad, centre.Y - radius - pad);
        Float3 localMax = new(centre.X + radius + pad, maxY + pad, centre.Y + radius + pad);
        AABB bounds = TransformBounds(terrain, localMin, localMax);
        Float3 sortPosition = terrain.TerrainToWorld(new Float3(centre.X, (minY + maxY) * 0.5f, centre.Y));

        if (s_defaultStandardMat.IsNotValid()) s_defaultStandardMat = Resources.Material.LoadDefault(DefaultMaterial.Standard);

        int subMeshCount = mesh.SubMeshCount;
        var result = new IRenderable[subMeshCount];
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            Material material = null!;
            if (sub < proto.Materials.Count) material = CollectionsMarshal.AsSpan(proto.Materials)[sub].Res!;
            if (material.IsNotValid()) material = s_defaultStandardMat!;

            result[sub] = new InstancedMeshRenderable(
                mesh, material, instances, sortPosition,
                terrain.GameObject.LayerIndex, s_emptyProps, bounds,
                subMeshIndex: subMeshCount > 1 ? sub : -1);
        }
        return result;
    }

    private static AABB TransformBounds(TerrainComponent terrain, Float3 localMin, Float3 localMax)
    {
        Float3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Float3 corner = new(
                (i & 1) == 0 ? localMin.X : localMax.X,
                (i & 2) == 0 ? localMin.Y : localMax.Y,
                (i & 4) == 0 ? localMin.Z : localMax.Z);

            Float3 world = terrain.TerrainToWorld(corner);
            min = new Float3(MathF.Min(min.X, world.X), MathF.Min(min.Y, world.Y), MathF.Min(min.Z, world.Z));
            max = new Float3(MathF.Max(max.X, world.X), MathF.Max(max.Y, world.Y), MathF.Max(max.Z, world.Z));
        }
        return new AABB(min, max);
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
}
