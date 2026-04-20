// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// References another <see cref="Graph"/> asset and re-exposes its
/// <see cref="GraphInputNode"/>s as input ports + <see cref="GraphOutputNode"/>s as
/// output ports. Acts as a callable "function" in the host graph; double-clicking the
/// node opens the referenced asset in a new editor tab (handled by the editor).
/// </summary>
/// <remarks>
/// Port list is rebuilt from the referenced asset every time <see cref="DefineNode"/>
/// runs — so editing the inner graph's interface (renaming a GraphInput, adding a
/// GraphOutput) reflects in the outer graph after re-loading. Wires that reference a
/// no-longer-existing port name will be dropped by <see cref="Graph.OnAfterDeserialize"/>'s
/// dangling-edge cleanup.
/// </remarks>
[UniversalNode]
public sealed class SubgraphNode : Node
{
    /// <summary>The graph asset this node embeds. Editing this in the Inspector triggers
    /// a port rebuild on the next <see cref="Node.EnsureDefined"/> call.</summary>
    public AssetRef<Graph> Subgraph;

    public override string Title
    {
        get
        {
            var g = Subgraph.Res;
            return g != null ? $"{g.Name}" : "Subgraph (none)";
        }
    }
    public override string Category => "Subgraph";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 90, 130, 200);

    protected override void DefineNode()
    {
        var inner = Subgraph.Res;
        if (inner == null) return;

        // Walk the inner graph's nodes, find every interface node, and mirror it on
        // this side. Type is auto-inferred from the wire connected on the inside —
        // an inner GraphInputNode whose Out port feeds a Float input gives this
        // SubgraphNode a Float input; if nothing's connected yet, the port stays
        // typed as object so it accepts any wire (and the user can connect inside
        // first then refresh).
        foreach (var n in inner.Nodes)
        {
            if (n is not IGraphInterfaceNode iface) continue;
            var t = ResolveInterfaceType(inner, n, iface.IsInput);
            if (iface.IsInput) GraphInterfaceUtil.AddInput(this, t, iface.PortName);
            else                GraphInterfaceUtil.AddOutput(this, t, iface.PortName);
        }
    }

    /// <summary>For an interface node, look at the wires inside the subgraph that touch
    /// its single port and return the data type carried. Returns object when the
    /// interface port has no internal wires yet (so the outer SubgraphNode port still
    /// accepts anything).</summary>
    private static Type ResolveInterfaceType(Graph inner, Node ifaceNode, bool isInput)
    {
        // Input node has an OUTPUT "Out" port → look downstream.
        // Output node has an INPUT  "In"  port → look upstream.
        if (isInput)
        {
            foreach (var edge in inner.Edges)
            {
                if (edge.SourceNodeId != ifaceNode.Id) continue;
                var target = inner.FindNode(edge.TargetNodeId);
                var port = target?.GetInput(edge.TargetPortName);
                if (port != null && port.DataType != typeof(object))
                    return port.DataType;
            }
        }
        else
        {
            foreach (var edge in inner.Edges)
            {
                if (edge.TargetNodeId != ifaceNode.Id) continue;
                var source = inner.FindNode(edge.SourceNodeId);
                var port = source?.GetOutput(edge.SourcePortName);
                if (port != null && port.DataType != typeof(object))
                    return port.DataType;
            }
        }
        return typeof(object);
    }
}
