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
/// A volumetric raycast-vehicle wheel. Instead of a thin ray, it sweeps a cylinder (the wheel
/// volume) down the suspension axis each physics step, so it can't tunnel through edges or fall
/// into cracks and it produces a stable contact normal. Suspension is a spring/damper tuned from a
/// natural frequency, and grip uses a saturating slip model clamped to a friction circle.
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

    // Suspension
    [SerializeField] private float suspensionDistance = 0.3f;
    [SerializeField] private float suspensionFrequency = 2.0f;    // Hz, higher = stiffer
    [SerializeField] private float suspensionDampingRatio = 0.5f; // 1 = critically damped
    [SerializeField] private float sprungMass = 0.0f;            // 0 = auto (body mass / wheel count)

    // Friction (friction-circle)
    [SerializeField] private float forwardFriction = 1.6f;       // longitudinal grip coefficient
    [SerializeField] private float sidewaysFriction = 1.8f;      // lateral grip coefficient
    [SerializeField] private float gripSaturationSpeed = 4.0f;   // slip speed (m/s) at which grip saturates

    // Wheel spin
    [SerializeField] private float wheelMass = 15.0f;            // for spin inertia I = 0.5*m*r^2
    [SerializeField] private float maxAngularVelocity = 250.0f;

    /// <summary>Steering angle in radians (rotation about the suspension axis). Set by a controller.</summary>
    public float SteerAngle;

    /// <summary>Drive torque in N*m applied to the wheel spin this step. Set by a controller each frame.</summary>
    public float MotorTorque;

    /// <summary>Brake torque in N*m opposing the wheel spin this step. Set by a controller each frame.</summary>
    public float BrakeTorque;

    // Runtime state
    private Rigidbody3D rb;
    private int wheelCount = 1;
    private bool counted;
    private float displacement;        // suspension compression, 0..suspensionDistance
    private bool onFloor;
    private float angularVelocity;     // wheel spin (rad/s)
    private float wheelRotation;       // accumulated spin (rad), for visuals
    private float frictionTorque;      // ground reaction torque on the wheel this step
    private float rollingAngVel;       // spin that matches ground speed (pure rolling)
    private Float3 contactPoint;
    private Float3 contactNormal = Float3.UnitY;
    private RigidBody groundBody;

    // Public configuration
    public float Radius { get => radius; set => radius = Maths.Max(0.01f, value); }
    public float Width { get => width; set => width = Maths.Max(0.01f, value); }
    public float SuspensionDistance { get => suspensionDistance; set => suspensionDistance = Maths.Max(0.0f, value); }
    public float SuspensionFrequency { get => suspensionFrequency; set => suspensionFrequency = Maths.Max(0.1f, value); }
    public float SuspensionDampingRatio { get => suspensionDampingRatio; set => suspensionDampingRatio = Maths.Max(0.0f, value); }
    /// <summary>Sprung mass (kg) this wheel supports. 0 = auto-estimate from the body mass and wheel count.</summary>
    public float SprungMass { get => sprungMass; set => sprungMass = Maths.Max(0.0f, value); }
    public float ForwardFriction { get => forwardFriction; set => forwardFriction = Maths.Max(0.0f, value); }
    public float SidewaysFriction { get => sidewaysFriction; set => sidewaysFriction = Maths.Max(0.0f, value); }
    public float GripSaturationSpeed { get => gripSaturationSpeed; set => gripSaturationSpeed = Maths.Max(0.1f, value); }
    public float WheelMass { get => wheelMass; set => wheelMass = Maths.Max(0.01f, value); }
    public float MaxAngularVelocity { get => maxAngularVelocity; set => maxAngularVelocity = Maths.Max(0.0f, value); }

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
        suspensionDistance = Maths.Max(0.0f, suspensionDistance);
        suspensionFrequency = Maths.Max(0.1f, suspensionFrequency);
        suspensionDampingRatio = Maths.Max(0.0f, suspensionDampingRatio);
        sprungMass = Maths.Max(0.0f, sprungMass);
        forwardFriction = Maths.Max(0.0f, forwardFriction);
        sidewaysFriction = Maths.Max(0.0f, sidewaysFriction);
        gripSaturationSpeed = Maths.Max(0.1f, gripSaturationSpeed);
        wheelMass = Maths.Max(0.01f, wheelMass);
        maxAngularVelocity = Maths.Max(0.0f, maxAngularVelocity);
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

        GameObject.Scene.Physics.PreStep += OnPreStep;
        GameObject.Scene.Physics.PostStep += OnPostStep;
    }

    public override void OnDisable()
    {
        if (GameObject?.Scene?.Physics != null)
        {
            GameObject.Scene.Physics.PreStep -= OnPreStep;
            GameObject.Scene.Physics.PostStep -= OnPostStep;
        }
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

        // Volumetric contact: sweep the wheel cylinder down the suspension axis over the travel
        // range. The cylinder's height axis (local Y) is aligned to the axle.
        JVector mount = ToJ(Transform.Position);
        JQuaternion cylOrient = JQuaternion.CreateFromToRotation(JVector.UnitY, axle);
        JVector sweepDir = -up;
        float maxLambda = suspensionDistance + 1e-4f;

        bool hit = world.DynamicTree.SweepCastCylinder(
            radius, width * 0.5f, cylOrient, mount, sweepDir, maxLambda,
            SweepFilter, null,
            out IDynamicTreeProxy proxy, out _, out _, out JVector normal, out float lambda);

        if (!hit || proxy is not RigidBodyShape rbs)
        {
            onFloor = false;
            groundBody = null;
            displacement = 0.0f;
            return;
        }

        onFloor = true;
        groundBody = rbs.RigidBody;

        // lambda = distance travelled down before contact; compression is the remaining travel.
        lambda = Maths.Clamp(lambda, 0.0f, suspensionDistance);
        displacement = Maths.Clamp(suspensionDistance - lambda, 0.0f, suspensionDistance);

        // A degenerate (overlapping) sweep returns a zero normal; fall back to the suspension axis.
        Float3 n = ToF(normal);
        if (Float3.LengthSquared(n) > 1e-6f)
        {
            JVector.NormalizeInPlace(ref normal);
            n = ToF(normal);
        }
        else n = upF;
        // A grazing/edge hit can report a near-horizontal normal; treating it as the suspension axis
        // keeps the load from collapsing to zero for a frame (which causes support-loss bobbing). We
        // still respond to the hit, just hold the wheel up instead of dropping it.
        if (Float3.Dot(n, upF) < 0.1f) n = upF;
        contactNormal = n;

        Float3 restedCenter = ToF(mount) - upF * lambda;
        contactPoint = restedCenter - upF * radius;
        JVector contact = ToJ(contactPoint);

        // Relative velocity of the chassis contact point (car minus ground body). Used for both the
        // suspension damper and tyre friction. It comes straight from the solver, so it doesn't
        // amplify sweep noise the way a frame-differenced compression rate would.
        JVector vRel = car.Velocity + JVector.Cross(car.AngularVelocity, contact - car.Position);
        if (groundBody != null && groundBody.MotionType != MotionType.Static)
            vRel -= groundBody.Velocity + JVector.Cross(groundBody.AngularVelocity, contact - groundBody.Position);

        // Suspension (spring + damper, tuned from a natural frequency). The damper acts on the real
        // contact-point velocity along the suspension axis, so it stays stable at any step size and
        // can't pump energy in the way a lagged finite-difference damper can.
        float sMass = sprungMass > 0.0f ? sprungMass : (float)rb.Mass / wheelCount;
        float omega = 2.0f * Maths.PI * suspensionFrequency;
        float springK = sMass * omega * omega;
        float damperC = 2.0f * suspensionDampingRatio * sMass * omega;

        float suspVelUp = (float)JVector.Dot(vRel, up); // + = extending, - = compressing
        float springForce = springK * displacement;

        // Cap the damper force at the spring's full-compression force. A wheel far from the COM sees
        // a huge contact-point velocity when the chassis rotates even slightly; without this cap the
        // damper turns that into an enormous force, which spins the chassis faster (more contact-point
        // velocity) and runs away. Capping keeps the damper dissipative without injecting energy.
        float damperForce = -damperC * suspVelUp;
        float damperCap = springK * suspensionDistance;
        damperForce = Maths.Clamp(damperForce, -damperCap, damperCap);

        float load = springForce + damperForce;
        load *= Maths.Max(0.0f, Float3.Dot(contactNormal, upF)); // taper on slopes
        if (load < 0.0f) load = 0.0f;

        JVector suspensionForce = load * up;

        // Tyre friction (saturating slip, friction-circle clamp).
        JVector cn = ToJ(contactNormal);
        JVector planeFwd = fwd - cn * JVector.Dot(fwd, cn);
        if (planeFwd.LengthSquared() < 1e-8f) planeFwd = fwd;
        JVector.NormalizeInPlace(ref planeFwd);
        JVector planeLeft = JVector.Cross(cn, planeFwd);
        JVector.NormalizeInPlace(ref planeLeft);

        float vFwd = JVector.Dot(vRel, planeFwd);
        float vLat = JVector.Dot(vRel, planeLeft);

        float rimSpeed = angularVelocity * radius;
        float longSlip = vFwd - rimSpeed; // contact faster than rim => braking reaction
        rollingAngVel = vFwd / radius;

        float maxLong = forwardFriction * load;
        float maxLat = sidewaysFriction * load;

        float fLong = -Maths.Clamp(longSlip / gripSaturationSpeed, -1.0f, 1.0f) * maxLong;
        float fLat = -Maths.Clamp(vLat / gripSaturationSpeed, -1.0f, 1.0f) * maxLat;

        // Friction circle: the combined force can't exceed the available grip ellipse.
        float ex = maxLong > 0.0f ? fLong / maxLong : 0.0f;
        float ey = maxLat > 0.0f ? fLat / maxLat : 0.0f;
        float e = Maths.Sqrt(ex * ex + ey * ey);
        if (e > 1.0f) { fLong /= e; fLat /= e; }

        JVector frictionForce = fLong * planeFwd + fLat * planeLeft;

        // Reaction torque on the wheel spin from the longitudinal ground force.
        frictionTorque = -fLong * radius;

        // Auto force-application point: apply the tyre forces at the centre-of-mass height (along the
        // suspension axis, at the wheel's horizontal position) instead of way down at the contact.
        // This zeroes the vertical lever of the horizontal friction force so the car can't tip over,
        // while the suspension's roll/pitch restoring is preserved (that comes from the wheels'
        // horizontal spread, which is unchanged). No manual "force app point" tuning needed.
        float appHeight = Maths.Max(0.0f, (float)JVector.Dot(car.Position - contact, up));
        JVector forcePoint = contact + up * appHeight;

        JVector total = suspensionForce + frictionForce;
        car.AddForce(total, forcePoint);
        car.SetActivationState(true);

        if (groundBody != null && groundBody.MotionType != MotionType.Static)
        {
            // Newton's third law reaction goes to the ground at the real contact point. Clamp it so a
            // heavy vehicle can't launch small dynamic bodies.
            const float maxOtherAcc = 500.0f;
            float maxOtherForce = maxOtherAcc * (float)groundBody.Mass;
            JVector reaction = total;
            if (reaction.LengthSquared() > maxOtherForce * maxOtherForce)
                reaction *= maxOtherForce / reaction.Length();
            groundBody.SetActivationState(true);
            groundBody.AddForce(-reaction, contact);
        }
    }

    private void OnPostStep(float timeStep)
    {
        if (rb?._body == null || timeStep <= 0.0f) return;

        float dt = timeStep;
        float inertia = 0.5f * wheelMass * radius * radius;
        if (inertia <= 1e-6f) inertia = 1e-6f;

        float orig = angularVelocity;

        // Ground reaction drives the spin toward pure rolling.
        if (onFloor)
        {
            angularVelocity += frictionTorque * dt / inertia;
            frictionTorque = 0.0f;

            // Don't let the reaction overshoot the rolling speed (prevents jitter/oscillation).
            if ((orig > rollingAngVel && angularVelocity < rollingAngVel) ||
                (orig < rollingAngVel && angularVelocity > rollingAngVel))
                angularVelocity = rollingAngVel;
        }

        // Drive torque (can spin the wheel past the rolling speed, i.e. wheelspin).
        angularVelocity += MotorTorque * dt / inertia;

        // Brake torque opposes the spin and can't reverse it.
        float brakeDelta = BrakeTorque * dt / inertia;
        if (Maths.Abs(angularVelocity) <= brakeDelta) angularVelocity = 0.0f;
        else angularVelocity -= (angularVelocity > 0.0f ? 1.0f : -1.0f) * brakeDelta;

        angularVelocity = Maths.Clamp(angularVelocity, -maxAngularVelocity, maxAngularVelocity);
        wheelRotation += angularVelocity * dt;
    }

    private bool SweepFilter(IDynamicTreeProxy proxy)
    {
        if (proxy is not RigidBodyShape rbs) return false;
        if (rb?._body == null) return false;
        return rbs.RigidBody != rb._body; // ignore the vehicle's own body
    }

    private static JVector ToJ(Float3 v) => new(v.X, v.Y, v.Z);
    private static Float3 ToF(JVector v) => new(v.X, v.Y, v.Z);

    public override void DrawGizmos()
    {
        Float3 center = GetWheelCenter();
        Float3 up = Transform.Up;

        // Wheel frame: steered forward + axle, and a radial basis (r1, r2) in the roll plane.
        JVector fwdJ = JVector.Transform(ToJ(Transform.Forward), JMatrix.CreateRotationMatrix(ToJ(up), SteerAngle));
        JVector axleJ = JVector.Cross(ToJ(up), fwdJ);
        JVector.NormalizeInPlace(ref axleJ);

        Float3 axle = ToF(axleJ);
        Float3 r1 = Float3.Normalize(ToF(fwdJ));
        Float3 r2 = Float3.Normalize(Float3.Cross(axle, r1));

        float halfW = width * 0.5f;
        Float3 leftC = center + axle * halfW;
        Float3 rightC = center - axle * halfW;

        Color color = onFloor ? new Color(0f, 1f, 0f, 1f) : new Color(1f, 0.5f, 0f, 1f);

        // Cylinder: a circle on each wheel face plus four struts joining them.
        Debug.DrawWireCircle(leftC, axle, radius, color, 24);
        Debug.DrawWireCircle(rightC, axle, radius, color, 24);
        for (int i = 0; i < 4; i++)
        {
            float a = i * (Maths.PI * 0.5f);
            Float3 dir = r1 * Maths.Cos(a) + r2 * Maths.Sin(a);
            Debug.DrawLine(leftC + dir * radius, rightC + dir * radius, color);
        }

        // Suspension travel line + contact normal.
        Debug.DrawLine(Transform.Position, Transform.Position - up * suspensionDistance, new Color(1f, 1f, 0f, 1f));
        if (onFloor)
            Debug.DrawLine(contactPoint, contactPoint + contactNormal * 0.25f, new Color(0f, 0.6f, 1f, 1f));
    }
}
