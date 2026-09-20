using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>How a channel entry combines with the value already on the pose.</summary>
public enum ChannelBlendMode : byte
{
    /// <summary>Replace the value.</summary>
    Override,
    /// <summary>Add to the value.</summary>
    Add,
    /// <summary>Keep whichever is larger, so two drivers never cancel out.</summary>
    Max,
}

// ---- Reading a channel -------------------------------------------------------------------------

/// <summary>
/// Reads one float channel off a pose node as a graph value, so a weight or a speed authored into a
/// clip can drive the rest of the graph. It only watches that node, it does not play it.
/// </summary>
public sealed class PoseChannelDefinition : ValueNodeDefinition
{
    public PoseChannelDefinition(int poseNodeIndex, StringID channel)
    {
        PoseNodeIndex = poseNodeIndex;
        Channel = channel;
    }

    public int PoseNodeIndex { get; }

    public StringID Channel { get; }

    /// <summary>The value to report when the skeleton has no such channel.</summary>
    public float DefaultValue { get; set; }

    public override AnimationValueType ValueType => AnimationValueType.Float;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly PoseChannelDefinition _def;
        private PoseNodeInstance _source = null!;
        private int _channel;

        public Instance(PoseChannelDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            _source = context.ObservePoseNode(_def.PoseNodeIndex);
            _channel = context.Skeleton.GetFloatChannelIndex(_def.Channel);
        }

        protected override ParameterValue Compute(GraphContext context)
            => ParameterValue.FromFloat(_channel == Skeleton.InvalidIndex ? _def.DefaultValue : _source.Pose.GetFloat(_channel));
    }
}

// ---- Writing channels --------------------------------------------------------------------------

/// <summary>One channel driven by a <see cref="FloatChannelLayerDefinition"/>.</summary>
public readonly struct ChannelDriver
{
    public ChannelDriver(StringID channel, int valueNodeIndex, ChannelBlendMode mode = ChannelBlendMode.Override)
    {
        Channel = channel;
        ValueNodeIndex = valueNodeIndex;
        Mode = mode;
    }

    public StringID Channel { get; }

    /// <summary>Float value node giving the channel's value.</summary>
    public int ValueNodeIndex { get; }

    public ChannelBlendMode Mode { get; }
}

/// <summary>
/// Writes float channels onto its child's pose from graph values, leaving the bones alone. This is how
/// an expression, a viseme or any other scalar gets layered over body animation without a clip for it.
/// Channels the skeleton does not have are ignored.
/// </summary>
public sealed class FloatChannelLayerDefinition : PoseNodeDefinition
{
    public FloatChannelLayerDefinition(int child, IReadOnlyList<ChannelDriver> drivers)
    {
        Child = child;
        Drivers = new List<ChannelDriver>(drivers).ToArray();
    }

    public int Child { get; }

    public ChannelDriver[] Drivers { get; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly FloatChannelLayerDefinition _def;
        private ValueNodeInstance[] _values = null!;
        private int[] _channels = null!;

        public Instance(FloatChannelLayerDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _values = new ValueNodeInstance[_def.Drivers.Length];
            _channels = new int[_def.Drivers.Length];
            for (int i = 0; i < _values.Length; i++)
            {
                _values[i] = context.ValueNode(_def.Drivers[i].ValueNodeIndex, ValueInputKind.Number);
                _channels[i] = context.Skeleton.GetFloatChannelIndex(_def.Drivers[i].Channel);
            }
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);

            for (int i = 0; i < _values.Length; i++)
            {
                int channel = _channels[i];
                if (channel == Skeleton.InvalidIndex)
                    continue;

                float value = _values[i].GetValue(context).AsFloat();
                if (!float.IsFinite(value))
                    continue;

                Pose.SetFloat(channel, _def.Drivers[i].Mode switch
                {
                    ChannelBlendMode.Add => Pose.GetFloat(channel) + value,
                    ChannelBlendMode.Max => MathF.Max(Pose.GetFloat(channel), value),
                    _ => value,
                });
            }
        }
    }
}

// ---- Channels driven by a joint ----------------------------------------------------------------

/// <summary>
/// Drives a float channel from how far a bone has turned away from its reference pose, which is how a
/// corrective shape (a bicep bulge, a shoulder crease) follows the joint that causes it. The angle is
/// measured about a chosen axis in the bone's own space, or as the total turn when no axis is given.
/// </summary>
public sealed class DrivenChannelDefinition : PoseNodeDefinition
{
    public DrivenChannelDefinition(int child, StringID bone, StringID channel, float fromDegrees, float toDegrees)
    {
        Child = child;
        Bone = bone;
        Channel = channel;
        FromDegrees = fromDegrees;
        ToDegrees = toDegrees;
    }

    public int Child { get; }

    public StringID Bone { get; }

    public StringID Channel { get; }

    /// <summary>The angle at which the channel reads <see cref="FromValue"/>.</summary>
    public float FromDegrees { get; }

    /// <summary>The angle at which the channel reads <see cref="ToValue"/>.</summary>
    public float ToDegrees { get; }

    public float FromValue { get; set; }

    public float ToValue { get; set; } = 1f;

    /// <summary>
    /// Axis to measure the turn about, in the bone's parent space. Left at zero the total turn is used,
    /// which is always positive.
    /// </summary>
    public Float3 Axis { get; set; }

    /// <summary>How the value joins whatever is already on the channel.</summary>
    public ChannelBlendMode Mode { get; set; } = ChannelBlendMode.Override;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly DrivenChannelDefinition _def;
        private int _bone;
        private int _channel;
        private Quaternion _reference;
        private Float3 _axis;
        private bool _aboutAxis;

        public Instance(DrivenChannelDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _bone = context.Skeleton.GetBoneIndex(_def.Bone);
            _channel = context.Skeleton.GetFloatChannelIndex(_def.Channel);
            _reference = _bone == Skeleton.InvalidIndex ? Quaternion.Identity : context.Skeleton.GetBoneParentSpaceTransform(_bone).rotation;

            float length = Float3.Length(_def.Axis);
            _aboutAxis = length > 1e-6f;
            _axis = _aboutAxis ? _def.Axis / length : Float3.Zero;
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);
            if (_bone == Skeleton.InvalidIndex || _channel == Skeleton.InvalidIndex)
                return;

            Quaternion turn = Quaternion.Normalize(Quaternion.Inverse(_reference) * Pose.GetTransform(_bone).rotation);
            if (turn.W < 0f)
                turn = new Quaternion(-turn.X, -turn.Y, -turn.Z, -turn.W);

            var vector = new Float3(turn.X, turn.Y, turn.Z);
            float radians = _aboutAxis
                ? 2f * MathF.Atan2(Float3.Dot(vector, _axis), turn.W)
                : 2f * MathF.Atan2(Float3.Length(vector), turn.W);

            float degrees = radians * (180f / MathF.PI);
            float span = _def.ToDegrees - _def.FromDegrees;
            float t = MathF.Abs(span) < 1e-6f ? 0f : Math.Clamp((degrees - _def.FromDegrees) / span, 0f, 1f);
            float value = _def.FromValue + (_def.ToValue - _def.FromValue) * t;

            Pose.SetFloat(_channel, _def.Mode switch
            {
                ChannelBlendMode.Add => Pose.GetFloat(_channel) + value,
                ChannelBlendMode.Max => MathF.Max(Pose.GetFloat(_channel), value),
                _ => value,
            });
        }
    }
}
