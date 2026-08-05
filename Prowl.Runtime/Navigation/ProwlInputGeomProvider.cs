// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Feeds collected Prowl geometry to the Recast builder as world-space triangle soups,
/// grouped one <see cref="RcTriMesh"/> per navigation area so
/// <see cref="NavMeshTileBuilder.BuildTileLayers"/> can rasterize each group with its own
/// area (sources with <see cref="NavMeshGeometrySource.UnspecifiedArea"/> resolve to the
/// bake's default area).
/// </summary>
internal sealed class ProwlInputGeomProvider : IRcInputGeomProvider
{
    /// <summary>One area's triangle soup, with the area pre-converted to Detour form and its
    /// world-XZ extent for cheap tile rejection (most tiles of a bounded bake overlap nothing;
    /// an AABB test here beats even the chunky-index walk and allocates nothing).</summary>
    internal readonly struct AreaMesh
    {
        public readonly RcTriMesh Mesh;
        public readonly int DetourArea;
        public readonly float MinX, MinZ, MaxX, MaxZ;

        public AreaMesh(RcTriMesh mesh, int detourArea, float minX, float minZ, float maxX, float maxZ)
        {
            Mesh = mesh;
            DetourArea = detourArea;
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
        }

        /// <summary>Does this area's geometry overlap the XZ rect at all?</summary>
        public bool OverlapsXZ(float minX, float minZ, float maxX, float maxZ)
            => MinX <= maxX && MaxX >= minX && MinZ <= maxZ && MaxZ >= minZ;
    }

    private readonly List<AreaMesh> _areaMeshes = [];
    private readonly RcVec3f _boundsMin;
    private readonly RcVec3f _boundsMax;
    private readonly List<RcConvexVolume> _convexVolumes = [];

    /// <summary>Total triangle count across all areas.</summary>
    public int TriangleCount { get; }

    /// <summary>The per-area triangle soups, for the area-aware voxelizer.</summary>
    internal IReadOnlyList<AreaMesh> AreaMeshes => _areaMeshes;

    /// <summary>
    /// Flatten sources into per-area world-space soups. Vertices are transformed by each
    /// source's matrix here, on the calling thread, so the provider itself has no dependency
    /// on live Transforms and is safe to hand to a background build.
    /// </summary>
    public ProwlInputGeomProvider(IReadOnlyList<NavMeshGeometrySource> sources, int defaultArea = NavMeshAreas.Walkable)
    {
        ArgumentNullException.ThrowIfNull(sources);

        // Group source indices by resolved area. Order within a group is preserved, and
        // groups are keyed in first-seen order, so identical input yields identical output.
        var groups = new Dictionary<int, List<int>>();
        var groupOrder = new List<int>();
        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i].Vertices == null || sources[i].Indices == null) continue;
            int area = sources[i].Area;
            if (area < 0) area = defaultArea;
            area = Math.Clamp(area, 0, NavMeshAreas.MaxAreas - 1);

            if (!groups.TryGetValue(area, out List<int>? list))
            {
                groups[area] = list = [];
                groupOrder.Add(area);
            }
            list.Add(i);
        }

        var min = new Float3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Float3(float.MinValue, float.MinValue, float.MinValue);
        int totalTris = 0;
        bool anyVerts = false;

        foreach (int area in groupOrder)
        {
            List<int> group = groups[area];

            int vertCount = 0, triCount = 0;
            foreach (int s in group)
            {
                vertCount += sources[s].Vertices.Length;
                triCount += sources[s].TriangleCount;
            }
            if (triCount == 0) continue;

            float[] verts = new float[vertCount * 3];
            int[] tris = new int[triCount * 3];
            int vBase = 0, tWrite = 0;
            float gMinX = float.MaxValue, gMinZ = float.MaxValue, gMaxX = float.MinValue, gMaxZ = float.MinValue;

            foreach (int s in group)
            {
                NavMeshGeometrySource source = sources[s];
                for (int v = 0; v < source.Vertices.Length; v++)
                {
                    Float3 world = Float4x4.TransformPoint(source.Vertices[v], source.Transform);
                    int o = (vBase + v) * 3;
                    verts[o + 0] = (float)world.X;
                    verts[o + 1] = (float)world.Y;
                    verts[o + 2] = (float)world.Z;
                    min = Maths.Min(min, world);
                    max = Maths.Max(max, world);
                    gMinX = Math.Min(gMinX, verts[o + 0]);
                    gMinZ = Math.Min(gMinZ, verts[o + 2]);
                    gMaxX = Math.Max(gMaxX, verts[o + 0]);
                    gMaxZ = Math.Max(gMaxZ, verts[o + 2]);
                    anyVerts = true;
                }

                // t + 2 < Length guards indices whose count isn't a multiple of 3 (same guard
                // as BakedPhysicsMesh); out-of-range indices drop the whole triangle.
                for (int t = 0; t + 2 < source.Indices.Length; t += 3)
                {
                    int i0 = source.Indices[t + 0], i1 = source.Indices[t + 1], i2 = source.Indices[t + 2];
                    if ((uint)i0 >= source.Vertices.Length || (uint)i1 >= source.Vertices.Length || (uint)i2 >= source.Vertices.Length)
                        continue;
                    tris[tWrite++] = vBase + i0;
                    tris[tWrite++] = vBase + i1;
                    tris[tWrite++] = vBase + i2;
                }

                vBase += source.Vertices.Length;
            }

            // Dropped triangles leave a tail of zeros that would become degenerate triangles
            // at the origin; trim to what was actually written.
            if (tWrite == 0) continue;
            if (tWrite != tris.Length)
                Array.Resize(ref tris, tWrite);

            totalTris += tWrite / 3;
            _areaMeshes.Add(new AreaMesh(new RcTriMesh(verts, tris), DetourAreaFor(area), gMinX, gMinZ, gMaxX, gMaxZ));
        }

        TriangleCount = totalTris;

        if (!anyVerts)
        {
            min = Float3.Zero;
            max = Float3.Zero;
        }

        _boundsMin = new RcVec3f((float)min.X, (float)min.Y, (float)min.Z);
        _boundsMax = new RcVec3f((float)max.X, (float)max.Y, (float)max.Z);
    }

    /// <summary>
    /// Bake-side area conversion: Not Walkable maps to Detour's null area (0), so the marked
    /// voxels are obstacles rather than traversable "area 1" polys — Unity's Not Walkable
    /// semantics for sources, modifiers, and modifier volumes alike. Everything else goes
    /// through <see cref="NavMeshAreas.ToDetourArea"/>.
    /// </summary>
    internal static int DetourAreaFor(int area)
        => area == NavMeshAreas.NotWalkable ? 0 : NavMeshAreas.ToDetourArea(area);

    /// <summary>The first area's soup (interface requirement; the area-aware voxelizer uses
    /// <see cref="AreaMeshes"/> instead, which carries all of them).</summary>
    public RcTriMesh GetMesh() => _areaMeshes.Count > 0 ? _areaMeshes[0].Mesh : new RcTriMesh([], []);

    public RcVec3f GetMeshBoundsMin() => _boundsMin;

    public RcVec3f GetMeshBoundsMax() => _boundsMax;

    public IEnumerable<RcTriMesh> Meshes()
    {
        foreach (AreaMesh areaMesh in _areaMeshes)
            yield return areaMesh.Mesh;
    }

    public void AddConvexVolume(RcConvexVolume convexVolume) => _convexVolumes.Add(convexVolume);

    public IList<RcConvexVolume> ConvexVolumes() => _convexVolumes;

    // Off-mesh connections never travel through the geometry provider: tiles are contoured by
    // the TileCache at runtime, which injects the link set itself
    // (NavMeshTileBuilder.ProwlTileCacheMeshProcess). Nothing reads these back, so there is
    // nothing to store — they exist only because IRcInputGeomProvider declares them.

    public List<RcOffMeshConnection> GetOffMeshConnections() => [];

    public void AddOffMeshConnection(RcVec3f start, RcVec3f end, float radius, bool bidir, int area, int flags) { }

    public void RemoveOffMeshConnections(Predicate<RcOffMeshConnection> filter) { }
}
