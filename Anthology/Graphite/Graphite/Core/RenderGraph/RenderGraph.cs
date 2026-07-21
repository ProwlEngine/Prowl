using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>Solved render graph: passes ordered readers-after-writers, plus merged resource table. Built from a pipeline's passes and centrally declared resources.</summary>
public sealed class RenderGraph<TView> : IDisposable
    where TView : IRenderView
{
    /// <summary>A pass plus its declared resources.</summary>
    public readonly struct PassNode
    {
        /// <summary>The pass.</summary>
        public readonly IPass<TView> Pass;

        /// <summary>Declared inputs, for profiling/wiring.</summary>
        public readonly RenderResourceID[] Inputs;

        /// <summary>Declared outputs, for profiling/wiring.</summary>
        public readonly RenderResourceID[] Outputs;

        internal PassNode(IPass<TView> pass, RenderResourceID[] inputs, RenderResourceID[] outputs)
        {
            Pass = pass;
            Inputs = inputs;
            Outputs = outputs;
        }
    }

    /// <summary>Passes in exec order (topo sorted, ties by insertion order).</summary>
    public IReadOnlyList<PassNode> OrderedPasses { get; }

    /// <summary>All declared resources by ID (first declaration wins).</summary>
    public IReadOnlyDictionary<RenderResourceID, GraphResource> Resources { get; }

    /// <summary>Present pass's declared inputs, for profiling/wiring.</summary>
    public IReadOnlyList<RenderResourceID> PresentInputs { get; }

    /// <summary>True if present pass wants the window's swapchain target.</summary>
    public bool PresentRequestsSwapchain { get; }

    private RenderGraph(
        PassNode[] ordered,
        Dictionary<RenderResourceID, GraphResource> resources,
        RenderResourceID[] presentInputs,
        bool presentRequestsSwapchain)
    {
        OrderedPasses = ordered;
        Resources = resources;
        PresentInputs = presentInputs;
        PresentRequestsSwapchain = presentRequestsSwapchain;
    }

    /// <summary>Disposes physical resources owned by any history resource here.</summary>
    public void Dispose()
    {
        foreach (GraphResource resource in Resources.Values)
            resource.DisposeOwned();
    }

    private readonly struct Node(IPass<TView> pass, RenderResourceID[] inputs, RenderResourceID[] outputs)
    {
        public readonly IPass<TView> Pass = pass;
        public readonly RenderResourceID[] Inputs = inputs;
        public readonly RenderResourceID[] Outputs = outputs;
    }

    /// <summary>
    /// Builds the solved graph: runs pass setup, links writers to readers by ID, topo sorts. Present pass always runs last, so its inputs are recorded but not ordered. Throws if an input has no producer, or on a dependency cycle.
    /// </summary>
    public static RenderGraph<TView> Build(
        IReadOnlyList<IPass<TView>> passes,
        IPresentPass<TView> presentPass,
        IReadOnlyList<GraphResource>? centralResources = null)
    {
        int count = passes.Count;
        var nodes = new Node[count];
        var resources = new Dictionary<RenderResourceID, GraphResource>();

        if (centralResources != null)
        {
            foreach (GraphResource resource in centralResources)
                resources.TryAdd(resource.Id, resource);
        }

        var builder = new RenderContextBuilder();
        for (int i = 0; i < count; i++)
        {
            IPass<TView> pass = passes[i];

            builder.Reset();
            pass.Setup(builder);

            var inputs = new RenderResourceID[builder.Inputs.Count];
            for (int r = 0; r < inputs.Length; r++)
                inputs[r] = builder.Inputs[r];

            var outputs = new RenderResourceID[builder.Outputs.Count];
            for (int w = 0; w < outputs.Length; w++)
            {
                GraphResource output = builder.Outputs[w];
                outputs[w] = output.Id;
                resources.TryAdd(output.Id, output);
            }

            nodes[i] = new Node(pass, inputs, outputs);
        }

        var presentBuilder = new PresentContextBuilder();
        presentPass.Setup(presentBuilder);

        var presentInputs = new RenderResourceID[presentBuilder.Inputs.Count];
        for (int r = 0; r < presentInputs.Length; r++)
            presentInputs[r] = presentBuilder.Inputs[r];

        ValidateInputsHaveProducers(nodes, presentPass.Name, presentInputs, resources);

        int[] ordered = TopologicalSort(nodes);

        var orderedNodes = new PassNode[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            Node n = nodes[ordered[i]];
            orderedNodes[i] = new PassNode(n.Pass, n.Inputs, n.Outputs);
        }

        return new RenderGraph<TView>(
            orderedNodes, resources, presentInputs, presentBuilder.RequestsSwapchain);
    }

    private static void ValidateInputsHaveProducers(
        Node[] nodes,
        string presentPassName,
        RenderResourceID[] presentInputs,
        Dictionary<RenderResourceID, GraphResource> resources)
    {
        foreach (Node node in nodes)
        {
            foreach (RenderResourceID input in node.Inputs)
            {
                if (!resources.ContainsKey(input))
                    throw new InvalidOperationException(
                        $"Pass '{node.Pass.Name}' reads resource '{RenderResourceID.ToString(input)}' but no pass " +
                        "outputs it and it is not declared centrally on the pipeline.");
            }
        }

        foreach (RenderResourceID input in presentInputs)
        {
            if (!resources.ContainsKey(input))
                throw new InvalidOperationException(
                    $"Present pass '{presentPassName}' reads resource '{RenderResourceID.ToString(input)}' but no " +
                    "pass outputs it and it is not declared centrally on the pipeline.");
        }
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
                throw new InvalidOperationException("Render graph has a cyclic resource dependency and cannot be ordered.");

            scheduled[next] = true;
            order[emitted++] = next;

            foreach (int dependent in adjacency[next])
                indegree[dependent]--;
        }

        return order;
    }
}
