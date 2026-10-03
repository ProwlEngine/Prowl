// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// How a tyre's grip grows with slip to a peak, then eases down to what it keeps while sliding. Slip is a ratio:
/// along the tyre, how much faster or slower it turns than the ground passes under it; across, the tangent of the
/// angle between where the tyre points and where it travels.
/// </summary>
public struct WheelFrictionCurve
{
    /// <summary>The slip where grip peaks.</summary>
    public float ExtremumSlip;

    /// <summary>The share of the tyre's grip at the peak, usually 1.</summary>
    public float ExtremumValue;

    /// <summary>The slip past which the tyre is fully sliding.</summary>
    public float AsymptoteSlip;

    /// <summary>The share of grip left once fully sliding. Below the peak, so a slide is easier to keep than to start.</summary>
    public float AsymptoteValue;

    public WheelFrictionCurve(float extremumSlip, float extremumValue, float asymptoteSlip, float asymptoteValue)
    {
        ExtremumSlip = extremumSlip;
        ExtremumValue = extremumValue;
        AsymptoteSlip = asymptoteSlip;
        AsymptoteValue = asymptoteValue;
    }

    public static WheelFrictionCurve Forward => new(0.15f, 1f, 0.6f, 0.75f);
    public static WheelFrictionCurve Sideways => new(0.15f, 1f, 0.5f, 0.75f);

    /// <summary>The share of the tyre's grip in use at a slip, rising from zero to the peak and easing to the asymptote beyond.</summary>
    public readonly float Evaluate(float slip)
    {
        slip = Maths.Abs(slip);
        float extremum = Maths.Max(ExtremumSlip, 1e-4f);
        if (slip <= extremum)
        {
            float t = slip / extremum;
            return ExtremumValue * t * (2.0f - t);
        }

        if (slip >= AsymptoteSlip) return AsymptoteValue;
        float u = (slip - extremum) / Maths.Max(AsymptoteSlip - extremum, 1e-4f);
        u = u * u * (3.0f - 2.0f * u);
        return ExtremumValue + (AsymptoteValue - ExtremumValue) * u;
    }

    /// <summary>How fast grip grows with slip at zero slip.</summary>
    public readonly float InitialStiffness => 2.0f * ExtremumValue / Maths.Max(ExtremumSlip, 1e-4f);
}

/// <summary>What a grounded wheel is touching, and how hard.</summary>
public struct WheelHit
{
    public Float3 Point;
    public Float3 Normal;

    /// <summary>The way the tyre rolls along the ground.</summary>
    public Float3 ForwardDir;

    /// <summary>Across the tyre along the ground, toward the wheel's right.</summary>
    public Float3 SidewaysDir;

    /// <summary>How fast the ground under the tyre is moving, for a wheel on a platform or a trailer. Zero on static ground.</summary>
    public Float3 GroundVelocity;

    /// <summary>The load pressing the tyre into the ground, in newtons.</summary>
    public float Force;
    public float ForwardSlip;
    public float SidewaysSlip;

    /// <summary>The collider the wheel rests on, or null on terrain.</summary>
    public Collider? Collider;

    /// <summary>The rigidbody the wheel rests on, or null on static ground.</summary>
    public Rigidbody3D? Rigidbody;
    public GameObject? GameObject;
}

/// <summary>Scales the grip of every WheelCollider rolling on this GameObject's colliders or terrain, for ice, mud or a racing surface.</summary>
[AddComponentMenu("Physics/Wheel Surface")]
public sealed class WheelSurface : MonoBehaviour
{
    public float Grip = 1.0f;
}

/// <summary>
/// Links the two wheels of an axle so the body rolls less in corners: the more compressed side is pushed up and the
/// other pulled down, by the difference in compression times <see cref="Stiffness"/>.
/// </summary>
[AddComponentMenu("Physics/Anti Roll Bar")]
public sealed class AntiRollBar : MonoBehaviour
{
    public WheelCollider? Left;
    public WheelCollider? Right;

    /// <summary>Newtons per metre of difference in compression between the two wheels.</summary>
    public float Stiffness = 20000.0f;

    public override void FixedUpdate()
    {
        if (Left.IsNotValid() || Right.IsNotValid()) return;
        Rigidbody3D? body = Left!.Body;
        if (body.IsNotValid() || body != Right!.Body) return;

        float difference = (Left.SuspensionCompression * Left.SuspensionDistance) - (Right.SuspensionCompression * Right.SuspensionDistance);
        float force = difference * Stiffness;
        if (Left.IsGrounded) body!.AddForceAtPosition(Left.Transform.Up * force, Left.Transform.Position);
        if (Right.IsGrounded) body!.AddForceAtPosition(Right.Transform.Up * -force, Right.Transform.Position);
    }
}

/// <summary>
/// A raycast-vehicle wheel. Ground contact is a grid of downward rays across the wheel face. Each ray finds the
/// surface below it, and the wheel's contact with that surface is solved exactly for its round profile, so it sits
/// precisely on slopes and still rides over bumps and kerbs between rays. Suspension is a spring and damper tuned
/// from a natural frequency, and grip follows a friction curve on slip ratio and slip angle, solved together with
/// the wheel's spin so it stays stable for any mass and holds still on slopes and moving platforms.
///
/// The wheel applies forces to the nearest enabled <see cref="Rigidbody3D"/> above it; it does not add a collision
/// shape of its own. Place the wheel GameObject so its position marks the top of the suspension travel; the wheel
/// hangs below along -up. Positive <see cref="SteerAngle"/> turns toward the wheel's right.
/// </summary>
[AddComponentMenu("Physics/Wheel Collider")]
[ComponentIcon("")] // CarSide
public sealed class WheelCollider : MonoBehaviour
{
    // Geometry
    [SerializeField] private float radius = 0.35f;
    [SerializeField] private float width = 0.25f;
    [SerializeField] private float camber = 0.0f;

    // Ground detection
    [SerializeField] private int forwardRayCount = 5;
    [SerializeField] private int sideRayCount = 3;
    [SerializeField] private LayerMask layerMask = LayerMask.Everything;
    [SerializeField] private bool depenetrate = true;

    // Suspension
    [SerializeField] private float suspensionDistance = 0.3f;
    [SerializeField] private float suspensionFrequency = 2.0f;
    [SerializeField] private float suspensionDampingRatio = 0.5f;
    [SerializeField] private float sprungMass = 0.0f;
    [SerializeField] private float preload = 0.0f;

    // Grip
    [SerializeField] private float forwardFriction = 1.6f;
    [SerializeField] private float sidewaysFriction = 1.8f;
    [SerializeField] private WheelFrictionCurve forwardCurve = WheelFrictionCurve.Forward;
    [SerializeField] private WheelFrictionCurve sidewaysCurve = WheelFrictionCurve.Sideways;
    [SerializeField] private float weightTransfer = 0.0f;
    [SerializeField] private float camberThrust = 0.0f;

    // Spin
    [SerializeField] private float wheelMass = 15.0f;
    [SerializeField] private float dragTorque = 30.0f;
    [SerializeField] private float spinDamping = 0.25f;
    [SerializeField] private float maxAngularVelocity = 600.0f;

    /// <summary>Steering angle in radians about the suspension axis, positive toward the wheel's right. Set by a controller.</summary>
    public float SteerAngle;

    /// <summary>Drive torque in N m applied to the wheel spin. Set by a controller each frame.</summary>
    public float MotorTorque;

    /// <summary>Brake torque in N m opposing the wheel spin. Set by a controller each frame. Negative values count as zero.</summary>
    public float BrakeTorque;

    /// <summary>Optional visual wheel transform. When set it is placed at the wheel centre and rotated
    /// for steering (about the suspension axis) and spin (about the axle) each frame. Keep this separate
    /// from the WheelCollider's own GameObject, which marks the fixed suspension mount used by physics.</summary>
    public Transform VisualTransform;

    // Below this speed slip is measured against it instead of the ground speed, which makes the tyre stiff enough
    // at a standstill to hold the car rather than letting it creep.
    private const float LowSpeed = 0.3f;

    // Hits steeper than this from the suspension axis are walls the wheel is pressed against, not ground it rests on.
    private const float MinSupportCos = 0.35f;

    // A tyre slower than this over the ground is held to the spot where it stopped, until it slides faster than the
    // release speed or its grip gives out. Each substep pulls it back by a share of how far it has crept.
    private const float AnchorSpeed = 0.05f;
    private const float ReleaseSpeed = 0.3f;
    private const float AnchorPull = 0.2f;

    // Ground this close below the tyre is kept in view, so a wheel resting on a very stiff spring that lifts off for
    // a moment meets the ground again within the step instead of falling for the whole of it.
    private const float ContactMargin = 0.03f;

    private static readonly Dictionary<Rigidbody3D, List<WheelCollider>> s_wheelsOnBody = new();

    // Runtime state
    private Rigidbody3D? rb;
    private bool _bound;
    private PhysicsWorld? _world;
    private DynamicTree.RayCastFilterPre _rayFilter = null!;
    private RigidBody? _rayCar;
    private float displacement;
    private float _reach;
    private float _previousDisplacement;
    private float _side = 1.0f;
    private bool onFloor;
    private float angularVelocity;
    private float wheelRotation;
    private Float3 contactPoint;
    private Float3 contactNormal = Float3.UnitY;
    private RigidBody? groundBody;
    private IDynamicTreeProxy? _groundProxy;
    private IDynamicTreeProxy? _surfaceProxy;
    private float _surfaceGrip = 1.0f;
    private float _groundedTime, _airTime;
    private float _sleptSteer, _sleptBrake;
    private bool _anchored;
    private JVector _anchor;
    private RigidBody? _anchorGround;

    // Per-step cache, set in PreStep and used by the per-substep forces in PreSubStep.
    private JVector _contactJ, _mountJ, _upJ, _wheelUpJ, _planeFwd, _planeRight, _bodyAtStep, _groundAtStep;
    private float _springK, _damperC, _damperCap, _preloadForce, _depenetrationStiffness;
    private float _sprungMass;
    private float _load, _fLong, _fLat;
    private float _forwardSlip, _sidewaysSlip;

    // Public configuration
    public float Radius { get => radius; set => radius = Maths.Max(0.01f, value); }
    public float Width { get => width; set => width = Maths.Max(0.01f, value); }

    /// <summary>Wheel tilt in degrees about the forward axis, mirrored per side so one value suits both. Positive leans the tops inward.</summary>
    public float Camber { get => camber; set => camber = value; }

    /// <summary>Rays along the wheel's length. Always odd, so one ray sits under the wheel's centre.</summary>
    public int ForwardRayCount { get => forwardRayCount; set => forwardRayCount = Odd(value); }

    /// <summary>Rays across the wheel's width.</summary>
    public int SideRayCount { get => sideRayCount; set => sideRayCount = Maths.Max(1, value); }

    /// <summary>The layers the wheel can rest on, on top of the layer collision rules for its body.</summary>
    public LayerMask LayerMask { get => layerMask; set => layerMask = value; }

    /// <summary>Push the wheel (and chassis) out along the contact normal when the suspension bottoms out against an object.</summary>
    public bool Depenetrate { get => depenetrate; set => depenetrate = value; }
    public float SuspensionDistance { get => suspensionDistance; set => suspensionDistance = Maths.Max(0.01f, value); }

    /// <summary>The spring's natural frequency in Hz, from 0.1 to 30. Higher is stiffer.</summary>
    public float SuspensionFrequency { get => suspensionFrequency; set => suspensionFrequency = Maths.Clamp(value, 0.1f, 30.0f); }
    public float SuspensionDampingRatio { get => suspensionDampingRatio; set => suspensionDampingRatio = Maths.Max(0.0f, value); }

    /// <summary>Sprung mass (kg) this wheel supports. 0 shares the body's mass evenly between its wheels.</summary>
    public float SprungMass { get => sprungMass; set => sprungMass = Maths.Max(0.0f, value); }

    /// <summary>The share of this wheel's weight the spring carries before it compresses at all, 0 to 1. Higher sits the car higher.</summary>
    public float Preload { get => preload; set => preload = Maths.Clamp(value, 0.0f, 1.0f); }
    public float ForwardFriction { get => forwardFriction; set => forwardFriction = Maths.Max(0.0f, value); }
    public float SidewaysFriction { get => sidewaysFriction; set => sidewaysFriction = Maths.Max(0.0f, value); }
    public WheelFrictionCurve ForwardCurve { get => forwardCurve; set => forwardCurve = value; }
    public WheelFrictionCurve SidewaysCurve { get => sidewaysCurve; set => sidewaysCurve = value; }

    /// <summary>
    /// Where grip pushes on the body, 0 to 1. At 0 it pushes at the body's centre height, so braking and cornering
    /// never pitch or roll it. At 1 it pushes at the ground, for full weight transfer.
    /// </summary>
    public float WeightTransfer { get => weightTransfer; set => weightTransfer = Maths.Clamp(value, 0.0f, 1.0f); }

    /// <summary>Sideways grip a leaning wheel gives toward the side it leans, as a share of its load per unit of lean. For motorcycles.</summary>
    public float CamberThrust { get => camberThrust; set => camberThrust = Maths.Max(0.0f, value); }
    public float WheelMass { get => wheelMass; set => wheelMass = Maths.Max(0.01f, value); }

    /// <summary>Rolling resistance torque (N*m). Brakes the wheel toward rest so the car doesn't coast on perturbations.</summary>
    public float DragTorque { get => dragTorque; set => dragTorque = Maths.Max(0.0f, value); }

    /// <summary>Torque in N m per rad/s that slows the spin at all times, in the air too.</summary>
    public float SpinDamping { get => spinDamping; set => spinDamping = Maths.Max(0.0f, value); }

    /// <summary>The fastest the wheel may spin, in rad/s. 0 for no limit.</summary>
    public float MaxAngularVelocity { get => maxAngularVelocity; set => maxAngularVelocity = Maths.Max(0.0f, value); }

    // Public state
    public bool IsGrounded => onFloor && _reach > 0.0f;
    public float SuspensionCompression => displacement / suspensionDistance;
    public float AngularVelocity => angularVelocity;
    public float WheelRotation => wheelRotation;
    public float Rpm => angularVelocity * 60.0f / (2.0f * Maths.PI);
    public Float3 ContactPoint => contactPoint;
    public Float3 ContactNormal => contactNormal;

    /// <summary>The load pressing the tyre into the ground, in newtons. Zero in the air.</summary>
    public float Load => IsGrounded ? _load : 0.0f;

    /// <summary>Seconds the wheel has touched the ground without a break, and seconds it has been off it.</summary>
    public float GroundedTime => _groundedTime;
    public float AirTime => _airTime;

    /// <summary>The body this wheel pushes on.</summary>
    public Rigidbody3D? Body => rb;

    /// <summary>
    /// How fast the tyre's surface slides over the ground along its heading, in m/s. Positive while the ground runs
    /// faster than the tyre turns (braking, a locked wheel), negative for wheelspin. Zero in the air.
    /// </summary>
    public float ForwardSlip => _forwardSlip;

    /// <summary>How fast the tyre slides across its heading, in m/s, positive toward the wheel's right. Zero in the air.</summary>
    public float SidewaysSlip => _sidewaysSlip;

    public override void OnValidate()
    {
        radius = Maths.Max(0.01f, radius);
        width = Maths.Max(0.01f, width);
        forwardRayCount = Odd(forwardRayCount);
        sideRayCount = Maths.Max(1, sideRayCount);
        suspensionDistance = Maths.Max(0.01f, suspensionDistance);
        suspensionFrequency = Maths.Clamp(suspensionFrequency, 0.1f, 30.0f);
        suspensionDampingRatio = Maths.Max(0.0f, suspensionDampingRatio);
        sprungMass = Maths.Max(0.0f, sprungMass);
        preload = Maths.Clamp(preload, 0.0f, 1.0f);
        forwardFriction = Maths.Max(0.0f, forwardFriction);
        sidewaysFriction = Maths.Max(0.0f, sidewaysFriction);
        weightTransfer = Maths.Clamp(weightTransfer, 0.0f, 1.0f);
        camberThrust = Maths.Max(0.0f, camberThrust);
        dragTorque = Maths.Max(0.0f, dragTorque);
        wheelMass = Maths.Max(0.01f, wheelMass);
        spinDamping = Maths.Max(0.0f, spinDamping);
        maxAngularVelocity = Maths.Max(0.0f, maxAngularVelocity);
    }

    private static int Odd(int count) => Maths.Max(1, count) | 1;

    public override void OnEnable()
    {
        _rayFilter = proxy => _world != null && _rayCar != null && _world.CastAccepts(_rayCar, proxy, layerMask);
        Bind();
        if (rb.IsNotValid())
        {
            Debug.LogError("WheelCollider requires an enabled Rigidbody3D on itself or a parent GameObject.");
            return;
        }

        // PreStep finds the ground once a step; PreSubStep applies suspension, grip and spin every substep.
        _world = GameObject.Scene.Physics;
        _world.PreStep += OnPreStep;
        _world.PreSubStep += OnPreSubStep;
    }

    public override void OnDisable()
    {
        Unbind();
        if (_world != null)
        {
            _world.PreStep -= OnPreStep;
            _world.PreSubStep -= OnPreSubStep;
            _world = null;
        }
    }

    public override void Update()
    {
        if (VisualTransform == null) return;

        // The mount transform (this GameObject) gives the base wheel frame: up = suspension axis,
        // right = axle. Steer rotates about the suspension axis, camber tilts about forward, spin about the axle.
        Quaternion steer = Quaternion.AxisAngle(Float3.UnitY, SteerAngle);
        Quaternion camberQ = Quaternion.AxisAngle(Float3.UnitZ, CamberRadians());
        Quaternion spin = Quaternion.AxisAngle(Float3.UnitX, wheelRotation);
        VisualTransform.Rotation = Transform.Rotation * steer * camberQ * spin;
        VisualTransform.Position = GetWheelCenter();
    }

    /// <summary>Finds the body again and recounts its wheels. Wheels do this themselves each step, so it is only needed to see the result at once.</summary>
    public void Recalculate() => Bind();

    /// <summary>The wheel centre for drawing, between the last two physics steps like the interpolated body it hangs from.</summary>
    public Float3 GetWheelCenter()
    {
        float shown = Maths.Lerp(_previousDisplacement, displacement, Time.FixedAlpha);
        return Transform.Position - Transform.Up * (suspensionDistance - shown);
    }

    /// <summary>What the wheel rests on. False in the air.</summary>
    public bool GetGroundHit(out WheelHit hit)
    {
        hit = default;
        if (!IsGrounded) return false;

        hit.Point = contactPoint;
        hit.Normal = contactNormal;
        hit.ForwardDir = ToF(_planeFwd);
        hit.SidewaysDir = ToF(_planeRight);
        hit.Force = _load;
        hit.ForwardSlip = _forwardSlip;
        hit.SidewaysSlip = _sidewaysSlip;
        if (groundBody != null && groundBody.MotionType != MotionType.Static)
        {
            JVector arm = contactPoint.ToJitter() - groundBody.Position;
            hit.GroundVelocity = ToF(groundBody.Velocity + JVector.Cross(groundBody.AngularVelocity, arm));
        }
        if (_world != null && _groundProxy != null)
        {
            MonoBehaviour owner = _world.GetProxyOwner(_groundProxy);
            if (owner.IsValid())
            {
                hit.Collider = owner as Collider;
                hit.GameObject = owner.GameObject;
                hit.Rigidbody = owner.GetComponentInParent<Rigidbody3D>();
            }
        }
        return true;
    }

    // ----------------------------------------------------------------
    //  Which body the wheel drives
    // ----------------------------------------------------------------

    private Rigidbody3D? FindBody()
    {
        for (Transform t = Transform; t != null; t = t.Parent)
        {
            Rigidbody3D body = t.GameObject.GetComponent<Rigidbody3D>();
            if (body.IsValid() && body.Enabled) return body;
        }
        return null;
    }

    // Joins the wheel to the body it hangs from, moving it over if it was re-parented since the last step.
    private void Bind()
    {
        Rigidbody3D? found = FindBody();
        if (_bound && ReferenceEquals(found, rb)) return;

        Unbind();
        rb = found;
        if (rb.IsNotValid()) return;
        if (!s_wheelsOnBody.TryGetValue(rb!, out List<WheelCollider>? wheels)) s_wheelsOnBody[rb!] = wheels = new List<WheelCollider>();
        wheels.Add(this);
        _bound = true;
    }

    private void Unbind()
    {
        if (_bound && !ReferenceEquals(rb, null) && s_wheelsOnBody.TryGetValue(rb, out List<WheelCollider>? wheels))
        {
            wheels.Remove(this);
            if (wheels.Count == 0) s_wheelsOnBody.Remove(rb);
        }
        _bound = false;
    }

    private int WheelCount => _bound && s_wheelsOnBody.TryGetValue(rb!, out List<WheelCollider>? wheels) ? Maths.Max(1, wheels.Count) : 1;

    // ----------------------------------------------------------------
    //  Once a step: find the ground
    // ----------------------------------------------------------------

    private void OnPreStep(float timeStep)
    {
        if (timeStep <= 0.0f) return;
        Bind();
        if (rb.IsNotValid() || rb!.Native == null) return;

        RigidBody car = rb.Native;
        World world = car.World;
        float gravity = _world != null ? Float3.Length(_world.Gravity) : 9.81f;

        // The Transform is interpolated for rendering and trails the simulation, so the wheel's pose is rebuilt
        // from where the physics body really is.
        Quaternion toBody = Quaternion.Inverse(rb.Transform.Rotation);
        Float3 localMount = toBody * (Transform.Position - rb.Transform.Position);
        Quaternion wheelRotation = rb.Rotation * (toBody * Transform.Rotation);
        Float3 mountF = rb.Position + rb.Rotation * localMount;

        // Wheel frame: suspension axis (up), steered forward, axle toward the wheel's right.
        Float3 upF = Float3.Normalize(wheelRotation * Float3.UnitY);
        JVector up = upF.ToJitter();
        JVector fwd = JVector.Transform((wheelRotation * Float3.UnitZ).ToJitter(), JMatrix.CreateRotationMatrix(up, SteerAngle));
        JVector axle = JVector.NormalizeSafe(JVector.Cross(up, fwd));
        if (axle.LengthSquared() <= 0.0f) return;
        fwd = JVector.NormalizeSafe(JVector.Cross(axle, up));
        if (fwd.LengthSquared() <= 0.0f) return;

        // The side comes from where the mount sits on the body, so steering never flips it.
        _side = localMount.X >= 0.0f ? 1.0f : -1.0f;
        axle = ApplyCamber(axle, fwd);
        JVector mount = mountF.ToJitter();

        _rayCar = car;
        bool grounded = CastGround(world, mount, up, fwd, axle, out float rawCompression, out Float3 normal, out Float3 point, out IDynamicTreeProxy? proxy, out RigidBody? gb);
        float newDisplacement = grounded ? Maths.Clamp(rawCompression, 0.0f, suspensionDistance) : 0.0f;

        // A body asleep on still ground stays asleep: nothing the wheel would push with has changed since it settled.
        if (!car.IsActive)
        {
            bool groundMoving = gb != null && gb.MotionType != MotionType.Static && gb.IsActive
                && (gb.Velocity.LengthSquared() > 1e-4f || gb.AngularVelocity.LengthSquared() > 1e-4f);
            bool changed = grounded != onFloor || Maths.Abs(newDisplacement - displacement) > 1e-3f || MotorTorque != 0.0f
                || Maths.Abs(SteerAngle - _sleptSteer) > 1e-4f || Maths.Abs(BrakeTorque - _sleptBrake) > 1e-3f || groundMoving;
            if (!changed) return;
            car.SetActivationState(true);
        }
        _sleptSteer = SteerAngle;
        _sleptBrake = BrakeTorque;

        _previousDisplacement = displacement;
        if (!grounded)
        {
            onFloor = false;
            groundBody = null;
            _groundProxy = null;
            _anchored = false;
            displacement = 0.0f;
            _groundedTime = 0.0f;
            _airTime += timeStep;
            return;
        }

        if (rawCompression > 0.0f)
        {
            _groundedTime = IsGrounded ? _groundedTime + timeStep : 0.0f;
            _airTime = 0.0f;
        }
        else
        {
            _groundedTime = 0.0f;
            _airTime += timeStep;
        }
        onFloor = true;
        _reach = rawCompression;
        groundBody = gb;
        _groundProxy = proxy;
        displacement = newDisplacement;
        contactNormal = normal;
        contactPoint = point;
        _surfaceGrip = SurfaceGrip(proxy);

        float sMass = sprungMass > 0.0f ? sprungMass : (float)rb.Mass / WheelCount;
        _sprungMass = sMass;
        float omega = 2.0f * Maths.PI * suspensionFrequency;
        _springK = sMass * omega * omega;
        _damperC = 2.0f * suspensionDampingRatio * sMass * omega;
        _preloadForce = preload * sMass * gravity;
        _damperCap = _springK * suspensionDistance + _preloadForce;

        // Grip acts in the ground plane.
        JVector cn = normal.ToJitter();
        JVector planeFwd = fwd - cn * JVector.Dot(fwd, cn);
        if (planeFwd.LengthSquared() < 1e-8f) planeFwd = fwd;
        JVector.NormalizeInPlace(ref planeFwd);
        JVector planeRight = JVector.Cross(cn, planeFwd);
        JVector.NormalizeInPlace(ref planeRight);

        _mountJ = mount;
        _bodyAtStep = car.Position;
        _groundAtStep = gb != null ? gb.Position : JVector.Zero;
        _upJ = up;
        _wheelUpJ = JVector.Cross(fwd, axle);
        _contactJ = point.ToJitter();
        _planeFwd = planeFwd;
        _planeRight = planeRight;

        // How hard a bottomed out wheel pushes back per metre it sinks in, counted as load so it still grips.
        _depenetrationStiffness = depenetrate ? _sprungMass * Maths.Max(gravity, 1.0f) / radius : 0.0f;
    }

    private float SurfaceGrip(IDynamicTreeProxy? proxy)
    {
        if (ReferenceEquals(proxy, _surfaceProxy)) return _surfaceGrip;
        _surfaceProxy = proxy;
        if (proxy == null || _world == null) return 1.0f;
        MonoBehaviour owner = _world.GetProxyOwner(proxy);
        if (owner.IsNotValid()) return 1.0f;
        WheelSurface surface = owner.GetComponentInParent<WheelSurface>();
        return surface.IsValid() ? Maths.Max(0.0f, surface.Grip) : 1.0f;
    }

    // ----------------------------------------------------------------
    //  Every substep: suspension, grip and spin
    // ----------------------------------------------------------------

    private void OnPreSubStep(float dt)
    {
        if (rb.IsNotValid() || rb!.Native == null || dt <= 0.0f) return;

        RigidBody car = rb.Native;
        if (!car.IsActive) return;

        float inertia = Maths.Max(0.5f * wheelMass * radius * radius, 1e-6f);
        float brake = Maths.Max(0.0f, BrakeTorque);
        float frictionTorque = 0.0f;
        bool held = false;
        float groundSpeed = 0.0f;

        if (onFloor)
        {
            // The body has moved on since the ground was found, so the mount and contact move with it, and the
            // spring sees how far it closed on the ground in the meantime.
            JVector drift = car.Position - _bodyAtStep;
            bool movingGround = groundBody != null && groundBody.MotionType != MotionType.Static;
            JVector groundDrift = movingGround ? groundBody!.Position - _groundAtStep : JVector.Zero;
            JVector mount = _mountJ + drift;
            JVector contact = _contactJ + drift;
            JVector n = contactNormal.ToJitter();
            float upOnNormal = Maths.Max(JVector.Dot(_upJ, n), 0.1f);
            float rawReach = _reach - JVector.Dot(drift - groundDrift, n) / upOnNormal;
            float reach = Maths.Min(rawReach, suspensionDistance);
            float compression = Maths.Max(reach, 0.0f);

            // Damper speed is measured along the ground normal, so driving over flat ground with the body pitched
            // is not mistaken for the suspension moving. Spring and damper are solved implicitly against the wheel's
            // share of mass, so even very stiff springs settle instead of bouncing the car.
            float closing = -(float)Double3.Dot(ContactVel(car, D(mount)), D(n)) / upOnNormal;
            float spring = _springK * compression;
            float mass = Maths.Max(_sprungMass, 1e-3f);
            float implicitForce = (_springK * (reach + closing * dt) + _damperC * closing) / (1.0f + (_damperC * dt + _springK * dt * dt) / mass);
            float damperForce = Maths.Clamp(implicitForce - spring, -_damperCap, _damperCap);

            // Still short of the ground by the end of this substep: the tyre is not touching yet.
            bool touching = reach + closing * dt > 0.0f;
            // Suspension bottomed out and the wheel is inside the surface: extra load, by how deep it is and how fast it
            // is still closing, so the rigid wheel does not sink in. Worked out every substep, so it lets go the moment
            // the wheel starts back out.
            float penetration = Maths.Max(0.0f, rawReach - suspensionDistance);
            float depenetration = penetration > 0.0f ? Maths.Max(0.0f, _depenetrationStiffness * (penetration * 12.0f + closing * 0.8f)) : 0.0f;
            float load = touching ? Maths.Max(0.0f, spring + _preloadForce + damperForce + depenetration) : 0.0f;
            _load = load;

            // The ground can only push straight out of its surface; along it is the tyre's job. Pushing along a tilted
            // suspension axis instead would drive a nose heavy car forward on its own.
            JVector suspensionImpulse = n * (load * dt);
            Push(car, suspensionImpulse, mount);
            PushGround(-suspensionImpulse, contact, dt);

            // Grip pushes somewhere between the body's centre height and the ground, by WeightTransfer.
            double height = Double3.Dot(D(car.Position) - D(contact), D(n)) * (1.0f - weightTransfer);
            if (height < 0.0) height = 0.0;
            JVector forcePoint = contact + n * (float)height;

            // Gravity adds its pull along the ground before the next substep, so the tyre answers for it now; without
            // that a parked car creeps down a slope at the speed gravity adds each substep.
            Double3 vActual = ContactVel(car, D(forcePoint));
            Double3 vRel = vActual + D(RelativeGravity(car, movingGround) * dt);
            float vFwd = (float)Double3.Dot(vRel, D(_planeFwd));
            float vLat = (float)Double3.Dot(vRel, D(_planeRight));

            // Static friction: a tyre that has stopped on the ground is held to that spot, so a slope or a slow push
            // cannot creep it away. Sideways always; along the tyre only while the brake holds the wheel, below.
            float actualFwd = (float)Double3.Dot(vActual, D(_planeFwd));
            float actualLat = (float)Double3.Dot(vActual, D(_planeRight));
            if (_anchored && (Maths.Abs(actualFwd) > ReleaseSpeed || Maths.Abs(actualLat) > ReleaseSpeed || _anchorGround != groundBody)) _anchored = false;
            if (!_anchored && Maths.Abs(actualFwd) < AnchorSpeed && Maths.Abs(actualLat) < AnchorSpeed)
            {
                _anchored = true;
                _anchorGround = groundBody;
                _anchor = ToGround(mount, movingGround);
            }

            // The mount moves smoothly with the body, where the contact point can step between rays, so it is what
            // the anchor holds.
            JVector creep = _anchored ? mount - FromGround(_anchor, movingGround) : JVector.Zero;
            if (_anchored) vLat += AnchorPull * JVector.Dot(creep, _planeRight) / dt;

            float maxLong = forwardFriction * _surfaceGrip * load;
            float maxLat = sidewaysFriction * _surfaceGrip * load;
            float speed = Maths.Max(Maths.Abs(vFwd), LowSpeed);

            // How much a force moves the slip within this substep: the body's share of mass, plus a moving ground's.
            float bodyResponse = dt / Maths.Max(_sprungMass, 1e-3f);
            if (movingGround && groundBody!.MotionType == MotionType.Dynamic) bodyResponse += dt / Maths.Max((float)groundBody.Mass, 1e-3f);

            // First the wheel is taken to keep turning, with the brake and rolling resistance as a steady torque against
            // its spin, and the tyre force is solved against the spin that leaves. A braked wheel then rolls on where the
            // ground's pull balances the brake.
            float brakeHold = brake + dragTorque;
            float turning = Maths.Abs(angularVelocity) > 1e-3f ? Maths.Sign(angularVelocity) : Maths.Sign(vFwd);
            float spun = angularVelocity + (MotorTorque - brakeHold * turning) * dt / inertia;
            float longSlip = vFwd - spun * radius;
            float fLong = TyreForce(longSlip, speed, bodyResponse + dt * radius * radius / inertia, maxLong, forwardCurve);

            // Only if that would wind the wheel backwards has the brake really stopped it. A stopped wheel takes the
            // tyre's pull through the brake, so it stays still and grips only as hard as the brake can resist.
            float spinAfter = spun - fLong * radius * dt / inertia;
            held = Maths.Abs(MotorTorque) <= brakeHold && (turning == 0.0f || Maths.Sign(spinAfter) != turning);
            if (held)
            {
                longSlip = vFwd + (_anchored ? AnchorPull * JVector.Dot(creep, _planeFwd) / dt : 0.0f);
                float strongest = (brakeHold + Maths.Abs(MotorTorque)) / radius;
                fLong = Maths.Clamp(TyreForce(longSlip, speed, bodyResponse, maxLong, forwardCurve), -strongest, strongest);
            }
            else if (_anchored)
            {
                // A rolling wheel carries its anchor along with it, so only creep across the tyre is held.
                _anchor = ToGround(FromGround(_anchor, movingGround) + _planeFwd * JVector.Dot(creep, _planeFwd), movingGround);
            }

            float fLat = TyreForce(vLat, speed, bodyResponse, maxLat, sidewaysCurve);
            fLat += camberThrust * load * JVector.Dot(_wheelUpJ, _planeRight);

            // Both directions share one budget, an ellipse through the two peak grips.
            float peakLong = maxLong * forwardCurve.ExtremumValue, peakLat = maxLat * sidewaysCurve.ExtremumValue;
            float ex = peakLong > 0.0f ? fLong / peakLong : (fLong != 0.0f ? float.PositiveInfinity : 0.0f);
            float ey = peakLat > 0.0f ? fLat / peakLat : (fLat != 0.0f ? float.PositiveInfinity : 0.0f);
            float e = Maths.Sqrt(ex * ex + ey * ey);
            if (e > 1.0f)
            {
                fLong = float.IsFinite(e) ? fLong / e : 0.0f;
                fLat = float.IsFinite(e) ? fLat / e : 0.0f;
                _anchored = false;
            }

            frictionTorque = -fLong * radius;
            _fLong = fLong;
            _fLat = fLat;
            groundSpeed = actualFwd;
            _sidewaysSlip = vLat;

            JVector frictionForce = _planeFwd * fLong + _planeRight * fLat;
            Push(car, frictionForce * dt, forcePoint);
            PushGround(-frictionForce * dt, contact, dt);
        }
        else _fLong = _fLat = _forwardSlip = _sidewaysSlip = _load = 0.0f;

        // Spin: the tyre's reaction, the drive and a little damping, then the brake and rolling resistance pull
        // it toward rest without ever reversing it.
        bool onGround = onFloor && _load > 0.0f;
        if (held)
        {
            angularVelocity = 0.0f;
            _forwardSlip = groundSpeed;
            return;
        }
        if (onGround) angularVelocity += frictionTorque * dt / inertia;
        angularVelocity += MotorTorque * dt / inertia;
        angularVelocity /= 1.0f + spinDamping * dt / inertia;

        float brakeDelta = (brake + (onGround ? dragTorque : 0.0f)) * dt / inertia;
        if (Maths.Abs(angularVelocity) <= brakeDelta) angularVelocity = 0.0f;
        else angularVelocity -= (angularVelocity > 0.0f ? 1.0f : -1.0f) * brakeDelta;

        if (maxAngularVelocity > 0.0f) angularVelocity = Maths.Clamp(angularVelocity, -maxAngularVelocity, maxAngularVelocity);
        if (onFloor) _forwardSlip = groundSpeed - angularVelocity * radius;
        wheelRotation = (wheelRotation + angularVelocity * dt) % (2.0f * Maths.PI);
    }

    // The tyre force for a slip speed, from the friction curve. The slip is first settled by how far that force
    // itself closes it within the substep, which keeps stiff grip stable on heavy vehicles and light wheels alike.
    private static float TyreForce(float slip, float speed, float response, float maxForce, in WheelFrictionCurve curve)
    {
        if (maxForce <= 0.0f || slip == 0.0f) return 0.0f;
        float stiffness = maxForce * curve.InitialStiffness / speed;
        float settled = slip / (1.0f + stiffness * response);
        return -Maths.Sign(slip) * maxForce * curve.Evaluate(settled / speed);
    }

    private JVector RelativeGravity(RigidBody car, bool movingGround)
    {
        if (_world == null) return JVector.Zero;
        JVector gravity = _world.Gravity.ToJitter();
        JVector own = car.AffectedByGravity ? gravity : JVector.Zero;
        if (movingGround && groundBody!.MotionType == MotionType.Dynamic && groundBody.AffectedByGravity) own -= gravity;
        return own;
    }

    private float CamberRadians() => camber * (Maths.PI / 180.0f) * _side;

    // Tilt the axle about the forward axis by the (side-mirrored) camber angle.
    private JVector ApplyCamber(JVector axle, JVector fwd)
    {
        if (camber == 0.0f) return axle;
        JVector tilted = JVector.Transform(axle, JMatrix.CreateRotationMatrix(fwd, CamberRadians()));
        JVector.NormalizeInPlace(ref tilted);
        return tilted;
    }

    private static Float3 ToF(JVector v) => new(v.X, v.Y, v.Z);
    private static Double3 D(JVector v) => new(v.X, v.Y, v.Z);

    /// <summary>Contact-point velocity (chassis minus moving ground) in double precision. It's a
    /// difference of world-space positions crossed with angular velocity, which loses float precision
    /// (worse away from the origin); doing it in double keeps the slip/damper readings clean.</summary>
    private Double3 ContactVel(RigidBody car, Double3 contactD)
    {
        Double3 v = D(car.Velocity) + Double3.Cross(D(car.AngularVelocity), contactD - D(car.Position));
        if (groundBody != null && groundBody.MotionType != MotionType.Static)
            v -= D(groundBody.Velocity) + Double3.Cross(D(groundBody.AngularVelocity), contactD - D(groundBody.Position));
        return v;
    }

    // A grid of rays down the suspension axis across the wheel face. Each hit gives a compression two ways: the
    // tyre's round profile resting on that exact point, and the whole wheel resting on the surface plane there,
    // which is used when the plane's touching point lies in this ray's own patch of the wheel. The deepest wins,
    // so the wheel sits exactly on flat and sloped ground and still rides up kerbs between rays. Hits from inside
    // geometry, and surfaces too steep to stand on, are walls rather than ground and are skipped.
    private bool CastGround(World world, JVector mount, JVector up, JVector fwd, JVector axle,
        out float compression, out Float3 normal, out Float3 point, out IDynamicTreeProxy? groundProxy, out RigidBody? ground)
    {
        compression = 0.0f;
        normal = ToF(up);
        point = ToF(mount);
        groundProxy = null;
        ground = null;

        int lat = Maths.Max(1, sideRayCount);
        int lon = Odd(forwardRayCount);
        float halfW = width * 0.5f;
        float stepX = lat == 1 ? 0.0f : width / (lat - 1);
        float stepY = lon == 1 ? 0.0f : radius * 2.0f / (lon - 1);
        float patchX = lat == 1 ? halfW : stepX * 0.5f;
        float patchY = lon == 1 ? radius : stepY * 0.5f;
        float reach = suspensionDistance + radius + 0.05f;

        float best = float.NegativeInfinity;
        float weightSum = 0.0f;
        Float3 normalSum = Float3.Zero;
        Float3 pointSum = Float3.Zero;
        Float3 bestNormal = ToF(up);

        for (int xi = 0; xi < lat; xi++)
        {
            float x = lat == 1 ? 0.0f : -halfW + stepX * xi;
            for (int yi = 0; yi < lon; yi++)
            {
                float y = lon == 1 ? 0.0f : -radius + stepY * yi;
                JVector origin = mount + fwd * y + axle * x;

                if (!world.DynamicTree.RayCast(origin, -up, reach, _rayFilter, null, out IDynamicTreeProxy? proxy, out JVector n, out float dist))
                    continue;
                if (dist <= 1e-4f || n.LengthSquared() < 1e-8f) continue;
                JVector.NormalizeInPlace(ref n);
                float upward = JVector.Dot(n, up);
                if (upward < MinSupportCos) continue;

                JVector hit = origin - up * dist;
                float curvature = Maths.Sqrt(Maths.Max(0.0f, radius * radius - y * y));
                float c = suspensionDistance + curvature - dist;
                JVector touch = hit;

                // The wheel resting flat on this surface: its lowest point toward the surface, round across the
                // tread and square across the width.
                float across = JVector.Dot(axle, n);
                JVector inPlane = n - axle * across;
                float inPlaneLength = inPlane.Length();
                float lowest = radius * inPlaneLength + halfW * Maths.Abs(across);
                float extension = (JVector.Dot(mount - hit, n) - lowest) / upward;
                JVector center = mount - up * extension;
                JVector tangent = center - axle * (halfW * Maths.Sign(across));
                if (inPlaneLength > 1e-6f) tangent -= inPlane * (radius / inPlaneLength);
                float along = JVector.Dot(tangent - mount, fwd);
                float side = JVector.Dot(tangent - mount, axle);
                if (Maths.Abs(along - y) <= patchY + 1e-4f && Maths.Abs(side - x) <= patchX + 1e-4f && suspensionDistance - extension > c)
                {
                    c = suspensionDistance - extension;
                    touch = tangent;
                }

                if (c <= -ContactMargin) continue;

                // Normals and touching points are averaged with the deepest weighted heavily, so the contact turns
                // smoothly over edges and sits under the middle of the tyre when several rays touch equally.
                float depth = c + ContactMargin;
                float w2 = depth * depth;
                float w = w2 * w2 * depth;
                weightSum += w;
                normalSum += ToF(n) * w;
                pointSum += ToF(touch) * w;

                if (c > best)
                {
                    best = c;
                    bestNormal = ToF(n);
                    groundProxy = proxy;
                    ground = (proxy as RigidBodyShape)?.RigidBody;
                }
            }
        }

        if (best <= -ContactMargin) return false;

        compression = best;
        point = pointSum * (1.0f / weightSum);
        Float3 meanNormal = normalSum * (1.0f / weightSum);
        normal = Float3.LengthSquared(meanNormal) > 1e-6f ? Float3.Normalize(meanNormal) : bestNormal;
        return true;
    }

    // Pushing back with the full reaction would hand a light body under the wheel the whole car's load
    // and tyre grip, launching it. Capping the velocity change keeps heavy ground bodies (a platform, a
    // ferry) fully reactive while a crate is only shoved.
    private const float MaxGroundAcceleration = 25.0f;

    // Only an awake car pushes, and it wakes what it stands on to do so. Once the car itself falls asleep it stops
    // pushing, so a car and the body under it can settle and sleep together.
    private void PushGround(JVector impulse, JVector point, float dt)
    {
        if (groundBody == null || groundBody.MotionType != MotionType.Dynamic) return;
        if (!groundBody.IsActive) groundBody.SetActivationState(true);

        float max = MaxGroundAcceleration * (float)groundBody.Mass * dt;
        if (impulse.LengthSquared() > max * max) impulse *= max / impulse.Length();

        Push(groundBody, impulse, point);
    }

    // Jitter's ApplyImpulse restarts a body's sleep timer on every call, which would keep a parked car awake for
    // ever, so the wheel changes the velocities itself.
    private static void Push(RigidBody body, JVector impulse, JVector point)
    {
        if (body.MotionType != MotionType.Dynamic) return;
        ref RigidBodyData data = ref body.Data;
        data.Velocity += JVector.Multiply(impulse, data.InverseMassVector);
        data.AngularVelocity += JVector.Transform(JVector.Cross(point - data.Position, impulse), data.InverseInertiaWorld);
    }

    // The anchor is kept in the moving ground's own space, so it rides along with a platform.
    private JVector ToGround(JVector point, bool movingGround)
        => movingGround ? JVector.ConjugatedTransform(point - groundBody!.Position, groundBody.Orientation) : point;

    private JVector FromGround(JVector local, bool movingGround)
        => movingGround ? JVector.Transform(local, groundBody!.Orientation) + groundBody.Position : local;

    public override void DrawGizmos()
    {
        Float3 center = GetWheelCenter();
        Float3 up = Transform.Up;

        // Wheel frame: steered forward + axle, and a radial basis (r1, r2) in the roll plane.
        JVector fwdJ = JVector.Transform(Transform.Forward.ToJitter(), JMatrix.CreateRotationMatrix(up.ToJitter(), SteerAngle));
        JVector axleJ = JVector.Cross(up.ToJitter(), fwdJ);
        JVector.NormalizeInPlace(ref axleJ);
        axleJ = ApplyCamber(axleJ, fwdJ);

        Float3 axle = ToF(axleJ);
        Float3 r1 = Float3.Normalize(ToF(fwdJ));
        Float3 r2 = Float3.Normalize(Float3.Cross(axle, r1));

        float halfW = width * 0.5f;
        Float3 leftC = center + axle * halfW;
        Float3 rightC = center - axle * halfW;

        Color color = onFloor ? new Color(0f, 1f, 0f, 1f) : new Color(1f, 0.5f, 0f, 1f);

        Debug.DrawWireCircle(leftC, axle, radius, color, 24);
        Debug.DrawWireCircle(rightC, axle, radius, color, 24);

        // Spinning spokes (one red) so the wheel's spin is visible. If they don't turn, it isn't spinning.
        for (int i = 0; i < 4; i++)
        {
            float a = i * (Maths.PI * 0.5f) + wheelRotation;
            Float3 dir = r1 * Maths.Cos(a) + r2 * Maths.Sin(a);
            Color sc = i == 0 ? new Color(1f, 0f, 0f, 1f) : color;
            Debug.DrawLine(leftC + dir * radius, rightC + dir * radius, sc);
            Debug.DrawLine(center, center + dir * radius, sc);
        }

        // Suspension travel line (yellow).
        Debug.DrawLine(Transform.Position, Transform.Position - up * suspensionDistance, new Color(1f, 1f, 0f, 1f));

        if (onFloor)
        {
            DrawCross(contactPoint, 0.06f, new Color(1f, 1f, 1f, 1f));               // contact point (white)
            Debug.DrawLine(contactPoint, contactPoint + contactNormal * 0.3f, new Color(0f, 0.8f, 1f, 1f));   // normal (cyan)
            Debug.DrawLine(contactPoint, contactPoint + ToF(_planeFwd) * 0.4f, new Color(0.2f, 0.4f, 1f, 1f)); // roll dir (blue)
            Debug.DrawLine(contactPoint, contactPoint + ToF(_planeRight) * 0.3f, new Color(1f, 0f, 1f, 1f));   // lateral (magenta)
            Debug.DrawLine(contactPoint, contactPoint + ToF(_planeFwd) * (_fLong * 0.002f), new Color(1f, 1f, 0f, 1f));   // long force (yellow)
            Debug.DrawLine(contactPoint, contactPoint + ToF(_planeRight) * (_fLat * 0.002f), new Color(1f, 0.5f, 0f, 1f)); // lat force (orange)
        }
    }

    private static void DrawCross(Float3 p, float s, Color c)
    {
        Debug.DrawLine(p - new Float3(s, 0, 0), p + new Float3(s, 0, 0), c);
        Debug.DrawLine(p - new Float3(0, s, 0), p + new Float3(0, s, 0), c);
        Debug.DrawLine(p - new Float3(0, 0, s), p + new Float3(0, 0, s), c);
    }
}
