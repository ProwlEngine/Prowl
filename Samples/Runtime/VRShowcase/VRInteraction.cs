// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// What the hands interact with:
//   Grabbable   a dynamic body the physics hands can hold, with optional grab points: a point snaps the item into
//               a set place in the hand (a knife's handle), a line lets the hand slide and twist along it (a spear's
//               shaft) and locks while the trigger is held.
//   HandTarget  something a hand works without picking it up, such as a gun's slide or a bow's string.
//   Socket      a slot an item goes into when let go nearby, such as a rack or a magazine well.
//   UIPointer   a laser from the hands that works world space UI, the trigger clicks.
//   MovingPlatform, Spinner  kinematic platforms that carry the body and whatever else rides them.
//

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace VRShowcase;

public enum GrabKind
{
    /// <summary>The hand takes a fixed place on the item, given by <see cref="GrabPoint.Position"/> and <see cref="GrabPoint.Rotation"/>.</summary>
    Point,
    /// <summary>The hand can sit anywhere between <see cref="GrabPoint.LineStart"/> and <see cref="GrabPoint.LineEnd"/>, gripping around the line.</summary>
    Line,
}

/// <summary>
/// Where a hand may hold an item, in the item's own space. The hand grips around its own Y axis, fingers wrapping
/// toward the palm, with its thumb toward +Y, so a point's rotation turns the hand into the item's frame and a
/// line runs along the hand's Y through the grip.
/// </summary>
public sealed class GrabPoint
{
    public GrabKind Kind;
    public Float3 Position;
    public Quaternion Rotation = Quaternion.Identity;
    public Float3 LineStart;
    public Float3 LineEnd;

    /// <summary>
    /// A grip a second hand only braces, like a foregrip. Braced, it holds the item's place and leaves its aim to the
    /// hand on the main grip.
    /// </summary>
    public bool Support;

    public static GrabPoint At(Float3 position, Quaternion? rotation = null) => new() { Kind = GrabKind.Point, Position = position, Rotation = rotation ?? Quaternion.Identity };
    public static GrabPoint Line(Float3 start, Float3 end) => new() { Kind = GrabKind.Line, LineStart = start, LineEnd = end };

    public GrabPoint AsSupport()
    {
        Support = true;
        return this;
    }

    /// <summary>The nearest place on this point to <paramref name="local"/>, in the item's space.</summary>
    public Float3 Nearest(Float3 local)
    {
        if (Kind == GrabKind.Point) return Position;
        Float3 line = LineEnd - LineStart;
        float t = Maths.Clamp(Float3.Dot(local - LineStart, line) / MathF.Max(Float3.LengthSquared(line), 1e-6f), 0f, 1f);
        return LineStart + line * t;
    }
}

/// <summary>
/// How a hand follows its controller, as the natural frequency and damping ratio its position and its turn settle
/// with, and the most force and torque it may use. Higher frequencies follow more tightly, ratios over 1 settle
/// without overshooting.
/// </summary>
public readonly record struct HandDrive(float Frequency, float DampingRatio, float MaxForce, float TurnFrequency, float TurnDampingRatio, float MaxTorque)
{
    /// <summary>The spring that gives a body of <paramref name="mass"/> this natural frequency, in hertz.</summary>
    public static float Spring(float mass, float frequency) => mass * Squared(2f * MathF.PI * frequency);

    /// <summary>The damper that gives a body of <paramref name="mass"/> this damping ratio at this frequency.</summary>
    public static float Damper(float mass, float frequency, float dampingRatio) => 2f * dampingRatio * mass * 2f * MathF.PI * frequency;

    private static float Squared(float value) => value * value;
}

[Flags]
public enum SocketSize
{
    Small = 1,
    Medium = 2,
    Large = 4,
    Magazine = 8,
    Any = Small | Medium | Large,
}

/// <summary>A dynamic body the physics hands can pick up, hold in one or both hands, throw and put in sockets.</summary>
public sealed class Grabbable : Component
{
    public static readonly List<Grabbable> All = new();

    /// <summary>Where the item may be held. Without any, the hand holds it wherever it touched.</summary>
    public List<GrabPoint> Points = new();

    /// <summary>How far from its centre a hand can take hold of an item without grab points, roughly its size.</summary>
    public float Reach = 0.15f;

    /// <summary>Heavier items are held where the hand met them instead of being pulled into its grip.</summary>
    public float MaxSnapMass = 20f;

    /// <summary>How a hand carrying this alone follows its controller, and each hand while two share it. Unset leaves the hand's own.</summary>
    public HandDrive? CarryDrive;
    public HandDrive? SharedDrive;

    /// <summary>How much of its motion while held the item keeps when let go.</summary>
    public float ThrowFactor = 1f;

    /// <summary>
    /// How far toward the hands holding it the item's mass moves while held, from 0 not at all to 1 right into the
    /// grips. Long things held near their end turn about the hands more and swing on their own centre less.
    /// </summary>
    public float GripPivot;
    private Float3? _ownCenterOfMass;
    private bool _pivotedOnGrips;

    public SocketSize Size = SocketSize.Medium;

    /// <summary>The item's pose relative to a socket it sits in.</summary>
    public Float3 SocketPosition;
    public Quaternion SocketRotation = Quaternion.Identity;

    public readonly List<PhysicsHand> Holders = new();
    public Socket? InSocket { get; internal set; }

    /// <summary>Whether the item pressed into something solid on the last step, which lets a hand lean on it.</summary>
    public bool IsTouchingSolid { get; private set; }
    private bool _touched;

    /// <summary>Whether the item hit anything at all on the last step.</summary>
    public bool HitSomething { get; private set; }
    private bool _hit;

    // Where the item was over the last moments of being held, with when, for working out how it was thrown.
    private const int PathLength = 16;
    private const float ThrowWindow = 0.08f;
    private readonly Float3[] _pathPositions = new Float3[PathLength];
    private readonly Quaternion[] _pathRotations = new Quaternion[PathLength];
    private readonly float[] _pathTimes = new float[PathLength];
    private int _pathCount, _pathNewest;
    private float _clock;

    public Rigidbody3D Body => GetComponent<Rigidbody3D>()!;
    public bool IsHeld => Holders.Count > 0;

    /// <summary>Where the part that goes into a socket is, in the world, from the simulated pose.</summary>
    public Float3 SocketPoint => Body.Position + Body.Rotation * SocketPosition;

    public override void OnEnable()
    {
        All.Add(this);
        // Swung, thrown or shot, it can pass through things between steps, so its contacts always look ahead.
        Body.EnableSpeculativeContacts = true;
    }

    public override void OnDisable()
    {
        All.Remove(this);
        for (int i = Holders.Count - 1; i >= 0; i--)
            Holders[i].Release();
    }

    public override void FixedUpdate()
    {
        IsTouchingSolid = _touched;
        HitSomething = _hit;
        _touched = _hit = false;

        Rigidbody3D body = Body;
        _clock += Time.FixedDeltaTime;
        _pathNewest = (_pathNewest + 1) % PathLength;
        _pathPositions[_pathNewest] = body.Position;
        _pathRotations[_pathNewest] = body.Rotation;
        _pathTimes[_pathNewest] = _clock;
        _pathCount = Math.Min(_pathCount + 1, PathLength);
    }

    public override void OnCollisionBegin(Collision collision)
    {
        _touched |= IsSolid(collision);
        _hit = true;
    }

    public override void OnCollisionStay(Collision collision) => _touched |= IsSolid(collision);

    /// <summary>Moves the centre of mass <see cref="GripPivot"/> of the way to the holding hands' grips, or back to the item's own once nobody holds it.</summary>
    public void PivotOnGrips()
    {
        if (GripPivot <= 0f) return;
        Rigidbody3D body = Body;
        if (Holders.Count == 0)
        {
            if (!_pivotedOnGrips) return;
            body.CenterOfMassOverride = _ownCenterOfMass;
            _pivotedOnGrips = false;
            return;
        }

        if (!_pivotedOnGrips) _ownCenterOfMass = body.CenterOfMassOverride;
        _pivotedOnGrips = true;
        Float3 grips = Float3.Zero;
        foreach (PhysicsHand hand in Holders) grips += hand.GripPoint;
        Float3 towardGrips = Quaternion.Inverse(body.Rotation) * (grips / Holders.Count - body.Position);
        Float3 own = _ownCenterOfMass ?? Float3.Zero;
        body.CenterOfMassOverride = own + (towardGrips - own) * Maths.Clamp(GripPivot, 0f, 1f);
    }

    /// <summary>Forgets where the item has been, so a throw only counts what happened after this, like after a teleport.</summary>
    public void ForgetMotion() => _pathCount = 0;

    /// <summary>
    /// Lets the item go moving the way it moved over the last moments before release. The velocity is the slope of a
    /// straight line fitted through where its origin was against when, which evens out a jittery hand without
    /// losing the throw, and the spin is how far it turned across the same stretch.
    /// </summary>
    public void Throw()
    {
        Rigidbody3D body = Body;
        if (!FitPath(out Float3 velocity, out Float3 spin)) return;
        velocity *= ThrowFactor;
        spin *= ThrowFactor;
        Float3 center = body.Rotation * (body.CenterOfMassOverride ?? Float3.Zero);
        body.LinearVelocity = velocity + Float3.Cross(spin, center);
        body.AngularVelocity = spin;
    }

    /// <summary>Least squares velocity and the average spin over the samples within the throw window of the newest.</summary>
    private bool FitPath(out Float3 velocity, out Float3 spin)
    {
        velocity = spin = Float3.Zero;
        if (_pathCount < 2) return false;

        float newest = _pathTimes[_pathNewest];
        int count = 0, oldest = _pathNewest;
        float meanTime = 0f;
        Float3 meanPosition = Float3.Zero;
        for (int i = 0; i < _pathCount; i++)
        {
            int index = (_pathNewest - i + PathLength) % PathLength;
            if (newest - _pathTimes[index] > ThrowWindow + 1e-4f) break;
            meanTime += _pathTimes[index];
            meanPosition += _pathPositions[index];
            oldest = index;
            count++;
        }
        if (count < 2) return false;
        meanTime /= count;
        meanPosition /= count;

        float spread = 0f;
        Float3 covariance = Float3.Zero;
        for (int i = 0; i < count; i++)
        {
            int index = (_pathNewest - i + PathLength) % PathLength;
            float t = _pathTimes[index] - meanTime;
            spread += t * t;
            covariance += (_pathPositions[index] - meanPosition) * t;
        }
        if (spread < 1e-9f) return false;

        velocity = covariance / spread;
        spin = PhysicsHand.AngularVelocityTo(_pathRotations[oldest], _pathRotations[_pathNewest], newest - _pathTimes[oldest]);
        return true;
    }

    /// <summary>Fixed ground and walls, moving platforms and anything heavy enough to lean on.</summary>
    public static bool IsSolid(Collision collision)
    {
        Rigidbody3D other = collision.Rigidbody;
        return other.IsNotValid() || other.MotionType != Jitter2.Dynamics.MotionType.Dynamic || other.Mass >= 25f;
    }

    private bool IsTaken(GrabPoint point)
    {
        foreach (PhysicsHand holder in Holders)
            if (holder.HeldPoint == point) return true;
        return false;
    }

    /// <summary>
    /// How far a hand's grip at <paramref name="grip"/> is from taking hold, and the grab point it would use.
    /// Items without grab points are measured from their centre, less their reach.
    /// </summary>
    public float DistanceTo(Float3 grip, out GrabPoint? point)
    {
        point = null;
        if (Points.Count == 0) return Float3.Distance(grip, Body.Position) - Reach;

        float best = float.MaxValue;
        Rigidbody3D body = Body;
        Float3 local = Quaternion.Inverse(body.Rotation) * (grip - body.Position);
        foreach (GrabPoint candidate in Points)
        {
            // A hand already fills a fixed hold, a line has room for more.
            if (candidate.Kind == GrabKind.Point && IsTaken(candidate)) continue;
            float distance = Float3.Distance(body.Position + body.Rotation * candidate.Nearest(local), grip);
            if (distance < best)
            {
                best = distance;
                point = candidate;
            }
        }
        return best;
    }
}

/// <summary>
/// Something a hand works by gripping it, without picking anything up: the hand stays free and follows its
/// controller while the target reads where the hand is, like pulling a slide or drawing a bowstring.
/// </summary>
public abstract class HandTarget : Component
{
    public static readonly List<HandTarget> All = new();

    public PhysicsHand? User { get; private set; }

    public override void OnEnable() => All.Add(this);

    public override void OnDisable()
    {
        All.Remove(this);
        User?.Release();
    }

    /// <summary>How far the grip at <paramref name="grip"/> is from this target, or infinity when the hand cannot use it now.</summary>
    public abstract float DistanceTo(PhysicsHand hand, Float3 grip);

    /// <summary>The body the hand should stop colliding with while it works this target.</summary>
    public virtual Rigidbody3D? Body => GetComponentInParent<Rigidbody3D>(includeSelf: true);

    internal void Begin(PhysicsHand hand)
    {
        User = hand;
        OnBegin(hand);
    }

    internal void End(PhysicsHand hand)
    {
        if (User != hand) return;
        User = null;
        OnEnd(hand);
    }

    protected virtual void OnBegin(PhysicsHand hand) { }
    protected virtual void OnEnd(PhysicsHand hand) { }
}

/// <summary>
/// A slot that takes an item let go of while the item or the hand holding it is inside its radius. In a socket on a
/// body the item is welded on and moves with it. Anywhere else it is held
/// kinematically and carried along with the socket, so a rack keeps its item. Grabbing the item takes it
/// back out.
/// </summary>
public sealed class Socket : Component
{
    public static readonly List<Socket> All = new();

    public SocketSize Accepts = SocketSize.Any;
    public float Radius = 0.15f;

    public Grabbable? Item { get; private set; }

    public event Action<Grabbable>? Inserted;
    public event Action<Grabbable>? Removed;

    private LineRenderer _ring = null!;
    private GameObject? _weld;
    private Rigidbody3D? _parentBody;
    private bool _settled;
    private Grabbable? _justRemoved;
    private float _removedAt;

    private static readonly Color Idle = new(0.6f, 0.65f, 0.7f, 0.6f);
    private static readonly Color Ready = new(0.3f, 1f, 0.5f, 1f);
    private const float ReinsertDelay = 0.6f;

    public override void OnEnable()
    {
        All.Add(this);
        _parentBody = ParentBody();
        if (_ring == null)
        {
            var ring = new GameObject("Ring");
            ring.SetParent(GameObject);
            ring.Transform.LocalPosition = Float3.Zero;
            ring.Transform.LocalRotation = Quaternion.Identity;
            _ring = ring.AddComponent<LineRenderer>();
            _ring.Material = new Material(Shader.LoadDefault(DefaultShader.Line));
            _ring.StartWidth = _ring.EndWidth = 0.01f;
            _ring.Loop = true;
        }
    }

    public override void OnDisable()
    {
        All.Remove(this);
        Remove();
    }

    private Rigidbody3D? ParentBody() => GameObject.Parent.IsValid() ? GameObject.Parent.GetComponentInParent<Rigidbody3D>(includeSelf: true) : null;

    public bool CanTake(Grabbable item)
        => Item.IsNotValid() && (Accepts & item.Size) != 0 && !(item == _justRemoved && Time.TimeSinceStartup - _removedAt < ReinsertDelay);

    /// <summary>The socket's pose in the world. On a moving body it comes from the body's simulated pose, which joints and drives agree with.</summary>
    public void GetPose(out Float3 position, out Quaternion rotation)
    {
        if (_parentBody.IsValid())
        {
            Transform bodyTransform = _parentBody.Transform;
            Float3 local = bodyTransform.InverseTransformPoint(Transform.Position);
            Quaternion localRotation = Quaternion.Inverse(bodyTransform.Rotation) * Transform.Rotation;
            rotation = _parentBody.Rotation * localRotation;
            position = _parentBody.Position + _parentBody.Rotation * local;
            return;
        }
        position = Transform.Position;
        rotation = Transform.Rotation;
    }

    /// <summary>Whether a held item is close enough to go in: its socket point, or the hand holding it, inside the radius.</summary>
    public bool IsInReach(Grabbable item)
    {
        if (!CanTake(item)) return false;
        GetPose(out Float3 position, out _);
        if (Float3.Distance(item.SocketPoint, position) < Radius) return true;
        foreach (PhysicsHand hand in item.Holders)
            if (Float3.Distance(hand.GripPoint, position) < Radius) return true;
        return false;
    }

    /// <summary>The nearest free socket that would take <paramref name="item"/> where it is now.</summary>
    public static Socket? Near(Grabbable item)
    {
        Socket? best = null;
        float bestDistance = float.MaxValue;
        foreach (Socket socket in All)
        {
            if (!socket.IsInReach(item)) continue;
            socket.GetPose(out Float3 position, out _);
            float distance = Float3.Distance(item.SocketPoint, position);
            if (distance < bestDistance)
            {
                best = socket;
                bestDistance = distance;
            }
        }
        return best;
    }

    public void Insert(Grabbable item)
    {
        if (!CanTake(item)) return;
        if (item.InSocket.IsValid()) item.InSocket.Remove();
        for (int i = item.Holders.Count - 1; i >= 0; i--)
            item.Holders[i].Release(intoSocket: false);

        Item = item;
        item.InSocket = this;
        Rigidbody3D body = item.Body;
        TargetPose(item, out Float3 position, out Quaternion rotation);

        // Before the scene runs the body does not exist yet and takes its pose from the transform.
        item.Transform.Position = position;
        item.Transform.Rotation = rotation;
        body.MovePosition(position);
        body.MoveRotation(rotation);
        body.LinearVelocity = Float3.Zero;
        body.AngularVelocity = Float3.Zero;

        // Filled while the world is being built, the rest waits for the scene to be running.
        _settled = false;
        if (GameObject.Scene.IsValid()) Settle();
        Inserted?.Invoke(item);
    }

    /// <summary>
    /// Joins the item to the socket: welded to a socket on a body, carried kinematically by any other, and kept from
    /// pushing the body it sits on.
    /// </summary>
    private void Settle()
    {
        _settled = true;
        if (Item.IsNotValid()) return;
        _parentBody = ParentBody();
        Rigidbody3D body = Item.Body;
        PhysicsWorld physics = GameObject.Scene.Physics;
        if (_parentBody.IsValid()) physics.IgnoreCollisionBetween(body, _parentBody);

        if (_parentBody.IsValid())
        {
            body.LinearVelocity = _parentBody.GetPointVelocity(body.Position);
            body.AngularVelocity = _parentBody.AngularVelocity;
            Weld(Item);
        }
        else
        {
            body.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        }
    }

    private void Weld(Grabbable item)
    {
        _weld = new GameObject("Socket Weld");
        _weld.Enabled = false;
        _weld.SetParent(item.GameObject);
        _weld.Transform.LocalPosition = Float3.Zero;
        var socket = _weld.AddComponent<BallSocketConstraint>();
        socket.Anchor = item.SocketPosition;
        socket.ConnectedBody = _parentBody;
        _weld.AddComponent<FixedAngleConstraint>().ConnectedBody = _parentBody;
        _weld.Enabled = true;
    }

    public void Remove()
    {
        if (_weld.IsValid())
        {
            _weld.Enabled = false;
            _weld.Destroy();
        }
        _weld = null;

        if (Item.IsNotValid())
        {
            Item = null;
            return;
        }

        Grabbable item = Item;
        Rigidbody3D? body = item.GetComponent<Rigidbody3D>();
        if (body.IsValid() && GameObject.Scene.IsValid())
        {
            body.MotionType = Jitter2.Dynamics.MotionType.Dynamic;
            PhysicsWorld physics = GameObject.Scene.Physics;
            if (_parentBody.IsValid()) physics.EnableCollisionBetween(body, _parentBody);
        }

        item.InSocket = null;
        Item = null;
        _justRemoved = item;
        _removedAt = Time.TimeSinceStartup;
        Removed?.Invoke(item);
    }

    private void TargetPose(Grabbable item, out Float3 position, out Quaternion rotation)
    {
        GetPose(out Float3 socketPosition, out Quaternion socketRotation);
        rotation = socketRotation * Quaternion.Inverse(item.SocketRotation);
        position = socketPosition - rotation * item.SocketPosition;
    }

    /// <summary>Carries a kinematic item along with the socket by velocity, so it moves smoothly and pushes what it meets.</summary>
    public override void FixedUpdate()
    {
        if (!_settled) Settle();

        if (Item.IsNotValid() || _weld.IsValid()) return;

        float dt = Time.FixedDeltaTime;
        Rigidbody3D body = Item.Body;
        TargetPose(Item, out Float3 position, out Quaternion rotation);
        body.LinearVelocity = (position - body.Position) / dt;
        body.AngularVelocity = PhysicsHand.AngularVelocityTo(body.Rotation, rotation, dt);
    }

    /// <summary>Draws the socket's ring while it is empty, larger and green when a held item that fits would go in.</summary>
    public override void Update()
    {
        _ring.Points.Clear();
        if (Item.IsValid()) return;

        bool ready = false;
        foreach (Grabbable item in Grabbable.All)
            if (item.IsHeld && IsInReach(item))
                ready = true;

        _ring.StartColor = _ring.EndColor = ready ? Ready : Idle;
        float radius = MathF.Min(Radius, 0.12f) * (ready ? 0.8f : 0.5f);
        for (int i = 0; i < 24; i++)
        {
            float angle = i / 24f * MathF.PI * 2f;
            _ring.Points.Add(new Float3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0f));
        }
    }
}

/// <summary>
/// Points a laser from whichever empty hand aims at world space UI and drives the scene's <see cref="EventSystem"/>
/// with it. The laser only shows while it is on a panel, and the trigger presses.
/// </summary>
public sealed class UIPointer : Component
{
    public PhysicsHand LeftHand = null!;
    public PhysicsHand RightHand = null!;
    public float MaxDistance = 4f;

    private LineRenderer _laser = null!;
    private PhysicsHand? _active;
    private bool _wasPressed;

    public override void OnEnable()
    {
        if (_laser != null) return;
        var laser = new GameObject("UI Laser");
        laser.SetParent(GameObject);
        _laser = laser.AddComponent<LineRenderer>();
        _laser.Material = new Material(Shader.LoadDefault(DefaultShader.Line));
        _laser.StartWidth = 0.006f;
        _laser.EndWidth = 0.003f;
        _laser.StartColor = new Color(0.4f, 0.8f, 1f, 0.2f);
        _laser.EndColor = new Color(0.4f, 0.8f, 1f, 1f);
    }

    public override void Update()
    {
        _laser.Points.Clear();
        EventSystem? events = EventSystem.Current;
        if (events.IsNotValid() || !XR.IsRunning)
        {
            if (events.IsValid()) events.WorldPointer = null;
            return;
        }

        // The hand pressing keeps the pointer while it holds the trigger, so a drag is not handed to the other hand.
        if (!(_active.IsValid() && _wasPressed))
        {
            _active = null;
            float nearest = MaxDistance;
            foreach (PhysicsHand hand in new[] { RightHand, LeftHand })
                if (!hand.IsBusy && TryAim(hand, out Ray ray) && EventSystem.RaycastWorld(ray, out _, out float distance) && distance < nearest)
                {
                    nearest = distance;
                    _active = hand;
                }
        }

        if (_active.IsNotValid() || !TryAim(_active, out Ray aim))
        {
            events.WorldPointer = null;
            _wasPressed = false;
            return;
        }

        bool pressed = XRInput.GetTrigger(_active.Hand) > (_wasPressed ? 0.5f : 0.7f);
        if (pressed && !_wasPressed) XRInput.Vibrate(_active.Hand, 0.2f, 0.02f);
        _wasPressed = pressed;
        events.WorldPointer = new EventSystem.RayPointer { Ray = aim, Pressed = pressed };

        float length = events.WorldPointerDistance ?? (_wasPressed ? MaxDistance * 0.5f : 0f);
        if (length <= 0f) return;
        _laser.Points.Add(aim.Origin);
        _laser.Points.Add(aim.Origin + aim.Direction * length);
    }

    private static bool TryAim(PhysicsHand hand, out Ray ray)
    {
        XRPose pose = XR.GetPose(hand.Hand == XRHand.Left ? XRNode.LeftHandAim : XRNode.RightHandAim);
        Quaternion rotation = hand.Origin.Rotation * pose.Rotation;
        ray = new Ray(hand.Origin.TransformPoint(pose.Position), rotation * Float3.UnitZ);
        return pose.IsValid;
    }
}

/// <summary>Moves a kinematic body back and forth between two points by velocity, pausing at each end, so whatever stands on it is carried.</summary>
public sealed class MovingPlatform : Component
{
    public Float3 From;
    public Float3 To;
    public float Speed = 1f;
    public float Pause = 2f;

    private float _along;
    private bool _forward = true;
    private float _waiting;

    public override void FixedUpdate()
    {
        float dt = Time.FixedDeltaTime;
        Rigidbody3D body = GetComponent<Rigidbody3D>()!;
        float length = Float3.Distance(From, To);

        if (_waiting > 0f) _waiting -= dt;
        else
        {
            _along += (_forward ? 1f : -1f) * Speed * dt / MathF.Max(length, 1e-3f);
            if (_along >= 1f || _along <= 0f)
            {
                _along = Maths.Clamp(_along, 0f, 1f);
                _forward = !_forward;
                _waiting = Pause;
            }
        }

        // Eased at the ends, so riders are not jolted when it sets off and stops.
        float eased = _along * _along * (3f - 2f * _along);
        Float3 target = From + (To - From) * eased;
        body.LinearVelocity = (target - body.Position) / dt;
    }
}

/// <summary>Spins a kinematic body about the vertical, carrying and turning whatever rides it.</summary>
public sealed class Spinner : Component
{
    public float DegreesPerSecond = 20f;

    public override void FixedUpdate()
        => GetComponent<Rigidbody3D>()!.AngularVelocity = new Float3(0f, DegreesPerSecond * Maths.Deg2Rad, 0f);
}
