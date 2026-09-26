using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>Resolves an IK goal value, a model space Vector or a Target, to a model space point.</summary>
internal static class IKGoalResolver
{
    public static bool TryResolve(in ParameterValue value, Pose pose, GraphContext context, out Float3 position)
    {
        position = default;
        switch (value.Type)
        {
            case AnimationValueType.Vector:
                position = value.Vector;
                break;
            case AnimationValueType.Target:
                if (!value.Target.TryGetTransform(pose, out Transform3D resolved))
                    return false;
                position = value.Target.IsBoneTarget ? resolved.position : context.WorldToCharacter(resolved.position);
                break;
            default:
                return false;
        }
        return float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
    }
}

/// <summary>
/// Applies two bone IK on top of a child pose, driving the end bone to a goal value (a model space
/// Vector or a Target). An unset target skips the solve.
/// </summary>
public sealed class TwoBoneIKDefinition : PoseNodeDefinition
{
    public TwoBoneIKDefinition(int child, int targetNodeIndex, int upper, int mid, int end, FloatInput weight)
    { Child = child; TargetNodeIndex = targetNodeIndex; Upper = upper; Mid = mid; End = end; Weight = weight; }
    public int Child { get; }
    public int TargetNodeIndex { get; }
    public int Upper { get; }
    public int Mid { get; }
    public int End { get; }
    public FloatInput Weight { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly TwoBoneIKDefinition _def;
        private ValueNodeInstance _target = null!;
        private BoundFloat _weight;
        public Instance(TwoBoneIKDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _target = context.ValueNode(_def.TargetNodeIndex, ValueInputKind.Vector | ValueInputKind.Target);
            _weight = BoundFloat.Bind(context, _def.Weight);
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);
            float w = _weight.Get(context);
            if (w > 0f && IKGoalResolver.TryResolve(_target.GetValue(context), Pose, context, out Float3 goal))
                TwoBoneIK.Solve(Pose, _def.Upper, _def.Mid, _def.End, goal, w);
        }
    }
}

/// <summary>
/// Applies a humanoid look at on top of a child pose (requires an avatar). The goal is a model space
/// Vector or a Target. An unset target skips the solve.
/// </summary>
public sealed class LookAtDefinition : PoseNodeDefinition
{
    public LookAtDefinition(int child, int targetNodeIndex, FloatInput clamp, FloatInput body, FloatInput head, FloatInput eyes)
        : this(child, targetNodeIndex, 1f, clamp, body, head, eyes) { }

    public LookAtDefinition(int child, int targetNodeIndex, FloatInput weight, FloatInput clamp, FloatInput body, FloatInput head, FloatInput eyes)
    { Child = child; TargetNodeIndex = targetNodeIndex; Weight = weight; Clamp = clamp; Body = body; Head = head; Eyes = eyes; }
    public int Child { get; }
    public int TargetNodeIndex { get; }
    public FloatInput Weight { get; }
    public FloatInput Clamp { get; }
    public FloatInput Body { get; }
    public FloatInput Head { get; }
    public FloatInput Eyes { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly LookAtDefinition _def;
        private ValueNodeInstance _target = null!;
        private BoundFloat _weight, _clamp, _body, _head, _eyes;
        public Instance(LookAtDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _target = context.ValueNode(_def.TargetNodeIndex, ValueInputKind.Vector | ValueInputKind.Target);
            _weight = BoundFloat.Bind(context, _def.Weight);
            _clamp = BoundFloat.Bind(context, _def.Clamp);
            _body = BoundFloat.Bind(context, _def.Body);
            _head = BoundFloat.Bind(context, _def.Head);
            _eyes = BoundFloat.Bind(context, _def.Eyes);
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);
            if (context.Avatar is { IsHuman: true } avatar && IKGoalResolver.TryResolve(_target.GetValue(context), Pose, context, out Float3 goal))
                LookAtSolver.Solve(Pose, avatar.Humanoid!, goal, _weight.Get(context), _clamp.Get(context),
                    _body.Get(context), _head.Get(context), _eyes.Get(context));
        }
    }
}

/// <summary>
/// Places the humanoid feet on the ground under them with <see cref="FootPlacement"/>: each foot moves by
/// the ground's height rather than being pinned to it, the hips drop so both legs reach, and planted
/// feet tilt onto slopes. The ground comes from the graph's probe, or from world heights and normals
/// wired in. Needs a humanoid avatar.
/// </summary>
public sealed class FootGroundingDefinition : PoseNodeDefinition
{
    public FootGroundingDefinition(int child, int leftGroundYNodeIndex = -1, int rightGroundYNodeIndex = -1, int weightNodeIndex = -1)
        : this(child, leftGroundYNodeIndex, rightGroundYNodeIndex, weightNodeIndex, -1, -1) { }

    public FootGroundingDefinition(int child, int leftGroundYNodeIndex, int rightGroundYNodeIndex, int weightNodeIndex, int leftNormalNodeIndex, int rightNormalNodeIndex)
    {
        Child = child;
        LeftGroundYNodeIndex = leftGroundYNodeIndex;
        RightGroundYNodeIndex = rightGroundYNodeIndex;
        WeightNodeIndex = weightNodeIndex;
        LeftNormalNodeIndex = leftNormalNodeIndex;
        RightNormalNodeIndex = rightNormalNodeIndex;
    }

    public int Child { get; }
    public int LeftGroundYNodeIndex { get; }
    public int RightGroundYNodeIndex { get; }
    public int WeightNodeIndex { get; }
    public int LeftNormalNodeIndex { get; }
    public int RightNormalNodeIndex { get; }

    /// <summary>Finds the ground with the graph's own probe instead of the wired heights.</summary>
    public bool ProbeGround { get; set; } = true;

    /// <summary>How far above the character's floor a foot may step up, in world units.</summary>
    public float MaxStepUp { get; set; } = 0.5f;

    /// <summary>How far below the character's floor a foot may reach down, in world units.</summary>
    public float MaxStepDown { get; set; } = 0.5f;

    /// <inheritdoc cref="FootPlacement.AdjustHips"/>
    public bool AdjustHips { get; set; } = true;

    /// <inheritdoc cref="FootPlacement.FootSmoothing"/>
    public float FootSmoothing { get; set; } = 0.04f;

    /// <inheritdoc cref="FootPlacement.HipsSmoothing"/>
    public float HipsSmoothing { get; set; } = 0.08f;

    /// <inheritdoc cref="FootPlacement.MaxFootAngle"/>
    public float MaxFootAngle { get; set; } = 45f;

    /// <inheritdoc cref="FootPlacement.FootLiftHeight"/>
    public float FootLiftHeight { get; set; } = 0.1f;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {

        private readonly FootGroundingDefinition _def;
        private readonly FootPlacement _placement;
        private ValueNodeInstance? _leftY, _rightY, _weight, _leftNormal, _rightNormal;

        public Instance(FootGroundingDefinition def)
        {
            _def = def;
            _placement = new FootPlacement
            {
                AdjustHips = def.AdjustHips,
                FootSmoothing = def.FootSmoothing,
                HipsSmoothing = def.HipsSmoothing,
                MaxFootAngle = def.MaxFootAngle,
                FootLiftHeight = def.FootLiftHeight,
            };
        }

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _leftY = context.OptionalValueNode(_def.LeftGroundYNodeIndex, ValueInputKind.Number);
            _rightY = context.OptionalValueNode(_def.RightGroundYNodeIndex, ValueInputKind.Number);
            _weight = context.OptionalValueNode(_def.WeightNodeIndex, ValueInputKind.Number);
            _leftNormal = context.OptionalValueNode(_def.LeftNormalNodeIndex, ValueInputKind.Vector);
            _rightNormal = context.OptionalValueNode(_def.RightNormalNodeIndex, ValueInputKind.Vector);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            _placement.Reset();
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);

            HumanoidRig? rig = context.Avatar?.Humanoid;
            if (rig is null || !rig.HasBone(HumanBodyBone.LeftFoot) || !rig.HasBone(HumanBodyBone.RightFoot))
                return;

            float weight = _weight is not null ? _weight.GetValue(context).AsFloat() : 1f;
            FootGround left = FindGround(context, rig, HumanBodyBone.LeftFoot, _leftY, _leftNormal);
            FootGround right = FindGround(context, rig, HumanBodyBone.RightFoot, _rightY, _rightNormal);
            _placement.Solve(Pose, rig, left, right, weight, context.FrameTime);
        }

        private FootGround FindGround(GraphContext context, HumanoidRig rig, HumanBodyBone footBone, ValueNodeInstance? height, ValueNodeInstance? normal)
        {
            Float3 foot = Pose.GetModelSpaceTransform(rig.GetSkeletonBoneIndex(footBone)).position;
            Float3 floor = context.WorldTransform.TransformPoint(new Float3(foot.X, 0f, foot.Z));

            Float3 hit, hitNormal;
            if (_def.ProbeGround && context.Ground is { } ground)
            {
                Float3 from = floor + Float3.UnitY * _def.MaxStepUp;
                if (!ground.Raycast(from, -Float3.UnitY, _def.MaxStepUp + _def.MaxStepDown, out hit, out hitNormal))
                    return FootGround.None;
            }
            else
            {
                hit = new Float3(floor.X, height is not null ? height.GetValue(context).AsFloat() : floor.Y, floor.Z);
                hitNormal = normal is not null ? normal.GetValue(context).Vector : Float3.UnitY;
                float step = hit.Y - floor.Y;
                if (step > _def.MaxStepUp || step < -_def.MaxStepDown)
                    return FootGround.None;
            }

            return new FootGround(context.WorldToCharacter(hit).Y, context.WorldNormalToCharacter(hitNormal));
        }
    }
}

/// <summary>One effector of an <see cref="IKRigDefinition"/>: a bone chain driven to a target value node.</summary>
public sealed class IKEffectorInfo
{
    public IKEffectorInfo(string name, int[] chain, int targetNodeIndex, FloatInput? weight = null)
    { Name = name; Chain = chain; TargetNodeIndex = targetNodeIndex; Weight = weight ?? 1f; }

    public string Name { get; }
    public int[] Chain { get; }
    public int TargetNodeIndex { get; }
    public FloatInput Weight { get; }
}

/// <summary>
/// Solves a multi effector IK rig over a child pose. Each effector's goal comes from a value node (a
/// model space Vector, or a Target). Effectors with an unset target are skipped.
/// </summary>
public sealed class IKRigDefinition : PoseNodeDefinition
{
    public IKRigDefinition(int child, System.Collections.Generic.IReadOnlyList<IKEffectorInfo> effectors)
    {
        Child = child;
        Effectors = new System.Collections.Generic.List<IKEffectorInfo>(effectors).ToArray();
    }

    public int Child { get; }
    public IKEffectorInfo[] Effectors { get; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly IKRigDefinition _def;
        private IKRig _rig = null!;
        private IKEffector[] _effectors = null!;
        private ValueNodeInstance[] _targets = null!;
        private BoundFloat[] _weights = null!;

        public Instance(IKRigDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);

            _rig = new IKRig();
            int n = _def.Effectors.Length;
            _effectors = new IKEffector[n];
            _targets = new ValueNodeInstance[n];
            _weights = new BoundFloat[n];
            for (int i = 0; i < n; i++)
            {
                IKEffectorInfo info = _def.Effectors[i];
                _effectors[i] = _rig.AddEffector(info.Name, info.Chain);
                _targets[i] = context.ValueNode(info.TargetNodeIndex, ValueInputKind.Vector | ValueInputKind.Target);
                _weights[i] = BoundFloat.Bind(context, info.Weight);
            }
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);

            for (int i = 0; i < _effectors.Length; i++)
            {
                bool resolved = IKGoalResolver.TryResolve(_targets[i].GetValue(context), Pose, context, out Float3 goal);
                _effectors[i].Target = goal;
                _effectors[i].Weight = resolved ? _weights[i].Get(context) : 0f;
            }
            _rig.Solve(Pose);
        }
    }
}
