// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GraphTools.ShaderGraphs;

/// <summary>
/// Static helpers for the most common GLSL emission patterns — unary funcs, binary
/// ops, ternary funcs. Lets each node body stay terse:
/// <code>
/// string IShaderNode.Evaluate(...) =&gt; ShaderEmit.UnaryFunc(this, "abs", "In", ctx);
/// </code>
/// instead of repeating the type-promotion + EvaluateInputAs dance per class.
/// </summary>
public static class ShaderEmit
{
    /// <summary>Pick the unifying numeric type across two source types — typically the
    /// highest channel count wins (Float + Vec3 = Vec3) so the operator runs
    /// component-wise on the wider operand. Color is treated as Vec4 for promotion.</summary>
    public static ShaderType MaxChannel(ShaderType a, ShaderType b)
    {
        int ca = ShaderTypeUtil.Channels(a);
        int cb = ShaderTypeUtil.Channels(b);
        // Both samplers / matrices / unknown — fall back to a; Promote will pass them
        // through unchanged and let GLSL surface the real error.
        if (ca == 0 && cb == 0) return a;
        if (ca >= cb) return Widen(a, ca, cb);
        return Widen(b, cb, ca);
    }

    public static ShaderType MaxChannel(ShaderType a, ShaderType b, ShaderType c)
        => MaxChannel(MaxChannel(a, b), c);

    public static ShaderType MaxChannel(ShaderType a, ShaderType b, ShaderType c, ShaderType d)
        => MaxChannel(MaxChannel(a, b), MaxChannel(c, d));

    private static ShaderType Widen(ShaderType wider, int wc, int nc)
    {
        // Promote scalar-like to Float so we don't get vec3(int(...)) chains.
        if (wc == 1 && wider != ShaderType.Float) return ShaderType.Float;
        return wider;
    }

    /// <summary>Emit <c>func(in)</c>, evaluating the input at its source type.</summary>
    public static string UnaryFunc(Node node, string func, string portName, ShaderGenContext ctx)
    {
        var input = node.GetInput(portName)!;
        var t = ctx.GetSourceType(input);
        return $"{func}({ctx.EvaluateInputAs(input, t)})";
    }

    /// <summary>Emit <c>(prefix in suffix)</c> — used for unary expressions that
    /// aren't simple func calls (negation, 1-x, etc).</summary>
    public static string UnaryExpr(Node node, string prefix, string suffix, string portName, ShaderGenContext ctx)
    {
        var input = node.GetInput(portName)!;
        var t = ctx.GetSourceType(input);
        return $"({prefix}{ctx.EvaluateInputAs(input, t)}{suffix})";
    }

    /// <summary>Emit <c>(a op b)</c> at the unifying type. Used for + - * /.</summary>
    public static string BinaryOp(Node node, string op, ShaderGenContext ctx, string aPort = "A", string bPort = "B")
    {
        var t = MaxChannel(
            ctx.GetSourceType(node.GetInput(aPort)!),
            ctx.GetSourceType(node.GetInput(bPort)!));
        var a = ctx.EvaluateInputAs(node.GetInput(aPort)!, t);
        var b = ctx.EvaluateInputAs(node.GetInput(bPort)!, t);
        return $"({a} {op} {b})";
    }

    /// <summary>Emit <c>func(a, b)</c> at the unifying type. Used for min/max/pow/mod.</summary>
    public static string BinaryFunc(Node node, string func, ShaderGenContext ctx, string aPort = "A", string bPort = "B")
    {
        var t = MaxChannel(
            ctx.GetSourceType(node.GetInput(aPort)!),
            ctx.GetSourceType(node.GetInput(bPort)!));
        var a = ctx.EvaluateInputAs(node.GetInput(aPort)!, t);
        var b = ctx.EvaluateInputAs(node.GetInput(bPort)!, t);
        return $"{func}({a}, {b})";
    }

    /// <summary>Emit <c>func(a, b, c)</c> at the unifying max-channel type.</summary>
    public static string TernaryFunc(Node node, string func, string aPort, string bPort, string cPort, ShaderGenContext ctx)
    {
        var t = MaxChannel(
            ctx.GetSourceType(node.GetInput(aPort)!),
            ctx.GetSourceType(node.GetInput(bPort)!),
            ctx.GetSourceType(node.GetInput(cPort)!));
        var a = ctx.EvaluateInputAs(node.GetInput(aPort)!, t);
        var b = ctx.EvaluateInputAs(node.GetInput(bPort)!, t);
        var c = ctx.EvaluateInputAs(node.GetInput(cPort)!, t);
        return $"{func}({a}, {b}, {c})";
    }

    // ─── Type-helpers used by GetOutputType bodies — keeps the calling pattern compact ─

    /// <summary>Output type for unary nodes: matches the input's source type.</summary>
    public static ShaderType TypeFromInput(Node node, string portName, ShaderGenContext ctx)
        => ctx.GetSourceType(node.GetInput(portName)!);

    /// <summary>Output type for binary dynamic nodes: max-channel of two inputs.</summary>
    public static ShaderType TypeFromInputs(Node node, string a, string b, ShaderGenContext ctx)
        => MaxChannel(
            ctx.GetSourceType(node.GetInput(a)!),
            ctx.GetSourceType(node.GetInput(b)!));

    /// <summary>Output type for ternary dynamic nodes: max-channel of three inputs.</summary>
    public static ShaderType TypeFromInputs(Node node, string a, string b, string c, ShaderGenContext ctx)
        => MaxChannel(
            ctx.GetSourceType(node.GetInput(a)!),
            ctx.GetSourceType(node.GetInput(b)!),
            ctx.GetSourceType(node.GetInput(c)!));

    /// <summary>
    /// Ensure the fragment stage has a <c>_sgTBN</c> local — the tangent→world basis built
    /// from the three vertex varyings. Returns the local name so a caller can use it for
    /// <c>tangent →world</c> normal rotation, parallax view-dir transform, etc. Safe to call
    /// many times per compile — emits the definition only once via <see cref="ShaderGenContext.EmitOnce"/>.
    /// </summary>
    public static string EmitTBN(ShaderGenContext ctx)
    {
        const string name = "_sgTBN";
        ctx.Varyings.Add(("vNormal",    "vec3"));
        ctx.Varyings.Add(("vTangent",   "vec3"));
        ctx.Varyings.Add(("vBitangent", "vec3"));
        ctx.EmitOnce("sg_tbn", () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    mat3 {name} = mat3(normalize(vTangent), normalize(vBitangent), normalize(vNormal));");
        });
        return name;
    }

    /// <summary>
    /// Same as <see cref="EmitTBN"/> but returns the transpose — used to convert a
    /// world-space vector INTO tangent space (e.g. world view-dir → tangent-space for
    /// parallax occlusion mapping).
    /// </summary>
    public static string EmitTBNTranspose(ShaderGenContext ctx)
    {
        const string name = "_sgTBNT";
        var tbn = EmitTBN(ctx);
        ctx.EmitOnce("sg_tbn_t", () =>
        {
            ctx.BodyPrelude.AppendLine($"    mat3 {name} = transpose({tbn});");
        });
        return name;
    }
}
