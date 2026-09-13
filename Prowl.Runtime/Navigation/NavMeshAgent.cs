// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Walks a scene's navmesh for one agent type, steered by a <see cref="NavMeshCrowd"/> resolved through
/// the scene's <see cref="NavMeshSystem"/> by <see cref="AgentTypeId"/> rather than a direct reference to
/// a <see cref="NavMeshSurface"/> - see <see cref="NavMeshSystem"/>'s own doc comment for why. Registers
/// and unregisters purely on enable/disable; it never scans the scene for anything.
/// <para/>
/// Joins its crowd's simulation exactly once, in <see cref="OnEnable"/> (or lazily on the first
/// <see cref="SetDestination"/> if no crowd existed yet), and every subsequent <see cref="SetDestination"/>
/// call reuses that same slot - never re-adds. This is the direct fix for the bug PR #335's review
/// flagged in its own <c>Move()</c>: tearing a crowd agent down and rebuilding it on every call is both
/// expensive and behaviorally broken (it resets the corridor, discarding in-flight steering state).
/// <para/>
/// A destination anywhere on this agent's type's navmesh - however far, however many baked tiles it
/// spans - is a single <see cref="SetDestination"/> call: the underlying navmesh is genuinely tiled
/// (see <see cref="NavMeshQuery"/>'s own doc comment), and Detour paths across tile boundaries natively,
/// the same as it does across polygons within one tile. There is no per-region stitching here to get
/// wrong or reason about.
/// </summary>
[AddComponentMenu("Navigation/NavMesh Agent")]
public sealed class NavMeshAgent : MonoBehaviour
{
    /// <summary>The crowd this agent has joined (see <see cref="EnsureJoinedCrowd"/>), or null before
    /// its first <see cref="SetDestination"/> call.</summary>
    private NavMeshCrowd? _crowd;

    /// <summary>This agent's slot within <see cref="_crowd"/>. Null exactly when <see cref="_crowd"/> is.</summary>
    private NavMeshCrowdHandle? _crowdHandle;

    /// <summary>Whether <see cref="Update"/> last observed this agent on an off-mesh connection - lets
    /// it edge-trigger <see cref="NavMeshLink.Traversed"/> only on the frame crossing one begins.</summary>
    private bool _wasOnOffMeshConnection;

    /// <summary>Which agent type (see <see cref="NavMeshAgentTypes"/>) this agent walks as. Only a
    /// surface baked for the same type is ever visible to it. Hidden from the default inspector -
    /// <c>NavMeshAgentEditor</c> draws it as a named dropdown instead of a raw id.</summary>
    [HideInInspector]
    public int AgentTypeId = NavMeshAgentTypes.HumanoidId;

    /// <summary>Which navmesh areas (see <see cref="NavMeshAreas"/>) this agent is allowed to cross,
    /// one bit per area index. Defaults to every area. Applies both to <see cref="FindPath"/> and to
    /// actual crowd movement (<see cref="SetDestination"/>). Read once, the moment this agent joins its
    /// crowd (see <see cref="EnsureJoinedCrowd"/>) - changing it afterward has no effect until the agent
    /// leaves and rejoins (disable then re-enable, or a rebake/link-change forcing a rejoin).</summary>
    public uint AreaMask = uint.MaxValue;

    /// <summary>Top walking speed, in world units per second.</summary>
    public float Speed = 3.5f;

    /// <summary>How quickly <see cref="Speed"/> is reached, in world units per second squared.</summary>
    public float Acceleration = 8f;

    /// <summary>How high this agent hops while crossing a <see cref="NavMeshLink"/>'s off-mesh
    /// connection - a purely visual arc on top of the crowd's own crossing position, peaking at the
    /// midpoint of the crossing. Zero disables the hop, so a link representing a flat bridge rather
    /// than a jump can opt out.</summary>
    public float JumpHeight = 1f;

    /// <summary>How close to a destination counts as arrived. See <see cref="HasArrived"/>.</summary>
    public float StoppingDistance = 0.2f;

    /// <summary>Vertical offset between the navmesh surface and this GameObject's <see cref="Transform"/> -
    /// for a character whose pivot sits at its center rather than its feet. Purely visual: <see cref="Position"/>
    /// (what pathing/arrival logic reads) is never offset by this, only what <see cref="Update"/> writes
    /// to <see cref="Transform"/>.</summary>
    public float BaseOffset;

    /// <summary>Whether <see cref="Update"/> writes the crowd simulation's position to <see cref="Transform"/>.
    /// Turn off to drive a Rigidbody or CharacterController from <see cref="Velocity"/> yourself instead -
    /// the crowd still simulates and steers this agent either way, only the write is skipped.</summary>
    public bool UpdatePosition = true;

    /// <summary>Whether <see cref="Update"/> turns <see cref="Transform"/> to face this agent's steering
    /// direction, at up to <see cref="AngularSpeed"/> degrees/second. Turn off for a character whose
    /// facing is driven by animation or by the caller instead.</summary>
    public bool UpdateRotation = true;

    /// <summary>How fast <see cref="Transform"/> turns to face this agent's steering direction, in
    /// degrees per second. Only applies while <see cref="UpdateRotation"/> is on.</summary>
    public float AngularSpeed = 720f;

    /// <summary>The query for this agent's type, resolved through the scene's <see cref="NavMeshSystem"/>.
    /// Null if the scene has no enabled, built surface of this agent's type.</summary>
    public NavMeshQuery? Query => Scene.IsValid() ? NavMeshSystem.GetOrCreate(Scene).GetQuery(AgentTypeId) : null;

    /// <summary>This agent's simulated position while it has joined a crowd (see <see cref="SetDestination"/>);
    /// its <see cref="Transform"/>'s own position otherwise. Deliberately the raw simulated position,
    /// not what <see cref="Update"/> renders <see cref="Transform"/> at - while crossing a link with
    /// <see cref="JumpHeight"/> above zero, <see cref="Transform"/> is higher than this by the jump
    /// arc's current offset. Pathing/arrival logic should read this; visuals should read <see cref="Transform"/>.</summary>
    public Float3 Position => _crowd != null && _crowdHandle != null ? _crowd.GetPosition(_crowdHandle.Value) : Transform.Position;

    /// <summary>This agent's current simulated velocity. Zero while it hasn't joined a crowd.</summary>
    public Float3 Velocity => _crowd != null && _crowdHandle != null ? _crowd.GetVelocity(_crowdHandle.Value) : Float3.Zero;

    /// <summary>Whether this agent has a destination and has closed to within <see cref="StoppingDistance"/>
    /// of it. False while it hasn't joined a crowd (nothing baked for its type yet) or has no destination.</summary>
    public bool HasArrived => _crowd != null && _crowdHandle != null && _crowd.HasArrived(_crowdHandle.Value, StoppingDistance);

    /// <summary>True while this agent is currently crossing a <see cref="NavMeshLink"/>'s off-mesh
    /// connection rather than walking ordinary ground.</summary>
    public bool IsOnOffMeshLink => _crowd != null && _crowdHandle != null && _crowd.IsOnOffMeshConnection(_crowdHandle.Value);

    /// <summary>The <see cref="NavMeshLink"/> this agent is currently crossing, or null while it isn't on
    /// one (or this agent's query wasn't built with link source tracking).</summary>
    public NavMeshLink? CurrentOffMeshLinkData => _crowd != null && _crowdHandle != null ? _crowd.ResolveCurrentLink(_crowdHandle.Value) : null;

    /// <summary>Reused by <see cref="UpcomingCorners"/> so repeated calls never allocate - see that
    /// method's own doc comment.</summary>
    private NavMeshCorridorCorner[] _cornerBuffer = new NavMeshCorridorCorner[8];

    /// <summary>The next <paramref name="count"/> corridor corners this agent is actually steering
    /// toward right now (not a fresh <see cref="FindPath"/> call), nearest first, each paired with the
    /// corridor distance to reach it, as a slice of a buffer this instance reuses across calls - never
    /// allocates beyond growing that buffer the first time a caller asks for more than it currently
    /// holds. Shorter than <paramref name="count"/> once the corridor runs out before reaching the
    /// destination; empty while this agent has no active path.</summary>
    public ReadOnlySpan<NavMeshCorridorCorner> UpcomingCorners(int count)
    {
        if (_crowd == null || _crowdHandle == null) return [];

        if (_cornerBuffer.Length < count)
            _cornerBuffer = new NavMeshCorridorCorner[count];

        int actual = _crowd.GetUpcomingCorners(_crowdHandle.Value, _cornerBuffer.AsSpan(0, count));
        return _cornerBuffer.AsSpan(0, actual);
    }

    /// <summary>Straight-line distance to the very next corridor corner - 0 while this agent has no
    /// active path, or has already reached the last corner this tick.</summary>
    public float NextCornerDistance => _crowd != null && _crowdHandle != null ? _crowd.GetNextCornerDistance(_crowdHandle.Value) : 0f;

    /// <summary>How sharply, in degrees, the path bends at the next corner - 0 while fewer than two
    /// corners are currently known. Useful for slowing into a sharp turn, or triggering a turn animation
    /// ahead of reaching the corner itself.</summary>
    public float NextTurnAngle => _crowd != null && _crowdHandle != null ? _crowd.GetNextTurnAngleDegrees(_crowdHandle.Value) : 0f;

    /// <summary>Whether a <see cref="NavMeshLink"/> appears anywhere ahead in this agent's currently-known
    /// corridor (not yet being crossed - see <see cref="IsOnOffMeshLink"/> for that), and if so, which one
    /// and how far along the corridor it is. False (with <paramref name="link"/> null and
    /// <paramref name="distance"/> 0) while this agent has no active path or no link lies ahead within it.</summary>
    public bool IsApproachingLink(out NavMeshLink? link, out float distance)
    {
        if (_crowd != null && _crowdHandle != null)
            return _crowd.TryGetApproachingLink(_crowdHandle.Value, out link, out distance);

        link = null;
        distance = 0f;
        return false;
    }

    /// <summary>Finds a path from this agent's current position to <paramref name="destination"/> on
    /// its own type's navmesh. Purely informational (for visualizing a route); does not move the agent
    /// - see <see cref="SetDestination"/> for that.</summary>
    public NavMeshPath FindPath(Float3 destination)
    {
        NavMeshQuery? query = Query;
        return query != null ? query.FindPath(Position, destination, AreaMask) : NavMeshPath.None;
    }

    /// <summary>Steers this agent toward <paramref name="destination"/> via crowd simulation - avoidance,
    /// separation from other agents of the same type, and off-mesh connections all apply automatically.
    /// Joins this agent's crowd on first use; every call after that just retargets the same slot (see
    /// this class's own doc comment for why that matters). False if this agent's type has no usable
    /// navmesh yet, or no path to <paramref name="destination"/> exists at all.</summary>
    public bool SetDestination(Float3 destination)
    {
        if (!EnsureJoinedCrowd()) return false;
        _flowField = null; // a fixed destination and a followed field are mutually exclusive modes
        return _crowd!.SetDestination(_crowdHandle!.Value, destination);
    }

    /// <summary>The shared route this agent is currently steering along via <see cref="FollowFlowField"/>,
    /// or null while it is following an ordinary <see cref="SetDestination"/> target instead (the two
    /// are mutually exclusive - each replaces whichever the other last set).</summary>
    private NavMeshFlowField? _flowField;

    /// <summary>Steers this agent along <paramref name="field"/> instead of toward one fixed destination -
    /// the alternative <see cref="NavMeshAgent.SetDestination"/> is for, when many agents share the same
    /// goal and would otherwise each pay for their own path to it. Every <see cref="Update"/> tick this
    /// agent asks <paramref name="field"/> for the direction at its current position and requests that as
    /// its velocity directly; crowd separation/avoidance still applies on top exactly as it would for a
    /// path-following agent. Joins this agent's crowd on first use, same as <see cref="SetDestination"/>.
    /// False if this agent's type has no usable navmesh yet.</summary>
    public bool FollowFlowField(NavMeshFlowField field)
    {
        if (!EnsureJoinedCrowd()) return false;
        _crowd!.ResetMoveTarget(_crowdHandle!.Value); // drop any fixed-destination request this replaces
        _flowField = field;
        return true;
    }

    /// <summary>Clears this agent's current destination (or followed flow field) in place - it stops
    /// where it is. Does not leave its crowd or drop its corridor allocation, unlike toggling
    /// <see cref="Enabled"/> would.</summary>
    public void ResetPath()
    {
        _flowField = null;
        if (_crowd != null && _crowdHandle != null)
            _crowd.ResetMoveTarget(_crowdHandle.Value);
    }

    /// <summary>Instantly moves this agent to <paramref name="position"/>, bypassing crowd steering -
    /// for a respawn, a cutscene hand-off, or a scripted placement. Rejoins the crowd fresh at the new
    /// position (a full corridor reset, since nothing about the old corridor is valid from
    /// <paramref name="position"/>), so any in-progress path is discarded; pass a destination through
    /// <see cref="SetDestination"/> again afterward if one is still wanted. False if this agent's type
    /// has no usable navmesh yet.</summary>
    public bool Warp(Float3 position)
    {
        Transform.Position = position;

        if (_crowd == null || _crowdHandle == null) return true; // not joined yet - nothing further to do

        _crowd.RemoveAgent(_crowdHandle.Value);
        _crowdHandle = null;
        return EnsureJoinedCrowd();
    }

    /// <summary>Finds the closest point on any navmesh edge within <paramref name="maxRadius"/> of this
    /// agent's current position - see <see cref="NavMeshQuery.FindClosestEdge"/>. False if this agent's
    /// type has no usable navmesh yet, or no edge is within range.</summary>
    public bool FindClosestEdge(float maxRadius, out Float3 hitPosition, out Float3 hitNormal, out float distance)
    {
        NavMeshQuery? query = Query;
        if (query != null) return query.FindClosestEdge(Position, maxRadius, out hitPosition, out hitNormal, out distance);

        hitPosition = Position;
        hitNormal = Float3.Zero;
        distance = 0f;
        return false;
    }

    /// <summary>Casts a straight-line "can this agent walk directly there" ray from its current position -
    /// see <see cref="NavMeshQuery.Raycast"/>. False (with <paramref name="hitPoint"/> left at
    /// <paramref name="destination"/>) if this agent's type has no usable navmesh yet.</summary>
    public bool Raycast(Float3 destination, out Float3 hitPoint)
    {
        NavMeshQuery? query = Query;
        if (query != null) return query.Raycast(Position, destination, out hitPoint);

        hitPoint = destination;
        return false;
    }

    /// <summary>Makes sure this agent occupies a slot in its type's current crowd, joining (or
    /// rejoining, if the crowd it was in has since become stale) as needed. False if this agent's type
    /// has no usable crowd yet.</summary>
    private bool EnsureJoinedCrowd()
    {
        if (Scene.IsNotValid()) return false;

        NavMeshCrowd? targetCrowd = NavMeshSystem.GetOrCreate(Scene).GetOrCreateCrowd(AgentTypeId);
        if (targetCrowd == null) return false;

        if (targetCrowd != _crowd)
        {
            if (_crowd != null && _crowdHandle != null) _crowd.RemoveAgent(_crowdHandle.Value);
            _crowd = targetCrowd;
            _crowdHandle = null;
        }

        if (_crowdHandle == null)
        {
            NavMeshAgentTypeInfo type = NavMeshAgentTypes.GetById(AgentTypeId) ?? NavMeshAgentTypeInfo.CreateHumanoid();
            _crowdHandle = _crowd.AddAgent(Transform.Position, type.AgentRadius, type.AgentHeight, Speed, Acceleration, AreaMask);
        }
        return true;
    }

    /// <summary>Registers this agent with the scene's <see cref="NavMeshSystem"/>. Does not join a
    /// crowd yet - that happens lazily on the first <see cref="SetDestination"/> call.</summary>
    public override void OnEnable()
    {
        if (Scene.IsValid())
            NavMeshSystem.GetOrCreate(Scene).RegisterAgent(this);
    }

    /// <summary>Leaves this agent's crowd (if it had joined one) and unregisters it from the scene's
    /// <see cref="NavMeshSystem"/>.</summary>
    public override void OnDisable()
    {
        if (_crowd != null && _crowdHandle != null)
            _crowd.RemoveAgent(_crowdHandle.Value);
        _crowd = null;
        _crowdHandle = null;
        _wasOnOffMeshConnection = false;

        if (Scene.IsValid())
            NavMeshSystem.GetOrCreate(Scene).UnregisterAgent(this);
    }

    /// <summary>Syncs this GameObject's transform to the crowd simulation's result for this agent, and
    /// fires <see cref="NavMeshLink.Traversed"/> the moment it starts crossing one. Position sync reads
    /// whatever the last <see cref="NavMeshSystem.TickCrowds"/> fixed tick produced - the simulation
    /// itself always advances on a fixed cadence (see that method), never here.</summary>
    public override void Update()
    {
        if (_crowd == null || _crowdHandle == null) return;

        if (_flowField != null && _flowField.TryGetDirection(Position, out Float3 direction))
            _crowd.RequestVelocity(_crowdHandle.Value, direction * Speed);

        if (UpdatePosition)
        {
            Float3 position = _crowd.GetPosition(_crowdHandle.Value);

            float? crossingProgress = _crowd.GetOffMeshConnectionProgress(_crowdHandle.Value);
            if (crossingProgress != null && JumpHeight > 0f)
                position += new Float3(0f, MathF.Sin(crossingProgress.Value * MathF.PI) * JumpHeight, 0f);

            Transform.Position = position + new Float3(0f, BaseOffset, 0f);
        }

        if (UpdateRotation)
            UpdateFacing();

        bool onOffMeshConnection = _crowd.IsOnOffMeshConnection(_crowdHandle.Value);
        if (onOffMeshConnection && !_wasOnOffMeshConnection)
        {
            NavMeshLink? link = _crowd.ResolveCurrentLink(_crowdHandle.Value);
            if (link.IsValid()) link.NotifyTraversed(this);
        }
        _wasOnOffMeshConnection = onOffMeshConnection;
    }

    /// <summary>Turns <see cref="Transform"/> toward this agent's current steering direction (flattened
    /// to the horizontal plane), at up to <see cref="AngularSpeed"/> degrees this tick. A no-op while
    /// barely moving - an agent standing still (or crossing an off-mesh link with no lateral velocity)
    /// has no steering direction worth facing.</summary>
    private void UpdateFacing()
    {
        Float3 velocity = Velocity;
        Float3 flat = new(velocity.X, 0f, velocity.Z);
        if (Float3.Length(flat) < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(Float3.Normalize(flat), Float3.UnitY);
        float maxDegrees = AngularSpeed * Time.DeltaTime;
        float angle = Quaternion.Angle(Transform.Rotation, targetRotation);

        Transform.Rotation = angle <= maxDegrees || angle < 0.0001f
            ? targetRotation
            : Quaternion.Slerp(Transform.Rotation, targetRotation, maxDegrees / angle);
    }

    /// <summary>Draws a capsule sized from this agent's type, colored per <see cref="AgentTypeId"/>
    /// (see <see cref="NavMeshGizmoColors"/>) so agents of different types are distinguishable at a
    /// glance in a scene with more than one.</summary>
    public override void DrawGizmos()
    {
        NavMeshAgentTypeInfo type = NavMeshAgentTypes.GetById(AgentTypeId) ?? NavMeshAgentTypeInfo.CreateHumanoid();
        Color color = NavMeshGizmoColors.ForAgentType(AgentTypeId);

        Float3 basePos = Position;
        float halfHeight = Maths.Max(type.AgentHeight * 0.5f, type.AgentRadius);
        Float3 bottom = basePos + new Float3(0, type.AgentRadius, 0);
        Float3 top = basePos + new Float3(0, Maths.Max(type.AgentHeight - type.AgentRadius, type.AgentRadius), 0);
        Debug.DrawWireCapsule(bottom, top, type.AgentRadius, color);

        Float3 velocity = Velocity;
        if (Float3.Length(velocity) > 0.01f)
            Debug.DrawArrow(basePos + new Float3(0, halfHeight, 0), Float3.Normalize(velocity), color);
    }

    /// <summary>Draws this agent's current path to its active destination, if it has one - the
    /// corner-to-corner route <see cref="NavMeshPath.Corners"/> describes, the same one steering
    /// actually walks (see <see cref="NavMeshPath.CorridorPolygons"/>'s own doc comment for why this,
    /// not that).</summary>
    public override void DrawGizmosSelected()
    {
        if (_crowd == null || _crowdHandle == null) return;

        Float3? destination = _crowd.GetDestination(_crowdHandle.Value);
        if (destination == null) return;

        NavMeshPath path = _crowd.Query.FindPath(Position, destination.Value, AreaMask);
        if (!path.Success) return;

        Color color = NavMeshGizmoColors.ForAgentType(AgentTypeId);
        for (int i = 0; i < path.Corners.Length - 1; i++)
            Debug.DrawLine(path.Corners[i], path.Corners[i + 1], color);

        Debug.DrawWireSphere(destination.Value, 0.2f, color);
    }
}
