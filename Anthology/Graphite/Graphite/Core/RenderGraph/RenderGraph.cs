using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// A solved render graph: passes ordered so readers run after writers, plus the merged resource table
/// and the resource presented as the result. Built from the passes a pipeline adds.
/// </summary>
public sealed class RenderGraph<TView, TDrawCommand>
    where TView : IRenderView
{
    /// <summary>A pass and the resource it nominated as main output.</summary>
    public readonly struct PassNode
    {
        /// <summary>The pass this node runs.</summary>
        public readonly IPass<TView, TDrawCommand> Pass;

        /// <summary>Resource nominated as main output, or default if none.</summary>
        public readonly RenderResourceID MainOutput;

        /// <summary>Resources declared as inputs, kept for profiling and wiring.</summary>
        public readonly RenderResourceID[] Inputs;

        /// <summary>Resources declared as outputs, kept for profiling and wiring.</summary>
        public readonly RenderResourceID[] Outputs;

        internal PassNode(IPass<TView, TDrawCommand> pass, RenderResourceID mainOutput, RenderResourceID[] inputs, RenderResourceID[] outputs)
        {
            Pass = pass;
            MainOutput = mainOutput;
            Inputs = inputs;
            Outputs = outputs;
        }
    }

    /// <summary>Passes in execution order (topo sorted, ties broken by insertion order).</summary>
    public IReadOnlyList<PassNode> OrderedPasses { get; }

    /// <summary>All declared resources and their alloc description (first declaration wins).</summary>
    public IReadOnlyDictionary<RenderResourceID, GraphTextureDesc> Resources { get; }

    /// <summary>Main output of the last pass that set one; the graph's result.</summary>
    public RenderResourceID PresentationSource { get; }

    private RenderGraph(PassNode[] ordered, Dictionary<RenderResourceID, GraphTextureDesc> resources, RenderResourceID presentation)
    {
        OrderedPasses = ordered;
        Resources = resources;
        PresentationSource = presentation;
    }

    private readonly struct Node(IPass<TView, TDrawCommand> pass, RenderResourceID[] inputs, RenderResourceID[] outputs, RenderResourceID mainOutput)
    {
        public readonly IPass<TView, TDrawCommand> Pass = pass;
        public readonly RenderResourceID[] Inputs = inputs;
        public readonly RenderResourceID[] Outputs = outputs;
        public readonly RenderResourceID MainOutput = mainOutput;
    }

    /// <summary>
    /// Builds a solved graph from a pass list. Runs each pass's setup, merges declared resources, links
    /// writers to readers, topo sorts. Last pass to nominate a main output becomes the presentation
    /// source. Throws on a dependency cycle.
    /// </summary>
    public static RenderGraph<TView, TDrawCommand> Build(IReadOnlyList<IPass<TView, TDrawCommand>> passes)
    {
        int count = passes.Count;
        var nodes = new Node[count];
        var resources = new Dictionary<RenderResourceID, GraphTextureDesc>();

        var builder = new RenderContextBuilder();
        for (int i = 0; i < count; i++)
        {
            IPass<TView, TDrawCommand> pass = passes[i];

            builder.Reset();
            pass.Setup(builder);

            var inputs = new RenderResourceID[builder.Inputs.Count];
            for (int r = 0; r < inputs.Length; r++)
            {
                RenderContextBuilder.ResourceDecl decl = builder.Inputs[r];
                inputs[r] = decl.Id;
                resources.TryAdd(decl.Id, decl.Desc);
            }

            var outputs = new RenderResourceID[builder.Outputs.Count];
            bool mainIsOutput = false;
            for (int w = 0; w < outputs.Length; w++)
            {
                RenderContextBuilder.ResourceDecl decl = builder.Outputs[w];
                outputs[w] = decl.Id;
                resources.TryAdd(decl.Id, decl.Desc);
                mainIsOutput |= decl.Id == builder.MainOutput;
            }

            if (builder.HasMainOutput && !mainIsOutput)
                throw new InvalidOperationException($"Pass '{pass.Name}' set a main output that it did not declare as an output texture.");

            nodes[i] = new Node(pass, inputs, outputs, builder.HasMainOutput ? builder.MainOutput : default);
        }

        int[] ordered = TopologicalSort(nodes);

        var orderedNodes = new PassNode[ordered.Length];
        RenderResourceID presentation = default;
        for (int i = 0; i < ordered.Length; i++)
        {
            Node n = nodes[ordered[i]];
            orderedNodes[i] = new PassNode(n.Pass, n.MainOutput, n.Inputs, n.Outputs);
            if (n.MainOutput.IsValid)
                presentation = n.MainOutput;
        }

        return new RenderGraph<TView, TDrawCommand>(orderedNodes, resources, presentation);
    }

    private static int[] TopologicalSort(Node[] nodes)
    {
        int count = nodes.Length;

        var writersOf = new Dictionary<RenderResourceID, List<int>>();
        for (int i = 0; i < count; i++)
        {
            foreach (RenderResourceID output in nodes[i].Outputs)
            {
                if (!writersOf.TryGetValue(output, out List<int>? list))
                    writersOf[output] = list = new List<int>();
                list.Add(i);
            }
        }

        var adjacency = new List<int>[count];
        var indegree = new int[count];
        for (int i = 0; i < count; i++)
            adjacency[i] = new List<int>();

        for (int reader = 0; reader < count; reader++)
        {
            foreach (RenderResourceID input in nodes[reader].Inputs)
            {
                if (!writersOf.TryGetValue(input, out List<int>? writers))
                    continue;

                foreach (int writer in writers)
                {
                    if (writer == reader || adjacency[writer].Contains(reader))
                        continue;

                    adjacency[writer].Add(reader);
                    indegree[reader]++;
                }
            }
        }

        var order = new int[count];
        int emitted = 0;
        var scheduled = new bool[count];

        while (emitted < count)
        {
            int next = -1;
            for (int i = 0; i < count; i++)
            {
                if (!scheduled[i] && indegree[i] == 0)
                {
                    next = i;
                    break;
                }
            }

            if (next < 0)
                throw new InvalidOperationException("Render graph has a cyclic texture dependency and cannot be ordered.");

            scheduled[next] = true;
            order[emitted++] = next;

            foreach (int dependent in adjacency[next])
                indegree[dependent]--;
        }

        return order;
    }
}
