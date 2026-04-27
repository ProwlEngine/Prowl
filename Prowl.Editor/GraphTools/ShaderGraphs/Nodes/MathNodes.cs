// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;


namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

// ─────────────────────────────────────────────────────────────────────────────
// Color accent management for math node categories
// ─────────────────────────────────────────────────────────────────────────────

internal static class MathAccents
{
    public static readonly System.Drawing.Color Arithmetic = System.Drawing.Color.FromArgb(255, 150, 130, 80);
    public static readonly System.Drawing.Color Trig = System.Drawing.Color.FromArgb(255, 80, 150, 160);
    public static readonly System.Drawing.Color Constant = System.Drawing.Color.FromArgb(255, 130, 90, 160);
}

// ═════════════════════════════════════════════════════════════════════════════
// UNARY ARITHMETIC NODES
// ═════════════════════════════════════════════════════════════════════════════

public sealed class AbsNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Abs";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "abs", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class CeilNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Ceil";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "ceil", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class FloorNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Floor";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "floor", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class RoundNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Round";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    // GLSL 4.00+ has native round(); Prowl targets 4.1 so use it directly.
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "round", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class TruncNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Trunc";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "trunc", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class FracNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Frac";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    // GLSL uses 'fract', not 'frac'
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "fract", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class SignNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Sign";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "sign", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class NegateNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Negate";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryExpr(this, "(-", ")", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class OneMinusNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "One Minus";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryExpr(this, "(1.0 - ", ")", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class ReciprocalNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Reciprocal";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryExpr(this, "(1.0 / ", ")", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class SqrtNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Sqrt";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "sqrt", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class ExpNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Exp";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "exp", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

/// <summary>Natural logarithm (base e). See <see cref="Log2Node"/> and
/// <see cref="Log10Node"/> for the other common bases.</summary>
public sealed class LogNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Log";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 1f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "log", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

/// <summary>Base-2 logarithm GLSL's native <c>log2</c>.</summary>
public sealed class Log2Node : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Log2";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 1f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "log2", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

/// <summary>Base-10 logarithm. GLSL has no native log10 emit <c>log(x) / log(10)</c>.</summary>
public sealed class Log10Node : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Log10";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 1f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var t = ShaderEmit.TypeFromInput(this, "In", ctx);
        var inExpr = ctx.EvaluateInputAs(GetInput("In")!, t);
        // 1/ln(10) prefer mul over div so the GPU can fuse this with surrounding ALU.
        return $"(log({inExpr}) * 0.4342944819)";
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class Clamp01Node : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Clamp 0-1";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var input = this.GetInput("In")!;
        var t = ctx.GetSourceType(input);
        return $"clamp({ctx.EvaluateInputAs(input, t)}, 0.0, 1.0)";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

// ═════════════════════════════════════════════════════════════════════════════
// BINARY ARITHMETIC NODES
// ═════════════════════════════════════════════════════════════════════════════

public sealed class AddNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Add";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("A", 0f, required: true); AddInput<float>("B", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.BinaryOp(this, "+", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInputs(this, "A", "B", ctx);
}

public sealed class SubtractNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Subtract";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("A", 0f, required: true); AddInput<float>("B", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.BinaryOp(this, "-", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInputs(this, "A", "B", ctx);
}

public sealed class MultiplyNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Multiply";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("A", 0f, required: true); AddInput<float>("B", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.BinaryOp(this, "*", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInputs(this, "A", "B", ctx);
}

public sealed class DivideNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Divide";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("A", 0f, required: true); AddInput<float>("B", 1f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.BinaryOp(this, "/", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInputs(this, "A", "B", ctx);
}

public sealed class MinNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Min";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("A", 0f, required: true); AddInput<float>("B", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.BinaryFunc(this, "min", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInputs(this, "A", "B", ctx);
}

public sealed class MaxNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Max";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("A", 0f, required: true); AddInput<float>("B", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.BinaryFunc(this, "max", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInputs(this, "A", "B", ctx);
}

public sealed class PowerNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Power";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("Base", 1f, required: true); AddInput<float>("Exp", 1f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.BinaryFunc(this, "pow", ctx, "Base", "Exp");
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInputs(this, "Base", "Exp", ctx);
}

public sealed class FmodNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Fmod";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("A", 0f, required: true); AddInput<float>("B", 1f, required: true); AddOutput<float>("Out"); }
    // GLSL uses 'mod', not 'fmod'
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.BinaryFunc(this, "mod", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInputs(this, "A", "B", ctx);
}

public sealed class StepNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Step";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("Edge", 0f, required: true); AddInput<float>("X", 0f, required: true); AddOutput<float>("Out"); }
    // GLSL step(edge, x) returns 0.0 if x < edge, 1.0 otherwise
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.BinaryFunc(this, "step", ctx, "Edge", "X");
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInputs(this, "Edge", "X", ctx);
}

// ═════════════════════════════════════════════════════════════════════════════
// TRIGONOMETRY NODES
// ═════════════════════════════════════════════════════════════════════════════

public sealed class SinNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Sin";
    public override string Category => "Trigonometry";
    public override System.Drawing.Color AccentColor => MathAccents.Trig;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "sin", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class CosNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Cos";
    public override string Category => "Trigonometry";
    public override System.Drawing.Color AccentColor => MathAccents.Trig;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "cos", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class TanNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Tan";
    public override string Category => "Trigonometry";
    public override System.Drawing.Color AccentColor => MathAccents.Trig;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "tan", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class ArcSinNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "ArcSin";
    public override string Category => "Trigonometry";
    public override System.Drawing.Color AccentColor => MathAccents.Trig;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "asin", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class ArcCosNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "ArcCos";
    public override string Category => "Trigonometry";
    public override System.Drawing.Color AccentColor => MathAccents.Trig;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "acos", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class ArcTanNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "ArcTan";
    public override string Category => "Trigonometry";
    public override System.Drawing.Color AccentColor => MathAccents.Trig;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.UnaryFunc(this, "atan", "In", ctx);
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class ArcTan2Node : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "ArcTan2";
    public override string Category => "Trigonometry";
    public override System.Drawing.Color AccentColor => MathAccents.Trig;
    protected override void DefineNode() { AddInput<float>("Y", 0f, required: true); AddInput<float>("X", 1f, required: true); AddOutput<float>("Out"); }
    // GLSL's two-arg atan is atan(y, x)
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => ShaderEmit.BinaryFunc(this, "atan", ctx, "Y", "X");
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInputs(this, "Y", "X", ctx);
}

// ═════════════════════════════════════════════════════════════════════════════
// MATH CONSTANT NODES
// ═════════════════════════════════════════════════════════════════════════════

public sealed class PiNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Pi";
    public override string Category => "Math Constants";
    public override System.Drawing.Color AccentColor => MathAccents.Constant;
    protected override void DefineNode() => AddOutput<float>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => "3.14159265359";
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

public sealed class TauNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Tau";
    public override string Category => "Math Constants";
    public override System.Drawing.Color AccentColor => MathAccents.Constant;
    protected override void DefineNode() => AddOutput<float>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => "6.28318530718";
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

public sealed class PhiNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Phi";
    public override string Category => "Math Constants";
    public override System.Drawing.Color AccentColor => MathAccents.Constant;
    protected override void DefineNode() => AddOutput<float>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => "1.61803398875";
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

public sealed class ENode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "e";
    public override string Category => "Math Constants";
    public override System.Drawing.Color AccentColor => MathAccents.Constant;
    protected override void DefineNode() => AddOutput<float>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => "2.718281828459";
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

public sealed class Root2Node : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Root 2";
    public override string Category => "Math Constants";
    public override System.Drawing.Color AccentColor => MathAccents.Constant;
    protected override void DefineNode() => AddOutput<float>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx) => "1.41421356237309504";
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// CONSTANT-PARAMETER SIMPLE NODES (baked literal min/max values)
// ═════════════════════════════════════════════════════════════════════════════

public sealed class ClampSimpleNode : Node, IShaderNode, IShaderGraphNode
{
    public float Min = 0f;
    public float Max = 1f;

    public override string Title => "Clamp (Simple)";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("In", 0f, required: true); AddOutput<float>("Out"); }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var input = this.GetInput("In")!;
        var t = ctx.GetSourceType(input);
        var minStr = ShaderGenContext.Fmt(Min);
        var maxStr = ShaderGenContext.Fmt(Max);
        return $"clamp({ctx.EvaluateInputAs(input, t)}, {minStr}, {maxStr})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "In", ctx);
}

public sealed class LerpSimpleNode : Node, IShaderNode, IShaderGraphNode
{
    // Public fields, not properties Echo's serializer is fields-only and silently
    // drops property-backed values across save/load. Keep these as fields so the
    // user-edited endpoints survive a graph reload.
    public float A = 0f;
    public float B = 1f;

    public override string Title => "Lerp (Simple)";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("T", 0f, required: true); AddOutput<float>("Out"); }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var input = this.GetInput("T")!;
        var t = ctx.GetSourceType(input);
        var aStr = ShaderGenContext.Fmt(A);
        var bStr = ShaderGenContext.Fmt(B);
        return $"mix({aStr}, {bStr}, {ctx.EvaluateInputAs(input, t)})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "T", ctx);
}

public sealed class InverseLerpSimpleNode : Node, IShaderNode, IShaderGraphNode
{
    // See LerpSimpleNode public fields are required by Echo's fields-only
    // serializer; converting to properties silently drops the values.
    public float A = 0f;
    public float B = 1f;

    public override string Title => "InvLerp (Simple)";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => MathAccents.Arithmetic;
    protected override void DefineNode() { AddInput<float>("V", 0f, required: true); AddOutput<float>("Out"); }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var input = this.GetInput("V")!;
        var t = ctx.GetSourceType(input);
        var aStr = ShaderGenContext.Fmt(A);
        var bStr = ShaderGenContext.Fmt(B);
        return $"(({ctx.EvaluateInputAs(input, t)} - {aStr}) / ({bStr} - {aStr}))";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderEmit.TypeFromInput(this, "V", ctx);
}
