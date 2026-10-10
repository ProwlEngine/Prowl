// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Controller Showcase
//
// One open world to walk around, full of everything a character controller has to cope with. Pieces
// are grouped by theme into neighbourhoods around the spawn, every object at the spot written out in
// SceneLayout.cs:
//   Stairs and slopes    step heights either side of the step limit, ramps over stairs, a mesh staircase,
//                        a fan of slopes, a crest, a trough, a valley too steep to stand in, spiral towers
//   Ceilings and gaps    slanted ceilings, a pitched roof, a crawl tunnel, doorways and bars
//   Traps                corners, wedges, pockets and a funnel that press from several sides
//   Curves               quarter pipes, arched tunnels, domes and logs
//   Rough ground         a jagged field, rolling bumps and a dense stone field
//   Moving things        platforms, lifts, a crush lift, spinning discs and sweepers, log rollers,
//                        push blocks, a spinning column and a seesaw
//   Play                 crates to shove and climb
//   Edges and jumps      hop blocks, gaps, ledges and beams
//
// Controls:
//   WASD        Run, relative to the camera
//   Space       Jump, hold for a higher jump
//   C           Crouch
//   Right Mouse Orbit the camera, the wheel zooms
//   Left Mouse  Drag any body around
//   F1          Hide the HUD
//


using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace ControllerShowcase;

internal class Program
{
    static void Main(string[] args)
    {
        new ControllerShowcaseGame().Run("Controller Showcase", 1600, 900);
    }
}

public sealed class ControllerShowcaseGame : StationGame
{
    private static readonly Color Orange = new(1f, 0.32f, 0.04f, 1f);

    private readonly Float3 _spawn = new(0f, 0.05f, 0f);
    private ChaseCamera _chase = null!;
    private CharacterController _character = null!;
    private CharacterInput _input = null!;

    private readonly List<Landmark> _landmarks = new();

    protected override string MoveKeys => "WASD  run    Space  jump    C  crouch    Right Mouse  orbit";

    public override string Stats
    {
        get
        {
            string ground = _character.IsGrounded ? $"grounded on a {_character.GroundSlopeAngle:0} degree surface" : "in the air";
            Float3 v = _character.Velocity;
            float speed = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
            string state = $"{ground}    {speed:0.0} m/s    height {_character.Transform.Position.Y:0.00} m    touching {_character.Collisions}    {(_input.Crouched ? "crouched" : "standing")}";

            Landmark? near = Nearest();
            return near == null ? state : $"{near.Name}: {near.About}\n{state}";
        }
    }

    private Landmark? Nearest()
    {
        Float3 at = _character.Transform.Position;
        Landmark? best = null;
        float bestDistance = float.MaxValue;
        foreach (Landmark landmark in _landmarks)
        {
            float dx = landmark.Position.X - at.X, dz = landmark.Position.Z - at.Z;
            float distance = MathF.Sqrt(dx * dx + dz * dz) - landmark.Radius;
            if (distance < bestDistance) { bestDistance = distance; best = landmark; }
        }
        return bestDistance < 6f ? best : null;
    }

    protected override void Build()
    {
        AddStation("Controller playground", "One open world full of everything a character controller has to cope with: stairs and slopes, low ceilings and tight gaps, traps that press from several sides, curves, rough ground, moving platforms, jump pads, teleporters, and gravity that pulls sideways, upward or toward a little planet. Walk up to anything and the line below says what it is testing.", Float3.Zero, new Float3(0f, 5f, -12f), 1f);

        var sun = new GameObject("Sun");
        DirectionalLight light = sun.AddComponent<DirectionalLight>();
        light.ShadowDistance = 70f;
        light.Intensity = 0.6f;
        sun.Transform.LocalEulerAngles = new Float3(50f, 30f, 0f);
        Add(sun);

        _chase = CameraObject.AddComponent<ChaseCamera>();
        CameraObject.GetComponent<PhysicsGrabber>()!.Enabled = false;
        _chase.Distance = 7f;

        _landmarks.AddRange(SceneLayout.Spawn(SampleScene));
        BuildSpawnPad();

        const float Size = 240f;
        Add(Block("Ground", new Float3(Size, 1f, Size), Floor(Size, Size), new Float3(0f, -0.5f, 0f)));

        BuildPlayer(_spawn);
        _chase.Target = _character.Transform;
    }

    protected override void OnStationChanged(int index)
    {
        CameraObject.GetComponent<FlyCamera>()!.Enabled = false;
        _input.Stand();
        _character.Teleport(_spawn);
        _chase.Yaw = 0f;
    }

    protected override void Tick()
    {
        // Anything that falls out of the world comes back to the spawn.
        if (_character.Transform.Position.Y < -20f)
        {
            _input.Stand();
            _character.Teleport(_spawn);
        }
    }

    private void BuildSpawnPad()
    {
        Mesh pad = Mesh.CreateCylinder(3f, 0.06f, 40);
        Add(Model("Spawn Pad", pad, Lit(new Color(0.05f, 0.05f, 0.06f, 1f)).Emissive(Orange, 0.08f), new Float3(0f, 0.03f, 0f)));
    }

    // ----------------------------------------------------------------
    //  Player
    // ----------------------------------------------------------------

    private void BuildPlayer(Float3 position)
    {
        var player = new GameObject("Player");
        player.Transform.Position = position;

        var model = new GameObject("Model");
        model.SetParent(player);
        model.Transform.LocalPosition = Float3.Zero;
        void Part(Mesh mesh, Material material, Float3 local)
        {
            GameObject part = Model("Part", mesh, material, Float3.Zero);
            part.SetParent(model);
            part.Transform.LocalPosition = local;
            part.Transform.LocalRotation = Quaternion.Identity;
        }
        Material suit = Lit(Orange, 0f, 0.45f);
        Part(Mesh.CreateCapsule(0.35f, 1.25f), suit, new Float3(0f, 0.68f, 0f));
        Part(Mesh.CreateSphere(0.27f, 12, 18), suit, new Float3(0f, 1.52f, 0f));
        Part(Mesh.CreateCube(new Float3(0.38f, 0.14f, 0.12f)), Lit(new Color(0.01f, 0.02f, 0.04f, 1f), 0.8f, 0.1f).Emissive(new Color(0.2f, 0.7f, 1f, 1f), 1.5f), new Float3(0f, 1.55f, 0.22f));
        Part(Mesh.CreateCube(new Float3(0.45f, 0.55f, 0.22f)), Lit(new Color(0.05f, 0.05f, 0.06f, 1f), 0f, 0.6f), new Float3(0f, 0.95f, -0.3f));

        _character = player.AddComponent<CharacterController>();
        _character.Radius = 0.38f;
        _character.Height = 1.8f;
        _character.StepSize = 0.3f;
        _input = player.AddComponent<CharacterInput>();
        _input.Model = model.Transform;
        _input.View = _chase;
        Add(player);
    }

    public override void DrawControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Movement");
        Slider(paper, font, "Run speed", _input.Speed, 1f, 12f, v => _input.Speed = v, "0.0");
        Slider(paper, font, "Jump height", _input.JumpHeight, 0.2f, 3f, v => _input.JumpHeight = v, "0.0");
        Slider(paper, font, "Gravity", _input.Gravity, 5f, 50f, v => _input.Gravity = v, "0");
        Slider(paper, font, "Air control", _input.AirControl, 0f, 1f, v => _input.AirControl = v);

        Header(paper, font, "Controller", 1);
        Slider(paper, font, "Steepest walkable slope", _character.MaxSlopeAngle, 10f, 80f, v => _character.MaxSlopeAngle = v, "0");
        Slider(paper, font, "Step height", _character.StepSize, 0f, 0.6f, v => _character.StepSize = v);
        Slider(paper, font, "Snap down distance", _character.SnapDownDistance, 0f, 1f, v => _character.SnapDownDistance = v);
        Button(paper, font, "Back to the spawn", () => { _input.Stand(); _character.Teleport(_spawn); });
    }
}

/// <summary>Drives a kinematic body back and forth between two points through its velocity, pausing at each end.</summary>
public sealed class PingPongMover : Component
{
    public Float3 From, To;
    public float Speed = 3f;
    public float Pause = 0.6f;

    private Rigidbody3D _body = null!;
    private bool _forward = true;
    private float _wait;

    public override void OnEnable() => _body = GetComponent<Rigidbody3D>()!;

    public override void FixedUpdate()
    {
        float dt = Time.FixedDeltaTime;
        if (_wait > 0f)
        {
            _wait -= dt;
            _body.LinearVelocity = Float3.Zero;
            return;
        }

        Float3 target = _forward ? To : From;
        Float3 offset = target - _body.Position;
        float distance = Float3.Length(offset);
        if (distance <= Speed * dt)
        {
            _body.LinearVelocity = offset / dt;
            _forward = !_forward;
            _wait = Pause;
            return;
        }

        _body.LinearVelocity = offset / distance * Speed;
    }
}

/// <summary>Spins a kinematic body at a steady rate about its own up axis.</summary>
public sealed class Spinner : Component
{
    public float DegreesPerSecond = 45f;

    private Rigidbody3D _body = null!;

    public override void OnEnable() => _body = GetComponent<Rigidbody3D>()!;

    public override void FixedUpdate()
        => _body.AngularVelocity = _body.Rotation * Float3.UnitY * (DegreesPerSecond * MathF.PI / 180f);
}

public sealed class CharacterInput : Component
{
    public float Speed = 6f;
    public float JumpHeight = 1.4f;
    public float AirControl = 0.35f;

    /// <summary>The pull of gravity outside every <see cref="GravityZone"/>.</summary>
    public float Gravity = 24f;
    public Transform Model = null!;
    public ChaseCamera View = null!;

    private const float Acceleration = 50f;
    private const float CoyoteTime = 0.12f;
    private const float JumpBuffer = 0.12f;
    private const float StandingHeight = 1.8f;
    private const float CrouchHeight = 1.1f;

    private CharacterController _controller = null!;
    private Float3 _velocity;
    private float _sinceGrounded;
    private float _sinceJumpPressed = 1f;
    private float _sinceLaunched = 1f;
    private float _facing;
    private bool _wantsCrouch;
    private Quaternion _body = Quaternion.Identity;
    private Teleporter? _arrivedOn;

    public bool Crouched { get; private set; }

    public override void OnEnable() => _controller = GetComponent<CharacterController>()!;

    /// <summary>Stands back up immediately and forgets any motion, for a teleport.</summary>
    public void Stand()
    {
        _wantsCrouch = false;
        _velocity = Float3.Zero;
        _arrivedOn = null;
        if (_controller.IsNotValid()) return;
        _controller.Up = Float3.UnitY;
        if (Crouched && _controller.TrySetHeight(StandingHeight)) SetCrouched(false);
    }

    private void SetCrouched(bool crouched)
    {
        Crouched = crouched;
        Model.LocalScale = new Float3(1f, crouched ? CrouchHeight / StandingHeight : 1f, 1f);
    }

    public override void Update()
    {
        float dt = Time.DeltaTime;
        if (dt <= 0f) return;

        if (Input.GetKeyDown(KeyCode.C)) _wantsCrouch = !_wantsCrouch;
        if (_wantsCrouch && !Crouched && _controller.TrySetHeight(CrouchHeight)) SetCrouched(true);
        else if (!_wantsCrouch && Crouched && _controller.TrySetHeight(StandingHeight)) SetCrouched(false);

        // Gravity comes from whatever zone the middle of the body is in, and up is straight away from it.
        Float3 pull = GravityZone.At(_controller.Center, new Float3(0f, -Gravity, 0f));
        float gravity = Float3.Length(pull);
        if (gravity > 1e-3f) _controller.Up = -pull / gravity;
        Float3 up = _controller.Up;
        View.Up = up;

        Float2 input = Float2.Zero;
        if (Input.GetKey(KeyCode.W)) input.Y += 1f;
        if (Input.GetKey(KeyCode.S)) input.Y -= 1f;
        if (Input.GetKey(KeyCode.D)) input.X += 1f;
        if (Input.GetKey(KeyCode.A)) input.X -= 1f;
        if (Float2.LengthSquared(input) > 1f) input = Float2.Normalize(input);

        Float3 forward = Across(View.Heading, up);
        if (Float3.LengthSquared(forward) < 1e-4f) forward = Across(_body * Float3.UnitZ, up);
        forward = Float3.Normalize(forward);
        Float3 right = Float3.Cross(up, forward);
        Float3 wish = (forward * input.Y + right * input.X) * Speed * (Crouched ? 0.45f : 1f);

        bool grounded = _controller.IsGrounded && _sinceLaunched > 0.2f;
        _sinceGrounded = grounded ? 0f : _sinceGrounded + dt;
        _sinceJumpPressed = Input.GetKeyDown(KeyCode.Space) ? 0f : _sinceJumpPressed + dt;
        _sinceLaunched += dt;

        float rising = Float3.Dot(_velocity, up);
        Float3 across = _velocity - up * rising;

        float control = grounded ? 1f : AirControl;
        Float3 change = wish - across;
        float maxChange = Acceleration * control * dt;
        if (Float3.Length(change) > maxChange) change = Float3.Normalize(change) * maxChange;
        across += change;

        if (grounded && rising <= 0f) rising = -2f;
        else rising -= gravity * dt;

        if (!Crouched && _sinceJumpPressed < JumpBuffer && _sinceGrounded < CoyoteTime && rising <= 0f)
        {
            Float3 carried = _controller.GroundVelocity;
            across += Across(carried, up);
            rising = MathF.Sqrt(2f * gravity * JumpHeight) + MathF.Max(Float3.Dot(carried, up), 0f);
            _sinceJumpPressed = 1f;
            _sinceGrounded = 1f;
        }

        // Letting go of jump early cuts the climb short, for small hops. A pad launch always flies its full arc.
        if (!Input.GetKey(KeyCode.Space) && rising > 0f && _sinceLaunched > 1f) rising -= gravity * dt;

        _velocity = across + up * rising;
        UsePadsAndGates();

        CharacterController.CollisionFlags flags = _controller.Move(_velocity * dt);
        Float3 achieved = _controller.Velocity;
        rising = Float3.Dot(_velocity, up);
        across = _velocity - up * rising;
        if ((flags & CharacterController.CollisionFlags.Above) != 0 && rising > 0f) rising = 0f;
        if ((flags & CharacterController.CollisionFlags.Sides) != 0) across = Across(achieved, up);

        // In the air, a fall something held up stops building speed, and a run up a steep slope keeps the upward speed it gained.
        if (!_controller.IsGrounded) rising = MathF.Max(rising, Float3.Dot(achieved, up));
        _velocity = across + up * rising;

        TurnBody(up, wish, input, dt);
    }

    private static Float3 Across(Float3 v, Float3 up) => v - up * Float3.Dot(v, up);

    /// <summary>Turns the body to stand along up, smoothly, and to face the way it walks.</summary>
    private void TurnBody(Float3 up, Float3 wish, Float2 input, float dt)
    {
        Quaternion toUp = Quaternion.FromToRotation(_body * Float3.UnitY, up);
        _body = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, toUp, MathF.Min(1f, dt * 12f)) * _body);

        _facing += _controller.GroundYawDelta;
        if (Float2.LengthSquared(input) > 0.01f)
        {
            Float3 local = Quaternion.Inverse(_body) * wish;
            float target = MathF.Atan2(local.X, local.Z) * 180f / MathF.PI;
            float delta = ((target - _facing) % 360f + 540f) % 360f - 180f;
            _facing += delta * MathF.Min(1f, dt * 12f);
        }
        Model.Rotation = _body * Quaternion.FromEuler(new Float3(0f, _facing, 0f));
    }

    /// <summary>Launches off any jump pad it stands on, and steps through any teleporter it walks onto.</summary>
    private void UsePadsAndGates()
    {
        Float3 feet = Transform.Position;

        if (_sinceLaunched > 0.3f)
        {
            foreach (JumpPad pad in JumpPad.All)
            {
                if (!pad.Holds(feet)) continue;
                _velocity = pad.Launch;
                _sinceLaunched = 0f;
                _sinceGrounded = 1f;
                break;
            }
        }

        if (_arrivedOn != null && !_arrivedOn.Holds(feet)) _arrivedOn = null;
        foreach (Teleporter gate in Teleporter.All)
        {
            if (gate == _arrivedOn || gate.Exit == null || !gate.Holds(feet)) continue;
            _controller.Teleport(gate.Exit.Transform.Position + gate.Exit.Transform.Up * 0.05f);
            _arrivedOn = gate.Exit;
            break;
        }
    }
}

/// <summary>
/// A region with its own gravity: a fixed pull across a box, or a pull toward the middle of a sphere
/// for a planet. Where zones overlap the higher <see cref="Priority"/> wins.
/// </summary>
public sealed class GravityZone : Component
{
    public static readonly List<GravityZone> All = new();

    /// <summary>True pulls toward the zone's middle across a sphere of <see cref="Radius"/>, false pulls along <see cref="Direction"/> across a box of <see cref="Size"/>.</summary>
    public bool TowardCenter;
    public float Radius = 10f;
    public Float3 Size = new(10f, 10f, 10f);

    /// <summary>The way the box pulls, in the zone's own space.</summary>
    public Float3 Direction = new(0f, -1f, 0f);
    public float Strength = 24f;
    public int Priority;

    public override void OnEnable() => All.Add(this);

    public override void OnDisable() => All.Remove(this);

    public bool Contains(Float3 point)
    {
        if (TowardCenter) return Float3.Length(point - Transform.Position) <= Radius;

        Float3 local = Quaternion.Inverse(Transform.Rotation) * (point - Transform.Position);
        return MathF.Abs(local.X) <= Size.X * 0.5f && MathF.Abs(local.Y) <= Size.Y * 0.5f && MathF.Abs(local.Z) <= Size.Z * 0.5f;
    }

    public Float3 Pull(Float3 point)
    {
        if (!TowardCenter) return Transform.Rotation * Float3.Normalize(Direction) * Strength;

        Float3 toCenter = Transform.Position - point;
        float distance = Float3.Length(toCenter);
        return distance > 1e-3f ? toCenter / distance * Strength : Float3.Zero;
    }

    /// <summary>The gravity at a point: the highest priority zone holding it, or <paramref name="outside"/>.</summary>
    public static Float3 At(Float3 point, Float3 outside)
    {
        GravityZone? best = null;
        foreach (GravityZone zone in All)
            if (zone.Contains(point) && (best == null || zone.Priority > best.Priority)) best = zone;
        return best == null ? outside : best.Pull(point);
    }
}

/// <summary>A pad that throws whatever stands on it at <see cref="Launch"/>.</summary>
public sealed class JumpPad : Component
{
    public static readonly List<JumpPad> All = new();

    public Float3 Launch;
    public float Radius = 1.2f;

    public override void OnEnable() => All.Add(this);

    public override void OnDisable() => All.Remove(this);

    public bool Holds(Float3 feet)
    {
        Float3 local = Quaternion.Inverse(Transform.Rotation) * (feet - Transform.Position);
        return local.X * local.X + local.Z * local.Z <= Radius * Radius && local.Y > -0.2f && local.Y < 0.5f;
    }
}

/// <summary>One end of a teleporter: stepping onto it puts the walker on <see cref="Exit"/>.</summary>
public sealed class Teleporter : Component
{
    public static readonly List<Teleporter> All = new();

    public Teleporter? Exit;
    public float Radius = 1f;

    public override void OnEnable() => All.Add(this);

    public override void OnDisable() => All.Remove(this);

    public bool Holds(Float3 feet)
    {
        Float3 local = Quaternion.Inverse(Transform.Rotation) * (feet - Transform.Position);
        return local.X * local.X + local.Z * local.Z <= Radius * Radius && local.Y > -0.3f && local.Y < 0.6f;
    }
}
