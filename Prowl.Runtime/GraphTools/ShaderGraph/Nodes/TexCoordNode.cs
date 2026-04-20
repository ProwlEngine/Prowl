// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>The vertex's UV coordinate, available in fragment stage. Channel index
/// selects between texCoord0 / texCoord1 — most meshes only have channel 0.</summary>
public sealed class TexCoordNode : Node, IShaderGraphNode
{
    public int Channel = 0;

    public override string Title => $"UV{Channel}";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 140, 110, 60);

    protected override void DefineNode() => AddOutput<Float2>("UV");
}
