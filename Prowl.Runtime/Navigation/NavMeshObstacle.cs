// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// A shape that blocks agents wherever it currently sits - a parked vehicle, a dropped crate, a placed
/// building. <see cref="Carve"/> on (the default) cuts a real hole in the navmesh via literal
/// <c>DtTileCache</c> obstacle carving (see <see cref="NavMeshQuery.AddBoxObstacle"/>/<see cref="NavMeshQuery.AddCylinderObstacle"/>),
/// not a re-bake of anything: enabling, moving, or disabling this obstacle just queues an add/remove
/// against whichever agent-type queries it <see cref="AppliesTo"/>, applied over the next few
/// <see cref="NavMeshSystem.TickCrowds"/> ticks (see <see cref="NavMeshQuery.TickTileCache"/>) - a small,
/// bounded amount of work regardless of how much geometry the surrounding tile actually has, since the
/// tile's own heightfield was already rasterized once, at bake time, and never needs to be again.
/// <para/>
/// A crowd agent already walking through the tile this carves keeps walking, uninterrupted, through the
/// whole operation - the persistent navmesh a query's <c>DtTileCache</c> maintains is never torn down
/// and rebuilt the way a full rebake elsewhere in the engine still is, so there is no "rejoin the crowd"
/// step for a caller to worry about here at all.
/// <para/>
/// <see cref="Carve"/> off leaves the mesh untouched and instead joins each applicable crowd as an
/// immovable neighbour (see <see cref="NavMeshCrowd.AddStaticObstacle"/>): paths still lead straight
/// through this obstacle's footprint, but real agents steer around it locally via ordinary crowd
/// separation/avoidance. That costs nothing when the obstacle moves - repositioning a crowd slot is far
/// cheaper than re-cutting tiles - which is the right trade for something in near-constant motion, like a
/// patrolling vehicle: carving it would mean re-contouring the same tiles every time it crosses one.
/// <see cref="CarveOnlyStationary"/> combines both: carve a real hole while this obstacle is still, lift
/// the hole the moment it starts moving, and only routing around it locally (via crowd avoidance) while
/// it's in motion.
/// <para/>
/// This is the runtime, moving counterpart to <see cref="NavMeshModifierVolume"/>: that one paints an
/// area into the bake itself (permanent, and it can assign any area, not just "not walkable"); this one
/// is always a hole (or a locally-avoided obstacle), and only ever exists after the bake, layered on top
/// of it.
/// </summary>
[AddComponentMenu("Navigation/NavMesh Obstacle")]
public sealed class NavMeshObstacle : MonoBehaviour
{
    /// <summary>How far this obstacle's world-space box has to move (center or extents) before it
    /// re-carves - without this, floating-point jitter on an otherwise-still obstacle would queue a
    /// remove-and-re-add every single frame for no reason.</summary>
    private const float MoveThreshold = 0.05f;

    /// <summary>Which <c>DtTileCache</c> primitive this obstacle carves with.</summary>
    public NavMeshObstacleShape Shape = NavMeshObstacleShape.Box;

    /// <summary>Center of the box, local to this GameObject's transform. Also the capsule's center when
    /// <see cref="Shape"/> is <see cref="NavMeshObstacleShape.Capsule"/>.</summary>
    public Float3 Center;

    /// <summary>Size of the box, local to this GameObject's transform (rotation and scale are honoured).
    /// Only used when <see cref="Shape"/> is <see cref="NavMeshObstacleShape.Box"/>.</summary>
    public Float3 Size = Float3.One;

    /// <summary>Radius of the capsule, scaled by the larger of the transform's X/Z lossy scale. Only used
    /// when <see cref="Shape"/> is <see cref="NavMeshObstacleShape.Capsule"/>.</summary>
    public float Radius = 0.5f;

    /// <summary>Height of the capsule, scaled by the transform's Y lossy scale. Only used when
    /// <see cref="Shape"/> is <see cref="NavMeshObstacleShape.Capsule"/>.</summary>
    public float Height = 2f;

    /// <summary>On: cut a hole so paths route around this obstacle. Off: leave the mesh alone and join
    /// each applicable crowd as an immovable neighbour instead - see this class's own doc comment.</summary>
    public bool Carve = true;

    /// <summary>While <see cref="Carve"/> is on, lift the carved hole the moment this obstacle moves past
    /// <see cref="CarvingMoveThreshold"/> and fall back to crowd-avoidance steering until it has been
    /// still for <see cref="CarvingTimeToStationary"/> seconds, then re-cut the hole. Has no effect when
    /// <see cref="Carve"/> is off (nothing is ever carved to begin with).</summary>
    public bool CarveOnlyStationary;

    /// <summary>How far this obstacle has to move, in one <see cref="Update"/> tick's worth of travel,
    /// before <see cref="CarveOnlyStationary"/> considers it "moving" rather than merely jittering.</summary>
    public float CarvingMoveThreshold = 0.1f;

    /// <summary>How long this obstacle has to stay under <see cref="CarvingMoveThreshold"/> before
    /// <see cref="CarveOnlyStationary"/> re-cuts its hole.</summary>
    public float CarvingTimeToStationary = 0.5f;

    /// <summary>When true, this obstacle affects every agent type's navmesh. When false, only the types
    /// listed in <see cref="AgentTypeIds"/>.</summary>
    public bool AffectAllAgentTypes = true;

    /// <summary>Agent types this obstacle affects when <see cref="AffectAllAgentTypes"/> is false.</summary>
    public List<int> AgentTypeIds = [];

    /// <summary>This obstacle's currently-queued/active <c>DtTileCache</c> obstacle reference per agent
    /// type it has carved into - needed to remove the right one later, since each type's query owns an
    /// entirely separate tile cache. Populated only while actually carving (<see cref="Carve"/> on, and -
    /// if <see cref="CarveOnlyStationary"/> - currently settled).</summary>
    private readonly Dictionary<int, long> _obstacleRefsByType = [];

    /// <summary>This obstacle's crowd-avoidance slot per agent type, for <see cref="Carve"/> off (or,
    /// with <see cref="CarveOnlyStationary"/>, while currently in motion). See
    /// <see cref="NavMeshCrowd.AddStaticObstacle"/>.</summary>
    private readonly Dictionary<int, (NavMeshCrowd Crowd, NavMeshCrowdHandle Handle)> _crowdProxiesByType = [];

    /// <summary>The world-space placement last carved/proxied, so <see cref="Update"/> can tell whether
    /// this obstacle has actually moved enough to be worth re-carving.</summary>
    private Float3 _lastCenter;
    private Float3 _lastExtents;
    private bool _hasCarved;

    /// <summary>How long, in seconds, this obstacle has been under <see cref="CarvingMoveThreshold"/> -
    /// drives <see cref="CarveOnlyStationary"/>'s settle-then-carve behavior.</summary>
    private float _stationarySeconds;

    /// <summary>Whether <see cref="CarveOnlyStationary"/> currently considers this obstacle settled
    /// (carved) rather than moving (crowd-avoided). Meaningless when <see cref="CarveOnlyStationary"/>
    /// is off.</summary>
    private bool _isSettled;

    // Snapshot of the three fields that decide *which* mechanism this obstacle uses (carve vs. crowd
    // avoidance, and which carve shape) as of its last Refresh() - not just "did it move". A caller
    // flipping Carve, Shape or CarveOnlyStationary at runtime (rather than at construction) needs the
    // exact same migration Refresh() already does for a move: tear down whatever mechanism was active
    // and stand up whichever one the new configuration actually wants. Without this, e.g. toggling
    // Carve from true to false left the old tile-cache obstacle carved forever, since the avoidance
    // branch below only ever adds a *missing* crowd slot - it never knew a stale carve existed to remove.
    private bool _configCarve = true;
    private bool _configCarveOnlyStationary;
    private NavMeshObstacleShape _configShape;

    /// <summary>Whether this obstacle applies to a given agent type.</summary>
    public bool AppliesTo(int agentTypeId) => AffectAllAgentTypes || AgentTypeIds.Contains(agentTypeId);

    /// <summary>This obstacle's current oriented box, reduced to an axis-aligned center/half-extents
    /// pair - <c>DtTileCache</c>'s own box obstacles are always axis-aligned, so a rotated box is
    /// conservatively covered by its own world AABB rather than represented exactly.</summary>
    private (Float3 Center, Float3 Extents) WorldBox()
    {
        Float4x4 world = Float4x4.CreateTRS(Transform.Position, Transform.Rotation, Transform.LossyScale);
        Float3 half = Size * 0.5f;

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Float3 sign = new((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
            Float3 corner = Float4x4.TransformPoint(Center + sign * half, world);
            minX = Maths.Min(minX, corner.X); minY = Maths.Min(minY, corner.Y); minZ = Maths.Min(minZ, corner.Z);
            maxX = Maths.Max(maxX, corner.X); maxY = Maths.Max(maxY, corner.Y); maxZ = Maths.Max(maxZ, corner.Z);
        }

        Float3 min = new(minX, minY, minZ);
        Float3 max = new(maxX, maxY, maxZ);
        return ((min + max) * 0.5f, (max - min) * 0.5f);
    }

    /// <summary>This obstacle's current world-space capsule - upright only (no tilt), matching what
    /// <c>DtTileCache</c>'s own cylinder obstacle supports.</summary>
    private (Float3 BasePos, float Radius, float Height) WorldCapsule()
    {
        Float3 scale = Transform.LossyScale;
        float radiusScale = Maths.Max(Maths.Abs(scale.X), Maths.Abs(scale.Z));
        float heightScale = Maths.Abs(scale.Y);

        Float4x4 world = Float4x4.CreateTRS(Transform.Position, Transform.Rotation, Transform.LossyScale);
        Float3 centerWorld = Float4x4.TransformPoint(Center, world);
        float worldHeight = Height * heightScale;

        Float3 basePos = centerWorld - new Float3(0, worldHeight * 0.5f, 0);
        return (basePos, Radius * radiusScale, worldHeight);
    }

    /// <summary>The current placement's center and a conservative bounding radius/height, shape-agnostic -
    /// what <see cref="_crowdProxiesByType"/> needs, since a crowd-avoidance slot is always a single
    /// radius regardless of which <see cref="Shape"/> produced it.</summary>
    private (Float3 Center, float Radius, float Height) WorldAvoidanceCircle()
    {
        if (Shape == NavMeshObstacleShape.Capsule)
        {
            (Float3 basePos, float radius, float height) = WorldCapsule();
            return (basePos + new Float3(0, height * 0.5f, 0), radius, height);
        }

        (Float3 center, Float3 extents) = WorldBox();
        return (center, Maths.Max(extents.X, extents.Z), extents.Y * 2f);
    }

    /// <summary>Queues this obstacle's removal from every agent type it had previously carved, and drops
    /// every crowd-avoidance slot it was occupying.</summary>
    private void Uncarve()
    {
        if (Scene.IsValid())
        {
            NavMeshSystem system = NavMeshSystem.GetOrCreate(Scene);
            foreach (KeyValuePair<int, long> entry in _obstacleRefsByType)
                system.GetQuery(entry.Key)?.RemoveObstacle(entry.Value);

            foreach ((NavMeshCrowd crowd, NavMeshCrowdHandle handle) in _crowdProxiesByType.Values)
                crowd.RemoveAgent(handle);
        }

        _obstacleRefsByType.Clear();
        _crowdProxiesByType.Clear();
        _hasCarved = false;
    }

    /// <summary>Queues this obstacle's removal from wherever it previously carved/proxied, then queues it
    /// back in at its current position, for every agent type it <see cref="AppliesTo"/> that has a usable
    /// query. A type with no usable query yet (nothing baked at all) is simply skipped, not an error -
    /// see <see cref="Update"/> for how it still gets picked up once one exists.</summary>
    private void Refresh()
    {
        Uncarve();

        _configCarve = Carve;
        _configShape = Shape;
        _configCarveOnlyStationary = CarveOnlyStationary;

        if (Scene.IsNotValid()) return;

        FillMissing();

        (Float3 center, Float3 extents) = WorldBox();
        _lastCenter = center;
        _lastExtents = extents;
        _hasCarved = true;
    }

    /// <summary>Adds this obstacle to every applicable agent type that doesn't already have it - either a
    /// real carve (<see cref="Carve"/> on and, if <see cref="CarveOnlyStationary"/>, currently settled)
    /// or a crowd-avoidance slot otherwise. Never touches a type already present in either dictionary -
    /// callers that already cleared the old entries (<see cref="Refresh"/>, via <see cref="Uncarve"/>)
    /// get a full re-add; <see cref="Update"/>'s own retry for a type that had no query last time gets
    /// just that type, leaving the rest alone.</summary>
    private void FillMissing()
    {
        if (Scene.IsNotValid()) return;

        bool wantsRealCarve = Carve && (!CarveOnlyStationary || _isSettled);

        NavMeshSystem system = NavMeshSystem.GetOrCreate(Scene);
        foreach (NavMeshAgentTypeInfo type in NavMeshAgentTypes.Types)
        {
            if (!AppliesTo(type.Id)) continue;
            if (_obstacleRefsByType.ContainsKey(type.Id) || _crowdProxiesByType.ContainsKey(type.Id)) continue;

            if (wantsRealCarve)
            {
                NavMeshQuery? query = system.GetQuery(type.Id);
                if (query == null) continue;

                long obstacleRef = Shape == NavMeshObstacleShape.Capsule
                    ? AddCapsuleObstacle(query)
                    : AddBoxObstacleFor(query);
                if (obstacleRef != 0) _obstacleRefsByType[type.Id] = obstacleRef;
            }
            else
            {
                NavMeshCrowd? crowd = system.GetOrCreateCrowd(type.Id);
                if (crowd == null) continue;

                (Float3 center, float radius, float height) = WorldAvoidanceCircle();
                NavMeshCrowdHandle handle = crowd.AddStaticObstacle(center, radius, height);
                _crowdProxiesByType[type.Id] = (crowd, handle);
            }
        }
    }

    private long AddBoxObstacleFor(NavMeshQuery query)
    {
        (Float3 center, Float3 extents) = WorldBox();
        return query.AddBoxObstacle(center - extents, center + extents);
    }

    private long AddCapsuleObstacle(NavMeshQuery query)
    {
        (Float3 basePos, float radius, float height) = WorldCapsule();
        return query.AddCylinderObstacle(basePos, radius, height);
    }

    /// <summary>Repositions every crowd-avoidance slot this obstacle currently occupies to its current
    /// position, without touching any real carve - cheap to call every tick regardless of movement.</summary>
    private void RepositionCrowdProxies()
    {
        if (_crowdProxiesByType.Count == 0) return;

        (Float3 center, _, _) = WorldAvoidanceCircle();
        foreach ((NavMeshCrowd crowd, NavMeshCrowdHandle handle) in _crowdProxiesByType.Values)
            crowd.MoveStaticObstacle(handle, center);
    }

    /// <summary>Carves/proxies this obstacle in.</summary>
    public override void OnEnable() => Refresh();

    /// <summary>Un-carves this obstacle and drops every crowd-avoidance slot it held.</summary>
    public override void OnDisable() => Uncarve();

    /// <summary>Re-evaluates this obstacle every tick. A pure avoidance obstacle (<see cref="Carve"/>
    /// off, no <see cref="CarveOnlyStationary"/>) only ever repositions its existing crowd slots - never
    /// removes and re-adds them - so moving one costs nothing beyond a position write, matching this
    /// class's own doc comment. A carving obstacle only touches the tile cache when something that
    /// actually requires it changed: a real move/resize, a still-missing query finally appearing, or
    /// (with <see cref="CarveOnlyStationary"/>) a transition between settled and moving.</summary>
    public override void Update()
    {
        (Float3 center, Float3 extents) = WorldBox();

        // Carve, Shape or CarveOnlyStationary changed since the last Refresh() - migrate to whatever
        // mechanism the new configuration wants before anything else below (which only ever knows how
        // to maintain whichever mechanism is *already* active) runs.
        if (Carve != _configCarve || Shape != _configShape || CarveOnlyStationary != _configCarveOnlyStationary)
        {
            _isSettled = false;
            _stationarySeconds = 0f;
            Refresh();
            _lastCenter = center; _lastExtents = extents;
            return;
        }

        if (!Carve)
        {
            RepositionCrowdProxies();
            FillMissing(); // retry any type still missing a crowd
            _lastCenter = center; _lastExtents = extents;
            return;
        }

        if (CarveOnlyStationary)
        {
            bool movedPastCarvingThreshold = Float3.Distance(center, _lastCenter) >= CarvingMoveThreshold || Float3.Distance(extents, _lastExtents) >= CarvingMoveThreshold;
            if (movedPastCarvingThreshold)
            {
                _stationarySeconds = 0f;
                if (_isSettled) { _isSettled = false; Refresh(); }
                else RepositionCrowdProxies();
                _lastCenter = center; _lastExtents = extents;
                return;
            }

            _stationarySeconds += Time.DeltaTime;
            if (!_isSettled && _stationarySeconds >= CarvingTimeToStationary)
            {
                _isSettled = true;
                Refresh();
            }
            else if (!_isSettled)
            {
                RepositionCrowdProxies();
            }

            FillMissing(); // retry any type still missing a query/crowd
            return;
        }

        bool moved = !_hasCarved || Float3.Distance(center, _lastCenter) >= MoveThreshold || Float3.Distance(extents, _lastExtents) >= MoveThreshold;
        if (_hasCarved && !moved)
        {
            FillMissing();
            return;
        }

        Refresh();
    }

    /// <summary>Draws this obstacle's outline - a box or a capsule, matching <see cref="Shape"/>.</summary>
    public override void DrawGizmos()
    {
        Color color = new(1f, 0.3f, 0.2f, 1f);
        if (Shape == NavMeshObstacleShape.Capsule)
        {
            (Float3 basePos, float radius, float height) = WorldCapsule();
            Debug.DrawWireCapsule(basePos + new Float3(0, radius, 0), basePos + new Float3(0, Maths.Max(height - radius, radius), 0), radius, color);
        }
        else
        {
            Debug.PushMatrix(Float4x4.CreateTRS(Transform.Position, Transform.Rotation, Transform.LossyScale));
            Debug.DrawWireCube(Center, Size * 0.5f, color);
            Debug.PopMatrix();
        }
    }
}
