using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

// ---------------------------------------------------------------------------------------------
// Control parameter (external input)
// ---------------------------------------------------------------------------------------------

/// <summary>A graph input set from gameplay/script. Its value lives in the instance parameter store.</summary>
public sealed class ControlParameterDefinition : ValueNodeDefinition
{
    public ControlParameterDefinition(string name, AnimationValueType type, ParameterValue defaultValue)
    {
        Name = name;
        ValueTypeField = type;
        DefaultValue = defaultValue;
    }

    private AnimationValueType ValueTypeField { get; }
    public override AnimationValueType ValueType => ValueTypeField;

    public ParameterValue DefaultValue { get; }

    /// <summary>Index into the instance's parameter store.</summary>
    public int ParameterIndex { get; internal set; } = -1;

    public override GraphNodeInstance CreateInstance() => new ControlParameterInstance(this);
}

internal sealed class ControlParameterInstance : ValueNodeInstance
{
    private readonly ControlParameterDefinition _def;
    public ControlParameterInstance(ControlParameterDefinition def) => _def = def;
    protected override ParameterValue Compute(GraphContext context) => context.Parameters[_def.ParameterIndex];
}

// ---------------------------------------------------------------------------------------------
// Reference pose / zero pose / static clip pose
// ---------------------------------------------------------------------------------------------

/// <summary>Outputs the skeleton's reference (bind) pose.</summary>
public sealed class ReferencePoseDefinition : PoseNodeDefinition
{
    public override GraphNodeInstance CreateInstance() => new ReferencePoseInstance();
}

internal sealed class ReferencePoseInstance : PoseNodeInstance
{
    public override void Bind(GraphBindContext context)
    {
        Pose = new Pose(context.Skeleton);
        Pose.SetToReferencePose();
    }

    protected override void OnUpdate(GraphContext context) => RootMotionDelta = Transform3D.Identity;
}

/// <summary>Outputs the zero (additive-identity) pose.</summary>
public sealed class ZeroPoseDefinition : PoseNodeDefinition
{
    public override GraphNodeInstance CreateInstance() => new ZeroPoseInstance();
}

internal sealed class ZeroPoseInstance : PoseNodeInstance
{
    public override void Bind(GraphBindContext context)
    {
        Pose = new Pose(context.Skeleton);
        Pose.SetToZeroPose();
    }

    protected override void OnUpdate(GraphContext context) => RootMotionDelta = Transform3D.Identity;
}

/// <summary>Samples a clip at a normalized time from a value node (a static pose, no playback).</summary>
public sealed class AnimationPoseDefinition : PoseNodeDefinition
{
    public AnimationPoseDefinition(AnimationClipBase clip, int timeNodeIndex) { Clip = clip; TimeNodeIndex = timeNodeIndex; }
    public AnimationClipBase Clip { get; }
    public int TimeNodeIndex { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PoseNodeInstance
    {
        private readonly AnimationPoseDefinition _def;
        private SkeletonMapping? _mapping;
        private ValueNodeInstance _time = null!;
        public Instance(AnimationPoseDefinition def) => _def = def;

        public override SyncTrack SyncTrack => _def.Clip.SyncTrack;

        public override void Bind(GraphBindContext context)
        {
            Pose = new Pose(context.Skeleton);
            _mapping = _def.Clip.GetMappingTo(context.Skeleton);
            _time = context.ValueNode(_def.TimeNodeIndex, ValueInputKind.Number);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime) => Duration = _def.Clip.Duration;

        protected override void OnUpdate(GraphContext context)
        {
            float n = _time.GetValue(context).AsFloat();
            n = float.IsFinite(n) ? Maths.Clamp(n, 0f, 1f) : 0f;
            _def.Clip.GetPose(n, Pose, _mapping);
            PreviousTime = NormalizedTime = n;
            RootMotionDelta = Transform3D.Identity;
        }
    }
}

/// <summary>Forwards a child pose unchanged (useful as a named wiring/insertion point).</summary>
public sealed class PassthroughDefinition : PoseNodeDefinition
{
    public PassthroughDefinition(int child) => Child = child;
    public int Child { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly PassthroughDefinition _def;
        public Instance(PassthroughDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => BindChild(context, _def.Child);
    }
}

// ---------------------------------------------------------------------------------------------
// Clip
// ---------------------------------------------------------------------------------------------

/// <summary>Plays a clip, looping or clamping, sampling events and root motion.</summary>
public sealed class ClipNodeDefinition : PoseNodeDefinition
{
    public ClipNodeDefinition(AnimationClipBase clip) => Clip = clip;

    public AnimationClipBase Clip { get; }
    public bool Loop { get; set; } = true;

    /// <summary>Playback rate multiplier (0 or more). The node reports the clip duration divided by it.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    /// <summary>Optional bool value node; while true the clip plays backwards. -1 = none.</summary>
    public int PlayInReverseNodeIndex { get; set; } = -1;

    /// <summary>Optional bool value node; when it rises to true the clip time resets to the start. -1 = none.</summary>
    public int ResetTimeNodeIndex { get; set; } = -1;

    public override GraphNodeInstance CreateInstance() => new ClipNodeInstance(this);
}

internal sealed class ClipNodeInstance : PoseNodeInstance
{
    private readonly ClipNodeDefinition _def;
    private SkeletonMapping? _mapping;
    private ValueNodeInstance? _reverse;
    private ValueNodeInstance? _reset;
    private ClipCursor _cursor;
    private bool _resetWasSet;
    private bool _firstUpdate;

    public ClipNodeInstance(ClipNodeDefinition def) => _def = def;

    public override SyncTrack SyncTrack => _def.Clip.SyncTrack;

    /// <summary>The playback step taken by the last update (direction, wraps), used by warp nodes.</summary>
    internal PlaybackSpan LastSpan { get; private set; }

    /// <summary>The clip this node plays (used by warp nodes that wrap a clip).</summary>
    public AnimationClipBase Clip => _def.Clip;

    /// <summary>Average linear root speed (units/second) at the node's playback rate, 0 without root motion.</summary>
    public float AverageLinearSpeed => _def.Clip.AverageLinearSpeed * MathF.Max(_def.SpeedMultiplier, 0f);

    public override void Bind(GraphBindContext context)
    {
        Pose = new Pose(context.Skeleton);
        _mapping = _def.Clip.GetMappingTo(context.Skeleton);
        Duration = EffectiveDuration;
        _reverse = context.OptionalValueNode(_def.PlayInReverseNodeIndex, ValueInputKind.Number);
        _reset = context.OptionalValueNode(_def.ResetTimeNodeIndex, ValueInputKind.Number);
    }

    private float EffectiveDuration
    {
        get
        {
            float speed = _def.SpeedMultiplier;
            return speed > 1e-6f && float.IsFinite(speed) ? _def.Clip.Duration / speed : 0f;
        }
    }

    protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
    {
        Duration = EffectiveDuration;
        _cursor = default;
        _cursor.Time = initialTime.HasValue ? SyncTrack.GetPercentageThrough(initialTime.Value) : 0f;
        PreviousTime = NormalizedTime = _cursor.Time;
        LastSpan = new PlaybackSpan(_cursor.Time, _cursor.Time, 0, false, true);
        _resetWasSet = false;
        _firstUpdate = true;
    }

    protected override void OnUpdate(GraphContext context)
    {
        Duration = EffectiveDuration;

        PlaybackSpan span;
        if (context.SyncRange is { } range)
        {
            GraphSync.Resolve(SyncTrack, range, out float from, out float to, out bool backward, out int wraps);
            if (wraps > 0 && !_def.Loop)
            {
                to = backward ? 0f : 1f;
                wraps = 0;
            }
            span = new PlaybackSpan(from, to, wraps, backward, _firstUpdate);
            _cursor.Time = to;
            _cursor.LoopCount += backward ? -wraps : wraps;
        }
        else
        {
            bool reverse = _reverse is not null && _reverse.GetValue(context).AsBool();
            bool resetNow = false;
            if (_reset is not null)
            {
                bool resetSet = _reset.GetValue(context).AsBool();
                resetNow = resetSet && !_resetWasSet;
                _resetWasSet = resetSet;
            }
            if (resetNow)
                _cursor.Time = reverse ? 1f : 0f;

            float deltaSeconds = reverse ? -context.DeltaTime : context.DeltaTime;
            span = _cursor.Advance(deltaSeconds, Duration, _def.Loop, _firstUpdate || resetNow);
        }

        PreviousTime = span.From;
        NormalizedTime = span.To;
        LoopCount = _cursor.LoopCount;
        PlayingBackward = span.Backward;
        LastSpan = span;
        _firstUpdate = false;

        _def.Clip.GetPose(ApplyFrameSnap(NormalizedTime), Pose, _mapping);
        RootMotionDelta = _def.Clip.GetRootMotionDelta(span);
        _def.Clip.SampleEvents(span, context.Events, context.IsActiveBranch, NodeIndex);
    }

    /// <summary>Snaps the sample time to a frame when a SnapToFrame event covers the current time.</summary>
    private float ApplyFrameSnap(float normalized)
    {
        int frames = _def.Clip.FrameCount;
        if (frames <= 1)
            return normalized;

        IReadOnlyList<AnimationEvent> events = _def.Clip.Events;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] is not SnapToFrameEvent snap || !snap.IsActiveAt(normalized))
                continue;
            float frame = normalized * (frames - 1);
            float snapped = snap.Mode == FrameSnapMode.Floor ? MathF.Floor(frame) : MathF.Round(frame);
            return snapped / (frames - 1);
        }
        return normalized;
    }
}

// ---------------------------------------------------------------------------------------------
// Blend 1D (parameter-driven blend across sorted sources)
// ---------------------------------------------------------------------------------------------

/// <summary>Blends across N child pose nodes by a float parameter, picking the bracketing pair.</summary>
public sealed class Blend1DDefinition : PoseNodeDefinition
{
    public Blend1DDefinition(int parameterNodeIndex, IReadOnlyList<(int Child, float Threshold)> entries)
    {
        ParameterNodeIndex = parameterNodeIndex;
        var sorted = new List<(int Child, float Threshold)>(entries);
        sorted.Sort(static (a, b) => a.Threshold.CompareTo(b.Threshold));
        Entries = sorted.ToArray();
    }

    public int ParameterNodeIndex { get; }
    public (int Child, float Threshold)[] Entries { get; }

    /// <summary>When false the blend plays once and holds its last frame.</summary>
    public bool Loop { get; init; } = true;

    public override GraphNodeInstance CreateInstance() => new Blend1DInstance(this);
}

/// <summary>
/// Shared runtime for parameter driven 1D blends: brackets the sorted sources by the parameter,
/// builds the blended sync track of the pair, and plays both over the same sync range so they stay
/// phase locked. Only the one or two sources in use are updated.
/// </summary>
internal abstract class ParameterizedBlend1DInstance : PoseNodeInstance, IBlendWeights
{
    private readonly SyncTrack _blendedTrack = new();
    private ValueNodeInstance _parameter = null!;
    protected PoseNodeInstance[] Children = null!;
    protected (int Child, float Threshold)[] Entries = null!;
    private int _low, _high;
    private float _weight;

    public override SyncTrack SyncTrack => _blendedTrack;

    protected abstract int ParameterNodeIndex { get; }

    protected abstract bool Loop { get; }

    /// <summary>Resolves the sorted child sources and their 1D thresholds.</summary>
    protected abstract void BuildSources(GraphBindContext context, out PoseNodeInstance[] children, out (int Child, float Threshold)[] entries);

    public override void Bind(GraphBindContext context)
    {
        Pose = new Pose(context.Skeleton);
        _parameter = context.ValueNode(ParameterNodeIndex, ValueInputKind.Number);
        BuildSources(context, out Children, out Entries);
        if (Children.Length == 0)
            throw context.Error("a 1D blend needs at least one source.");
    }

    protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
    {
        foreach (PoseNodeInstance child in Children)
            child.Initialize(context, initialTime);
        EvaluateBlendSpace(context);
        PreviousTime = NormalizedTime = initialTime.HasValue ? _blendedTrack.GetPercentageThrough(initialTime.Value) : 0f;
    }

    protected override void OnShutdown(GraphContext context)
    {
        foreach (PoseNodeInstance child in Children)
            child.Shutdown(context);
    }

    protected override void OnUpdate(GraphContext context)
    {
        EvaluateBlendSpace(context);
        float previous = NormalizedTime;
        SyncTrackTimeRange range = GraphSync.UpdateRange(context, _blendedTrack, previous, Duration, Loop, out float current);

        PoseNodeInstance lowNode = Children[_low];
        GraphSync.UpdateSynchronized(context, lowNode, range);

        if (_low == _high)
        {
            Pose.CopyFrom(lowNode.Pose);
            RootMotionDelta = lowNode.RootMotionDelta;
        }
        else
        {
            PoseNodeInstance highNode = Children[_high];
            GraphSync.UpdateSynchronized(context, highNode, range);
            Blender.Blend(Pose, lowNode.Pose, highNode.Pose, _weight);
            RootMotionDelta = Blender.BlendRootMotionDeltas(lowNode.RootMotionDelta, highNode.RootMotionDelta, _weight);
            context.Events.BlendRanges(lowNode.SampledEventRange, highNode.SampledEventRange, _weight);
        }

        GraphSync.Resolve(_blendedTrack, range, out _, out _, out bool backward, out int wraps);
        PreviousTime = context.SyncRange.HasValue ? _blendedTrack.GetPercentageThrough(range.Start) : previous;
        NormalizedTime = current;
        PlayingBackward = backward;
        LoopCount += backward ? -wraps : wraps;
    }

    public float WeightOf(int childNodeIndex)
    {
        if (!IsInitialized || Children == null || Children.Length == 0) return 0f;

        float weight = 0f;
        if (Children[_low].NodeIndex == childNodeIndex) weight += _low == _high ? 1f : 1f - _weight;
        if (_low != _high && Children[_high].NodeIndex == childNodeIndex) weight += _weight;
        return weight;
    }

    private void EvaluateBlendSpace(GraphContext context)
    {
        float value = _parameter.GetValue(context).AsFloat();
        GraphSync.ResolvePair(Entries, value, out _low, out _high, out _weight);

        PoseNodeInstance low = Children[_low];
        PoseNodeInstance high = Children[_high];
        float timing = GraphSync.TimingWeight(low.Duration, high.Duration, _weight);
        _blendedTrack.SetToBlend(low.SyncTrack, high.SyncTrack, timing);
        Duration = SyncTrack.CalculateDurationSynchronized(low.Duration, high.Duration, low.SyncTrack.EventCount, high.SyncTrack.EventCount, _blendedTrack.EventCount, timing);
    }
}

internal sealed class Blend1DInstance : ParameterizedBlend1DInstance
{
    private readonly Blend1DDefinition _def;
    public Blend1DInstance(Blend1DDefinition def) => _def = def;

    protected override int ParameterNodeIndex => _def.ParameterNodeIndex;

    protected override bool Loop => _def.Loop;

    protected override void BuildSources(GraphBindContext context, out PoseNodeInstance[] children, out (int Child, float Threshold)[] entries)
    {
        children = new PoseNodeInstance[_def.Entries.Length];
        entries = new (int Child, float Threshold)[_def.Entries.Length];
        for (int i = 0; i < children.Length; i++)
        {
            children[i] = context.PoseNode(_def.Entries[i].Child);
            entries[i] = (i, _def.Entries[i].Threshold);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Velocity blend (1D blend parameterized by each child clip's average speed)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// A 1D blend whose thresholds are derived from each child clip's average linear root speed, driven
/// by a desired-speed float parameter. Every child must be a clip node.
/// </summary>
public sealed class VelocityBlendDefinition : PoseNodeDefinition
{
    public VelocityBlendDefinition(int speedParameterNodeIndex, IReadOnlyList<int> clipNodeIndices)
    {
        SpeedParameterNodeIndex = speedParameterNodeIndex;
        ClipNodeIndices = new List<int>(clipNodeIndices).ToArray();
    }

    public int SpeedParameterNodeIndex { get; }
    public int[] ClipNodeIndices { get; }

    /// <summary>When false the blend plays once and holds its last frame.</summary>
    public bool Loop { get; init; } = true;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ParameterizedBlend1DInstance
    {
        private readonly VelocityBlendDefinition _def;
        public Instance(VelocityBlendDefinition def) => _def = def;

        protected override int ParameterNodeIndex => _def.SpeedParameterNodeIndex;

        protected override bool Loop => _def.Loop;

        protected override void BuildSources(GraphBindContext context, out PoseNodeInstance[] children, out (int Child, float Threshold)[] entries)
        {
            int count = _def.ClipNodeIndices.Length;
            var pairs = new (PoseNodeInstance Node, float Speed)[count];
            for (int i = 0; i < count; i++)
            {
                if (context.PoseNode(_def.ClipNodeIndices[i]) is not ClipNodeInstance clip)
                    throw context.Error("velocity blend sources must be clip nodes.");
                pairs[i] = (clip, clip.AverageLinearSpeed);
            }
            Array.Sort(pairs, static (a, b) => a.Speed.CompareTo(b.Speed));

            children = new PoseNodeInstance[count];
            entries = new (int Child, float Threshold)[count];
            for (int i = 0; i < count; i++)
            {
                children[i] = pairs[i].Node;
                entries[i] = (i, pairs[i].Speed);
            }
        }
    }
}
