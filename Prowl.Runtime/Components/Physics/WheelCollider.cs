// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A raycast-vehicle wheel. Ground contact is a grid of downward rays across the wheel face, riding over
/// bumps/kerbs; the ray distances are analytic so the contact is stable. Suspension is a spring/damper
/// tuned from a natural frequency, grip uses a saturating slip model clamped to a friction circle, and a
/// fully compressed wheel depenetrates out of the surface.
///
/// The wheel applies forces to the parent <see cref="Rigidbody3D"/>; it does not add a collision
/// shape of its own. Place the wheel GameObject so its position marks the top of the suspension
/// travel; the wheel hangs below along -up.
/// </summary>
[AddComponentMenu("Physics/Wheel Collider")]
[ComponentIcon("")] // CarSide
public sealed class WheelCollider : MonoBehaviour
{
    // Geometry
    [SerializeField] private float radius = 0.35f;
    [SerializeField] private float width = 0.25f;
    [SerializeField] private float camber = 0.0f; // wheel tilt in degrees about the forward axis (auto-mirrored per side)

    // Ground detection (raycast grid)
    [SerializeField] private int forwardRayCount = 9; // rays along the wheel length (odd keeps a centre ray)
    [SerializeField] private int sideRayCount = 3;    // raycast grid: scan planes across the wheel width
    [SerializeField] private bool depenetrate = true; // push out of objects when the suspension bottoms out

    // Suspension
    [SerializeField] private float suspensionDistance = 0.3f;
    [SerializeField] private float suspensionFrequency = 2.0f;    // Hz, higher = stiffer
    [SerializeField] private float suspensionDampingRatio = 0.5f; // 1 = critically damped
    [SerializeField] private float sprungMass = 0.0f;            // 0 = auto (body mass / wheel count)

    // Friction (friction-circle)
    [SerializeField] private float forwardFriction = 1.6f;       // longitudinal grip coefficient
    [SerializeField] private float sidewaysFriction = 1.8f;      // lateral grip coefficient
    [SerializeField] private float gripSaturationSpeed = 4.0f;   // slip speed (m/s) at which grip saturates
    [SerializeField] private float dragTorque = 30.0f;          // rolling resistance (N*m) so the wheel/car comes to rest

    // Wheel spin
    [SerializeField] private float wheelMass = 15.0f;            // for spin inertia I = 0.5*m*r^2

    /// <summary>Steering angle in radians (rotation about the suspension axis). Set by a controller.</summary>
    public float SteerAngle;

    /// <summary>Drive torque in N*m applied to the wheel spin this step. Set by a controller each frame.</summary>
    public float MotorTorque;

    /// <summary>Brake torque in N*m opposing the wheel spin this step. Set by a controller each frame.</summary>
    public float BrakeTorque;

    /// <summary>Optional visual wheel transform. When set it is placed at the wheel centre and rotated
    /// for steering (about the suspension axis) and spin (about the axle) each frame. Keep this separate
    /// from the WheelCollider's own GameObject, which marks the fixed suspension mount used by physics.</summary>
    public Transform visualTransform;

    // Runtime state
    private Rigidbody3D rb;
    private int wheelCount = 1;
    private bool counted;
    private float displacement;        // suspension compression, 0..suspensionDistance
    private float _side = 1.0f;         // +1 right, -1 left; mirrors camber so a single angle works on both sides
    private bool onFloor;
    private float angularVelocity;     // wheel spin (rad/s)
    private float wheelRotation;       // accumulated spin (rad), for visuals
    private Float3 contactPoint;
    private Float3 contactNormal = Float3.UnitY;
    private RigidBody groundBody;

    // Per-step cache (set in PreStep, used by the per-substep friction/spin in PreSubStep).
    private JVector _contactJ, _mountJ, _upJ, _planeFwd, _planeLeft;
    private float _springK, _damperC, _damperCap;
    private float _sprungMass; // this wheel's share of the body mass (cached in PreStep)
    private float _fLong, _fLat; // last tyre forces, for gizmos

    // Public configuration
    public float Radius { get => radius; set => radius = Maths.Max(0.01f, value); }
    public float Width { get => width; set => width = Maths.Max(0.01f, value); }
    /// <summary>Wheel camber in degrees (tilt about the forward axis). Auto-mirrored for left/right wheels.</summary>
    public float Camber { get => camber; set => camber = value; }
    /// <summary>Raycast grid: number of rays along the wheel length. Odd keeps a centre ray.</summary>
    public int ForwardRayCount { get => forwardRayCount; set => forwardRayCount = Maths.Max(1, value); }
    /// <summary>Raycast grid: number of scan planes across the wheel width.</summary>
    public int SideRayCount { get => sideRayCount; set => sideRayCount = Maths.Max(1, value); }
    /// <summary>Push the wheel (and chassis) out along the contact normal when the suspension bottoms out against an object.</summary>
    public bool Depenetrate { get => depenetrate; set => depenetrate = value; }
    public float SuspensionDistance { get => suspensionDistance; set => suspensionDistance = Maths.Max(0.0f, value); }
    public float SuspensionFrequency { get => suspensionFrequency; set => suspensionFrequency = Maths.Max(0.1f, value); }
    public float SuspensionDampingRatio { get => suspensionDampingRatio; set => suspensionDampingRatio = Maths.Max(0.0f, value); }
    /// <summary>Sprung mass (kg) this wheel supports. 0 = auto-estimate from the body mass and wheel count.</summary>
    public float SprungMass { get => sprungMass; set => sprungMass = Maths.Max(0.0f, value); }
    public float ForwardFriction { get => forwardFriction; set => forwardFriction = Maths.Max(0.0f, value); }
    public float SidewaysFriction { get => sidewaysFriction; set => sidewaysFriction = Maths.Max(0.0f, value); }
    public float GripSaturationSpeed { get => gripSaturationSpeed; set => gripSaturationSpeed = Maths.Max(0.1f, value); }
    /// <summary>Rolling resistance torque (N*m). Brakes the wheel toward rest so the car doesn't coast on perturbations.</summary>
    public float DragTorque { get => dragTorque; set => dragTorque = Maths.Max(0.0f, value); }
    public float WheelMass { get => wheelMass; set => wheelMass = Maths.Max(0.01f, value); }
    //public float MaxAngularVelocity { get => maxAngularVelocity; set => maxAngularVelocity = Maths.Max(0.0f, value); }

    // Public state
    public bool IsGrounded => onFloor;
    public float SuspensionCompression => suspensionDistance > 0 ? displacement / suspensionDistance : 0;
    public float AngularVelocity => angularVelocity;
    public float WheelRotation => wheelRotation;
    public Float3 ContactPoint => contactPoint;
    public Float3 ContactNormal => contactNormal;

    // The inspector writes the backing fields directly (bypassing the property setters), so clamp
    // them here to keep inspector-entered values valid. The properties still validate scripting use.
    public override void OnValidate()
    {
        radius = Maths.Max(0.01f, radius);
        width = Maths.Max(0.01f, width);
        forwardRayCount = Maths.Max(1, forwardRayCount);
        sideRayCount = Maths.Max(1, sideRayCount);
        suspensionDistance = Maths.Max(0.0f, suspensionDistance);
        suspensionFrequency = Maths.Max(0.1f, suspensionFrequency);
        suspensionDampingRatio = Maths.Max(0.0f, suspensionDampingRatio);
        sprungMass = Maths.Max(0.0f, sprungMass);
        forwardFriction = Maths.Max(0.0f, forwardFriction);
        sidewaysFriction = Maths.Max(0.0f, sidewaysFriction);
        gripSaturationSpeed = Maths.Max(0.1f, gripSaturationSpeed);
        dragTorque = Maths.Max(0.0f, dragTorque);
        wheelMass = Maths.Max(0.01f, wheelMass);
        //maxAngularVelocity = Maths.Max(0.0f, maxAngularVelocity);
    }

    public override void OnEnable()
    {
        rb = GetComponentInParent<Rigidbody3D>();
        if (rb == null)
        {
            Debug.LogError("WheelCollider requires a Rigidbody3D on a parent GameObject.");
            return;
        }

        Recalculate();
        counted = false; // recount on the first physics step, once all sibling wheels exist

        // PreStep: ground cast + suspension constants (once/step). PreSubStep: suspension + friction + spin,
        // run each physics substep against the body's re-integrated velocity.
        GameObject.Scene.Physics.PreStep += OnPreStep;
        GameObject.Scene.Physics.PreSubStep += OnPreSubStep;
    }

    public override void OnDisable()
    {
        if (GameObject?.Scene?.Physics != null)
        {
            GameObject.Scene.Physics.PreStep -= OnPreStep;
            GameObject.Scene.Physics.PreSubStep -= OnPreSubStep;
        }
    }

    public override void Update()
    {
        if (visualTransform == null) return;

        // The mount transform (this GameObject) gives the base wheel frame: up = suspension axis,
        // right = axle. Steer rotates about the suspension axis, camber tilts about forward, spin about the axle.
        Quaternion steer = Quaternion.AxisAngle(Float3.UnitY, SteerAngle);
        Quaternion camberQ = Quaternion.AxisAngle(Float3.UnitZ, CamberRadians());
        Quaternion spin = Quaternion.AxisAngle(Float3.UnitX, wheelRotation);
        visualTransform.Rotation = Transform.Rotation * steer * camberQ * spin;
        visualTransform.Position = GetWheelCenter();
    }

    /// <summary>Re-resolves the rigidbody and counts sibling wheels (for auto sprung-mass). Call after
    /// adding/removing wheels on the vehicle.</summary>
    public void Recalculate()
    {
        rb = GetComponentInParent<Rigidbody3D>();
        if (rb == null) return;
        int count = 0;
        foreach (var _ in rb.GameObject.GetComponentsInChildren<WheelCollider>()) count++;
        wheelCount = Maths.Max(1, count);
    }

    /// <summary>The current world-space wheel centre (accounts for suspension compression).</summary>
    public Float3 GetWheelCenter() => Transform.Position - Transform.Up * (suspensionDistance - displacement);

    private void OnPreStep(float timeStep)
    {
        if (rb?._body == null || timeStep <= 0.0f) return;

        // Wheels enable one at a time; recount once on the first step so auto sprung-mass divides
        // by the full wheel count rather than however many existed when this wheel first enabled.
        if (!counted) { Recalculate(); counted = true; }

        RigidBody car = rb._body;
        World world = car.World;

        // Wheel frame: suspension axis (up), steered forward, axle (left).
        Float3 upF = Transform.Up;
        JVector up = ToJ(upF);
        JVector.NormalizeInPlace(ref up);

        JVector fwd = JVector.Transform(ToJ(Transform.Forward), JMatrix.CreateRotationMatrix(up, SteerAngle));
        JVector axle = JVector.Cross(up, fwd);
        JVector.NormalizeInPlace(ref axle);
        fwd = JVector.Cross(axle, up);
        JVector.NormalizeInPlace(ref fwd);

        // Camber tilts the wheel (its axle) about the forward axis; the suspension axis stays vertical.
        // Mirror it per side so one positive angle leans the tops of both wheels the same way.
        _side = Float3.Dot(Transform.Position - ToF(car.Position), ToF(axle)) >= 0.0f ? 1.0f : -1.0f;
        axle = ApplyCamber(axle, fwd);

        // Ground detection: a stable grid of rays across the wheel face. Reports a raw compression (which
        // can exceed the travel when the wheel is jammed into something, driving depenetration) plus a
        // processed contact normal and point.
        JVector mount = ToJ(Transform.Position);

        bool grounded = CastRaycastGrid(world, mount, up, upF, fwd, axle,
            out float rawCompression, out Float3 outNormal, out Float3 outPoint, out RigidBody gb);

        if (!grounded)
        {
            onFloor = false;
            groundBody = null;
            displacement = 0.0f;
            return;
        }

        onFloor = true;
        groundBody = gb;
        displacement = Maths.Clamp(rawCompression, 0.0f, suspensionDistance);
        float penetration = Maths.Max(0.0f, rawCompression - suspensionDistance);
        contactNormal = outNormal;
        contactPoint = outPoint;
        JVector contact = ToJ(contactPoint);

        // Suspension constants. The spring/damper forces themselves are evaluated per-substep in
        // OnPreSubStep (not here) so the suspension is integrated at the same rate as the body.
        float sMass = sprungMass > 0.0f ? sprungMass : (float)rb.Mass / wheelCount;
        _sprungMass = sMass;
        float omega = 2.0f * Maths.PI * suspensionFrequency;
        _springK = sMass * omega * omega;
        _damperC = 2.0f * suspensionDampingRatio * sMass * omega;
        _damperCap = _springK * suspensionDistance;

        // Friction-plane basis (perpendicular to the contact normal).
        JVector cn = ToJ(contactNormal);
        JVector planeFwd = fwd - cn * JVector.Dot(fwd, cn);
        if (planeFwd.LengthSquared() < 1e-8f) planeFwd = fwd;
        JVector.NormalizeInPlace(ref planeFwd);
        JVector planeLeft = JVector.Cross(cn, planeFwd);
        JVector.NormalizeInPlace(ref planeLeft);

        _mountJ = mount;
        _upJ = up;
        _contactJ = contact;
        _planeFwd = planeFwd;
        _planeLeft = planeLeft;

        // Suspension bottomed out and the wheel is inside the surface: push it back out.
        if (depenetrate && penetration > 0.0f)
            ApplyDepenetration(car, penetration, ToJ(contactNormal), contact, timeStep);
    }

    // Suspension, tyre friction and wheel spin, applied each physics substep as impulses (force * dt)
    // against the body's re-integrated velocity.
    private void OnPreSubStep(float dt)
    {
        if (rb?._body == null || dt <= 0.0f) return;

        RigidBody car = rb._body;
        float inertia = 0.5f * wheelMass * radius * radius;
        if (inertia <= 1e-6f) inertia = 1e-6f;

        float frictionTorque = 0.0f;

        if (onFloor)
        {
            // ---- Suspension: spring (from the swept compression) + damper (from the mount velocity along
            // the suspension axis), applied at the mount. ----
            float suspVelUp = (float)Double3.Dot(ContactVel(car, D(_mountJ)), D(_upJ)); // + extending, - compressing
            float damperForce = Maths.Clamp(-_damperC * suspVelUp, -_damperCap, _damperCap);
            float load = _springK * displacement + damperForce; // displacement is the steady swept compression
            if (load < 0.0f) load = 0.0f;

            JVector suspImpulse = _upJ * (load * dt);
            car.ApplyImpulse(suspImpulse, _mountJ);
            car.SetActivationState(true);
            if (groundBody != null && groundBody.MotionType != MotionType.Static)
                groundBody.ApplyImpulse(-suspImpulse, _contactJ);

            // ---- Tyre friction at the COM-height point (measured there too, so it's dissipative) ----
            JVector cn = ToJ(contactNormal);
            double ah = Double3.Dot(D(car.Position) - D(_contactJ), D(cn));
            if (ah < 0.0) ah = 0.0;
            JVector forcePoint = _contactJ + cn * (float)ah;

            Double3 vRel = ContactVel(car, D(forcePoint));
            float vFwd = (float)Double3.Dot(vRel, D(_planeFwd));
            float vLat = (float)Double3.Dot(vRel, D(_planeLeft));

            float maxLong = forwardFriction * load;
            float maxLat = sidewaysFriction * load;

            float rimSpeed = angularVelocity * radius;
            float longSlip = vFwd - rimSpeed;

            float fLong = -Maths.Clamp(longSlip / gripSaturationSpeed, -1.0f, 1.0f) * maxLong;
            float fLat = -Maths.Clamp(vLat / gripSaturationSpeed, -1.0f, 1.0f) * maxLat;

            // Cancel the slope's sideways pull on this wheel's mass share so a parked car holds instead of
            // creeping sideways. Clamped to the friction ellipse below, so on a slope too steep for the grip
            // it still slides.
            float gLat = JVector.Dot(ToJ(GameObject.Scene.Physics.Gravity), _planeLeft);
            fLat += -_sprungMass * gLat;

            float ex = maxLong > 0.0f ? fLong / maxLong : 0.0f;
            float ey = maxLat > 0.0f ? fLat / maxLat : 0.0f;
            float e = Maths.Sqrt(ex * ex + ey * ey);
            if (e > 1.0f) { fLong /= e; fLat /= e; }

            frictionTorque = -fLong * radius;

            _fLong = fLong; _fLat = fLat;

            JVector frictionForce = _planeFwd * fLong + _planeLeft * fLat;
            car.ApplyImpulse(frictionForce * dt, forcePoint);
            car.SetActivationState(true);

            if (groundBody != null && groundBody.MotionType != MotionType.Static)
            {
                float maxImp = 500.0f * (float)groundBody.Mass * dt;
                JVector imp = frictionForce * dt;
                if (imp.LengthSquared() > maxImp * maxImp) imp *= maxImp / imp.Length();
                groundBody.SetActivationState(true);
                groundBody.ApplyImpulse(-imp, _contactJ);
            }
        }
        else _fLong = _fLat = 0.0f;

        // Spin integration. The wheel spin is a free flywheel: the tyre friction reaction, drive torque,
        // and brake/rolling-resistance are the only torques; grip-limited friction transfers spin momentum
        // to the car and pulls the spin toward the rolling speed.
        if (onFloor)
            angularVelocity += frictionTorque * dt / inertia;

        angularVelocity += MotorTorque * dt / inertia;

        // Brake + rolling resistance (drag only while grounded). Rolling resistance is what brings a
        // coasting wheel - and the car - to rest, so it doesn't drift forever on tiny perturbations.
        float brakeDelta = (BrakeTorque + (onFloor ? dragTorque : 0.0f)) * dt / inertia;
        if (Maths.Abs(angularVelocity) <= brakeDelta) angularVelocity = 0.0f;
        else angularVelocity -= (angularVelocity > 0.0f ? 1.0f : -1.0f) * brakeDelta;

        //angularVelocity = Maths.Clamp(angularVelocity, -maxAngularVelocity, maxAngularVelocity);
        wheelRotation += angularVelocity * dt;
    }

    private bool SweepFilter(IDynamicTreeProxy proxy)
    {
        if (proxy is not RigidBodyShape rbs) return false;
        if (rb?._body == null) return false;
        return rbs.RigidBody != rb._body; // ignore the vehicle's own body
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

    private static JVector ToJ(Float3 v) => new(v.X, v.Y, v.Z);
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

    // Grid of downward rays across the wheel face. Each ray's hit is turned into an equivalent spring
    // compression using the wheel's curvature at that forward offset (a point further along the tyre sits
    // higher on the round profile), and the deepest contact wins - so the wheel rides up over bumps and
    // kerbs. The returned compression can exceed the suspension travel (penetration) to drive depenetration,
    // and the contact normal is the average of all hits (smoother than any single ray).
    private bool CastRaycastGrid(World world, JVector mount, JVector up, Float3 upF, JVector fwd, JVector axle,
        out float rawCompression, out Float3 outNormal, out Float3 outPoint, out RigidBody outGround)
    {
        rawCompression = 0.0f;
        outNormal = upF;
        outPoint = ToF(mount);
        outGround = null;

        int lat = Maths.Max(1, sideRayCount);
        int lon = Maths.Max(1, forwardRayCount);
        float halfW = width * 0.5f;
        float stepX = lat == 1 ? 0.0f : width / (lat - 1);
        float stepY = lon == 1 ? 0.0f : radius * 2.0f / (lon - 1);

        float bestC = float.NegativeInfinity;
        float weightSum = 0.0f;
        float compSum = 0.0f;
        Float3 pointSum = new(0.0f, 0.0f, 0.0f);
        Float3 normalSum = new(0.0f, 0.0f, 0.0f);

        for (int xi = 0; xi < lat; xi++)
        {
            float x = lat == 1 ? 0.0f : -halfW + stepX * xi;
            for (int yi = 0; yi < lon; yi++)
            {
                float y = lon == 1 ? 0.0f : -radius + stepY * yi;
                float curvature = Maths.Sqrt(Maths.Max(0.0f, radius * radius - y * y));
                JVector origin = mount + fwd * y + axle * x;
                float maxDist = suspensionDistance + curvature + 1e-4f;

                if (!world.DynamicTree.RayCast(origin, -up, SweepFilter, null,
                        out IDynamicTreeProxy proxy, out JVector n, out float dist))
                    continue;
                if (proxy is not RigidBodyShape rbs || dist > maxDist) continue;

                // curvature lifts the equivalent ground contact to the tyre surface at this offset.
                float c = suspensionDistance + curvature - dist;
                if (c <= 0.0f) continue; // ray has a gap to the ground, not in contact

                Float3 nf = ToF(n);
                if (Float3.LengthSquared(nf) > 1e-6f) nf = Float3.Normalize(nf);
                else nf = upF;
                if (Float3.Dot(nf, upF) < 0.1f) nf = upF;

                // Weighted average of all contacting rays (compression, point, normal), weighting deeper
                // rays by closeness^5.
                float c2 = c * c;
                float w = c2 * c2 * c;
                weightSum += w;
                compSum += c * w;
                pointSum += (ToF(origin) - upF * dist) * w;
                normalSum += nf * w;

                if (c > bestC) { bestC = c; outGround = rbs.RigidBody; }
            }
        }

        if (weightSum <= 0.0f) return false;

        float inv = 1.0f / weightSum;
        rawCompression = compSum * inv;
        outPoint = pointSum * inv;
        if (Float3.LengthSquared(normalSum) > 1e-6f) outNormal = Float3.Normalize(normalSum);
        return true;
    }

    // Penalty contact for when the suspension is fully compressed and the wheel is still inside an object
    // (a kerb, a hard landing). Pushes the chassis - and a dynamic ground body - apart along the contact
    // normal, scaled by penetration depth plus a closing-speed term, so the rigid wheel doesn't sink in.
    private void ApplyDepenetration(RigidBody car, float penetration, JVector normal, JVector point, float dt)
    {
        Double3 nD = D(normal);
        Double3 relVel = D(car.Velocity);
        if (groundBody != null && groundBody.MotionType != MotionType.Static)
            relVel -= D(groundBody.Velocity);
        float contactVel = (float)Double3.Dot(relVel, nD); // < 0 closing, > 0 separating

        float f = (float)rb.Mass / wheelCount * 9.81f / radius; // weight-per-wheel scaled by radius
        float j = penetration * f * 12.0f * dt
                + -contactVel * dt * f * (contactVel < 0.0f ? 0.8f : 3.0f);
        if (j <= 0.0f) return;

        JVector impulse = normal * j;
        car.ApplyImpulse(impulse, _mountJ);
        car.SetActivationState(true);
        if (groundBody != null && groundBody.MotionType != MotionType.Static)
        {
            groundBody.SetActivationState(true);
            groundBody.ApplyImpulse(-impulse, point);
        }
    }

    public override void DrawGizmos()
    {
        Float3 center = GetWheelCenter();
        Float3 up = Transform.Up;

        // Wheel frame: steered forward + axle, and a radial basis (r1, r2) in the roll plane.
        JVector fwdJ = JVector.Transform(ToJ(Transform.Forward), JMatrix.CreateRotationMatrix(ToJ(up), SteerAngle));
        JVector axleJ = JVector.Cross(ToJ(up), fwdJ);
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
            Debug.DrawLine(contactPoint, contactPoint + ToF(_planeLeft) * 0.3f, new Color(1f, 0f, 1f, 1f));    // lateral (magenta)
            Debug.DrawLine(contactPoint, contactPoint + ToF(_planeFwd) * (_fLong * 0.002f), new Color(1f, 1f, 0f, 1f));   // long force (yellow)
            Debug.DrawLine(contactPoint, contactPoint + ToF(_planeLeft) * (_fLat * 0.002f), new Color(1f, 0.5f, 0f, 1f)); // lat force (orange)
        }
    }

    private static void DrawCross(Float3 p, float s, Color c)
    {
        Debug.DrawLine(p - new Float3(s, 0, 0), p + new Float3(s, 0, 0), c);
        Debug.DrawLine(p - new Float3(0, s, 0), p + new Float3(0, s, 0), c);
        Debug.DrawLine(p - new Float3(0, 0, s), p + new Float3(0, 0, s), c);
    }
}
