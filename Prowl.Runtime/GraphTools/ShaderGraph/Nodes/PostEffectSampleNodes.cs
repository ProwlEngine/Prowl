// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Nodes specific to the PostEffect shader type — sampling the scene-colour buffer
// being processed, and getting the fullscreen-quad UV. Gated via [ShaderType]
// so they don't clutter Surface / Grass / Terrain node browsers.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>
/// Samples the scene-colour render target being processed by this post-effect.
/// Under the hood this is <c>texture(_MainTex, TexCoords)</c> — the render pipeline
/// binds the scene's current colour buffer as <c>_MainTex</c> before invoking
/// the effect.
/// </summary>
[ShaderType("PostEffect")]
public sealed class PostEffectSceneColorNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Scene Color";
    public override string Category => "Post Effect";
    public override System.Drawing.Color AccentColor => PostEffectAccents.PostEffect;

    protected override void DefineNode()
    {
        AddInput<Float2>("UV", Float2.Zero,
            tooltip: "Where to sample the scene colour. Defaults to this pixel's screen UV.");
        AddOutput<Float4>("Color");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "vec4(0.0)";

        ctx.Uniforms.Add("uniform sampler2D _MainTex;");

        var uvPort = GetInput("UV")!;
        string uv = ctx.IsConnected(uvPort)
            ? ctx.EvaluateInputAs(uvPort, ShaderType.Vec2)
            : "TexCoords";
        return $"texture(_MainTex, {uv})";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Vec4;
}

/// <summary>
/// Returns the fullscreen-quad UV for the current pixel — the built-in
/// <c>TexCoords</c> varying provided by the PostEffect vertex stage. Use when you
/// want to offset / distort / warp the sampling coordinate before feeding it back
/// into <see cref="PostEffectSceneColorNode"/>.
/// </summary>
[ShaderType("PostEffect")]
public sealed class PostEffectScreenUVNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Screen UV";
    public override string Category => "Post Effect";
    public override System.Drawing.Color AccentColor => PostEffectAccents.PostEffect;

    protected override void DefineNode()
    {
        AddOutput<Float2>("UV");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "vec2(0.0)";
        return "TexCoords";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Vec2;
}
