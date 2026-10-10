// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace VehicleShowcase;

/// <summary>
/// Rides a two wheeled vehicle made of two WheelColliders under one Rigidbody3D. Steering input asks for a lean,
/// a balance torque leans the bike there, and the front wheel turns as far as that lean needs at the current
/// speed, the way a rider steers a real bike. At walking pace it steers directly and stays upright on its own.
/// It depends on nothing else in the sample, so it can be copied as it is.
/// </summary>
public sealed class MotorcycleController : Component
{
    public WheelCollider Front = null!;
    public WheelCollider Rear = null!;

    /// <summary>Turned with the front wheel about its own up axis, for the forks and handlebars.</summary>
    public Transform? Steering;

    /// <summary>Drive torque in N m on the rear wheel.</summary>
    public float Torque = 700f;

    /// <summary>Speed in m/s past which the drive stops pushing.</summary>
    public float TopSpeed = 55f;
    public float Brake = 1500f;

    /// <summary>The furthest the bike leans into a turn, in degrees.</summary>
    public float MaxLean = 50f;

    /// <summary>How quickly the bike reaches the lean asked for. Higher is snappier.</summary>
    public float LeanResponse = 10f;

    /// <summary>The share of the brake the rear wheel gets. The front does most of a bike's braking.</summary>
    public float RearBrakeShare = 0.3f;

    /// <summary>How much of the lean is kept while braking, so the bike stands up a little as a rider would.</summary>
    public float LeanWhileBraking = 0.6f;

    /// <summary>
    /// How much more the front wheel turns than the lean alone would balance, for tighter turns than a real bike
    /// manages at speed. 1 is true to life.
    /// </summary>
    public float TurnAssist = 2f;

    /// <summary>
    /// Holds the drive back to what the rear tyre can take on top of the lean, and cuts it while the tyre spins or
    /// slides, so opening the throttle in a turn does not swing the back out.
    /// </summary>
    public bool TractionControl = true;

    /// <summary>The steering lock at walking pace, in degrees.</summary>
    public float MaxSteer = 30f;

    /// <summary>Reads the keyboard when set. Otherwise the bike holds its brakes and stands upright.</summary>
    public bool Controlled;
    public bool LightsOn = true;
    public Material? BrakeLights;
    public readonly List<Light> Headlights = new();

    private const float Gravity = 9.81f;

    private Rigidbody3D _body = null!;
    private Quaternion _steeringRest = Quaternion.Identity;
    private float _input;
    private float _targetLean;
    private float _steer;
    private float _frontBrake = 1f, _rearBrake = 1f, _traction = 1f;

    public IEnumerable<WheelCollider> Wheels => [Front, Rear];

    /// <summary>The lean in degrees, positive to the right.</summary>
    public float Lean { get; private set; }
    public string GearLabel { get; private set; } = "N";
    /// <summary>Speed over the ground under the wheels, which may itself be a moving trailer or platform.</summary>
    public float Speed => _body.IsValid() ? Float3.Length(GroundRelativeVelocity()) : 0f;

    public override void OnEnable()
    {
        _body = GetComponent<Rigidbody3D>()!;
        if (Steering != null) _steeringRest = Steering.LocalRotation;
    }

    public override void Update()
    {
        float dt = Time.DeltaTime;
        float throttle = 0f, steer = 0f;
        bool brake = !Controlled;
        if (Controlled)
        {
            if (Input.GetKey(KeyCode.W)) throttle += 1f;
            if (Input.GetKey(KeyCode.S)) throttle -= 1f;
            if (Input.GetKey(KeyCode.D)) steer += 1f;
            if (Input.GetKey(KeyCode.A)) steer -= 1f;
            brake = Input.GetKey(KeyCode.Space);
        }

        foreach (Light light in Headlights) light.Enabled = Controlled && LightsOn;

        float forwardSpeed = Float3.Dot(GroundRelativeVelocity(), _body.Rotation * Float3.UnitZ);
        bool braking = brake || (throttle < 0f && forwardSpeed > 1f) || (throttle > 0f && forwardSpeed < -1f);
        GearLabel = !Controlled ? "P" : braking ? "BRAKE" : forwardSpeed < -0.5f ? "R" : throttle != 0f ? "D" : "N";

        _input += (steer - _input) * MathF.Min(1f, dt * 10f);

        // Leaning only makes sense once the bike is rolling. Below that it steers like a scooter being walked.
        float rolling = Math.Clamp((MathF.Abs(forwardSpeed) - 2f) / 4f, 0f, 1f);
        _targetLean = _input * MaxLean * rolling * (braking ? LeanWhileBraking : 1f);

        float lean = Lean * MathF.PI / 180f;
        float wheelbase = Float3.Distance(Front.Transform.Position, Rear.Transform.Position);
        float leaned = MathF.Atan(wheelbase * Gravity * MathF.Tan(lean) / MathF.Max(forwardSpeed * forwardSpeed, 1f)) * 180f / MathF.PI * TurnAssist;
        float walked = _input * MaxSteer;
        _steer = Math.Clamp(walked + (leaned - walked) * rolling, -MaxSteer, MaxSteer);

        Front.SteerAngle = _steer * MathF.PI / 180f;
        if (Steering != null) Steering.LocalRotation = _steeringRest * Quaternion.AxisAngle(Float3.UnitY, Front.SteerAngle);

        float headroom = Math.Clamp((TopSpeed - MathF.Abs(forwardSpeed)) / (TopSpeed * 0.1f), 0f, 1f);
        _traction = TractionControl ? Traction(forwardSpeed, dt) : 1f;
        float drive = braking ? 0f : throttle * Torque * headroom * _traction;
        Rear.MotorTorque = drive;
        Front.MotorTorque = 0f;
        _frontBrake = AntiLock(Front, _frontBrake, forwardSpeed, dt);
        _rearBrake = AntiLock(Rear, _rearBrake, forwardSpeed, dt);
        // A leaned tyre is already using its grip to turn, so the brakes ease off with the lean.
        float leanShare = 1f - 0.6f * Math.Clamp(MathF.Abs(Lean) / MathF.Max(MaxLean, 1f), 0f, 1f);
        Front.BrakeTorque = braking ? Brake * _frontBrake * leanShare : 0f;
        Rear.BrakeTorque = braking ? Brake * RearBrakeShare * _rearBrake * leanShare : 0f;

        if (BrakeLights.IsValid()) BrakeLights!.SetFloat("_EmissionIntensity", braking ? 6f : 1f);
    }

    // The share of the drive the rear tyre can take. Leaned over, the tyre already holds the turn, and its contact
    // sits inside the body's centre, so pushing there swings the body further in and the tyre must hold that too.
    // The drive is kept to what fits inside the tyre's grip on top of both, and eases off further while the tyre
    // spins or slides, coming back once it grips. It goes by the lean asked for, so it backs off as the turn begins.
    private float Traction(float speed, float dt)
    {
        float moving = MathF.Max(MathF.Abs(speed), 1f);
        bool spinning = MathF.Abs(Rear.ForwardSlip) > MathF.Max(1f, moving * 0.08f);
        bool sliding = MathF.Abs(Rear.SidewaysSlip) / moving > MathF.Tan(6f * MathF.PI / 180f);
        float share = spinning || sliding ? MathF.Max(0f, _traction - dt * 10f) : MathF.Min(1f, _traction + dt * 2f);

        if (!Rear.GetGroundHit(out WheelHit hit) || Rear.Load <= 0f) return share;
        Float3 arm = _body.Position - hit.Point;
        float behind = MathF.Max(Float3.Dot(arm, hit.ForwardDir), 0.1f);
        float swing = MathF.Abs(Float3.Dot(arm, hit.SidewaysDir)) / behind;

        // Largest drive force F with (F / longGrip)^2 + ((turn + swing F) / latGrip)^2 inside the margin.
        const float Margin = 0.75f;
        float longGrip = Rear.ForwardFriction * Rear.Load, latGrip = Rear.SidewaysFriction * Rear.Load;
        float lean = MathF.Max(MathF.Abs(Lean), MathF.Abs(_targetLean));
        float turn = Rear.Load * MathF.Tan(lean * MathF.PI / 180f);
        float qa = 1f / (longGrip * longGrip) + swing * swing / (latGrip * latGrip);
        float qb = turn * swing / (latGrip * latGrip);
        float qc = turn * turn / (latGrip * latGrip) - Margin * Margin;
        float force = qc >= 0f ? 0f : (-qb + MathF.Sqrt(qb * qb - qa * qc)) / qa;
        return MathF.Min(share, force * Rear.Radius / MathF.Max(Torque, 1f));
    }

    // Eases a brake off while its wheel skids and back on once it rolls again, so a hard stop keeps the bike steerable.
    private static float AntiLock(WheelCollider wheel, float share, float speed, float dt)
    {
        bool skidding = wheel.ForwardSlip > MathF.Max(0.5f, MathF.Abs(speed) * 0.05f);
        return skidding ? MathF.Max(0.1f, share - dt * 20f) : MathF.Min(1f, share + dt * 8f);
    }

    // The body's velocity over whatever its wheels stand on, so riding on a moving trailer works like riding on the ground.
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

    public override void FixedUpdate()
    {
        Quaternion rotation = _body.Rotation;
        Float3 up = rotation * Float3.UnitY;
        Float3 forward = rotation * Float3.UnitZ;
        Float3 flatForward = Float3.Normalize(new Float3(forward.X, 0f, forward.Z));
        Float3 flatRight = Float3.Cross(Float3.UnitY, flatForward);
        Lean = MathF.Asin(Math.Clamp(Float3.Dot(up, flatRight), -1f, 1f)) * 180f / MathF.PI;

        // A balance torque swings the bike's up toward the lean asked for, critically damped about the roll axis.
        float target = _targetLean * MathF.PI / 180f;
        Float3 wantedUp = Float3.UnitY * MathF.Cos(target) + flatRight * MathF.Sin(target);
        Float3 roll = Float3.Cross(up, wantedUp);
        Float3 rollRate = forward * Float3.Dot(_body.AngularVelocity, forward);
        _body.AddTorque(roll * LeanResponse * LeanResponse - rollRate * 2f * LeanResponse, ForceMode.Acceleration);

        if (!Front.IsGrounded && !Rear.IsGrounded)
        {
            // In the air the pitch and yaw are calmed too, so jumps land on both wheels.
            _body.AngularVelocity *= 1f - MathF.Min(1f, 2f * Time.FixedDeltaTime);
            Float3 pitch = Float3.Cross(forward, flatForward);
            _body.AddTorque(pitch * 10f, ForceMode.Acceleration);
        }
    }
}
