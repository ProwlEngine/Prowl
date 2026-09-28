// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Motion;
using Prowl.Vector;

namespace Prowl.Runtime.AnimationNodes;

public sealed class CachedValueNode : AnimationGraphNode
{
    private readonly InputPin _value = NumberInput("Value", optional: false), _sample = FlagInput("Sample");
    private readonly ChoiceSetting<CachedValueMode> _mode = Choice("Mode", CachedValueMode.OnEntry);

    public CachedValueNode()
    {
        Pins(_value, _sample);
        Settings(_mode);
    }

    public override string Id => AnimationNodeIds.CachedValue;
    public override string DisplayName => "Hold Value";
    public override string Category => "Values";
    public override string Description => "Samples its input once and holds it.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddCachedValue(ctx.Input(r, _value), ctx.Input(r, _sample), r.Get(_mode));
}

public sealed class ConstBoolNode : AnimationGraphNode
{
    private readonly FlagSetting _value = Toggle("Value");

    public ConstBoolNode()
    {
        Settings(_value);
    }

    public override string Id => AnimationNodeIds.ConstBool;
    public override string DisplayName => "Flag";
    public override string Category => "Values";
    public override string Description => "A fixed true or false.";

    public override NodePinKind Output => NodePinKind.Flag;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddConstBool(r.Get(_value));
}

public sealed class ConstFloatNode : AnimationGraphNode
{
    private readonly NumberSetting _value = Number("Value");

    public ConstFloatNode()
    {
        Settings(_value);
    }

    public override string Id => AnimationNodeIds.ConstFloat;
    public override string DisplayName => "Number";
    public override string Category => "Values";
    public override string Description => "A fixed number.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddConstFloat(r.Get(_value));
}

public sealed class ConstIdNode : AnimationGraphNode
{
    private readonly TextSetting _value = Name("Value");

    public ConstIdNode()
    {
        Settings(_value);
    }

    public override string Id => AnimationNodeIds.ConstId;
    public override string DisplayName => "Name";
    public override string Category => "Values";
    public override string Description => "A fixed name, for matching against events and states.";

    public override NodePinKind Output => NodePinKind.Id;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddConstId(ctx.Id(r, _value));
}

public sealed class ConstIntNode : AnimationGraphNode
{
    private readonly IntegerSetting _value = Count("Value");

    public ConstIntNode()
    {
        Settings(_value);
    }

    public override string Id => AnimationNodeIds.ConstInt;
    public override string DisplayName => "Whole Number";
    public override string Category => "Values";
    public override string Description => "A fixed integer.";

    public override NodePinKind Output => NodePinKind.Integer;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddConstInt(r.Get(_value));
}

public sealed class ConstVectorNode : AnimationGraphNode
{
    private readonly VectorSetting _value = new("Value");

    public ConstVectorNode()
    {
        Settings(_value);
    }

    public override string Id => AnimationNodeIds.ConstVector;
    public override string DisplayName => "Vector";
    public override string Category => "Values";
    public override string Description => "A fixed direction or position.";

    public override NodePinKind Output => NodePinKind.Vector;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddConstVector(r.Get(_value));
}

public sealed class CurveNode : AnimationGraphNode
{
    private readonly InputPin _value = NumberInput("Value", optional: false);
    private readonly AssetSetting _curve = Asset("Curve", NodeValueKind.Curve);

    public CurveNode()
    {
        Pins(_value);
        Settings(_curve);
    }

    public override string Id => AnimationNodeIds.Curve;
    public override string DisplayName => "Curve";
    public override string Category => "Values";
    public override string Description => "Shapes a number through an authored curve.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Curve(r, _curve) is { } shape ? ctx.Graph.AddCurve(ctx.Input(r, _value), shape) : ctx.Input(r, _value);
}

public sealed class FloatAngleNode : AnimationGraphNode
{
    private readonly InputPin _degrees = NumberInput("Degrees", optional: false);
    private readonly ChoiceSetting<AngleOp> _op = Choice("Op", AngleOp.ClampTo180);

    public FloatAngleNode()
    {
        Pins(_degrees);
        Settings(_op);
    }

    public override string Id => AnimationNodeIds.FloatAngle;
    public override string DisplayName => "Angle";
    public override string Category => "Values";
    public override string Description => "Wraps or flips an angle in degrees.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddFloatAngleMath(ctx.Input(r, _degrees), r.Get(_op));
}

public sealed class FloatCompareNode : AnimationGraphNode
{
    private readonly InputPin _a = NumberInput("A", optional: false), _b = NumberInput("B", optional: false);
    private readonly ChoiceSetting<CompareOp> _op = Choice("Op", CompareOp.Greater);

    public FloatCompareNode()
    {
        Pins(_a, _b);
        Settings(_op);
    }

    public override string Id => AnimationNodeIds.FloatCompare;
    public override string DisplayName => "Compare";
    public override string Category => "Values";
    public override string Description => "Tests two numbers against each other.";

    public override NodePinKind Output => NodePinKind.Flag;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddFloatCompare(ctx.Input(r, _a), ctx.Input(r, _b), r.Get(_op));
}

public sealed class FloatEaseNode : AnimationGraphNode
{
    private readonly InputPin _value = NumberInput("Value", optional: false);
    private readonly NumberSetting _seconds = Number("Seconds", 0.2f), _startValue = Number("StartValue", label: "Start Value");
    private readonly ChoiceSetting<EasingOp> _easing = Choice("Easing", EasingOp.EaseInOut);
    private readonly FlagSetting _useStart = Toggle("UseStartValue", label: "Use Start Value");

    public FloatEaseNode()
    {
        Pins(_value);
        Settings(_seconds, _easing, _useStart, _startValue);
    }

    public override string Id => AnimationNodeIds.FloatEase;
    public override string DisplayName => "Ease";
    public override string Category => "Values";
    public override string Description => "Eases toward its input rather than jumping to it.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddFloatEase(ctx.Input(r, _value), r.Get(_seconds), r.Get(_easing), r.Get(_useStart), r.Get(_startValue));
}

public sealed class FloatMathNode : AnimationGraphNode
{
    private readonly InputPin _a = NumberInput("A", optional: false), _b = NumberInput("B", optional: false);
    private readonly ChoiceSetting<FloatMathOp> _op = Choice("Op", FloatMathOp.Add);

    public FloatMathNode()
    {
        Pins(_a, _b);
        Settings(_op);
    }

    public override string Id => AnimationNodeIds.FloatMath;
    public override string DisplayName => "Maths";
    public override string Category => "Values";
    public override string Description => "Combines two numbers, or changes one.";

    public override NodePinKind Output => NodePinKind.Number;

    protected override bool PinShown(GraphNodeRecord r, InputPin pin)
        => pin != _b || !FloatMathDefinition.IsUnary(r.Get(_op));

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => FloatMathDefinition.IsUnary(r.Get(_op))
        ? ctx.Graph.AddNode(new FloatMathDefinition(ctx.Input(r, _a), -1, r.Get(_op)))
        : ctx.Graph.AddFloatMath(ctx.Input(r, _a), ctx.Input(r, _b), r.Get(_op));
}

public sealed class FloatRangeNode : AnimationGraphNode
{
    private readonly InputPin _value = NumberInput("Value", optional: false);
    private readonly NumberSetting _min = Number("Min"), _max = Number("Max", 1f);
    private readonly FlagSetting _inclusive = Toggle("Inclusive", true);

    public FloatRangeNode()
    {
        Pins(_value);
        Settings(_min, _max, _inclusive);
    }

    public override string Id => AnimationNodeIds.FloatRange;
    public override string DisplayName => "In Range";
    public override string Category => "Values";
    public override string Description => "True while a number sits between two bounds.";

    public override NodePinKind Output => NodePinKind.Flag;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddFloatRangeComparison(ctx.Input(r, _value), r.Get(_min), r.Get(_max), r.Get(_inclusive));
}

public sealed class FloatRemapNode : AnimationGraphNode
{
    private readonly InputPin _value = NumberInput("Value", optional: false);
    private readonly NumberSetting _inMin = Number("InMin", label: "In Min"), _inMax = Number("InMax", 1f, "In Max");
    private readonly NumberSetting _outMin = Number("OutMin", label: "Out Min"), _outMax = Number("OutMax", 1f, "Out Max");
    private readonly FlagSetting _clamp = Toggle("Clamp");

    public FloatRemapNode()
    {
        Pins(_value);
        Settings(_inMin, _inMax, _outMin, _outMax, _clamp);
    }

    public override string Id => AnimationNodeIds.FloatRemap;
    public override string DisplayName => "Remap";
    public override string Category => "Values";
    public override string Description => "Maps a number from one range onto another. Clamp stops it at the ends, which with the same range on both sides is a plain clamp.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddNode(new FloatRemapDefinition(ctx.Input(r, _value), r.Get(_inMin), r.Get(_inMax), r.Get(_outMin), r.Get(_outMax))
        {
            Clamp = r.Get(_clamp),
        });
}

public sealed class FloatSpringNode : AnimationGraphNode
{
    private readonly InputPin _value = NumberInput("Value", optional: false);
    private readonly NumberSetting _frequency = Number("Frequency", 4f), _damping = Number("Damping", 1f);

    public FloatSpringNode()
    {
        Pins(_value);
        Settings(_frequency, _damping);
    }

    public override string Id => AnimationNodeIds.FloatSpring;
    public override string DisplayName => "Spring";
    public override string Category => "Values";
    public override string Description => "Chases its input under a spring, so it never jumps.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddFloatSpring(ctx.Input(r, _value), r.Get(_frequency), r.Get(_damping));
}

public sealed class IdComparisonNode : AnimationGraphNode
{
    private readonly InputPin _name = NameInput("Name");
    private readonly ChoiceSetting<IdComparison> _comparison = Choice("Comparison", IdComparison.Matches);
    private readonly TextSetting _names = TextList("Names");

    public IdComparisonNode()
    {
        Pins(_name);
        Settings(_comparison, _names);
    }

    public override string Id => AnimationNodeIds.IdComparison;
    public override string DisplayName => "Name Matches";
    public override string Category => "Values";
    public override string Description => "Tests a name against a comma separated list.";

    public override NodePinKind Output => NodePinKind.Flag;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddIdComparison(ctx.Input(r, _name), r.Get(_comparison), ctx.Ids(r, _names));
}

public sealed class IdToFloatNode : AnimationGraphNode
{
    private readonly InputPin _name = NameInput("Name");
    private readonly TextSetting _values = TextList("Values");
    private readonly TextSetting _names;
    private readonly NumberSetting _fallback = Number("Default");

    public IdToFloatNode()
    {
        _names = new TextSetting("Names") { List = true, PairedWith = _values };
        Pins(_name);
        Settings(_names, _values, _fallback);
    }

    public override string Id => AnimationNodeIds.IdToFloat;
    public override string DisplayName => "Name To Number";
    public override string Category => "Values";
    public override string Description => "Maps each name in a list to the number beside it.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        StringID[] ids = ctx.Ids(r, _names);
        float[] numbers = ctx.Numbers(r, _values);
        int count = Math.Min(ids.Length, numbers.Length);
        return count == 0
            ? ctx.Graph.AddConstFloat(r.Get(_fallback))
            : ctx.Graph.AddIdToFloat(ctx.Input(r, _name), ids[..count], numbers[..count], r.Get(_fallback));
    }
}

public sealed class LogicNode : AnimationGraphNode
{
    private readonly InputPin _a = FlagInput("A", optional: false), _b = FlagInput("B", optional: false);
    private readonly ChoiceSetting<BoolOp> _op = Choice("Op", BoolOp.And);

    public LogicNode()
    {
        Pins(_a, _b);
        Settings(_op);
    }

    public override string Id => AnimationNodeIds.Logic;
    public override string DisplayName => "Logic";
    public override string Category => "Values";
    public override string Description => "And, Or, or Not of flags.";

    public override NodePinKind Output => NodePinKind.Flag;

    protected override bool PinShown(GraphNodeRecord r, InputPin pin)
        => pin != _b || r.Get(_op) != BoolOp.Not;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => r.Get(_op) switch
        {
            BoolOp.Not => ctx.Graph.AddNot(ctx.Input(r, _a)),
            BoolOp.Or => ctx.Graph.AddOr(ctx.Input(r, _a), ctx.Input(r, _b)),
            _ => ctx.Graph.AddAnd(ctx.Input(r, _a), ctx.Input(r, _b)),
        };
}

public sealed class NoiseNode : AnimationGraphNode
{
    private readonly NumberSetting _frequency = Number("Frequency", 1f), _amplitude = Number("Amplitude", 1f);
    private readonly IntegerSetting _seed = Count("Seed", 1), _octaves = Count("Octaves", 1);

    public NoiseNode()
    {
        Settings(_frequency, _amplitude, _seed, _octaves);
    }

    public override string Id => AnimationNodeIds.Noise;
    public override string DisplayName => "Noise";
    public override string Category => "Values";
    public override string Description => "A wandering number, for idle variation.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddNoise(r.Get(_frequency), r.Get(_amplitude), (uint)r.Get(_seed), r.Get(_octaves));
}

public sealed class ParameterNode : AnimationGraphNode
{
    /// <summary>The parameter read, which a Virtual Parameter names the same way.</summary>
    internal static readonly TextSetting NameSetting = new("Name");

    public ParameterNode()
    {
        Settings(NameSetting);
    }

    public override string Id => AnimationNodeIds.Parameter;
    public override string DisplayName => "Parameter";
    public override string Category => "Values";
    public override string Description => "Reads a value the game sets on the animator.";

    public override NodePinKind Output => NodePinKind.Number;

    // Made by dragging a parameter out of the graph editor's side panel.
    public override bool Hidden => true;

    public override NodePinKind OutputOf(AnimationGraph? asset, GraphNodeRecord r)
    {
        if (asset == null) return Output;
        string name = r.Get(NameSetting);
        foreach (GraphParameterRecord parameter in asset.Parameters)
            if (parameter.Name == name) return PinKindOf(parameter.Kind);
        return Output;
    }

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        string name = r.Get(NameSetting);
        if (ctx.Parameters.TryGetValue(name, out int index)) return index;
        if (ctx.VirtualParameter(name) is { } named) return ctx.Node(named.Id);
        Debug.LogWarning($"[AnimationGraph] A node reads the parameter '{name}', which the graph does not declare.");
        return -1;
    }
}

public sealed class PickNumberNode : AnimationGraphNode
{
    private readonly InputPin _condition = FlagInput("Condition", optional: false, variadic: true), _value = NumberInput("Value", variadic: true);
    private readonly NumberSetting _fallback = Driven("Default"), _easeTime = Number("EaseTime", 0.2f, "Ease Time");
    private readonly ChoiceSetting<EasingOp> _easing = Choice("Easing", EasingOp.None);

    public PickNumberNode()
    {
        Pins(_condition, _value);
        Settings(_fallback, _easeTime, _easing);
    }

    public override string Id => AnimationNodeIds.PickNumber;
    public override string DisplayName => "Choose Number";
    public override string Category => "Values";
    public override string Description => "The value beside the first condition that reads true, or the default when none does.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        FloatInput otherwise = ctx.Get(r, _fallback);
        List<PinGroup> options = ctx.Groups(r);
        if (options.Count == 0) return otherwise.IsDriven ? otherwise.NodeIndex : ctx.Graph.AddConstFloat(otherwise.Constant);

        var conditions = new int[options.Count];
        var values = new FloatInput[options.Count];
        for (int i = 0; i < options.Count; i++)
        {
            conditions[i] = ctx.Input(options[i], _condition);
            // An unwired value reads the number kept on its condition.
            int wired = ctx.Input(options[i], _value);
            float kept = options[i].Entry.Value;
            values[i] = wired >= 0 ? FloatInput.From(wired, kept) : FloatInput.Of(kept);
        }
        return ctx.Graph.AddNode(new FloatSelectorDefinition(conditions, values, otherwise, r.Get(_easeTime), r.Get(_easing)));
    }
}

public sealed class TargetInfoNode : AnimationGraphNode
{
    private readonly InputPin _target = TargetInput();
    private readonly ChoiceSetting<TargetField> _info = Choice("Info", TargetField.Distance);

    public TargetInfoNode()
    {
        Pins(_target);
        Settings(_info);
    }

    public override string Id => AnimationNodeIds.TargetInfo;
    public override string DisplayName => "Target Info";
    public override string Category => "Values";
    public override string Description => "Reads something out of a target: whether it is set, its point, a distance or an angle.";

    public override NodePinKind Output => NodePinKind.Number;

    public override NodePinKind OutputOf(AnimationGraph? asset, GraphNodeRecord r)
        => r.Get(_info) switch
        {
            TargetField.IsSet => NodePinKind.Flag,
            TargetField.Point => NodePinKind.Vector,
            _ => NodePinKind.Number,
        };

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => r.Get(_info) switch
        {
            TargetField.IsSet => ctx.Graph.AddIsTargetSet(ctx.Input(r, _target)),
            TargetField.Point => ctx.Graph.AddTargetPoint(ctx.Input(r, _target)),
            TargetField field => ctx.Graph.AddTargetInfo(ctx.Input(r, _target), ToTargetInfo(field)),
        };

    private static TargetInfo ToTargetInfo(TargetField field) => field switch
    {
        TargetField.AngleHorizontal => TargetInfo.AngleHorizontal,
        TargetField.AngleVertical => TargetInfo.AngleVertical,
        TargetField.DistanceHorizontalOnly => TargetInfo.DistanceHorizontalOnly,
        TargetField.DistanceVerticalOnly => TargetInfo.DistanceVerticalOnly,
        TargetField.DeltaOrientationX => TargetInfo.DeltaOrientationX,
        TargetField.DeltaOrientationY => TargetInfo.DeltaOrientationY,
        TargetField.DeltaOrientationZ => TargetInfo.DeltaOrientationZ,
        _ => TargetInfo.Distance,
    };
}

/// <summary>What a Target Info node reads out of a target.</summary>
public enum TargetField
{
    IsSet,
    Point,
    AngleHorizontal,
    AngleVertical,
    Distance,
    DistanceHorizontalOnly,
    DistanceVerticalOnly,
    DeltaOrientationX,
    DeltaOrientationY,
    DeltaOrientationZ,
}

public sealed class TargetOffsetNode : AnimationGraphNode
{
    private readonly InputPin _target = TargetInput();
    private readonly VectorSetting _rotation = new("Rotation"), _translation = new("Translation");

    public TargetOffsetNode()
    {
        Pins(_target);
        Settings(_rotation, _translation);
    }

    public override string Id => AnimationNodeIds.TargetOffset;
    public override string DisplayName => "Offset Target";
    public override string Category => "Values";
    public override string Description => "Shifts and turns a target by a fixed amount.";

    public override NodePinKind Output => NodePinKind.Target;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddTargetOffset(ctx.Input(r, _target), Quaternion.FromEuler(r.Get(_rotation)), r.Get(_translation));
}

public sealed class TimerNode : AnimationGraphNode
{
    private readonly InputPin _reset = FlagInput("Reset");
    private readonly NumberSetting _loop = Number("Loop");
    private readonly FlagSetting _normalized = Toggle("Normalized");

    public TimerNode()
    {
        Pins(_reset);
        Settings(_loop, _normalized);
    }

    public override string Id => AnimationNodeIds.Timer;
    public override string DisplayName => "Timer";
    public override string Category => "Values";
    public override string Description => "Counts seconds, optionally looping.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddTimer(r.Get(_loop), r.Get(_normalized), ctx.Input(r, _reset));
}

public sealed class VectorCreateNode : AnimationGraphNode
{
    private readonly InputPin _x = NumberInput("X", optional: false), _y = NumberInput("Y", optional: false), _z = NumberInput("Z", optional: false);

    public VectorCreateNode()
    {
        Pins(_x, _y, _z);
    }

    public override string Id => AnimationNodeIds.VectorCreate;
    public override string DisplayName => "Make Vector";
    public override string Category => "Values";
    public override string Description => "Builds a vector from three numbers.";

    public override NodePinKind Output => NodePinKind.Vector;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddVectorCreate(ctx.Input(r, _x), ctx.Input(r, _y), ctx.Input(r, _z));
}

public sealed class VectorInfoNode : AnimationGraphNode
{
    private readonly InputPin _vector = VectorInput("Vector");
    private readonly ChoiceSetting<VectorComponent> _component = Choice("Component", VectorComponent.X);

    public VectorInfoNode()
    {
        Pins(_vector);
        Settings(_component);
    }

    public override string Id => AnimationNodeIds.VectorInfo;
    public override string DisplayName => "Vector Component";
    public override string Category => "Values";
    public override string Description => "Reads one component of a vector, or its length.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddVectorInfo(ctx.Input(r, _vector), r.Get(_component));
}

public sealed class VectorNegateNode : AnimationGraphNode
{
    private readonly InputPin _vector = VectorInput("Vector");

    public VectorNegateNode()
    {
        Pins(_vector);
    }

    public override string Id => AnimationNodeIds.VectorNegate;
    public override string DisplayName => "Negate Vector";
    public override string Category => "Values";
    public override string Description => "Points a vector the other way.";

    public override NodePinKind Output => NodePinKind.Vector;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddVectorNegate(ctx.Input(r, _vector));
}

public sealed class VirtualParameterNode : AnimationGraphNode
{
    private readonly InputPin _value = NumberInput("Value", optional: false);

    public VirtualParameterNode()
    {
        Pins(_value);
        Settings(ParameterNode.NameSetting);
    }

    public override string Id => AnimationNodeIds.VirtualParameter;
    public override string DisplayName => "Virtual Parameter";
    public override string Category => "Values";
    public override string Description => "Names a value, which Parameter nodes can then read by that name like any parameter.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        string name = r.Get(ParameterNode.NameSetting);
        return name.Length == 0 ? ctx.Input(r, _value) : ctx.Graph.AddVirtualParameter(name, ctx.Input(r, _value));
    }
}
