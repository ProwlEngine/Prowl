// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Melee:
//   Stabber      a blade that sinks into anything Stabbable when it hits point first and fast enough. Once in, a
//                sliding joint holds it on its own line, with a motor whose force limit acts as the friction of
//                the material, and it comes free when pulled back out past where it went in.
//   Stabbable    marks something a blade can go into, and how hard it resists.
//   MeleeWeapon  remembers how fast a weapon was moving before each hit.
//   HitFlash     flashes when struck, stabbed or shot, harder for harder hits.
//

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace VRShowcase;

/// <summary>Something a blade can sink into.</summary>
public sealed class Stabbable : MonoBehaviour
{
    /// <summary>The slowest a blade can be moving point first and still go in, in metres per second.</summary>
    public float RequiredSpeed = 1.2f;

    /// <summary>Scales how hard the material holds a blade. A melon is soft, a wooden board is not.</summary>
    public float Resistance = 1f;

    /// <summary>How deep a blade can go, at most. Zero lets it go its whole length.</summary>
    public float MaxDepth;
}

/// <summary>
/// Something that makes a <see cref="HitFlash"/> flash when it hits it fast enough. It remembers its velocity from
/// before each step, since by the time a hit is reported the collision has already slowed it.
/// </summary>
public sealed class MeleeWeapon : MonoBehaviour
{
    /// <summary>Hits slower than this, in metres per second, do not count.</summary>
    public float MinSpeed = 2f;

    private Float3 _velocity, _spin;

    public Rigidbody3D Body => GetComponent<Rigidbody3D>()!;

    public override void FixedUpdate()
    {
        _velocity = Body.LinearVelocity;
        _spin = Body.AngularVelocity;
    }

    /// <summary>How fast the point on the weapon at <paramref name="point"/> was moving just before this step's hit.</summary>
    public Float3 VelocityBeforeHit(Float3 point) => _velocity + Float3.Cross(_spin, point - Body.Position);
}

/// <summary>A blade along a weapon's local <see cref="Axis"/>, ending at <see cref="Tip"/>.</summary>
public sealed class Stabber : MonoBehaviour
{
    public Float3 Tip;
    public Float3 Axis = Float3.UnitY;
    public float Length = 0.6f;

    /// <summary>From 0 to 1. A sharper blade slides through the inside of a target more easily.</summary>
    public float Sharpness = 0.4f;

    // The outer skin of a target grips hardest, the inside less, both scaled by the target's resistance.
    private const float OuterLayer = 0.025f;
    private const float OuterFriction = 900f;
    private const float InnerFriction = 660f;
    private const float AngleTolerance = 0.5f;
    private const float TipReach = 0.07f;
    private const float UnstabDelay = 0.25f;
    private const float UnstabBack = 0.01f;

    private static readonly List<Stabber> s_stuck = new();

    private Float3 _velocity, _spin;
    private GameObject? _joint;
    private PrismaticJoint? _slide;
    private Stabbable? _target;
    private float _since;
    private float _entryDepth;

    public bool IsStuck => _target.IsValid();
    public Rigidbody3D Body => GetComponent<Rigidbody3D>()!;

    /// <summary>Whether <paramref name="item"/> has a blade stuck in something, which keeps the hand from pulling it into a grip pose.</summary>
    public static bool IsStuckIn(Grabbable item)
    {
        foreach (Stabber stabber in s_stuck)
            if (stabber.GameObject == item.GameObject) return true;
        return false;
    }

    public override void FixedUpdate()
    {
        // The velocity before this step, so a blade that hits point first can carry on as if the hit never happened.
        _velocity = Body.LinearVelocity;
        _spin = Body.AngularVelocity;

        if (!IsStuck)
        {
            if (_joint.IsValid()) Unstab();
            return;
        }

        _since += Time.FixedDeltaTime;
        // How deep the tip is now: wherever it was when the joint formed, plus how far it has slid since.
        float depth = _entryDepth + _slide!.CurrentDistance;
        float friction = depth < OuterLayer ? OuterFriction : InnerFriction * (1f - Sharpness);
        _slide.MotorMaxForce = friction * _target!.Resistance;

        if (_since > UnstabDelay && depth < -UnstabBack) Unstab();
    }

    public override void OnCollisionBegin(Collision collision)
    {
        if (IsStuck || collision.Rigidbody.IsNotValid()) return;
        Stabbable? target = collision.Rigidbody.GetComponent<Stabbable>();
        if (target.IsNotValid()) return;

        Float3 tip = Body.Position + Body.Rotation * Tip;
        if (Float3.Distance(collision.Point, tip) > TipReach) return;

        // Point first: the blade's line has to run into the surface, not glance along it.
        Float3 axis = Float3.Normalize(Body.Rotation * Axis);
        if (MathF.Abs(Float3.Dot(axis, collision.Normal)) < AngleTolerance) return;

        Float3 tipVelocity = _velocity + Float3.Cross(_spin, tip - Body.Position);
        Float3 relative = tipVelocity - collision.Rigidbody.GetPointVelocity(tip);
        if (Float3.Dot(relative, axis) < target.RequiredSpeed) return;

        Stab(target, collision.Rigidbody, DepthIn(collision.Rigidbody, tip, axis));
    }

    /// <summary>
    /// How far the tip already is inside the target. A fast blade can cross most of its length in the step that
    /// reports the hit, so the surface is found by casting back along the blade from outside it.
    /// </summary>
    private float DepthIn(Rigidbody3D target, Float3 tip, Float3 axis)
    {
        float back = Length + 0.05f;
        Float3 from = tip - axis * back;
        QueryFilter filter = QueryFilter.Default.Ignoring(Body);
        if (!GameObject.Scene.Physics.Raycast(from, axis, out RaycastHit hit, back, filter)) return 0f;
        if (hit.Collider.IsNotValid() || hit.Collider.AttachedRigidbody != target) return 0f;
        return MathF.Max(0f, back - hit.Distance);
    }

    /// <summary>Sticks the blade into <paramref name="target"/>, already <paramref name="alreadyIn"/> deep, for something that found the hit itself, like an arrow too fast to wait for the collision.</summary>
    public void StickInto(Stabbable target, float alreadyIn)
    {
        if (IsStuck) return;
        Rigidbody3D? body = target.GetComponent<Rigidbody3D>();
        if (body.IsNotValid()) return;
        _velocity = Body.LinearVelocity;
        _spin = Body.AngularVelocity;
        Stab(target, body, alreadyIn);
    }

    private void Stab(Stabbable target, Rigidbody3D targetBody, float alreadyIn)
    {
        Body.LinearVelocity = _velocity;
        Body.AngularVelocity = _spin;

        // A blade that crossed past the target's depth in one step is set back to it.
        float limit = target.MaxDepth > 0f ? MathF.Min(target.MaxDepth, Length) : Length;
        if (alreadyIn > limit)
        {
            Float3 axis = Float3.Normalize(Body.Rotation * Axis);
            Body.MovePosition(Body.Position - axis * (alreadyIn - limit));
            alreadyIn = limit;
        }

        // Built on an inactive child and switched on once, so it takes the blade's pose right now as where it went in.
        _joint = new GameObject("Stab");
        _joint.Enabled = false;
        _joint.SetParent(GameObject);
        _joint.Transform.LocalPosition = Float3.Zero;

        _slide = _joint.AddComponent<PrismaticJoint>();
        _slide.Anchor = Tip;
        _slide.Axis = Axis;
        _slide.Pinned = true;
        _slide.MinDistance = -(alreadyIn + UnstabBack * 3f);
        _slide.MaxDistance = MathF.Max(0.005f, limit - alreadyIn);
        _slide.HasMotor = true;
        _slide.MotorTargetVelocity = 0f;
        _slide.MotorMaxForce = OuterFriction * target.Resistance;
        _slide.ConnectedBody = targetBody;
        _joint.Enabled = true;

        _target = target;
        _since = 0f;
        _entryDepth = alreadyIn;
        s_stuck.Add(this);

        HitFlash? flash = target.GetComponent<HitFlash>();
        if (flash.IsValid()) flash.Flash(1f);
        Pulse(0.6f, 0.06f);
    }

    private void Unstab()
    {
        if (_joint.IsValid())
        {
            _joint.Enabled = false;
            _joint.Destroy();
        }
        _joint = null;
        _slide = null;
        _target = null;
        s_stuck.Remove(this);
        Pulse(0.25f, 0.03f);
    }

    public override void OnDisable()
    {
        if (_joint.IsValid()) Unstab();
    }

    private void Pulse(float amplitude, float seconds)
    {
        Grabbable? grabbable = GetComponent<Grabbable>();
        if (grabbable.IsNotValid()) return;
        foreach (PhysicsHand hand in grabbable.Holders)
            XRInput.Vibrate(hand.Hand, amplitude, seconds);
    }
}

/// <summary>Flashes its materials when struck by a weapon or a fist, stabbed or shot, brighter for harder hits.</summary>
public sealed class HitFlash : MonoBehaviour
{
    public Color BaseColor = new(0.6f, 0.5f, 0.35f, 1f);
    public Color FlashColor = new(1f, 0.15f, 0.05f, 1f);
    public List<Material> Materials = new();

    private float _flash;

    public override void OnCollisionBegin(Collision collision)
    {
        if (collision.Rigidbody.IsNotValid()) return;
        MeleeWeapon? weapon = collision.Rigidbody.GetComponent<MeleeWeapon>();
        if (weapon.IsNotValid()) return;

        Rigidbody3D self = GetComponent<Rigidbody3D>()!;
        float speed = Float3.Length(weapon.VelocityBeforeHit(collision.Point) - self.GetPointVelocity(collision.Point));
        if (speed > weapon.MinSpeed) Flash((speed - weapon.MinSpeed) / 6f);
    }

    public void Flash(float strength) => _flash = MathF.Max(_flash, Maths.Clamp(strength, 0.2f, 1f));

    public override void Update()
    {
        _flash = MathF.Max(0f, _flash - Time.DeltaTime * 4f);
        Color color = new(
            BaseColor.R + (FlashColor.R - BaseColor.R) * _flash,
            BaseColor.G + (FlashColor.G - BaseColor.G) * _flash,
            BaseColor.B + (FlashColor.B - BaseColor.B) * _flash,
            1f);
        foreach (Material material in Materials)
            material.SetColor("_MainColor", color);
    }
}

