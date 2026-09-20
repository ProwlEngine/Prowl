using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

public enum FloatMathOp : byte { Add, Subtract, Multiply, Divide, Min, Max }
public enum CompareOp : byte { Greater, GreaterOrEqual, Less, LessOrEqual, Equal, NotEqual }
public enum BoolOp : byte { And, Or, Not }
public enum VectorComponent : byte { X, Y, Z, Length }

/// <summary>Angle-domain operations on a degrees input.</summary>
public enum AngleOp : byte { ClampTo180, ClampTo360, FlipHemisphere, FlipHemisphereNegate }

/// <summary>Easing curves used by eased value nodes; None disables easing.</summary>
public enum EasingOp : byte { None, Linear, EaseIn, EaseOut, EaseInOut }

/// <summary>Evaluates the eased value-node easing curves.</summary>
internal static class Easing
{
    public static float Evaluate(EasingOp op, float t) => op switch
    {
        EasingOp.EaseIn => t * t,
        EasingOp.EaseOut => 1f - (1f - t) * (1f - t),
        EasingOp.EaseInOut => t * t * (3f - 2f * t),
        _ => t, // None / Linear
    };

    public static float WrapDegrees180(float deg)
    {
        float d = deg % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return d;
    }

    public static float WrapDegrees360(float deg)
    {
        float d = deg % 360f;
        if (d < 0f) d += 360f;
        return d;
    }
}

/// <summary>
/// Eases a value toward a target over a fixed time. Every change of the target becomes its own eased
/// step that plays over the ease time, and the output is the sum of those steps. A single jump eases
/// exactly along the curve, and a continuously moving target gives the same result at any frame rate.
/// </summary>
internal struct EasedFloat
{
    private float _settled;
    private float _target;
    private float[]? _amounts;
    private float[]? _ages;
    private int _count;

    public float Value { get; private set; }

    public void Reset(float value)
    {
        _settled = _target = Value = value;
        _count = 0;
    }

    public float Advance(float target, float deltaTime, float easeTime, EasingOp easing)
    {
        if (!float.IsFinite(target))
            return Value;
        if (!(easeTime > 0f))
        {
            Reset(target);
            return Value;
        }

        float step = float.IsFinite(deltaTime) ? MathF.Max(deltaTime, 0f) : 0f;
        int kept = 0;
        for (int i = 0; i < _count; i++)
        {
            float age = _ages![i] + step;
            if (age >= easeTime)
            {
                _settled += _amounts![i];
                continue;
            }
            _ages[kept] = age;
            _amounts![kept] = _amounts[i];
            kept++;
        }
        _count = kept;

        if (target != _target)
        {
            Add(target - _target, step);
            _target = target;
        }

        float value = _settled;
        for (int i = 0; i < _count; i++)
            value += _amounts![i] * Easing.Evaluate(easing, Math.Clamp(_ages![i] / easeTime, 0f, 1f));
        if (_count == 0)
            value = _settled = _target;
        Value = value;
        return value;
    }

    private void Add(float amount, float age)
    {
        if (_amounts is null || _count == _amounts.Length)
        {
            int size = _amounts is null ? 8 : _amounts.Length * 2;
            Array.Resize(ref _amounts, size);
            Array.Resize(ref _ages, size);
        }
        _amounts[_count] = amount;
        _ages![_count] = age;
        _count++;
    }
}

/// <summary>What a <see cref="StateQueryDefinition"/> reports about a state machine's active state.</summary>
public enum StateQuery : byte
{
    /// <summary>Seconds elapsed since the current state was entered.</summary>
    TimeInState,
    /// <summary>Normalized progress [0,1] of the current state's content.</summary>
    NormalizedTime,
}

// ---- Constant -------------------------------------------------------------------------------

/// <summary>A constant value.</summary>
public sealed class ConstValueDefinition : ValueNodeDefinition
{
    public ConstValueDefinition(ParameterValue value) => Value = value;
    public ParameterValue Value { get; }
    public override AnimationValueType ValueType => Value.Type;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly ConstValueDefinition _def;
        public Instance(ConstValueDefinition def) => _def = def;
        protected override ParameterValue Compute(GraphContext context) => _def.Value;
    }
}

// ---- Float math -----------------------------------------------------------------------------

/// <summary>Combines two float values with an arithmetic operation.</summary>
public sealed class FloatMathDefinition : ValueNodeDefinition
{
    public FloatMathDefinition(int a, int b, FloatMathOp op) { A = a; B = b; Op = op; }
    public int A { get; }
    public int B { get; }
    public FloatMathOp Op { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatMathDefinition _def;
        private ValueNodeInstance _a = null!, _b = null!;
        public Instance(FloatMathDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) { _a = context.ValueNode(_def.A, ValueInputKind.Number); _b = context.ValueNode(_def.B, ValueInputKind.Number); }
        protected override ParameterValue Compute(GraphContext context)
        {
            float a = _a.GetValue(context).AsFloat();
            float b = _b.GetValue(context).AsFloat();
            float r = _def.Op switch
            {
                FloatMathOp.Add => a + b,
                FloatMathOp.Subtract => a - b,
                FloatMathOp.Multiply => a * b,
                FloatMathOp.Divide => MathF.Abs(b) < 1e-9f ? 0f : a / b,
                FloatMathOp.Min => MathF.Min(a, b),
                FloatMathOp.Max => MathF.Max(a, b),
                _ => a
            };
            return ParameterValue.FromFloat(r);
        }
    }
}

// ---- Float comparison (-> bool) -------------------------------------------------------------

/// <summary>Compares two float values, producing a bool.</summary>
public sealed class FloatCompareDefinition : ValueNodeDefinition
{
    public FloatCompareDefinition(int a, int b, CompareOp op, float epsilon = 1e-4f) { A = a; B = b; Op = op; Epsilon = epsilon; }
    public int A { get; }
    public int B { get; }
    public CompareOp Op { get; }
    public float Epsilon { get; }
    public override AnimationValueType ValueType => AnimationValueType.Bool;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatCompareDefinition _def;
        private ValueNodeInstance _a = null!, _b = null!;
        public Instance(FloatCompareDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) { _a = context.ValueNode(_def.A, ValueInputKind.Number); _b = context.ValueNode(_def.B, ValueInputKind.Number); }
        protected override ParameterValue Compute(GraphContext context)
        {
            float a = _a.GetValue(context).AsFloat();
            float b = _b.GetValue(context).AsFloat();
            bool r = _def.Op switch
            {
                CompareOp.Greater => a > b,
                CompareOp.GreaterOrEqual => a >= b,
                CompareOp.Less => a < b,
                CompareOp.LessOrEqual => a <= b,
                CompareOp.Equal => MathF.Abs(a - b) <= _def.Epsilon,
                CompareOp.NotEqual => MathF.Abs(a - b) > _def.Epsilon,
                _ => false
            };
            return ParameterValue.FromBool(r);
        }
    }
}

// ---- Float remap / clamp --------------------------------------------------------------------

/// <summary>Remaps a float from one range to another (extrapolating outside the input range).</summary>
public sealed class FloatRemapDefinition : ValueNodeDefinition
{
    public FloatRemapDefinition(int input, float inMin, float inMax, float outMin, float outMax)
    { Input = input; InMin = inMin; InMax = inMax; OutMin = outMin; OutMax = outMax; }
    public int Input { get; }
    public float InMin { get; }
    public float InMax { get; }
    public float OutMin { get; }
    public float OutMax { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatRemapDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(FloatRemapDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Number);
        protected override ParameterValue Compute(GraphContext context)
        {
            float v = _input.GetValue(context).AsFloat();
            float span = _def.InMax - _def.InMin;
            float t = MathF.Abs(span) < 1e-9f ? 0f : (v - _def.InMin) / span;
            return ParameterValue.FromFloat(_def.OutMin + (_def.OutMax - _def.OutMin) * t);
        }
    }
}

/// <summary>Clamps a float to a range.</summary>
public sealed class FloatClampDefinition : ValueNodeDefinition
{
    public FloatClampDefinition(int input, float min, float max)
    {
        if (!(min <= max))
            throw new ArgumentException($"FloatClamp needs min <= max, got [{min}, {max}].");
        Input = input;
        Min = min;
        Max = max;
    }
    public int Input { get; }
    public float Min { get; }
    public float Max { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatClampDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(FloatClampDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Number);
        protected override ParameterValue Compute(GraphContext context)
            => ParameterValue.FromFloat(Math.Clamp(_input.GetValue(context).AsFloat(), _def.Min, _def.Max));
    }
}

/// <summary>Absolute value of a float.</summary>
public sealed class FloatAbsDefinition : ValueNodeDefinition
{
    public FloatAbsDefinition(int input) => Input = input;
    public int Input { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatAbsDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(FloatAbsDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Number);
        protected override ParameterValue Compute(GraphContext context)
            => ParameterValue.FromFloat(MathF.Abs(_input.GetValue(context).AsFloat()));
    }
}

/// <summary>Picks one of two float inputs based on a bool selector.</summary>
public sealed class FloatSwitchDefinition : ValueNodeDefinition
{
    public FloatSwitchDefinition(int selector, int trueValue, int falseValue) { Selector = selector; TrueValue = trueValue; FalseValue = falseValue; }
    public int Selector { get; }
    public int TrueValue { get; }
    public int FalseValue { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatSwitchDefinition _def;
        private ValueNodeInstance _selector = null!, _true = null!, _false = null!;
        public Instance(FloatSwitchDefinition def) => _def = def;
        public override void Bind(GraphBindContext context)
        { _selector = context.ValueNode(_def.Selector, ValueInputKind.Number); _true = context.ValueNode(_def.TrueValue, ValueInputKind.Number); _false = context.ValueNode(_def.FalseValue, ValueInputKind.Number); }
        protected override ParameterValue Compute(GraphContext context)
            => ParameterValue.FromFloat((_selector.GetValue(context).AsBool() ? _true : _false).GetValue(context).AsFloat());
    }
}

/// <summary>True when a float input lies within a range (inclusive or exclusive bounds).</summary>
public sealed class FloatRangeComparisonDefinition : ValueNodeDefinition
{
    public FloatRangeComparisonDefinition(int input, float min, float max, bool inclusive = true)
    { Input = input; Min = min; Max = max; Inclusive = inclusive; }
    public int Input { get; }
    public float Min { get; }
    public float Max { get; }
    public bool Inclusive { get; }
    public override AnimationValueType ValueType => AnimationValueType.Bool;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatRangeComparisonDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(FloatRangeComparisonDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Number);
        protected override ParameterValue Compute(GraphContext context)
        {
            float v = _input.GetValue(context).AsFloat();
            bool r = _def.Inclusive ? v >= _def.Min && v <= _def.Max : v > _def.Min && v < _def.Max;
            return ParameterValue.FromBool(r);
        }
    }
}

/// <summary>Angle-domain operations on a degrees input.</summary>
public sealed class FloatAngleMathDefinition : ValueNodeDefinition
{
    public FloatAngleMathDefinition(int input, AngleOp op) { Input = input; Op = op; }
    public int Input { get; }
    public AngleOp Op { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatAngleMathDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(FloatAngleMathDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Number);
        protected override ParameterValue Compute(GraphContext context)
        {
            float deg = _input.GetValue(context).AsFloat();
            float r = _def.Op switch
            {
                AngleOp.ClampTo180 => Easing.WrapDegrees180(deg),
                AngleOp.ClampTo360 => Easing.WrapDegrees360(deg),
                AngleOp.FlipHemisphere => Easing.WrapDegrees180(deg - 180f),
                AngleOp.FlipHemisphereNegate => -Easing.WrapDegrees180(deg - 180f),
                _ => deg
            };
            return ParameterValue.FromFloat(r);
        }
    }
}

/// <summary>Time-based eased follow of a float input toward its target using an easing curve.</summary>
public sealed class FloatEaseDefinition : ValueNodeDefinition
{
    public FloatEaseDefinition(int input, float easeTime, EasingOp easing, bool useStartValue = false, float startValue = 0f)
    { Input = input; EaseTime = easeTime; Easing = easing; UseStartValue = useStartValue; StartValue = startValue; }
    public int Input { get; }
    public float EaseTime { get; }
    public EasingOp Easing { get; }
    public bool UseStartValue { get; }
    public float StartValue { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatEaseDefinition _def;
        private ValueNodeInstance _input = null!;
        private EasedFloat _value;
        private double _lastTime;
        private bool _started;
        public Instance(FloatEaseDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Number);

        protected override void OnInitialize(GraphContext context) => _started = false;

        protected override ParameterValue Compute(GraphContext context)
        {
            float target = _input.GetValue(context).AsFloat();
            if (!_started)
            {
                _value.Reset(_def.UseStartValue ? _def.StartValue : target);
                _lastTime = context.Time;
                _started = true;
            }

            float elapsed = (float)(context.Time - _lastTime);
            _lastTime = context.Time;
            return ParameterValue.FromFloat(_value.Advance(target, elapsed, _def.EaseTime, _def.Easing));
        }
    }
}

/// <summary>Outputs the float for the first true condition (priority order), optionally eased.</summary>
public sealed class FloatSelectorDefinition : ValueNodeDefinition
{
    public FloatSelectorDefinition(IReadOnlyList<int> conditionNodes, IReadOnlyList<float> values, float defaultValue, float easeTime = 0.2f, EasingOp easing = EasingOp.None)
    {
        if (conditionNodes.Count != values.Count)
            throw new ArgumentException("FloatSelector needs one value per condition.");
        ConditionNodes = new List<int>(conditionNodes).ToArray();
        Values = new List<float>(values).ToArray();
        DefaultValue = defaultValue;
        EaseTime = easeTime;
        Easing = easing;
    }

    public int[] ConditionNodes { get; }
    public float[] Values { get; }
    public float DefaultValue { get; }
    public float EaseTime { get; }
    public EasingOp Easing { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FloatSelectorDefinition _def;
        private ValueNodeInstance[] _conditions = null!;
        private EasedFloat _value;
        private double _lastTime;
        private bool _started;
        public Instance(FloatSelectorDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            _conditions = new ValueNodeInstance[_def.ConditionNodes.Length];
            for (int i = 0; i < _conditions.Length; i++)
                _conditions[i] = context.ValueNode(_def.ConditionNodes[i], ValueInputKind.Number);
        }

        protected override void OnInitialize(GraphContext context) => _started = false;

        protected override ParameterValue Compute(GraphContext context)
        {
            float target = _def.DefaultValue;
            for (int i = 0; i < _conditions.Length; i++)
            {
                if (_conditions[i].GetValue(context).AsBool())
                {
                    target = _def.Values[i];
                    break;
                }
            }

            if (_def.Easing == EasingOp.None)
                return ParameterValue.FromFloat(target);

            if (!_started)
            {
                _value.Reset(target);
                _lastTime = context.Time;
                _started = true;
            }

            float elapsed = (float)(context.Time - _lastTime);
            _lastTime = context.Time;
            return ParameterValue.FromFloat(_value.Advance(target, elapsed, _def.EaseTime, _def.Easing));
        }
    }
}

// ---- Bool logic -----------------------------------------------------------------------------

/// <summary>And/Or of two bools, or Not of one (B unused for Not).</summary>
public sealed class BoolLogicDefinition : ValueNodeDefinition
{
    public BoolLogicDefinition(int a, int b, BoolOp op) { A = a; B = b; Op = op; }
    public int A { get; }
    public int B { get; }
    public BoolOp Op { get; }
    public override AnimationValueType ValueType => AnimationValueType.Bool;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly BoolLogicDefinition _def;
        private ValueNodeInstance _a = null!;
        private ValueNodeInstance? _b;
        public Instance(BoolLogicDefinition def) => _def = def;
        public override void Bind(GraphBindContext context)
        {
            _a = context.ValueNode(_def.A, ValueInputKind.Number);
            _b = _def.Op == BoolOp.Not ? null : context.ValueNode(_def.B, ValueInputKind.Number);
        }
        protected override ParameterValue Compute(GraphContext context)
        {
            bool a = _a.GetValue(context).AsBool();
            bool r = _def.Op switch
            {
                BoolOp.Not => !a,
                BoolOp.And => a && _b!.GetValue(context).AsBool(),
                BoolOp.Or => a || _b!.GetValue(context).AsBool(),
                _ => a
            };
            return ParameterValue.FromBool(r);
        }
    }
}

/// <summary>Negates a vector value.</summary>
public sealed class VectorNegateDefinition : ValueNodeDefinition
{
    public VectorNegateDefinition(int input) => Input = input;
    public int Input { get; }
    public override AnimationValueType ValueType => AnimationValueType.Vector;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly VectorNegateDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(VectorNegateDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Vector);
        protected override ParameterValue Compute(GraphContext context)
        {
            Float3 v = _input.GetValue(context).Vector;
            return ParameterValue.FromVector(new Float3(-v.X, -v.Y, -v.Z));
        }
    }
}

// ---- ID value nodes -------------------------------------------------------------------------

public enum IdComparison : byte { Matches, DoesntMatch }

/// <summary>A constant id value.</summary>
public sealed class ConstIdDefinition : ValueNodeDefinition
{
    public ConstIdDefinition(StringID value) => Value = value;
    public StringID Value { get; }
    public override AnimationValueType ValueType => AnimationValueType.Id;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly ConstIdDefinition _def;
        public Instance(ConstIdDefinition def) => _def = def;
        protected override ParameterValue Compute(GraphContext context) => ParameterValue.FromId(_def.Value);
    }
}

/// <summary>True when an input id matches (or does not match) a set of ids.</summary>
public sealed class IdComparisonDefinition : ValueNodeDefinition
{
    public IdComparisonDefinition(int input, IdComparison comparison, IReadOnlyList<StringID> ids)
    { Input = input; Comparison = comparison; Ids = new List<StringID>(ids).ToArray(); }
    public int Input { get; }
    public IdComparison Comparison { get; }
    public StringID[] Ids { get; }
    public override AnimationValueType ValueType => AnimationValueType.Bool;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly IdComparisonDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(IdComparisonDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Id);
        protected override ParameterValue Compute(GraphContext context)
        {
            StringID id = _input.GetValue(context).AsId();
            bool contains = Contains(id);
            bool result = _def.Comparison switch
            {
                IdComparison.Matches => _def.Ids.Length == 0 ? !id.IsValid : contains,
                IdComparison.DoesntMatch => !contains,
                _ => false
            };
            return ParameterValue.FromBool(result);
        }

        private bool Contains(StringID id)
        {
            foreach (StringID candidate in _def.Ids)
                if (candidate == id)
                    return true;
            return false;
        }
    }
}

/// <summary>Maps an input id to a float via parallel id/value arrays, with a default.</summary>
public sealed class IdToFloatDefinition : ValueNodeDefinition
{
    public IdToFloatDefinition(int input, IReadOnlyList<StringID> ids, IReadOnlyList<float> values, float defaultValue)
    {
        if (ids.Count != values.Count)
            throw new ArgumentException("IdToFloat needs one value per id.");
        Input = input; Ids = new List<StringID>(ids).ToArray(); Values = new List<float>(values).ToArray(); DefaultValue = defaultValue;
    }
    public int Input { get; }
    public StringID[] Ids { get; }
    public float[] Values { get; }
    public float DefaultValue { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly IdToFloatDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(IdToFloatDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Id);
        protected override ParameterValue Compute(GraphContext context)
        {
            StringID id = _input.GetValue(context).AsId();
            for (int i = 0; i < _def.Ids.Length; i++)
                if (_def.Ids[i] == id)
                    return ParameterValue.FromFloat(_def.Values[i]);
            return ParameterValue.FromFloat(_def.DefaultValue);
        }
    }
}

// ---- Cached value (sample and hold) ---------------------------------------------------------

/// <summary>When a <see cref="CachedValueDefinition"/> takes its sample.</summary>
public enum CachedValueMode : byte
{
    /// <summary>Sample once when the owning branch starts (each time it is entered).</summary>
    OnEntry,
    /// <summary>Follow the input while the owning branch is active and freeze it once the branch becomes inactive.</summary>
    OnExit,
}

/// <summary>
/// Caches its input value. In <see cref="CachedValueMode.OnEntry"/> mode it latches when first read
/// after its branch starts, with a bool sample driver it also latches again on each rising edge (sample
/// and hold). In <see cref="CachedValueMode.OnExit"/> mode it follows the input until its branch
/// becomes the losing side of a transition.
/// </summary>
public sealed class CachedValueDefinition : ValueNodeDefinition
{
    public CachedValueDefinition(int input, AnimationValueType valueType, int sampleWhenNodeIndex = -1, CachedValueMode mode = CachedValueMode.OnEntry)
    { Input = input; ValueTypeField = valueType; SampleWhenNodeIndex = sampleWhenNodeIndex; Mode = mode; }
    public int Input { get; }
    public int SampleWhenNodeIndex { get; }
    public CachedValueMode Mode { get; }
    private AnimationValueType ValueTypeField { get; }
    public override AnimationValueType ValueType => ValueTypeField;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly CachedValueDefinition _def;
        private ValueNodeInstance _input = null!;
        private ValueNodeInstance? _sampleWhen;
        private ParameterValue _cached;
        private bool _hasValue;
        private bool _frozen;
        private bool _driverWasSet;
        public Instance(CachedValueDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            _input = context.ValueNode(_def.Input);
            _sampleWhen = context.OptionalValueNode(_def.SampleWhenNodeIndex, ValueInputKind.Number);
        }

        protected override void OnInitialize(GraphContext context)
        {
            _hasValue = false;
            _frozen = false;
        }

        protected override ParameterValue Compute(GraphContext context)
        {
            if (_def.Mode == CachedValueMode.OnExit)
            {
                if (!_frozen)
                {
                    if (context.IsActiveBranch || !_hasValue)
                        _cached = _input.GetValue(context);
                    _hasValue = true;
                    _frozen = !context.IsActiveBranch;
                }
                return _cached;
            }

            bool driver = _sampleWhen is not null && _sampleWhen.GetValue(context).AsBool();
            if (!_hasValue)
            {
                _cached = _input.GetValue(context);
                _hasValue = true;
            }
            else if (driver && !_driverWasSet)
            {
                _cached = _input.GetValue(context);
            }
            _driverWasSet = driver;
            return _cached;
        }
    }
}

// ---- Target value nodes ---------------------------------------------------------------------

/// <summary>The scalar a <see cref="TargetInfoDefinition"/> extracts from a target.</summary>
public enum TargetInfo : byte
{
    AngleHorizontal,
    AngleVertical,
    Distance,
    DistanceHorizontalOnly,
    DistanceVerticalOnly,
    DeltaOrientationX,
    DeltaOrientationY,
    DeltaOrientationZ,
}

/// <summary>True when the input target is set.</summary>
public sealed class IsTargetSetDefinition : ValueNodeDefinition
{
    public IsTargetSetDefinition(int input) => Input = input;
    public int Input { get; }
    public override AnimationValueType ValueType => AnimationValueType.Bool;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly IsTargetSetDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(IsTargetSetDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Target);
        protected override ParameterValue Compute(GraphContext context)
            => ParameterValue.FromBool(_input.GetValue(context).Target.IsSet);
    }
}

/// <summary>Resolves targets into character (model) space for value nodes.</summary>
internal static class TargetSpace
{
    /// <summary>
    /// Bone targets resolve against the previous frame's pose, world targets are converted with the
    /// inverse of the character's world transform.
    /// </summary>
    public static bool TryResolve(Target target, GraphContext context, out Transform3D result)
    {
        result = Transform3D.Identity;
        if (!target.IsSet || context.PreviousPose is null || !target.TryGetTransform(context.PreviousPose, out Transform3D resolved))
            return false;
        result = target.IsBoneTarget ? resolved : context.WorldToCharacter(resolved);
        return true;
    }
}

/// <summary>
/// Extracts a scalar (angle, distance, or delta orientation euler) from a target, measured in
/// character space. Bone targets resolve against the previous frame's pose.
/// </summary>
public sealed class TargetInfoDefinition : ValueNodeDefinition
{
    private static readonly Float3 Forward = new(0f, 0f, 1f);
    private static readonly Float3 Right = new(1f, 0f, 0f);

    public TargetInfoDefinition(int input, TargetInfo info) { Input = input; Info = info; }
    public int Input { get; }
    public TargetInfo Info { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly TargetInfoDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(TargetInfoDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Target);

        protected override ParameterValue Compute(GraphContext context)
        {
            if (!TargetSpace.TryResolve(_input.GetValue(context).Target, context, out Transform3D xform))
                return ParameterValue.FromFloat(0f);

            Float3 p = xform.position;
            float r = _def.Info switch
            {
                TargetInfo.Distance => Float3.Length(p),
                TargetInfo.DistanceHorizontalOnly => MathF.Sqrt(p.X * p.X + p.Z * p.Z),
                TargetInfo.DistanceVerticalOnly => MathF.Abs(p.Y),
                TargetInfo.AngleHorizontal => HorizontalAngle(p),
                TargetInfo.AngleVertical => VerticalAngle(p),
                TargetInfo.DeltaOrientationX => xform.rotation.EulerAngles.X,
                TargetInfo.DeltaOrientationY => xform.rotation.EulerAngles.Y,
                TargetInfo.DeltaOrientationZ => xform.rotation.EulerAngles.Z,
                _ => 0f
            };
            return ParameterValue.FromFloat(r);
        }

        private static float HorizontalAngle(Float3 p)
        {
            var horiz = new Float3(p.X, 0f, p.Z);
            float len = Float3.Length(horiz);
            if (len < 1e-5f)
                return 0f;
            var dir = new Float3(horiz.X / len, 0f, horiz.Z / len);
            float angle = Maths.Rad2Deg * MathF.Acos(Math.Clamp(Forward.X * dir.X + Forward.Y * dir.Y + Forward.Z * dir.Z, -1f, 1f));
            if (Right.X * dir.X + Right.Y * dir.Y + Right.Z * dir.Z < 0f)
                angle = -angle;
            return angle;
        }

        private static float VerticalAngle(Float3 p)
        {
            float len = Float3.Length(p);
            return len < 1e-5f ? 0f : Maths.Rad2Deg * MathF.Asin(Math.Clamp(p.Y / len, -1f, 1f));
        }
    }
}

/// <summary>Returns the translation (point) of a target as a vector.</summary>
public sealed class TargetPointDefinition : ValueNodeDefinition
{
    public TargetPointDefinition(int input) => Input = input;
    public int Input { get; }
    public override AnimationValueType ValueType => AnimationValueType.Vector;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly TargetPointDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(TargetPointDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Target);
        protected override ParameterValue Compute(GraphContext context)
        {
            if (TargetSpace.TryResolve(_input.GetValue(context).Target, context, out Transform3D xform))
                return ParameterValue.FromVector(xform.position);
            return ParameterValue.FromVector(default);
        }
    }
}

/// <summary>Folds a fixed rotation/translation offset into a bone target.</summary>
public sealed class TargetOffsetDefinition : ValueNodeDefinition
{
    public TargetOffsetDefinition(int input, Quaternion rotationOffset, Float3 translationOffset)
    { Input = input; RotationOffset = rotationOffset; TranslationOffset = translationOffset; }
    public int Input { get; }
    public Quaternion RotationOffset { get; }
    public Float3 TranslationOffset { get; }
    public override AnimationValueType ValueType => AnimationValueType.Target;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly TargetOffsetDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(TargetOffsetDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Target);
        protected override ParameterValue Compute(GraphContext context)
        {
            Target target = _input.GetValue(context).Target;
            return ParameterValue.FromTarget(target.WithBoneOffset(_def.RotationOffset, _def.TranslationOffset));
        }
    }
}

// ---- State machine query --------------------------------------------------------------------

/// <summary>
/// Reads timing from a state machine node's active state (time-in-state or normalized progress),
/// producing a float. Combine with a <see cref="FloatCompareDefinition"/> to build "state finished"
/// or "after N seconds" transition conditions.
/// </summary>
public sealed class StateQueryDefinition : ValueNodeDefinition
{
    public StateQueryDefinition(int stateMachineNodeIndex, StateQuery query)
    { StateMachineNodeIndex = stateMachineNodeIndex; Query = query; }
    public int StateMachineNodeIndex { get; }
    public StateQuery Query { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly StateQueryDefinition _def;
        private StateMachineInstance _machine = null!;
        public Instance(StateQueryDefinition def) => _def = def;
        public override void Bind(GraphBindContext context)
        {
            int index = _def.StateMachineNodeIndex;
            if (index < 0 || index >= context.Nodes.Length || context.Nodes[index] is not StateMachineInstance machine)
                throw context.Error($"expects a state machine at index {index}.");
            _machine = machine;
        }
        protected override ParameterValue Compute(GraphContext context)
        {
            float r = _def.Query switch
            {
                StateQuery.TimeInState => _machine.TimeInCurrentState,
                StateQuery.NormalizedTime => _machine.CurrentStateNormalizedTime,
                _ => 0f
            };
            return ParameterValue.FromFloat(r);
        }
    }
}

// ---- Vector create / info -------------------------------------------------------------------

/// <summary>Builds a vector from three float values.</summary>
public sealed class VectorCreateDefinition : ValueNodeDefinition
{
    public VectorCreateDefinition(int x, int y, int z) { X = x; Y = y; Z = z; }
    public int X { get; }
    public int Y { get; }
    public int Z { get; }
    public override AnimationValueType ValueType => AnimationValueType.Vector;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly VectorCreateDefinition _def;
        private ValueNodeInstance _x = null!, _y = null!, _z = null!;
        public Instance(VectorCreateDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) { _x = context.ValueNode(_def.X, ValueInputKind.Number); _y = context.ValueNode(_def.Y, ValueInputKind.Number); _z = context.ValueNode(_def.Z, ValueInputKind.Number); }
        protected override ParameterValue Compute(GraphContext context)
            => ParameterValue.FromVector(new Float3(_x.GetValue(context).AsFloat(), _y.GetValue(context).AsFloat(), _z.GetValue(context).AsFloat()));
    }
}

/// <summary>Extracts a component or the length of a vector value as a float.</summary>
public sealed class VectorInfoDefinition : ValueNodeDefinition
{
    public VectorInfoDefinition(int input, VectorComponent component) { Input = input; Component = component; }
    public int Input { get; }
    public VectorComponent Component { get; }
    public override AnimationValueType ValueType => AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly VectorInfoDefinition _def;
        private ValueNodeInstance _input = null!;
        public Instance(VectorInfoDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _input = context.ValueNode(_def.Input, ValueInputKind.Vector);
        protected override ParameterValue Compute(GraphContext context)
        {
            Float3 v = _input.GetValue(context).Vector;
            float r = _def.Component switch
            {
                VectorComponent.X => v.X,
                VectorComponent.Y => v.Y,
                VectorComponent.Z => v.Z,
                VectorComponent.Length => Float3.Length(v),
                _ => 0f
            };
            return ParameterValue.FromFloat(r);
        }
    }
}
