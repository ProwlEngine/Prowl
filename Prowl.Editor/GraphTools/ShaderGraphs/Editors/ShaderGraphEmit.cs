// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Shared emission helpers used by IShaderPass implementations across every shader
// type (Surface / PostEffect / Particle / Grass / Terrain / ...). All the knowledge
// about how Prowl's .shader source is spelled lives here so new shader types only
// need to describe *what* they emit, not how to stringify each keyword.

using System.Collections.Generic;
using System.Text;

using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

namespace Prowl.Editor.GraphTools.ShaderGraphs.Editors;

public static class ShaderGraphEmit
{
    // ─── Keyword tables ─────────────────────────────────────────────────────────

    public static string QueueTag(ShaderRenderQueue q) => q switch
    {
        ShaderRenderQueue.Background  => "Background",
        ShaderRenderQueue.AlphaTest   => "AlphaTest",
        ShaderRenderQueue.Transparent => "Transparent",
        ShaderRenderQueue.Overlay     => "Overlay",
        _                              => "Opaque",
    };

    public static string CullKeyword(ShaderCullMode c) => c switch
    {
        ShaderCullMode.Front        => "Front",
        ShaderCullMode.Off          => "Off",
        ShaderCullMode.FrontAndBack => "FrontAndBack",
        _                            => "Back",
    };

    /// <summary>Parser uses shortened names for depth test. Keep our enum
    /// human-readable but emit the short form.</summary>
    public static string ZTestKeyword(ShaderZTest z) => z switch
    {
        ShaderZTest.Off          => "Off",
        ShaderZTest.Never        => "Never",
        ShaderZTest.Less         => "Less",
        ShaderZTest.Equal        => "Equal",
        ShaderZTest.LessEqual    => "LEqual",
        ShaderZTest.Greater      => "Greater",
        ShaderZTest.NotEqual     => "NotEqual",
        ShaderZTest.GreaterEqual => "GEqual",
        ShaderZTest.Always       => "Always",
        _                         => "LEqual",
    };

    public static string BlendFactor(ShaderBlendFactor f) => f.ToString();
    public static string BlendOp(ShaderBlendOp op) => op.ToString();

    // ─── Render state block ─────────────────────────────────────────────────────

    /// <summary>Emit the top portion of a Pass block Tags, Cull, ZWrite, ZTest,
    /// Winding (only when CCW), and the Blend line. Does NOT emit the Pass header
    /// or the opening brace; call sites do that so they can add extra lines like
    /// <c>GrabTexture</c> directives.</summary>
    public static void AppendRenderState(StringBuilder sb, ShaderGraphRenderSettings s, string indent)
    {
        sb.AppendLine($"{indent}Tags {{ \"RenderOrder\" = \"{QueueTag(s.Queue)}\" }}");
        sb.AppendLine($"{indent}Cull {CullKeyword(s.Cull)}");
        sb.AppendLine($"{indent}ZWrite {(s.ZWrite ? "On" : "Off")}");
        sb.AppendLine($"{indent}ZTest {ZTestKeyword(s.ZTest)}");
        if (s.Winding == ShaderWinding.CCW)
            sb.AppendLine($"{indent}Winding CCW");

        AppendBlendLine(sb, s, indent);
    }

    /// <summary>
    /// Emit the Blend line in parser-correct syntax. Parser accepts four presets
    /// (Off/Additive/Alpha/Override) as a single identifier; anything else needs the
    /// block form with Src/Dst/Mode entries. Preset names are used when the settings
    /// match exactly so the generated shader stays readable.
    /// </summary>
    public static void AppendBlendLine(StringBuilder sb, ShaderGraphRenderSettings s, string indent)
    {
        if (s.Blend == ShaderBlendMode.Custom)
        {
            sb.AppendLine($"{indent}Blend {{ Src {BlendFactor(s.BlendSrc)}; Dst {BlendFactor(s.BlendDst)}; Mode {BlendOp(s.BlendOp)}; }}");
            return;
        }

        switch (s.Blend)
        {
            case ShaderBlendMode.Opaque:
                // No Blend line parser treats absence as DoBlend=false.
                return;
            case ShaderBlendMode.Alpha:
                sb.AppendLine($"{indent}Blend Alpha"); return;
            case ShaderBlendMode.Additive:
                sb.AppendLine($"{indent}Blend Additive"); return;
            case ShaderBlendMode.Override:
                sb.AppendLine($"{indent}Blend Override"); return;
            case ShaderBlendMode.Multiply:
                sb.AppendLine($"{indent}Blend {{ Src DstColor; Dst Zero; Mode Add; }}"); return;
            case ShaderBlendMode.Premultiplied:
                sb.AppendLine($"{indent}Blend {{ Src One; Dst OneMinusSrcAlpha; Mode Add; }}"); return;
        }
    }

    // ─── Property collection ────────────────────────────────────────────────────

    /// <summary>Walk every <see cref="IShaderProperty"/> node on the graph and emit
    /// the shader's Properties-block lines + the parallel uniform declarations. Each
    /// is de-duped by emitted name (first wins).</summary>
    public static void CollectProperties(Graph graph, List<string> propertyBlock, List<string> uniformDecls)
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var n in graph.Nodes)
        {
            if (n is not IShaderProperty p) continue;

            // Whatever the user typed, the emitted name has to be a legal GLSL
            // identifier or the parser will reject the whole Properties block.
            var name = SanitisePropertyIdentifier(p.PropertyName);
            if (!seen.Add(name)) continue;

            var keyword = ShaderTypeUtil.ToPropertyKeyword(p.PropertyType);
            if (keyword == null) continue;

            // Range(min, max) override the plain "Float" keyword when the node asks
            // for a ranged slider. Lowered back to Float at parse time.
            if (n is IShaderPropertyRange rp && p.PropertyType == ShaderType.Float)
                keyword = $"Range({ShaderGenContext.Fmt(rp.RangeMin)}, {ShaderGenContext.Fmt(rp.RangeMax)})";

            var def = p.DefaultLiteral;
            var display = EscapeShaderString(p.DisplayName ?? "");
            if (string.IsNullOrWhiteSpace(def))
                propertyBlock.Add($"    {name} (\"{display}\", {keyword})");
            else
                propertyBlock.Add($"    {name} (\"{display}\", {keyword}) = {def}");
            uniformDecls.Add($"uniform {ShaderTypeUtil.ToGlsl(p.PropertyType)} {name};");
        }
    }

    /// <summary>Coerce a raw name into a legal GLSL identifier.</summary>
    public static string SanitisePropertyIdentifier(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "_Property";
        var sb = new StringBuilder(raw.Length);
        char first = raw[0];
        sb.Append(char.IsLetter(first) || first == '_' ? first : '_');
        for (int i = 1; i < raw.Length; i++)
        {
            char c = raw[i];
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }
        return sb.ToString();
    }

    /// <summary>Escape chars that would break the display-name string literal.</summary>
    public static string EscapeShaderString(string raw)
        => raw.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ─── Fallback shader ────────────────────────────────────────────────────────

    /// <summary>Magenta "shader's broken" placeholder keeps materials renderable
    /// when the real compile fails, and doubles as a visible signal in-scene.</summary>
    public static string StubShader(string shaderName, string reason)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Shader \"Generated/{shaderName}\"");
        sb.AppendLine("// " + reason);
        sb.AppendLine("Properties { }");
        sb.AppendLine("Pass \"Standard\"");
        sb.AppendLine("{");
        sb.AppendLine("    Tags { \"RenderOrder\" = \"Opaque\" }");
        sb.AppendLine("    GLSLPROGRAM");
        sb.AppendLine("    Vertex { #include \"Fragment\" #include \"VertexAttributes\" void main() { gl_Position = TransformClip(vertexPosition); } }");
        sb.AppendLine("    Fragment { layout(location = 0) out vec4 fragColor; void main() { fragColor = vec4(1.0, 0.0, 1.0, 1.0); } }");
        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
