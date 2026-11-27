using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Absorbs a pose discontinuity instead of cross fading through it: on a trigger, the gap to the new
/// pose decays to zero over the blend time.
/// </summary>
public sealed class InertializeDefinition : PoseNodeDefinition
{
    public InertializeDefinition(int child, float blendSeconds, int triggerNodeIndex = -1)
    {
        Child = child;
        BlendSeconds = blendSeconds;
        TriggerNodeIndex = triggerNodeIndex;
    }

    public int Child { get; }

    /// <summary>How long the gap takes to close, in seconds.</summary>
    public float BlendSeconds { get; }

    /// <summary>Bool value node; the blend starts when it rises to true. -1 means the node watches the child instead.</summary>
    public int TriggerNodeIndex { get; }

    /// <summary>
    /// With no trigger node, a bone turning further than this in one step, well past what it was doing
    /// the frame before, starts a blend by itself.
    /// </summary>
    public float AutoTriggerRadians { get; set; } = 0.1f;

    /// <summary>The same for a bone that moves rather than turns.</summary>
    public float AutoTriggerDistance { get; set; } = 0.02f;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly InertializeDefinition _def;
        private Inertializer _inertializer = null!;
        private ValueNodeInstance? _trigger;
        private bool _triggerWasSet;

        public Instance(InertializeDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _inertializer = new Inertializer(context.Skeleton);
            _trigger = context.OptionalValueNode(_def.TriggerNodeIndex, ValueInputKind.Number);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            _inertializer.Reset();
            _triggerWasSet = false;
        }

        protected override void OnUpdate(GraphContext context)
        {
            Child.Update(context);
            CopyTimingFrom(Child);
            RootMotionDelta = Child.RootMotionDelta;

            bool start = false;
            if (_trigger is not null)
            {
                bool set = _trigger.GetValue(context).AsBool();
                start = set && !_triggerWasSet;
                _triggerWasSet = set;
            }
            else
            {
                start = _inertializer.DetectJump(Child.Pose, _def.AutoTriggerRadians, _def.AutoTriggerDistance);
            }

            if (start)
                _inertializer.Begin(Child.Pose, _def.BlendSeconds);

            _inertializer.Apply(Child.Pose, Pose, context.DeltaTime);
        }
    }
}

/// <summary>Passes a child through until its hold input turns true, then keeps the pose captured at that moment.</summary>
public sealed class PoseSnapshotDefinition : PoseNodeDefinition
{
    public PoseSnapshotDefinition(int child, int holdNodeIndex)
    {
        Child = child;
        HoldNodeIndex = holdNodeIndex;
    }

    public int Child { get; }

    /// <summary>Bool value node. The pose is captured when it rises and held while it stays true.</summary>
    public int HoldNodeIndex { get; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly PoseSnapshotDefinition _def;
        private ValueNodeInstance _hold = null!;
        private bool _captured;

        public Instance(PoseSnapshotDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _hold = context.ValueNode(_def.HoldNodeIndex, ValueInputKind.Number);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            _captured = false;
        }

        protected override void OnUpdate(GraphContext context)
        {
            if (_hold.GetValue(context).AsBool() && _captured)
            {
                // The child is left alone while frozen, so nothing behind it advances either.
                RootMotionDelta = Transform3D.Identity;
                return;
            }

            Child.Update(context);
            CopyResultFrom(Child);
            _captured = true;
        }
    }
}

/// <summary>Turns a pose into an additive one, measured against the reference pose or another child's pose.</summary>
public sealed class MakeAdditiveDefinition : PoseNodeDefinition
{
    public MakeAdditiveDefinition(int child, int referenceChild = -1)
    {
        Child = child;
        ReferenceChild = referenceChild;
    }

    public int Child { get; }

    /// <summary>Pose node to subtract, or -1 to subtract the skeleton's reference pose.</summary>
    public int ReferenceChild { get; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly MakeAdditiveDefinition _def;
        private PoseNodeInstance? _reference;

        public Instance(MakeAdditiveDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            if (_def.ReferenceChild >= 0)
                _reference = context.PoseNode(_def.ReferenceChild);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            _reference?.Initialize(context, initialTime);
        }

        protected override void OnShutdown(GraphContext context)
        {
            base.OnShutdown(context);
            _reference?.Shutdown(context);
        }

        protected override void OnUpdate(GraphContext context)
        {
            Child.Update(context);
            CopyTimingFrom(Child);
            RootMotionDelta = Child.RootMotionDelta;

            _reference?.Update(context);
            if (_reference is null)
                Blender.MakeAdditive(Pose, Child.Pose);
            else
                Blender.MakeAdditive(Pose, Child.Pose, _reference.Pose);
        }
    }
}

/// <summary>One input of a <see cref="WeightedBlendDefinition"/>: a pose and the value node weighting it.</summary>
public readonly struct WeightedPose
{
    public WeightedPose(int pose, FloatInput? weight = null)
    {
        Pose = pose;
        Weight = weight ?? 1f;
    }

    public int Pose { get; }
    public FloatInput Weight { get; }
}

/// <summary>Blends any number of poses by normalized weights. Every input updates each frame.</summary>
public sealed class WeightedBlendDefinition : PoseNodeDefinition
{
    public WeightedBlendDefinition(IReadOnlyList<WeightedPose> inputs) => Inputs = new List<WeightedPose>(inputs).ToArray();

    public WeightedPose[] Inputs { get; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PoseNodeInstance
    {
        private readonly WeightedBlendDefinition _def;
        private PoseNodeInstance[] _inputs = null!;
        private BoundFloat[] _weights = null!;
        private float[] _values = null!;

        public Instance(WeightedBlendDefinition def) => _def = def;

        public override SyncTrack SyncTrack => _inputs.Length > 0 ? _inputs[0].SyncTrack : SyncTrack.Default;

        public override void Bind(GraphBindContext context)
        {
            if (_def.Inputs.Length == 0)
                throw context.Error("a weighted blend needs at least one input.");

            Pose = new Pose(context.Skeleton);
            _inputs = new PoseNodeInstance[_def.Inputs.Length];
            _weights = new BoundFloat[_def.Inputs.Length];
            _values = new float[_def.Inputs.Length];
            for (int i = 0; i < _inputs.Length; i++)
            {
                _inputs[i] = context.PoseNode(_def.Inputs[i].Pose);
                _weights[i] = BoundFloat.Bind(context, _def.Inputs[i].Weight);
            }
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            foreach (PoseNodeInstance input in _inputs)
                input.Initialize(context, initialTime);
            CopyTimingFrom(_inputs[0]);
        }

        protected override void OnShutdown(GraphContext context)
        {
            foreach (PoseNodeInstance input in _inputs)
                input.Shutdown(context);
        }

        protected override void OnUpdate(GraphContext context)
        {
            float total = 0f;
            for (int i = 0; i < _inputs.Length; i++)
            {
                _inputs[i].Update(context);
                float weight = _weights[i].Get(context);
                _values[i] = float.IsFinite(weight) && weight > 0f ? weight : 0f;
                total += _values[i];
            }

            CopyTimingFrom(_inputs[0]);

            // An input fires its events as strongly as it shows, so a clip at weight 0 makes no footsteps.
            for (int i = 0; i < _inputs.Length; i++)
                context.Events.UpdateWeights(_inputs[i].SampledEventRange, total > 0f ? _values[i] / total : i == 0 ? 1f : 0f);

            if (total <= 0f)
            {
                CopyResultFrom(_inputs[0]);
                return;
            }

            Pose.CopyFrom(_inputs[0].Pose);
            RootMotionDelta = _inputs[0].RootMotionDelta;
            float accumulated = _values[0];
            for (int i = 1; i < _inputs.Length; i++)
            {
                if (_values[i] <= 0f)
                    continue;
                accumulated += _values[i];
                float weight = _values[i] / accumulated;
                Blender.Blend(Pose, Pose, _inputs[i].Pose, weight);
                RootMotionDelta = Blender.BlendRootMotionDeltas(RootMotionDelta, _inputs[i].RootMotionDelta, weight);
            }
        }
    }
}

/// <summary>Eases the output toward its child by a half life in seconds, independent of frame rate.</summary>
public sealed class PoseSmoothingDefinition : PoseNodeDefinition
{
    public PoseSmoothingDefinition(int child, FloatInput halfLifeSeconds)
    {
        Child = child;
        HalfLifeSeconds = halfLifeSeconds;
    }

    public int Child { get; }

    public FloatInput HalfLifeSeconds { get; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly PoseSmoothingDefinition _def;
        private BoundFloat _halfLife;
        private bool _started;

        public Instance(PoseSmoothingDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _halfLife = BoundFloat.Bind(context, _def.HalfLifeSeconds);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            _started = false;
        }

        protected override void OnUpdate(GraphContext context)
        {
            Child.Update(context);
            CopyTimingFrom(Child);
            RootMotionDelta = Child.RootMotionDelta;

            float halfLife = _halfLife.Get(context);
            if (!_started || !(halfLife > 0f))
            {
                Pose.CopyFrom(Child.Pose);
                _started = true;
                return;
            }

            float weight = Maths.HalfLifeFactor(context.DeltaTime, halfLife);
            Blender.Blend(Pose, Pose, Child.Pose, Math.Clamp(weight, 0f, 1f));
        }
    }
}
