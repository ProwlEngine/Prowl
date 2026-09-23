using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Resolves an IK goal value to a model space point. A Vector is taken as a model space point. A
/// Target resolves bone targets against the pose and converts world targets with the character's
/// inverse world transform. Unset or unresolvable targets report false so the solve is skipped.
/// </summary>
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

// ---- Two bone IK ----------------------------------------------------------------------------

/// <summary>
/// Applies two bone IK on top of a child pose, driving the end bone to a goal value (a model space
/// Vector or a Target). An unset target skips the solve.
/// </summary>
public sealed class TwoBoneIKDefinition : PoseNodeDefinition
{
    public TwoBoneIKDefinition(int child, int targetNodeIndex, int upper, int mid, int end, int weightNodeIndex, float defaultWeight)
    { Child = child; TargetNodeIndex = targetNodeIndex; Upper = upper; Mid = mid; End = end; WeightNodeIndex = weightNodeIndex; DefaultWeight = defaultWeight; }
    public int Child { get; }
    public int TargetNodeIndex { get; }
    public int Upper { get; }
    public int Mid { get; }
    public int End { get; }
    public int WeightNodeIndex { get; }
    public float DefaultWeight { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly TwoBoneIKDefinition _def;
        private ValueNodeInstance _target = null!;
        private ValueNodeInstance? _weight;
        public Instance(TwoBoneIKDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _target = context.ValueNode(_def.TargetNodeIndex, ValueInputKind.Vector | ValueInputKind.Target);
            _weight = context.OptionalValueNode(_def.WeightNodeIndex, ValueInputKind.Number);
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);
            float w = _weight?.GetValue(context).AsFloat() ?? _def.DefaultWeight;
            if (w > 0f && IKGoalResolver.TryResolve(_target.GetValue(context), Pose, context, out Float3 goal))
                TwoBoneIK.Solve(Pose, _def.Upper, _def.Mid, _def.End, goal, w);
        }
    }
}

// ---- Look at --------------------------------------------------------------------------------

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

// ---- Foot grounding (humanoid foot lock IK) -------------------------------------------------

/// <summary>
/// Plants the humanoid feet on the ground via two bone IK, blended by a weight. Ground heights and
/// optional ground normals are world space (as returned by physics raycasts) and are converted with
/// the character's world transform. Heights default to 0 and normals to up. Requires the graph
/// instance to be created with a humanoid avatar.
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

    /// <summary>
    /// Finds the ground under each foot with the graph's own ground probe, rather than being told where
    /// it is. Without a probe the node falls back to the heights wired into it.
    /// </summary>
    public bool ProbeGround { get; set; }

    /// <summary>How far the probe looks below a foot, in world units.</summary>
    public float ProbeDistance { get; set; } = 1f;

    /// <summary>How far above a foot the probe starts, so ground it is already standing in is still found.</summary>
    public float ProbeRise { get; set; } = 0.5f;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private static readonly Float3 Up = new(0f, 1f, 0f);

        private readonly FootGroundingDefinition _def;
        private ValueNodeInstance? _leftY, _rightY, _weight, _leftNormal, _rightNormal;

        public Instance(FootGroundingDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _leftY = context.OptionalValueNode(_def.LeftGroundYNodeIndex, ValueInputKind.Number);
            _rightY = context.OptionalValueNode(_def.RightGroundYNodeIndex, ValueInputKind.Number);
            _weight = context.OptionalValueNode(_def.WeightNodeIndex, ValueInputKind.Number);
            _leftNormal = context.OptionalValueNode(_def.LeftNormalNodeIndex, ValueInputKind.Vector);
            _rightNormal = context.OptionalValueNode(_def.RightNormalNodeIndex, ValueInputKind.Vector);
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);

            HumanoidRig? rig = context.Avatar?.Humanoid;
            if (rig is null)
                return;

            float weight = _weight is not null ? _weight.GetValue(context).AsFloat() : 1f;
            if (!(weight > 0f))
                return;

            GroundFoot(context, rig, HumanGoal.LeftFoot, _leftY, _leftNormal, weight);
            GroundFoot(context, rig, HumanGoal.RightFoot, _rightY, _rightNormal, weight);
        }

        private void GroundFoot(GraphContext context, HumanoidRig rig, HumanGoal goal, ValueNodeInstance? height, ValueNodeInstance? normal, float weight)
        {
            HumanBodyBone footBone = goal == HumanGoal.LeftFoot ? HumanBodyBone.LeftFoot : HumanBodyBone.RightFoot;
            if (!rig.HasBone(footBone))
                return;

            float groundY = height is not null ? height.GetValue(context).AsFloat() : 0f;
            Float3 worldNormal = normal is not null ? normal.GetValue(context).Vector : Up;

            Float3 footWorld = context.WorldTransform.TransformPoint(Pose.GetModelSpaceTransform(rig.GetSkeletonBoneIndex(footBone)).position);

            if (_def.ProbeGround && context.Ground is { } ground)
            {
                Float3 from = new(footWorld.X, footWorld.Y + _def.ProbeRise, footWorld.Z);
                // Nothing underfoot means nothing to stand on, so the foot is left where the pose puts it.
                if (!ground.Raycast(from, -Up, _def.ProbeRise + _def.ProbeDistance, out Float3 hit, out Float3 hitNormal))
                    return;

                groundY = hit.Y;
                worldNormal = hitNormal;
            }

            Float3 groundPoint = context.WorldToCharacter(new Float3(footWorld.X, groundY, footWorld.Z));
            Float3 groundNormal = context.WorldNormalToCharacter(worldNormal);
            FootGrounding.Ground(Pose, rig, goal, groundPoint, groundNormal, weight);
        }
    }
}

// ---- IK rig (multi effector IK) -------------------------------------------------------------

/// <summary>One effector of an <see cref="IKRigDefinition"/>: a bone chain driven to a target value node.</summary>
public sealed class IKEffectorInfo
{
    public IKEffectorInfo(string name, int[] chain, int targetNodeIndex, int weightNodeIndex = -1, float defaultWeight = 1f)
    { Name = name; Chain = chain; TargetNodeIndex = targetNodeIndex; WeightNodeIndex = weightNodeIndex; DefaultWeight = defaultWeight; }

    public string Name { get; }
    public int[] Chain { get; }
    public int TargetNodeIndex { get; }
    public int WeightNodeIndex { get; }
    public float DefaultWeight { get; }
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
        private ValueNodeInstance?[] _weights = null!;

        public Instance(IKRigDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);

            _rig = new IKRig();
            int n = _def.Effectors.Length;
            _effectors = new IKEffector[n];
            _targets = new ValueNodeInstance[n];
            _weights = new ValueNodeInstance?[n];
            for (int i = 0; i < n; i++)
            {
                IKEffectorInfo info = _def.Effectors[i];
                _effectors[i] = _rig.AddEffector(info.Name, info.Chain);
                _targets[i] = context.ValueNode(info.TargetNodeIndex, ValueInputKind.Vector | ValueInputKind.Target);
                _weights[i] = context.OptionalValueNode(info.WeightNodeIndex, ValueInputKind.Number);
            }
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);

            for (int i = 0; i < _effectors.Length; i++)
            {
                bool resolved = IKGoalResolver.TryResolve(_targets[i].GetValue(context), Pose, context, out Float3 goal);
                _effectors[i].Target = goal;
                _effectors[i].Weight = !resolved ? 0f : _weights[i] is not null ? _weights[i]!.GetValue(context).AsFloat() : _def.Effectors[i].DefaultWeight;
            }
            _rig.Solve(Pose);
        }
    }
}
