// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace VRShowcase;

/// <summary>
/// A hand that is a rigidbody of its own. Each step it pushes toward the controller with a capped force and torque,
/// so it stops against walls and tables instead of passing through them, and anything it holds is joined to it,
/// so a heavy item drags on the hand and lags behind a light one. Pulled more than <see cref="MaxSeparation"/> from
/// the controller, it lets go and jumps back. A ghost hand shows where the controller is while they are apart.
/// <para>
/// A hand that cannot go where its controller is, because it holds on to something fixed or presses a held tool into
/// something solid, pushes the body the other way instead. That is what climbing is, and how pushing a hammer into the
/// ground lifts the player.
/// </para>
/// <para>
/// The hand model points its fingers along +Z with the palm toward the other hand and the thumb on top, and grips
/// around its own Y axis through <see cref="GripPoint"/>.
/// </para>
/// </summary>
public sealed class PhysicsHand : Component
{
    public XRHand Hand;
    public Transform Origin = null!;
    public VRBody Body = null!;

    /// <summary>The hand's mass and how hard its wrist is to turn, which the drives are tuned against.</summary>
    public const float HandMass = 2f;
    public const float WristInertia = 0.02f;

    /// <summary>An empty hand. Two hands pushing down on a table can just lift the body, one cannot.</summary>
    public HandDrive Free = new(6f, 1.8f, 380f, 24f, 7f, 75f);

    /// <summary>Carrying something alone, unless the item sets its own <see cref="Grabbable.CarryDrive"/>.</summary>
    public HandDrive Carry = new(6f, 1.8f, 650f, 24f, 7f, 75f);

    /// <summary>Each hand while two share an item. The wrists follow more loosely, so the two grips aim it between them instead of fighting.</summary>
    public HandDrive Shared = new(6f, 1.8f, 650f, 14f, 3f, 45f);

    /// <summary>A hand bracing a support grip while another holds the item. It holds the item's place and leaves its aim alone.</summary>
    public HandDrive Support = new(6f, 1.8f, 700f, 0f, 0f, 0f);

    /// <summary>Hanging on to the world, followed tightly enough that the body hangs from one hand without sagging.</summary>
    public HandDrive Hang = new(15f, 10f, 1000f, 24f, 7f, 75f);

    /// <summary>How hard the hand must push sideways on the body, in newtons, before the feet let the push move it.</summary>
    public float BraceThreshold = 80f;

    /// <summary>How far the hand may be left behind its controller, empty and holding something, before it lets go and jumps back.</summary>
    public float MaxSeparation = 0.6f;
    public float HeldMaxSeparation = 1.2f;

    /// <summary>How long the hand has to stay that far behind, so a fast swing never counts as being stuck.</summary>
    public float SeparationTime = 0.25f;

    /// <summary>How far the grip has to be squeezed to grab, and let go of to release.</summary>
    public const float GrabSqueeze = 0.6f;
    public const float ReleaseSqueeze = 0.35f;

    /// <summary>How far the trigger has to be pulled to let a line grip slide.</summary>
    public const float SlideTrigger = 0.5f;

    /// <summary>How quickly a sliding grip slows an item sliding through the hand, and turning in it, per second.</summary>
    public float SlideFriction = 2f;
    public float TwistFriction = 4f;
    public float GrabRange = 0.09f;
    public float GhostDistance = 0.08f;

    /// <summary>
    /// How far the hand is tipped forward from the controller's pointing ray, in degrees. A wrist held naturally
    /// points a held sword ahead and a gun level, rather than straight up.
    /// </summary>
    public float GripTilt = 25f;

    /// <summary>Where the model sits relative to the controller's aim pose, so the fingertips land near the controller's tip.</summary>
    private static readonly Float3 ModelOffset = new(0f, -0.02f, -0.1f);

    private const float LostTrackingGrace = 0.5f;

    /// <summary>The hand's colliders as one box in its space, for checking a let go item is clear of the fingers.</summary>
    private static readonly Float3 ClearanceCenter = new(0f, 0f, 0.0375f);
    private static readonly Float3 ClearanceSize = new(0.05f, 0.11f, 0.195f);

    /// <summary>How the pull into the grip tightens, from its slack start to its firm end, and how close counts as settled in the grip.</summary>
    private const float SlackPullFrequency = 3f;
    private const float FirmPullFrequency = 18f;
    private const float PullTightenTime = 0.15f;
    private const float SettledDistance = 0.004f;
    private const float SettledAngle = 0.06f;

    /// <summary>How long the pull gets before the item is set straight into the grip, for one that is blocked or turned too far.</summary>
    private const float MaxPullTime = 0.3f;

    private Rigidbody3D _rigidbody = null!;
    private GameObject _model = null!;
    private GameObject _ghost = null!;
    private Animator _modelAnimator = null!;
    private Animator _ghostAnimator = null!;

    private Grabbable? _held;
    private GrabPoint? _heldPoint;
    private GameObject? _grip;
    private bool _gripLocked;
    private bool _climbing;
    private DriveConstraint? _drive;
    private bool _touchFixed, _touchedFixed;
    private Rigidbody3D? _touchFixedBody, _touchedFixedBody;
    private HandTarget? _target;
    private bool _wasGripping;
    private float _grabWindow;
    private const float GrabWindow = 0.3f;
    private float _curlIndex, _curlGrip, _curlThumb;
    private bool _touched, _touchingSolid;

    private float _kickBack, _kickPitch;
    private const float KickRecovery = 12f;
    private bool _placed;
    private float _untracked;
    private float _apart;
    private Float3 _frameRelative, _frameVelocity, _frameSpin;
    private Quaternion _frameRotation = Quaternion.Identity;
    private bool _hasFrame;
    private readonly List<Rigidbody3D> _clearing = new();
    private readonly List<ShapeCastHit> _overlaps = new();
    private GameObject? _pull;
    private DriveConstraint? _pullDrive;
    private float _pullTightness, _pullTime;
    private Float3 _pullTo;
    private Quaternion _pullToRotation = Quaternion.Identity;
    private static readonly HashSet<Rigidbody3D> s_ignoringPlayer = new();
    private static readonly List<PhysicsHand> s_hands = new();

    /// <summary>
    /// Drives the hand from something other than its controller, such as a scripted hand or a test. While set, it
    /// gives the pose the hand reaches for, and the controller is ignored.
    /// </summary>
    public Func<(Float3 Position, Quaternion Rotation)>? TargetOverride;

    /// <summary>Overrides whether a line grip slides. Without it, the grip slides while the trigger is held.</summary>
    public bool? SlideOverride;

    /// <summary>Overrides how far the grip is squeezed, from 0 to 1, for a scripted hand or a test. Without it, the controller's grip.</summary>
    public Func<float>? GripOverride;

    public float GripValue => GripOverride?.Invoke() ?? XRInput.GetGrip(Hand);

    /// <summary>Overrides how far the trigger is pulled, from 0 to 1, for a scripted hand or a test. Without it, the controller's trigger.</summary>
    public Func<float>? TriggerOverride;

    public float TriggerValue => TriggerOverride?.Invoke() ?? XRInput.GetTrigger(Hand);

    /// <summary>Raised when a hand lets go of an item nobody else holds and it did not go into a socket.</summary>
    public static event Action<PhysicsHand, Grabbable>? LetGo;

    /// <summary>Whether a line grip slides along its line and turns about it instead of holding tight.</summary>
    public bool Sliding => SlideOverride ?? TriggerValue > SlideTrigger;

    public Rigidbody3D Rigidbody => _rigidbody;
    public Grabbable? Held => _held;
    public GrabPoint? HeldPoint => _heldPoint;
    public bool IsHolding => _held.IsValid();
    public bool IsClimbing => _climbing;
    public HandTarget? Target => _target;

    /// <summary>Holding something, climbing or working a target, so the hand is not free to point at UI.</summary>
    public bool IsBusy => IsHolding || IsClimbing || _target.IsValid();

    private float PalmSide => Hand == XRHand.Right ? -1f : 1f;

    /// <summary>Where gripped handles pass through the hand, in the hand's space.</summary>
    public Float3 GripLocal => new(PalmSide * 0.035f, 0f, 0.035f);
    /// <summary>The grip in the world, from the simulated pose, which is what joints and grabs have to agree with.</summary>
    public Float3 GripPoint => _rigidbody.Position + _rigidbody.Rotation * GripLocal;

    /// <summary>Builds the hand: a rigidbody with palm and finger colliders and a jointed model, ready to add to the scene.</summary>
    public static PhysicsHand Create(XRHand side, Transform origin, VRBody body, Color skin)
    {
        var go = new GameObject(side + " Hand");
        var rigidbody = go.AddComponent<Rigidbody3D>();
        rigidbody.Mass = HandMass;
        rigidbody.AffectedByGravity = false;
        rigidbody.Friction = 0.8f;
        // Heavier to turn than its shape, as a wrist is, so it holds steady against whatever it carries.
        rigidbody.InertiaTensorOverride = new Float3(WristInertia);
        rigidbody.EnableSpeculativeContacts = true;
        go.AddComponent<MeleeWeapon>().MinSpeed = 2.5f;

        var hand = go.AddComponent<PhysicsHand>();
        hand.Hand = side;
        hand.Origin = origin;
        hand.Body = body;
        hand._rigidbody = rigidbody;
        go.AddComponent<GravityGlove>().Hand = hand;

        var palm = new GameObject("Palm Collider");
        palm.SetParent(go);
        palm.Transform.LocalPosition = Float3.Zero;
        palm.AddComponent<BoxCollider>().Size = new Float3(0.03f, 0.09f, 0.1f);
        var fingers = new GameObject("Finger Collider");
        fingers.SetParent(go);
        fingers.Transform.LocalPosition = new Float3(0f, 0f, 0.085f);
        fingers.AddComponent<BoxCollider>().Size = new Float3(0.022f, 0.08f, 0.08f);

        hand._model = hand.BuildModel(go, Lit(skin, 0.7f), out hand._modelAnimator);
        hand._ghost = new GameObject(side + " Ghost Hand");
        var ghostMaterial = new Material(Shader.LoadDefault(DefaultShader.StandardTransparent));
        ghostMaterial.SetColor("_MainColor", new Color(0.6f, 0.8f, 1f, 0.3f));
        hand.BuildModel(hand._ghost, ghostMaterial, out hand._ghostAnimator);
        go.Transform.Position = new Float3(0f, -100f, 0f);
        return hand;
    }

    /// <summary>The ghost has no physics and lives at the root of the scene, so it is added alongside the hand.</summary>
    public GameObject Ghost => _ghost;

    private static Material Lit(Color color, float roughness)
    {
        var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        material.SetColor("_MainColor", color);
        material.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.White));
        material.SetFloat("_Roughness", roughness);
        return material;
    }

    // ----------------------------------------------------------------
    //  Model
    // ----------------------------------------------------------------

    // Which fingers each curl drives, by the bone that starts them, the mask spreading down to the tip.
    private static readonly (string Parameter, string Bones)[] FingerGroups =
    [
        ("Index", "Index1"),
        ("Grip", "Middle1,Ring1,Little1"),
        ("Thumb", "Thumb1"),
    ];

    /// <summary>
    /// The rigged hand model under <paramref name="parent"/>, in <paramref name="material"/>. Its animator lays the
    /// model's Fist pose over its Open pose once per group of fingers, each weighted by how far that group is curled.
    /// </summary>
    private GameObject BuildModel(GameObject parent, Material material, out Animator animator)
    {
        string path = Hand == XRHand.Right ? "Models/Right Hand" : "Models/Left Hand";
        PrefabAsset prefab = AssetDatabase.FindResource<PrefabAsset>(path) ?? throw new FileNotFoundException($"The hand model is missing from 'Assets/{path}'.");
        GameObject model = GameObject.InstantiateDetached(prefab)!;
        model.SetParent(parent, false);
        model.Transform.LocalPosition = Float3.Zero;
        model.Transform.LocalRotation = Quaternion.Identity;

        foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skin.Materials.Clear();
            skin.Materials.Add(material);
        }

        animator = model.GetComponent<Animator>()!;
        animator.Graph = FingerGraph(animator);
        return model;
    }

    private static AnimationGraph FingerGraph(Animator animator)
    {
        AnimationClip Pose(string name)
            => animator.Clips.Find(clip => clip.Name.EndsWith(name) || clip.SourceName == name)
               ?? throw new InvalidOperationException($"The hand model has no {name} pose.");

        var graph = new AnimationGraph { Name = "Fingers" };
        GraphNodeRecord open = graph.AddNode(AnimationNodeIds.Clip, "open");
        open.Properties["Clip"] = NodeValue.FromClip(Pose("Open"));

        GraphNodeRecord layers = graph.AddNode(AnimationNodeIds.LayerBlend, "layers");
        layers.Inputs.Add(new GraphInputRecord { Node = open.Id });
        foreach ((string parameter, string bones) in FingerGroups)
        {
            graph.Parameters.Add(new GraphParameterRecord { Name = parameter, Kind = NodeValueKind.Number });
            GraphNodeRecord weight = graph.AddNode(AnimationNodeIds.Parameter, parameter.ToLowerInvariant() + "Weight");
            weight.Properties["Name"] = NodeValue.FromText(parameter);
            GraphNodeRecord mask = graph.AddNode(AnimationNodeIds.BoneMask, parameter.ToLowerInvariant() + "Mask");
            mask.Properties["Bones"] = NodeValue.FromText(bones);
            GraphNodeRecord fist = graph.AddNode(AnimationNodeIds.Clip, parameter.ToLowerInvariant() + "Fist");
            fist.Properties["Clip"] = NodeValue.FromClip(Pose("Fist"));

            layers.Inputs.Add(new GraphInputRecord { Node = fist.Id });
            layers.Inputs.Add(new GraphInputRecord { Node = weight.Id });
            layers.Inputs.Add(new GraphInputRecord { Node = mask.Id });
        }
        graph.RootNode = layers.Id;
        return graph;
    }

    private void Curl(Animator animator, float thumb)
    {
        animator.SetFloat("Index", _curlIndex);
        animator.SetFloat("Grip", _curlGrip);
        animator.SetFloat("Thumb", thumb);
    }

    // ----------------------------------------------------------------
    //  Following the controller
    // ----------------------------------------------------------------

    /// <summary>Where the controller puts the hand, as the view sees it this frame.</summary>
    private bool TryGetTarget(out Float3 position, out Quaternion rotation)
    {
        if (TargetOverride != null)
        {
            (position, rotation) = TargetOverride();
            return true;
        }

        XRPose aim = XR.GetPose(Hand == XRHand.Left ? XRNode.LeftHandAim : XRNode.RightHandAim);
        rotation = Origin.Rotation * aim.Rotation;
        position = Origin.TransformPoint(aim.Position) + rotation * ModelOffset;
        rotation = rotation * Tilt;
        return XR.IsRunning && aim.HasPosition && aim.HasRotation;
    }

    /// <summary>The hand's turn forward from the controller's pointing ray, which leaves its fingers along the ray.</summary>
    public Quaternion Tilt => Quaternion.AxisAngle(Float3.UnitX, GripTilt * Maths.Deg2Rad);

    /// <summary>Where the controller points, from the simulated hand, for aiming at things out of reach.</summary>
    public Float3 AimDirection => _rigidbody.Rotation * Quaternion.Inverse(Tilt) * Float3.UnitZ;

    public override void FixedUpdate()
    {
        float dt = Time.FixedDeltaTime;
        IgnorePlayer(_rigidbody);
        ClearReleased();
        EnsureDrive();
        _touchingSolid = _touched;
        _touchedFixed = _touchFixed;
        _touchedFixedBody = _touchFixedBody;
        _touched = _touchFixed = false;
        _touchFixedBody = null;

        // A gun's kick shoves where the hand reaches for, back along the hand and nose up, and fades away.
        float recovery = MathF.Exp(-KickRecovery * dt);
        _kickBack *= recovery;
        _kickPitch *= recovery;

        if (!ComputeTarget(out Float3 target, out Quaternion targetRotation))
        {
            // A moment without tracking keeps the hand where it was, only a real loss puts it away.
            _untracked += dt;
            Relax();
            if (_untracked > LostTrackingGrace) Park();
            return;
        }
        _untracked = 0f;

        float limit = IsHolding ? HeldMaxSeparation : MaxSeparation;
        _apart = Float3.Distance(_rigidbody.Position, target) > limit ? _apart + dt : 0f;
        if (!_placed || _apart > SeparationTime)
        {
            // Snagged too far from the controller, the hand jumps back rather than dragging along. An item it holds
            // alone comes with it, anything else it lets go.
            _apart = 0f;
            if (IsClimbing || _target.IsValid() || (_held.IsValid() && _held.Holders.Count > 1)) Release();
            JumpTo(target, targetRotation);
            _placed = true;
            Relax();
            return;
        }

        // The hand is pulled to its controller by a spring and a damper on its position and on its rotation, relative to
        // the torso, the controller's own motion fed in as the speed to match. Whatever it holds is jointed to it and
        // simply comes along, and whatever it holds on to or presses pushes back on the body.
        Quaternion toTorso = Quaternion.Inverse(Body.Torso.Rotation);
        HandDrive feel = CurrentDrive();
        _drive!.TargetPosition = toTorso * (target - Body.Torso.Position);
        _drive.TargetRotation = toTorso * targetRotation;
        _drive.TargetVelocity = toTorso * _frameVelocity;
        _drive.TargetAngularVelocity = toTorso * _frameSpin;
        _drive.PositionSpring = HandDrive.Spring(HandMass, feel.Frequency);
        _drive.PositionDamper = HandDrive.Damper(HandMass, feel.Frequency, feel.DampingRatio);
        _drive.MaximumForce = feel.MaxForce;
        _drive.RotationSpring = HandDrive.Spring(WristInertia, feel.TurnFrequency);
        _drive.RotationDamper = HandDrive.Damper(WristInertia, feel.TurnFrequency, feel.TurnDampingRatio);
        _drive.MaximumTorque = feel.MaxTorque;

        if (IsPulling) UpdatePull(dt);
        else if (_held.IsValid() && !_gripLocked) DragAlongLine(_held.Body, dt);

        // Pushing on the world, the feet stop holding the body in place, so the push can move it.
        bool againstWorld = IsClimbing || _touchingSolid || (_held.IsValid() && _held.IsTouchingSolid);
        if (againstWorld && SidewaysPush(dt) > BraceThreshold) Body.MarkBraced();
    }

    /// <summary>
    /// Where the controller puts the hand for the physics step: the tracked pose moved into the simulated body's frame,
    /// with any gun kick on top.
    /// </summary>
    private bool ComputeTarget(out Float3 target, out Quaternion rotation)
    {
        if (!TryGetTarget(out target, out rotation)) return false;

        // The view rides the body's drawn pose, which trails the simulation by part of a step and by a different part
        // every frame. Moved into the simulated body's frame, the target stays still relative to the body.
        if (TargetOverride == null) target += Body.Torso.Position - Body.ViewedEyes;
        target -= rotation * Float3.UnitZ * _kickBack;
        rotation = rotation * Quaternion.AxisAngle(Float3.UnitX, -_kickPitch * Maths.Deg2Rad);
        return true;
    }

    /// <summary>How the hand follows its controller right now: hanging on to the world, carrying something alone, sharing it, bracing it, or free.</summary>
    private HandDrive CurrentDrive()
    {
        if (IsClimbing) return Hang;
        if (_held.IsNotValid()) return Free;
        if (_held.Holders.Count < 2) return _held.CarryDrive ?? Carry;
        if (_heldPoint is { Support: true }) return Support;
        return _held.SharedDrive ?? Shared;
    }

    /// <summary>Kicks the hand back by <paramref name="back"/> metres and its nose up by <paramref name="pitch"/> degrees, as a gun firing in it does.</summary>
    public void AddRecoil(float back, float pitch)
    {
        _kickBack = MathF.Min(_kickBack + back, 0.12f);
        _kickPitch = MathF.Min(_kickPitch + pitch, 35f);
    }

    /// <summary>Joins the hand to the torso with its drive, once both exist.</summary>
    private void EnsureDrive()
    {
        if (_drive.IsValid()) return;
        var go = new GameObject("Hand Drive");
        go.Enabled = false;
        go.SetParent(GameObject);
        go.Transform.LocalPosition = Float3.Zero;
        _drive = go.AddComponent<DriveConstraint>();
        _drive.ConnectedBody = Body.Torso;
        _drive.MaximumForce = 0f;
        _drive.MaximumTorque = 0f;
        go.Enabled = true;
    }

    /// <summary>Lets the hand go limp, for while it has no controller to follow or is being put back on it.</summary>
    private void Relax()
    {
        if (_drive.IsNotValid()) return;
        _drive.MaximumForce = 0f;
        _drive.MaximumTorque = 0f;
    }

    /// <summary>How hard, in newtons, the hand pushed the torso across the ground on the last step.</summary>
    private float SidewaysPush(float dt)
    {
        if (_drive.IsNotValid()) return 0f;
        Float3 impulse = _drive.Impulse;
        float substep = dt / Maths.Max(1, GameObject.Scene.Physics.Substep);
        return MathF.Sqrt(impulse.X * impulse.X + impulse.Z * impulse.Z) / substep;
    }

    private void DragAlongLine(Rigidbody3D item, float dt)
    {
        if (_heldPoint == null) return;
        Float3 axis = Float3.Normalize(item.Rotation * (_heldPoint.LineEnd - _heldPoint.LineStart));
        Float3 grip = GripPoint;

        float slide = Float3.Dot(item.GetPointVelocity(grip) - _rigidbody.GetPointVelocity(grip), axis);
        item.LinearVelocity -= axis * slide * MathF.Min(1f, SlideFriction * dt);

        float twist = Float3.Dot(item.AngularVelocity - _rigidbody.AngularVelocity, axis);
        item.AngularVelocity -= axis * twist * MathF.Min(1f, TwistFriction * dt);
    }

    /// <summary>
    /// Puts the hand back on its controller, swept out from the body so it stops at whatever is in between rather
    /// than landing inside a wall.
    /// </summary>
    private void JumpTo(Float3 position, Quaternion rotation)
    {
        Float3 from = Body.Torso.Position;
        Float3 toward = position - from;
        float distance = Float3.Length(toward);
        if (distance > 1e-3f && GameObject.Scene.IsValid())
        {
            var ignore = new HashSet<Rigidbody3D>(Body.IgnoredBodies) { _rigidbody };
            if (GameObject.Scene.Physics.SphereCast(from, 0.05f, toward / distance, distance, out ShapeCastHit hit, QueryFilter.Default.Ignoring(ignore)))
                position = from + toward / distance * MathF.Max(0f, hit.Distance - 0.02f);
        }

        _rigidbody.MotionType = Jitter2.Dynamics.MotionType.Dynamic;
        MoveWithHeld(position, rotation);
    }

    /// <summary>Puts the hand at a pose at rest, taking along whatever it holds as if the two were one piece.</summary>
    private void MoveWithHeld(Float3 position, Quaternion rotation)
    {
        Float3 from = _rigidbody.Position;
        Quaternion turn = rotation * Quaternion.Inverse(_rigidbody.Rotation);
        _rigidbody.MovePosition(position);
        _rigidbody.MoveRotation(rotation);
        _rigidbody.LinearVelocity = Float3.Zero;
        _rigidbody.AngularVelocity = Float3.Zero;

        // With two hands on it, the first one moves it and the second only itself.
        if (_held.IsNotValid() || _held.Holders[0] != this) return;
        Rigidbody3D item = _held.Body;
        item.MovePosition(position + turn * (item.Position - from));
        item.MoveRotation(turn * item.Rotation);
        item.LinearVelocity = Float3.Zero;
        item.AngularVelocity = Float3.Zero;
        _held.ForgetMotion();
    }

    /// <summary>Moves the hand and what it holds along with the body when the body teleports, letting go of anything fixed.</summary>
    private void OnTeleported(Float3 shift)
    {
        if (!_placed) return;
        if (IsClimbing || _target.IsValid()) Release();
        MoveWithHeld(_rigidbody.Position + shift, _rigidbody.Rotation);
    }

    /// <summary>Without a tracked controller the hand is put away below the world, where it touches nothing.</summary>
    private void Park()
    {
        Release();
        Relax();
        if (_rigidbody.MotionType == Jitter2.Dynamics.MotionType.Kinematic) return;
        _rigidbody.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        _rigidbody.MovePosition(new Float3(0f, -100f, 0f));
        _placed = false;
    }

    /// <summary>
    /// How fast the controller moves relative to the tracking space, which moves with the body, and how fast it turns.
    /// The motors drive the hand relative to the torso, so this is the motion they add on top of following the body.
    /// </summary>
    private void MeasureController(bool tracked, Float3 target, Quaternion rotation)
    {
        float dt = Time.DeltaTime;
        Float3 anchor = TargetOverride != null ? Body.Torso.Position : Origin.Position;
        Float3 relative = target - anchor;
        if (!tracked || !_hasFrame || dt <= 0f)
        {
            _frameVelocity = _frameSpin = Float3.Zero;
            _hasFrame = tracked;
        }
        else
        {
            _frameVelocity = (relative - _frameRelative) / dt;
            _frameSpin = AngularVelocityTo(_frameRotation, rotation, dt);
        }
        _frameRelative = relative;
        _frameRotation = rotation;
    }

    public override void OnCollisionBegin(Collision collision) => Touch(collision);
    public override void OnCollisionStay(Collision collision) => Touch(collision);

    private void Touch(Collision collision)
    {
        _touched |= Grabbable.IsSolid(collision);
        // Things to pick up are never held on to like a ledge, even sitting still in a rack.
        Rigidbody3D other = collision.Rigidbody;
        if (other.IsValid() && (other.MotionType == Jitter2.Dynamics.MotionType.Dynamic || other.GetComponent<Grabbable>().IsValid())) return;
        _touchFixed = true;
        _touchFixedBody = other.IsValid() ? other : null;
    }

    /// <summary>Whether the hand itself pressed into something solid on the last step.</summary>
    public bool IsTouchingSolid => _touchingSolid;

    public override void Update()
    {
        bool tracked = TryGetTarget(out Float3 target, out Quaternion targetRotation);
        MeasureController(tracked, target, targetRotation);
        bool shown = tracked || _untracked <= LostTrackingGrace;
        if (_model.Enabled != shown) _model.Enabled = shown;

        bool apart = tracked && Float3.Distance(_rigidbody.Transform.Position, target) > GhostDistance;
        if (_ghost.Enabled != apart) _ghost.Enabled = apart;
        _ghost.Transform.Position = target;
        _ghost.Transform.Rotation = targetRotation;
        if (!tracked) return;

        float trigger = XRInput.GetTrigger(Hand);
        float grip = GripValue;
        bool thumbDown = grip > 0.5f || XRInput.GetButton(Hand, XRButton.Primary) || XRInput.GetButton(Hand, XRButton.Secondary);
        float ease = MathF.Min(1f, Time.DeltaTime * 20f);
        float holding = IsBusy ? 0.75f : 0f;
        _curlIndex += (MathF.Max(trigger, holding) - _curlIndex) * ease;
        _curlGrip += (MathF.Max(grip, holding) - _curlGrip) * ease;
        _curlThumb += ((thumbDown ? 1f : 0f) - _curlThumb) * ease;
        Curl(_modelAnimator, _curlThumb);
        Curl(_ghostAnimator, 0f);

        // A squeeze that finds nothing in reach keeps trying for a moment, so something moving or thrown can still be caught.
        bool gripping = grip > (_wasGripping ? ReleaseSqueeze : GrabSqueeze);
        if (gripping && !_wasGripping) _grabWindow = TryGrab() ? 0f : GrabWindow;
        else if (gripping && _grabWindow > 0f)
        {
            _grabWindow -= Time.DeltaTime;
            if (TryGrab()) _grabWindow = 0f;
        }
        else if (!gripping && _wasGripping) Release();
        _wasGripping = gripping;

        // A line grip holds tight, and slides along the line and turns about it while the trigger is held.
        if (IsHolding && !IsPulling && _heldPoint?.Kind == GrabKind.Line)
        {
            bool lockLine = !Sliding;
            if (lockLine != _gripLocked) Attach(lockLine);
        }
    }

    // ----------------------------------------------------------------
    //  Grabbing
    // ----------------------------------------------------------------

    /// <summary>
    /// Takes hold of whatever is nearest the grip and within reach: an item, a target like a slide or a string, or
    /// something to climb. False when there is nothing to hold.
    /// </summary>
    public bool TryGrab()
    {
        if (IsBusy) return false;
        Float3 grip = GripPoint;
        float bestDistance = GrabRange;

        Grabbable? item = null;
        GrabPoint? itemPoint = null;
        foreach (Grabbable candidate in Grabbable.All)
        {
            if (candidate.Holders.Contains(this)) continue;
            float distance = candidate.DistanceTo(grip, out GrabPoint? point);
            if (distance < bestDistance)
            {
                item = candidate;
                itemPoint = point;
                bestDistance = distance;
            }
        }

        HandTarget? target = null;
        foreach (HandTarget candidate in HandTarget.All)
        {
            if (candidate.User.IsValid()) continue;
            float distance = candidate.DistanceTo(this, grip);
            if (distance < bestDistance)
            {
                target = candidate;
                item = null;
                bestDistance = distance;
            }
        }

        if (target.IsValid())
        {
            Use(target);
            return true;
        }
        if (item.IsValid())
        {
            Take(item, itemPoint, grip);
            return true;
        }

        // Anything fixed the hand is pressed against or right next to can be held: a ledge, a rung, a wall, a table edge.
        if (_touchedFixed)
        {
            Climb(_touchedFixedBody);
            return true;
        }
        if (FindHold(grip, out Rigidbody3D? hold))
        {
            Climb(hold);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Looks a grab's reach out from the grip in every direction of the hand for something fixed to hold on to:
    /// static geometry, or a body that moves on its own but is not an item to pick up, like a platform.
    /// </summary>
    private bool FindHold(Float3 grip, out Rigidbody3D? hold)
    {
        hold = null;
        var ignore = new HashSet<Rigidbody3D>(Body.IgnoredBodies) { _rigidbody };
        QueryFilter filter = QueryFilter.Default.Ignoring(ignore);
        Quaternion rotation = _rigidbody.Rotation;
        Float3[] directions = [Float3.UnitX, -Float3.UnitX, Float3.UnitY, -Float3.UnitY, Float3.UnitZ, -Float3.UnitZ];
        float nearest = float.MaxValue;
        bool found = false;
        foreach (Float3 local in directions)
        {
            if (!GameObject.Scene.Physics.Raycast(grip, rotation * local, out RaycastHit hit, GrabRange, filter) || hit.Distance >= nearest) continue;
            Rigidbody3D? body = hit.Rigidbody;
            if (body.IsValid() && (body.MotionType == Jitter2.Dynamics.MotionType.Dynamic || body.GetComponent<Grabbable>().IsValid())) continue;
            nearest = hit.Distance;
            hold = body.IsValid() ? body : null;
            found = true;
        }
        return found;
    }

    /// <summary>Closes the hand on an item flying into it, from a little further than a grab reaches, pulling it into the grip.</summary>
    public bool Catch(Grabbable item)
    {
        if (IsBusy) return false;
        Float3 grip = GripPoint;
        item.DistanceTo(grip, out GrabPoint? point);
        if (point == null)
        {
            Rigidbody3D body = item.Body;
            Float3 away = body.Position - grip;
            float distance = Float3.Length(away);
            if (distance > 1e-4f) body.MovePosition(grip + away / distance * MathF.Min(distance, item.Reach * 0.8f));
        }
        item.Body.LinearVelocity = _rigidbody.LinearVelocity;
        Take(item, point, grip);
        return true;
    }

    private void Take(Grabbable item, GrabPoint? point, Float3 grip)
    {
        if (item.InSocket.IsValid()) item.InSocket.Remove();
        if (Stabber.IsStuckIn(item)) point = null;

        _held = item;
        _heldPoint = point;
        item.Holders.Add(this);
        IgnorePlayer(item.Body);
        Body.IgnoreWhileHeld(item.Body);

        // Neither hand bumps an item while it is held, so the other hand can reach in for a second grip, a slide or a string.
        foreach (PhysicsHand hand in s_hands)
            GameObject.Scene.Physics.IgnoreCollisionBetween(hand._rigidbody, item.Body);

        // A light item nobody else holds is pulled into the hand's grip. A second hand moves onto its grip on the
        // item instead, and anything else is held where the hand met it.
        if (item.Holders.Count == 1) item.ForgetMotion();
        if (point != null && item.Holders.Count == 1 && item.Body.Mass <= item.MaxSnapMass)
            StartPull(item, point, grip);
        else
        {
            if (point != null && item.Holders.Count > 1) PlaceOn(item, point, grip);
            Attach(_heldPoint?.Kind != GrabKind.Line || !Sliding);
            item.PivotOnGrips();
        }
        XRInput.Vibrate(Hand, 0.35f, 0.04f);
    }

    /// <summary>Starts working a target, such as a slide or a bowstring, with this hand.</summary>
    public void Use(HandTarget target)
    {
        Release();
        _target = target;
        if (target.Body.IsValid()) GameObject.Scene.Physics.IgnoreCollisionBetween(_rigidbody, target.Body);
        target.Begin(this);
        XRInput.Vibrate(Hand, 0.3f, 0.03f);
    }

    /// <summary>Holds on to fixed or moving geometry, pinned to it where the hand is, so pulling on it pulls the body.</summary>
    private void Climb(Rigidbody3D? anchor)
    {
        _climbing = true;
        _grip = new GameObject("Climb Grip");
        _grip.Enabled = false;
        _grip.SetParent(GameObject);
        _grip.Transform.LocalPosition = Float3.Zero;

        var socket = _grip.AddComponent<BallSocketConstraint>();
        socket.Anchor = GripLocal;
        socket.ConnectedBody = anchor;
        var angle = _grip.AddComponent<FixedAngleConstraint>();
        angle.ConnectedBody = anchor;
        _grip.Enabled = true;

        _rigidbody.LinearVelocity = anchor.IsValid() ? anchor.GetPointVelocity(GripPoint) : Float3.Zero;
        _rigidbody.AngularVelocity = Float3.Zero;
        XRInput.Vibrate(Hand, 0.3f, 0.04f);
    }

    /// <summary>
    /// Where the item sits once its grab point is in the hand's grip, in the hand's space: a point as it is set, a line
    /// turned the least it needs to run through the grip.
    /// </summary>
    private void HoldPose(Rigidbody3D item, GrabPoint point, Float3 grip, out Float3 position, out Quaternion rotation)
    {
        Quaternion toHand = Quaternion.Inverse(_rigidbody.Rotation);
        Float3 held;
        if (point.Kind == GrabKind.Point)
        {
            rotation = Quaternion.Inverse(point.Rotation);
            held = point.Position;
        }
        else
        {
            Float3 along = Float3.Normalize(toHand * (item.Rotation * (point.LineEnd - point.LineStart)));
            Float3 handAxis = Float3.Dot(along, Float3.UnitY) < 0f ? -Float3.UnitY : Float3.UnitY;
            rotation = Quaternion.FromToRotation(along, handAxis) * (toHand * item.Rotation);
            held = point.Nearest(Quaternion.Inverse(item.Rotation) * (grip - item.Position));
        }
        position = GripLocal - rotation * held;
    }

    private bool IsPulling => _pull.IsValid();

    /// <summary>
    /// Starts drawing the item into the grip with a drive from the hand aimed straight at where it is held. The drive
    /// starts slack and tightens over a moment, so the item eases in rather than jumping, and is pushed by whatever it
    /// meets on the way, turning up off a table instead of passing into it. It is welded once it has settled in place.
    /// </summary>
    private void StartPull(Grabbable item, GrabPoint point, Float3 grip)
    {
        HoldPose(item.Body, point, grip, out _pullTo, out _pullToRotation);
        _pullTightness = 0f;
        _pullTime = 0f;

        _pull = new GameObject("Pull");
        _pull.Enabled = false;
        _pull.SetParent(item.GameObject);
        _pull.Transform.LocalPosition = Float3.Zero;
        _pullDrive = _pull.AddComponent<DriveConstraint>();
        _pullDrive.ConnectedBody = _rigidbody;
        _pullDrive.TargetPosition = _pullTo;
        _pullDrive.TargetRotation = _pullToRotation;
        TightenPull(item.Body);
        _pull.Enabled = true;
    }

    /// <summary>Sets the pull's drive for how far it has tightened: from a slow, gentle draw to a firm hold, critically damped throughout.</summary>
    private void TightenPull(Rigidbody3D item)
    {
        float ease = _pullTightness * _pullTightness * (3f - 2f * _pullTightness);
        float frequency = SlackPullFrequency + (FirmPullFrequency - SlackPullFrequency) * ease;
        Float3 inertia = item.InertiaTensor;
        float turnInertia = MathF.Max(inertia.X, MathF.Max(inertia.Y, inertia.Z));

        _pullDrive!.PositionSpring = HandDrive.Spring(item.Mass, frequency);
        _pullDrive.PositionDamper = HandDrive.Damper(item.Mass, frequency, 1f);
        _pullDrive.MaximumForce = Maths.Clamp(item.Mass * 150f, 20f, 300f) * (0.3f + 0.7f * ease);
        _pullDrive.RotationSpring = HandDrive.Spring(turnInertia, frequency);
        _pullDrive.RotationDamper = HandDrive.Damper(turnInertia, frequency, 1f);
        _pullDrive.MaximumTorque = Maths.Clamp(item.Mass * 10f, 2f, 30f) * (0.3f + 0.7f * ease);
    }

    private void UpdatePull(float dt)
    {
        Rigidbody3D item = _held!.Body;
        _pullTightness = MathF.Min(1f, _pullTightness + dt / PullTightenTime);
        _pullTime += dt;
        TightenPull(item);

        Quaternion toHand = Quaternion.Inverse(_rigidbody.Rotation);
        float off = Float3.Distance(toHand * (item.Position - _rigidbody.Position), _pullTo);
        float turn = Float3.Length(AngularVelocityTo(toHand * item.Rotation, _pullToRotation, 1f));
        bool settled = off <= SettledDistance && turn <= SettledAngle;
        if (!settled && _pullTime < MaxPullTime) return;

        if (!settled)
        {
            item.MoveRotation(_rigidbody.Rotation * _pullToRotation);
            item.MovePosition(_rigidbody.Position + _rigidbody.Rotation * _pullTo);
            item.LinearVelocity = _rigidbody.LinearVelocity;
            item.AngularVelocity = _rigidbody.AngularVelocity;
            _held.ForgetMotion();
        }

        // In place in the grip, so the weld takes it as it is, with nothing left to close.
        EndPull();
        Attach(_heldPoint?.Kind != GrabKind.Line || !Sliding);
        _held.PivotOnGrips();
    }

    private void EndPull()
    {
        if (_pull.IsNotValid()) return;
        _pull.Enabled = false;
        _pull.Destroy();
        _pull = null;
        _pullDrive = null;
    }

    /// <summary>Moves the hand onto its grab point on an item another hand already holds, a line turning the hand the least it can.</summary>
    private void PlaceOn(Grabbable item, GrabPoint point, Float3 grip)
    {
        Rigidbody3D body = item.Body;
        Quaternion rotation;
        Float3 at;
        if (point.Kind == GrabKind.Point)
        {
            rotation = body.Rotation * point.Rotation;
            at = body.Position + body.Rotation * point.Position;
        }
        else
        {
            Float3 along = Float3.Normalize(body.Rotation * (point.LineEnd - point.LineStart));
            Float3 handAxis = _rigidbody.Rotation * Float3.UnitY;
            if (Float3.Dot(along, handAxis) < 0f) along = -along;
            rotation = Quaternion.FromToRotation(handAxis, along) * _rigidbody.Rotation;
            at = body.Position + body.Rotation * point.Nearest(Quaternion.Inverse(body.Rotation) * (grip - body.Position));
        }

        _rigidbody.MoveRotation(rotation);
        _rigidbody.MovePosition(at - rotation * GripLocal);
        _rigidbody.LinearVelocity = body.GetPointVelocity(at);
        _rigidbody.AngularVelocity = body.AngularVelocity;
    }

    /// <summary>
    /// Joins the held item to the hand: welded in place, or for a line grip that is not locked, kept on the line but
    /// free to slide along it and turn about it, with a little friction both ways.
    /// </summary>
    private void Attach(bool locked)
    {
        DetachGrip();
        if (_held.IsNotValid()) return;

        Rigidbody3D item = _held.Body;
        _gripLocked = locked;

        // The joint is set up on an inactive child of the item and switched on once, so it is built against the
        // poses as they are now, already joined to the hand.
        _grip = new GameObject("Grip");
        _grip.Enabled = false;
        _grip.SetParent(item.GameObject);
        _grip.Transform.LocalPosition = Float3.Zero;

        Float3 grip = GripPoint;
        if (_heldPoint?.Kind == GrabKind.Line && !locked)
        {
            Float3 start = _heldPoint.LineStart, end = _heldPoint.LineEnd;
            Float3 axis = Float3.Normalize(end - start);
            float half = Float3.Length(end - start) * 0.5f;

            var line = _grip.AddComponent<PointOnLineConstraint>();
            line.LineAxis = axis;
            line.Anchor1 = (start + end) * 0.5f;
            line.Anchor2 = GripLocal;
            line.MinDistance = -half;
            line.MaxDistance = half;
            line.ConnectedBody = _rigidbody;

            var twist = _grip.AddComponent<HingeAngleConstraint>();
            twist.HingeAxis = axis;
            twist.ConnectedBody = _rigidbody;
        }
        else
        {
            var socket = _grip.AddComponent<BallSocketConstraint>();
            socket.Anchor = Quaternion.Inverse(item.Rotation) * (grip - item.Position);
            socket.ConnectedBody = _rigidbody;
            _grip.AddComponent<FixedAngleConstraint>().ConnectedBody = _rigidbody;
        }

        _grip.Enabled = true;
    }

    private void DetachGrip()
    {
        if (_grip.IsNotValid()) return;
        _grip.Enabled = false;
        _grip.Destroy();
        _grip = null;
    }

    /// <summary>
    /// Lets go of whatever the hand has. A held item let go near a socket that fits goes into it, anything else leaves
    /// with the motion it had over the last few steps, so it can be thrown.
    /// </summary>
    public void Release(bool intoSocket = true)
    {
        EndPull();
        DetachGrip();
        _climbing = false;

        if (_target.IsValid())
        {
            HandTarget target = _target;
            _target = null;
            target.End(this);
            if (target.Body.IsValid()) ClearAfterRelease(target.Body);
        }

        if (_held.IsNotValid())
        {
            _held = null;
            return;
        }

        Grabbable item = _held;
        Socket? socket = intoSocket && item.Holders.Count == 1 ? Socket.Near(item) : null;
        item.Holders.Remove(this);
        Body.StopIgnoring(item.Body);
        _held = null;
        _heldPoint = null;
        item.PivotOnGrips();
        if (item.IsHeld) return;

        if (socket.IsValid()) socket.Insert(item);
        else item.Throw();
        foreach (PhysicsHand hand in s_hands)
            hand.ClearAfterRelease(item.Body);
        if (!socket.IsValid()) LetGo?.Invoke(this, item);
    }

    /// <summary>Whatever was just let go may still be inside the fingers, so the two stay out of each other's way until it is clear of them.</summary>
    private void ClearAfterRelease(Rigidbody3D body)
    {
        GameObject.Scene.Physics.IgnoreCollisionBetween(_rigidbody, body);
        if (!_clearing.Contains(body)) _clearing.Add(body);
    }

    private void ClearReleased()
    {
        if (_clearing.Count == 0) return;
        PhysicsWorld physics = GameObject.Scene.Physics;
        Quaternion rotation = _rigidbody.Rotation;
        physics.OverlapBox(_rigidbody.Position + rotation * ClearanceCenter, ClearanceSize, rotation, _overlaps);

        for (int i = _clearing.Count - 1; i >= 0; i--)
        {
            Rigidbody3D body = _clearing[i];
            if (body.IsValid())
            {
                if (Overlaps(body)) continue;
                Grabbable? grabbable = body.GetComponent<Grabbable>();
                bool inUse = (grabbable.IsValid() && grabbable.IsHeld) || (_target.IsValid() && _target.Body == body);
                if (!inUse) physics.EnableCollisionBetween(_rigidbody, body);
            }
            _clearing.RemoveAt(i);
        }
    }

    private bool Overlaps(Rigidbody3D body)
    {
        foreach (ShapeCastHit hit in _overlaps)
            if (hit.Rigidbody == body) return true;
        return false;
    }

    /// <summary>Hands and held items never push the player's own body around.</summary>
    private void IgnorePlayer(Rigidbody3D body)
    {
        if (!s_ignoringPlayer.Add(body)) return;
        PhysicsWorld physics = GameObject.Scene.Physics;
        foreach (Rigidbody3D part in Body.Parts)
            physics.IgnoreCollisionBetween(body, part);
    }

    public override void OnEnable()
    {
        // Hands pass through each other, so one can reach past the other to the same item.
        foreach (PhysicsHand other in s_hands)
            GameObject.Scene.Physics.IgnoreCollisionBetween(_rigidbody, other._rigidbody);
        s_hands.Add(this);
        Body.Teleported += OnTeleported;
        Body.AddHand(_rigidbody);
    }

    public override void OnDisable()
    {
        Body.Teleported -= OnTeleported;
        Body.RemoveHand(_rigidbody);
        Release();
        s_hands.Remove(this);
        if (_ghost.IsValid()) _ghost.Enabled = false;
    }

    // ----------------------------------------------------------------
    //  Helpers
    // ----------------------------------------------------------------

    /// <summary>The angular velocity that turns <paramref name="from"/> into <paramref name="to"/> over <paramref name="dt"/> seconds.</summary>
    public static Float3 AngularVelocityTo(Quaternion from, Quaternion to, float dt)
    {
        Quaternion delta = Quaternion.Normalize(to * Quaternion.Inverse(from));
        if (delta.W < 0f) delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        float angle = 2f * MathF.Acos(Maths.Clamp(delta.W, -1f, 1f));
        float sine = MathF.Sqrt(MathF.Max(0f, 1f - delta.W * delta.W));
        return sine > 1e-5f ? new Float3(delta.X, delta.Y, delta.Z) / sine * (angle / dt) : Float3.Zero;
    }
}

/// <summary>
/// Gravity gloves. An empty hand pointing at something to pick up, in sight and within <see cref="MaxDistance"/>, shows
/// a faint line to it. Squeezing the grip picks it out, and a flick of the hand back toward the body throws it up on
/// an arc that lands in the hand, which catches it while the grip is still held.
/// </summary>
public sealed class GravityGlove : Component
{
    public PhysicsHand Hand = null!;
    public float MaxDistance = 8f;
    public float MaxAngle = 14f;

    /// <summary>How fast the hand has to move back toward the body, in metres per second, to count as a flick.</summary>
    public float FlickSpeed = 1.6f;

    private Grabbable? _aimed;
    private Grabbable? _picked;
    private Grabbable? _incoming;
    private float _incomingTime, _flightTime;
    private bool _incomingGravity;
    private float _pressedAt = -10f;
    private const float Gravity = 9.81f;
    private const float TurnFrom = 0.3f;
    private const float CatchWindow = 0.35f;
    private bool _wasGripping;
    private LineRenderer _line = null!;
    private readonly HashSet<Rigidbody3D> _ignore = new();

    private static readonly Color Aiming = new(0.5f, 0.8f, 1f, 0.35f);
    private static readonly Color Picked = new(1f, 0.7f, 0.3f, 0.9f);

    public override void OnEnable()
    {
        if (_line != null) return;
        var go = new GameObject("Gravity Glove Line");
        _line = go.AddComponent<LineRenderer>();
        _line.Material = new Material(Shader.LoadDefault(DefaultShader.Line));
        _line.StartWidth = 0.004f;
        _line.EndWidth = 0.01f;
        GameObject.Scene.Add(go);
    }

    public override void Update()
    {
        _line.Points.Clear();
        if (!XR.IsRunning && Hand.TargetOverride == null) return;

        bool gripping = Hand.GripValue > (_wasGripping ? PhysicsHand.ReleaseSqueeze : PhysicsHand.GrabSqueeze);
        bool pressed = gripping && !_wasGripping;
        _wasGripping = gripping;
        if (pressed) _pressedAt = Time.TimeSinceStartup;

        if (Hand.IsBusy || !gripping) _picked = null;

        if (_picked.IsNotValid() && _incoming.IsNotValid() && !Hand.IsBusy)
        {
            Grabbable? aimed = FindAimed();
            if (aimed != _aimed && aimed.IsValid()) XRInput.Vibrate(Hand.Hand, 0.1f, 0.01f);
            _aimed = aimed;

            // A squeeze that finds nothing in reach picks out whatever the hand points at.
            if (pressed && _aimed.IsValid() && !Hand.IsHolding)
            {
                _picked = _aimed;
                XRInput.Vibrate(Hand.Hand, 0.3f, 0.03f);
            }
        }

        Grabbable? shown = _picked.IsValid() ? _picked : _aimed;
        if (shown.IsValid() && !Hand.IsBusy && _incoming.IsNotValid())
        {
            _line.StartColor = _line.EndColor = _picked.IsValid() ? Picked : Aiming;
            _line.Points.Add(Hand.GripPoint);
            _line.Points.Add(shown.Body.Position);
        }
    }

    public override void FixedUpdate()
    {
        if (_picked.IsValid() && IsFlick()) Launch(_picked);

        if (_incoming.IsNotValid()) return;

        // The hand closes on it once it is near enough, with the grip held or squeezed just before or after it arrives.
        // It gives up if it runs out of time, or hits something once well on its way.
        float dt = Time.FixedDeltaTime;
        _incomingTime += dt;
        float remaining = _flightTime - _incomingTime;
        bool wantsIt = _wasGripping || Time.TimeSinceStartup - _pressedAt < CatchWindow;
        bool blocked = _incomingTime > _flightTime * TurnFrom && _incoming.HitSomething;
        if (Hand.IsBusy || blocked || remaining < -CatchWindow) EndFlight();
        else if (wantsIt && _incoming.DistanceTo(Hand.GripPoint, out _) < Hand.GrabRange * 2.5f && Hand.Catch(_incoming)) EndFlight();
        else Steer(_incoming, MathF.Max(remaining, 2f * dt));
    }

    /// <summary>
    /// Sets the item on the arc that reaches the hand in <paramref name="time"/> seconds, aimed again every step, so it
    /// follows the hand as it moves. Partway in it starts turning to the way the hand will hold it, done as it arrives.
    /// </summary>
    private void Steer(Grabbable item, float time)
    {
        Rigidbody3D body = item.Body;
        Float3 to = Hand.GripPoint + new Float3(0f, 0.1f * MathF.Min(1f, time / _flightTime), 0f);
        body.LinearVelocity = (to - body.Position) / time + new Float3(0f, 0.5f * Gravity * time, 0f);

        body.AngularVelocity = Float3.Zero;
        if (_incomingTime < _flightTime * TurnFrom) return;
        item.DistanceTo(Hand.GripPoint, out GrabPoint? point);
        if (point is not { Kind: GrabKind.Point }) return;
        Quaternion held = Hand.Rigidbody.Rotation * Quaternion.Inverse(point.Rotation);
        body.AngularVelocity = PhysicsHand.AngularVelocityTo(body.Rotation, held, time);
    }

    private void EndFlight()
    {
        if (_incoming.IsValid()) _incoming.Body.AffectedByGravity = _incomingGravity;
        _incoming = null;
    }

    /// <summary>The hand moving back toward the body, or up, fast enough to flick.</summary>
    private bool IsFlick()
    {
        Float3 relative = Hand.Rigidbody.LinearVelocity - Hand.Body.Torso.LinearVelocity;
        Float3 toBody = Hand.Body.Torso.Position - Hand.GripPoint;
        toBody = new Float3(toBody.X, 0f, toBody.Z);
        float back = Float3.LengthSquared(toBody) > 1e-4f ? Float3.Dot(relative, Float3.Normalize(toBody)) : 0f;
        return back > FlickSpeed || relative.Y > FlickSpeed;
    }

    /// <summary>Sends the item flying to the hand, sooner for nearer things, steered by the glove the whole way.</summary>
    private void Launch(Grabbable item)
    {
        _picked = null;
        _aimed = null;
        EndFlight();
        Rigidbody3D body = item.Body;
        if (item.InSocket.IsValid()) item.InSocket.Remove();

        _incoming = item;
        _incomingTime = 0f;
        _flightTime = Maths.Clamp(Float3.Distance(Hand.GripPoint, body.Position) / 8f, 0.35f, 0.8f);
        _incomingGravity = body.AffectedByGravity;
        body.AffectedByGravity = false;
        Steer(item, _flightTime);
        XRInput.Vibrate(Hand.Hand, 0.5f, 0.05f);
    }

    public override void OnDisable() => EndFlight();

    /// <summary>The loose item nearest the line the hand points along, within the cone and in plain sight.</summary>
    private Grabbable? FindAimed()
    {
        Float3 origin = Hand.GripPoint;
        Float3 direction = Hand.AimDirection;

        Grabbable? best = null;
        float bestScore = float.MaxValue;
        float minCos = MathF.Cos(MaxAngle * Maths.Deg2Rad);
        foreach (Grabbable candidate in Grabbable.All)
        {
            if (candidate.IsHeld || candidate.Body.MotionType == Jitter2.Dynamics.MotionType.Static) continue;
            if (candidate.Body.Mass > 15f) continue;
            Float3 toItem = candidate.Body.Position - origin;
            float distance = Float3.Length(toItem);
            if (distance < 0.3f || distance > MaxDistance) continue;
            float cos = Float3.Dot(toItem / distance, direction);
            if (cos < minCos) continue;

            float score = (1f - cos) * 40f + distance * 0.1f;
            if (score >= bestScore || !InSight(origin, candidate, distance)) continue;
            best = candidate;
            bestScore = score;
        }
        return best;
    }

    private bool InSight(Float3 origin, Grabbable item, float distance)
    {
        _ignore.Clear();
        _ignore.UnionWith(Hand.Body.IgnoredBodies);
        _ignore.Add(Hand.Rigidbody);
        _ignore.Add(item.Body);
        Float3 direction = (item.Body.Position - origin) / distance;
        return !GameObject.Scene.Physics.Raycast(origin, direction, out _, distance - 0.05f, QueryFilter.Default.Ignoring(_ignore));
    }
}
