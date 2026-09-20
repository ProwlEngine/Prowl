using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>
/// An animation graph definition: a flat list of node definitions, named control parameters, and a
/// root pose node. This is the asset/editor-authored data and is also built fluently from code for
/// NuGet users with no editor. Create an <see cref="AnimationGraphInstance"/> to play it.
/// </summary>
public sealed class AnimationGraph
{
    private readonly List<GraphNodeDefinition> _nodes = new();
    private readonly List<ControlParameterDefinition> _parameters = new();
    private readonly Dictionary<string, int> _parameterByName = new();
    private readonly Dictionary<string, int> _nodeByName = new();
    private int _rootIndex = -1;

    public int NodeCount => _nodes.Count;
    public int RootIndex => _rootIndex;
    public IReadOnlyList<GraphNodeDefinition> Nodes => _nodes;
    public IReadOnlyList<ControlParameterDefinition> Parameters => _parameters;

    /// <summary>Adds any node definition and returns its index.</summary>
    public int AddNode(GraphNodeDefinition node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Index >= 0)
            throw new ArgumentException("That node already belongs to a graph.", nameof(node));
        if (!string.IsNullOrEmpty(node.Name))
            EnsureNameFree(node.Name, -1);

        var parameter = node as ControlParameterDefinition;
        if (parameter is not null)
        {
            if (string.IsNullOrEmpty(parameter.Name))
                throw new ArgumentException("A control parameter needs a name.", nameof(node));
            if (_parameterByName.ContainsKey(parameter.Name))
                throw new ArgumentException($"Duplicate control parameter '{parameter.Name}'.", nameof(node));
        }

        node.Index = _nodes.Count;
        _nodes.Add(node);
        if (!string.IsNullOrEmpty(node.Name))
            _nodeByName[node.Name] = node.Index;
        if (parameter is not null)
        {
            parameter.ParameterIndex = _parameters.Count;
            _parameters.Add(parameter);
            _parameterByName[parameter.Name!] = parameter.ParameterIndex;
        }
        return node.Index;
    }

    /// <summary>Assigns a lookup name to a node (for editor wiring / debugging).</summary>
    public int NameNode(int nodeIndex, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        EnsureNameFree(name, nodeIndex);
        string? previous = _nodes[nodeIndex].Name;
        if (!string.IsNullOrEmpty(previous) && _nodeByName.TryGetValue(previous, out int owner) && owner == nodeIndex)
            _nodeByName.Remove(previous);
        _nodes[nodeIndex].Name = name;
        _nodeByName[name] = nodeIndex;
        return nodeIndex;
    }

    private void EnsureNameFree(string name, int nodeIndex)
    {
        if (_nodeByName.TryGetValue(name, out int existing) && existing != nodeIndex)
            throw new ArgumentException($"The name '{name}' is already used by node {existing}.", nameof(name));
    }

    /// <summary>Finds a node index by its name, or -1.</summary>
    public int GetNodeIndex(string name) => _nodeByName.TryGetValue(name, out int index) ? index : -1;

    /// <summary>Adds a control parameter and returns its node index.</summary>
    public int AddControlParameter(string name, AnimationValueType type, ParameterValue defaultValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return AddNode(new ControlParameterDefinition(name, type, defaultValue));
    }

    public int AddFloatParameter(string name, float defaultValue = 0f) => AddControlParameter(name, AnimationValueType.Float, ParameterValue.FromFloat(defaultValue));
    public int AddBoolParameter(string name, bool defaultValue = false) => AddControlParameter(name, AnimationValueType.Bool, ParameterValue.FromBool(defaultValue));
    public int AddIntParameter(string name, int defaultValue = 0) => AddControlParameter(name, AnimationValueType.Int, ParameterValue.FromInt(defaultValue));
    public int AddVectorParameter(string name, Float3 defaultValue = default) => AddControlParameter(name, AnimationValueType.Vector, ParameterValue.FromVector(defaultValue));

    public int AddClip(AnimationClipBase clip, bool loop = true)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return AddNode(new ClipNodeDefinition(clip) { Loop = loop });
    }

    /// <summary>Wires optional bool value nodes that drive a clip node's reverse playback and time reset (-1 = none).</summary>
    public void SetClipDrivers(int clipNodeIndex, int playInReverseNodeIndex = -1, int resetTimeNodeIndex = -1)
    {
        if (_nodes[clipNodeIndex] is not ClipNodeDefinition clip)
            throw new ArgumentException($"Node {clipNodeIndex} is not a clip node.", nameof(clipNodeIndex));
        clip.PlayInReverseNodeIndex = playInReverseNodeIndex;
        clip.ResetTimeNodeIndex = resetTimeNodeIndex;
    }

    public int AddReferencePose() => AddNode(new ReferencePoseDefinition());

    /// <summary>Adds a node that outputs the zero (additive-identity) pose.</summary>
    public int AddZeroPose() => AddNode(new ZeroPoseDefinition());

    /// <summary>Adds a node that samples a clip at a normalized time from a float value node (a static pose).</summary>
    public int AddAnimationPose(AnimationClipBase clip, int timeNodeIndex)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return AddNode(new AnimationPoseDefinition(clip, timeNodeIndex));
    }

    /// <summary>Adds a node that forwards a child pose unchanged (a named wiring point).</summary>
    public int AddPassthrough(int child) => AddNode(new PassthroughDefinition(child));

    public int AddSelector(int selectorNodeIndex, int[] children) => AddNode(new SelectorDefinition(selectorNodeIndex, children));

    /// <summary>
    /// Adds a selector that plays a random child, picking again each time the chosen one finishes.
    /// <paramref name="weights"/> gives each child a relative chance, or null for an even spread.
    /// </summary>
    public int AddRandomSelector(IReadOnlyList<int> children, IReadOnlyList<float>? weights = null, uint seed = 0)
        => AddNode(new RandomSelectorDefinition(children, weights) { Seed = seed });

    /// <summary>Adds a node that plays its children one after another, optionally looping the whole chain.</summary>
    public int AddSequence(IReadOnlyList<int> children, bool loop = false) => AddNode(new SequenceDefinition(children) { Loop = loop });

    /// <summary>Adds a selector that picks the child whose bool condition is first true (condition -1 = always).</summary>
    public int AddConditionSelector(IReadOnlyList<(int Child, int ConditionNode)> entries) => AddNode(new ConditionSelectorDefinition(entries));

    /// <summary>
    /// Names a value node as a virtual parameter so it can be referenced by name like a control
    /// parameter (a reusable computed sub-graph). Returns the same value node index.
    /// </summary>
    public int AddVirtualParameter(string name, int valueNodeIndex)
    {
        if (_nodes[valueNodeIndex] is not ValueNodeDefinition)
            throw new ArgumentException($"Node {valueNodeIndex} is not a value node.", nameof(valueNodeIndex));
        return NameNode(valueNodeIndex, name);
    }

    public int AddBlend2D(int xParam, int yParam, (int Child, Float2 Position)[] samples, bool loop = true)
        => AddNode(new Blend2DDefinition(xParam, yParam, samples) { Loop = loop });

    public int AddSpeedScale(int child, int speedNodeIndex = -1, float defaultSpeed = 1f) => AddNode(new SpeedScaleDefinition(child, speedNodeIndex, defaultSpeed));

    public int AddOverrideLayer(int basePose, int layerPose, int weightNodeIndex = -1, float defaultWeight = 1f, BoneMask? mask = null)
        => AddNode(new OverrideLayerDefinition(basePose, layerPose, weightNodeIndex, defaultWeight, mask));

    /// <summary>
    /// Blends N layers over a base pose, each with its own weight, dynamic bone mask, and additive flag.
    /// Only the base drives root motion unless <paramref name="onlySampleBaseRootMotion"/> is false.
    /// </summary>
    public int AddLayerBlend(int basePose, IReadOnlyList<LayerInfo> layers, bool onlySampleBaseRootMotion = true)
        => AddNode(new LayerBlendDefinition(basePose, layers) { OnlySampleBaseRootMotion = onlySampleBaseRootMotion });

    public int AddAdditiveLayer(int basePose, int layerPose, int weightNodeIndex = -1, float defaultWeight = 1f, BoneMask? mask = null)
        => AddNode(new OverrideLayerDefinition(basePose, layerPose, weightNodeIndex, defaultWeight, mask) { Additive = true });

    /// <summary>
    /// Adds a node that absorbs pose jumps in its child by decaying the gap over
    /// <paramref name="blendSeconds"/>. With no trigger node it starts a blend whenever the child's pose
    /// jumps on its own.
    /// </summary>
    public int AddInertialBlend(int child, float blendSeconds, int triggerNodeIndex = -1)
        => AddNode(new InertializeDefinition(child, blendSeconds, triggerNodeIndex));

    /// <summary>Adds a node that freezes its child's pose while a bool value node is true.</summary>
    public int AddPoseSnapshot(int child, int holdNodeIndex) => AddNode(new PoseSnapshotDefinition(child, holdNodeIndex));

    /// <summary>
    /// Adds a node turning its child into an additive pose, measured against the reference pose or
    /// against another pose node.
    /// </summary>
    public int AddMakeAdditive(int child, int referenceChild = -1) => AddNode(new MakeAdditiveDefinition(child, referenceChild));

    /// <summary>Adds a node blending any number of poses by their own (normalized) weights.</summary>
    public int AddWeightedBlend(IReadOnlyList<WeightedPose> inputs) => AddNode(new WeightedBlendDefinition(inputs));

    /// <summary>Adds a node easing toward its child, closing half the remaining gap every half life.</summary>
    public int AddPoseSmoothing(int child, float halfLifeSeconds) => AddNode(new PoseSmoothingDefinition(child, halfLifeSeconds));

    /// <summary>Adds a value node reading a float channel off a pose node (without playing it).</summary>
    public int AddPoseChannel(int poseNodeIndex, StringID channel, float defaultValue = 0f)
        => AddNode(new PoseChannelDefinition(poseNodeIndex, channel) { DefaultValue = defaultValue });

    /// <summary>Adds a node writing float channels onto its child's pose from graph values.</summary>
    public int AddFloatChannelLayer(int child, IReadOnlyList<ChannelDriver> drivers) => AddNode(new FloatChannelLayerDefinition(child, drivers));

    /// <summary>
    /// Adds a node driving a float channel from how far a bone has turned from its reference pose,
    /// mapping <paramref name="fromDegrees"/>..<paramref name="toDegrees"/> onto 0..1 by default.
    /// </summary>
    public int AddDrivenChannel(int child, StringID bone, StringID channel, float fromDegrees, float toDegrees)
        => AddNode(new DrivenChannelDefinition(child, bone, channel, fromDegrees, toDegrees));

    /// <summary>Adds a node turning one bone so its aim axis points at a target value.</summary>
    public int AddAimConstraint(int child, StringID bone, int targetNodeIndex, Float3 aimAxis = default)
    {
        var definition = new AimConstraintDefinition(child, bone, targetNodeIndex);
        if (Float3.LengthSquared(aimAxis) > 0f)
            definition.AimAxis = aimAxis;
        return AddNode(definition);
    }

    /// <summary>Adds a node copying another bone's model space transform onto a bone.</summary>
    public int AddCopyConstraint(int child, StringID bone, StringID sourceBone, TransformChannels channels = TransformChannels.PositionAndRotation)
        => AddNode(new CopyConstraintDefinition(child, bone, sourceBone, channels));

    /// <summary>Adds a node copying a target value onto a bone.</summary>
    public int AddCopyConstraint(int child, StringID bone, int sourceNodeIndex, TransformChannels channels = TransformChannels.PositionAndRotation)
        => AddNode(new CopyConstraintDefinition(child, bone, sourceNodeIndex, channels));

    /// <summary>Adds a node spreading a joint's roll onto the twist bones along a limb.</summary>
    public int AddTwistDistribution(int child, StringID driver, IReadOnlyList<(StringID Bone, float Share)> twistBones, Float3 axis = default)
    {
        var definition = new TwistDistributionDefinition(child, driver, twistBones);
        if (Float3.LengthSquared(axis) > 0f)
            definition.Axis = axis;
        return AddNode(definition);
    }

    /// <summary>
    /// Adds a node giving a bone chain secondary motion: it lags behind the animation, swings with the
    /// character and settles back under a spring. The chain is listed from its root outward.
    /// </summary>
    public int AddSpringBones(int child, IReadOnlyList<StringID> chain, float stiffness = 40f, float damping = 6f, Float3 gravity = default)
        => AddNode(new SpringBonesDefinition(child, chain) { Stiffness = stiffness, Damping = damping, Gravity = gravity });

    /// <summary>
    /// Adds spring motion to <paramref name="boneCount"/> bones starting at <paramref name="root"/>,
    /// following the first child each step. Stack two nodes to give a chain's tip its own feel: the
    /// second is rooted where the first ends.
    /// </summary>
    public int AddSpringBones(int child, StringID root, int boneCount, float stiffness = 40f, float damping = 6f, Float3 gravity = default)
        => AddNode(new SpringBonesDefinition(child, root, boneCount) { Stiffness = stiffness, Damping = damping, Gravity = gravity });

    /// <summary>
    /// Adds a node pinning a foot in world space while a bool value node reads true, so a planted foot
    /// stops sliding. The three bones run from the hip down to the foot.
    /// </summary>
    public int AddFootLock(int child, StringID upper, StringID mid, StringID end, int lockNodeIndex)
        => AddNode(new FootLockDefinition(child, upper, mid, end, lockNodeIndex));

    /// <summary>
    /// Adds a node bending a travelling clip's playback rate so it covers ground at the speed a float
    /// value node asks for.
    /// </summary>
    public int AddStrideWarp(int child, int desiredSpeedNodeIndex, float naturalSpeed = 0f)
        => AddNode(new StrideWarpDefinition(child, desiredSpeedNodeIndex) { NaturalSpeed = naturalSpeed });

    /// <summary>Adds a node keeping only part of its child's root motion.</summary>
    public int AddRootMotionFilter(int child, RootMotionChannels keep, float scale = 1f)
        => AddNode(new RootMotionFilterDefinition(child, keep) { Scale = scale });

    /// <summary>
    /// Adds a node fed by the host rather than by playback: write into the instance's source pose each
    /// frame. A pose from another rig is matched by bone name, or retargeted when both are humanoid.
    /// </summary>
    public int AddExternalPose(Skeleton sourceSkeleton, PoseTransferMode mode = PoseTransferMode.Auto)
        => AddNode(new ExternalPoseDefinition(sourceSkeleton) { Mode = mode });

    /// <summary>Adds a host fed pose node whose source belongs to another avatar.</summary>
    public int AddExternalPose(Avatar sourceAvatar, PoseTransferMode mode = PoseTransferMode.Auto)
        => AddNode(new ExternalPoseDefinition(sourceAvatar) { Mode = mode });

    /// <summary>
    /// Adds a node blending two poses in muscle space, optionally masked to parts of the body. Needs a
    /// humanoid avatar.
    /// </summary>
    public int AddMuscleLayer(int basePose, int layerPose, int weightNodeIndex = -1, HumanPoseMask? mask = null, bool additive = false, int referenceNodeIndex = -1)
        => AddNode(new MuscleLayerDefinition(basePose, layerPose, weightNodeIndex) { Mask = mask, Additive = additive, ReferenceNodeIndex = referenceNodeIndex });

    public int AddMirror(int child) => AddNode(new MirrorDefinition(child));

    /// <summary>
    /// Plants the humanoid feet on world space ground heights (and optional world space ground normal
    /// vectors) via IK. Requires a humanoid avatar instance.
    /// </summary>
    public int AddFootGrounding(int child, int leftGroundYNodeIndex = -1, int rightGroundYNodeIndex = -1, int weightNodeIndex = -1, int leftNormalNodeIndex = -1, int rightNormalNodeIndex = -1)
        => AddNode(new FootGroundingDefinition(child, leftGroundYNodeIndex, rightGroundYNodeIndex, weightNodeIndex, leftNormalNodeIndex, rightNormalNodeIndex));

    /// <summary>Re-heads a clip's root motion toward a Float3 direction value node.</summary>
    public int AddOrientationWarp(int clipChild, int directionNodeIndex)
        => AddNode(new OrientationWarpDefinition(clipChild, directionNodeIndex, isAngleOffset: false));

    /// <summary>Re-heads a clip's root motion by a float angle offset (degrees about up) from a value node.</summary>
    public int AddOrientationWarpAngle(int clipChild, int angleDegreesNodeIndex)
        => AddNode(new OrientationWarpDefinition(clipChild, angleDegreesNodeIndex, isAngleOffset: true));

    /// <summary>Warps a clip's root motion so total travel matches a desired character-space displacement (Float3 value node).</summary>
    public int AddTargetWarp(int clipChild, int displacementNodeIndex)
        => AddNode(new TargetWarpDefinition(clipChild, displacementNodeIndex));

    /// <summary>Solves a multi-effector IK rig over a child pose, each effector driven by a target value node.</summary>
    public int AddIKRig(int child, IReadOnlyList<IKEffectorInfo> effectors) => AddNode(new IKRigDefinition(child, effectors));

    public int AddTwoBoneIK(int child, int targetNodeIndex, int upper, int mid, int end, int weightNodeIndex = -1, float defaultWeight = 1f)
        => AddNode(new TwoBoneIKDefinition(child, targetNodeIndex, upper, mid, end, weightNodeIndex, defaultWeight));

    public int AddLookAt(int child, int targetNodeIndex, float clamp = 0.3f, float body = 0.4f, float head = 1f, float eyes = 1f, float weight = 1f)
        => AddNode(new LookAtDefinition(child, targetNodeIndex, weight, clamp, body, head, eyes));

    public int AddRootMotionOverride(int child, float speedScale = 1f, float maxLinearSpeed = 0f, float maxAngularDegrees = 0f)
        => AddNode(new RootMotionOverrideDefinition(child, speedScale, maxLinearSpeed, maxAngularDegrees));

    public int AddBlend1D(int parameterNodeIndex, IReadOnlyList<(int Child, float Threshold)> entries, bool loop = true)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
            throw new ArgumentException("Blend1D needs at least one entry.", nameof(entries));
        return AddNode(new Blend1DDefinition(parameterNodeIndex, entries) { Loop = loop });
    }

    // ---- Value nodes ----------------------------------------------------------------------------

    /// <summary>A 1D blend across clip nodes parameterized by each clip's average speed, driven by a desired speed.</summary>
    public int AddVelocityBlend(int speedParameterNodeIndex, IReadOnlyList<int> clipNodeIndices, bool loop = true)
        => AddNode(new VelocityBlendDefinition(speedParameterNodeIndex, clipNodeIndices) { Loop = loop });

    public int AddConstId(StringID value) => AddNode(new ConstIdDefinition(value));
    public int AddIdParameter(string name, StringID defaultValue = default) => AddControlParameter(name, AnimationValueType.Id, ParameterValue.FromId(defaultValue));
    public int AddIdComparison(int input, IdComparison comparison, IReadOnlyList<StringID> ids) => AddNode(new IdComparisonDefinition(input, comparison, ids));
    public int AddIdToFloat(int input, IReadOnlyList<StringID> ids, IReadOnlyList<float> values, float defaultValue) => AddNode(new IdToFloatDefinition(input, ids, values, defaultValue));

    /// <summary>Caches a value node (sample on entry, sample and hold when a bool driver node is given, or freeze on exit).</summary>
    public int AddCachedValue(int input, int sampleWhenNodeIndex = -1, CachedValueMode mode = CachedValueMode.OnEntry)
    {
        if (_nodes[input] is not ValueNodeDefinition value)
            throw new ArgumentException($"Node {input} is not a value node.", nameof(input));
        return AddNode(new CachedValueDefinition(input, value.ValueType, sampleWhenNodeIndex, mode));
    }

    public int AddConstFloat(float value) => AddNode(new ConstValueDefinition(ParameterValue.FromFloat(value)));
    public int AddConstBool(bool value) => AddNode(new ConstValueDefinition(ParameterValue.FromBool(value)));
    public int AddConstInt(int value) => AddNode(new ConstValueDefinition(ParameterValue.FromInt(value)));
    public int AddConstVector(Float3 value) => AddNode(new ConstValueDefinition(ParameterValue.FromVector(value)));

    /// <summary>Adds a value node that wanders smoothly, for small imperfections and idle motion.</summary>
    public int AddNoise(float frequency = 1f, float amplitude = 1f, uint seed = 1, int octaves = 1)
        => AddNode(new NoiseValueDefinition { Frequency = frequency, Amplitude = amplitude, Seed = seed, Octaves = octaves });

    /// <summary>Adds a value node counting the seconds since it became active, optionally looping.</summary>
    public int AddTimer(float loopSeconds = 0f, bool normalized = false, int resetNodeIndex = -1)
        => AddNode(new TimerValueDefinition { LoopSeconds = loopSeconds, Normalized = normalized, ResetNodeIndex = resetNodeIndex });

    /// <summary>Adds a value node that follows another value on a spring, so it lags and settles.</summary>
    public int AddFloatSpring(int inputNodeIndex, float frequency = 4f, float damping = 1f)
        => AddNode(new FloatSpringDefinition(inputNodeIndex) { Frequency = frequency, Damping = damping });

    /// <summary>Adds a value node shaping another value through an authored curve.</summary>
    public int AddCurve(int inputNodeIndex, AnimationCurve curve) => AddNode(new CurveValueDefinition(inputNodeIndex, curve));

    /// <summary>
    /// Adds a node passing a pose through for inspection, which also repairs any bone that has gone to
    /// NaN so one bad solve cannot spread.
    /// </summary>
    public int AddDebugPose(int child, Action<Pose>? inspect = null) => AddNode(new DebugPoseDefinition(child) { Inspect = inspect });

    public int AddFloatMath(int a, int b, FloatMathOp op) => AddNode(new FloatMathDefinition(a, b, op));
    public int AddFloatCompare(int a, int b, CompareOp op) => AddNode(new FloatCompareDefinition(a, b, op));

    /// <summary>Compares a value node against a constant, producing a bool.</summary>
    public int AddFloatCompare(int input, CompareOp op, float constant)
        => AddFloatCompare(input, AddConstFloat(constant), op);

    public int AddFloatRemap(int input, float inMin, float inMax, float outMin, float outMax)
        => AddNode(new FloatRemapDefinition(input, inMin, inMax, outMin, outMax));
    public int AddFloatClamp(int input, float min, float max) => AddNode(new FloatClampDefinition(input, min, max));
    public int AddFloatAbs(int input) => AddNode(new FloatAbsDefinition(input));
    public int AddFloatSwitch(int selector, int trueValue, int falseValue) => AddNode(new FloatSwitchDefinition(selector, trueValue, falseValue));
    public int AddFloatRangeComparison(int input, float min, float max, bool inclusive = true) => AddNode(new FloatRangeComparisonDefinition(input, min, max, inclusive));
    public int AddFloatAngleMath(int input, AngleOp op) => AddNode(new FloatAngleMathDefinition(input, op));
    public int AddFloatEase(int input, float easeTime, EasingOp easing, bool useStartValue = false, float startValue = 0f)
        => AddNode(new FloatEaseDefinition(input, easeTime, easing, useStartValue, startValue));
    public int AddFloatSelector(IReadOnlyList<int> conditionNodes, IReadOnlyList<float> values, float defaultValue, float easeTime = 0.2f, EasingOp easing = EasingOp.None)
        => AddNode(new FloatSelectorDefinition(conditionNodes, values, defaultValue, easeTime, easing));

    public int AddAnd(int a, int b) => AddNode(new BoolLogicDefinition(a, b, BoolOp.And));
    public int AddOr(int a, int b) => AddNode(new BoolLogicDefinition(a, b, BoolOp.Or));
    public int AddNot(int a) => AddNode(new BoolLogicDefinition(a, -1, BoolOp.Not));

    public int AddVectorCreate(int x, int y, int z) => AddNode(new VectorCreateDefinition(x, y, z));
    public int AddVectorInfo(int input, VectorComponent component) => AddNode(new VectorInfoDefinition(input, component));
    public int AddVectorNegate(int input) => AddNode(new VectorNegateDefinition(input));

    public int AddTargetParameter(string name, Target defaultValue = default) => AddControlParameter(name, AnimationValueType.Target, ParameterValue.FromTarget(defaultValue));
    public int AddIsTargetSet(int input) => AddNode(new IsTargetSetDefinition(input));
    public int AddTargetInfo(int input, TargetInfo info) => AddNode(new TargetInfoDefinition(input, info));
    public int AddTargetPoint(int input) => AddNode(new TargetPointDefinition(input));
    public int AddTargetOffset(int input, Quaternion rotationOffset, Float3 translationOffset) => AddNode(new TargetOffsetDefinition(input, rotationOffset, translationOffset));

    /// <summary>A bool true when a sampled id event is present this frame (any of the ids, or all when matchAll).</summary>
    public int AddIdEventCondition(IReadOnlyList<StringID> ids, bool matchAll = false) => AddNode(new IdEventConditionDefinition(ids, matchAll));

    /// <summary>A bool true when a single sampled id event is present this frame.</summary>
    public int AddIdEventCondition(StringID id) => AddNode(new IdEventConditionDefinition(new[] { id }, false));

    /// <summary>A bool true when a sampled foot event satisfies the given phase condition.</summary>
    public int AddFootEventCondition(FootPhaseCondition phaseCondition) => AddNode(new FootEventConditionDefinition(phaseCondition));

    /// <summary>A bool answering a transition-rule question about the sampled transition events.</summary>
    public int AddTransitionEventCondition(TransitionRuleCondition ruleCondition, StringID requireId = default)
        => AddNode(new TransitionEventConditionDefinition(ruleCondition, requireId));

    /// <summary>Reads timing (time-in-state / normalized progress) from a state machine's active state as a float.</summary>
    public int AddStateQuery(int stateMachineNodeIndex, StateQuery query) => AddNode(new StateQueryDefinition(stateMachineNodeIndex, query));

    /// <summary>A bool that becomes true once the state machine's current state has played past <paramref name="normalizedThreshold"/>.</summary>
    public int AddStateFinished(int stateMachineNodeIndex, float normalizedThreshold = 0.99f)
        => AddFloatCompare(AddStateQuery(stateMachineNodeIndex, StateQuery.NormalizedTime), CompareOp.GreaterOrEqual, normalizedThreshold);

    /// <summary>A bool that becomes true once the state machine has stayed in its current state for <paramref name="seconds"/>.</summary>
    public int AddStateTimeElapsed(int stateMachineNodeIndex, float seconds)
        => AddFloatCompare(AddStateQuery(stateMachineNodeIndex, StateQuery.TimeInState), CompareOp.GreaterOrEqual, seconds);

    /// <summary>Embeds another graph as a child sub-graph; returns its node index.</summary>
    public int AddReferencedGraph(AnimationGraph subGraph)
    {
        ArgumentNullException.ThrowIfNull(subGraph);
        return AddNode(new ReferencedGraphDefinition(subGraph));
    }

    /// <summary>Forwards a parent value node into a named control parameter of a referenced sub-graph.</summary>
    public void LinkGraphParameter(int referencedGraphNodeIndex, int parentValueNodeIndex, string childParameterName)
    {
        if (_nodes[referencedGraphNodeIndex] is not ReferencedGraphDefinition rg)
            throw new ArgumentException($"Node {referencedGraphNodeIndex} is not a referenced graph.", nameof(referencedGraphNodeIndex));
        rg.ParameterLinks.Add((parentValueNodeIndex, childParameterName));
    }

    /// <summary>Adds a runtime-swappable external graph slot (named for <see cref="AnimationGraphInstance.SetExternalGraph"/>).</summary>
    public int AddExternalGraphSlot(string slotName, int fallbackPoseNodeIndex = -1)
    {
        int index = AddNode(new ExternalGraphSlotDefinition(slotName, fallbackPoseNodeIndex));
        NameNode(index, slotName);
        return index;
    }

    /// <summary>Adds a bone-mask node that feathers seed weights (by bone id) down the hierarchy.</summary>
    public int AddBoneMask(IReadOnlyList<(StringID Bone, float Weight)> seeds) => AddNode(new HierarchicalBoneMaskDefinition(seeds));

    /// <summary>Adds a bone-mask node with a uniform fixed weight on every bone.</summary>
    public int AddFixedWeightBoneMask(float weight) => AddNode(new FixedWeightBoneMaskDefinition(weight));

    /// <summary>Adds a bone-mask node that lerps between two masks by a float value node.</summary>
    public int AddBoneMaskBlend(int maskA, int maskB, int blendNodeIndex) => AddNode(new BoneMaskBlendDefinition(maskA, maskB, blendNodeIndex));

    /// <summary>Adds a bone-mask node that selects one of several masks by an int value node.</summary>
    public int AddBoneMaskSelector(int selectorNodeIndex, IReadOnlyList<int> maskNodes) => AddNode(new BoneMaskSelectorDefinition(selectorNodeIndex, maskNodes));

    /// <summary>Adds an (initially empty) state machine and returns its node index.</summary>
    public int AddStateMachine() => AddNode(new StateMachineDefinition());

    /// <summary>Adds a state wrapping a content pose node; returns the state index within the machine.</summary>
    public int AddState(int stateMachineNodeIndex, int contentPoseNodeIndex, string? name = null)
    {
        StateMachineDefinition sm = StateMachine(stateMachineNodeIndex);
        sm.States.Add(new StateInfo { PoseNodeIndex = contentPoseNodeIndex, Name = name });
        return sm.States.Count - 1;
    }

    /// <summary>
    /// Adds a transition between two states, fired when <paramref name="conditionNodeIndex"/> is true
    /// (-1 = always). Returns the transition for further configuration (sync mode, easing, clamp).
    /// </summary>
    public TransitionInfo AddTransition(int stateMachineNodeIndex, int fromStateIndex, int toStateIndex, int conditionNodeIndex = -1, float duration = 0.2f)
    {
        StateMachineDefinition sm = StateMachine(stateMachineNodeIndex);
        var info = new TransitionInfo
        {
            TargetStateIndex = toStateIndex,
            ConditionNodeIndex = conditionNodeIndex,
            Duration = duration,
        };
        sm.States[fromStateIndex].Transitions.Add(info);
        return info;
    }

    public void SetStateMachineDefault(int stateMachineNodeIndex, int stateIndex)
        => StateMachine(stateMachineNodeIndex).DefaultStateIndex = stateIndex;

    private StateMachineDefinition StateMachine(int nodeIndex)
    {
        if (_nodes[nodeIndex] is not StateMachineDefinition sm)
            throw new ArgumentException($"Node {nodeIndex} is not a state machine.", nameof(nodeIndex));
        return sm;
    }

    /// <summary>Sets the root pose node that the graph outputs.</summary>
    public void SetRoot(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= _nodes.Count)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));
        if (_nodes[nodeIndex] is not PoseNodeDefinition)
            throw new ArgumentException("The root node must be a pose node.", nameof(nodeIndex));
        _rootIndex = nodeIndex;
    }

    public int GetParameterIndex(string name) => _parameterByName.TryGetValue(name, out int index) ? index : -1;

    /// <summary>
    /// Checks the graph has a root. Node references, pose node sharing and cycles are validated when an
    /// instance is created, which throws <see cref="GraphValidationException"/> naming the bad node.
    /// </summary>
    public void Validate()
    {
        if (_rootIndex < 0)
            throw new InvalidOperationException("The graph has no root node; call SetRoot.");
    }

    /// <summary>Creates a runtime instance bound to a skeleton.</summary>
    public AnimationGraphInstance CreateInstance(Skeleton skeleton)
    {
        Validate();
        return new AnimationGraphInstance(this, skeleton);
    }

    /// <summary>Creates a runtime instance bound to an avatar (enables humanoid nodes: mirror, look-at).</summary>
    public AnimationGraphInstance CreateInstance(Avatar avatar)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        Validate();
        return new AnimationGraphInstance(this, avatar.Skeleton, avatar);
    }

    /// <summary>Creates a runtime instance for sub-graph hosting, carrying the host's optional avatar.</summary>
    internal AnimationGraphInstance CreateInstance(Skeleton skeleton, Avatar? avatar)
    {
        Validate();
        return new AnimationGraphInstance(this, skeleton, avatar);
    }
}
