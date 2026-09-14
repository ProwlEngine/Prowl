// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Connects two navmesh positions that aren't walkably connected — a jump over a gap, a drop off a
/// ledge, a ladder. Agents whose area mask includes <see cref="Area"/> traverse it as part of
/// pathing; the crowd animates the hop and manual traversal is not supported. Links are BAKED data,
/// kept beside the layers and re-injected whenever a tile is re-contoured, so carving and links
/// coexist; with <see cref="AutoRebuild"/> on this component rebuilds the tiles around its endpoints
/// when it changes. Traversal cost comes from the area's cost, so price a link by giving it its own
/// area. Enabled outside play too, because a bake gathers links from the registry only enabled links
/// are in.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Navigation/NavMesh Link")]
[ComponentIcon("\uf0c1")] // link icon
public class NavMeshLink : MonoBehaviour
{
    [Tooltip("Link start position, local to this GameObject.")]
    [SerializeField] private Float3 startPoint = new(0, 0, -2.5f);

    [Tooltip("Link end position, local to this GameObject.")]
    [SerializeField] private Float3 endPoint = new(0, 0, 2.5f);

    [Tooltip("World-space width of the link: how wide a span of the edge it covers, which is also how far its endpoints may snap to reach walkable surface. 0 uses the agent's own radius.")]
    [SerializeField] private float width;

    [Tooltip("Whether the link can be traversed in both directions.")]
    [SerializeField] private bool bidirectional = true;

    [Tooltip("The link's area. Traversal cost comes from this area's cost, and agents whose mask excludes it won't use the link.")]
    [SerializeField] private NavMeshArea area = NavMeshAreas.Jump;

    [Tooltip("Whether the link is traversable. Toggling at runtime rebuilds the affected tiles (with Auto Rebuild on).")]
    [SerializeField] private bool activated = true;

    [Tooltip("Follow Transform movement at runtime by rebuilding the affected tiles when the endpoints move. Meant for occasional repositioning, not per-frame motion — every move pays a partial rebuild.")]
    [SerializeField] private bool autoUpdatePosition;

    [Tooltip("Automatically rebuild the affected tiles of matching surfaces when this link changes (enable/disable, Activated, moves with Auto Update Position). Turn off in games that manage rebuilds themselves with explicit sources.")]
    [SerializeField] private bool autoRebuild = true;

    [Tooltip("Agent types whose bakes include this link.")]
    [SerializeField] private NavMeshAgentTypeSet agentTypes = new();

    // Writing any part of the definition re-offers the link and rebuilds around both its old and
    // new endpoints, so a spawn-then-configure write lands without waiting for a frame. Each one
    // no-ops on an unchanged value: an edit costs partial rebuilds, not a field assignment.
    public Float3 StartPoint { get => startPoint; set => SetDefinition(ref startPoint, value); }
    public Float3 EndPoint { get => endPoint; set => SetDefinition(ref endPoint, value); }
    public float Width { get => width; set => SetDefinition(ref width, value); }
    public bool Bidirectional { get => bidirectional; set => SetDefinition(ref bidirectional, value); }
    public NavMeshArea Area { get => area; set => SetDefinition(ref area, value); }

    /// <summary>Toggling this rebuilds the affected tiles in place.</summary>
    public bool Activated
    {
        get => activated;
        set
        {
            if (activated == value) return;
            activated = value;
            ApplyChange(edited: false);
        }
    }

    private void SetDefinition<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        ApplyChange(edited: true);
    }

    /// <summary>Which surfaces the link resolves against is part of its definition: narrowing the
    /// scope has to rebuild the surfaces it is coming off, which is why this applies like an edit.
    /// Edits made to the set in place are picked up on the next frame.</summary>
    public NavMeshAgentTypeSet AgentTypes
    {
        get => agentTypes;
        set
        {
            if (ReferenceEquals(agentTypes, value)) return;
            agentTypes = value ?? new();
            ApplyChange(edited: true);
        }
    }

    // LateUpdate and RequestRebuild read these when they run, so there is nothing to apply.
    public bool AutoUpdatePosition { get => autoUpdatePosition; set => autoUpdatePosition = value; }
    public bool AutoRebuild { get => autoRebuild; set => autoRebuild = value; }

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

    // The state the navmesh last saw. World endpoints size the rebuild regions and follow the
    // Transform, which no setter sees; the applied Activated flag is what tells OnDisable whether
    // this link was contributing anything worth rebuilding away.
    private Float3 _appliedStart, _appliedEnd;
    // The width sizes the rebuild regions too: a link that narrows still has crossings out at the old width.
    private float _appliedWidth;
    private bool _appliedActive;
    // Agent-type scoping decides which surfaces the link resolves against, so it is part of the
    // definition too. A copy rather than the live set, or an in-place edit would never compare as a change.
    private NavMeshAgentTypeSet _appliedAgentTypes = new();

    /// <summary>Everything except the scope (see <see cref="CaptureAppliedScope"/>).</summary>
    private void CaptureAppliedState()
    {
        _appliedStart = WorldStart;
        _appliedEnd = WorldEnd;
        _appliedWidth = Width;
        _appliedActive = Activated;
    }

    /// <summary>Committed separately from the rest, and only after a rebuild has run: narrowing
    /// the scope has to rebuild the surfaces the link is being taken OFF, and those are only
    /// identifiable from the previous snapshot.</summary>
    private void CaptureAppliedScope() => _appliedAgentTypes = agentTypes.Clone();

    /// The setters, an inspector edit and AutoUpdatePosition all land here: rebuild around the OLD
    /// endpoints so those tiles drop the stale connection, then around the new ones so they gain it.
    /// Tiles are deduplicated when the world drains them, so unchanged endpoints cost nothing extra.
    private void ApplyChange(bool edited)
    {
        Float3 oldStart = _appliedStart, oldEnd = _appliedEnd;
        float oldWidth = _appliedWidth;
        CaptureAppliedState();

        // An edited link has to be re-offered to every instance: catch-up only attempts each
        // one once, and the earlier attempt applied the old definition.
        if (edited) _catchUpDone.Clear();

        RequestRebuild(oldStart, oldEnd, oldWidth);
        RequestRebuild(_appliedStart, _appliedEnd, _appliedWidth);
        CaptureAppliedScope(); // both rebuilds have seen the outgoing scope
    }

    // The inspector writes the backing field, so a setter never sees an authored edit.
    public override void OnValidate()
    {
        if (GameObject.IsNotValid()) return; // WorldStart needs a Transform
        ApplyChange(edited: true);
    }

    private NavMeshWorld? _world;
    // Instances this link has already run its catch-up check against — each instance is
    // attempted at most once, which is what keeps the NavMeshChanged handler from looping
    // (our own catch-up rebuild fires the event again) and keeps permanently-unattachable
    // links (endpoints over void) from rebuilding on every navmesh event.
    private readonly HashSet<NavMeshInstance> _catchUpDone = [];

    /// <summary>Does this link apply to bakes for the given agent type?</summary>
    public bool AffectsAgentType(NavMeshAgentTypeId agentTypeId)
        => agentTypes.Contains(agentTypeId);

    /// <summary>World-space start position.</summary>
    public Float3 WorldStart => Transform.TransformPoint(StartPoint);

    /// <summary>World-space end position.</summary>
    public Float3 WorldEnd => Transform.TransformPoint(EndPoint);

    /// <summary>This link as a self-contained bake payload (world space, current state).</summary>
    public NavMeshLinkSource ToLinkSource()
        => new(WorldStart, WorldEnd, Width, Bidirectional, Area, LinkId);

    public override void OnEnable()
    {
        CaptureAppliedState();
        CaptureAppliedScope();

        // Catch-up must survive any enable order between links and surfaces: a navmesh may
        // already be live (runtime-spawned link), or may only register later (scene load
        // order), which the registration event covers.
        Scene? scene = Scene;
        if (scene.IsValid())
        {
            _world = scene.Navigation;
            _world.InstanceRegistered += OnInstanceRegistered;
            _world.InstanceUnregistered += OnInstanceUnregistered;
            _world.RegisterLink(this);
        }
        if (Activated) CatchUp();
    }

    public override void OnDisable()
    {
        _catchUpDone.Clear();
        if (_world != null)
        {
            _world.InstanceRegistered -= OnInstanceRegistered;
            _world.InstanceUnregistered -= OnInstanceUnregistered;
            _world.UnregisterLink(this);

            // Unregistered first, so the rebuild collects the links WITHOUT this one — but
            // before the world reference is released, since that's what reaches the surfaces.
            // Scene teardown costs nothing here: Scene.OnDispose clears the navigation world
            // before GameObjects dispose, so every instance is already retired and the rebuild
            // finds nothing to do; a gameplay disable (pooling, a destroyed building) keeps it.
            if (_appliedActive) RequestRebuild(_appliedStart, _appliedEnd, _appliedWidth);

            _world = null;
        }
    }

    private bool _catchUpPending;

    /// <summary>A navmesh registered: schedule the catch-up check so links baked out of date
    /// insert themselves regardless of component enable order. Deferred to LateUpdate because the
    /// event fires inside AddNavMeshData, before the registering surface has assigned its Instance.</summary>
    private void OnInstanceRegistered(NavMeshInstance instance) => _catchUpPending = true;

    private void OnInstanceUnregistered(NavMeshInstance instance) => _catchUpDone.Remove(instance);

    /// <summary>
    /// For each matching surface with a live navmesh this link hasn't checked yet: if the
    /// baked mesh already contains the link (id stamped at bake) do nothing — baked-in links
    /// cost nothing at scene load — otherwise rebuild the endpoint tiles to insert it.
    /// </summary>
    private void CatchUp()
    {
        // A Not Walkable link is never in the mesh, so looking for it would rebuild on every registration.
        if (!AutoRebuild || _world == null || Area == NavMeshAreas.NotWalkable) return;

        IReadOnlyList<NavMeshSurface> surfaces = _world.Surfaces;
        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            NavMeshInstance? instance = surface.Instance;
            if (instance == null || !AffectsAgentType(surface.AgentTypeId)) continue;
            if (!_catchUpDone.Add(instance)) continue;      // one attempt per instance
            if (instance.ContainsLinkId(LinkId)) continue;  // already in the live mesh
            _world.MarkLinkEndpointsDirty(surface, _appliedStart, _appliedEnd, Width);
        }
    }

    public override void LateUpdate()
    {
        if (_catchUpPending)
        {
            _catchUpPending = false;
            if (Activated) CatchUp();
        }

        bool edited = !agentTypes.SameAs(_appliedAgentTypes);
        bool moved = AutoUpdatePosition
            && (Float3.Distance(WorldStart, _appliedStart) > 0.01 || Float3.Distance(WorldEnd, _appliedEnd) > 0.01);
        if (edited || moved) ApplyChange(edited);
    }

    /// <summary>Dirty the tiles around both endpoints on every registered surface this link applies
    /// to now or applied to before. Collection filters by the current scope, so a surface in the
    /// second group rebuilds without the link, which is how it gets removed. The world applies the
    /// regions, so a frame that moves many links re-contours each tile once. No-op when
    /// <see cref="AutoRebuild"/> is off or no matching navmesh is live.</summary>
    private void RequestRebuild(Float3 start, Float3 end, float width)
    {
        if (!AutoRebuild || _world == null) return;

        IReadOnlyList<NavMeshSurface> surfaces = _world.Surfaces;
        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface.Instance == null) continue;
            if (!AffectsAgentType(surface.AgentTypeId) && !_appliedAgentTypes.Contains(surface.AgentTypeId)) continue;
            _world.MarkLinkEndpointsDirty(surface, start, end, width);
        }
    }

    /// <summary>
    /// Draws whatever the navmesh made of this link — the same connection the surface's overlay
    /// draws, in the same place, so the two agree wherever both are shown. A link that has not
    /// attached has nothing there to draw, so it falls back to the authored line in grey: that
    /// difference in colour is the only warning that it reached nothing walkable.
    /// </summary>
    public override void DrawGizmosSelected()
    {
        IReadOnlyList<NavMeshSurface> surfaces = _world?.Surfaces ?? [];
        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshInstance? instance = surfaces[i].Instance;
            if (instance == null || !AffectsAgentType(surfaces[i].AgentTypeId)) continue;
            if (!instance.TryGetConnection(LinkId, out NavMeshConnection connection)) continue;
            NavMeshDebugDisplay.DrawConnection(connection, Float3.Zero);
            return;
        }

        var color = new Color(0.6f, 0.6f, 0.6f, 1f);
        Debug.DrawLine(WorldStart, WorldEnd, color);
        Debug.DrawWireSphere(WorldStart, NavMeshDebugDisplay.EndpointGizmoRadius, color);
        Debug.DrawWireSphere(WorldEnd, NavMeshDebugDisplay.EndpointGizmoRadius, color);
    }
}
