// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GraphTools.ShaderGraphs;

/// <summary>
/// A node-based shader source. Currently a thin marker subclass — Phase 5 will add
/// shader-specific node types (Sampler2D, Multiply, Lerp, ...) and a code generator
/// that compiles the graph to GLSL.
/// </summary>
[CreateAssetMenu("Shader Graph", Extension = ".shadergraph", Order = 5)]
public sealed class ShaderGraph : Graph
{
    public ShaderGraph() : base("New Shader Graph") { }

    public override System.Type NodeMarkerInterface => typeof(IShaderGraphNode);
}
