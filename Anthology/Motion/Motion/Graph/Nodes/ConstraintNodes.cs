using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>Helpers for nodes that pose one bone in model space and write the result back as a local transform.</summary>
internal static class BoneWriter
{
    /// <summary>The model space transform of a bone's parent, or the identity for a root.</summary>
    public static Transform3D ParentModel(Pose pose, int bone)
    {
        int parent = pose.Skeleton.GetParentBoneIndex(bone);
        return parent == Skeleton.InvalidIndex ? Transform3D.Identity : pose.GetModelSpaceTransform(parent);
    }

    /// <summary>Writes a model space rotation onto a bone, keeping its local position and scale.</summary>
    public static void SetModelRotation(Pose pose, int bone, Quaternion model)
        => SetModelRotation(pose, bone, ParentModel(pose, bone).rotation, model);

    /// <summary>
    /// The same write for a caller that already knows the parent's model rotation. Writing a bone drops
    /// the pose's model space cache, so a chain that looked the parent up would rebuild the whole
    /// skeleton once per bone.
    /// </summary>
    public static void SetModelRotation(Pose pose, int bone, Quaternion parentModelRotation, Quaternion model)
    {
        Transform3D local = pose.GetTransform(bone);
        pose.SetTransform(bone, new Transform3D(local.position, Quaternion.Normalize(Quaternion.Inverse(parentModelRotation) * model), local.scale));
    }
}

// ---- Aim constraint ----------------------------------------------------------------------------

/// <summary>
/// Turns one bone so a chosen axis of it points at a target: a turret, a head on a creature with no
/// humanoid rig, a weapon following a crosshair. The turn can be limited to a cone around where the
/// bone already points, and weighted in.
/// </summary>
/// <remarks>
/// The bone takes the shortest turn onto the target, so whatever roll the animation gave it about the
/// aim axis is kept. A constraint that instead rebuilt the orientation from an up vector would throw
/// that roll away.
/// </remarks>
public sealed class AimConstraintDefinition : PoseNodeDefinition
{
    public AimConstraintDefinition(int child, StringID bone, int targetNodeIndex)
    {
        Child = child;
        Bone = bone;
        TargetNodeIndex = targetNodeIndex;
    }

    public int Child { get; }

    public StringID Bone { get; }

    /// <summary>Value node giving the target: a model space vector, or a target.</summary>
    public int TargetNodeIndex { get; }

    /// <summary>The axis of the bone that should end up pointing at the target, in the bone's own space.</summary>
    public Float3 AimAxis { get; set; } = new(0f, 1f, 0f);

    /// <summary>Float value node scaling the effect, or -1 for <see cref="Weight"/>.</summary>
    public int WeightNodeIndex { get; set; } = -1;

    public float Weight { get; set; } = 1f;

    /// <summary>How far the bone may turn from where it started, in degrees. Zero or less is unlimited.</summary>
    public float MaxAngleDegrees { get; set; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly AimConstraintDefinition _def;
        private ValueNodeInstance _target = null!;
        private ValueNodeInstance? _weight;
        private int _bone;

        public Instance(AimConstraintDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _bone = context.Skeleton.GetBoneIndex(_def.Bone);
            _target = context.ValueNode(_def.TargetNodeIndex, ValueInputKind.Vector | ValueInputKind.Target);
            _weight = context.OptionalValueNode(_def.WeightNodeIndex, ValueInputKind.Number);
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);
            if (_bone == Skeleton.InvalidIndex)
                return;

            float weight = Math.Clamp(_weight?.GetValue(context).AsFloat() ?? _def.Weight, 0f, 1f);
            if (!(weight > 0f) || !IKGoalResolver.TryResolve(_target.GetValue(context), Pose, context, out Float3 goal))
                return;

            Transform3D model = Pose.GetModelSpaceTransform(_bone);
            Float3 toGoal = goal - model.position;
            if (Float3.LengthSquared(toGoal) < 1e-10f)
                return;

            Quaternion aimed = Quaternion.Normalize(TransformOps.FromToRotation(model.rotation * _def.AimAxis, Float3.Normalize(toGoal)) * model.rotation);
            if (_def.MaxAngleDegrees > 0f)
                aimed = Limit(model.rotation, aimed, _def.MaxAngleDegrees * MathF.PI / 180f);

            BoneWriter.SetModelRotation(Pose, _bone, Quaternion.Slerp(model.rotation, aimed, weight));
        }

        private static Quaternion Limit(Quaternion from, Quaternion to, float maxRadians)
        {
            float dot = Math.Clamp(MathF.Abs(Quaternion.Dot(from, to)), 0f, 1f);
            float angle = 2f * MathF.Acos(dot);
            return angle <= maxRadians ? to : Quaternion.Slerp(from, to, maxRadians / angle);
        }
    }
}

// ---- Copy constraint ---------------------------------------------------------------------------

/// <summary>Which parts of a source transform a <see cref="CopyConstraintDefinition"/> takes.</summary>
[Flags]
public enum TransformChannels : byte
{
    None = 0,
    Position = 1,
    Rotation = 2,
    Scale = 4,
    PositionAndRotation = Position | Rotation,
    All = Position | Rotation | Scale,
}

/// <summary>
/// Copies another bone's model space transform onto a bone, optionally with an offset: a prop held in
/// a hand, a hand pinned to a weapon's grip, a part that has to track another. The source may also be a
/// target value, so it can come from outside the skeleton.
/// </summary>
public sealed class CopyConstraintDefinition : PoseNodeDefinition
{
    public CopyConstraintDefinition(int child, StringID bone, StringID sourceBone, TransformChannels channels = TransformChannels.PositionAndRotation)
    {
        Child = child;
        Bone = bone;
        SourceBone = sourceBone;
        Channels = channels;
    }

    public CopyConstraintDefinition(int child, StringID bone, int sourceNodeIndex, TransformChannels channels = TransformChannels.PositionAndRotation)
    {
        Child = child;
        Bone = bone;
        SourceNodeIndex = sourceNodeIndex;
        Channels = channels;
    }

    public int Child { get; }

    public StringID Bone { get; }

    /// <summary>The bone to copy from, when the source is another bone.</summary>
    public StringID SourceBone { get; }

    /// <summary>Target value node to copy from, or -1 when the source is a bone.</summary>
    public int SourceNodeIndex { get; } = -1;

    public TransformChannels Channels { get; }

    /// <summary>Offset applied in the source's space, so a prop can sit off the hand's pivot.</summary>
    public Transform3D Offset { get; set; } = Transform3D.Identity;

    /// <summary>Float value node scaling the effect, or -1 for <see cref="Weight"/>.</summary>
    public int WeightNodeIndex { get; set; } = -1;

    public float Weight { get; set; } = 1f;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly CopyConstraintDefinition _def;
        private ValueNodeInstance? _source;
        private ValueNodeInstance? _weight;
        private int _bone;
        private int _sourceBone;

        public Instance(CopyConstraintDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _bone = context.Skeleton.GetBoneIndex(_def.Bone);
            _sourceBone = context.Skeleton.GetBoneIndex(_def.SourceBone);
            _source = context.OptionalValueNode(_def.SourceNodeIndex, ValueInputKind.Target);
            _weight = context.OptionalValueNode(_def.WeightNodeIndex, ValueInputKind.Number);

            if (_bone != Skeleton.InvalidIndex && _sourceBone != Skeleton.InvalidIndex && context.Skeleton.IsChildBoneOf(_bone, _sourceBone))
                throw context.Error("copies from a bone below the one it drives, which would fight itself.");
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);
            if (_bone == Skeleton.InvalidIndex || !TryGetSource(context, out Transform3D source))
                return;

            float weight = Math.Clamp(_weight?.GetValue(context).AsFloat() ?? _def.Weight, 0f, 1f);
            if (!(weight > 0f))
                return;

            Transform3D goal = TransformOps.Combine(source, _def.Offset);
            Transform3D model = Pose.GetModelSpaceTransform(_bone);

            Float3 position = (_def.Channels & TransformChannels.Position) != 0 ? Maths.Lerp(model.position, goal.position, weight) : model.position;
            Quaternion rotation = (_def.Channels & TransformChannels.Rotation) != 0 ? Quaternion.Slerp(model.rotation, goal.rotation, weight) : model.rotation;
            Float3 scale = (_def.Channels & TransformChannels.Scale) != 0 ? Maths.Lerp(model.scale, goal.scale, weight) : model.scale;

            Transform3D parent = BoneWriter.ParentModel(Pose, _bone);
            Pose.SetTransform(_bone, TransformOps.Delta(parent, new Transform3D(position, rotation, scale)));
        }

        private bool TryGetSource(GraphContext context, out Transform3D source)
        {
            if (_sourceBone != Skeleton.InvalidIndex)
            {
                source = Pose.GetModelSpaceTransform(_sourceBone);
                return true;
            }

            source = Transform3D.Identity;
            if (_source is null)
                return false;

            ParameterValue value = _source.GetValue(context);
            if (value.Type != AnimationValueType.Target || !value.Target.TryGetTransform(Pose, out Transform3D resolved))
                return false;

            source = value.Target.IsBoneTarget ? resolved : context.WorldToCharacter(resolved);
            return true;
        }
    }
}

// ---- Twist distribution ------------------------------------------------------------------------

/// <summary>
/// Spreads a joint's roll onto the twist bones many rigs carry along the forearm or thigh, so the mesh
/// does not pinch at the wrist or knee. It reads how far the driver bone (the hand, say) has rolled
/// about the limb axis and gives each twist bone its share of that roll.
/// </summary>
public sealed class TwistDistributionDefinition : PoseNodeDefinition
{
    public TwistDistributionDefinition(int child, StringID driver, IReadOnlyList<(StringID Bone, float Share)> twistBones)
    {
        Child = child;
        Driver = driver;
        TwistBones = new List<(StringID, float)>(twistBones).ToArray();
    }

    public int Child { get; }

    /// <summary>The bone whose roll is being spread (the hand for a forearm, the foot for a shin).</summary>
    public StringID Driver { get; }

    /// <summary>Each twist bone and the fraction of the roll it takes.</summary>
    public (StringID Bone, float Share)[] TwistBones { get; }

    /// <summary>The limb axis the roll is measured about, in the driver's own reference space.</summary>
    public Float3 Axis { get; set; } = new(1f, 0f, 0f);

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly TwistDistributionDefinition _def;
        private int _driver;
        private int[] _bones = null!;
        private float[] _shares = null!;
        private Quaternion _driverReference;
        private Float3 _axis;
        private Float3[] _boneAxes = null!;

        public Instance(TwistDistributionDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _driver = context.Skeleton.GetBoneIndex(_def.Driver);
            _bones = new int[_def.TwistBones.Length];
            _shares = new float[_def.TwistBones.Length];
            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i] = context.Skeleton.GetBoneIndex(_def.TwistBones[i].Bone);
                _shares[i] = _def.TwistBones[i].Share;
            }

            _driverReference = _driver == Skeleton.InvalidIndex ? Quaternion.Identity : context.Skeleton.GetBoneParentSpaceTransform(_driver).rotation;
            _axis = TransformOps.SafeNormalize(_def.Axis);
            BuildBoneAxes(context.Skeleton);
        }

        /// <summary>
        /// The axis is given in the driver's own reference space, which is where the roll is measured. A
        /// twist bone is turned about its own axes, so the same direction is carried into each bone's
        /// space through the reference pose.
        /// </summary>
        private void BuildBoneAxes(Skeleton skeleton)
        {
            _boneAxes = new Float3[_bones.Length];
            if (_driver == Skeleton.InvalidIndex)
                return;

            IReadOnlyList<Transform3D> model = skeleton.ModelSpaceReferencePose;
            Float3 modelAxis = model[_driver].rotation * _axis;

            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == Skeleton.InvalidIndex)
                    continue;
                _boneAxes[i] = TransformOps.SafeNormalize(Quaternion.Inverse(model[_bones[i]].rotation) * modelAxis);
            }
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);
            if (_driver == Skeleton.InvalidIndex || Float3.LengthSquared(_axis) < 0.5f)
                return;

            float roll = Roll(Quaternion.Normalize(Quaternion.Inverse(_driverReference) * Pose.GetTransform(_driver).rotation), _axis);
            for (int i = 0; i < _bones.Length; i++)
            {
                int bone = _bones[i];
                if (bone == Skeleton.InvalidIndex || _shares[i] == 0f)
                    continue;

                Transform3D local = Pose.GetTransform(bone);
                Quaternion twist = Quaternion.AxisAngle(_boneAxes[i], roll * _shares[i]);
                Pose.SetTransform(bone, new Transform3D(local.position, Quaternion.Normalize(local.rotation * twist), local.scale));
            }
        }

        /// <summary>The part of a rotation that turns about the axis (swing twist decomposition).</summary>
        private static float Roll(Quaternion rotation, Float3 axis)
        {
            float along = Float3.Dot(new Float3(rotation.X, rotation.Y, rotation.Z), axis);
            var twist = new Quaternion(axis.X * along, axis.Y * along, axis.Z * along, rotation.W);
            float length = MathF.Sqrt(twist.X * twist.X + twist.Y * twist.Y + twist.Z * twist.Z + twist.W * twist.W);
            if (length < 1e-7f)
                return 0f;

            float w = twist.W / length;
            float sign = w < 0f ? -1f : 1f;
            return 2f * MathF.Atan2(sign * along / length, MathF.Abs(w));
        }
    }
}
