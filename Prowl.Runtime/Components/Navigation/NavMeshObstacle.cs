// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using DotRecast.Core.Numerics;
using DotRecast.Detour.TileCache;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>Shape of a <see cref="NavMeshObstacle"/>.</summary>
public enum NavMeshObstacleShape
{
    /// <summary>Capsule input, carved as a cylinder of the same radius/height.</summary>
    Capsule,
    /// <summary>Oriented box (yaw only — Detour box obstacles rotate around Y).</summary>
    Box,
}

/// <summary>
/// Blocks agents while enabled — a parked vehicle, a dropped crate, a placed building. Mirrors
/// Unity's NavMeshObstacle, including both of its modes:
/// <para/>
/// <see cref="Carve"/> on cuts a hole in the navmesh, so pathfinding itself routes around the
/// obstacle. The affected tiles rebuild incrementally over the following frames, and with
/// <see cref="CarveOnlyStationary"/> the hole lifts while the obstacle moves and re-applies
/// once it has been still for <see cref="CarvingTimeToStationary"/>.
/// <para/>
/// <see cref="Carve"/> off is Unity's velocity-obstacle mode: the mesh is untouched and the
/// obstacle instead joins each crowd as an immovable neighbour, so agents steer around it
/// locally. That costs nothing per move, which makes it the right mode for something that moves
/// often — but paths are computed as if it weren't there, so an agent whose only route is
/// blocked will press against it rather than reroute.
/// <para/>
/// Either way the object's own geometry stays out of bakes: an obstacle is a runtime thing,
/// and voxelizing it would freeze a hole where it happened to be standing.
/// </summary>
[AddComponentMenu("Navigation/NavMesh Obstacle")]
[ComponentIcon("")] // road barrier
// Carving runs in the editor as well as in play, so placing a building shows the hole it will
// cut without entering play mode. Only the live navmesh is affected — the baked asset never
// stores carves — and the velocity-obstacle path stays play-only, since it is crowd steering.
[ExecuteAlways]
public class NavMeshObstacle : MonoBehaviour
{
    [Tooltip("Obstacle shape: a capsule carves a cylinder; a box carves an oriented (yaw-only) box.")]
    public NavMeshObstacleShape Shape = NavMeshObstacleShape.Box;

    [Tooltip("Obstacle center, local to this GameObject.")]
    public Float3 Center;

    [Tooltip("Box size, local (scaled by the Transform).")]
    [ShowIf(nameof(IsBox))]
    public Float3 Size = new(1, 2, 1);

    [Tooltip("Capsule radius (scaled by the largest horizontal Transform scale).")]
    [ShowIf(nameof(IsCapsule))]
    public float Radius = 0.5f;

    [Tooltip("Capsule height (scaled by the vertical Transform scale).")]
    [ShowIf(nameof(IsCapsule))]
    public float Height = 2f;

    [Tooltip("On: cut a hole in the navmesh so paths route around this. Off: leave the mesh alone and make agents steer around it locally instead — cheaper, and the right choice for something that moves, but paths still lead through it.")]
    public bool Carve = true;

    [Tooltip("Only carve while stationary: the carve lifts while the obstacle moves and re-applies once it has settled. Off re-carves on every move beyond the threshold — much more expensive for frequently-moving obstacles.")]
    public bool CarveOnlyStationary = true;

    [Tooltip("Movement beyond this distance (world units) counts as moving.")]
    public float CarvingMoveThreshold = 0.1f;

    [Tooltip("Seconds the obstacle must be still before it carves again (with Carve Only Stationary).")]
    public float CarvingTimeToStationary = 0.5f;

    private bool IsBox => Shape == NavMeshObstacleShape.Box;
    private bool IsCapsule => Shape == NavMeshObstacleShape.Capsule;

    private NavMeshWorld? _world;
    // Obstacle handle per navmesh instance the carve is registered with. Instances are
    // per-agent-type; the obstacle applies to every one of them (Unity has no agent filter on
    // obstacles).
    private readonly Dictionary<NavMeshInstance, long> _refs = [];
    private Float3 _appliedPosition;
    private float _stillTime;
    private bool _carveApplied;

    // Velocity-obstacle mode: one immovable agent per live crowd, re-pinned every frame.
    private readonly Dictionary<DotRecast.Detour.Crowd.DtCrowd, DotRecast.Detour.Crowd.DtCrowdAgent> _blockers = [];
    private int _blockerCrowdCount = -1;
    private float _blockerRadius, _blockerHeight;
    private bool _warnedBlockerUnplaced;

    // Geometry the live carve was registered with, re-checked each LateUpdate so writing the
    // public fields after AddComponent (spawn-then-configure) re-carves — the same drift-check
    // pattern as NavMeshAgent's AgentTypeId/AreaMask. Rotation compares quaternions by dot
    // product: no per-frame Euler conversion, no wrap false-positives at ±180°.
    private NavMeshObstacleShape _appliedShape;
    private Float3 _appliedSize;
    private float _appliedRadius, _appliedHeight;
    private Quaternion _appliedRotation = Quaternion.Identity;

    private void CaptureAppliedGeometry()
    {
        _appliedShape = Shape;
        _appliedSize = Size;
        _appliedRadius = Radius;
        _appliedHeight = Height;
        _appliedRotation = Transform.Rotation;
    }

    private bool GeometryChanged()
        => Shape != _appliedShape
            || !Size.Equals(_appliedSize)
            || Radius != _appliedRadius
            || Height != _appliedHeight
            || (Shape == NavMeshObstacleShape.Box
                && Math.Abs(Quaternion.Dot(Transform.Rotation, _appliedRotation)) < 0.9999);

    public override void OnEnable()
    {
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsValid())
        {
            _world = scene!.Navigation;
            _world.NavMeshChanged += OnNavMeshChanged;
        }

        _appliedPosition = Transform.Position;
        _stillTime = CarvingTimeToStationary; // spawning still: carve immediately
        TryApplyCarve();
    }

    public override void OnDisable()
    {
        RemoveCarve();
        RemoveBlockers();
        if (_world != null)
        {
            _world.NavMeshChanged -= OnNavMeshChanged;
            _world = null;
        }
    }

    private void OnNavMeshChanged()
    {
        // An instance may have been replaced (rebake) or registered late; dead entries
        // are dropped (their cache died with the instance) and the carve re-applies to any
        // new instance on the next LateUpdate. Gated on the structural counter because the
        // event also fires every frame a carve is converging — including this obstacle's own —
        // and re-attaching per frame would make each carve pay for itself repeatedly.
        if (_world == null || _world.StructureGeneration == _seenStructureGeneration) return;
        _seenStructureGeneration = _world.StructureGeneration;
        _refsPruneNeeded = true;
    }

    private bool _refsPruneNeeded;
    private int _seenStructureGeneration = -1;

    public override void LateUpdate()
    {
        if (!Carve)
        {
            // Switching modes at runtime must not leave the other mode's effect behind.
            if (_carveApplied) RemoveCarve();
            // Velocity mode is crowd steering, and crowds only step during play — outside it a
            // blocker would sit in a crowd nothing is running.
            if (Application.IsPlaying) UpdateBlockers();
            else if (_blockers.Count > 0) RemoveBlockers();
            return;
        }
        // The other half of that: a blocker left over from velocity mode would sit inside the
        // hole this obstacle carves, avoided by agents already routing around it.
        if (_blockers.Count > 0) RemoveBlockers();

        // Geometry drift MUST be evaluated before the new-instance pickup below: TryApplyCarve
        // captures the applied-geometry snapshot, so running the pickup first (its flag is set
        // by any NavMeshChanged — including the cache pump's own convergence events) would
        // record the new field values without re-carving and swallow the drift for good.
        if (_carveApplied && GeometryChanged())
        {
            RemoveCarve();
            TryApplyCarve();
        }

        if (_refsPruneNeeded)
        {
            _refsPruneNeeded = false;
            PruneDeadInstances();
            if (_carveApplied) TryApplyCarve(); // pick up newly registered navmeshes
        }

        double moved = Float3.Distance(Transform.Position, _appliedPosition);
        if (CarveOnlyStationary)
        {
            if (moved > CarvingMoveThreshold)
            {
                // Moving: lift the carve and restart the settle timer.
                RemoveCarve();
                _appliedPosition = Transform.Position;
                _stillTime = 0f;
            }
            else if (!_carveApplied)
            {
                _stillTime += (float)Time.DeltaTime;
                if (_stillTime >= CarvingTimeToStationary)
                    TryApplyCarve();
            }
        }
        else if (moved > CarvingMoveThreshold)
        {
            // Follow mode: re-carve at the new position. Every move pays incremental tile
            // rebuilds, hence the tooltip's warning.
            RemoveCarve();
            _appliedPosition = Transform.Position;
            TryApplyCarve();
        }
    }

    /// <summary>
    /// Velocity-obstacle mode: keep one immovable agent per live crowd sitting on this
    /// obstacle, so every other agent's local avoidance treats it as a neighbour to steer
    /// around. Crowds appear lazily (when the first agent of a type registers), so membership
    /// is re-derived whenever the world's crowd count changes rather than only at enable.
    /// </summary>
    private void UpdateBlockers()
    {
        if (_world == null) return;

        // LossyScale walks the parent chain, so the shape is measured once per frame and passed
        // down rather than re-derived by each helper.
        Float3 scale = Transform.LossyScale;
        float radius = BlockerRadius(scale);
        float height = BlockerHeight(scale);
        bool resized = Math.Abs(radius - _blockerRadius) > 1e-4f || Math.Abs(height - _blockerHeight) > 1e-4f;
        if (_blockerCrowdCount != _world.CrowdCount || resized || _refsPruneNeeded)
        {
            _refsPruneNeeded = false;
            _blockerRadius = radius;
            _blockerHeight = height;
            RefreshBlockers(radius, height);
        }

        // Write the position straight onto the crowd agent every frame. A crowd agent is
        // normally moved by its own steering, which a blocker has none of, so this is what
        // makes it follow the Transform — the mode for things that move. It also absorbs any
        // displacement the crowd's collision-resolution pass applies (measured at under a
        // centimetre even under sustained pressure, but it costs nothing to be exact).
        Float3 position = BlockerPosition(height);
        var pinned = new RcVec3f((float)position.X, (float)position.Y, (float)position.Z);
        foreach (DotRecast.Detour.Crowd.DtCrowdAgent blocker in _blockers.Values)
            blocker.npos = pinned;
    }

    /// <summary>Join every live crowd not already blocked, and drop memberships whose crowd
    /// died with its navmesh.</summary>
    private void RefreshBlockers(float radius, float height)
    {
        if (_world == null) return;

        Float3 position = BlockerPosition(height);
        var rcPosition = new RcVec3f((float)position.X, (float)position.Y, (float)position.Z);
        foreach (NavMeshAgentType type in NavMeshAgentTypes.All)
        {
            DotRecast.Detour.Crowd.DtCrowd? crowd = _world.GetNativeCrowd(type.Id);
            if (crowd == null) continue;
            if (_blockers.TryGetValue(crowd, out DotRecast.Detour.Crowd.DtCrowdAgent? existing))
            {
                crowd.UpdateAgentParameters(existing, BlockerParams(radius, height));
                continue;
            }

            DotRecast.Detour.Crowd.DtCrowdAgent blocker = crowd.AddAgent(rcPosition, BlockerParams(radius, height));
            _blockers[crowd] = blocker;
            WarnIfUnplaced(blocker);
        }

        // A crowd the world no longer owns died with its navmesh; its agents went with it.
        List<DotRecast.Detour.Crowd.DtCrowd>? dead = null;
        foreach (DotRecast.Detour.Crowd.DtCrowd crowd in _blockers.Keys)
        {
            bool live = false;
            foreach (NavMeshAgentType type in NavMeshAgentTypes.All)
                if (ReferenceEquals(_world.GetNativeCrowd(type.Id), crowd)) { live = true; break; }
            if (!live) (dead ??= []).Add(crowd);
        }
        if (dead != null)
            foreach (DotRecast.Detour.Crowd.DtCrowd crowd in dead)
                _blockers.Remove(crowd);

        _blockerCrowdCount = _world.CrowdCount;
    }

    private void RemoveBlockers()
    {
        foreach ((DotRecast.Detour.Crowd.DtCrowd crowd, DotRecast.Detour.Crowd.DtCrowdAgent blocker) in _blockers)
            crowd.RemoveAgent(blocker);
        _blockers.Clear();
        _blockerCrowdCount = -1;
    }

    /// <summary>
    /// A blocker that failed to place is an invisible failure: the component looks configured
    /// and nothing avoids it. The crowd's placement
    /// probe searches only a few units vertically (sized from
    /// <see cref="NavMeshWorld.CrowdMaxAgentRadius"/>), so an obstacle floating well above the
    /// walkable surface — a tall Center offset, spawned mid-air — lands invalid.
    /// </summary>
    private void WarnIfUnplaced(DotRecast.Detour.Crowd.DtCrowdAgent blocker)
    {
        if (blocker.state != DotRecast.Detour.Crowd.DtCrowdAgentState.DT_CROWDAGENT_STATE_INVALID) return;
        if (_warnedBlockerUnplaced) return;
        _warnedBlockerUnplaced = true;
        Debug.LogWarning($"[Navigation] NavMeshObstacle '{GameObject.Name}' could not place its avoidance blocker: no navmesh near its base. Agents will not steer around it. Move the obstacle onto the navmesh (check Center and the object's height), or raise NavMeshWorld.CrowdMaxAgentRadius to widen the placement search.");
    }

    /// <summary>Where the blocker stands: the obstacle's footprint centre at its base, matching
    /// how agents sit on the mesh at their feet.</summary>
    private Float3 BlockerPosition(float height)
    {
        Float3 center = Transform.TransformPoint(Center);
        return new Float3(center.X, center.Y - height * 0.5f, center.Z);
    }

    /// <summary>Avoidance is circle-based, so a box is approximated by the circle enclosing its
    /// footprint — agents give a rotated crate a slightly wider berth than its corners need.</summary>
    private float BlockerRadius(Float3 scale)
    {
        if (Shape == NavMeshObstacleShape.Capsule)
            return Radius * MathF.Max(0.01f, (float)Math.Max(Math.Abs(scale.X), Math.Abs(scale.Z)));

        double x = Size.X * 0.5f * Math.Abs(scale.X);
        double z = Size.Z * 0.5f * Math.Abs(scale.Z);
        return MathF.Max(0.01f, (float)Math.Sqrt(x * x + z * z));
    }

    private float BlockerHeight(Float3 scale)
    {
        float scaleY = MathF.Max(0.01f, (float)Math.Abs(scale.Y));
        return MathF.Max(0.01f, (Shape == NavMeshObstacleShape.Capsule ? Height : (float)Size.Y) * scaleY);
    }

    private DotRecast.Detour.Crowd.DtCrowdAgentParams BlockerParams(float radius, float height) => new()
    {
        radius = radius,
        height = height,
        // Immovable: no steering of its own, and no update flags, so the crowd never tries to
        // path, avoid or separate on its behalf. It exists purely to be avoided — which is also
        // why its own neighbour query is kept to its radius rather than the multiple a steering
        // agent needs: the crowd still runs that query every frame, and nothing consumes it.
        maxAcceleration = 0f,
        maxSpeed = 0f,
        collisionQueryRange = radius,
        pathOptimizationRange = 0f,
        updateFlags = 0,
        obstacleAvoidanceType = 0,
        separationWeight = 0f,
        queryFilterType = 0,
        userData = this,
    };

    /// <summary>Queue the carve on every registered navmesh not already carrying it.
    /// The affected tiles rebuild incrementally in NavMeshWorld.Update.</summary>
    private void TryApplyCarve()
    {
        if (_world == null || !Carve) return;

        foreach (NavMeshAgentType type in NavMeshAgentTypes.All)
        {
            NavMeshInstance? instance = _world.GetInstance(type.Id);
            if (instance == null || _refs.ContainsKey(instance)) continue;
            long obstacleRef = AddToCache(instance.TileCache, instance.NavMeshData.Settings.AgentRadius);
            if (obstacleRef == 0) continue;
            _refs[instance] = obstacleRef;
            instance.CachePending = true;
        }
        _carveApplied = true;
        CaptureAppliedGeometry();
    }

    // No try/catch: verified against DotRecast 2026.1.3 — AllocObstacle grows its pool and
    // the request queue is an unbounded list, so Add*Obstacle never throws and never returns
    // 0 for capacity (maxObstacles only sizes the initial id encoding).
    /// <param name="agentRadius">Envelope of the navmesh being carved. The hole is widened by it
    /// because a navmesh stores where an agent's CENTRE may be, not where its body fits: a bake
    /// pulls the mesh this far back from every wall, and a carve that did not would let agents
    /// walk their centre onto the obstacle's surface and stand half inside it.</param>
    private long AddToCache(DtTileCache cache, float agentRadius)
    {
        Float3 scale = Transform.LossyScale;
        Float3 worldCenter = Transform.TransformPoint(Center);
        float clearance = Math.Max(0f, agentRadius);

        if (Shape == NavMeshObstacleShape.Capsule)
        {
            float radius = Radius * MathF.Max(0.01f, (float)Math.Max(Math.Abs(scale.X), Math.Abs(scale.Z)));
            float height = Height * MathF.Max(0.01f, (float)Math.Abs(scale.Y));
            // Cylinder obstacles anchor at the base center.
            var basePos = new RcVec3f((float)worldCenter.X, (float)(worldCenter.Y - height * 0.5f), (float)worldCenter.Z);
            return cache.AddObstacle(basePos, radius + clearance, height);
        }

        // Horizontal only: erosion is a footprint concern, and growing the box vertically would
        // start carving under things the obstacle passes beneath.
        var halfExtents = new RcVec3f(
            (float)(Size.X * 0.5f * Math.Abs(scale.X)) + clearance,
            (float)(Size.Y * 0.5f * Math.Abs(scale.Y)),
            (float)(Size.Z * 0.5f * Math.Abs(scale.Z)) + clearance);
        float yawRadians = (float)(Transform.Rotation.EulerAngles.Y * Maths.Deg2Rad);
        return cache.AddBoxObstacle(new RcVec3f((float)worldCenter.X, (float)worldCenter.Y, (float)worldCenter.Z), halfExtents, yawRadians);
    }

    /// <summary>Queue removal of the carve everywhere it is registered.</summary>
    private void RemoveCarve()
    {
        foreach ((NavMeshInstance instance, long obstacleRef) in _refs)
        {
            instance.TileCache.RemoveObstacle(obstacleRef);
            instance.CachePending = true;
        }
        _refs.Clear();
        _carveApplied = false;
    }

    /// <summary>Drop handles whose instance is no longer registered — the cache (and its
    /// obstacle state) died with it, so there is nothing to remove.</summary>
    private void PruneDeadInstances()
    {
        if (_refs.Count == 0 || _world == null) return;
        List<NavMeshInstance>? dead = null;
        foreach (NavMeshInstance instance in _refs.Keys)
        {
            if (_world.GetInstance(instance.AgentTypeId) != instance)
                (dead ??= []).Add(instance);
        }
        if (dead != null)
            foreach (NavMeshInstance instance in dead)
                _refs.Remove(instance);
    }

    public override void DrawGizmosSelected()
    {
        var color = new Color(1f, 0.5f, 0.1f, 1f);
        if (Shape == NavMeshObstacleShape.Box)
        {
            Debug.PushMatrix(Transform.LocalToWorldMatrix);
            Debug.DrawWireCube(Center, Size * 0.5f, color);
            Debug.PopMatrix();
        }
        else
        {
            Float3 worldCenter = Transform.TransformPoint(Center);
            Debug.DrawWireSphere(worldCenter, Radius, color);
            Debug.DrawLine(worldCenter - new Float3(0, Height * 0.5f, 0), worldCenter + new Float3(0, Height * 0.5f, 0), color);
        }
    }
}
