// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Camera-relative scatter for mesh detail prototypes.

using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Scatters mesh detail prototypes across the terrain, on the CPU.
///
/// Textured details are placed in the vertex shader by <see cref="TerrainDetailRenderer"/>, but a
/// mesh prototype renders with its own user-authored materials, which know nothing about procedural
/// placement. Those get instances built here instead, one set around each camera that draws the
/// terrain, which views of that camera then frustum cull on the render thread.
///
/// A set covers the whole draw distance around its camera. It is built with a margin of slack and
/// only rebuilt once the camera has spent it, and placement is seeded from the world cell index, so
/// a rebuild puts every instance back exactly where it was.
/// </summary>
internal class TerrainMeshDetailRenderer
{
    private const int MaxInstancesPerCell = 16;

    /// <summary>Instances one prototype may place at once, so a solid paint cannot stall a frame.</summary>
    private const int MaxInstancesPerPrototype = 20000;

    /// <summary>
    /// Slack around the draw distance, as a fraction of it. The set reaches this much further
    /// than it needs to and is rebuilt once the camera has moved that far, which means anything
    /// inside the draw distance was already in the previous build: instances never pop in.
    /// </summary>
    private const float kRebuildMargin = 0.15f;

    // Builds per camera view, by prototype
    private readonly Dictionary<int, Dictionary<int, Build>> _builds = [];
    private readonly List<int> _stale = [];
    private GMaterial[] _materialScratch = [];

    // Painted cells in range of a rebuild, by distance from the camera then cell, reused between rebuilds
    private static readonly List<(float DistanceSq, int Cell)> s_cells = [];
    private static readonly Comparison<(float DistanceSq, int Cell)> s_nearestFirst =
        (a, b) => a.DistanceSq != b.DistanceSq ? a.DistanceSq.CompareTo(b.DistanceSq) : a.Cell.CompareTo(b.Cell);

    /// <summary>One prototype's instances around one camera. Its arrays are never changed once built.</summary>
    public sealed class Build
    {
        public Double4x4[] Transforms = [];
        public Float4[] Tints = [];
        public Float4[] Wind = [];
        public Double3[] Centres = [];
        public float[] Radii = [];
        public Float2 Centre;
        public float Distance;
        public int DetailsVersion = -1;
        public int HeightsVersion = -1;
        public int HolesVersion = -1;
        public Float4x4 LocalToWorld;
        public Mesh? Mesh;
        public int Prototype;
    }

    /// <summary>A build with what it draws with this frame.</summary>
    public readonly struct Batch(Build build, GpuMesh mesh, GMaterial[] materials)
    {
        public readonly Build Build = build;
        public readonly GpuMesh Mesh = mesh;
        public readonly GMaterial[] Materials = materials;
    }

    /// <summary>A terrain's mesh details for one frame, by camera view. Copied on the host.</summary>
    public sealed class Frame
    {
        public readonly Dictionary<int, Batch[]> Views = [];
        public RenderableFlags Flags;
        public uint LayerMask;
        public ProbeAnchorHandle Probe;
        public uint ObjectId;

        /// <summary>Adds the instances a view sees: its camera's set, or every camera's for -1. Render thread.</summary>
        public void Cull(int viewId, in ProviderView view, DrawItemSink sink)
        {
            if (viewId >= 0)
            {
                if (Views.TryGetValue(viewId, out Batch[]? batches)) Cull(batches, view, sink);
                return;
            }
            foreach (Batch[] batches in Views.Values) Cull(batches, view, sink);
        }

        private void Cull(Batch[] batches, in ProviderView view, DrawItemSink sink)
        {
            foreach (Batch batch in batches)
            {
                Build build = batch.Build;
                for (int i = 0; i < build.Transforms.Length; i++)
                {
                    if (!view.Sees(build.Centres[i], build.Radii[i])) continue;
                    var item = new DrawItem
                    {
                        Transform = build.Transforms[i],
                        Custom0 = build.Tints[i],
                        Custom1 = build.Wind[i],
                        LayerMask = LayerMask,
                        ObjectId = ObjectId,
                        Flags = Flags,
                        Probe = Probe,
                    };
                    for (int sub = 0; sub < batch.Materials.Length; sub++)
                        sink.Add(batch.Mesh, sub, batch.Materials[sub], item);
                }
            }
        }
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

    /// <summary>
    /// Brings the sets of every camera up to date and copies what the render thread draws, the cameras by view id and
    /// world position. Host thread.
    /// </summary>
    public Frame? Prepare(TerrainData data, TerrainComponent terrain, List<(int ViewId, Double3 Position)> cameras)
    {
        _stale.Clear();
        foreach (int viewId in _builds.Keys)
            if (!cameras.Exists(c => c.ViewId == viewId)) _stale.Add(viewId);
        foreach (int viewId in _stale) _builds.Remove(viewId);

        if (data.DetailPrototypes.Count == 0 || cameras.Count == 0) return null;

        GameObject go = terrain.GameObject;
        var frame = new Frame
        {
            Flags = terrain.RenderFlags,
            LayerMask = RenderableState.LayerBit(go.LayerIndex),
            ObjectId = (uint)terrain.InstanceID,
            Probe = terrain.ProbeAnchor,
        };

        var batches = new List<Batch>();
        foreach ((int viewId, Double3 position) in cameras)
        {
            batches.Clear();
            foreach (Build build in Collect(data, terrain, viewId, (Float3)position))
            {
                GpuMesh? mesh = TerrainComponent.GpuMeshOf(build.Mesh);
                if (mesh == null) continue;
                GMaterial[] materials = MaterialsFor(data, build);
                if (materials.Length > 0) batches.Add(new Batch(build, mesh, materials));
            }
            if (batches.Count > 0) frame.Views[viewId] = [.. batches];
        }
        return frame.Views.Count > 0 ? frame : null;
    }

    // One material per submesh, Standard where the prototype names none or its material is not loaded yet
    private GMaterial[] MaterialsFor(TerrainData data, Build build)
    {
        DetailPrototype? proto = build.Prototype < data.DetailPrototypes.Count ? data.DetailPrototypes[build.Prototype] : null;

        int subMeshCount = build.Mesh.IsValid() ? build.Mesh.SubMeshCount : 0;
        if (_materialScratch.Length != subMeshCount) _materialScratch = new GMaterial[subMeshCount];
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            Material? material = proto != null && sub < proto.Materials.Count ? proto.Materials[sub] : null;
            GMaterial? gpu = TerrainComponent.GpuMaterialOf(material) ?? RenderableState.DefaultGpuMaterial;
            if (gpu == null) return [];
            _materialScratch[sub] = gpu;
        }
        return [.. _materialScratch];
    }

    /// <summary>The sets around one camera, rebuilding those it has moved out of or whose data changed. Host thread.</summary>
    internal List<Build> Collect(TerrainData data, TerrainComponent terrain, int viewId, Float3 cameraWorld)
    {
        var result = new List<Build>();
        if (data.DetailPrototypes.Count == 0) return result;

        Float3 camLocal = terrain.WorldToTerrain(cameraWorld);
        Float4x4 terrainToWorld = terrain.Transform.LocalToWorldMatrix;
        Float3 terrainScale = terrain.Transform.LocalScale;
        float avgScale = MathF.Max(0.001f, (terrainScale.X + terrainScale.Z) * 0.5f);
        float distance = MathF.Max(terrain.DetailDistance, 0.01f) / avgScale;
        var centre = new Float2(camLocal.X, camLocal.Z);

        if (!_builds.TryGetValue(viewId, out Dictionary<int, Build>? builds))
            _builds[viewId] = builds = [];

        for (int protoIdx = 0; protoIdx < data.DetailPrototypes.Count; protoIdx++)
        {
            DetailPrototype proto = data.DetailPrototypes[protoIdx];
            if (proto.RenderMode != DetailRenderMode.Mesh) continue;
            if (protoIdx >= data.DetailLayers.Count) continue;

            Mesh? mesh = proto.Mesh;
            if (mesh.IsNotValid()) continue;

            // Nothing painted, nothing to place
            if (!data.TryGetDetailBounds(protoIdx, out Float4 painted)) continue;

            if (!builds.TryGetValue(protoIdx, out Build? build)
                || build.Mesh != mesh
                || build.DetailsVersion != data.DetailsVersion
                || build.HeightsVersion != data.HeightsVersion
                || build.HolesVersion != data.HolesVersion
                || build.LocalToWorld != terrainToWorld
                || BuildIsStale(build.Centre, build.Distance, centre, distance))
            {
                builds[protoIdx] = build = Rebuild(data, terrainToWorld, proto, protoIdx, mesh, centre, distance, painted);
            }

            if (build.Transforms.Length > 0) result.Add(build);
        }
        return result;
    }

    private static Build Rebuild(TerrainData data, Float4x4 terrainToWorld, DetailPrototype proto,
        int protoIdx, Mesh mesh, Float2 centre, float distance, Float4 painted)
    {
        byte[] densityMap = data.DetailLayers[protoIdx];
        int detailRes = data.DetailResolution;
        float terrainSize = data.Size;
        float cellSize = terrainSize / detailRes;
        float radius = BuildRadius(distance);

        var transforms = new List<Float4x4>();
        var tints = new List<Float4>();
        var wind = new List<Float4>();

        // Only the cells that are both in range and actually painted
        int startX = Math.Max(0, (int)MathF.Floor(MathF.Max(centre.X - radius, painted.X) / cellSize));
        int startZ = Math.Max(0, (int)MathF.Floor(MathF.Max(centre.Y - radius, painted.Y) / cellSize));
        int endX = Math.Min(detailRes - 1, (int)MathF.Ceiling(MathF.Min(centre.X + radius, painted.Z) / cellSize));
        int endZ = Math.Min(detailRes - 1, (int)MathF.Ceiling(MathF.Min(centre.Y + radius, painted.W) / cellSize));

        float radiusSq = radius * radius;

        s_cells.Clear();
        for (int cz = startZ; cz <= endZ; cz++)
        {
            for (int cx = startX; cx <= endX; cx++)
            {
                if (densityMap[cz * detailRes + cx] * (1f / 255f) < 0.01f) continue;

                float dx = (cx + 0.5f) * cellSize - centre.X, dz = (cz + 0.5f) * cellSize - centre.Y;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq <= radiusSq) s_cells.Add((distanceSq, cz * detailRes + cx));
            }
        }

        // Nearest first, so when the budget runs out it is the furthest cells that go without
        s_cells.Sort(s_nearestFirst);

        foreach ((float _, int cell) in s_cells)
        {
            if (transforms.Count >= MaxInstancesPerPrototype) break;
            int cx = cell % detailRes, cz = cell / detailRes;
            float rawDensity = densityMap[cell] * (1f / 255f);

            float dither = s_ditherTable[(cx & 7) + (cz & 7) * 8];
            // Ordered dither, so a faint cell still averages out to its share of an instance
            int count = Math.Clamp((int)(rawDensity * MaxInstancesPerCell + dither), 0, MaxInstancesPerCell);
            count = Math.Min(count, MaxInstancesPerPrototype - transforms.Count);
            if (count <= 0) continue;

            // Seeded from the world cell, so a rebuild reproduces the same scatter exactly
            var rng = new SeededRandom((uint)(cx * 73856093 ^ cz * 19349663 ^ protoIdx * 83492791));

            for (int k = 0; k < count; k++)
            {
                float u = (cx + rng.NextFloat()) / detailRes;
                float v = (cz + rng.NextFloat()) / detailRes;
                float rotY = rng.NextFloat() * MathF.PI * 2f;
                float windPhase = rng.NextFloat() * MathF.PI * 2f;
                if (data.IsHoleAt(u, v)) continue;

                float wx = u * terrainSize;
                float wz = v * terrainSize;
                float wy = data.GetInterpolatedHeight(u, v);

                float noise = NoiseAt(wx * proto.NoiseSpread, wz * proto.NoiseSpread);
                float densityScale = MathF.Min(1f, rawDensity * 2f);
                float sizeT = noise * densityScale;
                float sw = proto.MinWidth + sizeT * (proto.MaxWidth - proto.MinWidth);
                float sh = proto.MinHeight + sizeT * (proto.MaxHeight - proto.MinHeight);

                transforms.Add(terrainToWorld * Float4x4.CreateTranslation(new Float3(wx, wy, wz))
                    * Float4x4.FromAxisAngle(new Float3(0, 1, 0), rotY)
                    * Float4x4.CreateScale(new Float3(sw, sh, sw)));

                Color tint = Color.Lerp(proto.HealthyColor, proto.DryColor, 1f - noise);
                tints.Add(new Float4(tint.R, tint.G, tint.B, tint.A));
                wind.Add(new Float4(windPhase, proto.BendFactor, 0, 0));
            }
        }

        if (transforms.Count >= MaxInstancesPerPrototype)
        {
            Debug.LogWarningOnce("terrain_mesh_detail_budget",
                $"Mesh detail prototype {protoIdx} hit its {MaxInstancesPerPrototype} instance budget. " +
                "The furthest painted cells are left bare. Paint it more sparsely or shorten Detail Distance.");
        }

        // Spheres around each instance's mesh bounds, for the views' frustum tests
        AABB bounds = mesh.bounds;
        Float3 meshCentre = (bounds.Min + bounds.Max) * 0.5f;
        float meshRadius = Float3.Length(bounds.Max - bounds.Min) * 0.5f;

        var build = new Build
        {
            Centre = centre,
            Distance = distance,
            DetailsVersion = data.DetailsVersion,
            HeightsVersion = data.HeightsVersion,
            HolesVersion = data.HolesVersion,
            LocalToWorld = terrainToWorld,
            Mesh = mesh,
            Prototype = protoIdx,
            Transforms = new Double4x4[transforms.Count],
            Tints = [.. tints],
            Wind = [.. wind],
            Centres = new Double3[transforms.Count],
            Radii = new float[transforms.Count],
        };
        for (int i = 0; i < transforms.Count; i++)
        {
            Float4x4 transform = transforms[i];
            build.Transforms[i] = (Double4x4)transform;
            build.Centres[i] = Float4x4.TransformPoint(meshCentre, transform);
            build.Radii[i] = meshRadius * TerrainComponent.MaxScale(transform);
        }
        return build;
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
