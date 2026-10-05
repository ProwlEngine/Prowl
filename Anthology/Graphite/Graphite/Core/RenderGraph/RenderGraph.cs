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

        internal readonly GraphResource[] DeclaredOutputs;

        internal readonly ResourceAccess[] Accesses;

        /// <summary>True if the pass writes the view target, so it only runs for views that have one.</summary>
        public bool WritesViewTarget => Array.IndexOf(Outputs, GraphViewTargetResource.ViewTargetId) >= 0;

        internal PassNode(IPass<TView> pass, RenderResourceID[] inputs, RenderResourceID[] outputs, GraphResource[] declaredOutputs, ResourceAccess[] accesses)
        {
            Pass = pass;
            Inputs = inputs;
            Outputs = outputs;
            DeclaredOutputs = declaredOutputs;
            Accesses = accesses;
        }
    }

    /// <summary>Passes in exec order (topo sorted, ties by insertion order).</summary>
    public IReadOnlyList<PassNode> OrderedPasses { get; }

    /// <summary>All declared resources by ID (first declaration wins).</summary>
    public IReadOnlyDictionary<RenderResourceID, GraphResource> Resources { get; }

    /// <summary>True if any pass writes the view target, so views with a target draw and views with <see cref="IRenderView.TargetSwapchain"/> present.</summary>
    public bool WritesViewTarget { get; }

    private RenderGraph(
        PassNode[] ordered,
        Dictionary<RenderResourceID, GraphResource> resources)
    {
        OrderedPasses = ordered;
        Resources = resources;
        WritesViewTarget = resources.ContainsKey(GraphViewTargetResource.ViewTargetId);
    }

    /// <summary>Disposes physical resources owned by any history resource here.</summary>
    public void Dispose()
    {
        foreach (GraphResource resource in Resources.Values)
            resource.DisposeOwned();
    }

    /// <summary>
    /// Builds the solved graph: runs pass setup, links writers to readers by ID, topo sorts. Throws if an input has no producer, or on a dependency cycle.
    /// </summary>
    public static RenderGraph<TView> Build(
        IReadOnlyList<IPass<TView>> passes)
    {
        int count = passes.Count;
        var nodes = new PassNode[count];
        var resources = new Dictionary<RenderResourceID, GraphResource>();

        var builder = new RenderContextBuilder();
        for (int i = 0; i < count; i++)
        {
            IPass<TView> pass = passes[i];

            builder.Reset();
            pass.Setup(builder);

            RenderResourceID[] inputs = builder.Inputs.ToArray();

            var outputs = new RenderResourceID[builder.Outputs.Count];
            var declared = new GraphResource[builder.Outputs.Count];
            for (int w = 0; w < outputs.Length; w++)
            {
                GraphResource output = builder.Outputs[w];
                outputs[w] = output.Id;
                declared[w] = output;
                if (!resources.TryAdd(output.Id, output) && !SameDeclaration(resources[output.Id], output))
                    throw new InvalidOperationException(
                        $"Pass '{pass.Name}' declares resource '{RenderResourceID.ToString(output.Id)}' with a different description than an earlier declaration.");
            }

            nodes[i] = new PassNode(pass, inputs, outputs, declared, builder.Accesses.ToArray());
        }

        ValidateInputsHaveProducers(nodes, resources);
        ApplyStorageUsage(nodes, resources);

        int[] ordered = TopologicalSort(nodes);

        var orderedNodes = new PassNode[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
            orderedNodes[i] = nodes[ordered[i]];

        return new RenderGraph<TView>(orderedNodes, resources);
    }

    private static bool SameDeclaration(GraphResource existing, GraphResource declared) => (existing, declared) switch
    {
        (GraphTextureResource a, GraphTextureResource b) => a.HistoryDepth == b.HistoryDepth && SameTextureDesc(a.Description, b.Description),
        (GraphBufferResource a, GraphBufferResource b) => a.HistoryDepth == b.HistoryDepth
            && a.Description.SizeInBytes == b.Description.SizeInBytes
            && a.Description.Usage == b.Description.Usage,
        (GraphImportedTextureResource a, GraphImportedTextureResource b) => ReferenceEquals(a.Texture, b.Texture),
        (GraphViewTargetResource a, GraphViewTargetResource b) => a.DepthFormat == b.DepthFormat,
        _ => false,
    };

    private static bool SameTextureDesc(in GraphTextureDesc a, in GraphTextureDesc b)
        => a.SizeMode == b.SizeMode
            && a.Scale == b.Scale
            && a.Width == b.Width
            && a.Height == b.Height
            && a.EnableDepth == b.EnableDepth
            && (a.ColorFormats ?? []).AsSpan().SequenceEqual(b.ColorFormats ?? []);

    private static void ApplyStorageUsage(
        PassNode[] nodes,
        Dictionary<RenderResourceID, GraphResource> resources)
    {
        foreach (PassNode node in nodes)
        {
            foreach (ResourceAccess access in node.Accesses)
                ApplyStorageUsage(node.Pass.Name, access, resources);
        }
    }

    private static void ApplyStorageUsage(string passName, in ResourceAccess access, Dictionary<RenderResourceID, GraphResource> resources)
    {
        GraphResource resource = resources[access.Id];
        if (resource is GraphViewTargetResource)
        {
            if (!access.IsOutput)
                throw new InvalidOperationException($"Pass '{passName}' reads the view target; it can only be written.");
            return;
        }

        if (access.IsTexture != (resource is GraphTextureResource or GraphImportedTextureResource))
            throw new InvalidOperationException(
                $"Pass '{passName}' declares resource '{RenderResourceID.ToString(access.Id)}' as a " +
                $"{(access.IsTexture ? "texture" : "buffer")}, but it is a {(access.IsTexture ? "buffer" : "texture")}.");

        if (!access.IsTexture || (access.TextureUsage & TextureUsageKind.Storage) == 0)
            return;

        switch (resource)
        {
            case GraphTextureResource texture:
                texture.Storage = true;
                break;

            case GraphImportedTextureResource imported:
                foreach (Texture color in imported.Texture.ColorTextures)
                {
                    if ((color.Usage & TextureUsage.Storage) == 0)
                        throw new InvalidOperationException(
                            $"Pass '{passName}' declares imported texture '{RenderResourceID.ToString(access.Id)}' as Storage, " +
                            "but its color textures were not created with TextureUsage.Storage.");
                }
                break;
        }
    }

    private static void ValidateInputsHaveProducers(
        PassNode[] nodes,
        Dictionary<RenderResourceID, GraphResource> resources)
    {
        foreach (PassNode node in nodes)
        {
            foreach (RenderResourceID input in node.Inputs)
            {
                if (!resources.ContainsKey(input))
                    throw new InvalidOperationException(
                        $"Pass '{node.Pass.Name}' reads resource '{RenderResourceID.ToString(input)}' but no pass " +
                        "outputs it and it is not declared centrally on the pipeline.");
            }
        }
    }

    private static int[] TopologicalSort(PassNode[] nodes)
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
