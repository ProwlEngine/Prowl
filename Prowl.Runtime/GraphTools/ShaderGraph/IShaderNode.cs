// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GraphTools.ShaderGraphs;

/// <summary>
/// Implemented by every Node that participates in shader-graph compilation. Holds the
/// GLSL emission logic directly on the node — no separate impl class — so a node is
/// self-contained: ports + emission live in the same file.
/// </summary>
/// <remarks>
/// <para>The compiler walks back from the master output node, calls
/// <see cref="Evaluate"/> on each upstream node, and stitches the returned GLSL
/// expressions into the generated <c>.shader</c> source. Nodes that need a temp
/// variable can append lines to <see cref="ShaderGenContext.BodyPrelude"/>; nodes
/// that need a uniform / include / varying push it onto the matching set on the
/// context. The same context is shared by every node in a single stage's compile.</para>
///
/// <para><see cref="GetOutputType"/> exists so dynamic-output nodes (Add / Multiply /
/// Lerp / If / etc.) can compute their type from the connected operands — typically by
/// asking <see cref="ShaderGenContext.GetSourceType"/> on each input and picking the
/// max-channel type. Static-output nodes ignore the ctx and return a literal type.</para>
/// </remarks>
public interface IShaderNode
{
    /// <summary>
    /// Emit the GLSL expression that produces <paramref name="outputPort"/>'s value.
    /// May append helper lines to <paramref name="ctx"/>'s prelude / uniforms /
    /// varyings as a side effect — those get folded into the final shader source.
    /// </summary>
    string Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx);

    /// <summary>
    /// Output GLSL type. Receives <paramref name="ctx"/> so dynamic-output nodes can
    /// inspect their incoming wire types via <see cref="ShaderGenContext.GetSourceType"/>.
    /// Static-output nodes ignore the ctx.
    /// </summary>
    ShaderType GetOutputType(Port outputPort, ShaderGenContext ctx);
}
