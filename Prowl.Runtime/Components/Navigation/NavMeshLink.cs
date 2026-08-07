// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Connects two navmesh positions that aren't walkably connected — a jump over a gap, a drop
/// off a ledge, a ladder. Mirrors Unity's NavMeshLink: agents whose area mask includes
/// <see cref="Area"/> traverse the link automatically as part of pathing (the crowd animates
/// the hop; manual traversal is not supported). Links are BAKED data: they are collected into
/// bakes like geometry, and changing one at runtime requires rebuilding the tiles around its
/// endpoints — which this component does itself when <see cref="AutoRebuild"/> is on. A bake
/// keeps its links beside its layers and re-injects them whenever a tile is re-contoured, so
/// carving and links coexist.
/// A link's traversal cost comes from its area's cost; to price a link individually, give it
/// its own area with the desired cost.
/// </summary>
[AddComponentMenu("Navigation/NavMesh Link")]
[ComponentIcon("")] // link icon
public class NavMeshLink : MonoBehaviour
{
    [Tooltip("Link start position, local to this GameObject.")]
    public Float3 StartPoint = new(0, 0, -2.5f);

    [Tooltip("Link end position, local to this GameObject.")]
    public Float3 EndPoint = new(0, 0, 2.5f);

    [Tooltip("World-space width of the link: how wide a span of the edge it covers, which is also how far its endpoints may snap to reach walkable surface. 0 uses the agent's own radius.")]
    public float Width;

    [Tooltip("Whether the link can be traversed in both directions.")]
    public bool Bidirectional = true;

    [Tooltip("The link's area. Traversal cost comes from this area's cost, and agents whose mask excludes it won't use the link.")]
    [NavMeshArea]
    public int Area = NavMeshAreas.Jump;

    [Tooltip("Whether the link is traversable. Toggling at runtime rebuilds the affected tiles (with Auto Rebuild on).")]
    public bool Activated = true;

    [Tooltip("Follow Transform movement at runtime by rebuilding the affected tiles when the endpoints move. Meant for occasional repositioning, not per-frame motion — every move pays a partial rebuild.")]
    public bool AutoUpdatePosition;

    [Tooltip("Automatically rebuild the affected tiles of matching surfaces when this link changes (enable/disable, Activated, moves with Auto Update Position). Turn off in games that manage rebuilds themselves with explicit sources.")]
    public bool AutoRebuild = true;

    [Tooltip("Apply to bakes of every agent type. Turn off to pick specific types.")]
    public bool AffectAllAgentTypes = true;

    [Tooltip("Agent types whose bakes include this link, when not affecting all.")]
    [NavMeshAgentType]
    [EnableIf(nameof(UsesExplicitAgentTypes))]
    public List<int> AffectedAgentTypeIds = [];

    /// <summary>Persistent id stamped on the baked connections, resolving a traversing agent back
    /// to this component (<see cref="NavMeshAgent.CurrentOffMeshLinkData"/>). Derived from the
    /// component's <see cref="MonoBehaviour.Identifier"/>, which the scene persists, so it survives
    /// a reload and a duplicated object gets its own. Resolution is best-effort — baked data can
    /// outlive the component that produced it — so don't hang gameplay-critical logic on
    /// <c>CurrentOffMeshLinkData.Link</c>.</summary>
    public int LinkId => StableLinkId(Identifier);

    /// <summary>Fold an identifier into the non-zero int a baked connection stores. Written out
    /// rather than using Guid.GetHashCode, which is only guaranteed stable within one process;
    /// a baked id has to match across sessions.</summary>
    private static int StableLinkId(Guid identifier)
    {
        Span<byte> bytes = stackalloc byte[16];
        identifier.TryWriteBytes(bytes);

        int id = 0;
        for (int i = 0; i < 16; i += 4)
            id ^= BitConverter.ToInt32(bytes.Slice(i, 4));
        return id == 0 ? 1 : id;
    }

    private bool UsesExplicitAgentTypes => !AffectAllAgentTypes;

    // linkId → live component, for resolving a crowd agent's off-mesh connection back to the
    // component it came from. Session-local; ids themselves persist in baked data.
    private static readonly Dictionary<int, NavMeshLink> s_liveLinks = [];

    // The state the navmesh last saw, for change detection in LateUpdate. World endpoints size
    // the rebuild regions; the authored fields are tracked separately so writing them after
    // AddComponent (spawn-then-configure) re-applies the link without needing
    // AutoUpdatePosition, which is about following the Transform.
    private Float3 _appliedStart, _appliedEnd;
    private Float3 _appliedStartPoint, _appliedEndPoint;
    private float _appliedWidth;
    private int _appliedArea;
    private bool _appliedBidirectional;
    private bool _appliedActive;
    // Agent-type scoping decides which surfaces the link resolves against, so it is part of the
    // definition too; the id list is copied rather than aliased, or the comparison would be
    // against the caller's own live list and never report a change.
    private bool _appliedAffectAllAgentTypes;
    private readonly List<int> _appliedAgentTypeIds = [];

    private void CaptureAppliedDefinition()
    {
        _appliedStart = WorldStart;
        _appliedEnd = WorldEnd;
        _appliedStartPoint = StartPoint;
        _appliedEndPoint = EndPoint;
        _appliedWidth = Width;
        _appliedArea = Area;
        _appliedBidirectional = Bidirectional;
        _appliedActive = Activated;
        CaptureAppliedScope();
    }

    /// <summary>Everything except the scope (see <see cref="CaptureAppliedScope"/>).</summary>
    private void CaptureAppliedGeometry()
    {
        _appliedStart = WorldStart;
        _appliedEnd = WorldEnd;
        _appliedStartPoint = StartPoint;
        _appliedEndPoint = EndPoint;
        _appliedWidth = Width;
        _appliedArea = Area;
        _appliedBidirectional = Bidirectional;
        _appliedActive = Activated;
    }

    /// <summary>Committed separately from the rest, and only after a rebuild has run: narrowing
    /// the scope has to rebuild the surfaces the link is being taken OFF, and those are only
    /// identifiable from the previous snapshot.</summary>
    private void CaptureAppliedScope()
    {
        _appliedAffectAllAgentTypes = AffectAllAgentTypes;
        _appliedAgentTypeIds.Clear();
        if (AffectedAgentTypeIds != null) _appliedAgentTypeIds.AddRange(AffectedAgentTypeIds);
    }

    /// <summary>Surfaces to revisit on a change: those this link applies to now, plus those it
    /// applied to before. Collection filters by the current scope, so a surface in the second
    /// group rebuilds without the link — which is how it gets removed.</summary>
    private bool AffectsOrDidAffect(int agentTypeId)
        => AffectsAgentType(agentTypeId)
            || _appliedAffectAllAgentTypes
            || _appliedAgentTypeIds.Contains(agentTypeId);

    private bool DefinitionChanged()
        => !StartPoint.Equals(_appliedStartPoint)
            || !EndPoint.Equals(_appliedEndPoint)
            || Width != _appliedWidth
            || Area != _appliedArea
            || Bidirectional != _appliedBidirectional
            || AffectAllAgentTypes != _appliedAffectAllAgentTypes
            || AgentTypeIdsChanged();

    private bool AgentTypeIdsChanged()
    {
        int count = AffectedAgentTypeIds?.Count ?? 0;
        if (count != _appliedAgentTypeIds.Count) return true;
        for (int i = 0; i < count; i++)
            if (AffectedAgentTypeIds![i] != _appliedAgentTypeIds[i]) return true;
        return false;
    }

    private NavMeshWorld? _world;
    // Instances this link has already run its catch-up check against — each instance is
    // attempted at most once, which is what keeps the NavMeshChanged handler from looping
    // (our own catch-up rebuild fires the event again) and keeps permanently-unattachable
    // links (endpoints over void) from rebuilding on every navmesh event.
    private readonly HashSet<NavMeshInstance> _catchUpDone = [];

    /// <summary>Does this link apply to bakes for the given agent type?</summary>
    public bool AffectsAgentType(int agentTypeId)
        => AffectAllAgentTypes || AffectedAgentTypeIds.Contains(agentTypeId);

    /// <summary>World-space start position.</summary>
    public Float3 WorldStart => Transform.TransformPoint(StartPoint);

    /// <summary>World-space end position.</summary>
    public Float3 WorldEnd => Transform.TransformPoint(EndPoint);

    /// <summary>The live link with the given id, or null (agent link resolution).</summary>
    public static NavMeshLink? FindByLinkId(int linkId)
        => s_liveLinks.TryGetValue(linkId, out NavMeshLink? link) && link.IsValid() ? link : null;

    /// <summary>This link as a self-contained bake payload (world space, current state).</summary>
    public NavMeshLinkSource ToLinkSource()
        => new(WorldStart, WorldEnd, Width, Bidirectional, Area, LinkId);

    public override void OnEnable()
    {
        s_liveLinks[LinkId] = this;

        CaptureAppliedDefinition();

        // Catch-up must survive any enable order between links and surfaces: a navmesh may
        // already be live (runtime-spawned link), or may only register later (scene load
        // order) — the NavMeshChanged subscription covers the latter, mirroring how agents
        // handle late registration.
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsValid())
        {
            _world = scene!.Navigation;
            _world.NavMeshChanged += OnNavMeshChanged;
        }
        if (Activated) CatchUp();
    }

    public override void OnDisable()
    {
        s_liveLinks.Remove(LinkId);
        _catchUpDone.Clear();
        if (_world != null)
        {
            _world.NavMeshChanged -= OnNavMeshChanged;
            _world = null;
        }
        // Scene teardown never pays this: Scene.OnDispose marks the scene disposed and clears
        // the navigation world BEFORE GameObjects dispose, so RequestRebuild's scene-validity
        // early-out (and the surfaces' dead instances) make it a no-op. A gameplay disable
        // (pooling, destroyed building) keeps its rebuild — the world really changed.
        if (_appliedActive) RequestRebuild(_appliedStart, _appliedEnd);
    }

    private bool _catchUpPending;

    /// <summary>A navmesh registered or changed: schedule the catch-up check so links baked
    /// out of date (added/moved since the surface's last bake) insert themselves regardless
    /// of component enable order. Deferred to LateUpdate because NavMeshChanged fires INSIDE
    /// AddNavMeshData — before the registering surface has assigned its Instance — so an
    /// immediate check would see no surface to rebuild through.</summary>
    private void OnNavMeshChanged()
    {
        // Only a change to the SET of navmeshes can give this link somewhere new to attach.
        // The event also fires per frame while a surface converges a carve, and each
        // catch-up walks the whole scene — so gate on the structural counter, or every carving
        // frame pays a scene scan per link to discover nothing.
        if (_world == null || _world.StructureGeneration == _seenStructureGeneration) return;
        _seenStructureGeneration = _world.StructureGeneration;
        _catchUpPending = true;
    }

    private int _seenStructureGeneration = -1;

    /// <summary>
    /// For each matching surface with a live navmesh this link hasn't checked yet: if the
    /// baked mesh already contains the link (id stamped at bake) do nothing — baked-in links
    /// cost nothing at scene load — otherwise rebuild the endpoint tiles to insert it.
    /// </summary>
    private void CatchUp()
    {
        if (!AutoRebuild) return;
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsNotValid()) return;

        // Replaced instances (full rebakes) would otherwise be pinned by the checked set.
        _catchUpDone.RemoveWhere(i => _world?.GetInstance(i.AgentTypeId) != i);

        foreach (GameObject go in scene!.ActiveObjects)
        {
            if (go.IsNotValid()) continue;
            foreach (NavMeshSurface surface in go.GetComponents<NavMeshSurface>())
            {
                NavMeshInstance? instance = surface.Instance;
                if (instance == null || !AffectsAgentType(surface.AgentTypeId)) continue;
                if (!_catchUpDone.Add(instance)) continue;      // one attempt per instance
                if (instance.ContainsLinkId(LinkId)) continue;  // already in the live mesh
                RebuildEndpointRegions(surface, _appliedStart, _appliedEnd);
            }
        }
    }

    public override void LateUpdate()
    {
        if (_catchUpPending)
        {
            _catchUpPending = false;
            if (Activated) CatchUp();
        }

        bool activeChanged = Activated != _appliedActive;
        bool edited = DefinitionChanged();
        bool moved = AutoUpdatePosition
            && (Float3.Distance(WorldStart, _appliedStart) > 0.01 || Float3.Distance(WorldEnd, _appliedEnd) > 0.01);
        if (!activeChanged && !edited && !moved) return;

        // Rebuild around both the old and the new endpoints: the old tiles drop the stale
        // connection, the new ones gain it.
        Float3 oldStart = _appliedStart, oldEnd = _appliedEnd;
        bool relocated = moved || edited;
        CaptureAppliedGeometry();

        // An edited link has to be re-offered to every instance: catch-up only attempts each
        // one once, and the earlier attempt applied the old definition.
        if (edited) _catchUpDone.Clear();

        RequestRebuild(oldStart, oldEnd);
        if (relocated) RequestRebuild(_appliedStart, _appliedEnd);
        CaptureAppliedScope(); // both rebuilds have seen the outgoing scope
    }

    /// <summary>Rebuild the tiles around both endpoints on every registered surface this link
    /// affects. No-op when <see cref="AutoRebuild"/> is off or no matching navmesh is live
    /// (which includes scene teardown — see the note in <see cref="OnDisable"/>).</summary>
    private void RequestRebuild(Float3 start, Float3 end)
    {
        if (!AutoRebuild) return;
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsNotValid()) return;

        foreach (GameObject go in scene!.ActiveObjects)
        {
            if (go.IsNotValid()) continue;
            foreach (NavMeshSurface surface in go.GetComponents<NavMeshSurface>())
            {
                if (surface.Instance == null || !AffectsOrDidAffect(surface.AgentTypeId)) continue;
                RebuildEndpointRegions(surface, start, end);
            }
        }
    }

    /// <summary>Rebuild around both endpoints: one call when the padded regions overlap (the
    /// common short ladder/ledge link — rebuilding the same tiles twice back-to-back would
    /// double the dominant cost), two separate calls when they don't (a merged AABB across a
    /// long link would rebuild everything between the endpoints).</summary>
    private void RebuildEndpointRegions(NavMeshSurface surface, Float3 start, Float3 end)
    {
        float pad = Width * 0.5f + 1f;
        AABB startRegion = new AABB(start, start).Expanded(pad);
        AABB endRegion = new AABB(end, end).Expanded(pad);

        // A rebuild replaces the whole link registry, so the collection is identical for both
        // regions — gather it once rather than paying a scene scan per region.
        List<NavMeshLinkSource> links = surface.CollectLinks(null);

        if (startRegion.Intersects(endRegion))
            surface.RebuildLinkTiles(startRegion.Encapsulating(endRegion), links);
        else
        {
            surface.RebuildLinkTiles(startRegion, links);
            surface.RebuildLinkTiles(endRegion, links);
        }
    }

    public override void DrawGizmosSelected()
    {
        Color c = NavMeshSurface.AreaColor(Area);
        var color = new Color(c.R, c.G, c.B, 1f);
        Float3 start = WorldStart, end = WorldEnd;
        Debug.DrawLine(start, end, color);
        Debug.DrawWireSphere(start, 0.15f, color);
        Debug.DrawWireSphere(end, 0.15f, color);
        if (Width > 0f)
        {
            // Width extent ticks at both endpoints.
            var dir = new Float3(end.X - start.X, 0, end.Z - start.Z);
            double len = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            Float3 perp = len > 1e-4 ? new Float3((float)(-dir.Z / len), 0, (float)(dir.X / len)) : new Float3(1, 0, 0);
            Float3 half = perp * (Width * 0.5f);
            Debug.DrawLine(start - half, start + half, color);
            Debug.DrawLine(end - half, end + half, color);
        }
    }
}
