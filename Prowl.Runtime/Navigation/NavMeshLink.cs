// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Connects two navmesh positions that are not walkably connected - a jump, a drop, a ladder. Rides
/// entirely on the query a surface builds (see <see cref="NavMeshQuery"/>'s off-mesh-connection
/// wiring): registering, moving or removing a link only ever rebuilds the affected surfaces' queries,
/// never touches a baked <see cref="NavMesh"/> asset. Registers and unregisters purely on
/// enable/disable, and never walks the scene to find the surfaces it affects - see
/// <see cref="NavMeshSystem"/>.
/// </summary>
[AddComponentMenu("Navigation/NavMesh Link")]
public sealed class NavMeshLink : MonoBehaviour
{
    /// <summary>First endpoint, local to this GameObject's transform.</summary>
    public Float3 StartPoint = new(0, 0, -1);

    /// <summary>Second endpoint, local to this GameObject's transform.</summary>
    public Float3 EndPoint = new(0, 0, 1);

    /// <summary>0 makes this a single crossing point. A wider link contributes two parallel
    /// connections, one along each edge, so an agent enters near wherever it actually is rather than
    /// queuing through one shared center point.</summary>
    public float Width;

    /// <summary>Whether the connection can be crossed in either direction, or only start-to-end.</summary>
    public bool Bidirectional = true;

    /// <summary>The navmesh area (see <see cref="NavMeshAreas"/>) this link belongs to. Its cost
    /// applies to crossing it, and an agent whose area mask excludes it won't be offered it as a route.</summary>
    public int AreaIndex = NavMeshAreas.Jump;

    /// <summary>Whether this link is currently usable. Disabling it removes it from affected surfaces'
    /// queries the same way disabling the whole component would.</summary>
    public bool Activated = true;

    /// <summary>Follow this GameObject's <see cref="Runtime.Transform"/> at runtime: <see cref="Update"/>
    /// checks every tick for real movement and, if <see cref="AutoRebuild"/> is also on, requests a
    /// rebuild automatically. Off by default - each move this catches pays a partial rebuild (only the
    /// affected surfaces' queries, never a full re-bake), so a link that never moves at runtime should
    /// leave this off rather than pay a wasted comparison every tick for nothing.</summary>
    public bool AutoUpdatePosition;

    /// <summary>Rebuild the affected tiles automatically when this link changes (enables, disables, or -
    /// with <see cref="AutoUpdatePosition"/> - moves). Turn off in games that drive rebuilds themselves
    /// with explicit geometry and batch several link changes into one rebuild via
    /// <see cref="RequestRebuild"/> instead of paying one per change.</summary>
    public bool AutoRebuild = true;

    /// <summary>How far this link's world-space endpoints have to move, in one <see cref="Update"/>
    /// tick's worth of travel, before <see cref="AutoUpdatePosition"/> considers it "moved" rather than
    /// merely jittering.</summary>
    private const float MoveThreshold = 0.05f;

    /// <summary>This link's world-space endpoints as of the last time <see cref="Update"/> checked them -
    /// only meaningful while <see cref="AutoUpdatePosition"/> is on.</summary>
    private Float3 _lastWorldStart;
    private Float3 _lastWorldEnd;

    /// <summary>When true, this link affects every agent type's surfaces. When false, only the types
    /// listed in <see cref="AgentTypeIds"/>.</summary>
    public bool AffectAllAgentTypes = true;

    /// <summary>Agent types this link affects when <see cref="AffectAllAgentTypes"/> is false.</summary>
    public List<int> AgentTypeIds = [];

    /// <summary>Raised when an agent crosses this link. Nothing in this file fires it: detecting "an
    /// agent's corridor just crossed this specific link" needs per-frame movement tracking, which is
    /// <see cref="NavMeshAgent"/>'s crowd-movement update, not this component. Call
    /// <see cref="NotifyTraversed"/> from there.</summary>
    public event Action<NavMeshAgent>? Traversed;

    /// <summary>Raises <see cref="Traversed"/>. Exists as a named entry point so movement code doesn't
    /// need to reach past this component's public surface to fire its own event.</summary>
    public void NotifyTraversed(NavMeshAgent agent) => Traversed?.Invoke(agent);

    /// <summary>Whether this link should be included when building a query for the given agent type.</summary>
    public bool AppliesTo(int agentTypeId) => Activated && (AffectAllAgentTypes || AgentTypeIds.Contains(agentTypeId));

    /// <summary>Current world-space endpoints.</summary>
    public (Float3 Start, Float3 End) WorldPoints => (Transform.TransformPoint(StartPoint), Transform.TransformPoint(EndPoint));

    // Kept generous but fixed rather than exposing yet another field: how close a path needs to pass
    // to an endpoint to use it matters far less than getting the endpoint positions right.
    private const float ConnectionRadius = 0.25f;

    /// <summary>The connection(s) this link contributes to a query build: two parallel connections,
    /// offset by half <see cref="Width"/> on each side, when <see cref="Width"/> is positive; one
    /// otherwise. Empty when <see cref="Activated"/> is false.</summary>
    public IEnumerable<NavMeshLinkData> ToLinkData()
    {
        if (!Activated) return [];

        (Float3 start, Float3 end) = WorldPoints;

        if (Width <= 0f)
            return [new NavMeshLinkData(start, end, ConnectionRadius, Bidirectional, AreaIndex)];

        Float3 span = end - start;
        float length = Float3.Length(span);
        Float3 lateral = length > 0.0001f
            ? Float3.Normalize(Float3.Cross(span, Float3.UnitY)) * (Width * 0.5f)
            : Float3.UnitX * (Width * 0.5f);

        return
        [
            new NavMeshLinkData(start - lateral, end - lateral, ConnectionRadius, Bidirectional, AreaIndex),
            new NavMeshLinkData(start + lateral, end + lateral, ConnectionRadius, Bidirectional, AreaIndex),
        ];
    }

    /// <summary>Registers this link with the scene's <see cref="NavMeshSystem"/> and, unless
    /// <see cref="AutoRebuild"/> is off, rebuilds every query it affects.</summary>
    public override void OnEnable()
    {
        (_lastWorldStart, _lastWorldEnd) = WorldPoints;

        if (Scene.IsValid())
            NavMeshSystem.GetOrCreate(Scene).RegisterLink(this);
    }

    /// <summary>Unregisters this link from the scene's <see cref="NavMeshSystem"/> and, unless
    /// <see cref="AutoRebuild"/> is off, rebuilds every query it used to affect.</summary>
    public override void OnDisable()
    {
        if (Scene.IsValid())
            NavMeshSystem.GetOrCreate(Scene).UnregisterLink(this);
    }

    /// <summary>Rebuilds the query of every surface this link could affect, right now - the manual
    /// counterpart to <see cref="AutoRebuild"/>, for a caller that turned it off and wants to batch
    /// several link changes into one rebuild instead of paying one per change.</summary>
    public void RequestRebuild()
    {
        if (Scene.IsValid())
            NavMeshSystem.GetOrCreate(Scene).RebuildQueriesAffectedBy(this);
    }

    /// <summary>While <see cref="AutoUpdatePosition"/> is on, checks this link's world-space endpoints
    /// for real movement and, if <see cref="AutoRebuild"/> is also on, requests a rebuild. A no-op
    /// otherwise - most links are static once placed and pay nothing here beyond the flag check.</summary>
    public override void Update()
    {
        if (!AutoUpdatePosition) return;

        (Float3 start, Float3 end) = WorldPoints;
        if (Float3.Distance(start, _lastWorldStart) < MoveThreshold && Float3.Distance(end, _lastWorldEnd) < MoveThreshold)
            return;

        _lastWorldStart = start;
        _lastWorldEnd = end;
        if (AutoRebuild) RequestRebuild();
    }

    /// <summary>Draws this link's connection(s) - green while <see cref="Activated"/>, gray otherwise.</summary>
    public override void DrawGizmos()
    {
        (Float3 start, Float3 end) = WorldPoints;
        Color color = Activated ? new Color(0.2f, 1f, 0.4f, 1f) : new Color(0.5f, 0.5f, 0.5f, 1f);

        if (Width <= 0f)
        {
            Debug.DrawLine(start, end, color);
            return;
        }

        foreach (NavMeshLinkData data in ToLinkData())
            Debug.DrawLine(data.Start, data.End, color);
    }
}
