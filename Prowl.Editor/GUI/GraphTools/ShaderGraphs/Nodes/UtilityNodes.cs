// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;
using System.Text.RegularExpressions;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

internal static class UtilityAccents
{
    /// <summary>Neutral gray distinguishes utility nodes from math (amber) and
    /// geometry (dark amber) categories at a glance.</summary>
    public static readonly System.Drawing.Color Util = System.Drawing.Color.FromArgb(255, 170, 170, 170);
}

// =============================================================================
// CustomCodeNode
//
// Emits a real GLSL function at file scope via `ctx.TopLevelHelpers`, then calls
// it from the expression. The user writes a function body that references four
// typed parameters (In0..In3) and returns the declared output type.
// =============================================================================

/// <summary>
/// Inline GLSL code block. The user supplies a function body that references its
/// inputs as <c>In0</c>..<c>In3</c> and returns a value of <see cref="OutputType"/>.
/// </summary>
/// <remarks>
/// <para>Each input is evaluated at its source type (so wiring a Vec3 into In2
/// makes the parameter a <c>vec3</c>) and the function is emitted once per node
/// into <see cref="ShaderGenContext.TopLevelHelpers"/> the compiler places that
/// block between the uniform declarations and <c>void main()</c>.</para>
///
/// <para>The function body is exactly what the user types. Use <c>return X;</c>
/// to produce the output this is a real GLSL function, not an inlined block.</para>
/// </remarks>
public sealed class CustomCodeNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>GLSL function body. Reference inputs as <c>In0</c>..<c>In3</c> and
    /// return a value of <see cref="OutputType"/>.</summary>
    /// <example>return vec3(In0, In1, In2) * In3;</example>
    public string Code = "return vec4(0.0);";

    /// <summary>Return type of the emitted function.</summary>
    public ShaderType OutputType = ShaderType.Vec4;

    public override string Title => "Custom Code";
    public override string Category => "Utility";
    public override System.Drawing.Color AccentColor => UtilityAccents.Util;

    protected override void DefineNode()
    {
        AddInput<float>("In0", 0f, tooltip: "Accessible as In0 in the body. Parameter type matches the connected wire's type.");
        AddInput<float>("In1", 0f, tooltip: "Accessible as In1.");
        AddInput<float>("In2", 0f, tooltip: "Accessible as In2.");
        AddInput<float>("In3", 0f, tooltip: "Accessible as In3.");
        AddOutput<float>("Out", tooltip: "Return value of the GLSL function.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        var fnName = $"_ccfn_{Id:N}";

        // Resolve each input's source type so the function signature matches the
        // actual wire types. Unwired inputs default to float.
        ShaderType T(string name) => ctx.IsConnected(GetInput(name)!)
            ? ctx.GetSourceType(GetInput(name)!) : ShaderType.Float;
        var t0 = T("In0"); var t1 = T("In1"); var t2 = T("In2"); var t3 = T("In3");

        ValidateBody(Code, ctx);

        // Emit the function body exactly once per compile.
        ctx.EmitOnce("cc:" + fnName, () =>
        {
            var sb = new StringBuilder();
            sb.Append("        ").Append(ShaderTypeUtil.ToGlsl(OutputType)).Append(' ').Append(fnName)
              .Append('(')
              .Append(ShaderTypeUtil.ToGlsl(t0)).Append(" In0, ")
              .Append(ShaderTypeUtil.ToGlsl(t1)).Append(" In1, ")
              .Append(ShaderTypeUtil.ToGlsl(t2)).Append(" In2, ")
              .Append(ShaderTypeUtil.ToGlsl(t3)).Append(" In3)")
              .AppendLine();
            sb.AppendLine("        {");
            // User body split on any line-ending to normalise, indent two levels.
            foreach (var line in (Code ?? "").Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None))
                sb.Append("            ").AppendLine(line);
            sb.AppendLine("        }");
            ctx.TopLevelHelpers.Append(sb);
        });

        // Emit the call site. Evaluate each input at its own source type no
        // silent promotion; the user's function signature is the authoritative
        // type contract.
        var a0 = ctx.EvaluateInputAs(GetInput("In0")!, t0);
        var a1 = ctx.EvaluateInputAs(GetInput("In1")!, t1);
        var a2 = ctx.EvaluateInputAs(GetInput("In2")!, t2);
        var a3 = ctx.EvaluateInputAs(GetInput("In3")!, t3);
        return $"{fnName}({a0}, {a1}, {a2}, {a3})";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => OutputType;

    /// <summary>
    /// Cheap pre-flight check on the user's GLSL body before it gets handed to the
    /// driver. Catches the obvious mistakes (no return, mismatched braces, empty
    /// body) and surfaces them as node diagnostics so the message lands ON the
    /// CustomCode node instead of as an opaque shader-compile error elsewhere.
    /// Real GLSL typing / syntax is still the driver's job this only filters out
    /// the most common authoring mistakes.
    /// </summary>
    private void ValidateBody(string body, ShaderGenContext ctx)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            ctx.Diagnostics.Add((Id, "Custom Code body is empty must contain at least a 'return' statement.", NodeMessageSeverity.Error));
            return;
        }

        // Strip line + block comments first so braces/return inside comments don't
        // count toward the check.
        var stripped = StripComments(body);

        if (!System.Text.RegularExpressions.Regex.IsMatch(stripped, @"\breturn\b"))
            ctx.Diagnostics.Add((Id, "Custom Code body must contain a 'return' statement that produces the function's output.", NodeMessageSeverity.Error));

        int depth = 0;
        bool inString = false; // GLSL has no strings, but be defensive against pasted shader-graph snippets.
        foreach (var ch in stripped)
        {
            if (ch == '"') inString = !inString;
            if (inString) continue;
            if (ch == '{') depth++;
            else if (ch == '}') { depth--; if (depth < 0) break; }
        }
        if (depth != 0)
            ctx.Diagnostics.Add((Id, $"Custom Code body has mismatched braces (delta={depth}). Each '{{' must be paired with '}}'.", NodeMessageSeverity.Error));
    }

    private static string StripComments(string src)
    {
        // Remove /* ... */ blocks, then // ... line comments. Order matters block
        // comments may contain // sequences, so block comments come first.
        src = System.Text.RegularExpressions.Regex.Replace(src, @"/\*[\s\S]*?\*/", " ");
        src = System.Text.RegularExpressions.Regex.Replace(src, @"//[^\r\n]*", " ");
        return src;
    }
}

// =============================================================================
// SetVarNode
//
// Declares a named local in BodyPrelude so downstream GetVarNodes can read it.
// The input passes straight through on the Out port. Must be topologically
// upstream of every matching Get if a Get is reached before its Set, the
// GLSL identifier is undeclared and the shader fails to compile.
// =============================================================================

public sealed class SetVarNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>Logical variable name. Sanitised before GLSL emission only
    /// alphanumeric and underscore characters are preserved.</summary>
    public string VarName = "myVar";

    public override string Title => $"Set - {VarName}";
    public override string Category => "Utility";
    public override System.Drawing.Color AccentColor => UtilityAccents.Util;

    protected override void DefineNode()
    {
        AddInput<float>("In", required: true, tooltip: "Value to store.");
        AddOutput<float>("Out", tooltip: "Passthrough same value as In.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        var glslName = NormalizeVar(VarName);
        ctx.EmitOnce("var:" + glslName, () =>
        {
            var inputType = ctx.IsConnected(GetInput("In")!)
                ? ctx.GetSourceType(GetInput("In")!) : ShaderType.Float;
            var inExpr = ctx.EvaluateInputAs(GetInput("In")!, inputType);
            ctx.BodyPrelude.AppendLine($"    {ShaderTypeUtil.ToGlsl(inputType)} {glslName} = {inExpr};");
        });
        return glslName;
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => ctx.IsConnected(GetInput("In")!)
            ? ctx.GetSourceType(GetInput("In")!)
            : ShaderType.Float;

    /// <summary>Produce the GLSL identifier for <paramref name="name"/>. Strips any
    /// character that is not a letter, digit, or underscore, then prefixes with
    /// <c>_var_</c> to prevent collisions with GLSL keywords and built-ins.</summary>
    internal static string NormalizeVar(string name)
        => "_var_" + Regex.Replace(name ?? "unnamed", @"[^A-Za-z0-9_]", "_");
}

// =============================================================================
// GetVarNode
// =============================================================================

public sealed class GetVarNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>Must match the VarName on the corresponding SetVarNode exactly
    /// (both apply the same NormalizeVar transform).</summary>
    public string VarName = "myVar";

    /// <summary>GLSL type this Get expects the variable to carry. Must match the
    /// type that flowed into the paired SetVarNode's In port.</summary>
    public ShaderType Type = ShaderType.Float;

    public override string Title => $"Get - {VarName}";
    public override string Category => "Utility";
    public override System.Drawing.Color AccentColor => UtilityAccents.Util;

    protected override void DefineNode()
    {
        AddOutput<float>("Out", tooltip: "Value of the named variable declared by SetVarNode.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
        => SetVarNode.NormalizeVar(VarName);

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => Type;
}
