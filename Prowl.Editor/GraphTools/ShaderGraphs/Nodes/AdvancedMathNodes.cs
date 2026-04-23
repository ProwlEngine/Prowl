// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Advanced math nodes implementing standard shader operations.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

// ─── Lerp ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Linear interpolation: mix(A, B, T).
/// Output type = max-channel of A, B, T.
/// </summary>
public sealed class LerpNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Lerp";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 150, 130, 80);

    protected override void DefineNode()
    {
        AddInput<float>("A", 0f, required: true);
        AddInput<float>("B", 1f, required: true);
        AddInput<float>("T", 0.5f, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => ShaderEmit.TernaryFunc(this, "mix", "A", "B", "T", ctx);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInputs(this, "A", "B", "T", ctx);
}

// ─── Clamp ───────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Clamps In to [Min, Max]: clamp(In, Min, Max).
/// Output type = max-channel of In, Min, Max.
/// </summary>
public sealed class ClampNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Clamp";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 150, 130, 80);

    protected override void DefineNode()
    {
        AddInput<float>("In", 0f, required: true);
        AddInput<float>("Min", 0f, required: true);
        AddInput<float>("Max", 1f, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => ShaderEmit.TernaryFunc(this, "clamp", "In", "Min", "Max", ctx);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInputs(this, "In", "Min", "Max", ctx);
}

// ─── InverseLerp ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Inverse lerp: (Val - A) / (B - A).
/// Output type = max-channel of A, B, Val.
/// </summary>
public sealed class InverseLerpNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Inverse Lerp";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 150, 130, 80);

    protected override void DefineNode()
    {
        AddInput<float>("A", 0f, required: true);
        AddInput<float>("B", 1f, required: true);
        AddInput<float>("Val", 0.5f, required: true);
        AddOutput<float>("T");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var t = ShaderEmit.TypeFromInputs(this, "A", "B", "Val", ctx);
        var a   = ctx.EvaluateInputAs(GetInput("A")!,   t);
        var b   = ctx.EvaluateInputAs(GetInput("B")!,   t);
        var val = ctx.EvaluateInputAs(GetInput("Val")!, t);
        return $"(({val} - {a}) / ({b} - {a}))";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInputs(this, "A", "B", "Val", ctx);
}

// ─── Smoothstep ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Smooth Hermite interpolation: smoothstep(Min, Max, Val).
/// Output type = max-channel of Min, Max, Val.
/// </summary>
public sealed class SmoothstepNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Smoothstep";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 150, 130, 80);

    protected override void DefineNode()
    {
        AddInput<float>("Min", 0f, required: true);
        AddInput<float>("Max", 1f, required: true);
        AddInput<float>("Val", 0.5f, required: true);
        AddOutput<float>("T");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => ShaderEmit.TernaryFunc(this, "smoothstep", "Min", "Max", "Val", ctx);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInputs(this, "Min", "Max", "Val", ctx);
}

// ─── RemapNode ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Remaps Val from [iMin, iMax] to [oMin, oMax]:
///   oMin + (Val - iMin) * (oMax - oMin) / (iMax - iMin)
/// All five inputs wired; output type = max-channel of all five inputs.
/// </summary>
public sealed class RemapNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Remap";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 150, 130, 80);

    protected override void DefineNode()
    {
        AddInput<float>("Val",  0f,   required: true);
        AddInput<float>("iMin", 0f,   required: true);
        AddInput<float>("iMax", 1f,   required: true);
        AddInput<float>("oMin", -1f,  required: true);
        AddInput<float>("oMax", 1f,   required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var t    = ((IShaderNode)this).GetOutputType(p, ctx);
        var val  = ctx.EvaluateInputAs(GetInput("Val")!,  t);
        var iMin = ctx.EvaluateInputAs(GetInput("iMin")!, t);
        var iMax = ctx.EvaluateInputAs(GetInput("iMax")!, t);
        var oMin = ctx.EvaluateInputAs(GetInput("oMin")!, t);
        var oMax = ctx.EvaluateInputAs(GetInput("oMax")!, t);
        return $"({oMin} + ({val} - {iMin}) * ({oMax} - {oMin}) / ({iMax} - {iMin}))";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.MaxChannel(
            ShaderEmit.MaxChannel(
                ctx.GetSourceType(GetInput("Val")!),
                ctx.GetSourceType(GetInput("iMin")!),
                ctx.GetSourceType(GetInput("iMax")!)),
            ShaderEmit.MaxChannel(
                ctx.GetSourceType(GetInput("oMin")!),
                ctx.GetSourceType(GetInput("oMax")!)));
}

// ─── RemapSimpleNode ────────────────────────────────────────────────────────────────────────

/// <summary>
/// Remaps the single "In" input from [InMin, InMax] to [OutMin, OutMax] using
/// a pre-computed multiplier and offset baked into the shader as literals:
///   out = In * mul + off
/// where  mul = (OutMax - OutMin) / (InMax - InMin)
///        off = OutMin - InMin * mul
///
/// Output type follows the In wire.
/// </summary>
public sealed class RemapSimpleNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Remap (Simple)";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 150, 130, 80);

    // Public fields serialised by Echo, shown in the Inspector's PropertyGrid.
    public float InMin  = 0f;
    public float InMax  = 1f;
    public float OutMin = -1f;
    public float OutMax = 1f;

    protected override void DefineNode()
    {
        AddInput<float>("In", 0f, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        float oldRange = InMax - InMin;
        float mul = (oldRange == 0f) ? 0f : (OutMax - OutMin) / oldRange;
        float off = OutMin - InMin * mul;

        var t   = ctx.GetSourceType(GetInput("In")!);
        var val = ctx.EvaluateInputAs(GetInput("In")!, t);
        return $"({val} * {ShaderGenContext.Fmt(mul)} + {ShaderGenContext.Fmt(off)})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ctx.GetSourceType(GetInput("In")!);
}

// ─── PosterizeNode ───────────────────────────────────────────────────────────────────────

/// <summary>
/// Posterizes In to a given number of Steps (scalar):
///   floor(In * Steps) / (Steps - 1.0)
/// Steps is always kept as a scalar; In may be any width.
/// </summary>
public sealed class PosterizeNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Posterize";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 150, 130, 80);

    protected override void DefineNode()
    {
        AddInput<float>("In",    0f,   required: true);
        AddInput<float>("Steps", 4f,   required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // In may be any width; Steps stays scalar.
        var inType  = ctx.GetSourceType(GetInput("In")!);
        var val     = ctx.EvaluateInputAs(GetInput("In")!,    inType);
        var steps   = ctx.EvaluateInputAs(GetInput("Steps")!, ShaderType.Float);
        return $"(floor({val} * {steps}) / ({steps} - 1.0))";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ctx.GetSourceType(GetInput("In")!);
}

// ─── IfNode ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Branchless if-node: compares A and B (as scalars) and blends GT/EQ/LT results using step():
///   sta = step(A, B)       // 1 when A &lt;= B
///   stb = step(B, A)       // 1 when B &lt;= A
///   out = mix(sta*LT + stb*GT, EQ, sta*stb)
///
/// A and B are always compared as scalars (Float).
/// Output type = max-channel of GT, EQ, LT.
/// Intermediate step vars are cached in BodyPrelude.
/// </summary>
public sealed class IfNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "If";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 130, 100, 160);

    protected override void DefineNode()
    {
        AddInput<float>("A",   0f, required: true);
        AddInput<float>("B",   0f, required: true);
        AddInput<float>("GT",  1f, required: true);
        AddInput<float>("EQ",  0f, required: true);
        AddInput<float>("LT", -1f, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var staName = $"_if{Id:N}_sta";
        var stbName = $"_if{Id:N}_stb";

        if (!ctx.HelperFunctions.Contains(staName))
        {
            ctx.HelperFunctions.Add(staName);

            // A and B are compared as scalars evaluate at Float.
            var a = ctx.EvaluateInputAs(GetInput("A")!, ShaderType.Float);
            var b = ctx.EvaluateInputAs(GetInput("B")!, ShaderType.Float);
            ctx.BodyPrelude.AppendLine($"    float {staName} = step({a}, {b});");
            ctx.BodyPrelude.AppendLine($"    float {stbName} = step({b}, {a});");
        }

        // GT / EQ / LT evaluated at the unified output type.
        var t  = ((IShaderNode)this).GetOutputType(p, ctx);
        var gt = ctx.EvaluateInputAs(GetInput("GT")!, t);
        var eq = ctx.EvaluateInputAs(GetInput("EQ")!, t);
        var lt = ctx.EvaluateInputAs(GetInput("LT")!, t);

        var less   = $"({staName} * {lt})";
        var larger = $"({stbName} * {gt})";
        return $"mix({less} + {larger}, {eq}, {staName} * {stbName})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInputs(this, "GT", "EQ", "LT", ctx);
}

// ─── MultiplyMatrixNode ─────────────────────────────────────────────────────────────────

/// <summary>
/// Matrix / vector multiply: (A * B).
/// Follows GLSL's built-in * operator which handles mat4*mat4, mat4*vec4, vec4*mat4.
/// Output type: Mat4 when both inputs are matrices (Mat3 or Mat4); Vec4 otherwise.
/// </summary>
public sealed class MultiplyMatrixNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Multiply Matrix";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 100, 130, 160);

    protected override void DefineNode()
    {
        AddInput<Float4x4>("A", required: true);
        AddInput<Float4x4>("B", required: true);
        AddOutput<Float4x4>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Evaluate each side at its actual source type (mat4 or vec4).
        var ta = ctx.GetSourceType(GetInput("A")!);
        var tb = ctx.GetSourceType(GetInput("B")!);
        var a  = ctx.EvaluateInputAs(GetInput("A")!, ta);
        var b  = ctx.EvaluateInputAs(GetInput("B")!, tb);
        return $"({a} * {b})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
    {
        var ta = ctx.GetSourceType(GetInput("A")!);
        var tb = ctx.GetSourceType(GetInput("B")!);
        bool aIsMatrix = (ta == ShaderType.Mat4 || ta == ShaderType.Mat3);
        bool bIsMatrix = (tb == ShaderType.Mat4 || tb == ShaderType.Mat3);
        return (aIsMatrix && bIsMatrix) ? ShaderType.Mat4 : ShaderType.Vec4;
    }
}
