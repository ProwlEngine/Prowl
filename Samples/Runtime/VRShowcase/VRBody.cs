// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Vector;

namespace VRShowcase;

/// <summary>
/// A fully simulated player body: a ball that rolls to walk, a leg standing on it and a torso that slides up and
/// down the leg to stand and crouch. Every part is a rigidbody held together by joints, so the player pushes and is
/// pushed by the world, rides whatever the ball stands on, and can never be squeezed into geometry, since standing
/// up under a ceiling simply fails to lift the torso.
/// <para>
/// The parts never turn, which keeps the body upright. Walking spins the ball, and the body is steered toward the
/// wanted speed relative to whatever it stands on, no faster than feet could push.
/// </para>
/// </summary>
public sealed class VRBody : Component
{
    public const float BallRadius = 0.18f;
    public const float LegRadius = 0.14f;
    public const float TorsoRadius = 0.16f;
    public const float HeadRadius = 0.11f;

    // A 70 kg player, most of it in the torso, with heavy feet so the ball grips and the body stays upright.
    private const float PlayerMass = 70f;
    private const float BallShare = 0.29f;
    private const float LegShare = 0.11f;

    // The bumper is a sphere a little bigger than the ball, sitting over its top, so walls and step edges meet it
    // rather than the gripping ball.
    private const float BumperClearance = 0.02f;
    private const float BumperRise = 0.12f;

    // The leg spans from just above the bottom of the ball to the hips. The torso hangs from the eyes, down to
    // the hips when standing tall and shorter when crouched, so it neither leaves a gap at the waist nor reaches
    // the floor.
    private const float LegCenter = 0.45f;
    private const float LegHeight = 0.6f;
    private const float Hips = 0.7f;
    private const float LowestTorso = 0.1f;
    private const float NeckBelowEyes = 0.12f;
    public const float LowestEyes = 0.6f;
    private const float ResizeStep = 0.03f;

    public Rigidbody3D Ball = null!;
    public Rigidbody3D Leg = null!;
    public Rigidbody3D Torso = null!;
    public PrismaticJoint Spine = null!;

    /// <summary>How hard the ball may speed up its spin, in radians per second per second.</summary>
    public float RollAcceleration = 160f;

    /// <summary>How quickly the body can change speed on the ground, in metres per second per second.</summary>
    public float GroundAcceleration = 20f;

    /// <summary>How quickly the body can change speed in the air, in metres per second per second.</summary>
    public float AirAcceleration = 4f;

    /// <summary>The tallest ledge the ball is lifted onto when walked into, like a stair.</summary>
    public float StepHeight = 0.35f;

    /// <summary>How fast the body rises when stepping up.</summary>
    public float StepLiftSpeed = 1.6f;

    /// <summary>The steepest ground that counts as standing, in degrees. Steeper ground is slid down.</summary>
    public float MaxWalkSlope = 45f;

    /// <summary>How fast the torso rises when standing up, slow enough that stopping at the top never lifts the feet.</summary>
    public float StandSpeed = 1.8f;

    /// <summary>The eye height the torso is driven toward, above the bottom of the ball.</summary>
    public float TargetEyeHeight;

    /// <summary>The ground speed the body is trying to move at, relative to whatever it stands on.</summary>
    public Float3 DesiredVelocity;

    public bool IsGrounded { get; private set; }
    public Float3 GroundVelocity { get; private set; }
    public Float3 GroundNormal { get; private set; } = Float3.UnitY;

    /// <summary>How fast whatever the body stands on turns about the vertical, in radians per second.</summary>
    public float GroundYawSpeed { get; private set; }

    /// <summary>The height the body was built for, which the spine's travel is measured from.</summary>
    public float BuiltEyeHeight { get; private set; }

    private const float HoldStiffness = 6f;
    private const float HoldRange = 0.3f;

    private readonly HashSet<Rigidbody3D> _parts = new();
    private bool _groundIsFixed;
    private bool _holding;
    private Float3 _holdPoint;
    private readonly HashSet<Rigidbody3D> _ignored = new();
    private CapsuleCollider _torsoShape = null!;
    private float _torsoLength;
    private bool _jumpRequested;
    private float _jumpSpeed;
    private bool _collisionsSet;
    private bool _braced;
    private bool _carried;

    public IReadOnlySet<Rigidbody3D> Parts => _parts;

    /// <summary>The body's own parts and anything it is holding, which its ground and step probes see through.</summary>
    public IReadOnlySet<Rigidbody3D> IgnoredBodies => _ignored;

    /// <summary>The eye position the view was placed from last frame, which the hands use to find the simulated frame the view trails.</summary>
    public Float3 ViewedEyes { get; set; }

    /// <summary>Whether a hand held on or leaned on something solid during the last step and pushed the body.</summary>
    public bool IsBraced { get; private set; }

    /// <summary>Whether something carried the body through the last step, like a rocket, wings, a rope or a zip line.</summary>
    public bool IsCarried { get; private set; }

    public float Mass => PlayerMass;

    /// <summary>Where the eyes are, from the torso's interpolated pose.</summary>
    public Float3 EyePosition => Torso.Transform.Position;

    /// <summary>The bottom of the ball, where the player stands.</summary>
    public Float3 FeetPosition => Ball.Transform.Position - new Float3(0f, BallRadius, 0f);

    public float EyeHeight => EyePosition.Y - FeetPosition.Y;

    /// <summary>Builds a body standing at <paramref name="feet"/> with eyes <paramref name="eyeHeight"/> above it.</summary>
    public static VRBody Create(Float3 feet, float eyeHeight)
    {
        var root = new GameObject("VR Body");
        var body = root.AddComponent<VRBody>();
        body.BuiltEyeHeight = eyeHeight;
        body.TargetEyeHeight = eyeHeight;

        var ball = Part(root, "Ball", feet + new Float3(0f, BallRadius, 0f), PlayerMass * BallShare, 1.2f);
        ball.AddComponent<SphereCollider>().Radius = BallRadius;
        body.Ball = ball;

        // The leg and its bumper never turn and never grip, so walls slide past them while only the spinning ball
        // touches the ground.
        var leg = Part(root, "Leg", feet + new Float3(0f, LegCenter, 0f), PlayerMass * LegShare, 0f);
        leg.Constraints = RigidbodyConstraints.FreezeRotation;
        var legShape = leg.AddComponent<CapsuleCollider>();
        legShape.Radius = LegRadius;
        legShape.Height = LegHeight;
        var bumper = new GameObject("Bumper");
        bumper.SetParent(leg.GameObject);
        bumper.Transform.LocalPosition = new Float3(0f, BallRadius + BumperRise - LegCenter, 0f);
        bumper.AddComponent<SphereCollider>().Radius = BallRadius + BumperClearance;
        body.Leg = leg;

        var socket = leg.AddComponent<BallSocketConstraint>();
        socket.Anchor = new Float3(0f, BallRadius - LegCenter, 0f);
        socket.ConnectedBody = body.Ball;

        // The torso's origin is the eyes, with the head just above and the trunk hanging below.
        var torso = Part(root, "Torso", feet + new Float3(0f, eyeHeight, 0f), PlayerMass * (1f - BallShare - LegShare), 0f);
        torso.Constraints = RigidbodyConstraints.FreezeRotation;
        body._torsoShape = torso.AddComponent<CapsuleCollider>();
        body._torsoShape.Radius = TorsoRadius;
        body.FitTorso(eyeHeight, force: true);
        var head = new GameObject("Head");
        head.SetParent(torso.GameObject);
        head.Transform.LocalPosition = new Float3(0f, 0.02f, 0f);
        head.AddComponent<SphereCollider>().Radius = HeadRadius;
        body.Torso = torso;

        // The spine lets the torso slide straight up and down the leg, and its motor holds the eye height.
        var spine = torso.AddComponent<PrismaticJoint>();
        spine.Axis = Float3.UnitY;
        spine.Pinned = true;
        spine.MinDistance = LowestEyes - eyeHeight;
        spine.MaxDistance = 0.4f;
        spine.HasMotor = true;
        spine.MotorMaxForce = 4000f;
        spine.ConnectedBody = body.Leg;
        body.Spine = spine;
        body.ViewedEyes = torso.Transform.Position;

        foreach (Rigidbody3D part in new[] { body.Ball, body.Leg, body.Torso })
        {
            body._parts.Add(part);
            body._ignored.Add(part);
        }
        return body;
    }

    private static Rigidbody3D Part(GameObject root, string name, Float3 position, float mass, float friction)
    {
        var go = new GameObject(name);
        go.SetParent(root);
        go.Transform.Position = position;
        var rb = go.AddComponent<Rigidbody3D>();
        rb.Mass = mass;
        rb.Friction = friction;
        rb.EnableSpeculativeContacts = true;
        return rb;
    }

    /// <summary>Jumps on the next step if the ball is on the ground then.</summary>
    public void Jump(float height) => (_jumpRequested, _jumpSpeed) = (true, MathF.Sqrt(2f * 9.81f * height));

    /// <summary>A hand pushing hard on the world this step, so the feet let that push move the body instead of holding it still.</summary>
    public void MarkBraced() => _braced = true;

    /// <summary>Something carrying the body this step, so neither the feet nor the air steering hold back the speed it gives.</summary>
    public void MarkCarried() => _carried = true;

    /// <summary>Pushes the whole body by an impulse, every part changing speed alike so it moves as one.</summary>
    public void Push(Float3 impulse)
    {
        Float3 change = impulse / PlayerMass;
        foreach (Rigidbody3D part in _parts)
            part.LinearVelocity += change;
    }

    /// <summary>Lets the ground and step probes see through something the hands are holding.</summary>
    public void IgnoreWhileHeld(Rigidbody3D body) => _ignored.Add(body);

    /// <summary>The hands never collide with the body, so it is never stood on one, and its probes see through them.</summary>
    public void AddHand(Rigidbody3D hand) => _ignored.Add(hand);

    public void RemoveHand(Rigidbody3D hand) => _ignored.Remove(hand);

    public void StopIgnoring(Rigidbody3D body)
    {
        if (!_parts.Contains(body)) _ignored.Remove(body);
    }

    /// <summary>Raised after a teleport with how far the body moved, so the hands can come along.</summary>
    public event Action<Float3>? Teleported;

    /// <summary>Moves the whole body so its feet land at <paramref name="feet"/>, keeping its shape and stopping it.</summary>
    public void Teleport(Float3 feet)
    {
        Float3 shift = feet - FeetPosition;
        _holding = false;
        foreach (Rigidbody3D part in _parts)
        {
            part.Transform.Position += shift;
            part.LinearVelocity = Float3.Zero;
            part.AngularVelocity = Float3.Zero;
        }
        Teleported?.Invoke(shift);
    }

    public override void FixedUpdate()
    {
        float dt = Time.FixedDeltaTime;
        PhysicsWorld physics = GameObject.Scene.Physics;

        // The parts are separate bodies, the ball and the torso are not joined directly and must not push apart.
        if (!_collisionsSet)
        {
            physics.IgnoreCollisionBetween(Ball, Torso);
            _collisionsSet = true;
        }

        IsBraced = _braced;
        IsCarried = _carried;
        _braced = _carried = false;
        QueryFilter filter = QueryFilter.Default.Ignoring(_ignored);
        ProbeGround(physics, filter, dt);
        Roll(dt);
        StepUp(physics, filter);
        HoldHeight();
        FitTorso(Torso.Position.Y - (Ball.Position.Y - BallRadius));

        if (_jumpRequested)
        {
            _jumpRequested = false;
            if (IsGrounded)
                foreach (Rigidbody3D part in _parts)
                {
                    Float3 v = part.LinearVelocity;
                    part.LinearVelocity = new Float3(v.X, MathF.Max(v.Y, GroundVelocity.Y) + _jumpSpeed, v.Z);
                }
        }
    }

    /// <summary>
    /// Sizes the trunk to the eye height: down to the hips at any standing height, so a tall player has no gap at
    /// the waist, and shorter as the player crouches, so it never presses into the floor and lifts the feet.
    /// </summary>
    private void FitTorso(float eyeHeight, bool force = false)
    {
        float neck = eyeHeight - NeckBelowEyes;
        float bottom = MathF.Max(LowestTorso, MathF.Min(Hips, neck - TorsoRadius * 2f));
        float length = MathF.Max(neck - bottom, TorsoRadius * 2f);
        if (!force && MathF.Abs(length - _torsoLength) < ResizeStep) return;

        _torsoLength = length;
        _torsoShape.Center = new Float3(0f, -NeckBelowEyes - length * 0.5f, 0f);
        _torsoShape.Height = length;
    }

    private void ProbeGround(PhysicsWorld physics, QueryFilter filter, float dt)
    {
        float minNormalY = MathF.Cos(MaxWalkSlope * Maths.Deg2Rad);
        IsGrounded = physics.SphereCast(Ball.Position + new Float3(0f, 0.05f, 0f), BallRadius * 0.9f, -Float3.UnitY, 0.1f, out ShapeCastHit hit, filter)
            && hit.Normal.Y >= minNormalY;

        GroundVelocity = Float3.Zero;
        GroundYawSpeed = 0f;
        GroundNormal = IsGrounded ? hit.Normal : Float3.UnitY;
        _groundIsFixed = IsGrounded && hit.Rigidbody.IsNotValid();
        if (!IsGrounded || hit.Rigidbody.IsNotValid()) return;

        // Where the ground will have carried the ball by the next step, so riding a turntable follows the circle
        // instead of drifting outward along its tangent.
        Rigidbody3D ground = hit.Rigidbody;
        Float3 spin = ground.AngularVelocity;
        float rate = Float3.Length(spin);
        Float3 offset = Ball.Position - ground.Position;
        Float3 carried = rate > 1e-4f ? Quaternion.AxisAngle(spin / rate, rate * dt) * offset : offset;
        GroundVelocity = ground.LinearVelocity + (carried - offset) / dt;
        GroundYawSpeed = spin.Y;
    }

    /// <summary>
    /// Moves the body toward the wanted speed relative to the ground under it, no faster than feet could push,
    /// and spins the ball to match. A ball rolling at v spins at up cross v over its radius, so standing still is
    /// zero spin. Steering the body itself rather than leaving it to the ball's grip is what stops it sliding on
    /// after the stick is let go and gives it the push to ride its bumper up a step.
    /// </summary>
    private void Roll(float dt)
    {
        Float3 wanted = new(DesiredVelocity.X, 0f, DesiredVelocity.Z);

        // Carried through the air, the body keeps whatever speed it is given.
        if (IsCarried && !IsGrounded)
        {
            _holding = false;
            return;
        }

        // Hands holding on carry the body, so feet and air steering stay out of their way unless the stick is pushed.
        if ((IsBraced || IsCarried) && Float3.LengthSquared(wanted) < 1e-4f)
        {
            _holding = false;
            return;
        }

        if (IsGrounded)
        {
            Float3 spin = Float3.Cross(Float3.UnitY, wanted) / BallRadius;
            Ball.AngularVelocity = MoveTowards(Ball.AngularVelocity, spin, RollAcceleration * dt);
            Steer(new Float3(GroundVelocity.X, 0f, GroundVelocity.Z) + wanted + HoldPlace(wanted), GroundAcceleration * dt);
            return;
        }

        _holding = false;

        // In the air there is nothing to push on, so only a little steering is allowed.
        Steer(wanted, AirAcceleration * dt);
    }

    /// <summary>
    /// Gravity keeps pulling along a slope during each step, after the steering has run, so a player standing still
    /// would creep away a little every step. Standing still on fixed ground remembers where the body stopped and
    /// steers gently back to it, which holds it in place on any slope it can stand on.
    /// </summary>
    private Float3 HoldPlace(Float3 wanted)
    {
        if (Float3.LengthSquared(wanted) > 1e-4f || !_groundIsFixed)
        {
            _holding = false;
            return Float3.Zero;
        }

        if (!_holding)
        {
            _holdPoint = Ball.Position;
            _holding = true;
        }

        // Shoved properly, the body stays where it was pushed to rather than springing back.
        Float3 drift = _holdPoint - Ball.Position;
        drift = new Float3(drift.X, 0f, drift.Z);
        if (Float3.LengthSquared(drift) > HoldRange * HoldRange)
        {
            _holdPoint = Ball.Position;
            return Float3.Zero;
        }
        return drift * HoldStiffness;
    }

    /// <summary>
    /// A ball meets a stair's edge nearly head on, so pushing it gives almost no lift. When something low blocks
    /// the way but the same sweep one step higher is clear, the body is lifted over the edge instead.
    /// </summary>
    private void StepUp(PhysicsWorld physics, QueryFilter filter)
    {
        Float3 wanted = new(DesiredVelocity.X, 0f, DesiredVelocity.Z);
        float speed = Float3.Length(wanted);
        if (!IsGrounded || speed < 0.1f) return;

        Float3 direction = wanted / speed;
        Float3 center = Ball.Position;
        float radius = BallRadius * 0.95f;
        const float reach = 0.15f;
        float minNormalY = MathF.Cos(MaxWalkSlope * Maths.Deg2Rad);

        if (!physics.SphereCast(center + new Float3(0f, 0.04f, 0f), radius - 0.03f, direction, reach, out ShapeCastHit low, filter) || low.Normal.Y >= minNormalY) return;
        if (physics.SphereCast(center + new Float3(0f, StepHeight, 0f), radius, direction, reach + 0.05f, out _, filter)) return;

        foreach (Rigidbody3D part in _parts)
        {
            Float3 v = part.LinearVelocity;
            if (v.Y < StepLiftSpeed) part.LinearVelocity = new Float3(v.X, StepLiftSpeed, v.Z);
        }
    }

    private void Steer(Float3 target, float maxChange)
    {
        Float3 velocity = Torso.LinearVelocity;
        Float3 across = new(velocity.X, 0f, velocity.Z);
        Float3 change = MoveTowards(across, target, maxChange) - across;
        foreach (Rigidbody3D part in _parts)
            part.LinearVelocity += change;
    }

    /// <summary>Drives the spine toward the wanted eye height. A ceiling stops the torso rising, so the head never goes through it.</summary>
    private void HoldHeight()
    {
        float target = Maths.Clamp(TargetEyeHeight, LowestEyes, BuiltEyeHeight + 0.4f) - BuiltEyeHeight;
        float error = target - Spine.CurrentDistance;
        Spine.MotorTargetVelocity = Maths.Clamp(error * 25f, -4f, StandSpeed);
    }

    private static Float3 MoveTowards(Float3 from, Float3 to, float maxStep)
    {
        Float3 delta = to - from;
        float length = Float3.Length(delta);
        return length <= maxStep || length < 1e-6f ? to : from + delta * (maxStep / length);
    }
}
