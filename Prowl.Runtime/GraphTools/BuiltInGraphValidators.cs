// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Universal "sanity" validator that runs on every graph type. Checks for cycles
/// (which most evaluators can't handle) and flags the nodes participating in each
/// cycle with an error message. Cheap — a standard DFS over the graph.
/// </summary>
[GraphValidator]
public sealed class CycleDetectorValidator : GraphValidator
{
    public override void Validate(Graph graph)
    {
        // Adjacency: node → list of nodes it flows INTO (output-side of each edge).
        var adj = new Dictionary<System.Guid, List<System.Guid>>();
        foreach (var n in graph.Nodes) adj[n.Id] = new List<System.Guid>();
        foreach (var e in graph.Edges)
            if (adj.TryGetValue(e.SourceNodeId, out var outs)) outs.Add(e.TargetNodeId);

        // Standard "white/grey/black" DFS for back-edge detection.
        var color = new Dictionary<System.Guid, byte>(); // 0=white, 1=grey, 2=black
        foreach (var id in adj.Keys) color[id] = 0;
        var cycleNodes = new HashSet<System.Guid>();
        var stack = new Stack<System.Guid>();

        foreach (var start in adj.Keys)
        {
            if (color[start] != 0) continue;
            DfsIterative(start, adj, color, stack, cycleNodes);
        }

        if (cycleNodes.Count == 0) return;

        foreach (var n in graph.Nodes)
            if (cycleNodes.Contains(n.Id))
                n.Messages.Add(new NodeMessage { Severity = NodeMessageSeverity.Error, Text = "Node is part of a cycle — evaluation would loop." });
    }

    // ─── required port ──────────────────────────────────────────────────────────────

    private static void DfsIterative(System.Guid start,
        Dictionary<System.Guid, List<System.Guid>> adj,
        Dictionary<System.Guid, byte> color,
        Stack<System.Guid> stack, HashSet<System.Guid> cycleNodes)
    {
        // Iterative DFS — avoids stack overflow on wide graphs. Each frame = (node, next-outgoing-index).
        var frames = new Stack<(System.Guid node, int i)>();
        frames.Push((start, 0));
        color[start] = 1;
        stack.Push(start);

        while (frames.Count > 0)
        {
            var (u, i) = frames.Pop();
            var outs = adj[u];
            bool descended = false;
            for (int j = i; j < outs.Count; j++)
            {
                var v = outs[j];
                if (!color.ContainsKey(v)) continue;
                if (color[v] == 1)
                {
                    // Back-edge → cycle. Mark every node on the current DFS stack from v
                    // down to u; they're all in the same SCC as far as this check cares.
                    foreach (var n in stack)
                    {
                        cycleNodes.Add(n);
                        if (n == v) break;
                    }
                }
                else if (color[v] == 0)
                {
                    // Continue this frame later at j+1.
                    frames.Push((u, j + 1));
                    color[v] = 1;
                    stack.Push(v);
                    frames.Push((v, 0));
                    descended = true;
                    break;
                }
            }
            if (!descended)
            {
                color[u] = 2;
                if (stack.Count > 0) stack.Pop();
            }
        }
    }
}

/// <summary>
/// Flag any input port marked <see cref="Port.IsRequired"/> that has no incoming wire.
/// Ports whose default value isn't a meaningful fallback (Custom Code operands, sampler
/// bindings, etc.) opt in by setting <c>required: true</c> on their AddInput call.
/// Hidden ports are exempt — the owning node has chosen to make them unreachable.
/// </summary>
[GraphValidator]
public sealed class RequiredPortValidator : GraphValidator
{
    public override void Validate(Graph graph)
    {
        // Build a set of (nodeId, portName) that have at least one incoming edge — one
        // scan of Edges is cheaper than nested for-each per port.
        var connected = new HashSet<(System.Guid, string)>();
        foreach (var e in graph.Edges)
            connected.Add((e.TargetNodeId, e.TargetPortName));

        foreach (var n in graph.Nodes)
        {
            foreach (var p in n.Inputs)
            {
                if (!p.IsRequired || p.IsHidden) continue;
                if (connected.Contains((n.Id, p.Name))) continue;
                n.Messages.Add(new NodeMessage {
                    Severity = NodeMessageSeverity.Error,
                    Text = $"Input '{p.Name}' is required.",
                });
            }
        }
    }
}
