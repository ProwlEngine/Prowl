// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Pure graph-walk helpers: dependency depth, topological order, downstream reach.
/// Lives in Runtime because every graph type's evaluator needs the same primitives —
/// shader compiler, behaviour-tree runner, visual-script interpreter all start by
/// asking "what's the eval order, and how deep is each node from a leaf?".
/// </summary>
/// <remarks>
/// All routines treat <see cref="Edge.SourceNodeId"/> → <see cref="Edge.TargetNodeId"/>
/// as the dependency direction (output flows to input). Cycles are tolerated the
/// validator already flags them; here we just break the back-edge so the rest of the
/// graph still produces a usable order.
/// </remarks>
public static class GraphTraversal
{
    /// <summary>
    /// Return every node in dependency order: each node appears AFTER every node whose
    /// output flows into it. If the graph has a cycle, the back-edge is ignored and
    /// the rest of the order is still returned (use <see cref="CycleDetectorValidator"/>
    /// to flag cycles for the user).
    /// </summary>
    public static List<Node> TopologicalOrder(Graph graph)
    {
        // Kahn's algorithm. Indegree = count of incoming edges per node.
        var indegree = new Dictionary<Guid, int>();
        var outgoing = new Dictionary<Guid, List<Guid>>();
        var byId = new Dictionary<Guid, Node>();
        foreach (var n in graph.Nodes)
        {
            indegree[n.Id] = 0;
            outgoing[n.Id] = new List<Guid>();
            byId[n.Id] = n;
        }
        foreach (var e in graph.Edges)
        {
            if (!indegree.ContainsKey(e.TargetNodeId)) continue; // dangling edge ignore
            if (!outgoing.ContainsKey(e.SourceNodeId)) continue;
            indegree[e.TargetNodeId]++;
            outgoing[e.SourceNodeId].Add(e.TargetNodeId);
        }

        var ready = new Queue<Guid>();
        foreach (var kv in indegree) if (kv.Value == 0) ready.Enqueue(kv.Key);

        var result = new List<Node>(graph.Nodes.Count);
        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            result.Add(byId[id]);
            foreach (var t in outgoing[id])
            {
                indegree[t]--;
                if (indegree[t] == 0) ready.Enqueue(t);
            }
        }

        // Anything still > 0 is in a cycle append in stable iteration order so callers
        // get back every node, not silently a subset. Users see the cycle via the
        // validator's red badges; the evaluator can still try.
        if (result.Count < graph.Nodes.Count)
        {
            foreach (var n in graph.Nodes)
                if (indegree[n.Id] > 0) result.Add(n);
        }
        return result;
    }

    /// <summary>
    /// Compute the dependency depth of every node leaves (no inputs / no incoming
    /// edges) are depth 0; a node's depth is 1 + max(depth of incoming sources).
    /// Useful for layered evaluation, GPU pass scheduling, layout passes, etc.
    /// Cycle nodes get depth 0 to keep them processable.
    /// </summary>
    public static Dictionary<Guid, int> ComputeDepths(Graph graph)
    {
        var depth = new Dictionary<Guid, int>();
        foreach (var n in graph.Nodes) depth[n.Id] = 0;

        // Walk in topo order so a node's sources have their final depth before we
        // compute the node's own depth.
        var order = TopologicalOrder(graph);
        // Build reverse lookup: for each node, the list of its source nodes.
        var sources = new Dictionary<Guid, List<Guid>>();
        foreach (var n in graph.Nodes) sources[n.Id] = new List<Guid>();
        foreach (var e in graph.Edges)
        {
            if (!sources.ContainsKey(e.TargetNodeId)) continue;
            sources[e.TargetNodeId].Add(e.SourceNodeId);
        }

        foreach (var n in order)
        {
            int best = 0;
            foreach (var src in sources[n.Id])
            {
                if (depth.TryGetValue(src, out var d) && d + 1 > best)
                    best = d + 1;
            }
            depth[n.Id] = best;
        }
        return depth;
    }

    /// <summary>
    /// Return every node downstream of <paramref name="root"/> (transitive not just
    /// direct children). Excludes <paramref name="root"/> itself. Used for "what does
    /// this affect" queries (e.g. shader recompile scope when a property changes).
    /// </summary>
    public static HashSet<Guid> GetDownstream(Graph graph, Guid root)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        // Seed with direct downstream nodes.
        foreach (var e in graph.Edges)
            if (e.SourceNodeId == root && visited.Add(e.TargetNodeId))
                stack.Push(e.TargetNodeId);

        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var e in graph.Edges)
                if (e.SourceNodeId == n && visited.Add(e.TargetNodeId))
                    stack.Push(e.TargetNodeId);
        }
        return visited;
    }

    /// <summary>
    /// Return every node upstream of <paramref name="root"/> (transitive). Used for
    /// "what does this depend on" queries e.g. tracing inputs back to constants
    /// when generating shader code from an output node.
    /// </summary>
    public static HashSet<Guid> GetUpstream(Graph graph, Guid root)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        foreach (var e in graph.Edges)
            if (e.TargetNodeId == root && visited.Add(e.SourceNodeId))
                stack.Push(e.SourceNodeId);

        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var e in graph.Edges)
                if (e.TargetNodeId == n && visited.Add(e.SourceNodeId))
                    stack.Push(e.SourceNodeId);
        }
        return visited;
    }
}
