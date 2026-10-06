using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>Solved render graph: passes ordered readers-after-writers, plus merged resource table. Built from a pipeline's passes and centrally declared resources.</summary>
public sealed class RenderGraph : IDisposable
{
    /// <summary>A pass plus its declared resource accesses.</summary>
    public readonly struct PassNode
    {
        /// <summary>The pass.</summary>
        public readonly IPass Pass;

        internal readonly ResourceAccess[] Accesses;

        /// <summary>True if the pass writes the view target, so it only runs for views that have one.</summary>
        public bool WritesViewTarget
        {
            get
            {
                foreach (ResourceAccess access in Accesses)
                {
                    if (access.IsOutput && access.Id == GraphViewTargetResource.ViewTargetId)
                        return true;
                }
                return false;
            }
        }

        internal readonly RenderResourceID[] Inputs;

        internal readonly RenderResourceID[] Outputs;

        internal PassNode(IPass pass, ResourceAccess[] accesses)
        {
            Pass = pass;
            Accesses = accesses;
            Inputs = Ids(accesses, output: false);
            Outputs = Ids(accesses, output: true);
        }

        private static RenderResourceID[] Ids(ResourceAccess[] accesses, bool output)
        {
            var ids = new List<RenderResourceID>(accesses.Length);
            foreach (ResourceAccess access in accesses)
            {
                if (access.IsOutput == output)
                    ids.Add(access.Id);
            }
            return ids.ToArray();
        }
    }

    /// <summary>Passes in exec order (topo sorted, ties by insertion order).</summary>
    public IReadOnlyList<PassNode> OrderedPasses { get; }

    /// <summary>All declared resources by ID (first declaration wins).</summary>
    public IReadOnlyDictionary<RenderResourceID, GraphResource> Resources { get; }

    /// <summary>True if any pass writes the view target, so views with a target draw and views targeting a swapchain present.</summary>
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
    public static RenderGraph Build(
        IReadOnlyList<IPass> passes)
    {
        int count = passes.Count;
        var nodes = new PassNode[count];
        var resources = new Dictionary<RenderResourceID, GraphResource>();

        var builder = new RenderContextBuilder();
        for (int i = 0; i < count; i++)
        {
            IPass pass = passes[i];

            builder.Reset();
            pass.Setup(builder);

            foreach (ResourceAccess access in builder.Accesses)
            {
                GraphResource? output = access.Description;
                if (output == null)
                    continue;

                if (!resources.TryAdd(output.Id, output) && !SameDeclaration(resources[output.Id], output))
                    throw new InvalidOperationException(
                        $"Pass '{pass.Name}' declares resource '{RenderResourceID.ToString(output.Id)}' with a different description than an earlier declaration.");
            }

            nodes[i] = new PassNode(pass, builder.Accesses.ToArray());
        }

        ValidateInputsHaveProducers(nodes, resources);
        ApplyStorageUsage(nodes, resources);

        int[] ordered = TopologicalSort(nodes);

        var orderedNodes = new PassNode[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
            orderedNodes[i] = nodes[ordered[i]];

        return new RenderGraph(orderedNodes, resources);
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

        if (!access.IsTexture || access.TextureUsage != TextureState.Storage)
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
            foreach (ResourceAccess access in node.Accesses)
            {
                if (!access.IsOutput && !resources.ContainsKey(access.Id))
                    throw new InvalidOperationException(
                        $"Pass '{node.Pass.Name}' reads resource '{RenderResourceID.ToString(access.Id)}' but no pass " +
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
            foreach (ResourceAccess access in nodes[i].Accesses)
            {
                if (!access.IsOutput)
                    continue;

                if (!writersOf.TryGetValue(access.Id, out List<int>? list))
                    writersOf[access.Id] = list = new List<int>();
                list.Add(i);
            }
        }

        var adjacency = new List<int>[count];
        var indegree = new int[count];
        for (int i = 0; i < count; i++)
            adjacency[i] = new List<int>();

        for (int reader = 0; reader < count; reader++)
        {
            foreach (ResourceAccess access in nodes[reader].Accesses)
            {
                if (access.IsOutput || !writersOf.TryGetValue(access.Id, out List<int>? writers))
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
