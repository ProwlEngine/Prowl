// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;
using Prowl.Recast.Detour.Crowd;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>Status of a calculated path (matches Unity's NavMeshPathStatus).</summary>
public enum NavMeshPathStatus
{
    /// <summary>The path reaches the destination.</summary>
    PathComplete,
    /// <summary>The path is valid but cannot reach the destination; it leads to the closest reachable point.</summary>
    PathPartial,
    /// <summary>No path exists (or the endpoints are off the navmesh).</summary>
    PathInvalid,
}

/// <summary>Which objects a <see cref="NavMeshSurface"/> bake considers.</summary>
public enum NavMeshCollectObjects
{
    /// <summary>Every active object in the scene.</summary>
    All,
    /// <summary>Every active object whose geometry intersects the surface's volume
    /// (<see cref="NavMeshSurface.Center"/> / <see cref="NavMeshSurface.Size"/>).</summary>
    Volume,
    /// <summary>Only this GameObject and its children.</summary>
    Children,
}

/// <summary>Which scene representation a navmesh bake voxelizes.</summary>
public enum NavMeshCollectGeometry
{
    /// <summary>Use the visible render meshes (MeshRenderer). What you see is what you walk on.</summary>
    RenderMeshes,
    /// <summary>Use the physics colliders. Cheaper and usually simpler geometry; what physics
    /// collides with is what agents walk on.</summary>
    PhysicsColliders,
}

/// <summary>Shape of a <see cref="NavMeshObstacle"/>.</summary>
public enum NavMeshObstacleShape
{
    /// <summary>Upright cylinder of the given radius and height.</summary>
    Cylinder,
    /// <summary>Oriented box (yaw only — Detour box obstacles rotate around Y).</summary>
    Box,
}

/// <summary>Obstacle avoidance quality for a <see cref="NavMeshAgent"/>. Member names match
/// Unity's for migration; the inspector shows the friendly display names.</summary>
public enum ObstacleAvoidanceType
{
    [InspectorName("None")]
    NoObstacleAvoidance = 0,
    [InspectorName("Low")]
    LowQualityObstacleAvoidance = 1,
    [InspectorName("Medium")]
    MedQualityObstacleAvoidance = 2,
    [InspectorName("Good")]
    GoodQualityObstacleAvoidance = 3,
    [InspectorName("High")]
    HighQualityObstacleAvoidance = 4,
}

/// <summary>What a navmesh polygon edge borders, for debug drawing: the walkable boundary,
/// another polygon, or the seam to a neighbouring tile.</summary>
public enum NavMeshEdgeKind : byte
{
    /// <summary>Nothing walkable on the far side — the edge of the mesh.</summary>
    Border,

    /// <summary>Another polygon in the same tile.</summary>
    Inner,

    /// <summary>A tile seam; the far side lives in the neighbouring tile.</summary>
    TilePortal,
}

/// <summary>
/// A reference to one agent type in the project's table (see <see cref="NavMeshAgentTypes"/>), by
/// its persistent id. The default is <see cref="NavMeshAgentTypes.Humanoid"/>. Converts to and from
/// the id; the type is what gives a field the agent type dropdown in the inspector.
/// </summary>
public struct NavMeshAgentTypeId : IEquatable<NavMeshAgentTypeId>, ISerializable
{
    [SerializeField] private int id;

    public NavMeshAgentTypeId(int id) => this.id = id;

    public readonly int Value => id;

    /// <summary>The type's display name, or "Agent Type N" when the id is no longer defined.</summary>
    public readonly string Name => NavMeshAgentTypes.GetName(id);

    public static implicit operator int(NavMeshAgentTypeId agentType) => agentType.id;
    public static implicit operator NavMeshAgentTypeId(int id) => new(id);

    public readonly bool Equals(NavMeshAgentTypeId other) => id == other.id;
    public override readonly bool Equals(object? obj) => obj is NavMeshAgentTypeId other && Equals(other);
    public override readonly int GetHashCode() => id;

    public static bool operator ==(NavMeshAgentTypeId left, NavMeshAgentTypeId right) => left.id == right.id;
    public static bool operator !=(NavMeshAgentTypeId left, NavMeshAgentTypeId right) => left.id != right.id;

    public readonly void Serialize(ref EchoObject value, SerializationContext ctx) => value.Add("id", new EchoObject(id));

    public void Deserialize(EchoObject value, SerializationContext ctx)
        => id = value.TryGet("id", out EchoObject? stored) ? stored!.IntValue : 0;

    public override readonly string ToString() => $"NavMeshAgentTypeId({id})";
}

/// <summary>
/// Which agent types a link, modifier or modifier volume applies to: every type, or the listed ones.
/// </summary>
public sealed class NavMeshAgentTypeSet
{
    /// <summary>Apply to every agent type, ignoring <see cref="Ids"/>.</summary>
    public bool AffectsAll = true;

    /// <summary>The agent types applied to when <see cref="AffectsAll"/> is off.</summary>
    public List<NavMeshAgentTypeId> Ids = [];

    public bool Contains(NavMeshAgentTypeId agentTypeId) => AffectsAll || (Ids?.Contains(agentTypeId) ?? false);

    public NavMeshAgentTypeSet Clone() => new() { AffectsAll = AffectsAll, Ids = Ids == null ? [] : [.. Ids] };

    /// <summary>True when both sets apply to exactly the same types in the same listed order.</summary>
    public bool SameAs(NavMeshAgentTypeSet? other)
    {
        if (other == null || AffectsAll != other.AffectsAll) return false;
        int count = Ids?.Count ?? 0;
        if (count != (other.Ids?.Count ?? 0)) return false;
        for (int i = 0; i < count; i++)
            if (Ids![i] != other.Ids![i]) return false;
        return true;
    }

    /// <summary>A set applying only to the given types.</summary>
    public static NavMeshAgentTypeSet Of(params NavMeshAgentTypeId[] agentTypeIds) => new() { AffectsAll = false, Ids = [.. agentTypeIds] };
}

/// <summary>
/// One navigation area, by its index in the project's area table (see <see cref="NavMeshAreas"/>).
/// Converts to and from the index, so the area constants work wherever an area is expected; the
/// type is what gives a field the area dropdown in the inspector.
/// </summary>
public struct NavMeshArea : IEquatable<NavMeshArea>, ISerializable
{
    [SerializeField] private int index;

    public NavMeshArea(int index) => this.index = index;

    public readonly int Index => index;

    /// <summary>The area's name in the project's area table, or an empty string for an unnamed slot.</summary>
    public readonly string Name => NavMeshAreas.GetAreaName(index);

    public static implicit operator int(NavMeshArea area) => area.index;
    public static implicit operator NavMeshArea(int index) => new(index);

    public readonly bool Equals(NavMeshArea other) => index == other.index;
    public override readonly bool Equals(object? obj) => obj is NavMeshArea other && Equals(other);
    public override readonly int GetHashCode() => index;

    public static bool operator ==(NavMeshArea left, NavMeshArea right) => left.index == right.index;
    public static bool operator !=(NavMeshArea left, NavMeshArea right) => left.index != right.index;

    public readonly void Serialize(ref EchoObject value, SerializationContext ctx) => value.Add("index", new EchoObject(index));

    public void Deserialize(EchoObject value, SerializationContext ctx)
        => index = value.TryGet("index", out EchoObject? stored) ? stored!.IntValue : 0;

    public override readonly string ToString() => $"NavMeshArea({index})";
}

/// <summary>
/// A set of navigation areas (see <see cref="NavMeshAreas"/>), one bit per area index.
/// <para/>
/// Stored as exclusions, the way <see cref="LayerMask"/> is: a default mask matches every area,
/// including areas defined after it was saved, and clearing one area never quietly drops the rest.
/// The serialized form is the inclusion mask.
/// </summary>
public struct NavMeshAreaMask : ISerializable
{
    public static readonly NavMeshAreaMask Everything = default;
    public static readonly NavMeshAreaMask Nothing = FromMask(0);

    // Bits set here are the areas NOT matched, so all-zero (the default) matches everything.
    [SerializeField] private uint excluded;

    /// <summary>The areas this mask matches, one bit per area index.</summary>
    public readonly uint Mask => ~excluded;

    /// <summary>Builds a mask from inclusion bits, one per area index.</summary>
    public static NavMeshAreaMask FromMask(uint mask) => new() { excluded = ~mask };

    /// <summary>A mask matching a single area.</summary>
    public static NavMeshAreaMask Only(int area) => IsValid(area) ? FromMask(1u << area) : Nothing;

    public readonly bool HasArea(int area) => IsValid(area) && (excluded & (1u << area)) == 0;

    public void SetArea(int area)
    {
        if (IsValid(area)) excluded &= ~(1u << area);
    }

    public void RemoveArea(int area)
    {
        if (IsValid(area)) excluded |= 1u << area;
    }

    /// <summary>True when the two masks share at least one area.</summary>
    public readonly bool Overlaps(NavMeshAreaMask other) => (Mask & other.Mask) != 0;

    private static bool IsValid(int area) => (uint)area < NavMeshAreas.MaxAreas;

    public static NavMeshAreaMask operator |(NavMeshAreaMask a, NavMeshAreaMask b) => FromMask(a.Mask | b.Mask);
    public static NavMeshAreaMask operator &(NavMeshAreaMask a, NavMeshAreaMask b) => FromMask(a.Mask & b.Mask);

    public override readonly bool Equals(object? obj) => obj is NavMeshAreaMask other && excluded == other.excluded;
    public override readonly int GetHashCode() => excluded.GetHashCode();

    public static bool operator ==(NavMeshAreaMask left, NavMeshAreaMask right) => left.excluded == right.excluded;
    public static bool operator !=(NavMeshAreaMask left, NavMeshAreaMask right) => left.excluded != right.excluded;

    public readonly void Serialize(ref EchoObject value, SerializationContext ctx) => value.Add("mask", new EchoObject(Mask));

    public void Deserialize(EchoObject value, SerializationContext ctx)
        => excluded = value.TryGet("mask", out EchoObject? mask) ? ~mask!.UIntValue : 0u;

    public override readonly string ToString() => $"NavMeshAreaMask(0x{Mask:X8})";
}

/// <summary>
/// Per-area path cost overrides on top of the project's area costs (see <see cref="NavMeshAreas"/>),
/// carried by a <see cref="NavMeshQueryFilter"/>.
/// </summary>
public sealed class NavMeshAreaCosts
{
    // 0 means no override for that area.
    private float[]? _overrides;

    /// <summary>The cost for an area: this override, or the project default.</summary>
    public float GetAreaCost(int areaIndex)
    {
        if (_overrides != null && (uint)areaIndex < _overrides.Length && _overrides[areaIndex] > 0f)
            return _overrides[areaIndex];
        return NavMeshAreas.GetAreaCost(areaIndex);
    }

    /// <summary>Override an area's cost. Clamped to at least 1: Detour's heuristic is only admissible
    /// when nothing costs less than distance, so a lower cost silently produces worse paths. To prefer
    /// an area, raise the others instead.</summary>
    public void SetAreaCost(int areaIndex, float cost)
    {
        if ((uint)areaIndex >= NavMeshAreas.MaxAreas) return;
        _overrides ??= new float[NavMeshAreas.MaxAreas];
        _overrides[areaIndex] = Math.Max(1f, cost);
    }

    /// <summary>Remove every override, falling back to the project costs.</summary>
    public void Clear() => _overrides = null;

    /// <summary>The raw table, 0 meaning no override, or null when none were set. Read-only.</summary>
    internal float[]? Overrides => _overrides;
}

/// <summary>
/// Which navmesh a query runs against and which of its areas it may cross, the navigation
/// counterpart of <see cref="QueryFilter"/>. The default matches every area of the default agent
/// type's navmesh with the project's area costs.
/// </summary>
public struct NavMeshQueryFilter
{
    /// <summary>Areas the query may cross.</summary>
    public NavMeshAreaMask AreaMask;

    /// <summary>The agent type whose navmesh is queried. Each type has its own.</summary>
    public NavMeshAgentTypeId AgentTypeId;

    /// <summary>Per-area cost overrides, or null for the project's costs.</summary>
    public NavMeshAreaCosts? AreaCosts;

    public static readonly NavMeshQueryFilter Default = default;

    public NavMeshQueryFilter(NavMeshAreaMask areaMask) => AreaMask = areaMask;

    public NavMeshQueryFilter(NavMeshAreaMask areaMask, NavMeshAgentTypeId agentTypeId)
    {
        AreaMask = areaMask;
        AgentTypeId = agentTypeId;
    }

    /// <summary>This filter, querying another agent type's navmesh.</summary>
    public readonly NavMeshQueryFilter ForAgentType(NavMeshAgentTypeId agentTypeId)
    {
        NavMeshQueryFilter filter = this;
        filter.AgentTypeId = agentTypeId;
        return filter;
    }

    /// <summary>This filter, with per-area cost overrides.</summary>
    public readonly NavMeshQueryFilter WithAreaCosts(NavMeshAreaCosts? areaCosts)
    {
        NavMeshQueryFilter filter = this;
        filter.AreaCosts = areaCosts;
        return filter;
    }

    public static implicit operator NavMeshQueryFilter(NavMeshAreaMask areaMask) => new(areaMask);
}

/// <summary>
/// A rented thread-safe navmesh query. Dispose to return it to the pool. Leases hold a read
/// lock on the navmesh, so keep them short-lived. A ref struct so a lease cannot be stored in a
/// field, boxed, captured or held across an await, which is how a read lock outlives its frame.
/// </summary>
public ref struct NavMeshQueryLease
{
    private NavMeshInstance? _instance;

    /// <summary>The Detour query, valid until this lease is disposed.</summary>
    public DtNavMeshQuery Query { get; }

    internal NavMeshQueryLease(NavMeshInstance instance, DtNavMeshQuery query)
    {
        _instance = instance;
        Query = query;
    }

    public void Dispose()
    {
        if (_instance == null) return;
        _instance.QueryPool.Add(Query);
        _instance.Lock.ExitReadLock();
        _instance.Release();
        _instance = null;
    }
}

/// <summary>
/// Detour's view of a <see cref="NavMeshQueryFilter"/>: area inclusion against the full 32-bit area
/// mask (Detour's default filter only has 16 flag bits) and costs from the overrides or the project
/// table. Mutable and reused, one per thread for queries and one per crowd filter slot.
/// </summary>
internal sealed class NavMeshDetourFilter : IDtQueryFilter
{
    /// <summary>Inclusion bits, one per area index.</summary>
    public uint AreaMask = uint.MaxValue;

    /// <summary>Per-area overrides, 0 meaning none, or null for the project costs.</summary>
    public float[]? CostOverrides;

    public void Set(in NavMeshQueryFilter filter)
    {
        AreaMask = filter.AreaMask.Mask;
        CostOverrides = filter.AreaCosts?.Overrides;
    }

    public float GetAreaCost(int areaIndex)
    {
        if (CostOverrides != null && (uint)areaIndex < CostOverrides.Length && CostOverrides[areaIndex] > 0f)
            return CostOverrides[areaIndex];
        return NavMeshAreas.GetAreaCost(areaIndex);
    }

    public bool PassFilter(long refs, DtMeshTile tile, DtPoly poly)
    {
        if (poly.flags == 0) return false;
        int area = NavMeshAreas.FromDetourArea(poly.GetArea());
        return (AreaMask & (1u << area)) != 0;
    }

    public float GetCost(RcVec3f pa, RcVec3f pb, long prevRef, DtMeshTile prevTile, DtPoly prevPoly,
        long curRef, DtMeshTile curTile, DtPoly curPoly, long nextRef, DtMeshTile nextTile, DtPoly nextPoly)
    {
        int area = NavMeshAreas.FromDetourArea(curPoly.GetArea());
        return RcVec3f.Distance(pa, pb) * GetAreaCost(area);
    }
}

/// <summary>
/// Result of a navmesh query such as <see cref="NavMeshWorld.SamplePosition(Float3, out NavMeshHit, float)"/>,
/// <see cref="NavMeshWorld.Raycast(Float3, Float3, out NavMeshHit)"/> or
/// <see cref="NavMeshWorld.FindClosestEdge(Float3, out NavMeshHit)"/>.
/// </summary>
public struct NavMeshHit
{
    /// <summary>The resulting location on the navmesh.</summary>
    public Float3 Position;

    /// <summary>Normal at the hit (edge/wall normal for raycast and closest-edge queries;
    /// straight up for position samples).</summary>
    public Float3 Normal;

    /// <summary>Distance from the query origin to <see cref="Position"/>.</summary>
    public float Distance;

    /// <summary>The area of the polygon at the hit, as a single-area mask. <see cref="NavMeshAreaMask.Nothing"/>
    /// when there was no polygon to read it from.</summary>
    public NavMeshAreaMask Mask;

    /// <summary>True when the query found something.</summary>
    public bool Hit;
}

/// <summary>One segment of a navmesh polygon edge, classified. These are the polygon outlines —
/// the mesh's structure — as opposed to the height-detail triangles that carpet their interiors.
/// An outline is reported as the chain of segments the height detail actually renders, since the
/// detail bends the surface between corners and a single chord corner to corner would leave the
/// surface it is meant to outline.</summary>
public readonly struct NavMeshEdge(Float3 a, Float3 b, NavMeshEdgeKind kind)
{
    public readonly Float3 A = a;
    public readonly Float3 B = b;
    public readonly NavMeshEdgeKind Kind = kind;
}

/// <summary>
/// An off-mesh connection an agent can actually traverse, at the endpoints Detour snapped onto
/// walkable polygons rather than where the <see cref="NavMeshLink"/> asked for. A link that reached
/// nothing walkable is absent here, which is the only way to tell it failed.
/// </summary>
public readonly struct NavMeshConnection(Float3 start, Float3 end, float radius, int area, bool bidirectional, int linkId)
{
    public readonly Float3 Start = start;
    public readonly Float3 End = end;

    /// <summary>Endpoint radius: half the width the link was built with.</summary>
    public readonly float Radius = radius;

    /// <summary>Area index (see <see cref="NavMeshAreas"/>), which is also what it costs.</summary>
    public readonly int Area = area;

    public readonly bool Bidirectional = bidirectional;

    /// <summary>The <see cref="NavMeshLink.LinkId"/> stamped at bake, or 0 for a connection that
    /// came from somewhere else.</summary>
    public readonly int LinkId = linkId;

    /// <summary>
    /// Read one of a tile's connections, unless it is not traversable end to end — Detour keeps the
    /// stub whichever end failed, so the test is on the links. The area and endpoints come from the
    /// connection's polygon: <c>con.pos</c> holds what was asked for, its vertices where it snapped.
    /// </summary>
    internal static bool TryFrom(DtMeshTile tile, DtOffMeshConnection con, out NavMeshConnection connection)
    {
        connection = default;
        DtPoly poly = tile.data.polys[con.poly];
        if (!IsAttachedAtBothEnds(tile, poly)) return false;

        connection = new NavMeshConnection(
            VertexAt(tile, poly.verts[0]), VertexAt(tile, poly.verts[1]), con.rad,
            NavMeshAreas.FromDetourArea(poly.GetArea()),
            (con.flags & DtDetour.DT_OFFMESH_CON_BIDIR) != 0,
            con.userId);
        return true;
    }

    /// <summary>
    /// The two ends attach independently, each as its own tile is built, leaving a link on the
    /// connection's polygon tagged with which end it is. An end that found nothing walkable leaves
    /// none, and an agent arriving with no far end has nowhere to come out.
    /// </summary>
    private static bool IsAttachedAtBothEnds(DtMeshTile tile, DtPoly poly)
    {
        bool start = false, end = false;
        for (int i = poly.firstLink; i != DtDetour.DT_NULL_LINK; i = tile.links[i].next)
        {
            if (tile.links[i].edge == 0) start = true;
            else if (tile.links[i].edge == 1) end = true;
        }
        return start && end;
    }

    /// <summary>One of a tile's vertices, which are stored as loose floats.</summary>
    internal static Float3 VertexAt(DtMeshTile tile, int vertexIndex)
    {
        int i = vertexIndex * 3;
        return new Float3(tile.data.verts[i], tile.data.verts[i + 1], tile.data.verts[i + 2]);
    }
}

/// <summary>State of the off-mesh link a <see cref="NavMeshAgent"/> is traversing.</summary>
public readonly struct OffMeshLinkData
{
    /// <summary>True while the agent is on an off-mesh connection.</summary>
    public readonly bool Valid;

    /// <summary>World-space start of the traversal.</summary>
    public readonly Float3 StartPos;

    /// <summary>World-space end of the traversal.</summary>
    public readonly Float3 EndPos;

    /// <summary>The <see cref="NavMeshLink"/> component the connection came from, or null
    /// (connection baked without a link id, or the component is gone).</summary>
    public readonly NavMeshLink? Link;

    internal OffMeshLinkData(bool valid, Float3 startPos, Float3 endPos, NavMeshLink? link)
    {
        Valid = valid;
        StartPos = startPos;
        EndPos = endPos;
        Link = link;
    }
}

/// <summary>
/// The regenerated compressed layers of one navmesh tile, produced by a partial rebuild and applied
/// with <see cref="NavMeshSurface.ApplyRebuiltTiles"/>. No layers means the tile is now empty.
/// </summary>
public readonly record struct NavMeshTileRebuild(int X, int Z, IReadOnlyList<byte[]> Layers);

/// <summary>
/// A calculated navigation path: world-space corner points plus a status. Reusable — pass the
/// same instance to repeated <see cref="NavMeshWorld.CalculatePath(Float3, Float3, NavMeshPath)"/>
/// calls to avoid reallocating.
/// </summary>
public sealed class NavMeshPath
{
    private Float3[] _corners = [];
    private int _cornerCount;

    // The polygons the corners were derived from, so NavMeshAgent.SetPath can hand the crowd the
    // route itself. Corners cannot express one: two different polygon paths can share them.
    private long[] _polys = [];
    private int _polyCount;

    internal Span<long> Polys => _polys.AsSpan(0, _polyCount);

    /// <summary>The state of the path.</summary>
    public NavMeshPathStatus Status { get; internal set; } = NavMeshPathStatus.PathInvalid;

    /// <summary>The corner points of the path. Allocates a fresh array; use
    /// <see cref="GetCornersNonAlloc"/> on hot paths.</summary>
    public Float3[] Corners
    {
        get
        {
            Float3[] result = new Float3[_cornerCount];
            Array.Copy(_corners, result, _cornerCount);
            return result;
        }
    }

    /// <summary>Number of valid corners.</summary>
    public int CornerCount => _cornerCount;

    /// <summary>The point the path actually reaches, which for a partial path is not the requested
    /// destination. Callers must check <see cref="CornerCount"/> first.</summary>
    internal Float3 LastCorner => _corners[_cornerCount - 1];

    /// <summary>Copy up to <paramref name="results"/>.Length corners into the given array,
    /// returning the number written.</summary>
    public int GetCornersNonAlloc(Float3[] results)
    {
        ArgumentNullException.ThrowIfNull(results);
        int n = Math.Min(results.Length, _cornerCount);
        Array.Copy(_corners, results, n);
        return n;
    }

    /// <summary>Erase all corner points and reset the status to invalid.</summary>
    public void ClearCorners()
    {
        _cornerCount = 0;
        _polyCount = 0;
        Status = NavMeshPathStatus.PathInvalid;
    }

    internal void SetPolys(ReadOnlySpan<long> polys)
    {
        if (_polys.Length < polys.Length)
            _polys = new long[polys.Length];
        polys.CopyTo(_polys);
        _polyCount = polys.Length;
    }

    internal void SetCorners(ReadOnlySpan<Float3> corners, NavMeshPathStatus status)
    {
        if (_corners.Length < corners.Length)
            _corners = new Float3[Math.Max(corners.Length, 16)];
        corners.CopyTo(_corners);
        _cornerCount = corners.Length;
        Status = status;
    }
}

/// <summary>
/// One chunk of triangle geometry contributed to a navmesh bake: source-local vertices and
/// indices plus the transform into world space. Collected from renderers, colliders, terrain,
/// or supplied directly by user code for procedural geometry.
/// </summary>
public struct NavMeshGeometrySource
{
    /// <summary>Vertices in source-local space.</summary>
    public Float3[] Vertices;

    /// <summary>Triangle indices into <see cref="Vertices"/> (three per triangle).</summary>
    public int[] Indices;

    /// <summary>Transforms <see cref="Vertices"/> into world space.</summary>
    public Float4x4 Transform;

    /// <summary>Sentinel for <see cref="Area"/>: the source takes the bake's default area.</summary>
    public const int UnspecifiedArea = -1;

    /// <summary>The navigation area for this geometry (index into <see cref="NavMeshAreas"/>).
    /// Walkable polygons rasterized from this source carry this area, so per-source costs and
    /// masks apply. <see cref="UnspecifiedArea"/> (the constructor default) falls back to the
    /// bake's default area. Where surfaces of different areas overlap vertically within the
    /// climb threshold, the HIGHER area index wins the merged span (Recast's convention) — not
    /// the higher cost — so order user areas accordingly when stacking geometry.</summary>
    public int Area;

    public NavMeshGeometrySource(Float3[] vertices, int[] indices, Float4x4 transform, int area = UnspecifiedArea)
    {
        Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        Indices = indices ?? throw new ArgumentNullException(nameof(indices));
        Transform = transform;
        Area = area;
    }

    /// <summary>Number of whole triangles described by <see cref="Indices"/>.</summary>
    public readonly int TriangleCount => (Indices?.Length ?? 0) / 3;
}

/// <summary>
/// A world-space off-mesh connection fed into a bake (the payload of <see cref="NavMeshLink"/>).
/// Self-contained — no Transform or component references — so it is safe to hand to a
/// background build. A link with <see cref="Width"/> &gt; 0 is expanded into parallel
/// connections across the span, so an agent enters at the nearest point along it.
/// </summary>
public readonly struct NavMeshLinkSource
{
    /// <summary>World-space endpoints. Each must land within the agent radius of walkable
    /// surface for the connection to attach.</summary>
    public readonly Float3 Start, End;

    /// <summary>World-space width of the link: how wide a span of the edge it covers. 0 leaves
    /// the connection at the agent's own radius.</summary>
    public readonly float Width;

    /// <summary>Whether the link can be traversed end-to-start as well.</summary>
    public readonly bool Bidirectional;

    /// <summary>The link's area (see <see cref="NavMeshAreas"/>); traversal cost comes from
    /// the area's cost.</summary>
    public readonly int Area;

    /// <summary>Stable user id stamped on the baked connection, used to resolve a traversing
    /// agent back to its <see cref="NavMeshLink"/> component. 0 = none.</summary>
    public readonly int UserId;

    public NavMeshLinkSource(Float3 start, Float3 end, float width, bool bidirectional, int area, int userId)
    {
        Start = start;
        End = end;
        Width = Math.Max(0f, width);
        Bidirectional = bidirectional;
        Area = area;
        UserId = userId;
    }

    /// <summary>Conservative world AABB covering both endpoints plus the width, for bounds
    /// filtering and for sizing rebuild regions.</summary>
    public AABB Bounds => new AABB(Start, Start).Encapsulating(End).Expanded(Width * 0.5f + 0.5f);

    /// <summary>
    /// The crossing points this link becomes: one per parallel connection, spread across
    /// <see cref="Width"/> (capped at 8). Unity lets an agent enter a wide link at the nearest
    /// point along its entry edge; a Detour off-mesh connection is a single point, so a span is
    /// approximated by several of them side by side and the agent takes the nearest.
    /// Re-expanded every time the cache re-contours a tile.
    /// </summary>
    /// <param name="agentRadius">Bake agent radius: the connection radius, the spacing between
    /// parallel connections, and the inset that keeps the outermost ones on the span.</param>
    /// <param name="results">Receives the crossings; not cleared.</param>
    public void ExpandCrossings(float agentRadius, System.Collections.Generic.List<(Float3 Start, Float3 End)> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        float radius = Math.Max(0.01f, agentRadius);
        int count = Width <= 0f ? 1 : Math.Clamp((int)MathF.Ceiling(Width / (2f * radius)), 1, 8);

        // Horizontal perpendicular of the span, for spreading the parallel connections. A
        // (near-)vertical link has no meaningful width axis; fall back to +X.
        var dir = new Float3(End.X - Start.X, 0, End.Z - Start.Z);
        double len = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
        Float3 perp = len > 1e-4 ? new Float3((float)(-dir.Z / len), 0, (float)(dir.X / len)) : new Float3(1, 0, 0);

        // Endpoints inset by the radius so the outermost connections stay on the span.
        float half = Math.Max(0f, Width * 0.5f - radius);
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : -half + i * (2f * half / (count - 1));
            Float3 offset = perp * t;
            results.Add((Start + offset, End + offset));
        }
    }
}

/// <summary>
/// A world-space convex prism that stamps an area over already-rasterized geometry during a
/// bake (the payload of <see cref="NavMeshModifierVolume"/>, applied via Recast's convex
/// volume marking). Self-contained — no Transform or component references — so it is safe to
/// hand to a background build alongside <see cref="NavMeshGeometrySource"/>s. A volume only
/// re-marks voxels that geometry produced; it never creates walkable surface on its own, and
/// an <see cref="NavMeshAreas.NotWalkable"/> volume erases walkability inside its footprint
/// (Unity's Modifier Volume behaviour).
/// </summary>
public readonly struct NavMeshAreaVolume
{
    /// <summary>World-space convex footprint polygon; only X/Z are used.</summary>
    public readonly Float3[] Footprint;

    /// <summary>Vertical extent of the prism, in world space.</summary>
    public readonly float MinY, MaxY;

    /// <summary>The area stamped inside the volume (see <see cref="NavMeshAreas"/>).</summary>
    public readonly int Area;

    public NavMeshAreaVolume(Float3[] footprint, float minY, float maxY, int area)
    {
        Footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        MinY = minY;
        MaxY = maxY;
        Area = area;
    }

    /// <summary>Conservative world AABB of the prism, for bounds filtering.</summary>
    public AABB Bounds
    {
        get
        {
            // XZ from the footprint, Y from the prism's own extent (the hull keeps only the
            // XZ-extreme corners, so their Y values don't span the prism).
            AABB footprint = AABB.FromPoints(Footprint);
            return new AABB(
                new Float3(footprint.Min.X, MinY, footprint.Min.Z),
                new Float3(footprint.Max.X, MaxY, footprint.Max.Z));
        }
    }

    /// <summary>
    /// Build the volume for an oriented box (local center/size under a world transform): the
    /// 8 corners are transformed, the vertical range is their Y span, and the footprint is the
    /// convex hull of their XZ projection. Yaw-only boxes yield a 4-gon; arbitrary rotations
    /// project to up-to-6-gons, which stay convex and are marked exactly.
    /// </summary>
    public static NavMeshAreaVolume FromOrientedBox(in Float4x4 localToWorld, Float3 center, Float3 size, int area)
    {
        Float3 half = size * 0.5f;
        var corners = new Float3[8];
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var local = new Float3(
                center.X + ((i & 1) == 0 ? -half.X : half.X),
                center.Y + ((i & 2) == 0 ? -half.Y : half.Y),
                center.Z + ((i & 4) == 0 ? -half.Z : half.Z));
            Float3 world = Float4x4.TransformPoint(local, localToWorld);
            corners[i] = world;
            minY = Math.Min(minY, (float)world.Y);
            maxY = Math.Max(maxY, (float)world.Y);
        }

        return new NavMeshAreaVolume(ConvexHullXZ(corners), minY, maxY, area);
    }

    /// <summary>2D convex hull (monotone chain) over the points' XZ projection.</summary>
    private static Float3[] ConvexHullXZ(Float3[] points)
    {
        var sorted = new List<Float3>(points);
        sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));

        static double Cross(Float3 o, Float3 a, Float3 b)
            => (a.X - o.X) * (b.Z - o.Z) - (a.Z - o.Z) * (b.X - o.X);

        // Monotone chain. <= 0 drops collinear (and duplicate) points, so degenerate
        // projections (an edge-on box) collapse below 3 vertices and the volume marks nothing.
        var hull = new List<Float3>(sorted.Count);
        foreach (Float3 p in sorted) // lower hull
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        int lowerEnd = hull.Count + 1;
        for (int i = sorted.Count - 2; i >= 0; i--) // upper hull (sorted[^1] already placed)
        {
            Float3 p = sorted[i];
            while (hull.Count >= lowerEnd && Cross(hull[^2], hull[^1], p) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        hull.RemoveAt(hull.Count - 1); // the upper hull's last point repeats hull[0]
        return [.. hull];
    }
}

/// <summary>
/// One project-level agent type: the physical envelope navmeshes are voxelized for. Surfaces
/// and agents reference an entry by <see cref="Id"/> through a <see cref="NavMeshAgentTypeId"/>.
/// </summary>
public sealed class NavMeshAgentType
{
    /// <summary>Persistent identifier. Stable across renames and removals of other types —
    /// never an index into the table.</summary>
    public int Id;

    public string Name = string.Empty;

    /// <summary>Agent radius in world units. Walkable surfaces are eroded by this distance from walls.</summary>
    public float Radius = 0.5f;

    /// <summary>Agent height in world units. Spaces lower than this are not walkable.</summary>
    public float Height = 2.0f;

    /// <summary>Maximum walkable slope angle in degrees.</summary>
    public float MaxSlope = 45f;

    /// <summary>Maximum ledge height the agent can step up, in world units.</summary>
    public float MaxClimb = 0.4f;

    /// <summary>Copy, so the table holds entries of its own rather than the caller's objects.
    /// Field by field: a reference field added later would be shared, and settings loading would
    /// hand every table entry the same one.</summary>
    public NavMeshAgentType Clone() => new()
    {
        Id = Id,
        Name = Name,
        Radius = Radius,
        Height = Height,
        MaxSlope = MaxSlope,
        MaxClimb = MaxClimb,
    };
}

/// <summary>
/// The resolved input to a bake: an agent type's physical envelope plus the surface-level
/// rasterization overrides, composed by <see cref="NavMeshAgentTypes.GetBuildSettings"/>. A baked
/// <see cref="NavMeshData"/> keeps a snapshot, so later edits to the agent table or the surface do
/// not change what an existing navmesh was built with.
/// </summary>
public sealed class NavMeshBuildSettings
{
    /// <summary>Largest tile size a navmesh can represent: compressed layer headers store the
    /// layer's grid dimensions in a byte, and a wider tile wraps to an empty layer, a navmesh with
    /// no polygons at all from a bake that reported success.</summary>
    public const int MaxTileSize = 255;

    /// <summary>Tile size used when nothing is overridden. Carving re-contours a whole tile, so
    /// tile size is the per-carve cost and the default stays well under the cap.</summary>
    public const int DefaultTileSize = 64;

    /// <summary>The agent type this navmesh is built for. Agents only use navmeshes of their own type.</summary>
    public int AgentTypeId;

    /// <summary>The physical envelope voxelized for: radius, height, slope and climb.</summary>
    public NavMeshAgentType Agent = new();

    /// <summary>Voxel and tile sizes, region culling and Recast filtering.</summary>
    public NavMeshBuildOverrides Overrides = new();

    /// <summary>The XZ voxel size actually used: the override, or a third of the agent radius.</summary>
    public float EffectiveVoxelSize => Overrides.OverrideVoxelSize ? Math.Max(0.01f, Overrides.VoxelSize) : Math.Max(0.01f, Agent.Radius / 3f);

    /// <summary>The voxel height actually used (half the XZ voxel size).</summary>
    public float EffectiveVoxelHeight => EffectiveVoxelSize * 0.5f;

    /// <summary>The tile side length in voxels actually used. A bake stores the resolved value back
    /// into its overrides, so a baked asset always reports what was really used.</summary>
    public int EffectiveTileSize => Overrides.OverrideTileSize ? Math.Clamp(Overrides.TileSize, 16, MaxTileSize) : DefaultTileSize;

    /// <summary>Snapshot copy, so a bake is not mutated by later edits.</summary>
    public NavMeshBuildSettings Clone() => new()
    {
        AgentTypeId = AgentTypeId,
        Agent = Agent.Clone(),
        Overrides = Overrides.Clone(),
    };
}

/// <summary>
/// The surface-level half of the bake parameters: rasterization detail that belongs to a
/// particular bake rather than to an agent type. Defaults match Unity's; most bakes never need to
/// touch these.
/// </summary>
public sealed class NavMeshBuildOverrides
{
    [Tooltip("Use an explicit voxel size instead of deriving it from the agent radius (radius / 3). Smaller voxels capture finer geometry and cost more bake time and memory.")]
    public bool OverrideVoxelSize = false;

    [Tooltip("Explicit XZ voxel size in world units, used when Override Voxel Size is on. The navmesh cannot represent features smaller than this, and its surface sits above the geometry - one voxel height (half this) on flat ground, up to two on uneven ground with height detail on. Lower this to reduce that gap.")]
    [EnableIf(nameof(OverrideVoxelSize))]
    public float VoxelSize = 0.1666667f;

    [Tooltip("Use an explicit tile size instead of the default (64 voxels). Smaller tiles make partial rebuilds and obstacle carving cheaper (less area re-voxelized per change) but add per-tile overhead.")]
    public bool OverrideTileSize = false;

    [Tooltip("Tile side length in voxels, used when Override Tile Size is on. Capped at 255 (a format limit: layer headers store tile dimensions in a byte). Carving re-contours a whole tile, so keep this small.")]
    [EnableIf(nameof(OverrideTileSize))]
    public int TileSize = NavMeshBuildSettings.DefaultTileSize;

    [Tooltip("Walkable regions with a surface area smaller than this (world units squared) are removed. Raise it to cull small isolated islands like table tops.")]
    public float MinRegionArea = 2f;

    [Tooltip("How far the simplified border may deviate from the raw voxel contour, in voxels. Lower is more faithful and produces more polygons.")]
    public float EdgeMaxError = 1.3f;

    [Tooltip("Treat low obstacles (curbs, steps) the agent can climb as walkable.")]
    public bool FilterLowHangingObstacles = true;

    [Tooltip("Remove walkable voxels at ledges, preventing paths that overhang drops.")]
    public bool FilterLedgeSpans = true;

    [Tooltip("Remove walkable voxels with too little clearance above them for the agent to stand.")]
    public bool FilterWalkableLowHeightSpans = true;

    [Tooltip("Sample heights across each polygon so agents follow the ground it covers. Without it a polygon is flat between its corners, which is exact on floors and ramps but stretches across curved ground like terrain. Costs build time on every tile, including each obstacle carve, so turn it off for scenes built entirely from flat and planar surfaces.")]
    public bool BuildHeightDetail = true;

    /// <summary>Field-by-field copy, so a reference field added later cannot end up shared between copies.</summary>
    public NavMeshBuildOverrides Clone() => new()
    {
        OverrideVoxelSize = OverrideVoxelSize,
        VoxelSize = VoxelSize,
        OverrideTileSize = OverrideTileSize,
        TileSize = TileSize,
        MinRegionArea = MinRegionArea,
        EdgeMaxError = EdgeMaxError,
        FilterLowHangingObstacles = FilterLowHangingObstacles,
        FilterLedgeSpans = FilterLedgeSpans,
        FilterWalkableLowHeightSpans = FilterWalkableLowHeightSpans,
        BuildHeightDetail = BuildHeightDetail,
    };
}

/// <summary>
/// Tunables for a <see cref="NavMeshWorld"/>. Every new world starts from
/// <see cref="NavMeshWorld.DefaultSettings"/>, which the project's navigation settings fill in.
/// </summary>
public sealed class NavMeshWorldSettings
{
    /// <summary>
    /// How many navmesh polygons one path may cross. A route that would exceed it comes back
    /// <see cref="NavMeshPathStatus.PathPartial"/>, so setting it too low shows up as agents
    /// stopping short on long journeys.
    /// </summary>
    public int MaxPolyPath = 1024;

    /// <summary>How many corners one path may have. A path that fills the buffer is reported partial.</summary>
    public int MaxStraightPath = 256;

    /// <summary>Half extents used to snap query positions onto the navmesh, in world units. Larger
    /// values tolerate more vertical mismatch but can snap to the wrong floor.</summary>
    public Float3 DefaultQueryExtents = new(1f, 2f, 1f);

    /// <summary>Maximum agent radius the crowd proximity grids are sized for. Read when a crowd is created.</summary>
    public float CrowdMaxAgentRadius = 2f;

    /// <summary>How many obstacles one navmesh may carve at once. Read when a surface registers.</summary>
    public int TileCacheMaxObstacles = 256;

    /// <summary>Tiles each navmesh may rebuild per frame while draining queued carves.</summary>
    public int MaxTileUpdatesPerFrame = 4;

    public ObstacleAvoidanceSettings LowQualityAvoidance = ObstacleAvoidanceSettings.Preset(ObstacleAvoidanceType.LowQualityObstacleAvoidance);
    public ObstacleAvoidanceSettings MediumQualityAvoidance = ObstacleAvoidanceSettings.Preset(ObstacleAvoidanceType.MedQualityObstacleAvoidance);
    public ObstacleAvoidanceSettings GoodQualityAvoidance = ObstacleAvoidanceSettings.Preset(ObstacleAvoidanceType.GoodQualityObstacleAvoidance);
    public ObstacleAvoidanceSettings HighQualityAvoidance = ObstacleAvoidanceSettings.Preset(ObstacleAvoidanceType.HighQualityObstacleAvoidance);

    public ObstacleAvoidanceSettings GetAvoidance(ObstacleAvoidanceType quality) => quality switch
    {
        ObstacleAvoidanceType.LowQualityObstacleAvoidance => LowQualityAvoidance,
        ObstacleAvoidanceType.MedQualityObstacleAvoidance => MediumQualityAvoidance,
        ObstacleAvoidanceType.GoodQualityObstacleAvoidance => GoodQualityAvoidance,
        ObstacleAvoidanceType.HighQualityObstacleAvoidance => HighQualityAvoidance,
        _ => throw new ArgumentOutOfRangeException(nameof(quality), "NoObstacleAvoidance has no avoidance settings."),
    };

    public void SetAvoidance(ObstacleAvoidanceType quality, ObstacleAvoidanceSettings value)
    {
        switch (quality)
        {
            case ObstacleAvoidanceType.LowQualityObstacleAvoidance: LowQualityAvoidance = value; break;
            case ObstacleAvoidanceType.MedQualityObstacleAvoidance: MediumQualityAvoidance = value; break;
            case ObstacleAvoidanceType.GoodQualityObstacleAvoidance: GoodQualityAvoidance = value; break;
            case ObstacleAvoidanceType.HighQualityObstacleAvoidance: HighQualityAvoidance = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(quality), "NoObstacleAvoidance has no avoidance settings.");
        }
    }

    public NavMeshWorldSettings Clone() => (NavMeshWorldSettings)MemberwiseClone();
}

/// <summary>
/// Sampling and weighting agents use to steer around each other at one
/// <see cref="ObstacleAvoidanceType"/> quality level.
/// </summary>
public struct ObstacleAvoidanceSettings
{
    /// <summary>How far toward the current velocity the sampling is centered, 0 to 1.</summary>
    public float VelocityBias;
    /// <summary>Penalty for straying from the desired velocity.</summary>
    public float DesiredVelocityWeight;
    /// <summary>Penalty for changing the current velocity. Raise it to damp oscillation in tight corridors.</summary>
    public float CurrentVelocityWeight;
    /// <summary>Preference for passing others on the same side.</summary>
    public float SideWeight;
    /// <summary>Penalty for velocities that collide soon.</summary>
    public float TimeOfImpactWeight;
    /// <summary>How far ahead collisions are considered, in seconds.</summary>
    public float HorizonTime;
    public int GridSize;
    public int AdaptiveDivisions;
    public int AdaptiveRings;
    public int AdaptiveDepth;

    /// <summary>The built-in tuning for a quality level. The levels differ only in sampling density.</summary>
    public static ObstacleAvoidanceSettings Preset(ObstacleAvoidanceType quality)
    {
        (int divs, int rings, int depth) = quality switch
        {
            ObstacleAvoidanceType.LowQualityObstacleAvoidance => (5, 2, 1),
            ObstacleAvoidanceType.MedQualityObstacleAvoidance => (5, 2, 2),
            ObstacleAvoidanceType.GoodQualityObstacleAvoidance => (7, 2, 3),
            ObstacleAvoidanceType.HighQualityObstacleAvoidance => (7, 3, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(quality), "NoObstacleAvoidance has no avoidance settings."),
        };
        return new ObstacleAvoidanceSettings
        {
            VelocityBias = 0.4f,
            DesiredVelocityWeight = 2.0f,
            CurrentVelocityWeight = 0.75f,
            SideWeight = 0.75f,
            TimeOfImpactWeight = 2.5f,
            HorizonTime = 2.5f,
            GridSize = 33,
            AdaptiveDivisions = divs,
            AdaptiveRings = rings,
            AdaptiveDepth = depth,
        };
    }

    internal readonly DtObstacleAvoidanceParams ToDetour() => new()
    {
        velBias = VelocityBias,
        weightDesVel = DesiredVelocityWeight,
        weightCurVel = CurrentVelocityWeight,
        weightSide = SideWeight,
        weightToi = TimeOfImpactWeight,
        horizTime = HorizonTime,
        gridSize = GridSize,
        adaptiveDivs = AdaptiveDivisions,
        adaptiveRings = AdaptiveRings,
        adaptiveDepth = AdaptiveDepth,
    };
}

/// <summary>
/// How the scene view draws navmeshes. View state set by the editor, never saved with a scene.
/// </summary>
public static class NavMeshDebugDisplay
{
    /// <summary>Draw every surface's walkable overlay, not only the selected one's. The only way to
    /// watch obstacles carve while playing, since entering play mode clears the selection. Each
    /// overlay re-triangulates whenever its navmesh settles, so leave it off while profiling.</summary>
    public static bool AlwaysShow;

    /// <summary>Add the height detail wireframe, the triangles Recast fits inside each polygon.</summary>
    public static bool ShowDetail;

    /// <summary>Add a marker per navmesh vertex, polygon corners picked out from detail vertices.</summary>
    public static bool ShowVertices;

    /// <summary>Endpoint marker size, shared so a link's own gizmo matches the overlay.</summary>
    internal const float EndpointGizmoRadius = 0.15f;

    /// <summary>Stable debug color for an area index (Walkable is the familiar navmesh blue).</summary>
    public static Color AreaColor(int areaIndex)
    {
        if (areaIndex == NavMeshAreas.Walkable) return new Color(0f, 0.75f, 1f, 0.35f);
        float hue = (areaIndex * 137.5f) % 360f / 360f;
        float r = Math.Abs(hue * 6f - 3f) - 1f;
        float g = 2f - Math.Abs(hue * 6f - 2f);
        float b = 2f - Math.Abs(hue * 6f - 4f);
        return new Color(Math.Clamp(r, 0f, 1f), Math.Clamp(g, 0f, 1f), Math.Clamp(b, 0f, 1f), 0.35f);
    }

    /// <summary>
    /// One off-mesh connection, drawn where the navmesh put it rather than where the component
    /// asked: endpoints that snapped elsewhere show it, and a link that never attached is
    /// visibly absent. Opaque, because the translucent surface fill is a poor read for a line.
    /// </summary>
    internal static void DrawConnection(NavMeshConnection con, Float3 lift)
    {
        Color area = AreaColor(con.Area);
        var color = new Color(area.R, area.G, area.B, 1f);
        Float3 start = con.Start + lift, end = con.End + lift;

        Debug.DrawLine(start, end, color);
        Debug.DrawWireSphere(start, EndpointGizmoRadius, color);
        Debug.DrawWireSphere(end, EndpointGizmoRadius, color);

        // Measured flat: the markings read as ground plan, and a steep link would otherwise
        // splay them out of the surface.
        var flat = new Float3(end.X - start.X, 0, end.Z - start.Z);
        double length = Math.Sqrt(flat.X * flat.X + flat.Z * flat.Z);
        if (length <= 1e-4) return;
        var forward = new Float3((float)(flat.X / length), 0, (float)(flat.Z / length));
        var perp = new Float3(-forward.Z, 0, forward.X);

        // The width the connection actually covers, as a bar across each end.
        if (con.Radius > 0f)
        {
            Float3 half = perp * con.Radius;
            Debug.DrawLine(start - half, start + half, color);
            Debug.DrawLine(end - half, end + half, color);
        }

        // One-way connections get an arrowhead.
        if (con.Bidirectional) return;
        Float3 back = end - forward * (EndpointGizmoRadius * 3f);
        Float3 barb = perp * (EndpointGizmoRadius * 1.5f);
        Debug.DrawLine(end, back + barb, color);
        Debug.DrawLine(end, back - barb, color);
    }
}
