// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Recast;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Geom;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Feeds collected Prowl geometry to the Recast builder as world-space triangle soups,
/// grouped one <see cref="RcTriMesh"/> per navigation area so
/// <see cref="NavMeshTileBuilder.BuildTileLayers"/> can rasterize each group with its own
/// area (sources with <see cref="NavMeshGeometrySource.UnspecifiedArea"/> resolve to the
/// bake's default area).
/// </summary>
internal sealed class ProwlInputGeomProvider
{
    /// <summary>A world-space XZ rect. No Y bound: a rebuild's vertical range comes from the bake.</summary>
    internal readonly record struct RectXZ(float MinX, float MinZ, float MaxX, float MaxZ)
    {
        public bool Overlaps(float minX, float minZ, float maxX, float maxZ)
            => MinX <= maxX && MaxX >= minX && MinZ <= maxZ && MaxZ >= minZ;
    }

    /// <summary>One area's triangle soup, with the area pre-converted to Detour form and its XZ extent
    /// for cheap tile rejection (most tiles of a bounded bake overlap nothing).</summary>
    internal readonly record struct AreaMesh(RcTriMesh Mesh, int DetourArea, RectXZ Bounds);

    private readonly List<AreaMesh> _areaMeshes = [];

    /// <summary>Total triangle count across all areas.</summary>
    public int TriangleCount { get; }

    /// <summary>The per-area triangle soups, for the area-aware voxelizer.</summary>
    internal IReadOnlyList<AreaMesh> AreaMeshes => _areaMeshes;

    /// <summary>Area volumes applied to the compact heightfield of every tile.</summary>
    internal List<RcConvexVolume> ConvexVolumes { get; } = [];

    /// <summary>World-space bounds of every source vertex. Only meaningful when <see cref="TriangleCount"/> is above zero.</summary>
    public RcVec3f BoundsMin { get; }

    public RcVec3f BoundsMax { get; }

    /// <summary>
    /// Flatten sources into per-area world-space soups. Vertices are transformed by each
    /// source's matrix here, on the calling thread, so the provider itself has no dependency
    /// on live Transforms and is safe to hand to a background build.
    /// <para/>
    /// <paramref name="clip"/> drops triangles that cannot reach the tiles being built, which is
    /// what keeps a one-tile rebuild off the cost of the whole scene's geometry; null takes
    /// everything. Vertices are still transformed either way, so the reported mesh bounds cover
    /// every source regardless.
    /// </summary>
    public ProwlInputGeomProvider(IReadOnlyList<NavMeshGeometrySource> sources, int defaultArea, RectXZ? clip)
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

                // A mirroring transform reverses winding, and the slope test reads the normal from
                // winding — so a floor scaled by -1 on one axis rasterizes as a ceiling and bakes
                // unwalkable. For a TRS matrix the determinant is the product of the scales, so its
                // sign answers this exactly.
                bool flip = Float4x4.Determinant(source.Transform) < 0f;

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
                }

                // t + 2 < Length guards indices whose count isn't a multiple of 3 (same guard
                // as BakedPhysicsMesh); out-of-range indices drop the whole triangle.
                for (int t = 0; t + 2 < source.Indices.Length; t += 3)
                {
                    int i0 = source.Indices[t + 0], i1 = source.Indices[t + 1], i2 = source.Indices[t + 2];
                    if ((uint)i0 >= source.Vertices.Length || (uint)i1 >= source.Vertices.Length || (uint)i2 >= source.Vertices.Length)
                        continue;

                    if (clip is RectXZ rect)
                    {
                        // AABB overlap, not corner containment: a triangle wider than the rect has
                        // all three corners outside it and still covers every tile in it.
                        int o0 = (vBase + i0) * 3, o1 = (vBase + i1) * 3, o2 = (vBase + i2) * 3;
                        if (!rect.Overlaps(
                                MathF.Min(verts[o0], MathF.Min(verts[o1], verts[o2])),
                                MathF.Min(verts[o0 + 2], MathF.Min(verts[o1 + 2], verts[o2 + 2])),
                                MathF.Max(verts[o0], MathF.Max(verts[o1], verts[o2])),
                                MathF.Max(verts[o0 + 2], MathF.Max(verts[o1 + 2], verts[o2 + 2]))))
                            continue;
                    }

                    tris[tWrite++] = vBase + i0;
                    tris[tWrite++] = vBase + (flip ? i2 : i1);
                    tris[tWrite++] = vBase + (flip ? i1 : i2);
                }

                vBase += source.Vertices.Length;
            }

            // Dropped triangles leave a tail of zeros that would become degenerate triangles
            // at the origin; trim to what was actually written.
            if (tWrite == 0) continue;
            if (tWrite != tris.Length)
                Array.Resize(ref tris, tWrite);

            totalTris += tWrite / 3;
            _areaMeshes.Add(new AreaMesh(new RcTriMesh(verts, tris), RasterAreaFor(area), new RectXZ(gMinX, gMinZ, gMaxX, gMaxZ)));
        }

        TriangleCount = totalTris;
        BoundsMin = new RcVec3f((float)min.X, (float)min.Y, (float)min.Z);
        BoundsMax = new RcVec3f((float)max.X, (float)max.Y, (float)max.Z);
    }

    /// <summary>Area conversion for values written straight onto the compact heightfield (convex
    /// volumes) or into a tile (off-mesh connections): Not Walkable becomes Detour's null area, so
    /// it is an obstacle rather than a traversable "area 1" poly. Rasterized geometry goes through
    /// <see cref="RasterAreaFor"/> instead.</summary>
    internal static int DetourAreaFor(int area)
        => area == NavMeshAreas.NotWalkable ? 0 : NavMeshAreas.ToDetourArea(area);

    /// <summary>The area Not Walkable rasterizes as, above every real one: merging two spans keeps
    /// the HIGHER of their areas, so the null area would lose to a walkable surface within the
    /// climb threshold. <see cref="NavMeshTileBuilder"/> retires it once the spans are compacted.
    /// </summary>
    internal const int NotWalkableRasterArea = NavMeshAreas.MaxAreas + 1;

    /// <inheritdoc cref="DetourAreaFor"/>
    internal static int RasterAreaFor(int area)
        => area == NavMeshAreas.NotWalkable ? NotWalkableRasterArea : DetourAreaFor(area);
}
