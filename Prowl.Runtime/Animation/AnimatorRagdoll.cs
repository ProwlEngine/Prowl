// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;
using Jitter2.LinearMath;

using Prowl.Motion;
using Prowl.Runtime.AnimationNodes;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime;

/// <summary>
/// The physics puppet a Ragdoll node drives: a hidden, flat copy of the humanoid's bones with a body per
/// part. Muscles turn each part toward its animated angle from its parent and pins hold parts to their
/// animated place, both soft constraints solved with the joints and contacts. The character's own bones
/// show the pose the puppet took, and its controller ignores the puppet.
/// </summary>
internal sealed class AnimatorRagdoll
{
    private struct Part
    {
        public Rigidbody3D Body;
        public Transform Bone;
        public int BoneIndex, Parent;
        public int[] Beyond;

        // The animation now, which is what shows, and what each step aimed at in the world and within the character.
        public Float3 ShownPosition;
        public Quaternion ShownRotation;
        public TrackedPose Aim, ModelAim;

        public FixedAngle? Muscle, PinTurn;
        public BallSocket? PinPlace;
        public float MuscleLead, PinLead;

        // Everything beyond this part's joint: its mass, its inertia about the joint, and the load its motion takes.
        public float CarriedMass;
        public JMatrix CarriedInertia;
        public Float3 JointForce, JointTorque;
    }

    // How fast a muscle and a pin close a gap at full strength, as natural frequencies.
    private const float MuscleFrequency = 20f;
    private const float PinFrequency = 20f;

    private const int HistoryFrames = 8;

    private readonly Animator _animator;
    private readonly AnimatorBinding _binding;
    private readonly MotionSkeleton _skeleton;
    private readonly HumanoidRig? _rig;
    private readonly int[] _partOfBone;
    private readonly HashSet<Rigidbody3D> _bodies = new();
    private Part[] _parts = Array.Empty<Part>();
    private int[] _parentsFirst = Array.Empty<int>();
    private float _builtMass;
    private bool _builtHandsAndFeet;
    private Transform3D[] _shown = Array.Empty<Transform3D>();
    private bool[,] _pairCollides = new bool[0, 0];
    private GameObject? _puppet;
    private CharacterController? _controller;
    private PhysicsWorld? _physics;
    private RagdollPoseHistory? _history;

    private long _drivenFrame = -2;
    private float _muscle, _pin;
    private RagdollPinning _pinning;
    private double _clock;
    private long _clockFrame = -1;
    private Quaternion _aimCharacterRotation = Quaternion.Identity;
    private int _substep;

    public AnimatorRagdoll(Animator animator, AnimatorBinding binding, MotionSkeleton skeleton, HumanoidRig? rig)
    {
        _animator = animator;
        _binding = binding;
        _skeleton = skeleton;
        _rig = rig;
        _partOfBone = new int[skeleton.BoneCount];
        Array.Fill(_partOfBone, -1);
    }

    /// <summary>The puppet's bodies, for queries that must not find the character's own ragdoll.</summary>
    public IReadOnlySet<Rigidbody3D> Bodies => _bodies;

    public bool EnsureBodies(float totalMass, bool handsAndFeet)
    {
        if (_puppet.IsValid())
        {
            if (totalMass != _builtMass || handsAndFeet != _builtHandsAndFeet)
                Debug.LogWarningOnce($"Animator.Ragdolls.{_animator.GameObject.Name}",
                    $"[Animator] '{_animator.GameObject.Name}' has Ragdoll nodes with different bodies, but a character has one ragdoll, made as the first asked.");
            return _parts.Length > 0;
        }
        if (_rig == null || _animator.Scene.IsNotValid()) return false;

        Dictionary<HumanBodyBone, Transform>? bones = RagdollBuilder.FindBones(_rig, _binding, out string problem);
        if (bones == null)
        {
            Debug.LogWarning($"[Animator] '{_animator.GameObject.Name}' cannot make a ragdoll: {problem}");
            return false;
        }

        _builtMass = totalMass;
        _builtHandsAndFeet = handsAndFeet;
        try
        {
            Make(bones, totalMass, handsAndFeet);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Animator] '{_animator.GameObject.Name}' could not make its ragdoll: {ex.Message}");
            Release();
            return false;
        }
        return _parts.Length > 0;
    }

    private void Make(Dictionary<HumanBodyBone, Transform> bones, float totalMass, bool handsAndFeet)
    {
        // Flat, so no body is ever moved by another body's transform.
        _puppet = new GameObject($"{_animator.GameObject.Name} Ragdoll") { HideFlags = HideFlags.HideAndDontSave };
        _animator.Scene!.Add(_puppet);
        var puppetBones = new Dictionary<HumanBodyBone, Transform>(bones.Count);
        foreach ((HumanBodyBone bone, Transform source) in bones)
        {
            var go = new GameObject(bone.ToString()) { HideFlags = HideFlags.HideAndDontSave };
            go.SetParent(_puppet, false);
            go.Transform.Position = source.Position;
            go.Transform.Rotation = source.Rotation;
            puppetBones[bone] = go.Transform;
        }
        var settings = new RagdollBuilder.Settings { TotalMass = totalMass, HandsAndFeet = handsAndFeet, Avatar = _animator.Avatar };
        Dictionary<HumanBodyBone, Rigidbody3D> bodies = RagdollBuilder.Build(_animator.Transform, puppetBones, settings);

        var parts = new List<Part>(bodies.Count);
        foreach ((HumanBodyBone bone, Rigidbody3D body) in bodies)
        {
            int index = SkeletonIndexOf(bones[bone]);
            _partOfBone[index] = parts.Count;
            parts.Add(new Part { Body = body, Bone = bones[bone], BoneIndex = index });
        }
        _parts = parts.ToArray();
        for (int i = 0; i < _parts.Length; i++)
            _parts[i].Parent = ParentPart(_skeleton, _partOfBone, _parts[i].BoneIndex);
        for (int i = 0; i < _parts.Length; i++)
            _parts[i].Beyond = PartsBeyond(i);
        _parentsFirst = ParentsFirst();
        _shown = new Transform3D[_parts.Length];
        _history = new RagdollPoseHistory(_parts.Length, HistoryFrames);

        _physics = _animator.Scene.Physics;
        _physics.PreStep += Step;
        _physics.PreSubStep += Retarget;
        _controller = _animator.GameObject.GetComponentInParent<CharacterController>();
        Rigidbody3D? own = _animator.GameObject.GetComponentInParent<Rigidbody3D>();
        var ignored = new List<(Rigidbody3D, Rigidbody3D)>();
        for (int a = 0; a < _parts.Length; a++)
        {
            // Drawn between steps, so the puppet moves smoothly at any frame rate.
            _parts[a].Body.Interpolation = RigidbodyInterpolation.Interpolate;
            _bodies.Add(_parts[a].Body);
            if (_controller.IsValid()) _controller!.IgnoreCollisionWith(_parts[a].Body);
            if (own.IsValid()) ignored.Add((own!, _parts[a].Body));
            for (int b = a + 1; b < _parts.Length; b++) ignored.Add((_parts[a].Body, _parts[b].Body));
        }
        _physics.IgnoreCollisionsBetween(ignored);
        _pairCollides = new bool[_parts.Length, _parts.Length];
    }

    // The skeleton bone a Transform shows, so a part standing in for a bone the rig lacks sits on the bone it stands on.
    private int SkeletonIndexOf(Transform transform)
    {
        for (int i = 0; i < _skeleton.BoneCount; i++)
            if (ReferenceEquals(_binding.BoneTransform(i), transform)) return i;
        throw new InvalidOperationException($"'{transform.GameObject.Name}' is not one of the rig's bones.");
    }

    /// <summary>Takes the puppet out of the world.</summary>
    public void Release()
    {
        foreach (Part part in _parts)
        {
            RemoveConstraint(part.Muscle);
            RemoveConstraint(part.PinTurn);
            RemoveConstraint(part.PinPlace);
            if (_controller.IsValid() && part.Body.IsValid()) _controller!.EnableCollisionWith(part.Body);
        }

        if (_physics != null)
        {
            _physics.PreStep -= Step;
            _physics.PreSubStep -= Retarget;
        }
        _physics = null;
        if (_puppet.IsValid()) _puppet!.Destroy();
        _puppet = null;
        _history = null;
        _bodies.Clear();
        _pairCollides = new bool[0, 0];
        _parts = Array.Empty<Part>();
        _parentsFirst = Array.Empty<int>();
        Array.Fill(_partOfBone, -1);
    }

    /// <summary>
    /// Moves a pose toward where the bodies are, by <paramref name="blend"/>. Bones without a body keep
    /// their animation relative to the bone above them.
    /// </summary>
    public void Show(Pose pose, GraphContext context, float blend)
    {
        // Every target is taken from the animation before any bone moves, since moving one carries its children.
        foreach (int i in _parentsFirst)
            _shown[i] = pose.GetModelSpaceTransform(_parts[i].BoneIndex);

        foreach (int i in _parentsFirst)
        {
            if (!TryGetBodyPose(i, out Transform3D world)) continue;

            int bone = _parts[i].BoneIndex;
            Transform3D animated = _shown[i];
            Transform3D physics = context.WorldToCharacter(world);
            var target = new Transform3D(Maths.Lerp(animated.position, physics.position, blend),
                Quaternion.Slerp(animated.rotation, physics.rotation, blend), animated.scale);

            int parentBone = _skeleton.GetParentBoneIndex(bone);
            Transform3D parent = parentBone == MotionSkeleton.InvalidIndex ? Transform3D.Identity : pose.GetModelSpaceTransform(parentBone);
            pose.SetTransform(bone, new Transform3D(parent.InverseTransformPoint(target.position),
                Quaternion.Normalize(Quaternion.Inverse(parent.rotation) * target.rotation), pose.GetTransform(bone).scale));
        }
    }

    // What shows is the animation now, moved by how far the body is from what it was aimed at a step behind.
    private bool TryGetBodyPose(int index, out Transform3D world)
    {
        world = default;
        if (_parts[index].Body.IsNotValid()) return false;

        ref Part part = ref _parts[index];
        Transform transform = part.Body.Transform;
        Float3 position = transform.Position;
        Quaternion rotation = transform.Rotation;

        if (part.Aim.HasVelocity)
        {
            float t = Time.FixedAlpha;
            position = part.ShownPosition + (position - part.Aim.PositionAt(t));
            rotation = Quaternion.Normalize(rotation * Quaternion.Inverse(part.Aim.RotationAt(t)) * part.ShownRotation);
        }

        world = new Transform3D(position, rotation, Float3.One);
        return true;
    }

    /// <summary>Hands the puppet the animated pose to follow, and how hard, until the next call.</summary>
    public void Drive(Pose target, in Transform3D characterWorld, float muscle, float pin, RagdollPinning pinning)
    {
        if (_history != null)
        {
            var character = new Transform3D(characterWorld.position, characterWorld.rotation, Float3.One);
            int slot = _history.Record(FrameTime(), character);
            for (int i = 0; i < _parts.Length; i++)
            {
                Transform3D model = target.GetModelSpaceTransform(_parts[i].BoneIndex);
                RecordPart(slot, character, i, model.position * characterWorld.scale, model.rotation);
            }
        }

        _muscle = muscle;
        _pin = pin;
        _pinning = pinning;
        _drivenFrame = Time.FrameCount;
    }

    // Aims the muscles and the pins before the world steps, after every component has moved for the step.
    private void Step(float dt)
    {
        _substep = 0;
        if (dt <= 0f || _history == null || _physics == null || _animator.Scene.IsNotValid()) return;

        float muscle = _muscle, pin = _pin;
        RagdollPinning pinning = _pinning;

        // Without a node driving it the puppet follows the bones the animator posed, at full strength.
        if (Time.FrameCount - _drivenFrame > 1)
        {
            Transform animated = _animator.Transform;
            var character = new Transform3D(animated.Position, animated.Rotation, Float3.One);
            Quaternion toModel = Quaternion.Inverse(character.rotation);
            int slot = _history.Record(FrameTime(), character);
            for (int i = 0; i < _parts.Length; i++)
            {
                Transform bone = _parts[i].Bone;
                RecordPart(slot, character, i, toModel * (bone.Position - character.position), toModel * bone.Rotation);
            }
            muscle = pin = 1f;
            pinning = RagdollPinning.All;
        }
        if (_history.Count == 0) return;

        // The accumulator still holds this step, so this is a step before its end.
        RagdollPoseHistory.Moment moment = _history.At(FrameTime() - Time.FixedAccumulator);
        Transform3D aimed = _history.Character(moment);
        _aimCharacterRotation = aimed.rotation;
        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part part = ref _parts[i];
            if (!IsLive(part.Body)) continue;
            Transform3D model = _history.Part(moment, i);
            part.Aim.Push(aimed.position + aimed.rotation * model.position, aimed.rotation * model.rotation, dt);
            part.ModelAim.Push(model.position, model.rotation, dt);
        }

        UpdateSelfCollisions();
        ComputeLoads(dt, _physics.Gravity);

        // Pinned by the hips alone the body hangs from them, so the muscles and hips supply the motion's load.
        bool hanging = pinning == RagdollPinning.Hips;
        World world = _physics.World;
        float substep = dt / Math.Max(1, _physics.Substep);
        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part part = ref _parts[i];
            if (!IsLive(part.Body)) continue;
            part.Body.SetActive(true);

            if (part.Parent >= 0 && IsLive(_parts[part.Parent].Body)) Flex(world, ref part, ref _parts[part.Parent], muscle, hanging, dt, substep);
            bool pinned = part.Parent < 0 || pinning == RagdollPinning.All;
            Pin(world, ref part, pinned ? pin : 0f, hanging, dt, substep);
        }
    }

    private void RecordPart(int slot, in Transform3D character, int part, Float3 model, Quaternion modelRotation)
    {
        _history!.SetPart(slot, part, new Transform3D(model, modelRotation, Float3.One));
        _parts[part].ShownPosition = character.position + character.rotation * model;
        _parts[part].ShownRotation = character.rotation * modelRotation;
    }

    private double FrameTime()
    {
        if (_clockFrame != Time.FrameCount)
        {
            _clockFrame = Time.FrameCount;
            _clock += Time.DeltaTime;
        }
        return _clock;
    }

    // A spring toward the child's animated angle from its parent, sized to everything it turns.
    private static void Flex(World world, ref Part part, ref Part parent, float strength, bool hanging, float dt, float substep)
    {
        FixedAngle muscle = part.Muscle ??= CreateAngle(world, part.Body, parent.Body.Native!);
        muscle.IsEnabled = strength > 0f;
        if (strength <= 0f) return;

        if (hanging)
        {
            part.Body.AddTorque(part.JointTorque * strength);
            parent.Body.AddTorque(-part.JointTorque * strength);
        }

        ref FixedAngle.FixedAngleData data = ref muscle.Data;
        float mass = SpringMass(part.CarriedInertia, parent.Body.Native!.Data.InverseInertiaWorld);
        part.MuscleLead = Tune(ref data.Softness, ref data.BiasFactor, mass, strength * MuscleFrequency, dt, substep, AngleErrorPerRadian);
    }

    // Springs to the part's animated place and angle, sized to the part, or to the whole body at hanging hips.
    private static void Pin(World world, ref Part part, float strength, bool hanging, float dt, float substep)
    {
        FixedAngle turn = part.PinTurn ??= CreateAngle(world, part.Body, world.NullBody);
        BallSocket place = part.PinPlace ??= CreatePlace(world, part.Body);
        turn.IsEnabled = place.IsEnabled = strength > 0f;
        if (strength <= 0f) return;

        bool carriesBody = hanging && part.Parent < 0;
        if (carriesBody)
        {
            part.Body.AddForce(part.JointForce * strength);
            part.Body.AddTorque(part.JointTorque * strength);
        }

        float frequency = strength * PinFrequency;
        JMatrix inertia = carriesBody ? part.CarriedInertia : part.Body.WorldInertia;
        ref FixedAngle.FixedAngleData turnData = ref turn.Data;
        Tune(ref turnData.Softness, ref turnData.BiasFactor, SpringMass(inertia, default), frequency, dt, substep, AngleErrorPerRadian);
        ref BallSocket.BallSocketData placeData = ref place.Data;
        float mass = carriesBody ? part.CarriedMass : part.Body.Mass;
        part.PinLead = Tune(ref placeData.Softness, ref placeData.BiasFactor, mass, frequency, dt, substep, PlaceErrorPerUnit);
    }

    // Each substep aims the springs at where the parts should be when that substep ends.
    private void Retarget(float substepTime)
    {
        if (_physics == null) return;
        float along = Math.Min(1f, ++_substep / (float)Math.Max(1, _physics.Substep));

        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part part = ref _parts[i];
            Quaternion rotation = part.Aim.RotationAt(along);

            if (part.PinTurn?.IsEnabled == true)
            {
                part.PinTurn.Data.Q0 = Advance(rotation, part.Aim.Spin, part.PinLead).ToJitter();
                part.PinPlace!.Anchor2 = (part.Aim.PositionAt(along) + part.Aim.Velocity * part.PinLead).ToJitter();
            }

            if (part.Muscle?.IsEnabled != true) continue;
            ref Part parent = ref _parts[part.Parent];
            Quaternion above = Advance(parent.Aim.RotationAt(along), parent.Aim.Spin, part.MuscleLead);
            Quaternion child = Advance(rotation, part.Aim.Spin, part.MuscleLead);
            part.Muscle.Data.Q0 = Quaternion.Normalize(Quaternion.Inverse(above) * child).ToJitter();
        }
    }

    // A Jitter soft constraint is a critically damped spring once its softness and bias are set from a
    // frequency and mass. The solver scales both by the whole step, and an angle's error is half the angle.
    private const float AngleErrorPerRadian = 0.5f;
    private const float PlaceErrorPerUnit = 1f;

    // Tunes a constraint into a spring. Its damper pulls toward rest, so a moving target is led by the returned time.
    private static float Tune(ref float softness, ref float bias, float mass, float frequency, float dt, float substep, float errorPerUnit)
    {
        float omega = 2f * MathF.PI * frequency;
        float stiffness = mass * omega * omega;
        float damping = 2f * mass * omega;
        softness = dt / (substep * (damping + substep * stiffness));
        bias = dt * stiffness / (errorPerUnit * (damping + substep * stiffness));
        return damping / stiffness;
    }

    // The inertia a spring turns, about the heaviest axis, which for a limb is a bend rather than its twist.
    private static float SpringMass(in JMatrix inertia, in JMatrix otherInverse)
    {
        if (!JMatrix.Inverse(inertia, out JMatrix inverse)) return 0f;
        float smallest = SmallestEigenvalue(inverse + otherInverse);
        return smallest > 0f ? 1f / smallest : 0f;
    }

    private static float SmallestEigenvalue(in JMatrix m)
    {
        float xy = 0.5f * (m.M12 + m.M21), xz = 0.5f * (m.M13 + m.M31), yz = 0.5f * (m.M23 + m.M32);
        float off = xy * xy + xz * xz + yz * yz;
        float mean = (m.M11 + m.M22 + m.M33) / 3f;
        float x = m.M11 - mean, y = m.M22 - mean, z = m.M33 - mean;
        float spread = MathF.Sqrt((x * x + y * y + z * z + 2f * off) / 6f);
        if (spread < 1e-9f * MathF.Abs(mean)) return mean;

        x /= spread; y /= spread; z /= spread;
        float bxy = xy / spread, bxz = xz / spread, byz = yz / spread;
        float det = x * (y * z - byz * byz) - bxy * (bxy * z - byz * bxz) + bxz * (bxy * byz - y * bxz);
        float angle = MathF.Acos(Math.Clamp(det * 0.5f, -1f, 1f)) / 3f;
        return mean + 2f * spread * MathF.Cos(angle + 2f * MathF.PI / 3f);
    }

    // For every joint, what hangs beyond it: its mass and inertia about the joint, and by inverse dynamics
    // the force and torque it takes to move all of it the way the animation does, gravity included. The
    // physics keeps each body's mass at its origin, which is its joint. The motion is taken within the
    // character, since when the character itself moved between frames is too uneven to differentiate twice.
    private void ComputeLoads(float dt, Float3 gravity)
    {
        Span<Float3> force = stackalloc Float3[_parts.Length];
        Span<Float3> torque = stackalloc Float3[_parts.Length];
        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part part = ref _parts[i];
            if (!IsLive(part.Body) || !part.ModelAim.HasVelocity) continue;

            bool accelerating = part.ModelAim.HasAcceleration;
            Float3 acceleration = accelerating ? _aimCharacterRotation * part.ModelAim.Acceleration(dt) : Float3.Zero;
            Float3 angular = accelerating ? _aimCharacterRotation * part.ModelAim.AngularAcceleration(dt) : Float3.Zero;
            force[i] = part.Body.Mass * (acceleration - gravity);
            torque[i] = Apply(part.Body.WorldInertia, angular);
        }

        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part part = ref _parts[i];
            part.CarriedMass = 0f;
            part.CarriedInertia = default;
            part.JointForce = part.JointTorque = Float3.Zero;
            if (!IsLive(part.Body)) continue;

            Float3 joint = Position(part.Body);
            foreach (int j in part.Beyond)
            {
                Rigidbody3D body = _parts[j].Body;
                if (!IsLive(body)) continue;
                JVector offset = (Position(body) - joint).ToJitter();

                part.CarriedMass += body.Mass;
                part.CarriedInertia += body.WorldInertia + (JMatrix.Identity * JVector.Dot(offset, offset) - JVector.Outer(offset, offset)) * body.Mass;
                part.JointForce += force[j];
                part.JointTorque += torque[j] + Float3.Cross(_parts[j].Aim.Position - part.Aim.Position, force[j]);
            }
        }
    }

    // A part and every part hanging somewhere below it.
    private int[] PartsBeyond(int joint)
    {
        var beyond = new List<int>();
        for (int i = 0; i < _parts.Length; i++)
            for (int p = i; p >= 0; p = _parts[p].Parent)
                if (p == joint) { beyond.Add(i); break; }
        return beyond.ToArray();
    }

    // Two parts collide only while they are apart both in the animation and in the simulation.
    private void UpdateSelfCollisions()
    {
        List<(Rigidbody3D, Rigidbody3D)>? enable = null, ignore = null;
        for (int a = 0; a < _parts.Length; a++)
        {
            for (int b = a + 1; b < _parts.Length; b++)
            {
                ref Part first = ref _parts[a];
                ref Part second = ref _parts[b];
                if (!IsLive(first.Body) || !IsLive(second.Body) || first.Parent == b || second.Parent == a) continue;

                RigidBody one = first.Body.Native!, two = second.Body.Native!;
                bool apart = !Overlaps(one, two, first.Aim.Rotation.ToJitter(), second.Aim.Rotation.ToJitter(), first.Aim.Position.ToJitter(), second.Aim.Position.ToJitter())
                    && !Overlaps(one, two, one.Orientation, two.Orientation, one.Position, two.Position);
                if (_pairCollides[a, b] == apart) continue;

                _pairCollides[a, b] = apart;
                (apart ? enable ??= new() : ignore ??= new()).Add((first.Body, second.Body));
            }
        }
        if (enable != null) _physics!.EnableCollisionsBetween(enable);
        if (ignore != null) _physics!.IgnoreCollisionsBetween(ignore);
    }

    private static bool Overlaps(RigidBody one, RigidBody two, JQuaternion oneRotation, JQuaternion twoRotation, JVector onePosition, JVector twoPosition)
    {
        foreach (RigidBodyShape a in one.Shapes)
            foreach (RigidBodyShape b in two.Shapes)
                if (NarrowPhase.Overlap(a, b, oneRotation, twoRotation, onePosition, twoPosition)) return true;
        return false;
    }

    private static FixedAngle CreateAngle(World world, Rigidbody3D body, RigidBody other)
    {
        FixedAngle angle = world.CreateConstraint<FixedAngle>(body.Native!, other);
        angle.Initialize();
        return angle;
    }

    private static BallSocket CreatePlace(World world, Rigidbody3D body)
    {
        BallSocket place = world.CreateConstraint<BallSocket>(body.Native!, world.NullBody);
        place.Initialize(body.Native!.Position);
        return place;
    }

    private static void RemoveConstraint(Constraint? constraint)
    {
        if (constraint?.IsValid != true) return;
        constraint.Body1.World.Remove(constraint);
    }

    private int[] ParentsFirst()
    {
        var order = new int[_parts.Length];
        var depth = new int[_parts.Length];
        for (int i = 0; i < _parts.Length; i++)
        {
            order[i] = i;
            for (int p = _parts[i].Parent; p >= 0; p = _parts[p].Parent) depth[i]++;
        }
        Array.Sort(depth, order);
        return order;
    }

    private static int ParentPart(MotionSkeleton skeleton, int[] partOfBone, int bone)
    {
        for (int b = skeleton.GetParentBoneIndex(bone); b != MotionSkeleton.InvalidIndex; b = skeleton.GetParentBoneIndex(b))
            if (partOfBone[b] >= 0) return partOfBone[b];
        return -1;
    }

    private static bool IsLive(Rigidbody3D body) => body.IsValid() && body.IsSimulated;

    private static Quaternion Advance(Quaternion rotation, Float3 spin, float seconds) =>
        Quaternion.Normalize(Quaternion.FromRotationVector(spin * seconds) * rotation);

    private static Float3 Apply(in JMatrix m, Float3 v) => JVector.Transform(v.ToJitter(), m).ToProwl();

    private static Float3 Position(Rigidbody3D body) => body.Native!.Position.ToProwl();
}
