// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.Runtime.GraphTools.ShaderGraphs;

/// <summary>Flag a graph that's missing — or has more than one of — the master output node,
/// or whose master doesn't match the graph's <see cref="ShaderGraph.ShaderTypeId"/>.</summary>
[GraphValidator(typeof(IShaderGraphNode))]
public sealed class MasterOutputValidator : GraphValidator
{
    public override void Validate(Graph graph)
    {
        int count = 0;
        Node? first = null;
        foreach (var n in graph.Nodes)
            if (n is MasterNodeBase) { count++; first ??= n; }

        if (count == 0)
        {
            if (graph.Nodes.Count > 0)
                graph.Nodes[0].Messages.Add(new NodeMessage {
                    Severity = NodeMessageSeverity.Error,
                    Text = "Shader graph has no output node — every shader graph needs a master.",
                });
            return;
        }

        if (count > 1)
        {
            foreach (var n in graph.Nodes)
                if (n is MasterNodeBase)
                    n.Messages.Add(new NodeMessage {
                        Severity = NodeMessageSeverity.Error,
                        Text = "Multiple output nodes — only one is allowed per graph.",
                    });
            return;
        }

        // Exactly one master. Confirm it's the one the graph's shader type expects —
        // otherwise the compiler won't find it and produces the stub shader.
        if (graph is ShaderGraph sg && first != null)
        {
            var type = ShaderTypeRegistry.TryResolve(sg.ShaderTypeId);
            if (type != null && !type.MasterNodeType.IsInstanceOfType(first))
            {
                first.Messages.Add(new NodeMessage {
                    Severity = NodeMessageSeverity.Error,
                    Text = $"Output node type '{first.GetType().Name}' doesn't match the graph's shader type '{sg.ShaderTypeId}' (expects '{type.MasterNodeType.Name}').",
                });
            }
        }
    }
}

/// <summary>Flag two property nodes that share the same emitted name. They'd produce
/// duplicate uniform declarations and the second would silently override the first.</summary>
[GraphValidator(typeof(IShaderGraphNode))]
public sealed class DuplicatePropertyNameValidator : GraphValidator
{
    public override void Validate(Graph graph)
    {
        var byName = new Dictionary<string, List<Node>>();
        foreach (var n in graph.Nodes)
        {
            if (n is not Nodes.IShaderProperty p) continue;
            if (!byName.TryGetValue(p.PropertyName, out var list))
                byName[p.PropertyName] = list = new List<Node>();
            list.Add(n);
        }

        foreach (var (name, list) in byName)
        {
            if (list.Count <= 1) continue;
            foreach (var n in list)
                n.Messages.Add(new NodeMessage {
                    Severity = NodeMessageSeverity.Error,
                    Text = $"Property name '{name}' is used by {list.Count} property nodes — names must be unique.",
                });
        }
    }
}
