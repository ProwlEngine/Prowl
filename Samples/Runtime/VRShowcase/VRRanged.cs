// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Guns and bows:
//   Gun        a held gun that fires a round from its chamber with the trigger, kicks back, cycles the next round from
//              its magazine and locks its slide back once empty. The primary button drops the magazine.
//   GunSlide   the slide or charging handle. Pulled back by a free hand it ejects what is in the chamber, and going
//              forward again it chambers a round. Grabbing a slide locked back lets it fly forward.
//   Magazine   holds rounds. MagazineWell is the rail it slides in on, latching when pushed home.
//   Bow        a held bow. The other hand grips the string, which nocks an arrow, draws it back and lets it fly.
//   Arrow      turns to face where it flies and sticks into anything pierceable it hits point first.
//   Expire     removes a spent casing or arrow after a while.
//

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace VRShowcase;

public sealed class Magazine : Component
{
    public int Capacity = 15;
    public int Rounds = 15;
}

/// <summary>
/// A gun's magazine well, running up its own +Y from the mouth under the grip to where a seated magazine's top sits.
/// A magazine that reaches the mouth lined up with it, pushed up by a hand or thrown, goes onto a rail there and
/// slides along it freely, in and out. Pushed all the way in, it latches: it is welded in and can no longer be
/// grabbed. Only <see cref="Release"/>, the gun's magazine release, unlatches it to slide back out.
/// </summary>
public sealed class MagazineWell : Component
{
    /// <summary>How far a magazine travels along the rail, from the mouth to seated.</summary>
    public float Depth = 0.09f;
    public float CatchRadius = 0.035f;
    public float MinAlignment = 0.85f;

    public Grabbable? Item { get; private set; }
    public bool Latched { get; private set; }

    private GameObject? _joint;
    private Rigidbody3D? _gun;
    private bool _pendingSeat;
    private bool _latchArmed;
    private readonly Dictionary<Grabbable, Float3> _lastTops = new();

    private Rigidbody3D Gun
    {
        get
        {
            if (_gun.IsNotValid()) _gun = GetComponentInParent<Rigidbody3D>()!;
            return _gun;
        }
    }

    /// <summary>The seated top and the rail's direction in the world, from the gun's simulated pose.</summary>
    private void Rail(out Float3 seat, out Float3 axis, out Quaternion rotation)
    {
        Rigidbody3D gun = Gun;
        Transform gunTransform = gun.Transform;
        Float3 local = gunTransform.InverseTransformPoint(Transform.Position);
        Quaternion localRotation = Quaternion.Inverse(gunTransform.Rotation) * Transform.Rotation;
        rotation = gun.Rotation * localRotation;
        seat = gun.Position + gun.Rotation * local;
        axis = rotation * Float3.UnitY;
    }

    private static Float3 Top(Grabbable magazine) => magazine.SocketPoint;

    /// <summary>How far the magazine's top is along the rail from the mouth.</summary>
    private float Inserted(Grabbable magazine)
    {
        Rail(out Float3 seat, out Float3 axis, out _);
        return Float3.Dot(Top(magazine) - (seat - axis * Depth), axis);
    }

    /// <summary>Puts a magazine in latched while the world is being built, before anything is simulated.</summary>
    public void Load(Grabbable magazine)
    {
        Item = magazine;
        Latched = true;
        _pendingSeat = true;
        Quaternion rotation = Transform.Rotation * Quaternion.Inverse(magazine.SocketRotation);
        magazine.Transform.Rotation = rotation;
        magazine.Transform.Position = Transform.Position - rotation * magazine.SocketPosition;
        magazine.Enabled = false;
    }

    public override void FixedUpdate()
    {
        if (_pendingSeat)
        {
            _pendingSeat = false;
            if (Item.IsValid())
            {
                GameObject.Scene.Physics.IgnoreCollisionBetween(Item.Body, Gun);
                Weld(Item);
            }
        }

        if (Item.IsNotValid())
        {
            Catch();
            return;
        }
        if (Latched) return;

        // Just released, the magazine has to slide out a little before pushing it home can latch it again.
        float depth = Inserted(Item);
        if (depth < Depth - 0.015f) _latchArmed = true;
        if (_latchArmed && depth >= Depth - 0.004f) Latch();
        else if (depth < -0.015f) Detach();
    }

    /// <summary>
    /// Takes a magazine onto the rail when its top reaches the mouth lined up with the rail, held or flying free. A
    /// thrown magazine can cross the mouth between two steps, so the path its top took since the last step is checked,
    /// and it keeps whatever speed it had along the rail, so a good throw carries it in.
    /// </summary>
    private void Catch()
    {
        Rail(out Float3 seat, out Float3 axis, out Quaternion rotation);
        Float3 mouth = seat - axis * Depth;
        Grabbable? caught = null;
        float caughtAt = 0f;

        foreach (Grabbable candidate in Grabbable.All)
        {
            if ((candidate.Size & SocketSize.Magazine) == 0 || candidate.InSocket.IsValid() || candidate.GetComponent<Magazine>().IsNotValid()) continue;
            Float3 top = Top(candidate);
            bool seen = _lastTops.TryGetValue(candidate, out Float3 before);
            _lastTops[candidate] = top;
            if (caught.IsValid() || Float3.Distance(top, mouth) > 1f) continue;

            Rigidbody3D body = candidate.Body;
            if (Float3.Dot(body.Rotation * (candidate.SocketRotation * Float3.UnitY), axis) < MinAlignment) continue;
            float inward = Float3.Dot(body.LinearVelocity - Gun.GetPointVelocity(top), axis);
            if (!candidate.IsHeld && inward < -0.2f) continue;

            // The nearest the top came to the rail's line over the last step, between the mouth and the seat.
            Float3 from = seen ? before : top;
            float best = float.MaxValue, at = 0f;
            for (int i = 0; i <= 4; i++)
            {
                Float3 point = from + (top - from) * (i / 4f);
                Float3 offset = point - mouth;
                float along = Float3.Dot(offset, axis);
                float across = Float3.Length(offset - axis * along);
                if (along < -CatchRadius || along > Depth || across > CatchRadius) continue;
                if (across < best)
                {
                    best = across;
                    at = Maths.Clamp(along, 0f, Depth);
                }
            }
            if (best == float.MaxValue) continue;
            caught = candidate;
            caughtAt = at;
        }

        if (caught.IsNotValid()) return;
        Item = caught;
        _lastTops.Clear();
        Rigidbody3D caughtBody = caught.Body;
        GameObject.Scene.Physics.IgnoreCollisionBetween(caughtBody, Gun);
        _latchArmed = true;

        // Squared up on the rail where it met it, keeping its speed along the rail.
        Quaternion magazineRotation = rotation * Quaternion.Inverse(caught.SocketRotation);
        Float3 onRail = mouth + axis * caughtAt;
        float speed = Float3.Dot(caughtBody.LinearVelocity - Gun.GetPointVelocity(onRail), axis);
        caughtBody.MoveRotation(magazineRotation);
        caughtBody.MovePosition(onRail - magazineRotation * caught.SocketPosition);
        caughtBody.LinearVelocity = Gun.GetPointVelocity(onRail) + axis * speed;
        caughtBody.AngularVelocity = Gun.AngularVelocity;
        OnRail(caught);
        Haptics(caught, 0.2f);
        Grabbable? gun = Gun.GetComponent<Grabbable>();
        if (gun.IsValid())
            foreach (PhysicsHand hand in gun.Holders)
                XRInput.Vibrate(hand.Hand, 0.2f, 0.03f);
    }

    /// <summary>Joins the magazine to the gun on a line along the rail, free to slide but not to turn.</summary>
    private void OnRail(Grabbable magazine)
    {
        ClearJoint();
        Rigidbody3D gun = Gun;
        Transform gunTransform = gun.Transform;
        Float3 seatLocal = gunTransform.InverseTransformPoint(Transform.Position);
        Float3 axisLocal = Quaternion.Inverse(gunTransform.Rotation) * Transform.Rotation * Float3.UnitY;

        _joint = new GameObject("Magazine Rail");
        _joint.Enabled = false;
        _joint.SetParent(gun.GameObject);
        _joint.Transform.LocalPosition = Float3.Zero;
        var line = _joint.AddComponent<PointOnLineConstraint>();
        line.LineAxis = axisLocal;
        line.Anchor1 = seatLocal - axisLocal * (Depth * 0.5f);
        line.Anchor2 = magazine.SocketPosition;
        line.MinDistance = -Depth * 0.5f - 0.03f;
        line.MaxDistance = Depth * 0.5f;
        line.ConnectedBody = magazine.Body;
        _joint.AddComponent<FixedAngleConstraint>().ConnectedBody = magazine.Body;
        _joint.Enabled = true;
    }

    private void Weld(Grabbable magazine)
    {
        ClearJoint();
        _joint = new GameObject("Magazine Latch");
        _joint.Enabled = false;
        _joint.SetParent(magazine.GameObject);
        _joint.Transform.LocalPosition = Float3.Zero;
        var socket = _joint.AddComponent<BallSocketConstraint>();
        socket.Anchor = magazine.SocketPosition;
        socket.ConnectedBody = Gun;
        _joint.AddComponent<FixedAngleConstraint>().ConnectedBody = Gun;
        _joint.Enabled = true;
    }

    /// <summary>Seats the magazine: lets go of it from any hand, welds it in place and stops it being grabbed.</summary>
    private void Latch()
    {
        Grabbable magazine = Item!;
        Haptics(magazine, 0.5f);
        for (int i = magazine.Holders.Count - 1; i >= 0; i--)
            magazine.Holders[i].Release(intoSocket: false);

        Rail(out Float3 seat, out _, out Quaternion rotation);
        Quaternion magazineRotation = rotation * Quaternion.Inverse(magazine.SocketRotation);
        Rigidbody3D body = magazine.Body;
        body.MoveRotation(magazineRotation);
        body.MovePosition(seat - magazineRotation * magazine.SocketPosition);
        body.LinearVelocity = Gun.GetPointVelocity(body.Position);
        body.AngularVelocity = Gun.AngularVelocity;

        Weld(magazine);
        magazine.Enabled = false;
        Latched = true;
    }

    /// <summary>The magazine release: unlatches the magazine and pushes it back down the rail, out of the gun.</summary>
    public void Release()
    {
        if (Item.IsNotValid() || !Latched) return;
        Grabbable magazine = Item;
        Latched = false;
        _latchArmed = false;
        magazine.Enabled = true;
        OnRail(magazine);
        Rail(out _, out Float3 axis, out _);
        magazine.Body.LinearVelocity = Gun.GetPointVelocity(magazine.Body.Position) - axis * 1.5f;
    }

    private void Detach()
    {
        _lastTops.Clear();
        Grabbable magazine = Item!;
        ClearJoint();
        Item = null;
        GameObject.Scene.Physics.EnableCollisionBetween(magazine.Body, Gun);
    }

    private void ClearJoint()
    {
        if (_joint.IsNotValid()) return;
        _joint.Enabled = false;
        _joint.Destroy();
        _joint = null;
    }

    private static void Haptics(Grabbable magazine, float strength)
    {
        foreach (PhysicsHand hand in magazine.Holders)
            XRInput.Vibrate(hand.Hand, strength, 0.03f);
    }
}

/// <summary>
/// A gun along its own +Z, held by its first grab point. The hand holding that point fires with the trigger.
/// </summary>
public sealed class Gun : Component
{
    public Float3 Muzzle;
    public Float3 EjectPort;

    public bool Automatic;
    /// <summary>Seconds between shots while the trigger is held, for an automatic gun.</summary>
    public float FireInterval = 0.09f;

    /// <summary>The push a round gives whatever it hits, in newton seconds.</summary>
    public float HitImpulse = 4f;
    public float Range = 120f;

    /// <summary>The kick back along the barrel and up at the muzzle each shot, in newton seconds.</summary>
    public float RecoilBack = 0.8f;
    public float RecoilUp = 0.35f;

    /// <summary>How far each shot kicks the hand holding the grip, back in metres and nose up in degrees. A supporting hand feels a third of it.</summary>
    public float KickBack = 0.03f;
    public float KickPitch = 16f;

    public MagazineWell MagazineWell = null!;
    public GunSlide Slide = null!;

    public bool Chambered;
    public bool SpentInChamber;

    private Grabbable _grabbable = null!;
    private bool _wasPulled;
    private bool _wasReleasing;
    private float _cooldown;
    private PointLight _flash = null!;
    private float _flashTime;
    private LineRenderer _tracer = null!;
    private float _tracerTime;
    private readonly HashSet<Rigidbody3D> _ignore = new();
    private static Material? s_brass;

    public Rigidbody3D Body => GetComponent<Rigidbody3D>()!;
    public Magazine? Magazine => MagazineWell.Latched && MagazineWell.Item.IsValid() ? MagazineWell.Item.GetComponent<Magazine>() : null;

    public override void OnEnable()
    {
        _grabbable = GetComponent<Grabbable>()!;
        if (_flash != null) return;

        var flash = new GameObject("Muzzle Flash");
        flash.SetParent(GameObject);
        _flash = flash.AddComponent<PointLight>();
        _flash.Color = new Color(1f, 0.7f, 0.35f, 1f);
        _flash.Range = 4f;
        _flash.Intensity = 0f;
        _flash.CastShadows = false;

        var tracer = new GameObject("Tracer");
        tracer.SetParent(GameObject);
        tracer.Transform.LocalPosition = Float3.Zero;
        tracer.Transform.LocalRotation = Quaternion.Identity;
        _tracer = tracer.AddComponent<LineRenderer>();
        _tracer.Material = new Material(Shader.LoadDefault(DefaultShader.Line));
        _tracer.StartWidth = 0.012f;
        _tracer.EndWidth = 0.004f;
        _tracer.StartColor = new Color(1f, 0.85f, 0.5f, 1f);
        _tracer.EndColor = new Color(1f, 0.6f, 0.3f, 0.2f);
    }

    /// <summary>The hand holding the gun by its grip, which works the trigger and the magazine release.</summary>
    public PhysicsHand? GripHand
    {
        get
        {
            foreach (PhysicsHand hand in _grabbable.Holders)
                if (_grabbable.Points.Count > 0 && hand.HeldPoint == _grabbable.Points[0]) return hand;
            return null;
        }
    }

    public override void Update()
    {
        float dt = Time.DeltaTime;
        _cooldown -= dt;

        _flashTime -= dt;
        _flash.Intensity = _flashTime > 0f ? 3f : 0f;
        _tracerTime -= dt;
        if (_tracerTime <= 0f) _tracer.Points.Clear();

        PhysicsHand? hand = GripHand;
        if (hand.IsNotValid())
        {
            _wasPulled = _wasReleasing = false;
            return;
        }

        bool pulled = XRInput.GetTrigger(hand.Hand) > (_wasPulled ? 0.6f : 0.7f);
        bool fire = pulled && (!_wasPulled || Automatic) && _cooldown <= 0f;
        _wasPulled = pulled;
        if (fire) Fire(hand);

        bool releasing = XRInput.GetButton(hand.Hand, XRButton.Primary);
        if (releasing && !_wasReleasing) DropMagazine();
        _wasReleasing = releasing;
    }

    /// <summary>Pulls the trigger once for the hand holding the grip, as the trigger itself would.</summary>
    public void PullTrigger()
    {
        PhysicsHand? hand = GripHand;
        if (hand.IsValid()) Fire(hand);
    }

    private void Fire(PhysicsHand hand)
    {
        _cooldown = FireInterval;
        if (!Chambered || Slide.IsOpen)
        {
            XRInput.Vibrate(hand.Hand, 0.3f, 0.02f);
            _cooldown = 0.25f;
            return;
        }

        Chambered = false;
        SpentInChamber = true;
        Shoot(hand);

        // The gas from the shot works the slide: out goes the casing and in comes the next round, if there is one.
        EjectFromChamber();
        if (!ChamberFromMagazine()) Slide.LockBack();
        else Slide.Kick();
    }

    private void Shoot(PhysicsHand hand)
    {
        Rigidbody3D body = Body;
        Float3 muzzle = body.Position + body.Rotation * Muzzle;
        Float3 forward = body.Rotation * Float3.UnitZ;
        Float3 up = body.Rotation * Float3.UnitY;

        _ignore.Clear();
        _ignore.UnionWith(hand.Body.IgnoredBodies);
        _ignore.Add(body);
        _ignore.Add(hand.Rigidbody);
        foreach (PhysicsHand holder in _grabbable.Holders) _ignore.Add(holder.Rigidbody);
        if (MagazineWell.Item.IsValid()) _ignore.Add(MagazineWell.Item.Body);

        Float3 end = muzzle + forward * Range;
        if (GameObject.Scene.Physics.Raycast(muzzle, forward, out RaycastHit hit, Range, QueryFilter.Default.Ignoring(_ignore)))
        {
            end = hit.Point;
            Rigidbody3D? target = hit.Rigidbody;
            if (target.IsValid())
            {
                if (target.MotionType == Jitter2.Dynamics.MotionType.Dynamic) target.ApplyImpulse(forward * HitImpulse, hit.Point);
                HitFlash? flash = target.GetComponent<HitFlash>();
                if (flash.IsValid()) flash.Flash(0.8f);
            }
        }

        body.ApplyImpulse(-forward * RecoilBack, muzzle);
        body.ApplyImpulse(up * RecoilUp, muzzle);

        _flash.Transform.LocalPosition = Muzzle;
        _flashTime = 0.05f;
        _tracerTime = 0.04f;
        _tracer.Points.Clear();
        _tracer.Points.Add(Muzzle);
        _tracer.Points.Add(Transform.InverseTransformPoint(end));

        foreach (PhysicsHand holder in _grabbable.Holders)
        {
            float share = holder == hand ? 1f : 0.35f;
            holder.AddRecoil(KickBack * share, KickPitch * share);
            XRInput.Vibrate(holder.Hand, 0.8f * share, 0.06f);
        }
    }

    /// <summary>Takes the next round from the magazine into the chamber, if the chamber is empty and there is one.</summary>
    public bool ChamberFromMagazine()
    {
        if (Chambered || SpentInChamber) return false;
        Magazine? magazine = Magazine;
        if (magazine.IsNotValid() || magazine.Rounds <= 0) return false;
        magazine.Rounds--;
        Chambered = true;
        return true;
    }

    /// <summary>Throws whatever is in the chamber out of the ejection port: a live round or a spent casing.</summary>
    public void EjectFromChamber()
    {
        if (!Chambered && !SpentInChamber) return;
        bool live = Chambered;
        Chambered = false;
        SpentInChamber = false;
        SpawnCasing(live);
    }

    private void SpawnCasing(bool live)
    {
        Rigidbody3D body = Body;
        s_brass ??= SampleMaterial(new Color(0.75f, 0.55f, 0.15f, 1f));

        var casing = new GameObject(live ? "Round" : "Casing");
        casing.Transform.Position = body.Position + body.Rotation * EjectPort;
        casing.Transform.Rotation = body.Rotation;
        Float3 size = new(0.01f, 0.01f, live ? 0.03f : 0.02f);
        var renderer = casing.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(size);
        renderer.Material = s_brass;
        casing.AddComponent<BoxCollider>().Size = size;
        var rigidbody = casing.AddComponent<Rigidbody3D>();
        rigidbody.Mass = 0.012f;
        rigidbody.EnableSpeculativeContacts = true;
        casing.AddComponent<Expire>().Seconds = 6f;
        GameObject.Scene.Add(casing);

        Float3 right = body.Rotation * Float3.UnitX;
        Float3 up = body.Rotation * Float3.UnitY;
        rigidbody.LinearVelocity = body.GetPointVelocity(casing.Transform.Position) + right * 2.2f + up * 1.6f;
        rigidbody.AngularVelocity = up * 20f;
        foreach (PhysicsHand holder in _grabbable.Holders)
            GameObject.Scene.Physics.IgnoreCollisionBetween(rigidbody, holder.Rigidbody);
        GameObject.Scene.Physics.IgnoreCollisionBetween(rigidbody, body);
    }

    /// <summary>The magazine release: the magazine slides back out of the well.</summary>
    public void DropMagazine() => MagazineWell.Release();

    private static Material SampleMaterial(Color color)
    {
        var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        material.SetColor("_MainColor", color);
        material.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.White));
        material.SetFloat("_Metallic", 0.9f);
        material.SetFloat("_Roughness", 0.35f);
        return material;
    }
}

/// <summary>
/// A slide or charging handle, worked by a free hand gripping it at <see cref="Handle"/> while the other hand holds
/// the gun. It follows the hand back along the barrel up to <see cref="Travel"/> and springs forward when let go.
/// </summary>
public sealed class GunSlide : HandTarget
{
    public Gun Gun = null!;
    public Transform Visual = null!;
    public Float3 Handle;
    public float Travel = 0.04f;
    public float GripReach = 0.1f;
    public float ReturnSpeed = 6f;

    /// <summary>Whether the slide moves back with every shot. A charging handle stays put.</summary>
    public bool Reciprocates = true;

    public bool IsOpen => _locked || _offset > Travel * 0.5f;

    private Float3 _restVisual;
    private bool _hasRest;
    private float _offset;
    private bool _locked;
    private bool _pastEject;
    private float _grabbedAt;
    private float _kick;

    public override float DistanceTo(PhysicsHand hand, Float3 grip)
    {
        Grabbable gun = Gun.GetComponent<Grabbable>()!;
        if (!gun.IsHeld || gun.Holders.Contains(hand)) return float.MaxValue;
        Rigidbody3D body = Gun.Body;
        float distance = Float3.Distance(grip, body.Position + body.Rotation * (Handle - Float3.UnitZ * _offset));
        return distance < GripReach ? distance : float.MaxValue;
    }

    protected override void OnBegin(PhysicsHand hand)
    {
        // A slide held back on an empty magazine is let go by grabbing it, and flies home with a round if there is one.
        if (_locked)
        {
            _locked = false;
            _pastEject = true;
        }
        _grabbedAt = AlongBack(hand.GripPoint) - _offset;
    }

    private float AlongBack(Float3 point)
    {
        Rigidbody3D body = Gun.Body;
        return Float3.Dot(point - body.Position, body.Rotation * -Float3.UnitZ);
    }

    public void LockBack()
    {
        if (!Reciprocates && !_locked) return;
        _locked = true;
        _offset = Travel;
    }

    public void Kick()
    {
        if (Reciprocates) _kick = 1f;
    }

    public override void Update()
    {
        float dt = Time.DeltaTime;
        if (!_hasRest)
        {
            _restVisual = Visual.LocalPosition;
            _hasRest = true;
        }

        if (User.IsValid())
            _offset = Maths.Clamp(AlongBack(User.GripPoint) - _grabbedAt, 0f, Travel);
        else if (_locked)
            _offset = Travel;
        else
            _offset = MathF.Max(0f, _offset - ReturnSpeed * dt);

        // Back past the ejection point throws out what is in the chamber, forward again picks up the next round.
        if (_offset > Travel * 0.85f && !_pastEject)
        {
            _pastEject = true;
            Gun.EjectFromChamber();
            if (User.IsValid()) XRInput.Vibrate(User.Hand, 0.3f, 0.02f);
        }
        else if (_offset < Travel * 0.2f && _pastEject && !_locked)
        {
            _pastEject = false;
            if (Gun.ChamberFromMagazine() && User.IsValid()) XRInput.Vibrate(User.Hand, 0.4f, 0.03f);
        }

        _kick = MathF.Max(0f, _kick - dt * 18f);
        float shown = MathF.Max(_offset, MathF.Sin(_kick * MathF.PI) * Travel);
        Visual.LocalPosition = _restVisual - Float3.UnitZ * shown;
    }
}

/// <summary>
/// A bow along its own +Y, shooting along +Z. While one hand holds it, the other hand gripping the string at
/// <see cref="NockRest"/> nocks an arrow, from its hand or a fresh one, draws it back and looses it on letting go.
/// </summary>
public sealed class Bow : HandTarget
{
    public Float3 NockRest;
    public Float3 ArrowRest;
    public Float3 TopTip;
    public Float3 BottomTip;
    public float MaxDraw = 0.55f;
    public float MaxSpeed = 45f;
    public float StringReach = 0.12f;

    /// <summary>Makes a new arrow when an empty hand grips the string.</summary>
    public Func<Arrow> MakeArrow = null!;

    private Arrow? _nocked;
    private float _draw;
    private float _lastPulse;
    private LineRenderer _string = null!;

    public Grabbable Grabbable => GetComponent<Grabbable>()!;
    public Rigidbody3D BowBody => GetComponent<Rigidbody3D>()!;
    public float Draw => _draw;

    public override void OnEnable()
    {
        base.OnEnable();
        if (_string != null) return;
        var line = new GameObject("String");
        line.SetParent(GameObject);
        line.Transform.LocalPosition = Float3.Zero;
        line.Transform.LocalRotation = Quaternion.Identity;
        _string = line.AddComponent<LineRenderer>();
        _string.Material = new Material(Shader.LoadDefault(DefaultShader.Line));
        _string.StartWidth = _string.EndWidth = 0.004f;
        _string.StartColor = _string.EndColor = new Color(0.85f, 0.82f, 0.7f, 1f);
    }

    private Float3 World(Float3 local) => BowBody.Position + BowBody.Rotation * local;
    private Float3 Forward => BowBody.Rotation * Float3.UnitZ;

    public override float DistanceTo(PhysicsHand hand, Float3 grip)
    {
        if (!Grabbable.IsHeld || Grabbable.Holders.Contains(hand)) return float.MaxValue;
        float distance = Float3.Distance(grip, World(NockRest));
        return distance < StringReach ? distance : float.MaxValue;
    }

    protected override void OnBegin(PhysicsHand hand)
    {
        _draw = 0f;
        _lastPulse = 0f;
        if (_nocked.IsNotValid()) Nock(MakeArrow());
    }

    protected override void OnEnd(PhysicsHand hand)
    {
        if (_nocked.IsValid() && _draw > 0.08f) Loose();
        _draw = 0f;
    }

    private void Nock(Arrow arrow)
    {
        _nocked = arrow;
        arrow.Nock(this);
    }

    private void Loose()
    {
        Arrow arrow = _nocked!;
        _nocked = null;
        float speed = MaxSpeed * MathF.Pow(_draw / MaxDraw, 1.3f);
        arrow.Loose(Forward * speed + BowBody.GetPointVelocity(World(NockRest)));
        foreach (PhysicsHand holder in Grabbable.Holders)
            XRInput.Vibrate(holder.Hand, 0.6f, 0.05f);
    }

    /// <summary>Where a nocked arrow's notch sits and how it points, from the string pulled back by the current draw.</summary>
    public void NockedPose(out Float3 notch, out Quaternion rotation)
    {
        notch = World(NockRest) - Forward * _draw;
        Float3 rest = World(ArrowRest);
        Float3 direction = Float3.Normalize(rest - notch);
        rotation = Quaternion.FromToRotation(Float3.UnitY, direction);
        Float3 up = BowBody.Rotation * Float3.UnitY;
        Float3 arrowUp = rotation * Float3.UnitZ;
        float roll = MathF.Atan2(Float3.Dot(Float3.Cross(arrowUp, up), direction), Float3.Dot(arrowUp, up));
        rotation = Quaternion.AxisAngle(direction, roll) * rotation;
    }

    public override void Update()
    {
        Grabbable bow = Grabbable;

        // A hand holding an arrow nocks it by bringing its notch to the string.
        if (_nocked.IsNotValid() && User.IsNotValid() && bow.IsHeld)
            foreach (Grabbable item in Grabbable.All)
            {
                Arrow? arrow = item.GetComponent<Arrow>();
                if (arrow.IsNotValid() || item.Holders.Count != 1 || bow.Holders.Contains(item.Holders[0])) continue;
                if (Float3.Distance(arrow.NotchPoint, World(NockRest)) > StringReach) continue;
                PhysicsHand hand = item.Holders[0];
                hand.Release(intoSocket: false);
                Nock(arrow);
                hand.Use(this);
                break;
            }

        if (User.IsValid())
        {
            _draw = Maths.Clamp(Float3.Dot(User.GripPoint - World(NockRest), -Forward), 0f, MaxDraw);

            // The string creaks in clicks that crowd closer and bite harder as it tightens, the bow arm feeling the strain too.
            float tension = _draw / MaxDraw;
            float spacing = 0.05f + (0.008f - 0.05f) * tension;
            if (MathF.Abs(_draw - _lastPulse) > spacing)
            {
                _lastPulse = _draw;
                XRInput.Vibrate(User.Hand, 0.15f + 0.6f * tension * tension, 0.012f);
                foreach (PhysicsHand holder in bow.Holders) XRInput.Vibrate(holder.Hand, 0.25f * tension, 0.012f);
            }
        }
        else
        {
            _draw = 0f;
        }

        // Put down with an arrow on the string, the bow lets the arrow fall.
        if (_nocked.IsValid() && !bow.IsHeld)
        {
            _nocked.Loose(BowBody.LinearVelocity);
            _nocked = null;
        }

        _string.Points.Clear();
        _string.Points.Add(TopTip);
        _string.Points.Add(NockRest - Float3.UnitZ * _draw);
        _string.Points.Add(BottomTip);
    }
}

/// <summary>
/// An arrow along its own +Y with its notch at <see cref="Notch"/>. In flight it turns to face where it is going, and it
/// looks ahead each step so it is not too fast to stick in what it hits.
/// </summary>
public sealed class Arrow : Component
{
    public Float3 Notch;
    public Float3 Tip;

    private Bow? _bow;
    private Bow? _leftBow;
    private float _sinceLoosed;
    private bool _flying;
    private readonly HashSet<Rigidbody3D> _ignore = new();

    public Rigidbody3D Body => GetComponent<Rigidbody3D>()!;
    public Float3 NotchPoint => Body.Position + Body.Rotation * Notch;

    public void Nock(Bow bow)
    {
        _bow = bow;
        _flying = false;
        GetComponent<Grabbable>()!.Enabled = false;
        Body.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        bow.NockedPose(out Float3 notch, out Quaternion rotation);
        Float3 position = notch - rotation * Notch;
        Transform.Position = position;
        Transform.Rotation = rotation;
        Body.MovePosition(position);
        Body.MoveRotation(rotation);
        SetCollisionsWithBow(bow, ignore: true);

        // A kinematic arrow would shove the player's own body, so it never touches it again.
        foreach (PhysicsHand holder in bow.Grabbable.Holders)
            foreach (Rigidbody3D part in holder.Body.Parts)
                GameObject.Scene.Physics.IgnoreCollisionBetween(Body, part);
    }

    public void Loose(Float3 velocity)
    {
        Bow? bow = _bow;
        _bow = null;
        _flying = true;
        Rigidbody3D body = Body;
        body.MotionType = Jitter2.Dynamics.MotionType.Dynamic;
        body.LinearVelocity = velocity;
        body.AngularVelocity = Float3.Zero;
        GetComponent<Grabbable>()!.Enabled = true;
        if (bow.IsValid()) _ignore.UnionWith(BowBodies(bow));
        _leftBow = bow;
        _sinceLoosed = 0f;
    }

    private static IEnumerable<Rigidbody3D> BowBodies(Bow bow)
    {
        yield return bow.BowBody;
        foreach (PhysicsHand holder in bow.Grabbable.Holders) yield return holder.Rigidbody;
        if (bow.User.IsValid()) yield return bow.User.Rigidbody;
    }

    private void SetCollisionsWithBow(Bow bow, bool ignore)
    {
        PhysicsWorld physics = GameObject.Scene.Physics;
        foreach (Rigidbody3D other in BowBodies(bow))
            if (ignore) physics.IgnoreCollisionBetween(Body, other);
            else physics.EnableCollisionBetween(Body, other);
    }

    public override void FixedUpdate()
    {
        float dt = Time.FixedDeltaTime;
        Rigidbody3D body = Body;

        if (_bow.IsValid())
        {
            _bow.NockedPose(out Float3 notch, out Quaternion rotation);
            Float3 position = notch - rotation * Notch;
            body.LinearVelocity = (position - body.Position) / dt;
            body.AngularVelocity = PhysicsHand.AngularVelocityTo(body.Rotation, rotation, dt);
            return;
        }

        // Once clear of the bow and the hands, the arrow can touch them again.
        _sinceLoosed += dt;
        if (_leftBow.IsValid() && _sinceLoosed > 0.3f)
        {
            SetCollisionsWithBow(_leftBow, ignore: false);
            _leftBow = null;
        }

        if (!_flying) return;
        Float3 velocity = body.LinearVelocity;
        float speed = Float3.Length(velocity);
        if (speed < 2f)
        {
            Land();
            return;
        }

        // Turn to face along the flight, like the fletching would.
        Float3 direction = velocity / speed;
        Float3 axis = body.Rotation * Float3.UnitY;
        Quaternion facing = Quaternion.FromToRotation(axis, direction) * body.Rotation;
        body.AngularVelocity = PhysicsHand.AngularVelocityTo(body.Rotation, facing, 0.05f);

        // Look ahead of the tip for what it is about to hit, since a fast arrow covers most of its length each step.
        Float3 tip = body.Position + body.Rotation * Tip;
        _ignore.Add(body);
        if (!GameObject.Scene.Physics.Raycast(tip, direction, out RaycastHit hit, speed * dt + 0.02f, QueryFilter.Default.Ignoring(_ignore))) return;

        Stabbable? target = hit.Rigidbody.IsValid() ? hit.Rigidbody.GetComponent<Stabbable>() : null;
        Stabber? stabber = GetComponent<Stabber>();
        if (target.IsValid() && stabber.IsValid())
        {
            const float sink = 0.04f;
            body.MovePosition(body.Position + direction * (hit.Distance + sink));
            body.MoveRotation(facing);
            stabber.StickInto(target, sink);
            HitFlash? flash = target.GetComponent<HitFlash>();
            if (flash.IsValid()) flash.Flash(0.7f);
            Land();
            return;
        }

        if (hit.Rigidbody.IsValid() && hit.Rigidbody.MotionType == Jitter2.Dynamics.MotionType.Dynamic)
            hit.Rigidbody.ApplyImpulse(velocity * body.Mass, hit.Point);
    }

    private void Land()
    {
        _flying = false;
        _ignore.Clear();
    }
}

/// <summary>Removes its GameObject after <see cref="Seconds"/>.</summary>
public sealed class Expire : Component
{
    public float Seconds = 5f;

    public override void Update()
    {
        Seconds -= Time.DeltaTime;
        if (Seconds <= 0f) GameObject.Destroy();
    }
}
