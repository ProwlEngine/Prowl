// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// A VR player, so it lifts straight into a project:
//   VRBody     the simulated body, a rolling ball, a leg and a torso held together by joints (VRBody.cs).
//   VRRig      the tracking space around the headset and hands. It turns input into what the body should do,
//              and keeps the view riding along with wherever the body actually got to. Without a headset it
//              falls back to mouse and keyboard so the world can still be walked.
//   PhysicsHand  rigidbody hands that push toward the controllers and hold items with joints (VRHands.cs).
//   Grabbable, Socket  what the hands pick up and where it goes (VRInteraction.cs).
//   Stabber, Stabbable, MeleeWeapon, HitFlash  blades, what they go into, and hits (VRMelee.cs).
//

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace VRShowcase;

public enum TurnMode { Snap, Smooth }

public enum MoveDirection { Head, LeftHand }

/// <summary>
/// The tracking space. The rig's own transform is placed and turned in the world so the headset lands on the
/// <see cref="VRBody"/>, <see cref="Origin"/> under it lifts a seated headset to standing height, and the head and
/// hands are children of the origin.
/// <para>
/// Walking around the room moves the head straight away, and the body chases it by rolling there. Whatever the body
/// fails to cover, because a wall or a table is in the way, is taken back off the view, so the head can lean a
/// little way from the body but never through anything. Everything else the body does, stick walking, jumping,
/// riding a platform, being shoved, the view simply rides along with.
/// </para>
/// </summary>
public sealed class VRRig : Component
{
    public Transform Origin = null!;
    public Transform Head = null!;
    public PhysicsHand LeftHand = null!;
    public PhysicsHand RightHand = null!;
    public VRBody Body = null!;

    public bool SmoothLocomotion = true;
    public MoveDirection MoveRelativeTo = MoveDirection.Head;
    public float WalkSpeed = 2.5f;
    public float SprintSpeed = 5f;
    public float JumpHeight = 0.6f;

    public TurnMode Turning = TurnMode.Snap;
    public float SnapAngle = 45f;
    public float SmoothTurnSpeed = 120f;

    /// <summary>Right stick forward aims a teleport, and clicking the stick jumps. Off, pushing the stick up stands and jumps.</summary>
    public bool Teleporting = false;
    public float TeleportLaunchSpeed = 9f;

    /// <summary>The eye height when there is no headset, or when the headset tracks from a seat.</summary>
    public float StandingHeight = 1.65f;

    /// <summary>How far the head may lean away from the body before the view is held back.</summary>
    public float MaxLean = 0.25f;

    /// <summary>How quickly the body closes the gap to the head, per second.</summary>
    public float ChaseRate = 8f;

    /// <summary>How fast the stick crouches and stands, in metres per second.</summary>
    public float CrouchSpeed = 1.5f;

    private const float TeleportGravity = 9.81f;
    private const float TeleportStep = 1f / 30f;
    private const float FallLimit = -25f;
    private const float MaxChaseSpeed = 4f;
    private const float MaxDuckAhead = 0.6f;
    private const float MaxRiseAhead = 0.05f;

    private Float3 _spawn;
    private bool _placed;
    private Float3 _previousHead;
    private Float3 _previousEyes;
    private Float3 _lean;
    private float _crouch;
    // The rates headsets display at, including the halves they drop to when a frame is late.
    private static readonly float[] HeadsetRates = [45f, 60f, 72f, 80f, 90f, 120f, 144f];
    private const float RateSettleTime = 0.5f;
    private float _measuredRate, _stepRate, _candidateRate, _candidateFor;
    private float _fallSpeed;
    private bool _snapArmed = true;
    private bool _upArmed = true;
    private bool _aiming;
    private bool _hasTarget;
    private Float3 _target;
    private float _desktopPitch;
    private LineRenderer _arc = null!;
    private LineRenderer _ring = null!;

    private InputActionMap _actions = null!;
    private InputAction _move = null!;
    private InputAction _turn = null!;
    private InputAction _jump = null!;
    private InputAction _sprint = null!;
    private InputAction _crouchKey = null!;
    private InputAction _respawn = null!;

    public bool IsAiming => _aiming;

    /// <summary>How far the player has crouched with the stick, on top of crouching for real.</summary>
    public float StickCrouch => _crouch;

    /// <summary>Where the head is in the world this frame, from the headset when there is one.</summary>
    public Float3 HeadPosition => Transform.TransformPoint(HeadInRig());

    public override void OnEnable()
    {
        _spawn = Body.FeetPosition;
        if (_arc == null) CreateTeleportVisuals();
        CreateActions();
    }

    public override void OnDisable()
    {
        _actions.Disable();
        Input.UnregisterActionMap(_actions);
    }

    /// <summary>Every control bound to both the headset controllers and the keyboard, so one action serves either.</summary>
    private void CreateActions()
    {
        _actions = new InputActionMap("VR Rig");

        _move = _actions.AddAction("Move", InputActionType.Value);
        _move.ExpectedValueType = typeof(Float2);
        var leftStick = InputBinding.CreateXRStickBinding(XRHand.Left);
        leftStick.Processors.Add(new DeadzoneProcessor(0.15f));
        _move.AddBinding(leftStick);
        _move.AddBinding(new Vector2CompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.W),
            InputBinding.CreateKeyBinding(KeyCode.S),
            InputBinding.CreateKeyBinding(KeyCode.A),
            InputBinding.CreateKeyBinding(KeyCode.D),
            true));

        _turn = _actions.AddAction("Turn", InputActionType.Value);
        _turn.ExpectedValueType = typeof(Float2);
        _turn.AddBinding(InputBinding.CreateXRStickBinding(XRHand.Right));

        _jump = _actions.AddAction("Jump");
        _jump.AddBinding(XRHand.Right, XRButton.Thumbstick);
        _jump.AddBinding(KeyCode.Space);

        _sprint = _actions.AddAction("Sprint");
        _sprint.AddBinding(XRHand.Left, XRButton.Thumbstick);
        _sprint.AddBinding(KeyCode.ShiftLeft);

        _crouchKey = _actions.AddAction("Crouch");
        _crouchKey.AddBinding(KeyCode.C);

        _respawn = _actions.AddAction("Respawn");
        _respawn.AddBinding(XRHand.Left, XRButton.Menu);
        _respawn.AddBinding(KeyCode.R);

        Input.RegisterActionMap(_actions);
        _actions.Enable();
    }

    private void CreateTeleportVisuals()
    {
        var material = new Material(Shader.LoadDefault(DefaultShader.Line));

        var arc = new GameObject("Teleport Arc");
        arc.SetParent(GameObject);
        _arc = arc.AddComponent<LineRenderer>();
        _arc.Material = material;
        _arc.StartWidth = 0.02f;
        _arc.EndWidth = 0.02f;

        var ring = new GameObject("Teleport Target");
        ring.SetParent(GameObject);
        _ring = ring.AddComponent<LineRenderer>();
        _ring.Material = material;
        _ring.StartWidth = 0.03f;
        _ring.EndWidth = 0.03f;
        _ring.Loop = true;
    }

    /// <summary>
    /// The headset rate nearest to how fast frames are arriving, starting from the rate the headset reports. A different
    /// rate takes over only after it has been the nearest for <see cref="RateSettleTime"/>, so one slow frame changes nothing.
    /// </summary>
    private float PaceSteps(float dt)
    {
        float rate = 1f / dt;
        _measuredRate = _measuredRate > 0f ? _measuredRate + (rate - _measuredRate) * 0.1f : MathF.Max(XR.DisplayRefreshRate, 60f);
        if (_stepRate <= 0f) _stepRate = NearestHeadsetRate(_measuredRate);

        float nearest = NearestHeadsetRate(_measuredRate);
        _candidateFor = nearest == _candidateRate ? _candidateFor + dt : 0f;
        _candidateRate = nearest;
        if (nearest != _stepRate && _candidateFor > RateSettleTime) _stepRate = nearest;
        return _stepRate;
    }

    private static float NearestHeadsetRate(float rate)
    {
        float best = HeadsetRates[0];
        foreach (float candidate in HeadsetRates)
            if (MathF.Abs(candidate - rate) < MathF.Abs(best - rate)) best = candidate;
        return best;
    }

    public override void Update()
    {
        float dt = Time.DeltaTime;
        if (dt <= 0f) return;

        bool vr = XR.IsRunning;

        // One physics step for every frame shown, so nothing simulated judders against the display. The step is set to
        // the headset rate the frames are actually arriving at, changing only once a new rate has held for a moment.
        if (vr) Time.FixedDeltaTime = 1f / PaceSteps(dt);

        // A seated headset tracks from the head, so the tracking space is lifted to standing height.
        float originHeight = vr && XR.TrackingOrigin == XRTrackingOrigin.Seated ? StandingHeight : 0f;
        Float3 local = Origin.LocalPosition;
        if (local.Y != originHeight) Origin.LocalPosition = new Float3(local.X, originHeight, local.Z);

        if (!vr) DesktopLook();

        Float2 stick = vr ? _turn.ReadValue<Float2>() : Float2.Zero;
        if (vr)
        {
            UpdateTeleport(stick);
            if (!_aiming) UpdateTurning(stick.X, dt);
        }
        UpdateCrouch(stick, dt);

        if (_jump.WasPressedThisFrame()) Jump();
        if (_respawn.WasPressedThisFrame()) Respawn();

        FollowBody(dt);
        FeelLandings();

        if (Body.FeetPosition.Y < FallLimit) Respawn();
    }

    public void Respawn()
    {
        _aiming = false;
        HideTeleport();
        ForgetLean();
        Body.Teleport(_spawn);
    }

    // ----------------------------------------------------------------
    //  Head and body
    // ----------------------------------------------------------------

    /// <summary>The head in the rig's own space: the headset's pose, or a fixed standing height without one.</summary>
    private Float3 HeadInRig()
    {
        XRPose head = XR.GetPose(XRNode.Head);
        return head.IsValid ? Origin.LocalPosition + head.Position : Origin.LocalPosition + Head.LocalPosition;
    }

    /// <summary>
    /// Drops the gap between the head and the body by pulling the view back over the body. Only zeroing the gap
    /// would leave the view that far off the body for good, and every teleport would add to it.
    /// </summary>
    private void ForgetLean()
    {
        Transform.Position -= _lean;
        _lean = Float3.Zero;
    }

    private void FollowBody(float dt)
    {
        Float3 head = HeadInRig();

        if (!_placed)
        {
            // Start with the head right over the body.
            Float3 eyes = Body.EyePosition;
            Transform.Position += Flat(eyes - Transform.TransformPoint(head));
            _previousHead = head;
            _previousEyes = eyes;
            _placed = true;
        }

        // Stepping around the room moves the view at once and leaves a gap for the body to close.
        Float3 roomStep = Transform.Rotation * Flat(head - _previousHead);
        _lean += roomStep;
        float lean = Float3.Length(_lean);
        if (lean > MaxLean)
        {
            Float3 excess = _lean * ((lean - MaxLean) / lean);
            Transform.Position -= excess;
            _lean -= excess;
        }

        // The body rolls toward the head as well as wherever the stick says. What the chase is expected to
        // cover is taken off the view here and given back below by however far the body really moved, so a
        // blocked body slides the view back over it instead of letting the head pass through the wall.
        Float3 chase = _lean * ChaseRate;
        float chaseSpeed = Float3.Length(chase);
        if (chaseSpeed > MaxChaseSpeed) chase *= MaxChaseSpeed / chaseSpeed;
        Body.DesiredVelocity = StickVelocity() + chase;
        Float3 covered = chase * dt;
        _lean -= covered;
        Transform.Position -= covered;

        Float3 eyesNow = Body.EyePosition;
        Transform.Position += Flat(eyesNow - _previousEyes);
        _previousEyes = eyesNow;
        _previousHead = head;

        // Ducking for real and crouching on the stick both lower the torso.
        Body.TargetEyeHeight = MathF.Max(VRBody.LowestEyes, head.Y - _crouch);

        // The view sits at the body's eyes, so a ceiling, a jump or a ledge moves it exactly as it moves the body. Only
        // the gap left while the spine catches up with the real head is shown at once: ducking straight away, and
        // rising just a little ahead, since a ceiling may be what holds the body down.
        float ahead = Maths.Clamp(Body.TargetEyeHeight - Body.EyeHeight, -MaxDuckAhead, MaxRiseAhead);
        Float3 position = Transform.Position;
        Transform.Position = new Float3(position.X, eyesNow.Y + ahead - head.Y, position.Z);
        Body.ViewedEyes = eyesNow;

        // A turning platform turns the player with it.
        if (MathF.Abs(Body.GroundYawSpeed) > 1e-4f)
            TurnBy(Body.GroundYawSpeed * Maths.Rad2Deg * dt);
    }

    private Float3 StickVelocity()
    {
        if (!SmoothLocomotion) return Float3.Zero;
        Float2 input = _move.ReadValue<Float2>();
        if (Float2.LengthSquared(input) > 1f) input = Float2.Normalize(input);

        Float3 forward = MoveForward();
        Float3 right = Float3.Cross(Float3.UnitY, forward);
        return (forward * input.Y + right * input.X) * (_sprint.IsPressed() ? SprintSpeed : WalkSpeed);
    }

    private Float3 MoveForward()
    {
        Float3 forward = Head.Forward;
        if (MoveRelativeTo == MoveDirection.LeftHand && XR.IsRunning)
        {
            XRPose aim = XR.GetPose(XRNode.LeftHandAim);
            if (aim.IsValid) forward = Origin.Rotation * (aim.Rotation * Float3.UnitZ);
        }

        forward = Flat(forward);
        if (Float3.LengthSquared(forward) < 1e-4f) forward = Flat(Transform.Forward);
        return Float3.Normalize(forward);
    }

    /// <summary>
    /// Holding the right stick down crouches lower and lower. Pushing it up stands straight back up, and pushed up while
    /// already standing it jumps. C crouches while held without a headset.
    /// </summary>
    private void UpdateCrouch(Float2 stick, float dt)
    {
        float deepest = MathF.Max(0f, HeadInRig().Y - VRBody.LowestEyes);

        if (!XR.IsRunning)
        {
            float goal = _crouchKey.IsPressed() ? deepest : 0f;
            _crouch = MoveTowards(_crouch, goal, CrouchSpeed * dt);
            return;
        }

        bool sideways = MathF.Abs(stick.X) > 0.6f;
        if (!_aiming && stick.Y < -0.6f && !sideways) _crouch += CrouchSpeed * dt;

        if (!Teleporting)
        {
            bool up = stick.Y > 0.7f && !sideways;
            if (up && _upArmed)
            {
                if (_crouch > 0.02f) _crouch = 0f;
                else Jump();
            }
            if (up) _upArmed = false;
            else if (stick.Y < 0.3f) _upArmed = true;
        }

        _crouch = Maths.Clamp(_crouch, 0f, deepest);
    }

    /// <summary>Jumps, standing up out of any stick crouch first.</summary>
    private void Jump()
    {
        _crouch = 0f;
        Body.Jump(JumpHeight);
    }

    private void FeelLandings()
    {
        if (!Body.IsGrounded)
        {
            _fallSpeed = MathF.Max(_fallSpeed, -Body.Torso.LinearVelocity.Y);
            return;
        }

        if (_fallSpeed > 4f) Pulse(Maths.Clamp(_fallSpeed / 12f, 0.2f, 1f), 0.08f);
        _fallSpeed = 0f;
    }

    private void DesktopLook()
    {
        Head.LocalPosition = new Float3(0f, StandingHeight, 0f);

        if (Input.GetMouseButtonDown(1)) Input.LockCursor();
        else if (Input.GetMouseButtonUp(1)) Input.UnlockCursor();
        if (!Input.GetMouseButton(1)) return;

        Float2 delta = Input.MouseDelta * 0.2f;
        TurnBy(delta.X);
        _desktopPitch = Maths.Clamp(_desktopPitch + delta.Y, -85f, 85f);
        Head.LocalRotation = Quaternion.FromEuler(_desktopPitch, 0f, 0f);
    }

    // ----------------------------------------------------------------
    //  Turning
    // ----------------------------------------------------------------

    private void UpdateTurning(float stick, float dt)
    {
        if (Turning == TurnMode.Smooth)
        {
            if (MathF.Abs(stick) > 0.2f) TurnBy(stick * SmoothTurnSpeed * dt);
            return;
        }

        // One snap per push, the stick has to come back near the middle before it snaps again.
        if (_snapArmed && MathF.Abs(stick) > 0.75f)
        {
            TurnBy(MathF.Sign(stick) * SnapAngle);
            _snapArmed = false;
        }
        else if (MathF.Abs(stick) < 0.3f)
        {
            _snapArmed = true;
        }
    }

    /// <summary>Turns the whole rig about the head, so turning never slides the view sideways.</summary>
    public void TurnBy(float degrees)
    {
        Float3 pivot = HeadPosition;
        Transform.Rotation = Quaternion.AxisAngle(Float3.UnitY, degrees * Maths.Deg2Rad) * Transform.Rotation;
        Transform.Position += Flat(pivot - HeadPosition);
    }

    // ----------------------------------------------------------------
    //  Teleporting
    // ----------------------------------------------------------------

    /// <summary>Pushing the right stick forward aims, letting it go lands wherever the arc ends, if it ends on ground flat enough to stand on.</summary>
    private void UpdateTeleport(Float2 stick)
    {
        if (!Teleporting) return;

        if (!_aiming && stick.Y > 0.7f && MathF.Abs(stick.X) < 0.6f) _aiming = true;
        if (!_aiming) return;

        XRPose aim = XR.GetPose(XRNode.RightHandAim);
        if (aim.IsValid)
            TraceArc(Origin.TransformPoint(aim.Position), Origin.Rotation * (aim.Rotation * Float3.UnitZ));

        if (stick.Y < 0.3f)
        {
            _aiming = false;
            HideTeleport();
            if (_hasTarget)
            {
                ForgetLean();
                Body.Teleport(_target + Float3.UnitY * 0.02f);
                XRInput.Vibrate(XRHand.Right, 0.3f, 0.05f);
            }
        }
    }

    private void TraceArc(Float3 start, Float3 direction)
    {
        _arc.Points.Clear();
        _ring.Points.Clear();
        _hasTarget = false;

        float minNormalY = MathF.Cos(Body.MaxWalkSlope * Maths.Deg2Rad);
        PhysicsWorld physics = GameObject.Scene.Physics;
        QueryFilter filter = QueryFilter.Default.Ignoring(Body.IgnoredBodies);
        Float3 point = start;
        Float3 velocity = direction * TeleportLaunchSpeed;
        _arc.Points.Add(point);

        for (int i = 0; i < 120 && point.Y > FallLimit; i++)
        {
            Float3 next = point + velocity * TeleportStep;
            velocity.Y -= TeleportGravity * TeleportStep;

            Float3 step = next - point;
            float length = Float3.Length(step);
            if (length > 1e-5f && physics.Raycast(point, step / length, out RaycastHit hit, length, filter))
            {
                _arc.Points.Add(hit.Point);
                _hasTarget = hit.Normal.Y >= minNormalY;
                _target = hit.Point;
                break;
            }

            _arc.Points.Add(next);
            point = next;
        }

        Color color = _hasTarget ? new Color(0.3f, 1f, 0.5f, 1f) : new Color(1f, 0.3f, 0.25f, 1f);
        _arc.StartColor = new Color(color.R, color.G, color.B, 0.1f);
        _arc.EndColor = color;
        if (!_hasTarget) return;

        _ring.StartColor = _ring.EndColor = color;
        for (int i = 0; i < 32; i++)
        {
            float angle = i / 32f * MathF.PI * 2f;
            _ring.Points.Add(_target + new Float3(MathF.Cos(angle) * 0.3f, 0.03f, MathF.Sin(angle) * 0.3f));
        }
    }

    private void HideTeleport()
    {
        _arc.Points.Clear();
        _ring.Points.Clear();
        _hasTarget = false;
    }

    // ----------------------------------------------------------------
    //  Helpers
    // ----------------------------------------------------------------

    private static void Pulse(float amplitude, float seconds)
    {
        XRInput.Vibrate(XRHand.Left, amplitude, seconds);
        XRInput.Vibrate(XRHand.Right, amplitude, seconds);
    }

    private static float MoveTowards(float from, float to, float maxStep)
        => MathF.Abs(to - from) <= maxStep ? to : from + MathF.Sign(to - from) * maxStep;

    private static Float3 Flat(Float3 v) => new(v.X, 0f, v.Z);
}


