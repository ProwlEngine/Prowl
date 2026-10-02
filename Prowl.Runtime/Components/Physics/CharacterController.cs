// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A kinematic character that moves by sweeping its own shape through the world. It walks along the
/// ground, slides along walls and ceilings, climbs steps, snaps down stairs and slopes, and rides
/// whatever it stands on. It never becomes a rigidbody, so pushing things is left to the caller
/// through <see cref="Hits"/>.
/// </summary>
[AddComponentMenu("Physics/Character Controller")]
[ComponentIcon("")] // PersonRunning
public class CharacterController : MonoBehaviour
{
    /// <summary>Which sides of the controller met something during a move.</summary>
    [Flags]
    public enum CollisionFlags
    {
        None = 0,

        /// <summary>Something was met to the side, which is what stops horizontal movement.</summary>
        Sides = 1,

        /// <summary>Something was met overhead.</summary>
        Above = 2,

        /// <summary>Something was met underneath, which includes standing on the ground.</summary>
        Below = 4,
    }

    /// <summary>The shape the controller sweeps through the world.</summary>
    public enum ColliderShape
    {
        Capsule,
        Cylinder
    }

    public ColliderShape Shape = ColliderShape.Cylinder;
    public float Radius = 0.5f;
    public float Height = 1.8f;

    /// <summary>The gap kept between the controller and everything it touches, so casts never start inside.</summary>
    public float SkinWidth = 0.02f;

    /// <summary>Which layers the controller collides with.</summary>
    public LayerMask CollisionMask = LayerMask.Everything;

    /// <summary>The steepest surface in degrees that can be stood on and walked up.</summary>
    public float MaxSlopeAngle = 55.0f;

    /// <summary>How far the controller drops to stay on the ground while walking, which is what carries it down stairs and over crests.</summary>
    public float SnapDownDistance = 0.5f;

    /// <summary>The tallest ledge the controller walks straight up onto.</summary>
    public float StepSize = 0.3f;

    /// <summary>How far the controller's middle may go past the edge of a drop before it stops standing on the edge and falls.</summary>
    public float LedgeOverhang = 0.1f;

    /// <summary>Whether standing on something that moves or turns carries the controller with it.</summary>
    public bool RideMovingPlatforms = true;

    /// <summary>How many times a move may push the controller out of geometry it is already inside.</summary>
    public int MaxDepenetrationIterations = 8;

    /// <summary>How far past touching a depenetration pushes, so the next cast starts outside.</summary>
    public float DepenetrationBias = 0.001f;

    private const int MaxSlides = 10;
    private const int MaxPlanes = 5;
    private const float GroundProbeDistance = 0.05f;
    private const float LedgeProbeHeight = 0.1f;
    private const float LedgeProbeInset = 0.02f;
    private const float Epsilon = 1e-5f;
    private const float WallNormalY = 0.1f;
    private const int MaxPlatforms = 4;

    private static readonly Float3 Up = new(0f, 1f, 0f);
    private static readonly Float3 Down = new(0f, -1f, 0f);

    private Rigidbody3D _selfBody;
    private bool _selfBodyResolved;
    private readonly HashSet<Rigidbody3D> _ignoredBodies = new();
    private QueryFilter _filter;

    private readonly List<ShapeCastHit> _hits = new();
    private readonly List<ShapeCastHit> _overlaps = new();
    private readonly List<ShapeCastHit> _groundHits = new();
    private readonly Float3[] _planes = new Float3[MaxPlanes];
    private CollisionFlags _flags;
    private Float3 _achievedVelocity;

    private ShapeCastHit _groundHit;
    private Float3 _groundNormal = Up;
    private readonly Platform[] _platforms = new Platform[MaxPlatforms];
    private int _platformCount;
    private bool _leftGround;
    private bool _supported;
    private ShapeCastHit _support;

    private struct Platform
    {
        public GameObject Owner;
        public Collider Collider;
        public Float3 LocalPoint;
        public Quaternion Rotation;
    }

    private bool _failedHeightAttempt;
    private float _failedAttemptHeight;
    private float _failedAttemptRadius;

    /// <summary>Whether the controller is standing on something walkable.</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>
    /// Everything the last <see cref="Move"/> touched, in the order it was touched, including what it
    /// stands on and anything it had to push out of. Valid until the next move.
    /// </summary>
    public IReadOnlyList<ShapeCastHit> Hits => _hits;

    /// <summary>Which sides of the controller touched something during the last <see cref="Move"/>.</summary>
    public CollisionFlags Collisions => _flags;

    /// <summary>
    /// How far the controller travelled in the last <see cref="Move"/> under its own motion, divided by
    /// the frame time. Being carried by a platform is not included, that is <see cref="GroundVelocity"/>.
    /// </summary>
    public Float3 Velocity => _achievedVelocity;

    /// <summary>How fast what the controller stands on carried it during the last move.</summary>
    public Float3 GroundVelocity { get; private set; }

    /// <summary>How far what the controller stands on turned during the last move.</summary>
    public Quaternion GroundRotationDelta { get; private set; } = Quaternion.Identity;

    /// <summary>
    /// How many degrees about the up axis what the controller stands on turned during the last move.
    /// The controller never turns itself, so add this to whatever facing the game keeps.
    /// </summary>
    public float GroundYawDelta { get; private set; }

    /// <summary>The walkable surface normal under the controller, or up when it is not grounded.</summary>
    public Float3 GroundNormal => IsGrounded ? _groundNormal : Up;

    /// <summary>The angle in degrees of the surface under the controller, or zero when not grounded.</summary>
    public float GroundSlopeAngle => IsGrounded ? SlopeAngle(_groundNormal) : 0.0f;

    /// <summary>What the controller is standing on, or null when it is not grounded or the ground owns no collider.</summary>
    public Collider? GroundCollider => IsGrounded ? _groundHit.Collider : null;

    /// <summary>The rigidbody the controller is standing on, or null when it is not grounded.</summary>
    public Rigidbody3D? GroundBody => IsGrounded ? _groundHit.Rigidbody : null;

    /// <summary>The middle of the controller's shape in world space.</summary>
    public Float3 Center => ShapeCenter(GameObject.Transform.Position);

    /// <summary>The bottom of the controller in world space, which is where it stands.</summary>
    public Float3 Bottom => GameObject.Transform.Position;

    /// <summary>The top of the controller in world space.</summary>
    public Float3 Top => GameObject.Transform.Position + new Float3(0, Height, 0);

    private float MinWalkableNormalY => Maths.Cos(MaxSlopeAngle * Maths.Deg2Rad);

    /// <summary>Stops the controller colliding with a body that moves with it, such as a part of its ragdoll.</summary>
    public void IgnoreCollisionWith(Rigidbody3D body) => _ignoredBodies.Add(body);

    /// <summary>Undoes <see cref="IgnoreCollisionWith"/>.</summary>
    public void EnableCollisionWith(Rigidbody3D body) => _ignoredBodies.Remove(body);

    /// <summary>
    /// Re-resolves the rigidbody the controller must not collide with. Call after re-parenting, or after
    /// adding or removing a Rigidbody3D above this controller.
    /// </summary>
    public void ResolveSelfBody()
    {
        _selfBody = GetComponentInParent<Rigidbody3D>();
        _selfBodyResolved = true;
    }

    public override void OnEnable() => ResolveSelfBody();

    private QueryFilter BuildFilter()
    {
        if (!_selfBodyResolved) ResolveSelfBody();

        var filter = new QueryFilter(CollisionMask);
        if (_ignoredBodies.Count > 0) filter = filter.Ignoring(_ignoredBodies);
        return _selfBody.IsValid() ? filter.Ignoring(_selfBody) : filter;
    }

    /// <summary>
    /// Moves the controller by <paramref name="motion"/>, sliding along whatever it meets. A move that
    /// does not rise while grounded walks along the ground, climbing steps and following slopes, and
    /// only falls once there is no ground within <see cref="SnapDownDistance"/>. Returns which sides
    /// were touched, <see cref="Hits"/> holds what was touched.
    /// </summary>
    public CollisionFlags Move(Float3 motion)
    {
        _hits.Clear();
        _flags = CollisionFlags.None;
        _filter = BuildFilter();
        if (!IsFinite(motion)) motion = Float3.Zero;

        Float3 position = RidePlatforms(GameObject.Transform.Position);
        position = Depenetrate(position);
        Float3 start = position;

        bool walking = IsGrounded && motion.Y <= 0f;
        bool rising = !walking && motion.Y > 0f;
        _leftGround = false;
        _supported = false;

        if (walking)
        {
            position = WalkAlongGround(position, new Float3(motion.X, 0f, motion.Z), _groundNormal);
            if (!_leftGround && !SnapToGround(ref position) && motion.Y < 0f)
                position = SlideThroughAir(position, new Float3(0f, motion.Y, 0f));
        }
        else
        {
            position = SlideThroughAir(position, motion);
        }

        GameObject.Transform.Position = position;
        _achievedVelocity = Time.DeltaTime > 0.0f ? (position - start) / Time.DeltaTime : Float3.Zero;

        if (_leftGround || (rising && position.Y > start.Y + Epsilon)) SetAirborne();
        else ProbeGround(position);

        // Wedged between faces too steep to stand on, such as the bottom of a V, the fall is held up
        // just as surely as by flat ground, so it counts as ground rather than an endless fall.
        if (!IsGrounded && !_leftGround && _supported && motion.Y < 0f)
        {
            IsGrounded = true;
            _groundHit = _support;
            _groundNormal = Up;
        }

        RememberPlatforms(position);
        if (IsGrounded) _flags |= CollisionFlags.Below;

        return _flags;
    }

    /// <summary>
    /// Places the controller somewhere without sweeping there, then pushes it out of anything it
    /// landed inside. Use this for a spawn or a teleport.
    /// </summary>
    public void Teleport(Float3 position)
    {
        _hits.Clear();
        _flags = CollisionFlags.None;
        _filter = BuildFilter();
        _achievedVelocity = Float3.Zero;
        GroundVelocity = Float3.Zero;
        GroundRotationDelta = Quaternion.Identity;
        GroundYawDelta = 0f;
        _platformCount = 0;

        position = Depenetrate(position);
        GameObject.Transform.Position = position;
        ProbeGround(position);
        RememberPlatforms(position);
    }

    /// <summary>Sweeps the controller's own shape from where it stands, without moving it.</summary>
    public bool Cast(Float3 direction, float distance, out ShapeCastHit hit)
    {
        if (Float3.LengthSquared(direction) <= 0.0f || !IsFinite(direction))
        {
            hit = default;
            return false;
        }

        _filter = BuildFilter();
        return Sweep(GameObject.Transform.Position, Float3.Normalize(direction), distance, out hit);
    }

    /// <summary>Everything the controller's shape currently overlaps. Returns how many were written into <paramref name="results"/>.</summary>
    public int OverlapNow(List<ShapeCastHit> results)
    {
        _filter = BuildFilter();
        return OverlapShape(GameObject.Transform.Position, results);
    }

    // ----------------------------------------------------------------
    //  Walking
    // ----------------------------------------------------------------

    /// <summary>
    /// Walks a horizontal motion along the ground. Walkable surfaces met on the way become the new
    /// ground, anything steeper is climbed as a step if it is low enough and otherwise treated as an
    /// upright wall, so a slope too steep to walk or a ceiling sloping down to meet the floor is slid
    /// along rather than climbed or pressed into.
    /// </summary>
    private Float3 WalkAlongGround(Float3 position, Float3 horizontal, Float3 ground)
    {
        Float3 wish = horizontal;
        int planes = 0;
        int groundChanges = 0;

        for (int i = 0; i < MaxSlides && Float3.LengthSquared(horizontal) > Epsilon * Epsilon; i++)
        {
            Float3 move = AlongGround(horizontal, ground);
            float length = Float3.Length(move);
            Float3 direction = move / length;

            if (!Sweep(position, direction, length + SkinWidth, out ShapeCastHit hit))
                return position + move;

            Record(hit);
            float travelled = Maths.Clamp(hit.Distance - SkinWidth, 0f, length);
            position += direction * travelled;
            horizontal *= 1f - travelled / length;

            if (groundChanges < 4 && hit.Normal.Y >= MinWalkableNormalY)
            {
                ground = hit.Normal;
                groundChanges++;
                continue;
            }

            if (TryStepUp(ref position, ref horizontal, hit, out Float3 landing))
            {
                ground = landing;
                planes = 0;
                continue;
            }

            // A slope too steep to stand on is run up with whatever speed carries along it, then left
            // to the caller's gravity, rather than stopping dead as a wall would.
            // Only a run into the slope carries up it. Pressed against it, or just slid back off it, it holds
            // like a wall, or held input would hop up and slide back over and over.
            if (hit.Normal.Y > WallNormalY && RanInto(hit.Normal, length) && !StandableNormal(hit, position, out _))
            {
                Float3 up = Float3.ProjectOntoPlane(AlongGround(horizontal, ground), hit.Normal);
                if (up.Y > Epsilon)
                {
                    _leftGround = true;
                    return SlideThroughAir(position, up);
                }
            }

            // Following a ground that tilts the move up into a ceiling it already touches gets nowhere, so walk level instead.
            if (hit.Normal.Y < 0f && travelled < Epsilon && ground.Y < 1f - Epsilon)
            {
                ground = Up;
                continue;
            }

            Float3 wall = new(hit.Normal.X, 0f, hit.Normal.Z);
            if (Float3.LengthSquared(wall) < 1e-6f) break;
            if (planes == MaxPlanes) break;

            _planes[planes++] = Float3.Normalize(wall);
            horizontal = ClipToPlanes(horizontal, planes);
            if (Float3.Dot(horizontal, wish) <= 0f) break;
        }

        return position;
    }

    // Whether the last move was already heading into this surface at about the speed now asked for.
    private bool RanInto(Float3 normal, float length)
    {
        if (Time.DeltaTime <= 0f) return false;
        Float3 into = new(-normal.X, 0f, -normal.Z);
        if (Float3.LengthSquared(into) < 1e-6f) return false;
        Float3 last = new(_achievedVelocity.X, 0f, _achievedVelocity.Z);
        return Float3.Dot(last, Float3.Normalize(into)) * Time.DeltaTime > length * 0.5f;
    }

    // The motion along the ground plane heading the way asked, at the speed asked.
    private static Float3 AlongGround(Float3 horizontal, Float3 ground)
    {
        if (ground.Y < 1e-3f) return horizontal;
        float rise = -(ground.X * horizontal.X + ground.Z * horizontal.Z) / ground.Y;
        Float3 along = new(horizontal.X, rise, horizontal.Z);
        float length = Float3.Length(along);
        return length > Epsilon ? along * (Float3.Length(horizontal) / length) : horizontal;
    }

    /// <summary>
    /// Lifts over something in the way: up by the step height, forward by what is left of the move,
    /// then down onto ground that can be stood on. Landing on the edge of the step is fine, the top
    /// face is what decides whether it is walkable.
    /// </summary>
    private bool TryStepUp(ref Float3 position, ref Float3 horizontal, in ShapeCastHit blocker, out Float3 landing)
    {
        landing = Up;
        if (StepSize <= 0f || blocker.Normal.Y < -0.01f) return false;
        if (blocker.HitPoint.Y - position.Y > StepSize + SkinWidth * 2f) return false;

        float distance = Float3.Length(horizontal);
        if (distance < Epsilon) return false;
        Float3 forward = horizontal / distance;

        // Rising a little past the step size keeps clear of a step exactly that tall, the landing check
        // below is what holds the climb to the step size.
        float reach = StepSize + SkinWidth * 2f;
        float rise = reach;
        if (Sweep(position, Up, reach + SkinWidth, out ShapeCastHit above))
            rise = Maths.Max(0f, above.Distance - SkinWidth);
        if (rise < Epsilon) return false;

        Float3 raised = position + Up * rise;
        float ahead = distance;
        if (Sweep(raised, forward, distance + SkinWidth, out ShapeCastHit front))
            ahead = Maths.Clamp(front.Distance - SkinWidth, 0f, distance);
        if (ahead < Epsilon) return false;

        Float3 over = raised + forward * ahead;
        if (!Sweep(over, Down, rise + SkinWidth, out ShapeCastHit below)) return false;
        if (!StandableNormal(below, over, out landing, forward)) return false;

        Float3 landed = over + Down * Maths.Max(0f, below.Distance - SkinWidth);
        if (landed.Y < position.Y - SkinWidth || landed.Y > position.Y + StepSize + SkinWidth * 2f) return false;

        Record(below);
        position = landed;
        horizontal *= 1f - ahead / distance;
        return true;
    }

    /// <summary>Drops onto walkable ground within the snap distance, which is what keeps the controller on stairs going down.</summary>
    private bool SnapToGround(ref Float3 position)
    {
        float reach = Maths.Max(SnapDownDistance, GroundProbeDistance) + SkinWidth;
        if (!FindGround(position, reach, out ShapeCastHit hit, out _, out float drop)) return false;

        position += Down * drop;
        return true;
    }

    /// <summary>
    /// The ground below within reach. In the crease where walkable ground meets a steeper face, both are
    /// touched at once and the steep one can come first, so any standable surface met at about the same
    /// distance as the nearest counts. The drop is never past the nearest surface.
    /// </summary>
    private bool FindGround(Float3 position, float reach, out ShapeCastHit ground, out Float3 normal, out float drop)
    {
        ground = default;
        normal = Up;
        drop = 0f;

        int count = Shape == ColliderShape.Capsule
            ? GameObject.Scene.Physics.CapsuleCastAll(CapsuleBottom(position), CapsuleTop(position), EffectiveRadius, Down, reach, _groundHits, _filter)
            : GameObject.Scene.Physics.CylinderCastAll(ShapeCenter(position), EffectiveRadius, Height, Quaternion.Identity, Down, reach, _groundHits, _filter);
        if (count == 0) return false;

        float nearest = _groundHits[0].Distance;
        foreach (ShapeCastHit hit in _groundHits)
        {
            if (hit.Distance > nearest + SkinWidth) break;
            if (!StandableNormal(hit, position, out normal)) continue;

            ground = hit;
            drop = Maths.Max(0f, nearest - SkinWidth);
            return true;
        }

        return false;
    }

    // ----------------------------------------------------------------
    //  In the air
    // ----------------------------------------------------------------

    /// <summary>
    /// Moves freely, sliding along every surface met and along the crease where two meet. Coming down
    /// onto walkable ground ends the fall there and walks on with whatever horizontal motion is left.
    /// </summary>
    private Float3 SlideThroughAir(Float3 position, Float3 motion)
    {
        int planes = 0;

        for (int i = 0; i < MaxSlides; i++)
        {
            float length = Float3.Length(motion);
            if (length < Epsilon) break;
            Float3 direction = motion / length;

            if (!Sweep(position, direction, length + SkinWidth, out ShapeCastHit hit))
                return position + motion;

            Record(hit);
            float travelled = Maths.Clamp(hit.Distance - SkinWidth, 0f, length);
            position += direction * travelled;
            motion *= 1f - travelled / length;

            if (direction.Y < 0f && StandableNormal(hit, position, out Float3 ground))
            {
                _groundNormal = ground;
                return WalkAlongGround(position, new Float3(motion.X, 0f, motion.Z), ground);
            }

            if (planes == MaxPlanes) break;

            // Pushing into a slope too steep to stand on never gains height, so held input cannot pump
            // the controller up it. Upward speed it already has, from a jump or a run up, still carries.
            Float3 normal = hit.Normal;
            _planes[planes++] = normal;
            Float3 clipped = ClipToPlanes(motion, planes);
            if (normal.Y > 0f && clipped.Y > Maths.Max(motion.Y, 0f) + Epsilon)
            {
                Float3 wall = new(normal.X, 0f, normal.Z);
                if (Float3.LengthSquared(wall) > 1e-6f)
                {
                    _planes[planes - 1] = Float3.Normalize(wall);
                    clipped = ClipToPlanes(motion, planes);
                }
            }

            // A fall held up by the crease of two upward faces, as at the bottom of a V, is resting.
            if (motion.Y < 0f && clipped.Y > motion.Y * 0.1f && normal.Y > 0.05f && FormsCrease(normal, planes - 1))
            {
                _supported = true;
                _support = hit;
            }

            motion = clipped;
        }

        return position;
    }

    private bool FormsCrease(Float3 normal, int count)
    {
        for (int i = 0; i < count; i++)
            if (_planes[i].Y > 0.05f && Float3.Dot(_planes[i], normal) < 0.99f) return true;
        return false;
    }

    /// <summary>
    /// The part of a motion that runs along every plane met so far: along one plane if that keeps
    /// clear of the others, otherwise along the crease of two, otherwise nothing, which is a corner.
    /// </summary>
    private Float3 ClipToPlanes(Float3 motion, int count)
    {
        if (Clear(motion, count, -1)) return motion;

        for (int i = 0; i < count; i++)
        {
            if (Float3.Dot(motion, _planes[i]) >= 0f) continue;
            Float3 along = Float3.ProjectOntoPlane(motion, _planes[i]);
            if (Clear(along, count, i)) return along;
        }

        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                Float3 crease = Float3.Cross(_planes[i], _planes[j]);
                if (Float3.LengthSquared(crease) < 1e-8f) continue;
                crease = Float3.Normalize(crease);
                Float3 along = crease * Float3.Dot(motion, crease);
                if (Clear(along, count, i, j)) return along;
            }
        }

        return Float3.Zero;
    }

    private bool Clear(Float3 motion, int count, int skip, int skipToo = -1)
    {
        float tolerance = -1e-4f * Maths.Max(Float3.Length(motion), 1e-3f);
        for (int k = 0; k < count; k++)
        {
            if (k == skip || k == skipToo) continue;
            if (Float3.Dot(motion, _planes[k]) < tolerance) return false;
        }
        return true;
    }

    // ----------------------------------------------------------------
    //  Ground
    // ----------------------------------------------------------------

    private void ProbeGround(Float3 position)
    {
        if (FindGround(position, GroundProbeDistance + SkinWidth, out ShapeCastHit hit, out Float3 normal, out _))
        {
            IsGrounded = true;
            _groundHit = hit;
            _groundNormal = normal;
            Record(hit);
            return;
        }

        SetAirborne();
    }

    private void SetAirborne()
    {
        IsGrounded = false;
        _groundHit = default;
        _groundNormal = Up;
    }

    /// <summary>
    /// Whether a hit is something the controller can stand on, and the normal of the face it stands on.
    /// A rounded shape meets the edge of a face at an angle, so the face itself is found with a short ray
    /// just inside the contact. Standing on such an edge only holds until the controller's middle is
    /// <see cref="LedgeOverhang"/> past it, unless there is ground just beyond, as on a crest or a stair.
    /// </summary>
    private bool StandableNormal(in ShapeCastHit hit, Float3 position, out Float3 normal, Float3 inwardHint = default)
    {
        normal = hit.Normal;
        float minY = MinWalkableNormalY;

        Float3 inward = new(inwardHint.X, 0f, inwardHint.Z);
        if (Float3.LengthSquared(inward) <= Epsilon) inward = new Float3(-normal.X, 0f, -normal.Z);
        if (Float3.LengthSquared(inward) <= 1e-6f) return normal.Y >= minY;
        inward = Float3.Normalize(inward);

        if (!WalkableFaceBelow(hit.HitPoint + inward * LedgeProbeInset, LedgeProbeHeight, out Float3 face))
            return normal.Y >= minY;

        if (Float3.Dot(face, normal) > 0.98f)
        {
            normal = face;
            return true;
        }

        normal = face;
        Float3 outward = -inward;
        float overhang = Float3.Dot(new Float3(position.X - hit.HitPoint.X, 0f, position.Z - hit.HitPoint.Z), outward);
        if (overhang <= LedgeOverhang) return true;

        return WalkableFaceBelow(hit.HitPoint + outward * LedgeProbeInset, Maths.Max(StepSize, SnapDownDistance), out _);
    }

    // A walkable face straight down from just above a point, within reach below it.
    private bool WalkableFaceBelow(Float3 point, float reach, out Float3 normal)
    {
        normal = Up;
        if (!GameObject.Scene.Physics.Raycast(point + Up * LedgeProbeHeight, Down, out RaycastHit ray, LedgeProbeHeight + reach, _filter)) return false;
        if (!IsFinite(ray.Normal) || ray.Distance <= 0f || ray.Normal.Y < MinWalkableNormalY) return false;

        normal = ray.Normal;
        return true;
    }

    // ----------------------------------------------------------------
    //  Moving platforms
    // ----------------------------------------------------------------

    /// <summary>
    /// Carries the controller along with whatever it stood on at the end of the last move. Standing on
    /// several moving things at once, such as the crease between two rollers, it moves by their
    /// average, which is what lets one hand it over to the next.
    /// </summary>
    private Float3 RidePlatforms(Float3 position)
    {
        GroundVelocity = Float3.Zero;
        GroundRotationDelta = Quaternion.Identity;
        GroundYawDelta = 0f;
        if (!RideMovingPlatforms || !IsGrounded || _platformCount == 0) return position;

        Float3 sum = Float3.Zero;
        int moving = 0;
        for (int i = 0; i < _platformCount; i++)
        {
            ref Platform platform = ref _platforms[i];
            if (platform.Owner.IsNotValid()) continue;

            Float3 delta = platform.Owner.Transform.TransformPoint(platform.LocalPoint) - position;
            if (!IsFinite(delta) || Float3.LengthSquared(delta) < Epsilon * Epsilon) continue;
            sum += delta;
            moving++;
        }

        if (_platforms[0].Owner.IsValid())
        {
            Quaternion turn = _platforms[0].Owner.Transform.Rotation * Quaternion.Inverse(_platforms[0].Rotation);
            Float3 facing = turn * Float3.UnitZ;
            GroundRotationDelta = turn;
            GroundYawDelta = Maths.Atan2(facing.X, facing.Z) * Maths.Rad2Deg;
        }

        if (moving == 0) return position;

        Float3 start = position;
        QueryFilter filter = _filter;
        if (_platforms[0].Collider.IsValid()) _filter = _filter.Ignoring(_platforms[0].Collider);
        position = Carry(position, sum / moving);
        _filter = filter;

        if (Time.DeltaTime > 0f) GroundVelocity = (position - start) / Time.DeltaTime;
        return position;
    }

    /// <summary>Moves by a platform's motion, sliding along anything else in the way.</summary>
    private Float3 Carry(Float3 position, Float3 motion)
    {
        Float3 wish = motion;
        int planes = 0;

        for (int i = 0; i < MaxSlides && planes < MaxPlanes; i++)
        {
            float length = Float3.Length(motion);
            if (length < Epsilon) break;
            Float3 direction = motion / length;

            if (!Sweep(position, direction, length + SkinWidth, out ShapeCastHit hit))
                return position + motion;

            Record(hit);
            float travelled = Maths.Clamp(hit.Distance - SkinWidth, 0f, length);
            position += direction * travelled;
            motion *= 1f - travelled / length;

            _planes[planes++] = hit.Normal;
            motion = ClipToPlanes(motion, planes);
            if (Float3.Dot(motion, wish) <= 0f) break;
        }

        return position;
    }

    /// <summary>Notes everything under the controller so the next move can ride along with it.</summary>
    private void RememberPlatforms(Float3 position)
    {
        _platformCount = 0;
        if (!IsGrounded) return;

        AddPlatform(_groundHit.Collider, _groundHit.Transform, position);
        OverlapShape(position + Down * GroundProbeDistance, _overlaps);
        foreach (ShapeCastHit under in _overlaps)
        {
            if (_platformCount == MaxPlatforms) break;
            if (under.Normal.Y > 0.3f) AddPlatform(under.Collider, under.Transform, position);
        }
    }

    private void AddPlatform(Collider collider, Transform transform, Float3 position)
    {
        GameObject owner = collider.IsValid() ? collider.GameObject : transform?.GameObject;
        if (owner.IsNotValid()) return;

        for (int i = 0; i < _platformCount; i++)
            if (ReferenceEquals(_platforms[i].Owner, owner)) return;

        _platforms[_platformCount++] = new Platform
        {
            Owner = owner,
            Collider = collider,
            LocalPoint = owner.Transform.InverseTransformPoint(position),
            Rotation = owner.Transform.Rotation,
        };
    }

    // ----------------------------------------------------------------
    //  Overlaps
    // ----------------------------------------------------------------

    /// <summary>
    /// Pushes the controller out of anything it is inside, deepest contact first, which lets a wedge
    /// settle over a few passes rather than being over corrected in one.
    /// </summary>
    private Float3 Depenetrate(Float3 position)
    {
        for (int pass = 0; pass < MaxDepenetrationIterations; pass++)
        {
            if (OverlapShape(position, _overlaps) == 0) return position;

            int deepest = -1;
            for (int i = 0; i < _overlaps.Count; i++)
            {
                if (_overlaps[i].Penetration <= 0.0f || !IsFinite(_overlaps[i].Normal)) continue;
                if (Float3.LengthSquared(_overlaps[i].Normal) <= 0.0f) continue;
                if (deepest < 0 || _overlaps[i].Penetration > _overlaps[deepest].Penetration) deepest = i;
            }

            if (deepest < 0) return position;

            ShapeCastHit contact = _overlaps[deepest];
            position += Float3.Normalize(contact.Normal) * (contact.Penetration + DepenetrationBias);
            Record(contact);
        }

        return position;
    }

    private int OverlapShape(Float3 position, List<ShapeCastHit> results)
    {
        results.Clear();

        if (Shape == ColliderShape.Capsule)
            return GameObject.Scene.Physics.OverlapCapsule(CapsuleBottom(position), CapsuleTop(position), EffectiveRadius, results, _filter);

        return GameObject.Scene.Physics.OverlapCylinder(ShapeCenter(position), EffectiveRadius, Height, Quaternion.Identity, results, _filter);
    }

    /// <summary>Notes something the move touched and which side of the controller it was on. The same surface is only listed once.</summary>
    private void Record(in ShapeCastHit hit)
    {
        const float Facing = 0.5f;

        if (hit.Normal.Y > Facing) _flags |= CollisionFlags.Below;
        else if (hit.Normal.Y < -Facing) _flags |= CollisionFlags.Above;
        else _flags |= CollisionFlags.Sides;

        foreach (ShapeCastHit seen in _hits)
        {
            if (ReferenceEquals(seen.Shape, hit.Shape) && ReferenceEquals(seen.Collider, hit.Collider))
                return;
        }

        _hits.Add(hit);
    }

    // ----------------------------------------------------------------
    //  Resizing
    // ----------------------------------------------------------------

    /// <summary>Changes the height if the new size fits where the controller stands. Returns false and leaves it alone otherwise.</summary>
    public bool TrySetHeight(float newHeight)
    {
        float minHeight = Shape == ColliderShape.Capsule ? Radius * 2 : 0.1f;
        if (newHeight <= minHeight)
        {
            _failedHeightAttempt = false;
            return false;
        }

        if (!CheckShapeOverlap(GameObject.Transform.Position, newHeight, Radius))
        {
            Height = newHeight;
            _failedHeightAttempt = false;
            return true;
        }

        _failedHeightAttempt = true;
        _failedAttemptHeight = newHeight;
        _failedAttemptRadius = Radius;
        return false;
    }

    /// <summary>Changes the radius if the new size fits where the controller stands. Returns false and leaves it alone otherwise.</summary>
    public bool TrySetRadius(float newRadius)
    {
        if (newRadius <= 0) return false;
        if (Shape == ColliderShape.Capsule && newRadius * 2 >= Height) return false;
        if (CheckShapeOverlap(GameObject.Transform.Position, Height, newRadius)) return false;

        Radius = newRadius;
        return true;
    }

    private bool CheckShapeOverlap(Float3 position, float height, float radius)
    {
        float effectiveRadius = Maths.Max(radius - SkinWidth, 0.001f);
        QueryFilter filter = BuildFilter();

        if (Shape == ColliderShape.Capsule)
        {
            Float3 bottom = position + new Float3(0, effectiveRadius, 0);
            Float3 top = position + new Float3(0, Maths.Max(height - effectiveRadius, effectiveRadius + 0.001f), 0);
            return GameObject.Scene.Physics.CheckCapsule(bottom, top, effectiveRadius, filter);
        }

        return GameObject.Scene.Physics.CheckCylinder(position + new Float3(0, height * 0.5f, 0), effectiveRadius, height, Quaternion.Identity, filter);
    }

    // ----------------------------------------------------------------
    //  Shape
    // ----------------------------------------------------------------

    private Float3 ShapeCenter(Float3 position) => position + new Float3(0, Height * 0.5f, 0);

    private Float3 CapsuleBottom(Float3 position) => position + new Float3(0, EffectiveRadius, 0);

    // Jitter rejects a capsule of zero length, which a height at or below twice the radius would make.
    private Float3 CapsuleTop(Float3 position)
        => position + new Float3(0, Maths.Max(Height - EffectiveRadius, EffectiveRadius + 0.001f), 0);

    private float EffectiveRadius => Maths.Max(Radius - SkinWidth, 0.001f);

    private bool Sweep(Float3 position, Float3 direction, float distance, out ShapeCastHit hit)
    {
        if (Shape == ColliderShape.Capsule)
            return GameObject.Scene.Physics.CapsuleCast(CapsuleBottom(position), CapsuleTop(position), EffectiveRadius, direction, distance, out hit, _filter);

        return GameObject.Scene.Physics.CylinderCast(ShapeCenter(position), EffectiveRadius, Height, Quaternion.Identity, direction, distance, out hit, _filter);
    }

    private static float SlopeAngle(Float3 normal) => Maths.Acos(Maths.Clamp(normal.Y, -1.0f, 1.0f)) * Maths.Rad2Deg;

    private static bool IsFinite(Float3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public override void DrawGizmos()
    {
        if (GameObject.Scene.Physics == null) return;

        Float3 position = GameObject.Transform.Position;

        if (Shape == ColliderShape.Capsule)
            Debug.DrawWireCapsule(CapsuleBottom(position), CapsuleTop(position), Radius, Color.Cyan, 16);
        else
            Debug.DrawWireCylinder(ShapeCenter(position), Quaternion.Identity, Radius, Height, Color.Cyan, 16);

        if (_groundHit.Hit) _groundHit.DrawGizmos();

        if (_failedHeightAttempt)
        {
            if (Shape == ColliderShape.Capsule)
            {
                Float3 bottom = position + new Float3(0, _failedAttemptRadius, 0);
                Float3 top = position + new Float3(0, _failedAttemptHeight - _failedAttemptRadius, 0);
                Debug.DrawWireCapsule(bottom, top, _failedAttemptRadius, Color.Red, 16);
            }
            else
            {
                Float3 center = position + new Float3(0, _failedAttemptHeight * 0.5f, 0);
                Debug.DrawWireCylinder(center, Quaternion.Identity, _failedAttemptRadius, _failedAttemptHeight, Color.Red, 16);
            }
        }
    }
}
