// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;
using System.Text;

using Prowl.Editor.GraphTools.ShaderGraphs.Types;
using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;

namespace Prowl.Editor.GraphTools.ShaderGraphs;

/// <summary>
/// Thin dispatch layer. Resolves the graph's <see cref="IShaderType"/> from the
/// registry, locates the master node, collects properties, and then just loops over
/// <see cref="IShaderType.Passes"/> calling each pass's <see cref="IShaderPass.EmitPass"/>.
/// Every shader-type-specific detail (vertex logic, lighting math, pass structure)
/// lives on the shader type itself — not in here.
/// </summary>
public static class ShaderGraphCompiler
{
    public sealed class Result
    {
        /// <summary>Generated GLSL <c>.shader</c> source. Always non-null — falls
        /// back to a magenta stub shader on failure so materials stay renderable.</summary>
        public string ShaderSource = "";

        /// <summary>Errors / warnings surfaced as node messages by the editor.</summary>
        public List<(System.Guid? nodeId, string message, NodeMessageSeverity severity)> Diagnostics = new();

        public bool HasErrors => Diagnostics.Any(d => d.severity == NodeMessageSeverity.Error);
    }

    public static Result Compile(Graph graph, string shaderName)
    {
        var result = new Result();

        // Compiler only knows how to handle ShaderGraph — other graph kinds have
        // no shader type / render settings / properties, so bail cleanly with a
        // stub shader instead of null-refing deeper.
        if (graph is not ShaderGraph sg)
        {
            result.Diagnostics.Add((null, $"ShaderGraphCompiler invoked on a non-ShaderGraph ('{graph.GetType().Name}'). Only ShaderGraph is supported.", NodeMessageSeverity.Error));
            result.ShaderSource = ShaderGraphEmit.StubShader(shaderName, "Non-ShaderGraph passed to compiler.");
            return result;
        }

        // Resolve the shader type. Unknown id = usually a plugin was removed.
        var typeId = sg.ShaderTypeId;
        var type = ShaderTypeRegistry.TryResolve(typeId);
        if (type == null)
        {
            result.Diagnostics.Add((null, $"Unknown shader type '{typeId}'. Known ids: [{string.Join(", ", ShaderTypeRegistry.All.Select(t => t.Id))}]", NodeMessageSeverity.Error));
            result.ShaderSource = ShaderGraphEmit.StubShader(shaderName, $"Unknown shader type '{typeId}'.");
            return result;
        }

        // Locate the master node — the graph is invalid without exactly one of the
        // right concrete type.
        MasterNodeBase? master = null;
        int masterCount = 0;
        foreach (var n in graph.Nodes)
        {
            if (n is MasterNodeBase m && type.MasterNodeType.IsInstanceOfType(m))
            {
                master ??= m;
                masterCount++;
            }
        }
        if (master == null)
        {
            result.Diagnostics.Add((null, $"Graph has no {type.MasterNodeType.Name} — nothing to compile.", NodeMessageSeverity.Error));
            result.ShaderSource = ShaderGraphEmit.StubShader(shaderName, $"Graph missing {type.MasterNodeType.Name}.");
            return result;
        }
        if (masterCount > 1)
        {
            result.Diagnostics.Add((master.Id,
                $"Graph has {masterCount} {type.MasterNodeType.Name}s — only the first is used; delete the extras.",
                NodeMessageSeverity.Error));
        }

        // Sanity-check every node against the graph's shader type. Nodes declaring a
        // [ShaderType("X")] that doesn't match the current graph still emit via their
        // Evaluate(), but we warn so authors see the mismatch in the inspector.
        foreach (var n in graph.Nodes)
        {
            if (n is MasterNodeBase) continue;  // masters are type-checked above
            if (!ShaderTypeRegistry.IsNodeApplicable(n.GetType(), type.Id))
                result.Diagnostics.Add((n.Id,
                    $"Node '{n.GetType().Name}' isn't applicable to shader type '{type.Id}' — its output may be zero-filled.",
                    NodeMessageSeverity.Warning));
        }

        // Properties block — shared across every pass.
        var propertyBlock = new List<string>();
        var propertyUniforms = new List<string>();
        ShaderGraphEmit.CollectProperties(graph, propertyBlock, propertyUniforms);

        // Shared state handed to each pass.
        var shared = new PassEmitSharedState
        {
            PropertyUniforms = propertyUniforms,
            Diagnostics = result.Diagnostics,
        };

        // Assemble the final shader.
        var sb = new StringBuilder();
        sb.AppendLine($"Shader \"Generated/{shaderName}\"");
        sb.AppendLine();
        sb.AppendLine("Properties");
        sb.AppendLine("{");
        foreach (var p in propertyBlock) sb.AppendLine(p);
        sb.AppendLine("}");
        sb.AppendLine();

        // Emit each pass. Passes can skip themselves by returning an empty string
        // (e.g. Surface's DepthNormals pass skips for transparent graphs).
        foreach (var pass in type.Passes)
        {
            var passText = pass.EmitPass(master, sg, shared);
            if (!string.IsNullOrEmpty(passText))
            {
                sb.Append(passText);
                if (!passText.EndsWith("\n")) sb.AppendLine();
                sb.AppendLine();
            }
        }

        result.ShaderSource = sb.ToString();
        return result;
    }
}
