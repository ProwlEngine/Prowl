// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;

using Prowl.Echo;
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
    [SerializeField] private int agentTypeId = NavMeshAgentTypes.Humanoid;

    [Tooltip("Agent radius for avoidance and crowd separation.")]
    [SerializeField] private float radius = 0.5f;

    [Tooltip("Agent height (used by the crowd for vertical overlap checks).")]
    [SerializeField] private float height = 2.0f;

    [Tooltip("Vertical offset between the navmesh surface and the Transform position. The surface itself sits above the ground it was baked from - one voxel height on flat ground, up to two on uneven ground with height detail on - so this is where a visual is pulled back down onto it.")]
    [SerializeField] private float baseOffset = 0f;

    [Header("Steering")]
    [Tooltip("Maximum movement speed in world units/second.")]
    [SerializeField] private float speed = 3.5f;

    [Tooltip("Maximum turning speed in degrees/second, applied when UpdateRotation is on.")]
    [SerializeField] private float angularSpeed = 120f;

    [Tooltip("Maximum acceleration in world units/second².")]
    [SerializeField] private float acceleration = 8f;

    [Tooltip("Stop this far short of the destination.")]
    [SerializeField] private float stoppingDistance = 0f;

    [Tooltip("Decelerate to a stop as the destination is approached instead of overshooting.")]
    [SerializeField] private bool autoBraking = true;

    [Header("Obstacle Avoidance")]
    [Tooltip("Avoidance quality: higher avoids more reliably and costs more CPU.")]
    [InspectorName("Quality")]
    [SerializeField] private ObstacleAvoidanceType obstacleAvoidanceQuality = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

    [Tooltip("Agents with lower priority values are avoided by agents with higher values (0 = most important, 99 = least). Mapped to crowd separation weight.")]
    [Range(0, 99)]
    [SerializeField] private int avoidancePriority = 50;

    [Tooltip("Push away from nearby agents (crowd separation). Disable when units should pack tightly or walk single file through corridors.")]
    [SerializeField] private bool separation = true;

    [Tooltip("How far steering scans for neighbours and navmesh borders, in world units. 0 derives it from the radius (radius x 12, the open-level default). On tile maps, ranges wider than the corridors keep the borders inside view in every direction and make avoidance oscillate - tune this down toward the corridor width.")]
    [SerializeField] private float collisionQueryRange = 0f;

    [Tooltip("Path visibility optimization range, in world units. 0 derives it from the radius (radius x 30).")]
    [SerializeField] private float pathOptimizationRange = 0f;

    [Header("Pathfinding")]
    [Tooltip("Areas this agent may traverse.")]
    [NavMeshAreaMask]
    [SerializeField] private int areaMask = NavMeshAreas.AllAreas;

    [Tooltip("Automatically re-path when the navmesh changes under the current path.")]
    [SerializeField] private bool autoRepath = true;

    [Tooltip("Write the crowd position to the Transform each frame.")]
    [SerializeField] private bool updatePosition = true;

    [Tooltip("Rotate the Transform to face the movement direction.")]
    [SerializeField] private bool updateRotation = true;

    /// <summary>Writing this re-places the agent on its new type's crowd, keeping its destination.
    /// One agent type per navmesh, so this is a move between crowds rather than a parameter.</summary>
    public int AgentTypeId
    {
        get => agentTypeId;
        set
        {
            if (agentTypeId == value) return;
            agentTypeId = value;
            if (_agent != null) Unregister();
            TryRegister(); // the new type may have a navmesh where the old one had none
        }
    }

    // The nine members below feed the crowd agent's parameters, so writing one pushes it straight
    // into the live agent; AreaMask additionally re-derives the steering filter slot, which
    // RefreshParams does anyway. Unchanged values are skipped: a refresh releases and retakes a
    // filter slot.
    public float Radius
    {
        get => radius;
        set { if (radius == value) return; radius = value; RefreshParams(); }
    }

    public float Height
    {
        get => height;
        set { if (height == value) return; height = value; RefreshParams(); }
    }

    public float Speed
    {
        get => speed;
        set { if (speed == value) return; speed = value; RefreshParams(); }
    }

    public float Acceleration
    {
        get => acceleration;
        set { if (acceleration == value) return; acceleration = value; RefreshParams(); }
    }

    public ObstacleAvoidanceType ObstacleAvoidanceQuality
    {
        get => obstacleAvoidanceQuality;
        set { if (obstacleAvoidanceQuality == value) return; obstacleAvoidanceQuality = value; RefreshParams(); }
    }

    public int AvoidancePriority
    {
        get => avoidancePriority;
        set { if (avoidancePriority == value) return; avoidancePriority = value; RefreshParams(); }
    }

    public bool Separation
    {
        get => separation;
        set { if (separation == value) return; separation = value; RefreshParams(); }
    }

    public float CollisionQueryRange
    {
        get => collisionQueryRange;
        set { if (collisionQueryRange == value) return; collisionQueryRange = value; RefreshParams(); }
    }

    public float PathOptimizationRange
    {
        get => pathOptimizationRange;
        set { if (pathOptimizationRange == value) return; pathOptimizationRange = value; RefreshParams(); }
    }

    public int AreaMask
    {
        get => areaMask;
        set { if (areaMask == value) return; areaMask = value; RefreshParams(); }
    }

    // Read where they are used, every frame or on the event that needs them, so there is nothing
    // for a setter to apply.
    public float BaseOffset { get => baseOffset; set => baseOffset = value; }
    public float AngularSpeed { get => angularSpeed; set => angularSpeed = value; }
    public float StoppingDistance { get => stoppingDistance; set => stoppingDistance = value; }
    public bool AutoBraking { get => autoBraking; set => autoBraking = value; }
    public bool AutoRepath { get => autoRepath; set => autoRepath = value; }
    public bool UpdatePosition { get => updatePosition; set => updatePosition = value; }
    public bool UpdateRotation { get => updateRotation; set => updateRotation = value; }

    private NavMeshWorld? _world;
    private DtCrowdAgent? _agent;
    // The crowd _agent belongs to. Captured at registration because the world's crowd can be
    // replaced when the navmesh is swapped (rebake/regenerate) — a stale _agent must be
    // detached against ITS crowd, never the current one.
    private DtCrowd? _crowd;
    // The crowd entry _agent registered with, for filter-slot bookkeeping (same capture
    // rationale as _crowd).
    private NavMeshCrowdEntry? _crowdEntry;
    // The crowd filter slot this agent steers with.
    private int _filterSlot;
    // The agent type this agent registered under: what a stale agent must be detached against,
    // which is not necessarily the AgentTypeId gameplay has since written.
    private int _registeredAgentTypeId;
    private NavMeshQueryFilter? _filter;
    private Float3 _destination;
    private bool _hasDestination;
    private bool _isStopped;
    private bool _arrived;

    /// <summary>Whether obstacle avoidance is currently switched on for this agent. Starts on, so
    /// a freshly added agent avoids until a crowd step proves there is nothing in range.</summary>
    internal bool AvoidanceEngaged = true;

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

    /// <summary>Status of the current path.</summary>
    public NavMeshPathStatus PathStatus
    {
        get
        {
            if (_agent == null || _agent.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_FAILED)
                return NavMeshPathStatus.PathInvalid;

            // Nothing planned and nothing pending is not a path — except after arrival, where
            // the latch clears the move target and Unity still reports the completed path.
            if (!HasPath && !PathPending)
                return _arrived ? NavMeshPathStatus.PathComplete : NavMeshPathStatus.PathInvalid;

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
                return _world!.FindLink(con.userId);
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
            // Unity idiom. The honest lower bound is remaining hop distance PLUS the path after
            // landing: the hop distance alone collapses to ~0 as the animation lands, which
            // would make waypoint scripts issue their next destination mid-hop and ping-pong.
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

    /// <summary>Stop (true) or resume (false) movement. The path is kept while stopped, so
    /// resuming carries on along it rather than replanning. Matches Unity:
    /// <see cref="SetDestination"/> while stopped plans the route but does NOT clear the stopped
    /// state — movement resumes only when this is set back to false.
    /// <para/>
    /// Two things a stopped agent still does: an off-mesh hop already under way finishes (the
    /// crowd animates it to the far side rather than stranding it mid-air), and a corridor that
    /// cannot reach its target is re-planned about once a second until it can.</summary>
    public bool IsStopped
    {
        get => _isStopped;
        set
        {
            if (_isStopped == value) return;
            _isStopped = value;
            if (_agent == null) return;

            // Halting by capping speed rather than dropping the target: clearing it would throw
            // the corridor away, and resuming would then have to replan a path the agent was
            // already standing on.
            if (value)
            {
                _agent.vel = default;
                _agent.nvel = default;
                _agent.dvel = default;
            }

            RefreshParams();
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
        _world.NavMeshSettled += OnNavMeshSettled;
        TryRegister();
    }

    public override void OnDisable()
    {
        if (_world != null)
        {
            _world.NavMeshChanged -= OnNavMeshChanged;
            _world.NavMeshSettled -= OnNavMeshSettled;
            Unregister();
            _world = null;
        }
    }

    private void OnNavMeshChanged()
    {
        // Our crowd may have been dropped with its navmesh (rebake/regenerate); our crowd agent
        // and filter slot died with it, so forget both and fall through to re-registration
        // (which re-requests the remembered destination). MUST compare against
        // _registeredAgentTypeId, not AgentTypeId: the inspector writes the backing field, so the
        // two diverge until OnValidate runs, and the registered type's crowd is still alive and
        // still holds our agent — forgetting it here would strand a ghost agent and leak its
        // filter-slot refcount. Moving between types is the AgentTypeId setter's job.
        if (_agent != null && _world != null && !ReferenceEquals(_world.GetNativeCrowd(_registeredAgentTypeId), _crowd))
        {
            _agent = null;
            _crowd = null;
            _crowdEntry = null;
            _filterSlot = 0;
        }

        if (_agent == null)
        {
            // A navmesh may have just become available. Re-registering re-requests the
            // destination, so there is no replan to do here.
            TryRegister();
        }
    }

    /// <summary>The ground stopped moving: replan once. Carves span several frames and report a
    /// change on each, so replanning from that would throw the path away every frame of one.</summary>
    private void OnNavMeshSettled()
    {
        if (_agent != null && AutoRepath && _hasDestination && !_arrived)
            RequestPathTo(_destination);
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
        _filterSlot = entry.AcquireFilterSlot(AreaMask, _filter?.CostOverrides, GameObject.Name);
        _agent = entry.Crowd.AddAgent(ToRc(Transform.Position - new Float3(0, BaseOffset, 0)), BuildAgentParams());
        _crowd = entry.Crowd;
        if (_hasDestination && !_arrived)
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
        if (ObstacleAvoidanceQuality != ObstacleAvoidanceType.NoObstacleAvoidance && AvoidanceEngaged)
            updateFlags |= DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE;

        float radius = Math.Max(0.01f, Radius);
        return new DtCrowdAgentParams
        {
            radius = radius,
            height = Math.Max(0.01f, Height),
            // Real even while stopped: Integrate clamps the velocity change to
            // maxAcceleration * dt, so a zero here would freeze whatever velocity the agent had
            // and it would glide on at that speed.
            maxAcceleration = Acceleration,
            // Every rebuild of the params goes through here, so a stopped agent stays stopped
            // across an avoidance toggle, a filter change or an inspector edit.
            maxSpeed = _isStopped ? 0f : Math.Max(0f, Speed),
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

    /// <summary>Push the steering properties (speed, radius, avoidance, area mask and costs) into
    /// the live crowd agent. Every setter that feeds them calls this, as do an inspector edit and
    /// <see cref="SetAreaCost"/>, so it is only needed by hand after mutating the cost table
    /// through <see cref="Filter"/> directly.</summary>
    public void RefreshParams()
    {
        if (_agent == null || _crowd == null) return;

        // Re-derive the steering filter slot: release-then-acquire, so a config only this
        // agent used frees its slot before (typically) being retaken with the new values.
        if (_crowdEntry != null)
        {
            _crowdEntry.ReleaseFilterSlot(_filterSlot);
            _filterSlot = _crowdEntry.AcquireFilterSlot(AreaMask, _filter?.CostOverrides, GameObject.Name);
        }

        _crowd.UpdateAgentParameters(_agent, BuildAgentParams());
    }

    // The inspector writes the backing field, so a setter never sees an authored edit — and a type
    // change is a move between crowds rather than a parameter, which RefreshParams cannot do.
    public override void OnValidate()
    {
        if (_agent != null && agentTypeId != _registeredAgentTypeId) Unregister();
        if (_agent == null) TryRegister();
        RefreshParams();
    }

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
    /// (<see cref="IsStopped"/>) plans the route but stays halted until resumed — Unity
    /// semantics, where isStopped is a pause flag that survives new destinations.</summary>
    public bool SetDestination(Float3 target)
    {
        _destination = target;
        _hasDestination = true;
        _arrived = false;
        if (_agent == null) return false; // remembered; requested on registration
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

    /// <summary>
    /// Follow a pre-calculated path, steering along the route it describes rather than re-planning
    /// one to its endpoint. The path must come from <see cref="CalculatePath"/> (or
    /// <see cref="NavMeshWorld.CalculatePath(Float3, Float3, int, NavMeshPath)"/>) and start where
    /// the agent is standing; the crowd still re-plans later if the navmesh invalidates it.
    /// </summary>
    /// <returns>False if the path is unusable, or does not begin at the agent's current polygon.</returns>
    public bool SetPath(NavMeshPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Status == NavMeshPathStatus.PathInvalid || path.CornerCount == 0) return false;
        if (_agent == null || _crowd == null) return false;

        Span<long> polys = path.Polys;
        if (polys.Length == 0) return false;

        // The corridor must continue from where the agent stands, not teleport to wherever the
        // path was computed from.
        if (polys[0] != _agent.corridor.GetFirstPoly()) return false;

        Float3 destination = path.LastCorner;
        bool partial = path.Status == NavMeshPathStatus.PathPartial;
        if (!_crowd.SetAgentPath(_agent, polys[^1], ToRc(destination), polys, polys.Length, partial))
            return false;

        _destination = destination;
        _hasDestination = true;
        _arrived = false;
        return true;
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
        DtCrowd? crowd = _crowd;
        if (_world == null || _agent == null || crowd == null)
        {
            // The Transform moves, but with no crowd agent nothing snapped it to the mesh and
            // there is no one to ask whether it landed on any.
            Transform.Position = newPosition + new Float3(0, BaseOffset, 0);
            return false;
        }

        // Warping keeps the DtCrowdAgent, so anything holding NativeAgent stays valid. The
        // fallback covers the warp refusing for a reason re-adding can fix — a stale agent the
        // crowd no longer owns — not a target off the navmesh, which defeats both equally.
        if (!crowd.WarpAgent(_agent, ToRc(newPosition)))
        {
            crowd.RemoveAgent(_agent);
            _agent = crowd.AddAgent(ToRc(newPosition), BuildAgentParams());
            if (_agent.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_INVALID)
                return false; // nothing to snap to; the agent sits where it was put, off the mesh
        }

        // A teleport is a fresh approach, so a previous arrival would otherwise park the agent
        // wherever it landed.
        _arrived = false;
        if (_hasDestination)
            RequestPathTo(_destination);

        // Use the position the crowd snapped to: the requested one can be off the mesh.
        Transform.Position = ToFloat3(_agent.npos) + new Float3(0, BaseOffset, 0);
        return true;
    }

    /// <summary>
    /// Displace the agent by a world-space offset, constrained to the navmesh — the per-frame API
    /// for driving an agent yourself. Slides the path corridor along, keeping the path, boundary
    /// cache and neighbour set; use <see cref="Warp"/> to jump somewhere unrelated, which rebuilds
    /// all of that.
    /// </summary>
    public void Move(Float3 offset)
    {
        if (_agent == null || _world == null) return;
        if (!_world.TryRentQuery(out NavMeshQueryLease lease, AgentTypeId)) return;

        using (lease)
        {
            RcVec3f target = ToRc(NextPosition + offset);
            _agent.corridor.MovePosition(target, lease.Query, Filter);
            _agent.npos = _agent.corridor.GetPos();
        }

        if (UpdatePosition)
            Transform.Position = ToFloat3(_agent.npos) + new Float3(0, BaseOffset, 0);
    }

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

    /// <summary>Find the closest navmesh point within <paramref name="maxDistance"/> of a
    /// position, using the agent's filter.</summary>
    public bool SamplePosition(Float3 sourcePosition, float maxDistance, out NavMeshHit hit)
    {
        if (_world != null) return _world.SamplePosition(sourcePosition, out hit, maxDistance, Filter);
        hit = default;
        return false;
    }

    /// <summary>
    /// How far along its current path the agent could get, without moving it: walks the path from
    /// where the agent stands and stops at <paramref name="maxDistance"/>, at the end of the path,
    /// or where the path first enters a polygon <paramref name="areaMask"/> excludes.
    /// <para/>
    /// True when the walk stopped short of <paramref name="maxDistance"/>. Two different things
    /// cause that, and the mask tells them apart: <c>(hit.Mask &amp; areaMask) == 0</c> means an
    /// excluded polygon blocked it, anything else means the path simply ran out. <c>hit.Hit</c>
    /// separately says the position is a real point on the path rather than one interpolated where
    /// the budget ran out, and <c>hit.Distance</c> is measured ALONG the path rather than to it.
    /// <para/>
    /// Mid-hop across an off-mesh link the corridor already begins at the landing point, so the
    /// walk is measured from there rather than from where the agent hangs.
    /// <para/>
    /// Areas are tested per POLYGON rather than per corner, as Unity does, so a path that only clips
    /// the corner of an excluded polygon still stops at it.
    /// </summary>
    public bool SamplePathPosition(int areaMask, float maxDistance, out NavMeshHit hit)
    {
        hit = default;
        hit.Normal = Float3.UnitY; // a position sample, so up — as SamplePosition reports
        hit.Position = NextPosition;

        if (_agent == null || _world == null) return true;

        Span<long> corridor = _agent.corridor.GetPath();
        int polyCount = _agent.corridor.GetPathCount();
        if (polyCount <= 0 || corridor.Length < polyCount) return true;

        if (!_world.TryRentQuery(out NavMeshQueryLease lease, AgentTypeId)) return true;
        using (lease)
        {
            DtNavMesh mesh = lease.Query.GetAttachedNavMesh();
            hit.Mask = NavMeshWorld.GetPolyAreaMaskBit(mesh, corridor[0]);
            if ((hit.Mask & areaMask) == 0)
            {
                // Already standing in an excluded area: blocked at zero distance, and the
                // agent position is as real a terminus as one found part way along.
                hit.Hit = true;
                return true;
            }

            // ResetPath clears the move target and leaves the corridor behind it, so without this
            // the walk would report a lookahead along a path the agent has already discarded.
            if (!HasPath)
            {
                hit.Hit = true; // a zero-length walk, ending where it started
                return true;
            }

            // A corner at each apex portal and a crossing at each area change, both bounded by the
            // portals walked, plus the two ends: sized from the corridor rather than a constant, so
            // a long path is not silently truncated into a short answer.
            int maxPoints = 2 * polyCount + 1;
            DtStraightPath[] points = ArrayPool<DtStraightPath>.Shared.Rent(maxPoints);
            try
            {
                RcVec3f from = _agent.corridor.GetPos();
                DtStatus status = lease.Query.FindStraightPath(from, _agent.corridor.GetTarget(),
                    corridor[..polyCount], polyCount, points.AsSpan(0, maxPoints), out int count, maxPoints,
                    DtStraightPathOptions.DT_STRAIGHTPATH_AREA_CROSSINGS);
                if (status.Failed() || count == 0)
                {
                    // Unanswerable, and the mask is what the caller reads: leaving the current
                    // polygon in it would report the agent as standing on its destination.
                    hit.Mask = 0;
                    return true;
                }

                float walked = 0f;
                for (int i = 0; i < count - 1; i++)
                {
                    // AREA_CROSSINGS above is what makes this per-polygon rather than per-corner: a
                    // straight segment crosses many polygons, and without it the only ones reported
                    // are those entered at a turn — so a strip of excluded area straight ahead would
                    // never be looked at. With it there is a point wherever the area changes, and
                    // the polygon entered there governs the segment that follows.
                    int mask = NavMeshWorld.GetPolyAreaMaskBit(mesh, points[i].refs);
                    if (mask != 0 && (mask & areaMask) == 0)
                    {
                        hit.Position = ToFloat3(points[i].pos);
                        hit.Distance = walked;
                        hit.Mask = mask;
                        hit.Hit = true;
                        return true;
                    }

                    if (mask != 0) hit.Mask = mask;

                    Float3 a = ToFloat3(points[i].pos), b = ToFloat3(points[i + 1].pos);
                    float leg = (float)Float3.Distance(a, b);
                    if (walked + leg >= maxDistance)
                    {
                        // Clamped because maxDistance is the caller's: a negative one would
                        // extrapolate backwards off the path, and a zero-length leg has no t at all.
                        float t = leg > 0f ? Math.Clamp((maxDistance - walked) / leg, 0f, 1f) : 0f;
                        hit.Position = a + (b - a) * t;
                        hit.Distance = walked + leg * t;
                        hit.Hit = false;
                        return false; // covered the whole distance asked for
                    }

                    walked += leg;
                    hit.Position = b;
                }

                // Ran out of path first — unless the buffer filled, which FindStraightPath reports
                // as success: that last point is not the end of anything.
                hit.Distance = walked;
                hit.Hit = count < maxPoints;
                return true;
            }
            finally
            {
                ArrayPool<DtStraightPath>.Shared.Return(points);
            }
        }
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

        // Avoidance samples a DISCRETE set of candidate velocities, so running it with nothing in
        // range rounds the result and walks the agent sideways off a straight line. Engaged only when
        // something is in range — boundary segments included, so a wall counts — and only while the
        // agent can move: with no target the crowd skips steering, and maxSpeed 0 scales the result to
        // zero, so dvel is zero and the sampler, whose pattern is built from it, plans zero. Worth 90%
        // of a standing crowd's cost. Neighbours and boundary come from the last crowd step, so
        // engaging lags a frame.
        if (ObstacleAvoidanceQuality != ObstacleAvoidanceType.NoObstacleAvoidance)
        {
            // A velocity-controlled agent is the exception: that branch of the crowd's steering
            // ignores maxSpeed, so its dvel is whatever was commanded.
            bool steering = _agent.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY
                || (_agent.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE && _agent.option.maxSpeed > 0);
            bool engage = steering && (_agent.nneis > 0 || _agent.boundary.GetSegmentCount() > 0);
            if (engage != AvoidanceEngaged)
            {
                AvoidanceEngaged = engage;
                _crowd?.UpdateAgentParameters(_agent, BuildAgentParams());
            }
        }

        if (UpdatePosition)
            Transform.Position = ToFloat3(_agent.npos) + new Float3(0, BaseOffset, 0);

        if (UpdateRotation)
        {
            // Face where the agent STEERS, not where it moves: actual velocity carries avoidance
            // corrections that do not shrink with speed, so braking into a goal lets them take over
            // the heading and the agent shivers. The gates below drop a vector too slow to have a
            // direction, or pointing at a target already underfoot.
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

        // The corner window is a LOWER bound (at most the crowd's few visible corners), so its
        // distance only means "arrived" once the window reaches the path end — otherwise a tight
        // switchback under StoppingDistance, or a congestion-jammed agent, could falsely latch.
        // An EMPTY window is also untrustworthy: it happens both standing on the target and
        // transiently right after a hop lands, so measure straight to the target instead.
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
