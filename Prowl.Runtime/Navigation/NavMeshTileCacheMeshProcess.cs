// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Recast.Detour;
using Prowl.Recast.Detour.TileCache;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Runs every time <c>DtTileCache</c> turns one tile's cached heightfield layer into an actual navmesh
/// tile - the initial reconstruction in <see cref="NavMeshQuery"/>'s constructor, and again whenever a
/// <see cref="NavMeshObstacle"/> carve rebuilds that tile afterward. Two jobs, both mirroring what
/// <c>BuildDetourMesh</c> used to do for the whole (untiled) mesh at once:
/// <list type="bullet">
/// <item>Re-map each polygon's raw Recast area id back to a Prowl <see cref="NavMeshAreas"/> index, and
/// set its Detour flag bit from that index, so an area mask can select it later.</item>
/// <item>Weave in whichever <see cref="NavMeshLink"/> off-mesh connections start inside this specific
/// tile - a link is a query-time concern, never baked into a tile layer itself (see
/// <see cref="NavMeshBuildInput"/>'s own doc comment), so this is the one place they actually reach the
/// navmesh, and it runs fresh on every rebuild of a tile, obstacle carve included.</item>
/// </list>
/// </summary>
internal sealed class NavMeshTileCacheMeshProcess : IDtTileCacheMeshProcess
{
    private readonly IReadOnlyList<NavMeshLinkData> _links;

    public NavMeshTileCacheMeshProcess(IReadOnlyList<NavMeshLinkData> links) => _links = links;

    public void Process(DtNavMeshCreateParams option)
    {
        for (int i = 0; i < option.polyCount; i++)
        {
            int prowlArea = NavMeshAreas.FromRecastArea(option.polyAreas[i]);
            option.polyAreas[i] = prowlArea;
            // One flag bit per Prowl area index (not the Recast/Detour area id above), so a filter's
            // includeFlags can select this polygon by area - see NavMeshQuery.CreateFilter's own doc
            // comment for why this can't just be a uniform "always included" flag.
            option.polyFlags[i] = 1 << prowlArea;
        }

        var relevant = new List<int>();
        for (int i = 0; i < _links.Count; i++)
        {
            // A connection is only ever added to the tile containing its start point - Detour resolves
            // an endpoint that lands in a different tile as a native cross-tile link, the same way an
            // ordinary polygon edge at a tile border already does. Adding it to both tiles it might
            // touch would just double it up.
            if (WithinTileXZ(_links[i].Start, option))
                relevant.Add(i);
        }

        option.offMeshConCount = relevant.Count;
        if (relevant.Count == 0) return;

        int count = relevant.Count;
        var verts = new float[count * 6];
        var rad = new float[count];
        var dir = new int[count];
        var areas = new int[count];
        var flags = new int[count];
        var userIds = new int[count];

        for (int i = 0; i < count; i++)
        {
            NavMeshLinkData link = _links[relevant[i]];
            verts[i * 6 + 0] = link.Start.X;
            verts[i * 6 + 1] = link.Start.Y;
            verts[i * 6 + 2] = link.Start.Z;
            verts[i * 6 + 3] = link.End.X;
            verts[i * 6 + 4] = link.End.Y;
            verts[i * 6 + 5] = link.End.Z;
            rad[i] = link.Radius;
            dir[i] = link.Bidirectional ? DtDetour.DT_OFFMESH_CON_BIDIR : 0;
            areas[i] = NavMeshAreas.ToRecastArea(link.AreaIndex);
            flags[i] = 1 << link.AreaIndex;
            userIds[i] = relevant[i];
        }

        option.offMeshConVerts = verts;
        option.offMeshConRad = rad;
        option.offMeshConDir = dir;
        option.offMeshConAreas = areas;
        option.offMeshConFlags = flags;
        option.offMeshConUserID = userIds;
    }

    /// <summary>Whether <paramref name="point"/> falls within this tile's horizontal bounds - height is
    /// irrelevant here, a connection can start above or below the tile's own walkable surface and still
    /// belong to it.</summary>
    private static bool WithinTileXZ(Float3 point, DtNavMeshCreateParams option) =>
        point.X >= option.bmin.X && point.X <= option.bmax.X &&
        point.Z >= option.bmin.Z && point.Z <= option.bmax.Z;
}
