// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace VehicleShowcase;

/// <summary>
/// Drives a vehicle made of WheelColliders under one Rigidbody3D: throttle, brakes, steering, handbrake, boost,
/// traction control, downforce and a little help in the air. Wheels are grouped into axles that each choose how far
/// they steer and whether they are driven, so one controller runs a kart, a road car, a monster truck with four
/// wheel steering or a six wheeled truck. It depends on nothing else in the sample, so it can be copied as it is.
/// </summary>
public sealed class CarController : MonoBehaviour
{
    public sealed class Axle
    {
        public readonly List<WheelCollider> Wheels = new();

        /// <summary>How much of the steering this axle takes: 1 for a front axle, 0 for a fixed one, negative to steer against the front.</summary>
        public float Steer;
        public bool Driven;

        /// <summary>Whether the handbrake locks this axle.</summary>
        public bool Handbrake;

        /// <summary>The axle's sideways grip, which the handbrake lowers while it is held.</summary>
        public float Grip = 1.6f;
    }

    public readonly List<Axle> Axles = new();

    /// <summary>Total drive torque in N m, shared between the driven wheels.</summary>
    public float Torque = 2800f;

    /// <summary>Speed in m/s past which the drive stops pushing.</summary>
    public float TopSpeed = 50f;
    public float MaxSteer = 32f;

    /// <summary>How far past the grip limit the steering may go at speed, for catching a slide. Lower is more forgiving.</summary>
    public float SteerHeadroom = 1.25f;
    public float Brake = 3500f;

    /// <summary>The share of the drive torque that holds the driven wheels back with the throttle off, as an engine does.</summary>
    public float EngineBraking = 0.1f;

    /// <summary>The share of its grip a handbraked axle keeps sideways.</summary>
    public float HandbrakeGrip = 0.45f;

    /// <summary>A push into the ground that grows with the square of the speed.</summary>
    public float Downforce = 1.5f;

    /// <summary>How hard the car is levelled and calmed while every wheel is off the ground.</summary>
    public float AirStability = 0.5f;

    /// <summary>
    /// Calms the car's rotation once it slides further than <see cref="MaxSlideAngle"/>, as a road car's stability
    /// control does, so a slide is caught before it becomes a spin. The handbrake switches it off for drifting.
    /// </summary>
    public bool StabilityControl = true;

    /// <summary>How far, in degrees, the car may slide sideways before stability control steps in.</summary>
    public float MaxSlideAngle = 12f;

    /// <summary>Cuts the drive while the driven wheels spin faster than the ground, as a road car's traction control does.</summary>
    public bool TractionControl = true;

    /// <summary>How fast a driven tyre may spin past the ground, in m/s, before the drive is cut. The tyre grips hardest at about this much.</summary>
    public float AllowedWheelspin = 4f;

    /// <summary>Reads the keyboard when set. Otherwise the vehicle holds its brakes and waits.</summary>
    public bool Controlled;
    public bool LightsOn = true;
    public Material? BrakeLights;
    public readonly List<Light> Headlights = new();

    private Rigidbody3D _body = null!;
    private float _steer;
    private float _traction = 1f;
    private float _wheelbase = 1f;
    private readonly Dictionary<WheelCollider, float> _antiLock = new();
    private bool _handbrakeHeld;

    public IEnumerable<WheelCollider> Wheels => Axles.SelectMany(axle => axle.Wheels);
    public bool Boosting { get; private set; }
    public string GearLabel { get; private set; } = "N";
    /// <summary>Speed over the ground under the wheels, which may itself be a moving trailer or platform.</summary>
    public float Speed => _body.IsValid() ? Float3.Length(GroundRelativeVelocity()) : 0f;

    public override void OnEnable() => _body = GetComponent<Rigidbody3D>()!;

    public override void Update()
    {
        float dt = Time.DeltaTime;
        float throttle = 0f, steer = 0f;
        bool handbrake = !Controlled;
        Boosting = false;
        if (Controlled)
        {
            if (Input.GetKey(KeyCode.W)) throttle += 1f;
            if (Input.GetKey(KeyCode.S)) throttle -= 1f;
            if (Input.GetKey(KeyCode.D)) steer += 1f;
            if (Input.GetKey(KeyCode.A)) steer -= 1f;
            handbrake = Input.GetKey(KeyCode.Space);
            _handbrakeHeld = handbrake;
            Boosting = throttle > 0f && Input.GetKey(KeyCode.ShiftLeft);
        }

        foreach (Light light in Headlights) light.Enabled = Controlled && LightsOn;

        float forwardSpeed = Float3.Dot(GroundRelativeVelocity(), _body.Rotation * Float3.UnitZ);
        bool braking = (throttle < 0f && forwardSpeed > 1f) || (throttle > 0f && forwardSpeed < -1f);
        GearLabel = !Controlled ? "P" : braking ? "BRAKE" : forwardSpeed < -0.5f ? "R" : throttle != 0f ? "D" : "N";

        // At speed the lock is held near what the tyres can turn the car with, so a short car does not spin itself out,
        // plus as far again as the car is sliding, so a drift can always be caught by steering into it. The wheel also
        // turns at a finite rate, so it does not twitch.
        float lockAtSpeed = MathF.Min(MaxSteer, GripLock(forwardSpeed) * SteerHeadroom + SlideAngle());
        _steer += (steer * lockAtSpeed - _steer) * MathF.Min(1f, dt * 8f);

        float limit = TopSpeed * (Boosting ? 1.4f : 1f);
        // The drive eases off over the last tenth of the top speed rather than cutting out, so it never jerks the tyres.
        float headroom = Math.Clamp((limit - MathF.Abs(forwardSpeed)) / (limit * 0.1f), 0f, 1f);
        float drive = braking ? 0f : throttle * Torque * (Boosting ? 2f : 1f) * headroom;
        drive *= Traction(throttle, dt);

        int driven = Axles.Where(axle => axle.Driven).Sum(axle => axle.Wheels.Count);
        float perWheel = driven > 0 ? drive / driven : 0f;

        foreach (Axle axle in Axles)
        {
            bool locked = handbrake && (axle.Handbrake || !Controlled);
            foreach (WheelCollider wheel in axle.Wheels)
            {
                wheel.SteerAngle = _steer * axle.Steer * MathF.PI / 180f;
                wheel.MotorTorque = axle.Driven && !locked ? perWheel : 0f;
                float share = AntiLock(wheel, forwardSpeed, dt);
                float coasting = axle.Driven && throttle == 0f && driven > 0 ? EngineBraking * Torque / driven : 0f;
                wheel.BrakeTorque = locked ? Brake : braking ? Brake * share : coasting;
                wheel.SidewaysFriction = locked && Controlled ? axle.Grip * HandbrakeGrip : axle.Grip;
            }
        }

        if (BrakeLights.IsValid()) BrakeLights!.SetFloat("_EmissionIntensity", braking || (handbrake && Controlled) ? 6f : 1f);
    }

    // The steering angle, in degrees, that turns the car as tightly as its least grippy axle can hold at this speed.
    private float GripLock(float speed)
    {
        Float3 forward = _body.Rotation * Float3.UnitZ;
        float front = float.MinValue, rear = float.MaxValue, grip = float.MaxValue;
        foreach (Axle axle in Axles)
        {
            grip = MathF.Min(grip, axle.Grip);
            foreach (WheelCollider wheel in axle.Wheels)
            {
                float along = Float3.Dot(wheel.Transform.Position - _body.Position, forward);
                front = MathF.Max(front, along);
                rear = MathF.Min(rear, along);
            }
        }

        float wheelbase = MathF.Max(front - rear, 0.1f);
        _wheelbase = wheelbase;
        float sideways = grip * 9.81f;
        return MathF.Atan(wheelbase * sideways / MathF.Max(speed * speed, 1f)) * 180f / MathF.PI;
    }

    // Eases a brake off while its wheel skids and back on once it rolls again, so hard braking keeps the car steerable.
    // The handbrake skips this, it is meant to lock the wheels.
    private float AntiLock(WheelCollider wheel, float speed, float dt)
    {
        float share = _antiLock.TryGetValue(wheel, out float current) ? current : 1f;
        bool skidding = wheel.ForwardSlip * MathF.Sign(speed) > MathF.Max(1f, MathF.Abs(speed) * 0.1f);
        share = skidding ? MathF.Max(0.1f, share - dt * 20f) : MathF.Min(1f, share + dt * 8f);
        _antiLock[wheel] = share;
        return share;
    }

    // How far the car slides sideways over the ground, in degrees.
    private float SlideAngle()
    {
        Float3 up = _body.Rotation * Float3.UnitY;
        Float3 relative = GroundRelativeVelocity();
        Float3 velocity = relative - up * Float3.Dot(relative, up);
        float speed = Float3.Length(velocity);
        if (speed < 3f) return 0f;
        float along = MathF.Abs(Float3.Dot(_body.Rotation * Float3.UnitZ, velocity)) / speed;
        return MathF.Acos(Math.Clamp(along, -1f, 1f)) * 180f / MathF.PI;
    }

    // The body's velocity over whatever its wheels stand on, so driving on a moving trailer works like driving on the ground.
    private Float3 GroundRelativeVelocity()
    {
        Float3 ground = Float3.Zero;
        int touching = 0;
        foreach (WheelCollider wheel in Wheels)
        {
            if (!wheel.GetGroundHit(out WheelHit hit)) continue;
            ground += hit.GroundVelocity;
            touching++;
        }
        return _body.LinearVelocity - (touching > 0 ? ground / touching : Float3.Zero);
    }

    // Eases the drive down while any driven wheel spins past the ground, and back up once they grip again.
    private float Traction(float throttle, float dt)
    {
        if (!TractionControl || throttle == 0f) return _traction = 1f;

        float spin = 0f;
        foreach (Axle axle in Axles)
            if (axle.Driven)
                foreach (WheelCollider wheel in axle.Wheels)
                    spin = MathF.Max(spin, -wheel.ForwardSlip * MathF.Sign(throttle));

        _traction = spin > AllowedWheelspin ? MathF.Max(0f, _traction - dt * 10f) : MathF.Min(1f, _traction + dt * 3f);
        return _traction;
    }

    public override void FixedUpdate()
    {
        Float3 up = _body.Rotation * Float3.UnitY;
        if (!Wheels.Any(wheel => wheel.IsGrounded))
        {
            // Airborne: bleed off the tumble and lean the car back upright, so jumps land on the wheels.
            if (AirStability <= 0f) return;
            _body.AngularVelocity *= 1f - MathF.Min(1f, AirStability * 2f * Time.FixedDeltaTime);
            _body.AddTorque(Float3.Cross(up, Float3.UnitY) * AirStability * 12f, ForceMode.Acceleration);
            return;
        }

        if (StabilityControl && !_handbrakeHeld) Stabilise(up);

        if (Downforce <= 0f) return;
        float speed = Float3.Dot(GroundRelativeVelocity(), _body.Rotation * Float3.UnitZ);
        _body.AddForce(-up * Downforce * speed * speed * _body.Mass * 0.001f);
    }

    // Pulls the rotation back toward what the steering asks for, once the car slides too far sideways.
    private void Stabilise(Float3 up)
    {
        Float3 forward = _body.Rotation * Float3.UnitZ;
        Float3 relative = GroundRelativeVelocity();
        Float3 velocity = relative - up * Float3.Dot(relative, up);
        float speed = Float3.Length(velocity);
        if (speed < 3f || Axles.Count == 0) return;

        float along = Float3.Dot(forward, velocity) / speed;
        float slide = MathF.Acos(Math.Clamp(along, -1f, 1f)) * 180f / MathF.PI;
        if (slide <= MaxSlideAngle) return;

        // The turn the steering asks for: front and rear axles each steer by their own share.
        float steerFront = Axles.Max(axle => axle.Steer), steerRear = Axles.Min(axle => axle.Steer);
        float angle = _steer * MathF.PI / 180f;
        float turn = (MathF.Tan(angle * steerFront) - MathF.Tan(angle * steerRear)) / _wheelbase;
        float grip = Axles.Min(axle => axle.Grip) * 9.81f;
        float wanted = Math.Clamp(Float3.Dot(forward, velocity) * turn, -grip / speed, grip / speed);
        float yaw = Float3.Dot(_body.AngularVelocity, up);
        float strength = Math.Clamp((slide - MaxSlideAngle) / 10f, 0f, 1f);
        _body.AddTorque(up * (wanted - yaw) * 6f * strength, ForceMode.Acceleration);
    }
}
