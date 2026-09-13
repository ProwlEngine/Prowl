// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;
using Prowl.Recast.Detour.TileCache;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Produces jump and drop off-mesh connections automatically at bake time, for an agent type with
/// <see cref="NavMeshBakeSettings.JumpDistance"/> or <see cref="NavMeshBakeSettings.DropHeight"/> set:
/// scans the freshly-baked mesh's own boundary edges (the ones with no polygon neighbor across them -
/// where the walkable surface simply stops), pairs nearby ones within reach, and checks each candidate
/// pair for a clear line between them before emitting a link. Called by <see cref="RecastNavMeshBuilder.Build"/>
/// itself, gated purely by those two settings, so a project that never sets them pays nothing extra and
/// sees no behavior change at all.
/// <para/>
/// Results are stored in <see cref="NavMeshTileCacheData.GeneratedLinks"/>, replaced wholesale on every
/// bake that regenerates them - never in the scene as <see cref="NavMeshLink"/> components, so a
/// hand-placed link is never at risk of being touched by this.
/// </summary>
public static class NavMeshLinkGenerator
{


    /// <summary>How close, in world units, a height difference has to be to level before a candidate
    /// pair is treated as a jump (bidirectional) rather than a drop (one-directional, upper to lower) -
    /// <see cref="NavMeshBakeSettings.MaxStepHeight"/> itself, the same threshold the bake's own slope/
    /// step filtering already uses for "practically the same level".</summary>
    private const int MaxWallSegmentsPerPoly = 32;

    /// <summary>How many points a candidate link's clearance check samples along its straight line - a
    /// coarse, sampled approximation of a true swept-capsule test (see <see cref="HasClearPath"/>'s own
    /// doc comment), not a continuous sweep.</summary>
    private const float ClearanceSampleSpacing = 0.5f;

    /// <summary>Generates jump/drop links for a bake, or an empty list if <paramref name="settings"/>
    /// asks for neither (both <see cref="NavMeshBakeSettings.JumpDistance"/> and
    /// <see cref="NavMeshBakeSettings.DropHeight"/> are 0 or less).</summary>
    public static List<NavMeshLinkData> Generate(NavMeshBuildInput input, NavMeshTileCacheData tileCacheData, NavMeshBakeSettings settings)
    {
        var result = new List<NavMeshLinkData>();
        if (settings.JumpDistance <= 0f && settings.DropHeight <= 0f) return result;
        if (input.Triangles == null || input.Triangles.Length == 0) return result;

        // A throwaway navmesh, built purely to scan its own boundary edges - no links exist yet (that's
        // what this method is generating), and it is never handed to a caller or kept alive past here.
        (DtNavMesh navMesh, DtTileCache tileCache) = NavMeshQuery.BuildTiledNavMesh(tileCacheData, []);
        var query = new DtNavMeshQuery(navMesh);
        IDtQueryFilter filter = new DtQueryDefaultFilter(unchecked((int)uint.MaxValue), 0, new float[64]);

        List<(Float3 A, Float3 B)> boundaryEdges = CollectBoundaryEdges(navMesh, query, filter);

        for (int i = 0; i < boundaryEdges.Count; i++)
        {
            Float3 midA = (boundaryEdges[i].A + boundaryEdges[i].B) * 0.5f;
            float widthA = Float3.Distance(boundaryEdges[i].A, boundaryEdges[i].B);

            for (int j = i + 1; j < boundaryEdges.Count; j++)
            {
                Float3 midB = (boundaryEdges[j].A + boundaryEdges[j].B) * 0.5f;

                float horizontalDistance = MathF.Sqrt(
                    (midA.X - midB.X) * (midA.X - midB.X) + (midA.Z - midB.Z) * (midA.Z - midB.Z));
                if (horizontalDistance < 0.01f || horizontalDistance > settings.JumpDistance) continue;

                float heightDiff = midA.Y - midB.Y; // positive: A is higher than B

                bool isLevel = MathF.Abs(heightDiff) <= settings.MaxStepHeight;
                bool aDropsToB = !isLevel && heightDiff > 0f && heightDiff <= settings.DropHeight;
                bool bDropsToA = !isLevel && heightDiff < 0f && -heightDiff <= settings.DropHeight;

                bool wantJump = isLevel && settings.JumpDistance > 0f;
                bool wantDrop = (aDropsToB || bDropsToA) && settings.DropHeight > 0f;
                if (!wantJump && !wantDrop) continue;

                // Already connected by the ordinary walkable mesh (a narrow, thin platform's own two
                // rim edges, say) - no off-mesh connection is needed to cross what's already crossable.
                if (AlreadyConnected(query, filter, midA, midB)) continue;

                float width = MathF.Min(widthA, Float3.Distance(boundaryEdges[j].A, boundaryEdges[j].B));
                float radius = Maths.Max(settings.AgentRadius, width * 0.5f);

                if (!HasClearPath(midA, midB, settings.AgentRadius, settings.AgentHeight, settings.MaxStepHeight, input.Triangles))
                    continue;

                bool bidirectional = wantJump;
                Float3 start = aDropsToB || wantJump ? midA : midB;
                Float3 end = aDropsToB || wantJump ? midB : midA;
                result.Add(new NavMeshLinkData(start, end, radius, bidirectional, NavMeshAreas.Jump));
            }
        }

        return result;
    }

    /// <summary>Whether a straight path from <paramref name="a"/> to <paramref name="b"/> is already
    /// walkable end to end on the base mesh alone (no link needed at all).</summary>
    private static bool AlreadyConnected(DtNavMeshQuery query, IDtQueryFilter filter, Float3 a, Float3 b)
    {
        RcVec3f extents = new(1f, 4f, 1f);
        DtStatus statusA = query.FindNearestPoly(ToRc(a), extents, filter, out long refA, out RcVec3f _, out _);
        DtStatus statusB = query.FindNearestPoly(ToRc(b), extents, filter, out long refB, out RcVec3f _, out _);
        if (!statusA.Succeeded() || !statusB.Succeeded() || refA == 0 || refB == 0) return false;

        Span<long> path = new long[8];
        DtStatus status = query.FindPath(refA, refB, ToRc(a), ToRc(b), filter, path, out int count, path.Length);
        return status.Succeeded() && !status.IsPartial() && count > 0;
    }

    /// <summary>Every polygon edge, across the whole mesh, that has no neighbor across it - Detour's own
    /// <c>GetPolyWallSegments</c>, which returns exactly this per polygon (a zero neighbor reference
    /// means the edge borders nothing, not just a differently-filtered polygon).</summary>
    private static List<(Float3 A, Float3 B)> CollectBoundaryEdges(DtNavMesh navMesh, DtNavMeshQuery query, IDtQueryFilter filter)
    {
        var edges = new List<(Float3, Float3)>();
        Span<RcSegmentVert> segmentVerts = new RcSegmentVert[MaxWallSegmentsPerPoly];
        Span<long> segmentRefs = new long[MaxWallSegmentsPerPoly];

        for (int t = 0; t < navMesh.GetMaxTiles(); t++)
        {
            DtMeshTile? tile = navMesh.GetTile(t);
            DtMeshData? data = tile?.data;
            if (data?.header == null) continue;

            long baseRef = navMesh.GetPolyRefBase(tile!);
            for (int p = 0; p < data.header.polyCount; p++)
            {
                long polyRef = baseRef | (uint)p;
                int segmentCount = 0;
                DtStatus status = query.GetPolyWallSegments(polyRef, filter, segmentVerts, segmentRefs, ref segmentCount, MaxWallSegmentsPerPoly);
                if (!status.Succeeded()) continue;

                for (int s = 0; s < segmentCount; s++)
                {
                    if (segmentRefs[s] != 0) continue; // has a neighbor across this edge
                    edges.Add((ToFloat3(segmentVerts[s].vmin), ToFloat3(segmentVerts[s].vmax)));
                }
            }
        }
        return edges;
    }

    // A true swept capsule against a triangle soup needs a real capsule-vs-triangle intersection test;
    // this samples points along the straight line instead and checks each one against every triangle's
    // own 2D footprint (not just its vertices - a wide wall's vertices sit at its far corners, nowhere
    // near a sample point crossing its middle) whose height range overlaps the capsule there. The
    // remaining gap against a true continuous sweep is a thin obstruction that happens to fall entirely
    // between two consecutive sample points; see docs/navigation/design.md's own limitations section.
    /// <summary>Whether nothing in <paramref name="triangles"/> appears to block a straight line from
    /// <paramref name="a"/> to <paramref name="b"/> for a capsule of the given radius/height, sampled
    /// rather than swept continuously - see this method's own remarks above. Tests each sample against
    /// whole triangles (their 2D footprint in the horizontal plane, wherever a triangle's own height
    /// range overlaps the capsule's), not just proximity to a vertex - a wide wall's own vertices sit at
    /// its far corners, nowhere near a sample point walking straight through its middle, even though its
    /// face plainly crosses the line there.</summary>
    private static bool HasClearPath(Float3 a, Float3 b, float radius, float height, float stepHeight, Float3[] triangles)
    {
        float distance = Float3.Distance(a, b);
        int samples = Maths.Max(2, (int)MathF.Ceiling(distance / ClearanceSampleSpacing) + 1);

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1);
            Float3 samplePos = a + (b - a) * t;
            float floorY = a.Y + (b.Y - a.Y) * t;
            float blockedMinY = floorY + stepHeight;
            float blockedMaxY = floorY + height;

            for (int tri = 0; tri + 2 < triangles.Length; tri += 3)
            {
                Float3 v0 = triangles[tri], v1 = triangles[tri + 1], v2 = triangles[tri + 2];

                float triMinY = Maths.Min(v0.Y, Maths.Min(v1.Y, v2.Y));
                float triMaxY = Maths.Max(v0.Y, Maths.Max(v1.Y, v2.Y));
                if (triMaxY <= blockedMinY || triMinY >= blockedMaxY) continue;

                if (PointToTriangle2DDistance(samplePos.X, samplePos.Z, v0, v1, v2) <= radius) return false;
            }
        }
        return true;
    }

    /// <summary>Distance, in the horizontal (XZ) plane, from a point to a triangle's own 2D footprint -
    /// 0 when the point falls inside it (ignoring height entirely; <see cref="HasClearPath"/> already
    /// filtered by height range before calling this).</summary>
    private static float PointToTriangle2DDistance(float px, float pz, Float3 v0, Float3 v1, Float3 v2)
    {
        float d0 = Cross2D(px, pz, v0, v1);
        float d1 = Cross2D(px, pz, v1, v2);
        float d2 = Cross2D(px, pz, v2, v0);
        bool hasNeg = d0 < 0f || d1 < 0f || d2 < 0f;
        bool hasPos = d0 > 0f || d1 > 0f || d2 > 0f;
        if (!(hasNeg && hasPos)) return 0f; // inside (or exactly on an edge)

        return Maths.Min(
            PointToSegment2DDistance(px, pz, v0, v1),
            Maths.Min(PointToSegment2DDistance(px, pz, v1, v2), PointToSegment2DDistance(px, pz, v2, v0)));
    }

    private static float Cross2D(float px, float pz, Float3 a, Float3 b) =>
        (b.X - a.X) * (pz - a.Z) - (b.Z - a.Z) * (px - a.X);

    private static float PointToSegment2DDistance(float px, float pz, Float3 a, Float3 b)
    {
        float abx = b.X - a.X, abz = b.Z - a.Z;
        float lenSq = abx * abx + abz * abz;
        float t = lenSq > 0.0001f ? Maths.Clamp(((px - a.X) * abx + (pz - a.Z) * abz) / lenSq, 0f, 1f) : 0f;
        float cx = a.X + abx * t, cz = a.Z + abz * t;
        float dx = px - cx, dz = pz - cz;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static RcVec3f ToRc(Float3 v) => new(v.X, v.Y, v.Z);
    private static Float3 ToFloat3(RcVec3f v) => new(v.X, v.Y, v.Z);
}
