// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Gadgets for getting around and for fun:
//   Thruster      a rocket: the trigger throttles a push the way the hand points, which carries the player along.
//   AeroSurface   a flat plate that only pushes back when moved against its face, for wings.
//   ZipLine, ZipHook  a cable to slide down, and the hook on an item that rides it.
//   Flashlight    the trigger switches a spot light.
//   Balloons      lift whoever holds them, and float off when let go.
//   Bomb          goes off a moment after being thrown, shoving everything around it.
//   PushButton    a button that sinks in when pushed and springs back.
//   Lever         a handle to pull, whose angle sets a value, like the time of day.
//

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace VRShowcase;

/// <summary>Who holds an item and how far they pull the trigger, the most pulled hand first.</summary>
internal static class Holding
{
    public static PhysicsHand? Hand(Grabbable? item) => item.IsValid() && item.Holders.Count > 0 ? item.Holders[0] : null;

    public static float Trigger(Grabbable? item, out PhysicsHand? hand)
    {
        hand = null;
        float most = 0f;
        if (item.IsNotValid()) return 0f;
        foreach (PhysicsHand holder in item.Holders)
        {
            float value = holder.TriggerValue;
            if (value <= most && hand != null) continue;
            most = value;
            hand = holder;
        }
        return most;
    }

    /// <summary>Lets a hand bear the player's whole weight while it holds this item, as it does climbing.</summary>
    public static void Hang(Grabbable item, ref (HandDrive? Carry, HandDrive? Shared)? saved)
    {
        PhysicsHand? hand = Hand(item);
        if (hand == null || saved.HasValue) return;
        saved = (item.CarryDrive, item.SharedDrive);
        item.CarryDrive = hand.Hang;
        item.SharedDrive = hand.Hang;
    }

    public static void StopHanging(Grabbable item, ref (HandDrive? Carry, HandDrive? Shared)? saved)
    {
        if (!saved.HasValue) return;
        (item.CarryDrive, item.SharedDrive) = saved.Value;
        saved = null;
    }
}

/// <summary>A rocket to hold. The trigger throttles a push the way the hand points, strong enough to lift the player.</summary>
public sealed class Thruster : Component
{
    public float Thrust = 1100f;
    public Transform? Flame;

    private Grabbable _item = null!;
    private float _throttle;

    public override void OnEnable() => _item = GetComponent<Grabbable>()!;

    public override void FixedUpdate()
    {
        float trigger = Holding.Trigger(_item, out PhysicsHand? hand);
        _throttle = trigger > 0.1f ? trigger : 0f;
        if (hand == null || _throttle <= 0f) return;

        hand.Body.Push(hand.AimDirection * Thrust * _throttle * Time.FixedDeltaTime);
        hand.Body.MarkCarried();
        XRInput.Vibrate(hand.Hand, 0.15f + 0.35f * _throttle, Time.FixedDeltaTime * 2f);
    }

    public override void Update()
    {
        if (Flame == null) return;
        float flicker = 0.85f + 0.15f * MathF.Sin(Time.TimeSinceStartup * 60f);
        Flame.LocalScale = _throttle > 0f ? new Float3(1f, 1f, _throttle * flicker) : Float3.Zero;
    }
}

/// <summary>
/// A flat plate that only pushes back when moved against its face, hardest head on and not at all edge on, so it can
/// be swept down to push off the air and lifted back up for free. Held, it pushes the player. Tilted, a fall turns
/// into a glide.
/// </summary>
public sealed class AeroSurface : Component
{
    /// <summary>The middle of the plate and the way its working face looks, in the body's space.</summary>
    public Float3 Center;
    public Float3 Normal = Float3.UnitY;
    public float Area = 0.5f;

    /// <summary>The push per square metre for each metre per second squared against the face.</summary>
    public float Strength = 160f;

    /// <summary>A light drag in every direction, which keeps a glide from speeding up forever.</summary>
    public float Drag = 0.3f;
    public float MaxForce = 2500f;

    private Grabbable? _item;
    private Rigidbody3D _body = null!;

    public override void OnEnable()
    {
        _item = GetComponent<Grabbable>();
        _body = GetComponent<Rigidbody3D>()!;
    }

    public override void FixedUpdate()
    {
        float dt = Time.FixedDeltaTime;
        Float3 at = _body.Position + _body.Rotation * Center;
        Float3 normal = Float3.Normalize(_body.Rotation * Normal);
        Float3 velocity = _body.GetPointVelocity(at);

        float against = -Float3.Dot(velocity, normal);
        Float3 force = against > 0f ? normal * (Strength * Area * against * against) : Float3.Zero;
        force -= velocity * (Float3.Length(velocity) * Drag);
        float size = Float3.Length(force);
        if (size > MaxForce) force *= MaxForce / size;

        PhysicsHand? hand = Holding.Hand(_item);
        if (hand != null)
        {
            hand.Body.Push(force * dt);
            if (!hand.Body.IsGrounded) hand.Body.MarkCarried();
            if (against > 1.5f) XRInput.Vibrate(hand.Hand, MathF.Min(0.4f, against * 0.08f), dt * 2f);
        }
        else
        {
            _body.LinearVelocity += force * (dt / _body.Mass);
        }
    }
}

/// <summary>A cable from a high end to a low one. Anything with a <see cref="ZipHook"/> touching it hooks on and slides down.</summary>
public sealed class ZipLine : Component
{
    public static readonly List<ZipLine> All = new();

    public Float3 Top, Bottom;

    /// <summary>Air and pulley drag, which caps the speed down the cable, and the harder braking over its last stretch.</summary>
    public float Drag = 2.5f;
    public float BrakeLength = 4f;
    public float BrakeSpeed = 3f;

    public Float3 Direction => Float3.Normalize(Bottom - Top);
    public float Length => Float3.Distance(Top, Bottom);

    public override void OnEnable() => All.Add(this);
    public override void OnDisable() => All.Remove(this);

    /// <summary>How far down the cable the point nearest <paramref name="point"/> is, and how far away it is.</summary>
    public float DistanceTo(Float3 point, out float along)
    {
        along = Maths.Clamp(Float3.Dot(point - Top, Direction), 0f, Length);
        return Float3.Distance(point, Top + Direction * along);
    }
}

/// <summary>
/// The hook on an item that rides a <see cref="ZipLine"/>. Held against a cable it hooks on, kept on the cable but
/// free to turn on it, and the hand holding it bears the player's weight. It drops off near the bottom.
/// </summary>
public sealed class ZipHook : Component
{
    public Float3 Hook;
    public float CatchRadius = 0.1f;

    private const float EndMargin = 1f;
    private const float Cooldown = 1f;

    private Grabbable _item = null!;
    private ZipLine? _line;
    private GameObject? _joint;
    private float _sinceOff = Cooldown;
    private (HandDrive? Carry, HandDrive? Shared)? _saved;

    public bool IsRiding => _line.IsValid();

    public override void OnEnable() => _item = GetComponent<Grabbable>()!;
    public override void OnDisable() => Unhook();

    public override void FixedUpdate()
    {
        float dt = Time.FixedDeltaTime;
        _sinceOff += dt;
        Rigidbody3D body = _item.Body;
        Float3 hook = body.Position + body.Rotation * Hook;

        if (_line.IsNotValid())
        {
            if (!_item.IsHeld || _sinceOff < Cooldown) return;
            foreach (ZipLine line in ZipLine.All)
                if (line.GameObject.Scene == GameObject.Scene && line.DistanceTo(hook, out float at) < CatchRadius && at < line.Length - EndMargin)
                {
                    HookOn(line, at);
                    break;
                }
            return;
        }

        _line.DistanceTo(hook, out float along);
        if (along > _line.Length - EndMargin)
        {
            Unhook();
            return;
        }

        // Drag grows with the square of the speed down the cable, and the last stretch brakes to a gentle arrival.
        Float3 axis = _line.Direction;
        float speed = Float3.Dot(body.LinearVelocity, axis);
        float drag = _line.Drag * speed * MathF.Abs(speed);
        if (along > _line.Length - EndMargin - _line.BrakeLength && speed > _line.BrakeSpeed) drag += 250f * (speed - _line.BrakeSpeed);

        // Standing, the arm gives so the player can walk off the edge. Once off, the hand bears their weight.
        PhysicsHand? hand = Holding.Hand(_item);
        if (hand != null)
        {
            if (hand.Body.IsGrounded) Holding.StopHanging(_item, ref _saved);
            else Holding.Hang(_item, ref _saved);
            hand.Body.Push(-axis * drag * dt);
            hand.Body.MarkCarried();
            XRInput.Vibrate(hand.Hand, MathF.Min(0.3f, MathF.Abs(speed) * 0.03f), dt * 2f);
        }
        else
        {
            Holding.StopHanging(_item, ref _saved);
            body.LinearVelocity -= axis * (drag * dt / body.Mass);
        }
    }

    private void HookOn(ZipLine line, float along)
    {
        _line = line;
        _joint = new GameObject("Zip Hook");
        _joint.Enabled = false;
        _joint.SetParent(line.GameObject);
        _joint.Transform.LocalPosition = Float3.Zero;
        _joint.Transform.LocalRotation = Quaternion.Identity;

        var slide = _joint.AddComponent<PointOnLineConstraint>();
        slide.LineAxis = line.Direction;
        slide.Anchor1 = Float3.Zero;
        slide.Anchor2 = Hook;
        slide.MinDistance = 0f;
        slide.MaxDistance = line.Length;
        slide.BiasFactor = 0.2f;
        slide.ConnectedBody = _item.Body;
        _joint.Enabled = true;

        foreach (PhysicsHand hand in _item.Holders)
            XRInput.Vibrate(hand.Hand, 0.6f, 0.06f);
    }

    private void Unhook()
    {
        if (_joint.IsValid())
        {
            _joint.Enabled = false;
            _joint.Destroy();
        }
        _joint = null;
        if (_line.IsValid()) _sinceOff = 0f;
        _line = null;
        if (_item.IsValid()) Holding.StopHanging(_item, ref _saved);
    }
}

/// <summary>A torch whose trigger switches its spot light on and off. The beam follows the way the hand points.</summary>
public sealed class Flashlight : Component
{
    public SpotLight Lamp = null!;

    private Grabbable _item = null!;
    private bool _wasPulled;

    public override void OnEnable() => _item = GetComponent<Grabbable>()!;

    public override void Update()
    {
        float trigger = Holding.Trigger(_item, out PhysicsHand? hand);
        bool pulled = trigger > (_wasPulled ? 0.6f : 0.7f);
        if (pulled && !_wasPulled)
        {
            Lamp.Enabled = !Lamp.Enabled;
            XRInput.Vibrate(hand!.Hand, 0.3f, 0.02f);
        }
        _wasPulled = pulled;
        if (hand != null) Lamp.Transform.LocalRotation = Quaternion.Inverse(hand.Tilt);
    }
}

/// <summary>
/// A bunch of balloons. Held, they pull the player up hard enough that two bunches float. Let go, they drift up and
/// away, and come back to where they started once out of sight, or sooner with <see cref="Respawn"/>.
/// </summary>
public sealed class Balloons : Component
{
    public float Lift = 420f;

    /// <summary>How much the balloons hold back rising and falling, which keeps a float gentle.</summary>
    public float Damping = 50f;

    private Grabbable _item = null!;
    private Float3 _home;
    private bool _loose;

    public override void OnEnable()
    {
        _item = GetComponent<Grabbable>()!;
        _home = Transform.Position;
    }

    public override void FixedUpdate()
    {
        float dt = Time.FixedDeltaTime;
        Rigidbody3D body = _item.Body;
        PhysicsHand? hand = Holding.Hand(_item);
        if (hand != null)
        {
            _loose = true;
            float rising = hand.Body.Torso.LinearVelocity.Y;
            hand.Body.Push(new Float3(0f, Lift - Damping * rising, 0f) * dt);
            return;
        }

        if (!_loose)
        {
            body.LinearVelocity = Float3.Zero;
            body.AngularVelocity = Float3.Zero;
            return;
        }

        Float3 v = body.LinearVelocity;
        body.LinearVelocity = new Float3(v.X * 0.99f, MathF.Min(v.Y + 20f * dt, 1.5f), v.Z * 0.99f);
        if (body.Position.Y > _home.Y + 40f) Respawn();
    }

    /// <summary>Brings the balloons back to where they started, unless someone is holding them.</summary>
    public void Respawn()
    {
        if (_item.IsHeld) return;
        Rigidbody3D body = _item.Body;
        body.MovePosition(_home);
        body.MoveRotation(Quaternion.Identity);
        body.LinearVelocity = Float3.Zero;
        body.AngularVelocity = Float3.Zero;
        _loose = false;
    }
}

/// <summary>
/// A bomb. Thrown, it goes off when it hits something or a few seconds later, shoving every loose thing and the
/// player away from it, and comes back where it started a moment after.
/// </summary>
public sealed class Bomb : Component
{
    public VRBody Player = null!;
    public float Radius = 5f;
    public float Impulse = 60f;
    public float PlayerImpulse = 700f;
    public MeshRenderer Visual = null!;
    public Transform Blast = null!;

    private const float ArmSpeed = 2f, Fuse = 3f, Respawn = 3f;

    private Grabbable _item = null!;
    private Float3 _home;
    private bool _armed, _gone;
    private float _clock, _blastTime = 10f;
    private readonly List<ShapeCastHit> _hits = new();

    public override void OnEnable()
    {
        _item = GetComponent<Grabbable>()!;
        _home = Transform.Position;
        PhysicsHand.LetGo += Thrown;
    }

    public override void OnDisable() => PhysicsHand.LetGo -= Thrown;

    private void Thrown(PhysicsHand hand, Grabbable item)
    {
        if (item != _item || Float3.Length(item.Body.LinearVelocity) < ArmSpeed) return;
        _armed = true;
        _clock = 0f;
    }

    public override void FixedUpdate()
    {
        _clock += Time.FixedDeltaTime;
        if (_gone)
        {
            if (_clock > Respawn) Return();
            return;
        }
        if (!_armed || _item.IsHeld) return;
        if ((_item.HitSomething && _clock > 0.1f) || _clock > Fuse) Explode();
    }

    private void Explode()
    {
        Rigidbody3D body = _item.Body;
        Float3 center = body.Position;
        GameObject.Scene.Physics.OverlapSphere(center, Radius, _hits, QueryFilter.Default.Ignoring(body));
        var pushed = new HashSet<Rigidbody3D>();
        foreach (ShapeCastHit hit in _hits)
        {
            Rigidbody3D? other = hit.Rigidbody;
            if (other.IsNotValid() || other.MotionType != Jitter2.Dynamics.MotionType.Dynamic || Player.Parts.Contains(other) || !pushed.Add(other)) continue;
            Float3 away = other.Position - center;
            float distance = MathF.Max(Float3.Length(away), 0.2f);
            float strength = Impulse * (1f - MathF.Min(distance / Radius, 1f));
            other.LinearVelocity += away / distance * MathF.Min(strength / other.Mass, 25f);
        }

        Float3 toPlayer = Player.Torso.Position - center;
        float playerDistance = MathF.Max(Float3.Length(toPlayer), 0.3f);
        if (playerDistance < Radius)
            Player.Push((toPlayer / playerDistance + new Float3(0f, 0.5f, 0f)) * PlayerImpulse * (1f - playerDistance / Radius));

        _gone = true;
        _armed = false;
        _clock = 0f;
        _blastTime = 0f;
        Blast.Position = center;
        Visual.Enabled = false;
        body.MovePosition(_home + new Float3(0f, -50f, 0f));
        body.LinearVelocity = Float3.Zero;
    }

    private void Return()
    {
        _gone = false;
        Visual.Enabled = true;
        Rigidbody3D body = _item.Body;
        body.MovePosition(_home);
        body.LinearVelocity = Float3.Zero;
        body.AngularVelocity = Float3.Zero;
    }

    public override void Update()
    {
        _blastTime += Time.DeltaTime;
        float t = _blastTime / 0.3f;
        Blast.LocalScale = t < 1f ? new Float3(Radius * 2f * MathF.Sqrt(t)) : Float3.Zero;
    }
}

/// <summary>A button that sinks into its housing when pushed and springs back out. Pushed most of the way in, it presses.</summary>
public sealed class PushButton : Component
{
    public PrismaticJoint Slide = null!;
    public float Travel = 0.015f;

    public event Action? Pressed;
    public bool IsDown { get; private set; }

    public override void FixedUpdate()
    {
        float depth = -Slide.CurrentDistance;
        Slide.MotorTargetVelocity = depth * 25f;

        bool down = depth > Travel * (IsDown ? 0.4f : 0.7f);
        if (down == IsDown) return;
        IsDown = down;
        if (down) Pressed?.Invoke();
    }
}

/// <summary>A lever on a pivot, turned by gripping its knob and moving the hand. Its angle sets <see cref="Value"/>, 0 to 1.</summary>
public sealed class Lever : HandTarget
{
    public Transform Arm = null!;
    public float Length = 0.35f;
    public float Range = 60f;

    public float Value { get; private set; }
    public event Action<float>? Changed;

    private float _angle;

    public void SetValue(float value)
    {
        Value = Maths.Clamp(value, 0f, 1f);
        _angle = (Value * 2f - 1f) * Range;
        Arm.LocalRotation = Quaternion.AxisAngle(Float3.UnitX, _angle * Maths.Deg2Rad);
        Changed?.Invoke(Value);
    }

    private Float3 Knob => Transform.Position + Transform.Rotation * (Quaternion.AxisAngle(Float3.UnitX, _angle * Maths.Deg2Rad) * new Float3(0f, Length, 0f));

    public override float DistanceTo(PhysicsHand hand, Float3 grip)
    {
        float distance = Float3.Distance(grip, Knob);
        return distance < 0.12f ? distance : float.MaxValue;
    }

    public override void Update()
    {
        if (User.IsNotValid()) return;
        Float3 local = Quaternion.Inverse(Transform.Rotation) * (User.GripPoint - Transform.Position);
        float angle = Maths.Clamp(MathF.Atan2(local.Z, local.Y) * Maths.Rad2Deg, -Range, Range);
        if (MathF.Abs(angle - _angle) < 0.5f) return;
        SetValue((angle / Range + 1f) * 0.5f);
        if (MathF.Abs(angle) >= Range) XRInput.Vibrate(User.Hand, 0.2f, 0.02f);
    }
}
