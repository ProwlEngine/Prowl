// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Editor.Build;

/// <summary>One stage and what has to finish before it may start.</summary>
public sealed record StageNode
{
    public required BuildStage Stage { get; init; }
    public IReadOnlyList<BuildStage> DependsOn { get; init; } = Array.Empty<BuildStage>();
    public StageResources Resources { get; init; } = StageResources.IoBound;
    public StageFailurePolicy OnFailure { get; init; } = StageFailurePolicy.FailFast;
}

/// <summary>
/// The stages of one build and their ordering constraints.
/// </summary>
/// <remarks>
/// Built per request rather than per pipeline, because the shape genuinely changes with the request. A
/// desktop build that embeds assets into the assembly has to process them before the player project is
/// generated, while one that packs them alongside has to wait until after publish, since publish clears
/// the output directory. Those are different graphs from the same pipeline.
/// </remarks>
public sealed class StageGraph
{
    private readonly Dictionary<BuildStage, StageNode> _nodes;

    public StageGraph(IEnumerable<StageNode> nodes)
    {
        _nodes = new Dictionary<BuildStage, StageNode>();
        foreach (var node in nodes)
        {
            if (!_nodes.TryAdd(node.Stage, node))
                throw new ArgumentException($"Stage '{node.Stage}' was declared twice.", nameof(nodes));
        }

        foreach (var node in _nodes.Values)
            foreach (var dependency in node.DependsOn)
                if (!_nodes.ContainsKey(dependency))
                    throw new ArgumentException($"Stage '{node.Stage}' depends on '{dependency}', which is not in the graph.", nameof(nodes));

        // Proven up front so a cycle is a construction error with the offending stages named, rather than
        // a build that starts, does real work, and then wedges with nothing runnable.
        if (TopologicalOrder() is null)
            throw new ArgumentException("The stage graph contains a cycle.", nameof(nodes));
    }

    public IReadOnlyCollection<StageNode> Nodes => _nodes.Values;

    public StageNode this[BuildStage stage] => _nodes[stage];

    /// <summary>Stages whose dependencies are all in <paramref name="completed"/> and which have not run.</summary>
    public IEnumerable<StageNode> Ready(IReadOnlySet<BuildStage> completed)
        => _nodes.Values
            .Where(n => !completed.Contains(n.Stage) && n.DependsOn.All(completed.Contains))
            .OrderBy(n => n.Stage.Id, StringComparer.Ordinal);

    /// <summary>A dependency respecting order, or null when the graph has a cycle.</summary>
    public IReadOnlyList<BuildStage>? TopologicalOrder()
    {
        var order = new List<BuildStage>();
        var completed = new HashSet<BuildStage>();

        while (order.Count < _nodes.Count)
        {
            // Ordered by id so an identical graph always produces an identical order.
            var next = Ready(completed).FirstOrDefault();
            if (next is null) return null;

            order.Add(next.Stage);
            completed.Add(next.Stage);
        }

        return order;
    }
}
