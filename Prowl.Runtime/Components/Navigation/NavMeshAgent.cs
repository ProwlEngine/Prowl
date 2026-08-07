// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;
using Prowl.Recast.Detour.Crowd;

using Prowl.Vector;

namespace Prowl.Runtime;

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
/// Moves a character along the navmesh using crowd simulation: give it a
/// <see cref="Destination"/> (or call <see cref="SetDestination"/>) and it steers there,
/// avoiding other agents. Mirrors Unity's NavMeshAgent API. The agent joins the scene's crowd
/// when a navmesh for its <see cref="AgentTypeId"/> is available and writes its position back
/// to the Transform each LateUpdate (disable <see cref="UpdatePosition"/> to drive a
/// Rigidbody or CharacterController from <see cref="DesiredVelocity"/> yourself).
/// </summary>
[AddComponentMenu("Navigation/NavMesh Agent")]
[ComponentIcon("")] // Person Walking
public class NavMeshAgent : MonoBehaviour
{
    [Header("Agent")]
    [Tooltip("The agent type whose navmesh this agent walks on.")]
    [NavMeshAgentType]
    public int AgentTypeId = NavMeshAgentTypes.Humanoid;

    [Tooltip("Agent radius for avoidance and crowd separation.")]
    public float Radius = 0.5f;

    [Tooltip("Agent height (used by the crowd for vertical overlap checks).")]
    public float Height = 2.0f;

    [Tooltip("Vertical offset between the navmesh surface and the Transform position.")]
    public float BaseOffset = 0f;

    [Header("Steering")]
    [Tooltip("Maximum movement speed in world units/second.")]
    public float Speed = 3.5f;

    [Tooltip("Maximum turning speed in degrees/second, applied when UpdateRotation is on.")]
    public float AngularSpeed = 120f;

    [Tooltip("Maximum acceleration in world units/second².")]
    public float Acceleration = 8f;

    [Tooltip("Stop this far short of the destination.")]
    public float StoppingDistance = 0f;

    [Tooltip("Decelerate to a stop as the destination is approached instead of overshooting.")]
    public bool AutoBraking = true;

    [Header("Obstacle Avoidance")]
    [Tooltip("Avoidance quality: higher avoids more reliably and costs more CPU.")]
    [InspectorName("Quality")]
    public ObstacleAvoidanceType ObstacleAvoidanceQuality = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

    [Tooltip("Agents with lower priority values are avoided by agents with higher values (0 = most important, 99 = least). Mapped to crowd separation weight.")]
    [Range(0, 99)]
    public int AvoidancePriority = 50;

    [Tooltip("Push away from nearby agents (crowd separation). Disable when units should pack tightly or walk single file through corridors.")]
    public bool Separation = true;

    [Tooltip("How far steering scans for neighbours and navmesh borders, in world units. 0 derives it from the radius (radius x 12, the open-level default). On tile maps, ranges wider than the corridors keep the borders inside view in every direction and make avoidance oscillate - tune this down toward the corridor width.")]
    public float CollisionQueryRange = 0f;

    [Tooltip("Path visibility optimization range, in world units. 0 derives it from the radius (radius x 30).")]
    public float PathOptimizationRange = 0f;

    [Header("Pathfinding")]
    [Tooltip("Areas this agent may traverse.")]
    [NavMeshAreaMask]
    public int AreaMask = NavMeshAreas.AllAreas;

    [Tooltip("Automatically re-path when the navmesh changes under the current path.")]
    public bool AutoRepath = true;

    [Tooltip("Write the crowd position to the Transform each frame.")]
    public bool UpdatePosition = true;

    [Tooltip("Rotate the Transform to face the movement direction.")]
    public bool UpdateRotation = true;

    private NavMeshWorld? _world;
    private DtCrowdAgent? _agent;
    // The crowd _agent belongs to. Captured at registration because the world's crowd can be
    // replaced when the navmesh is swapped (rebake/regenerate) — a stale _agent must be
    // detached against ITS crowd, never the current one.
    private DtCrowd? _crowd;
    // The crowd entry _agent registered with, for filter-slot bookkeeping (same capture
    // rationale as _crowd).
    private NavMeshCrowdEntry? _crowdEntry;
    // The crowd filter slot this agent steers with, and the AreaMask baked into it — the mask
    // is re-checked each LateUpdate so writing the AreaMask field "just works" like Unity.
    private int _filterSlot;
    private int _slotAreaMask = NavMeshAreas.AllAreas;
    // The agent type this agent registered under, re-checked each LateUpdate so writing the
    // AgentTypeId field re-places the agent on its new type's crowd (Unity does the same).
    private int _registeredAgentTypeId;
    private NavMeshQueryFilter? _filter;
    private Float3 _destination;
    private bool _hasDestination;
    private bool _isStopped;
    private bool _arrived;

    // Corner-window distance below which a path counts as arrived when StoppingDistance is 0.
    private const float ArrivalEpsilon = 0.05f;

    /// <summary>The raw crowd agent, while registered. Advanced use.</summary>
    public DtCrowdAgent? NativeAgent => _agent;

    /// <summary>True while the agent is registered on a navmesh crowd.</summary>
    public bool IsOnNavMesh => _agent != null;

    /// <summary>The query filter this agent paths with (area mask + agent type). Mutating its
    /// costs directly (<c>agent.Filter.SetAreaCost(...)</c>) affects explicit queries only —
    /// crowd STEERING keeps the old cost table until the next refresh. Use
    /// <see cref="SetAreaCost"/> (or call <see cref="RefreshParams"/> after) to apply costs to
    /// both.</summary>
    public NavMeshQueryFilter Filter
    {
        get
        {
            _filter ??= new NavMeshQueryFilter();
            _filter.AreaMask = AreaMask;
            _filter.AgentTypeId = AgentTypeId;
            return _filter;
        }
    }

    #region Destination / movement state

    /// <summary>Set or get the movement target. Setting it requests a new path.</summary>
    public Float3 Destination
    {
        get => _hasDestination ? _destination : NextPosition;
        set => SetDestination(value);
    }

    /// <summary>True while a requested path is still being computed by the crowd's path queue.</summary>
    public bool PathPending => _agent != null &&
        _agent.targetState is DtMoveRequestState.DT_CROWDAGENT_TARGET_REQUESTING
            or DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_QUEUE
            or DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_PATH;

    /// <summary>True when the agent has a path it is following.</summary>
    public bool HasPath => _agent != null && _agent.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;

    /// <summary>True when the current path only reaches partway to the destination.</summary>
    public bool IsPathStale => _agent?.partial ?? false;

    /// <summary>Status of the current path.</summary>
    public NavMeshPathStatus PathStatus
    {
        get
        {
            if (_agent == null || _agent.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_FAILED)
                return NavMeshPathStatus.PathInvalid;
            return _agent.partial ? NavMeshPathStatus.PathPartial : NavMeshPathStatus.PathComplete;
        }
    }

    /// <summary>True while the agent is traversing an off-mesh link (the crowd animates the
    /// hop; traversal is always automatic).</summary>
    public bool IsOnOffMeshLink => _agent != null && _agent.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;

    /// <summary>The off-mesh traversal in progress (Valid false while walking normally).
    /// Resolves back to the <see cref="NavMeshLink"/> component via the id stamped at bake.</summary>
    public OffMeshLinkData CurrentOffMeshLinkData
    {
        get
        {
            if (!IsOnOffMeshLink || _agent!.animation == null || !_agent.animation.active)
                return default;
            DtCrowdAgentAnimation anim = _agent.animation;
            return new OffMeshLinkData(true, ToFloat3(anim.startPos), ToFloat3(anim.endPos), ResolveLink(anim.polyRef));
        }
    }

    /// <summary>The link component behind an off-mesh connection poly, via the user id
    /// stamped at bake time.</summary>
    private NavMeshLink? ResolveLink(long polyRef)
    {
        NavMeshInstance? instance = _world?.GetInstance(_registeredAgentTypeId);
        if (instance == null) return null;
        if (instance.NativeNavMesh.GetTileAndPolyByRef(polyRef, out DtMeshTile tile, out DtPoly poly).Failed())
            return null;
        var cons = tile?.data?.offMeshCons;
        if (cons == null) return null;
        foreach (DtOffMeshConnection con in cons)
            if (ReferenceEquals(tile!.data.polys[con.poly], poly))
                return NavMeshLink.FindByLinkId(con.userId);
        return null;
    }

    /// <summary>Current velocity of the agent in the crowd simulation.</summary>
    public Float3 Velocity => _agent != null ? ToFloat3(_agent.vel) : Float3.Zero;

    /// <summary>The velocity the agent wants (path steering before avoidance/acceleration limits).
    /// Drive a Rigidbody or CharacterController from this when <see cref="UpdatePosition"/> is off.</summary>
    public Float3 DesiredVelocity => _agent != null ? ToFloat3(_agent.dvel) : Float3.Zero;

    /// <summary>The agent's position in the crowd simulation (before <see cref="BaseOffset"/>).</summary>
    public Float3 NextPosition => _agent != null ? ToFloat3(_agent.npos) : Transform.Position;

    /// <summary>The next corner the agent is steering toward.</summary>
    public Float3 SteeringTarget => _agent != null && _agent.ncorners > 0 ? ToFloat3(_agent.corners[0].pos) : NextPosition;

    /// <summary>
    /// Distance to the end of the current path along its corners. Infinity while no path is
    /// available. When the path's visible corner window doesn't yet reach the destination this
    /// is a lower bound (matches Unity's remainingDistance semantics closely enough for
    /// arrival checks against <see cref="StoppingDistance"/>).
    /// </summary>
    public float RemainingDistance
    {
        get
        {
            if (_agent == null) return float.PositiveInfinity;
            if (_arrived) return 0f;
            if (!HasPath) return float.PositiveInfinity;
            // Mid-hop the corner window is empty and would read 0 — falsely "arrived" for the
            // Unity idiom. The honest lower bound is the remaining hop PLUS the path after
            // landing: the hop distance alone also collapses to ~0 as the animation lands,
            // which makes waypoint scripts issue their next destination mid-hop and ping-pong
            // the agent across the link forever.
            if (IsOnOffMeshLink && _agent.animation is { active: true } anim)
            {
                Float3 landing = ToFloat3(anim.endPos);
                return (float)(Float3.Distance(ToFloat3(_agent.npos), landing)
                    + Float3.Distance(landing, ToFloat3(_agent.targetPos)));
            }
            // An empty corner window is also transient right after a hop lands (corners not
            // recomputed until the next crowd update) — measure straight to the target rather
            // than trusting 0.
            if (_agent.ncorners == 0)
                return (float)Float3.Distance(ToFloat3(_agent.npos), ToFloat3(_agent.targetPos));
            return CornerWindowDistance();
        }
    }

    private float CornerWindowDistance()
    {
        if (_agent == null || _agent.ncorners == 0) return 0f;

        float total = 0f;
        RcVec3f prev = _agent.npos;
        for (int i = 0; i < _agent.ncorners; i++)
        {
            total += RcVec3f.Distance(prev, _agent.corners[i].pos);
            prev = _agent.corners[i].pos;
        }
        return total;
    }

    /// <summary>Stop (true) or resume (false) movement. Resuming re-requests the last
    /// destination. Matches Unity: <see cref="SetDestination"/> while stopped remembers the
    /// target but does NOT clear the stopped state — movement resumes only when this is set
    /// back to false.</summary>
    public bool IsStopped
    {
        get => _isStopped;
        set
        {
            if (_isStopped == value) return;
            _isStopped = value;
            if (_agent == null) return;

            if (value)
                _crowd?.ResetMoveTarget(_agent);
            else if (_hasDestination && !_arrived)
                RequestPathTo(_destination);
        }
    }

    #endregion

    #region Lifecycle

    public override void OnEnable()
    {
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsNotValid()) return;

        _world = scene!.Navigation;
        _world.NavMeshChanged += OnNavMeshChanged;
        TryRegister();
    }

    public override void OnDisable()
    {
        if (_world != null)
        {
            _world.NavMeshChanged -= OnNavMeshChanged;
            Unregister();
            _world = null;
        }
    }

    private void OnNavMeshChanged()
    {
        // Our crowd may have been dropped with its navmesh (rebake/regenerate); our crowd
        // agent and filter slot died with it, so forget both and fall through to
        // re-registration (which re-requests the remembered destination). MUST compare
        // against _registeredAgentTypeId, not the AgentTypeId field: if gameplay rewrote the
        // field and a navmesh event fires before the LateUpdate drift check runs, the
        // registered type's crowd is still alive and still contains our agent — forgetting it
        // here would strand a permanent ghost agent (and leak its filter-slot refcount).
        // Type changes are handled only by the drift check, which Unregisters properly.
        if (_agent != null && _world != null && !ReferenceEquals(_world.GetNativeCrowd(_registeredAgentTypeId), _crowd))
        {
            _agent = null;
            _crowd = null;
            _crowdEntry = null;
            _filterSlot = 0;
        }

        if (_agent == null)
        {
            // A navmesh may have just become available.
            TryRegister();
        }
        else if (AutoRepath && _hasDestination && !_isStopped && !_arrived)
        {
            // Ground moved under us: replan.
            RequestPathTo(_destination);
        }
    }

    private void TryRegister()
    {
        if (_agent != null || _world == null) return;

        NavMeshInstance? instance = _world.GetInstance(AgentTypeId);
        if (instance == null) return;

        if (Radius > _world.CrowdMaxAgentRadius)
            Debug.LogWarning($"[Navigation] Agent '{GameObject.Name}' radius {Radius:0.##} exceeds NavMeshWorld.CrowdMaxAgentRadius ({_world.CrowdMaxAgentRadius:0.##}); crowd proximity queries assume the smaller value. Raise CrowdMaxAgentRadius before the first agent registers.");

        NavMeshCrowdEntry entry = _world.EnsureCrowd(instance);
        _crowdEntry = entry;
        _registeredAgentTypeId = AgentTypeId;
        _slotAreaMask = AreaMask;
        _filterSlot = entry.AcquireFilterSlot(AreaMask, _filter?.CostOverrides, GameObject.Name);
        _agent = entry.Crowd.AddAgent(ToRc(Transform.Position - new Float3(0, BaseOffset, 0)), BuildAgentParams());
        _crowd = entry.Crowd;
        if (_hasDestination && !_isStopped && !_arrived)
            RequestPathTo(_destination);
    }

    private void Unregister()
    {
        if (_agent == null) return;
        _crowd?.RemoveAgent(_agent);
        // Releasing into an entry the world already dropped is a harmless no-op.
        _crowdEntry?.ReleaseFilterSlot(_filterSlot);
        _agent = null;
        _crowd = null;
        _crowdEntry = null;
        _filterSlot = 0;
    }

    private DtCrowdAgentParams BuildAgentParams()
    {
        int updateFlags = DtCrowdAgentUpdateFlags.DT_CROWD_ANTICIPATE_TURNS
            | DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_VIS
            | DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_TOPO;
        if (Separation)
            updateFlags |= DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION;
        if (ObstacleAvoidanceQuality != ObstacleAvoidanceType.NoObstacleAvoidance)
            updateFlags |= DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE;

        float radius = Math.Max(0.01f, Radius);
        return new DtCrowdAgentParams
        {
            radius = radius,
            height = Math.Max(0.01f, Height),
            maxAcceleration = Acceleration,
            maxSpeed = Speed,
            collisionQueryRange = CollisionQueryRange > 0f ? CollisionQueryRange : radius * 12f,
            pathOptimizationRange = PathOptimizationRange > 0f ? PathOptimizationRange : radius * 30f,
            updateFlags = updateFlags,
            obstacleAvoidanceType = Math.Max(0, (int)ObstacleAvoidanceQuality - 1),
            // Unity priority 0 (most important) pushes hardest; map to separation weight 0.5..3.
            separationWeight = 0.5f + 2.5f * (1f - AvoidancePriority / 99f),
            queryFilterType = _filterSlot,
            userData = this,
        };
    }

    /// <summary>Push current inspector values (speed, radius, avoidance, area mask/costs ...)
    /// into the live crowd agent. Called automatically on validate, on
    /// <see cref="SetAreaCost"/>, and when the <see cref="AreaMask"/> field changes; call
    /// manually after changing other fields from code mid-simulation.</summary>
    public void RefreshParams()
    {
        if (_agent == null || _crowd == null) return;

        // Re-derive the steering filter slot: release-then-acquire, so a config only this
        // agent used frees its slot before (typically) being retaken with the new values.
        if (_crowdEntry != null)
        {
            _crowdEntry.ReleaseFilterSlot(_filterSlot);
            _slotAreaMask = AreaMask;
            _filterSlot = _crowdEntry.AcquireFilterSlot(AreaMask, _filter?.CostOverrides, GameObject.Name);
        }

        _crowd.UpdateAgentParameters(_agent, BuildAgentParams());
    }

    public override void OnValidate() => RefreshParams();

    #endregion

    #region Commands

    /// <summary>
    /// Override the path cost of an area for THIS agent (explicit queries and crowd steering
    /// both). Clamped to >= 1 — see <see cref="NavMeshQueryFilter.SetAreaCost"/>; to prefer an
    /// area, raise the other areas' costs instead. Unity API parity.
    /// </summary>
    public void SetAreaCost(int areaIndex, float cost)
    {
        Filter.SetAreaCost(areaIndex, cost);
        RefreshParams(); // re-derive the crowd filter slot with the new cost table
    }

    /// <summary>The path cost this agent pays in an area: its own override, or the project
    /// default.</summary>
    public float GetAreaCost(int areaIndex) => Filter.GetAreaCost(areaIndex);

    /// <summary>Request a path to <paramref name="target"/>. Returns false when the agent is
    /// not on a navmesh or the target cannot be mapped onto it. A stopped agent
    /// (<see cref="IsStopped"/>) remembers the destination but stays halted until resumed —
    /// Unity semantics, where isStopped is a pause flag that survives new destinations.</summary>
    public bool SetDestination(Float3 target)
    {
        _destination = target;
        _hasDestination = true;
        _arrived = false;
        if (_agent == null || _isStopped) return false; // remembered; requested on registration/resume
        return RequestPathTo(target);
    }

    private bool RequestPathTo(Float3 target)
    {
        if (_agent == null || _world == null) return false;
        DtCrowd? crowd = _crowd;
        if (crowd == null) return false;

        if (!_world.TryRentQuery(out NavMeshQueryLease lease, AgentTypeId)) return false;
        using (lease)
        {
            lease.Query.FindNearestPoly(ToRc(target), crowd.GetQueryExtents(), Filter, out long polyRef, out RcVec3f nearest, out _);
            if (polyRef == 0) return false;
            return crowd.RequestMoveTarget(_agent, polyRef, nearest);
        }
    }

    /// <summary>Follow a pre-calculated path by steering to its end (the crowd re-plans the
    /// corridor itself; the path supplies the destination).</summary>
    public bool SetPath(NavMeshPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Status == NavMeshPathStatus.PathInvalid || path.CornerCount == 0) return false;
        Float3[] corners = path.Corners;
        return SetDestination(corners[^1]);
    }

    /// <summary>Clear the current path and destination without unregistering.</summary>
    public void ResetPath()
    {
        _hasDestination = false;
        _arrived = false;
        if (_agent != null)
            _crowd?.ResetMoveTarget(_agent);
    }

    /// <summary>Teleport the agent (and Transform) to a position on the navmesh. Keeps the
    /// current destination.</summary>
    public bool Warp(Float3 newPosition)
    {
        Transform.Position = newPosition + new Float3(0, BaseOffset, 0);
        if (_world == null) return false;

        DtCrowd? crowd = _crowd;
        if (_agent == null || crowd == null) return IsOnNavMesh;

        // Detour has no teleport: re-add the agent at the new position.
        crowd.RemoveAgent(_agent);
        _agent = crowd.AddAgent(ToRc(newPosition), BuildAgentParams());
        if (_hasDestination && !_isStopped && !_arrived)
            RequestPathTo(_destination);
        return true;
    }

    /// <summary>Displace the agent by a world-space offset, constrained to the navmesh.</summary>
    public void Move(Float3 offset) => Warp(NextPosition + offset);

    /// <summary>Calculate a path from the agent's position with the agent's filter, without
    /// moving the agent.</summary>
    public bool CalculatePath(Float3 targetPosition, NavMeshPath path)
        => _world?.CalculatePath(NextPosition, targetPosition, Filter, path) ?? false;

    /// <summary>Navmesh raycast from the agent's position with the agent's filter.</summary>
    public bool Raycast(Float3 targetPosition, out NavMeshHit hit)
    {
        if (_world != null) return _world.Raycast(NextPosition, targetPosition, out hit, Filter);
        hit = default;
        return false;
    }

    /// <summary>Closest navmesh edge from the agent's position with the agent's filter.</summary>
    public bool FindClosestEdge(out NavMeshHit hit)
    {
        if (_world != null) return _world.FindClosestEdge(NextPosition, out hit, Filter);
        hit = default;
        return false;
    }

    /// <summary>Sample a position on the navmesh near the agent with the agent's filter.</summary>
    public bool SamplePathPosition(Float3 sourcePosition, float maxDistance, out NavMeshHit hit)
    {
        if (_world != null) return _world.SamplePosition(sourcePosition, out hit, maxDistance, Filter);
        hit = default;
        return false;
    }

    #endregion

    public override void LateUpdate()
    {
        if (_agent == null)
        {
            // Deliberate belt-and-braces: NavMeshChanged already covers late registration, but
            // a subscription can be lost across domain edge cases (component re-enable racing a
            // world swap), and this retry is nearly free while unregistered.
            TryRegister();
            return;
        }

        // Public fields can be written directly (Unity-style), so cheap per-frame int compares
        // keep the crowd in sync without requiring RefreshParams calls: a changed AgentTypeId
        // re-places the agent on its new type's crowd (keeping its destination), a changed
        // AreaMask re-derives the steering filter slot. Cost overrides go through
        // SetAreaCost, which refreshes itself.
        if (AgentTypeId != _registeredAgentTypeId)
        {
            Unregister();
            TryRegister();
            if (_agent == null) return;
        }
        if (AreaMask != _slotAreaMask)
            RefreshParams();

        // Velocity-obstacle sampling is the expensive half of a crowd step, and it picks from a
        // DISCRETE set of candidate velocities: with nothing in range to dodge, the winner is
        // merely the sample nearest the velocity we asked for, and that rounding walks the agent
        // a few centimetres sideways off a straight line by the time it arrives. An agent with
        // no neighbours has nothing to avoid, so let it steer exactly — and skip the sampling.
        // Neighbours come from the last crowd step, so engaging avoidance lags by one frame;
        // they are gathered from several metres out, which is many frames of approach.
        if (ObstacleAvoidanceQuality != ObstacleAvoidanceType.NoObstacleAvoidance)
        {
            _agent.option.updateFlags = _agent.nneis > 0
                ? _agent.option.updateFlags | DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE
                : _agent.option.updateFlags & ~DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE;
        }

        if (UpdatePosition)
            Transform.Position = ToFloat3(_agent.npos) + new Float3(0, BaseOffset, 0);

        if (UpdateRotation)
        {
            // Face where the agent STEERS, not where it moves: the actual velocity carries
            // avoidance corrections that do not shrink with speed, so braking into a goal they
            // take over its direction and the agent shivers along a dead straight path. The two
            // gates below stop the heading chasing a vector that has stopped meaning anything —
            // one too slow to have a direction, one pointing at a target already underfoot,
            // where what is left is drift and following it spins the agent on the spot.
            const double MinFacingSpeedSq = 0.01; // 0.1 m/s
            Float3 face = ToFloat3(_agent.dvel);
            double speedSq = face.X * face.X + face.Z * face.Z;
            if (speedSq < MinFacingSpeedSq)
            {
                // Off-mesh hops: the crowd empties the steering vector and animates the agent
                // across, so the actual velocity is the only heading available — and during a
                // hop it is a clean straight line, with no avoidance running.
                face = ToFloat3(_agent.vel);
                speedSq = face.X * face.X + face.Z * face.Z;
            }
            // RemainingDistance walks the corner window, so only ask once the cheap gate passed.
            if (speedSq > MinFacingSpeedSq && !(HasPath && !PathPending && RemainingDistance <= Radius))
            {
                // Quaternion rotate-towards, no Euler round-trip: Quaternion.FromEuler is in
                // degrees while Maths.DeltaAngle wraps in radians — never mix the two.
                float targetYaw = MathF.Atan2((float)face.X, (float)face.Z) * Maths.Rad2Deg;
                Quaternion targetRotation = Quaternion.FromEuler(new Float3(0, targetYaw, 0));
                Transform.Rotation = RotateTowards(Transform.Rotation, targetRotation, AngularSpeed * Time.DeltaTime);
            }
        }

        UpdateArrival();
    }

    /// <summary>
    /// Arrival detection, independent of how the agent approaches (braking is HOW it arrives,
    /// this is WHETHER it has): once the corner window closes to within the stopping distance
    /// (or the agent has braked to a stop inside its own radius of the goal), the move target
    /// is released and <see cref="RemainingDistance"/> reads exactly 0, so the Unity-style
    /// "!PathPending &amp;&amp; RemainingDistance &lt;= StoppingDistance" idiom terminates.
    /// </summary>
    private void UpdateArrival()
    {
        // IsOnOffMeshLink: the crowd empties the corner window during a hop, which reads as
        // "corridor consumed" below — latching there resets the move target mid-traversal and
        // strands the agent at the link mouth.
        if (_agent == null || _arrived || _isStopped || !HasPath || PathPending || IsOnOffMeshLink) return;

        // The corner window is a LOWER bound (it holds at most the crowd's few visible
        // corners), so its distance only means "arrived" when the window actually reaches the
        // path end. Without this gate, a tight switchback whose visible corners total less
        // than StoppingDistance would falsely latch mid-path, and a congestion-jammed agent
        // could trip the braked latch far from its goal. An EMPTY window is untrustworthy:
        // it happens both when standing on the target and transiently right after an
        // off-mesh hop lands (corners recompute on the next crowd update) — so measure
        // straight to the target instead of trusting a 0-length window.
        float remaining;
        if (_agent.ncorners == 0)
        {
            remaining = (float)Float3.Distance(ToFloat3(_agent.npos), ToFloat3(_agent.targetPos));
        }
        else
        {
            if ((_agent.corners[_agent.ncorners - 1].flags & DtStraightPathFlags.DT_STRAIGHTPATH_END) == 0)
                return;
            remaining = CornerWindowDistance();
        }
        float threshold = MathF.Max(ArrivalEpsilon, StoppingDistance);

        Float3 vel = ToFloat3(_agent.vel);
        float horizontalSpeed = MathF.Sqrt((float)(vel.X * vel.X + vel.Z * vel.Z));
        // Auto-braking converges asymptotically, so also latch when the agent has effectively
        // stopped within its own radius of the (visible) goal.
        bool braked = remaining <= MathF.Max(0.1f, Radius) && horizontalSpeed <= 0.05f * MathF.Max(0.01f, Speed);

        if (remaining <= threshold || braked)
        {
            // _hasDestination stays true: Unity keeps agent.destination readable after
            // arrival, and migrated code does read it. _arrived gates every re-path site.
            _arrived = true;
            _crowd?.ResetMoveTarget(_agent);
        }
    }

    private static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegrees)
    {
        float dot = Math.Clamp(MathF.Abs(Quaternion.Dot(from, to)), 0f, 1f);
        float angleDeg = 2f * MathF.Acos(dot) * Maths.Rad2Deg;
        if (angleDeg <= maxDegrees || angleDeg < 1e-4f) return to;
        return Quaternion.Slerp(from, to, maxDegrees / angleDeg);
    }

    /// <summary>
    /// The agent's steering envelope: the crowd treats an agent as an upright cylinder of
    /// <see cref="Radius"/> x <see cref="Height"/> standing on the navmesh, so that is what is
    /// drawn — sized and placed exactly as the simulation sees it, including
    /// <see cref="BaseOffset"/>. Drawn unselected (like colliders) so a whole crowd's footprints
    /// are visible while tuning.
    /// </summary>
    public override void DrawGizmos()
    {
        float radius = MathF.Max(0.01f, Radius);
        float height = MathF.Max(0.01f, Height);

        // BaseOffset is the gap between the Transform and the surface the agent stands on, so
        // the cylinder's base sits that far below the Transform and rises by Height.
        Float3 basePos = Transform.Position - new Float3(0, BaseOffset, 0);
        Float3 center = basePos + new Float3(0, height * 0.5f, 0);

        var color = new Color(0f, 0.85f, 1f, 1f);
        Debug.DrawWireCylinder(center, Quaternion.Identity, radius, height, color);
        // Base ring, so the footprint reads clearly against the ground.
        Debug.DrawWireCircle(basePos, Float3.UnitY, radius, color);
    }

    /// <summary>
    /// The route the agent is currently steering along, plus its destination — drawn from the
    /// crowd's own corner list, so it shows what the simulation is actually following rather
    /// than a re-planned guess. Only meaningful while a crowd is running (in the editor an
    /// unregistered agent has no path), which matches Unity.
    /// </summary>
    public override void DrawGizmosSelected()
    {
        var pathColor = new Color(0.2f, 1f, 0.45f, 1f);
        var lift = new Float3(0, 0.05f, 0); // clear of the surface so it isn't z-fought away

        if (_hasDestination)
        {
            Float3 destination = _destination + lift;
            Debug.DrawWireSphere(destination, MathF.Max(0.05f, Radius * 0.35f), pathColor);
        }

        if (_agent == null) return;

        // Mid-hop across an off-mesh link the crowd empties the corner window, so draw the hop
        // itself — otherwise the route appears to vanish exactly when it is most interesting.
        if (IsOnOffMeshLink)
        {
            OffMeshLinkData hop = CurrentOffMeshLinkData;
            if (hop.Valid)
                Debug.DrawLine(hop.StartPos + lift, hop.EndPos + lift, new Color(1f, 0.8f, 0.2f, 1f));
            return;
        }

        // The crowd exposes a WINDOW of upcoming corners, not the whole route, so this is the
        // planned path as far as the simulation currently sees it.
        Float3 previous = NextPosition + lift;
        for (int i = 0; i < _agent.ncorners; i++)
        {
            Float3 corner = ToFloat3(_agent.corners[i].pos) + lift;
            Debug.DrawLine(previous, corner, pathColor);
            previous = corner;
        }
    }

    private static RcVec3f ToRc(Float3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
    private static Float3 ToFloat3(RcVec3f v) => new(v.X, v.Y, v.Z);
}
