// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// PostEffect shader type single fullscreen pass. Vertex stage is a classic
// fullscreen quad (geometry already in clip space); fragment runs the user's
// Color subtree and writes to fragColor.

using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Editor.GraphTools.ShaderGraphs.Editors;
using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools.ShaderGraphs.Editors.Types;

public sealed class PostEffectShaderType : IShaderType
{
    public const string TypeId = "PostEffect";

    public string Id => TypeId;
    public string DisplayName => "Post Effect";
    public Type MasterNodeType => typeof(PostEffectMasterNode);

    private static readonly IShaderPass[] s_passes = { new PostEffectPass() };
    public IReadOnlyList<IShaderPass> Passes => s_passes;

    public ShaderGraphRenderSettings DefaultRenderSettings => ShaderGraphRenderSettings.PostEffectDefaults();

    public IReadOnlyList<ShaderTypeMenuEntry> MenuEntries { get; } = new[]
    {
        new ShaderTypeMenuEntry("Default", "Shader Graph/Post Effect", 60),
    };

    public void SeedGraph(ShaderGraph graph, string variantKey)
    {
        graph.ShaderTypeId = TypeId;
        graph.RenderSettings = DefaultRenderSettings;

        // Seed a working passthrough: SceneColor → master.Color.
        // User replaces the intermediate path with their effect math.
        var master = new PostEffectMasterNode { Position = new Float2(500, 120) };
        graph.AddNode(master);

        var sceneColor = new PostEffectSceneColorNode { Position = new Float2(180, 120) };
        graph.AddNode(sceneColor);

        graph.Edges.Add(new Edge
        {
            SourceNodeId   = sceneColor.Id,
            SourcePortName = sceneColor.GetOutput("Color")!.Name,
            TargetNodeId   = master.Id,
            TargetPortName = master.GetInput("Color")!.Name,
        });
    }
}

/// <summary>Single fullscreen pass. Geometry is a quad already in clip space; the
/// vertex stage just forwards UV and writes <c>gl_Position</c> from <c>vertexPosition</c>
/// directly. Fragment evaluates the user's Color subtree.</summary>
internal sealed class PostEffectPass : IShaderPass
{
    public string Name => "PostEffect";
    public ShaderPassRole Role => ShaderPassRole.Fullscreen;

    public string EmitPass(MasterNodeBase masterBase, ShaderGraph graph, PassEmitSharedState shared)
    {
        var master = (PostEffectMasterNode)masterBase;
        var settings = graph.RenderSettings;

        // Fragment context: evaluate the user's Color.
        var fragCtx = new ShaderGenContext(graph, ShaderStage.Fragment);
        foreach (var u in shared.PropertyUniforms) fragCtx.Uniforms.Add(u);
        fragCtx.Includes.Add("Fragment");
        // TexCoords is always forwarded it's cheap and nodes like SceneColor / ScreenUV
        // expect it.
        fragCtx.Varyings.Add(("TexCoords", "vec2"));

        var colorPort = master.GetInput("Color");
        string colorExpr = colorPort != null
            ? fragCtx.EvaluateInput(colorPort)
            : "vec4(0.0, 0.0, 0.0, 1.0)";
        shared.Diagnostics.AddRange(fragCtx.Diagnostics);

        // Vertex context: just needs to output TexCoords. No includes the vertex
        // body is stand-alone (no TransformClip, no VertexAttributes).
        // We DO hand it the property uniforms in case a future user-authored
        // vertex-stage node (via CustomCode) wants them.

        var sb = new StringBuilder();
        sb.AppendLine($"Pass \"{Name}\"");
        sb.AppendLine("{");
        ShaderGraphEmit.AppendRenderState(sb, settings, "    ");
        foreach (var d in fragCtx.PassDirectives) sb.AppendLine($"    {d}");
        sb.AppendLine();
        sb.AppendLine("    GLSLPROGRAM");
        sb.AppendLine();

        // ─ Vertex (static, fullscreen quad pass-through) ─
        sb.AppendLine("    Vertex");
        sb.AppendLine("    {");
        sb.AppendLine("        layout (location = 0) in vec3 vertexPosition;");
        sb.AppendLine("        layout (location = 1) in vec2 vertexTexCoord;");
        sb.AppendLine("        out vec2 TexCoords;");
        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        sb.AppendLine("            TexCoords = vertexTexCoord;");
        sb.AppendLine("            gl_Position = vec4(vertexPosition, 1.0);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ─ Fragment ─
        sb.AppendLine("    Fragment");
        sb.AppendLine("    {");
        foreach (var d in fragCtx.Defines)    sb.AppendLine($"        #define {d}");
        foreach (var inc in fragCtx.Includes) sb.AppendLine($"        #include \"{inc}\"");
        sb.AppendLine();
        sb.AppendLine("        layout (location = 0) out vec4 fragColor;");
        foreach (var (n, t) in fragCtx.Varyings) sb.AppendLine($"        in {t} {n};");
        foreach (var u in fragCtx.Uniforms)   sb.AppendLine($"        {u}");
        if (fragCtx.TopLevelHelpers.Length > 0) { sb.AppendLine(); sb.Append(fragCtx.TopLevelHelpers.ToString()); }
        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        if (fragCtx.BodyPrelude.Length > 0) sb.Append(fragCtx.BodyPrelude.ToString());
        sb.AppendLine($"            fragColor = {colorExpr};");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
