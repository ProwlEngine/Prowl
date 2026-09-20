using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

// ---- Selector -------------------------------------------------------------------------------

/// <summary>
/// Outputs one of N child pose nodes chosen by an int value node (clamped to the valid range). When
/// the choice changes, the old child is shut down and the new one starts from the beginning.
/// </summary>
public sealed class SelectorDefinition : PoseNodeDefinition
{
    public SelectorDefinition(int selectorNodeIndex, int[] children) { SelectorNodeIndex = selectorNodeIndex; Children = children; }
    public int SelectorNodeIndex { get; }
    public int[] Children { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : SwitchingSelectorInstance
    {
        private readonly SelectorDefinition _def;
        private ValueNodeInstance _selector = null!;
        public Instance(SelectorDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            _selector = context.ValueNode(_def.SelectorNodeIndex, ValueInputKind.Number);
            BindOptions(context, _def.Children);
        }

        protected override int Select(GraphContext context) => Math.Clamp(_selector.GetValue(context).AsInt(), 0, Options.Length - 1);
    }
}

/// <summary>Shared runtime for selectors that may switch child while playing.</summary>
internal abstract class SwitchingSelectorInstance : PoseNodeInstance
{
    protected PoseNodeInstance[] Options = null!;
    private int _selected = -1;

    public override SyncTrack SyncTrack => _selected >= 0 ? Options[_selected].SyncTrack : SyncTrack.Default;

    protected void BindOptions(GraphBindContext context, IReadOnlyList<int> optionIndices)
    {
        Pose = new Pose(context.Skeleton);
        if (optionIndices.Count == 0)
            throw context.Error("a selector needs at least one option.");
        Options = new PoseNodeInstance[optionIndices.Count];
        for (int i = 0; i < Options.Length; i++)
            Options[i] = context.PoseNode(optionIndices[i]);
    }

    protected abstract int Select(GraphContext context);

    protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
    {
        _selected = Select(context);
        Options[_selected].Initialize(context, initialTime);
        CopyTimingFrom(Options[_selected]);
    }

    protected override void OnShutdown(GraphContext context)
    {
        if (_selected >= 0)
            Options[_selected].Shutdown(context);
        _selected = -1;
    }

    protected override void OnUpdate(GraphContext context)
    {
        int selected = Select(context);
        if (selected != _selected)
        {
            Options[_selected].Shutdown(context);
            _selected = selected;
            Options[_selected].Initialize(context, null);
        }

        PoseNodeInstance child = Options[_selected];
        child.Update(context);
        CopyResultFrom(child);
    }
}

// ---- Blend 2D (two parameter blend space) ---------------------------------------------------

/// <summary>
/// Blends child pose nodes placed in a 2D parameter space. The samples are triangulated once: a
/// point inside the triangulation blends its triangle's three corners by barycentric weight, a point
/// outside is projected onto the nearest hull edge. All active children play phase locked.
/// </summary>
public sealed class Blend2DDefinition : PoseNodeDefinition
{
    public Blend2DDefinition(int xParam, int yParam, (int Child, Float2 Position)[] samples)
    {
        XParam = xParam;
        YParam = yParam;
        Samples = samples;
        var points = new Float2[samples.Length];
        for (int i = 0; i < points.Length; i++)
            points[i] = samples[i].Position;
        BlendSpace = new BlendSpace2D(points);
    }

    public int XParam { get; }
    public int YParam { get; }
    public (int Child, Float2 Position)[] Samples { get; }
    public BlendSpace2D BlendSpace { get; }

    /// <summary>When false the blend plays once and holds its last frame.</summary>
    public bool Loop { get; init; } = true;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PoseNodeInstance
    {
        private readonly Blend2DDefinition _def;
        private readonly SyncTrack _blendedTrack = new();
        private readonly (int Index, float Weight)[] _active = new (int, float)[3];
        private ValueNodeInstance _x = null!, _y = null!;
        private PoseNodeInstance[] _children = null!;
        private int _activeCount;
        public Instance(Blend2DDefinition def) => _def = def;

        public override SyncTrack SyncTrack => _blendedTrack;

        public override void Bind(GraphBindContext context)
        {
            Pose = new Pose(context.Skeleton);
            _x = context.ValueNode(_def.XParam, ValueInputKind.Number);
            _y = context.ValueNode(_def.YParam, ValueInputKind.Number);
            _children = new PoseNodeInstance[_def.Samples.Length];
            for (int i = 0; i < _children.Length; i++)
                _children[i] = context.PoseNode(_def.Samples[i].Child);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            foreach (PoseNodeInstance child in _children)
                child.Initialize(context, initialTime);
            EvaluateBlendSpace(context);
            PreviousTime = NormalizedTime = initialTime.HasValue ? _blendedTrack.GetPercentageThrough(initialTime.Value) : 0f;
        }

        protected override void OnShutdown(GraphContext context)
        {
            foreach (PoseNodeInstance child in _children)
                child.Shutdown(context);
        }

        protected override void OnUpdate(GraphContext context)
        {
            EvaluateBlendSpace(context);
            float previous = NormalizedTime;
            SyncTrackTimeRange range = GraphSync.UpdateRange(context, _blendedTrack, previous, Duration, _def.Loop, out float current);

            PoseNodeInstance first = _children[_active[0].Index];
            GraphSync.UpdateSynchronized(context, first, range);
            Pose.CopyFrom(first.Pose);
            Transform3D rootMotion = first.RootMotionDelta;
            SampledEventRange events = first.SampledEventRange;
            float accumulated = _active[0].Weight;

            for (int i = 1; i < _activeCount; i++)
            {
                PoseNodeInstance child = _children[_active[i].Index];
                GraphSync.UpdateSynchronized(context, child, range);
                float t = _active[i].Weight / (accumulated + _active[i].Weight);
                Blender.Blend(Pose, Pose, child.Pose, t);
                rootMotion = Blender.BlendRootMotionDeltas(rootMotion, child.RootMotionDelta, t);
                events = context.Events.BlendRanges(events, child.SampledEventRange, t);
                accumulated += _active[i].Weight;
            }

            RootMotionDelta = rootMotion;
            GraphSync.Resolve(_blendedTrack, range, out _, out _, out bool backward, out int wraps);
            PreviousTime = context.SyncRange.HasValue ? _blendedTrack.GetPercentageThrough(range.Start) : previous;
            NormalizedTime = current;
            PlayingBackward = backward;
            LoopCount += backward ? -wraps : wraps;
        }

        private void EvaluateBlendSpace(GraphContext context)
        {
            var point = new Float2(_x.GetValue(context).AsFloat(), _y.GetValue(context).AsFloat());
            _activeCount = _def.BlendSpace.Evaluate(point, _active);

            PoseNodeInstance first = _children[_active[0].Index];
            _blendedTrack.SetToBlend(first.SyncTrack, first.SyncTrack, 0f);
            float duration = first.Duration;
            float accumulated = _active[0].Weight;
            for (int i = 1; i < _activeCount; i++)
            {
                PoseNodeInstance child = _children[_active[i].Index];
                float t = GraphSync.TimingWeight(duration, child.Duration, _active[i].Weight / (accumulated + _active[i].Weight));
                int blendedCount = _blendedTrack.EventCount;
                _blendedTrack.SetToBlend(_blendedTrack, child.SyncTrack, t);
                duration = SyncTrack.CalculateDurationSynchronized(duration, child.Duration, blendedCount, child.SyncTrack.EventCount, _blendedTrack.EventCount, t);
                accumulated += _active[i].Weight;
            }
            Duration = duration;
        }
    }
}

// ---- Speed scale ----------------------------------------------------------------------------

/// <summary>
/// Scales the play rate of a child by a float value node (or a constant), clamped to 0 or more. The
/// node reports the child duration divided by the speed so synchronized parents play it faster too.
/// </summary>
public sealed class SpeedScaleDefinition : PoseNodeDefinition
{
    public SpeedScaleDefinition(int child, int speedNodeIndex, float defaultSpeed) { Child = child; SpeedNodeIndex = speedNodeIndex; DefaultSpeed = defaultSpeed; }
    public int Child { get; }
    public int SpeedNodeIndex { get; }
    public float DefaultSpeed { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly SpeedScaleDefinition _def;
        private ValueNodeInstance? _speed;
        public Instance(SpeedScaleDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _speed = context.OptionalValueNode(_def.SpeedNodeIndex, ValueInputKind.Number);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            Duration = ScaledDuration(Speed(context));
        }

        protected override void OnUpdate(GraphContext context)
        {
            float speed = Speed(context);
            if (context.SyncRange.HasValue)
            {
                Child.Update(context);
            }
            else
            {
                float saved = context.DeltaTime;
                context.DeltaTime = saved * speed;
                Child.Update(context);
                context.DeltaTime = saved;
            }
            CopyResultFrom(Child);
            Duration = ScaledDuration(speed);
        }

        private float Speed(GraphContext context)
        {
            float speed = _speed?.GetValue(context).AsFloat() ?? _def.DefaultSpeed;
            return float.IsFinite(speed) ? MathF.Max(0f, speed) : 0f;
        }

        private float ScaledDuration(float speed) => speed > 1e-6f ? Child.Duration / speed : 0f;
    }
}

// ---- Layers (override / additive) -----------------------------------------------------------

/// <summary>
/// Blends an overlay onto a base by weight, optionally through a bone mask (override layer). The
/// base alone drives root motion and timing, the layer is only updated while its weight is above 0.
/// </summary>
public sealed class OverrideLayerDefinition : PoseNodeDefinition
{
    public OverrideLayerDefinition(int basePose, int layerPose, int weightNodeIndex, float defaultWeight, BoneMask? mask)
    { Base = basePose; Layer = layerPose; WeightNodeIndex = weightNodeIndex; DefaultWeight = defaultWeight; Mask = mask; }
    public int Base { get; }
    public int Layer { get; }
    public int WeightNodeIndex { get; }
    public float DefaultWeight { get; }
    public BoneMask? Mask { get; }
    public bool Additive { get; init; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly OverrideLayerDefinition _def;
        private PoseNodeInstance _layer = null!;
        private ValueNodeInstance? _weight;
        public Instance(OverrideLayerDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Base);
            _layer = context.PoseNode(_def.Layer);
            _weight = context.OptionalValueNode(_def.WeightNodeIndex, ValueInputKind.Number);
            if (_def.Mask is not null && _def.Mask.Length != context.Skeleton.BoneCount)
                throw context.Error("the layer bone mask does not match the skeleton.");
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            _layer.Initialize(context, null);
        }

        protected override void OnShutdown(GraphContext context)
        {
            _layer.Shutdown(context);
            base.OnShutdown(context);
        }

        protected override void OnUpdate(GraphContext context)
        {
            Child.Update(context);
            CopyResultFrom(Child);

            float w = LayerBlending.Weight(_weight?.GetValue(context).AsFloat() ?? _def.DefaultWeight);
            if (w <= 0f)
                return;

            SyncTrackTimeRange? saved = context.SyncRange;
            context.SyncRange = null;
            _layer.Update(context);
            context.SyncRange = saved;
            context.Events.UpdateWeights(_layer.SampledEventRange, w);

            LayerBlending.Apply(Pose, _layer.Pose, w, _def.Mask, _def.Additive);
        }
    }
}

/// <summary>Applies one layer onto an accumulated pose.</summary>
internal static class LayerBlending
{
    /// <summary>A layer weight clamped to [0,1], with non finite values reading as 0.</summary>
    public static float Weight(float weight) => float.IsFinite(weight) ? Math.Clamp(weight, 0f, 1f) : 0f;

    public static void Apply(Pose accumulated, Pose layer, float weight, BoneMask? mask, bool additive)
    {
        if (additive)
        {
            if (mask is not null) Blender.AdditiveBlend(accumulated, accumulated, layer, weight, mask);
            else Blender.AdditiveBlend(accumulated, accumulated, layer, weight);
        }
        else
        {
            if (mask is not null) Blender.Blend(accumulated, accumulated, layer, weight, mask);
            else Blender.Blend(accumulated, accumulated, layer, weight);
        }
    }
}

// ---- Mirror ---------------------------------------------------------------------------------

/// <summary>Mirrors a humanoid child pose left/right (passthrough for non-humanoid avatars).</summary>
public sealed class MirrorDefinition : PoseNodeDefinition
{
    public MirrorDefinition(int child) => Child = child;
    public int Child { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly MirrorDefinition _def;
        public Instance(MirrorDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => BindChild(context, _def.Child);

        protected override void OnUpdate(GraphContext context)
        {
            Child.Update(context);
            CopyResultFrom(Child);
            if (context.Avatar is { IsHuman: true } avatar)
            {
                PoseMirror.Apply(avatar, Child.Pose, Pose);
                RootMotionDelta = PoseMirror.MirrorRootMotion(avatar, RootMotionDelta);
                context.Events.MirrorFootEvents(Child.SampledEventRange);
            }
        }
    }
}

// ---- Root motion override -------------------------------------------------------------------

/// <summary>
/// Scales/clamps a child's root motion. A <see cref="RootMotionEvent"/> sampled by the child's own
/// subtree suspends the override so the clip's root motion plays through.
/// </summary>
public sealed class RootMotionOverrideDefinition : PoseNodeDefinition
{
    public RootMotionOverrideDefinition(int child, float speedScale, float maxLinearSpeed, float maxAngularDegrees)
    { Child = child; SpeedScale = speedScale; MaxLinearSpeed = maxLinearSpeed; MaxAngularDegrees = maxAngularDegrees; }
    public int Child { get; }
    public float SpeedScale { get; }
    public float MaxLinearSpeed { get; }
    public float MaxAngularDegrees { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly RootMotionOverrideDefinition _def;
        public Instance(RootMotionOverrideDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => BindChild(context, _def.Child);

        protected override void OnUpdate(GraphContext context)
        {
            Child.Update(context);
            CopyResultFrom(Child);
            if (ChildSampledRootMotionEvent(context))
                return;

            var options = new RootMotionOverrideOptions
            {
                LinearSpeedScale = _def.SpeedScale,
                MaxLinearSpeed = _def.MaxLinearSpeed,
                MaxAngularSpeedDegrees = _def.MaxAngularDegrees,
            };
            RootMotionDelta = RootMotionWarp.Override(Child.RootMotionDelta, context.DeltaTime, options);
        }

        private bool ChildSampledRootMotionEvent(GraphContext context)
        {
            SampledEventRange range = Child.SampledEventRange;
            for (int i = range.Start; i < range.End; i++)
            {
                SampledEvent e = context.Events[i];
                if (!e.IsIgnored && e.Weight > 0f && e.Event is RootMotionEvent)
                    return true;
            }
            return false;
        }
    }
}

// ---- Layer blend (N stacked layers over a base) ---------------------------------------------

/// <summary>One layer of a <see cref="LayerBlendDefinition"/>: a pose plus how it is applied.</summary>
public sealed class LayerInfo
{
    public LayerInfo(int pose, int weightNodeIndex = -1, int maskNodeIndex = -1, bool additive = false, float defaultWeight = 1f)
    { Pose = pose; WeightNodeIndex = weightNodeIndex; MaskNodeIndex = maskNodeIndex; Additive = additive; DefaultWeight = defaultWeight; }

    public int Pose { get; }
    public int WeightNodeIndex { get; }
    public int MaskNodeIndex { get; }
    public bool Additive { get; }
    public float DefaultWeight { get; }

    /// <summary>Optional float value node scaling this layer's root motion contribution (-1 = the layer weight alone).</summary>
    public int RootMotionWeightNodeIndex { get; init; } = -1;

    /// <summary>When true, the layer plays over the base layer's sync range instead of its own clock.</summary>
    public bool Synchronized { get; init; }

    /// <summary>When true, the events this layer samples are marked ignored.</summary>
    public bool IgnoreEvents { get; init; }
}

/// <summary>
/// Blends N layers over a base pose. Each layer has its own weight (value node or constant), an
/// optional dynamic bone mask (mask value node), and an override/additive mode. A layer only plays
/// while its weight is above 0. By default only the base contributes root motion.
/// </summary>
public sealed class LayerBlendDefinition : PoseNodeDefinition
{
    public LayerBlendDefinition(int basePose, IReadOnlyList<LayerInfo> layers)
    {
        BasePose = basePose;
        Layers = new List<LayerInfo>(layers).ToArray();
    }

    public int BasePose { get; }
    public LayerInfo[] Layers { get; }

    /// <summary>When true (the default) layers never change the base's root motion.</summary>
    public bool OnlySampleBaseRootMotion { get; init; } = true;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly LayerBlendDefinition _def;
        private PoseNodeInstance[] _layers = null!;
        private ValueNodeInstance?[] _weights = null!;
        private ValueNodeInstance?[] _rootMotionWeights = null!;
        private BoneMaskNodeInstance?[] _masks = null!;

        public Instance(LayerBlendDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.BasePose);
            int n = _def.Layers.Length;
            _layers = new PoseNodeInstance[n];
            _weights = new ValueNodeInstance?[n];
            _rootMotionWeights = new ValueNodeInstance?[n];
            _masks = new BoneMaskNodeInstance?[n];
            for (int i = 0; i < n; i++)
            {
                LayerInfo layer = _def.Layers[i];
                _layers[i] = context.PoseNode(layer.Pose);
                _weights[i] = context.OptionalValueNode(layer.WeightNodeIndex, ValueInputKind.Number);
                _rootMotionWeights[i] = context.OptionalValueNode(layer.RootMotionWeightNodeIndex, ValueInputKind.Number);
                _masks[i] = layer.MaskNodeIndex >= 0 ? context.BoneMaskNode(layer.MaskNodeIndex) : null;
            }
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            for (int i = 0; i < _layers.Length; i++)
                _layers[i].Initialize(context, _def.Layers[i].Synchronized ? initialTime : null);
        }

        protected override void OnShutdown(GraphContext context)
        {
            foreach (PoseNodeInstance layer in _layers)
                layer.Shutdown(context);
            base.OnShutdown(context);
        }

        protected override void OnUpdate(GraphContext context)
        {
            Child.Update(context);
            CopyResultFrom(Child);
            Transform3D rootMotion = Child.RootMotionDelta;
            SyncTrackTimeRange baseRange = GraphSync.DirectedRange(Child.SyncTrack, Child.PreviousTime, Child.NormalizedTime, Child.PlayingBackward);

            for (int i = 0; i < _layers.Length; i++)
            {
                LayerInfo info = _def.Layers[i];
                float w = LayerBlending.Weight(_weights[i] is { } weightNode ? weightNode.GetValue(context).AsFloat() : info.DefaultWeight);
                if (w <= 0f)
                    continue;

                PoseNodeInstance layer = _layers[i];
                SyncTrackTimeRange? saved = context.SyncRange;
                context.SyncRange = info.Synchronized ? baseRange : null;
                layer.Update(context);
                context.SyncRange = saved;

                context.Events.UpdateWeights(layer.SampledEventRange, w);
                if (info.IgnoreEvents)
                    context.Events.MarkIgnored(layer.SampledEventRange);

                LayerBlending.Apply(Pose, layer.Pose, w, _masks[i]?.GetMask(context), info.Additive);

                if (!_def.OnlySampleBaseRootMotion)
                {
                    float rootMotionWeight = _rootMotionWeights[i] is { } rmNode ? LayerBlending.Weight(rmNode.GetValue(context).AsFloat()) : 1f;
                    RootMotionBlendMode mode = info.Additive ? RootMotionBlendMode.Additive : RootMotionBlendMode.Blend;
                    rootMotion = Blender.BlendRootMotionDeltas(rootMotion, layer.RootMotionDelta, w * rootMotionWeight, mode);
                }
            }

            RootMotionDelta = rootMotion;
        }
    }
}

// ---- Condition selector (first true bool picks a child) -------------------------------------

/// <summary>
/// Selects the child whose bool condition is first true (priority order); a condition index of -1 is
/// an always-true fallback. Only the selected child is updated.
/// </summary>
public sealed class ConditionSelectorDefinition : PoseNodeDefinition
{
    public ConditionSelectorDefinition(IReadOnlyList<(int Child, int ConditionNode)> entries)
        => Entries = new List<(int, int)>(entries).ToArray();

    public (int Child, int ConditionNode)[] Entries { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : SwitchingSelectorInstance
    {
        private readonly ConditionSelectorDefinition _def;
        private ValueNodeInstance?[] _conditions = null!;

        public Instance(ConditionSelectorDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            var children = new int[_def.Entries.Length];
            for (int i = 0; i < children.Length; i++)
                children[i] = _def.Entries[i].Child;
            BindOptions(context, children);

            _conditions = new ValueNodeInstance?[children.Length];
            for (int i = 0; i < children.Length; i++)
                _conditions[i] = context.OptionalValueNode(_def.Entries[i].ConditionNode, ValueInputKind.Number);
        }

        protected override int Select(GraphContext context)
        {
            for (int i = 0; i < _conditions.Length; i++)
                if (_conditions[i] is null || _conditions[i]!.GetValue(context).AsBool())
                    return i;
            return _conditions.Length - 1;
        }
    }
}
