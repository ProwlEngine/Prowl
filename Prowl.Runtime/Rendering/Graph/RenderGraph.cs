// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// A solved render graph: passes ordered so every reader runs after its writers, plus the merged
/// resource table and the resource the pipeline presents to the camera target. Built by
/// <see cref="Build"/> from the passes a <see cref="RenderPipeline{TDrawCommand}"/> adds.
/// </summary>
public sealed class RenderGraph<TDrawCommand>
{
    /// <summary>A pass and the resource it nominated as its main output.</summary>
    public readonly struct PassNode
    {
        public readonly IPass<TDrawCommand> Pass;
        public readonly RenderResourceID MainOutput;

        /// <summary>The resources this pass declared as inputs, retained for profiling and wiring.</summary>
        public readonly RenderResourceID[] Inputs;

        /// <summary>The resources this pass declared as outputs, retained for profiling and wiring.</summary>
        public readonly RenderResourceID[] Outputs;

        internal PassNode(IPass<TDrawCommand> pass, RenderResourceID mainOutput, RenderResourceID[] inputs, RenderResourceID[] outputs)
        {
            Pass = pass;
            MainOutput = mainOutput;
            Inputs = inputs;
            Outputs = outputs;
        }
    }

    /// <summary>Passes in execution order (topologically sorted, insertion order breaking ties).</summary>
    public IReadOnlyList<PassNode> OrderedPasses { get; }

    /// <summary>Every declared resource and the description used to allocate it (first declaration wins).</summary>
    public IReadOnlyDictionary<RenderResourceID, RenderTextureDesc> Resources { get; }

    /// <summary>The main output of the final pass; blitted to the camera target when the graph runs.</summary>
    public RenderResourceID PresentationSource { get; }

    private RenderGraph(PassNode[] ordered, Dictionary<RenderResourceID, RenderTextureDesc> resources, RenderResourceID presentation)
    {
        OrderedPasses = ordered;
        Resources = resources;
        PresentationSource = presentation;
    }

    private readonly struct Node(IPass<TDrawCommand> pass, RenderResourceID[] inputs, RenderResourceID[] outputs, RenderResourceID mainOutput)
    {
        public readonly IPass<TDrawCommand> Pass = pass;
        public readonly RenderResourceID[] Inputs = inputs;
        public readonly RenderResourceID[] Outputs = outputs;
        public readonly RenderResourceID MainOutput = mainOutput;
    }

    /// <summary>
    /// Builds a solved graph from an ordered pass list. Runs each pass's <see cref="IPass.SetupInputs"/>,
    /// merges the declared resources, links writers to readers, and topologically sorts. A pass may
    /// optionally nominate a main output; the last pass in execution order that does becomes the
    /// resource presented to the camera target. Throws on a dependency cycle.
    /// </summary>
    public static RenderGraph<TDrawCommand> Build(IReadOnlyList<IPass<TDrawCommand>> passes)
    {
        int count = passes.Count;
        var nodes = new Node[count];
        var resources = new Dictionary<RenderResourceID, RenderTextureDesc>();

        var builder = new RenderContextBuilder();
        for (int i = 0; i < count; i++)
        {
            IPass<TDrawCommand> pass = passes[i];

            builder.Reset();
            pass.SetupInputs(builder);

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

        var ordered = TopologicalSort(nodes);

        var orderedNodes = new PassNode[ordered.Length];
        RenderResourceID presentation = default;
        for (int i = 0; i < ordered.Length; i++)
        {
            Node n = nodes[ordered[i]];
            orderedNodes[i] = new PassNode(n.Pass, n.MainOutput, n.Inputs, n.Outputs);
            if (n.MainOutput.IsValid)
                presentation = n.MainOutput;
        }

        return new RenderGraph<TDrawCommand>(orderedNodes, resources, presentation);
    }

    // Kahn's algorithm with insertion-order tie-breaking (stable). Edge writer -> reader for every
    // resource a pass reads that another pass writes, so a reader is never scheduled before its writers.
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
