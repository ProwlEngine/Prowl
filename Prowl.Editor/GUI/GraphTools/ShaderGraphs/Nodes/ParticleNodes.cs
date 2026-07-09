// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Particle-specific nodes. Gated with [ShaderType("Particle")] so they only show
// in the node browser when the current graph is a Particle shader.
// Each reads data forwarded by ParticlePass's vertex stage via pre-declared varyings.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

internal static class ParticleAccents
{
    /// <summary>Warm orange groups particle nodes in the browser.</summary>
    public static readonly System.Drawing.Color Particle =
        System.Drawing.Color.FromArgb(255, 220, 150, 90);
}

/// <summary>
/// Normalised particle age in [0, 1]. Forwarded from <c>instanceCustomData.x</c>
/// by the Particle vertex stage into the <c>vLifetime</c> varying.
/// </summary>
[ShaderType("Particle")]
public sealed class ParticleLifetimeNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Particle Lifetime";
    public override string Category => "Particle";
    public override System.Drawing.Color AccentColor => ParticleAccents.Particle;

    protected override void DefineNode()
    {
        AddOutput<float>("Age", tooltip: "Normalised lifetime [0,1] 0 at spawn, 1 at despawn.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "0.0";
        ctx.Varyings.Add(("vLifetime", "float"));
        return "vLifetime";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>
/// Returns the particle's animated UV <c>vertexTexCoord0 * customData.w + customData.yz</c>.
/// The ParticleSystem component packs flipbook offset/scale into the instance
/// custom data so a single shader handles any sprite-sheet layout.
/// </summary>
/// <remarks>
/// The ParticlePass vertex stage already applies this transform and writes the
/// result into <c>texCoord0</c>, so most users can just grab the standard UV
/// node. This exists for cases where the user wants the raw animated UV before
/// any additional offset / tiling.
/// </remarks>
[ShaderType("Particle")]
public sealed class ParticleAnimatedUVNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Particle UV";
    public override string Category => "Particle";
    public override System.Drawing.Color AccentColor => ParticleAccents.Particle;

    protected override void DefineNode()
    {
        AddOutput<Float2>("UV");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "vec2(0.0)";
        // The vertex stage already bakes this into texCoord0 just pass that through.
        ctx.Varyings.Add(("texCoord0", "vec2"));
        return "texCoord0";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Vec2;
}
