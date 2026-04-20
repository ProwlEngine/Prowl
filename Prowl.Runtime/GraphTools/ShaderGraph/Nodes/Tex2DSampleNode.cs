// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>
/// Sample a 2D texture. Inputs: a sampler (from a Texture2DPropertyNode) and a UV
/// coord (defaults to vertex UV0 when unconnected). Outputs the sampled vec4 plus
/// each channel split out for convenience (matches ShaderForge's Tex2D node).
/// </summary>
public sealed class Tex2DSampleNode : Node, IShaderGraphNode
{
    public override string Title => "Sample Texture 2D";
    public override string Category => "Texture";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 100, 160, 100);

    protected override void DefineNode()
    {
        AddInput<Resources.Texture2D>("Sampler");
        AddInput<Float2>("UV", Float2.Zero);
        AddOutput<Color>("RGBA");
        AddOutput<float>("R");
        AddOutput<float>("G");
        AddOutput<float>("B");
        AddOutput<float>("A");
    }
}
