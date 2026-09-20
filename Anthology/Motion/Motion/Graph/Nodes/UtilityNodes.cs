using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

// ---- Noise -------------------------------------------------------------------------------------

/// <summary>
/// A smooth wandering value, for the small imperfections that stop a character looking mechanical: an
/// idle sway, a weapon drifting off aim, a flickering light. It is smooth in time (no jitter between
/// frames) and repeats exactly for a given seed.
/// </summary>
public sealed class NoiseValueDefinition : ValueNodeDefinition
{
    /// <summary>How many times a second the value wanders to a new place.</summary>
    public float Frequency { get; set; } = 1f;

    /// <summary>The value swings between plus and minus this.</summary>
    public float Amplitude { get; set; } = 1f;

    /// <summary>Layers of finer, weaker noise on top, which makes the motion less regular.</summary>
    public int Octaves { get; set; } = 1;

    /// <summary>Picks which noise this is. Two nodes with different seeds never move together.</summary>
    public uint Seed { get; set; } = 1;

    public override AnimationValueType ValueType => AnimationValueType.Float;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly NoiseValueDefinition _def;
        private float _time;
        private uint _lastUpdateId;

        public Instance(NoiseValueDefinition def) => _def = def;

        protected override void OnInitialize(GraphContext context) => _time = 0f;

        protected override ParameterValue Compute(GraphContext context)
        {
            // The clock only advances once per frame, however many readers there are.
            if (_lastUpdateId != context.UpdateId)
            {
                _lastUpdateId = context.UpdateId;
                _time += context.DeltaTime;
            }

            float value = 0f;
            float amplitude = 1f;
            float frequency = _def.Frequency;
            float total = 0f;
            int octaves = Math.Clamp(_def.Octaves, 1, 8);
            for (int i = 0; i < octaves; i++)
            {
                value += Sample(_time * frequency, _def.Seed + (uint)i * 7919u) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return ParameterValue.FromFloat(total > 0f ? value / total * _def.Amplitude : 0f);
        }

        /// <summary>Value noise: a random value per whole step, eased between.</summary>
        private static float Sample(float t, uint seed)
        {
            float floor = MathF.Floor(t);
            int index = (int)floor;
            float fraction = t - floor;
            float smooth = fraction * fraction * (3f - 2f * fraction);
            return Maths.Lerp(Random(index, seed), Random(index + 1, seed), smooth);
        }

        /// <summary>A repeatable value in -1..1 for a whole step.</summary>
        private static float Random(int step, uint seed)
        {
            uint hash = (uint)step * 2654435761u ^ seed * 2246822519u;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;
            return (hash >> 8) * (2f / 16777216f) - 1f;
        }
    }
}

// ---- Timer -------------------------------------------------------------------------------------

/// <summary>
/// Seconds since the node became part of the playing graph, so a state can drive something by how long
/// it has been running. With a loop length it wraps, and it can report its progress through that loop
/// instead of raw seconds.
/// </summary>
public sealed class TimerValueDefinition : ValueNodeDefinition
{
    /// <summary>Length of one cycle in seconds, or 0 to count up forever.</summary>
    public float LoopSeconds { get; set; }

    /// <summary>With a loop length, report progress through the loop in 0..1 rather than seconds.</summary>
    public bool Normalized { get; set; }

    /// <summary>Bool value node; while true the clock is held at zero. -1 for none.</summary>
    public int ResetNodeIndex { get; set; } = -1;

    public override AnimationValueType ValueType => AnimationValueType.Float;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly TimerValueDefinition _def;
        private ValueNodeInstance? _reset;
        private float _time;
        private uint _lastUpdateId;

        public Instance(TimerValueDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
            => _reset = context.OptionalValueNode(_def.ResetNodeIndex, ValueInputKind.Number);

        protected override void OnInitialize(GraphContext context) => _time = 0f;

        protected override ParameterValue Compute(GraphContext context)
        {
            if (_lastUpdateId != context.UpdateId)
            {
                _lastUpdateId = context.UpdateId;
                if (_reset is not null && _reset.GetValue(context).AsBool())
                    _time = 0f;
                else
                    _time += context.DeltaTime;
            }

            float value = _time;
            if (_def.LoopSeconds > 0f)
            {
                value -= MathF.Floor(value / _def.LoopSeconds) * _def.LoopSeconds;
                if (_def.Normalized)
                    value /= _def.LoopSeconds;
            }

            return ParameterValue.FromFloat(value);
        }
    }
}

// ---- Spring ------------------------------------------------------------------------------------

/// <summary>
/// Follows another value like a weight on a spring: it overshoots a sudden change and settles, rather
/// than snapping to it. Good for aim offsets, camera style lag and anything that should feel like it has
/// mass. Damping at 1 settles without overshooting at all.
/// </summary>
public sealed class FloatSpringDefinition : ValueNodeDefinition
{
    public FloatSpringDefinition(int inputNodeIndex) => InputNodeIndex = inputNodeIndex;

    public int InputNodeIndex { get; }

    /// <summary>How fast it chases the input, in cycles per second.</summary>
    public float Frequency { get; set; } = 4f;

    /// <summary>1 settles with no overshoot, below 1 bounces, above 1 is sluggish.</summary>
    public float Damping { get; set; } = 1f;

    public override AnimationValueType ValueType => AnimationValueType.Float;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatSpringDefinition _def;
        private ValueNodeInstance _input = null!;
        private float _value, _velocity;
        private bool _started;
        private uint _lastUpdateId;

        public Instance(FloatSpringDefinition def) => _def = def;

        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.InputNodeIndex, ValueInputKind.Number);

        protected override void OnInitialize(GraphContext context)
        {
            _started = false;
            _velocity = 0f;
        }

        protected override ParameterValue Compute(GraphContext context)
        {
            float target = _input.GetValue(context).AsFloat();
            if (!float.IsFinite(target))
                target = _value;

            if (!_started)
            {
                _value = target;
                _started = true;
                return ParameterValue.FromFloat(_value);
            }

            if (_lastUpdateId == context.UpdateId)
                return ParameterValue.FromFloat(_value);

            _lastUpdateId = context.UpdateId;
            float dt = context.DeltaTime;
            if (!(dt > 0f))
                return ParameterValue.FromFloat(_value);

            // Semi implicit integration of a damped spring, which stays stable at long frame times.
            float angular = 2f * MathF.PI * MathF.Max(_def.Frequency, 0f);
            float stiffness = angular * angular;
            float damping = 2f * MathF.Max(_def.Damping, 0f) * angular;
            _velocity += (stiffness * (target - _value) - damping * _velocity) * dt;
            _value += _velocity * dt;
            return ParameterValue.FromFloat(_value);
        }
    }
}

// ---- Curve -------------------------------------------------------------------------------------

/// <summary>
/// Shapes one value with an authored curve, so a designer can decide how an input maps to an output
/// (how aim offset grows with speed, how a weight fades in) without touching the graph.
/// </summary>
public sealed class CurveValueDefinition : ValueNodeDefinition
{
    public CurveValueDefinition(int inputNodeIndex, AnimationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        InputNodeIndex = inputNodeIndex;
        Curve = curve;
    }

    public int InputNodeIndex { get; }

    public AnimationCurve Curve { get; }

    public override AnimationValueType ValueType => AnimationValueType.Float;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly CurveValueDefinition _def;
        private ValueNodeInstance _input = null!;

        public Instance(CurveValueDefinition def) => _def = def;

        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.InputNodeIndex, ValueInputKind.Number);

        protected override ParameterValue Compute(GraphContext context)
        {
            float input = _input.GetValue(context).AsFloat();
            if (!float.IsFinite(input))
                return ParameterValue.FromFloat(0f);

            float value = _def.Curve.Evaluate(input);
            return ParameterValue.FromFloat(float.IsFinite(value) ? value : 0f);
        }
    }
}

// ---- Debug -------------------------------------------------------------------------------------

/// <summary>
/// Passes a pose through untouched, with two things for finding trouble: it can hand each frame's pose
/// to an inspector (to draw it, log it or record it), and it can catch a bone that has gone to NaN and
/// put the reference pose back, so one bad solve does not spread through the rest of the graph.
/// </summary>
public sealed class DebugPoseDefinition : PoseNodeDefinition
{
    public DebugPoseDefinition(int child) => Child = child;

    public int Child { get; }

    /// <summary>Called once per update with the pose passing through. Keep it cheap.</summary>
    public Action<Pose>? Inspect { get; set; }

    /// <summary>Replace a bone that has gone to NaN or infinity with its reference transform.</summary>
    public bool RepairInvalidBones { get; set; } = true;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly DebugPoseDefinition _def;

        public Instance(DebugPoseDefinition def) => _def = def;

        /// <summary>How many bones have been repaired since the node started.</summary>
        public int RepairedBones { get; private set; }

        public override void Bind(GraphBindContext context) => BindChild(context, _def.Child);

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            RepairedBones = 0;
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);

            if (_def.RepairInvalidBones)
            {
                for (int b = 0; b < Pose.BoneCount; b++)
                {
                    if (IsFinite(Pose.GetTransform(b)))
                        continue;
                    Pose.SetTransform(b, Pose.Skeleton.GetBoneParentSpaceTransform(b));
                    RepairedBones++;
                }
            }

            _def.Inspect?.Invoke(Pose);
        }

        private static bool IsFinite(in Transform3D transform)
            => float.IsFinite(transform.position.X) && float.IsFinite(transform.position.Y) && float.IsFinite(transform.position.Z)
            && float.IsFinite(transform.rotation.X) && float.IsFinite(transform.rotation.Y) && float.IsFinite(transform.rotation.Z) && float.IsFinite(transform.rotation.W)
            && float.IsFinite(transform.scale.X) && float.IsFinite(transform.scale.Y) && float.IsFinite(transform.scale.Z);
    }
}
