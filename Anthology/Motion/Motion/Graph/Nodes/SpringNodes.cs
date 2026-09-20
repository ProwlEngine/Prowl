using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

// ---- Spring bones ------------------------------------------------------------------------------

/// <summary>
/// Gives a chain of bones (hair, a tail, a coat, an antenna) secondary motion: each bone lags behind
/// the animated pose, swings when the character moves and settles back under a spring. The chain is
/// simulated in world space, so turning or running drags it naturally.
/// </summary>
/// <remarks>
/// Bones are listed from the root of the chain outward. Each one is aimed at the simulated position of
/// the next, and the last uses <see cref="TipLength"/> along its own direction. Keep the stiffness
/// moderate: this is a spring, so a very stiff chain with a long frame time will ring.
/// </remarks>
public sealed class SpringBonesDefinition : PoseNodeDefinition
{
    public SpringBonesDefinition(int child, IReadOnlyList<StringID> chain)
    {
        Child = child;
        Chain = new List<StringID>(chain).ToArray();
    }

    /// <summary>
    /// Takes the chain from the rig itself: <paramref name="root"/> and the bones below it, following
    /// the first child each step. A hair strand is one node; a strand whose tip should swing more freely
    /// is two, the second rooted where the first ends.
    /// </summary>
    public SpringBonesDefinition(int child, StringID root, int boneCount)
    {
        if (boneCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(boneCount), "A spring chain needs at least one bone.");

        Child = child;
        Chain = Array.Empty<StringID>();
        Root = root;
        BoneCount = boneCount;
    }

    public int Child { get; }

    /// <summary>The chain named bone by bone, from its root outward. Empty when <see cref="Root"/> is used.</summary>
    public StringID[] Chain { get; }

    /// <summary>The first bone of the chain, when the chain is taken from the rig.</summary>
    public StringID Root { get; }

    /// <summary>How many bones the chain covers, counting the root.</summary>
    public int BoneCount { get; }

    /// <summary>How strongly a bone is pulled back to where the animation put it.</summary>
    public float Stiffness { get; set; } = 40f;

    /// <summary>How quickly the swinging dies down.</summary>
    public float Damping { get; set; } = 6f;

    /// <summary>Constant acceleration in the character's own space, usually a downward pull.</summary>
    public Float3 Gravity { get; set; }

    /// <summary>How far a bone may swing from where the animation put it, in degrees. Zero or less is unlimited.</summary>
    public float MaxAngleDegrees { get; set; } = 60f;

    /// <summary>How long the last bone of the chain is treated as being, since it has no child to aim at.</summary>
    public float TipLength { get; set; } = 0.1f;

    /// <summary>Moving further than this in one frame counts as a teleport and the chain snaps along.</summary>
    public float TeleportDistance { get; set; } = 2f;

    /// <summary>Float value node fading the whole effect in and out, or -1 for always on.</summary>
    public int WeightNodeIndex { get; set; } = -1;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly SpringBonesDefinition _def;
        private ValueNodeInstance? _weight;
        private int[] _bones = null!;
        private int[] _parents = null!;
        private Transform3D[] _chainModel = null!;
        private Float3[] _tips = null!;       // simulated tip positions, world space
        private Float3[] _velocities = null!;
        private bool _started;
        private Float3 _lastOrigin;

        public Instance(SpringBonesDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _weight = context.OptionalValueNode(_def.WeightNodeIndex, ValueInputKind.Number);

            _bones = _def.Chain.Length > 0 ? Named(context.Skeleton) : FromRig(context.Skeleton);
            _parents = new int[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
                _parents[i] = context.Skeleton.GetParentBoneIndex(_bones[i]);
            _chainModel = new Transform3D[_bones.Length];
            _tips = new Float3[_bones.Length];
            _velocities = new Float3[_bones.Length];
        }

        private int[] Named(Skeleton skeleton)
        {
            var bones = new List<int>(_def.Chain.Length);
            foreach (StringID id in _def.Chain)
            {
                int bone = skeleton.GetBoneIndex(id);
                if (bone != Skeleton.InvalidIndex)
                    bones.Add(bone);
            }
            return bones.ToArray();
        }

        // Walks down from the root, taking the first child each step, so a chain needs one name.
        private int[] FromRig(Skeleton skeleton)
        {
            int bone = skeleton.GetBoneIndex(_def.Root);
            if (bone == Skeleton.InvalidIndex)
                return Array.Empty<int>();

            var bones = new List<int>(_def.BoneCount);
            for (int i = 0; i < _def.BoneCount && bone != Skeleton.InvalidIndex; i++)
            {
                bones.Add(bone);
                bone = skeleton.GetFirstChildBoneIndex(bone);
            }
            return bones.ToArray();
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            _started = false;
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);
            if (_bones.Length == 0)
                return;

            Transform3D world = context.WorldTransform;
            float dt = context.DeltaTime;
            float weight = Math.Clamp(_weight?.GetValue(context).AsFloat() ?? 1f, 0f, 1f);

            bool teleported = Float3.LengthSquared(world.position - _lastOrigin) > _def.TeleportDistance * _def.TeleportDistance;
            if (!_started || teleported || !(dt > 0f))
            {
                Snap(world);
                return;
            }

            Float3 gravity = _def.Gravity;
            float decay = MathF.Exp(-MathF.Max(_def.Damping, 0f) * dt);
            float maxAngle = _def.MaxAngleDegrees * MathF.PI / 180f;
            Quaternion toModel = Quaternion.Inverse(world.rotation);

            for (int i = 0; i < _bones.Length; i++)
            {
                int bone = _bones[i];
                Transform3D model = ModelOf(i);
                _chainModel[i] = model;
                Float3 root = ToWorld(world, model.position);
                Float3 target = TipWorld(world, i, model);

                float length = Float3.Length(target - root);
                if (length < 1e-6f)
                {
                    _tips[i] = target;
                    _velocities[i] = Float3.Zero;
                    continue;
                }

                // Spring toward where the animation wants the tip, in world space, so character motion drags it.
                Float3 acceleration = (target - _tips[i]) * _def.Stiffness + world.rotation * gravity;
                _velocities[i] = (_velocities[i] + acceleration * dt) * decay;
                _tips[i] += _velocities[i] * dt;

                // A bone cannot stretch, so only its direction survives.
                Float3 animated = TransformOps.SafeNormalize(target - root);
                Float3 direction = TransformOps.SafeNormalize(_tips[i] - root);
                if (Float3.LengthSquared(direction) < 0.5f)
                    direction = animated;

                float angle = MathF.Acos(Math.Clamp(Float3.Dot(direction, animated), -1f, 1f));
                if (maxAngle > 0f && angle > maxAngle)
                {
                    direction = TransformOps.SafeNormalize(Maths.Lerp(animated, direction, maxAngle / angle));
                    _velocities[i] = Float3.Zero;
                }

                _tips[i] = root + direction * length;

                if (weight > 0f)
                {
                    Quaternion turn = TransformOps.FromToRotation(toModel * animated, toModel * direction);
                    if (weight < 1f)
                        turn = Quaternion.Slerp(Quaternion.Identity, turn, weight);

                    Quaternion turned = Quaternion.Normalize(turn * model.rotation);
                    BoneWriter.SetModelRotation(Pose, bone, ParentModelOf(i).rotation, turned);
                    _chainModel[i] = new Transform3D(model.position, turned, model.scale);
                }
            }

            _lastOrigin = world.position;
        }

        /// <summary>Puts the simulation exactly where the animation is, with no motion left over.</summary>
        private void Snap(Transform3D world)
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                Transform3D model = ModelOf(i);
                _chainModel[i] = model;
                _tips[i] = TipWorld(world, i, model);
                _velocities[i] = Float3.Zero;
            }

            _lastOrigin = world.position;
            _started = true;
        }

        /// <summary>Where the animation puts this bone's tip, in world space.</summary>
        private Float3 TipWorld(in Transform3D world, int index, in Transform3D model)
        {
            Float3 modelTip = index + 1 < _bones.Length
                ? ChildModel(index, model).position
                : model.position + model.rotation * new Float3(0f, _def.TipLength, 0f);
            return ToWorld(world, modelTip);
        }

        // Each of these prefers the bone the chain already holds, and only asks the pose when the chain
        // is not a straight parent to child walk.
        private Transform3D ModelOf(int index)
            => index > 0 && _parents[index] == _bones[index - 1]
                ? TransformOps.Combine(_chainModel[index - 1], Pose.GetTransform(_bones[index]))
                : Pose.GetModelSpaceTransform(_bones[index]);

        private Transform3D ParentModelOf(int index)
            => index > 0 && _parents[index] == _bones[index - 1]
                ? _chainModel[index - 1]
                : BoneWriter.ParentModel(Pose, _bones[index]);

        private Transform3D ChildModel(int index, in Transform3D model)
            => _parents[index + 1] == _bones[index]
                ? TransformOps.Combine(model, Pose.GetTransform(_bones[index + 1]))
                : Pose.GetModelSpaceTransform(_bones[index + 1]);

        private static Float3 ToWorld(in Transform3D world, Float3 point) => world.position + world.rotation * (point * world.scale);
    }
}
