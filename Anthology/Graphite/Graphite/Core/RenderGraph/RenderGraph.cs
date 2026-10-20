using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Graphite;

/// <summary>Solved render graph: every pass in one dependency order, run per execution or per instance of its kind, plus the merged resource table.</summary>
public sealed class RenderGraph : IDisposable
{
    /// <summary>A pass plus its declared resource accesses.</summary>
    public readonly struct PassNode
    {
        /// <summary>The pass.</summary>
        public readonly IPass Pass;

        internal readonly ResourceAccess[] Accesses;

        internal readonly PassthroughLink[] Passthroughs;

        internal readonly int Index;

        /// <summary>True if the pass writes the instance target, so it only runs for instances that have one.</summary>
        public bool WritesInstanceTarget
        {
            get
            {
                foreach (ResourceAccess access in Accesses)
                {
                    if (access.IsOutput && access.Id == GraphInstanceTargetResource.InstanceTargetId)
                        return true;
                }
                return false;
            }
        }

        internal readonly PassResourceAccess[] PublicAccesses;

        internal PassNode(IPass pass, int index, ResourceAccess[] accesses, PassthroughLink[] passthroughs)
        {
            Pass = pass;
            Index = index;
            Accesses = accesses;
            Passthroughs = passthroughs;
            PublicAccesses = ToPublic(accesses);
        }

        private static PassResourceAccess[] ToPublic(ResourceAccess[] accesses)
        {
            PassResourceAccess[] result = new PassResourceAccess[accesses.Length];
            for (int i = 0; i < accesses.Length; i++)
            {
                ResourceAccess access = accesses[i];
                result[i] = new PassResourceAccess(
                    access.Id,
                    access.IsTexture ? GraphResourceKind.Texture : GraphResourceKind.Buffer,
                    access.IsOutput,
                    access.TextureUsage,
                    access.DepthUsage,
                    access.BufferUsage);
            }

            return result;
        }
    }

    private readonly PassNode[] _nodes;
    private readonly PassNode[] _sorted;
    private readonly int[] _positions;
    private readonly Dictionary<GraphKind, PassNode[]> _byKind;
    private readonly Dictionary<GraphKind, GraphKind> _parents;
    private readonly Dictionary<RenderResourceID, GraphKind> _owners;
    private readonly Dictionary<RenderResourceID, int[]> _writers;
    private readonly Dictionary<RenderResourceID, (int Node, RenderResourceID Input)[]> _passthroughs;

    /// <summary>Debug name reported to profilers of every execution of this graph.</summary>
    public string Name { get; }

    /// <summary>The kinds of the root and every declared scope, each once.</summary>
    public IReadOnlyList<GraphKind> Kinds { get; }

    /// <summary>All declared resources by ID (first declaration wins).</summary>
    public IReadOnlyDictionary<RenderResourceID, GraphResource> Resources { get; }

    /// <summary>True if any pass writes the instance target.</summary>
    public bool WritesInstanceTarget { get; }

    private RenderGraph(
        string name,
        PassNode[] nodes,
        PassNode[] sorted,
        int[] positions,
        Dictionary<GraphKind, PassNode[]> byKind,
        Dictionary<GraphKind, GraphKind> parents,
        Dictionary<RenderResourceID, GraphResource> resources,
        Dictionary<RenderResourceID, GraphKind> owners,
        Dictionary<RenderResourceID, int[]> writers,
        Dictionary<RenderResourceID, (int Node, RenderResourceID Input)[]> passthroughs)
    {
        Name = name;
        _nodes = nodes;
        _sorted = sorted;
        _positions = positions;
        _byKind = byKind;
        _parents = parents;
        Resources = resources;
        _owners = owners;
        _writers = writers;
        _passthroughs = passthroughs;
        WritesInstanceTarget = resources.ContainsKey(GraphInstanceTargetResource.InstanceTargetId);

        List<GraphKind> kinds = [GraphKind.Root];
        foreach (GraphKind kind in parents.Keys)
            kinds.Add(kind);
        Kinds = kinds;
    }

    /// <summary>The root kind's passes in execution order.</summary>
    public IReadOnlyList<PassNode> OrderedPasses => PassesOf(GraphKind.Root);

    /// <summary>Every pass of every kind in the one dependency order.</summary>
    public IReadOnlyList<PassNode> Passes => _sorted;

    /// <summary>A kind's passes in execution order. Empty for a kind with none.</summary>
    public IReadOnlyList<PassNode> PassesOf(GraphKind kind) => _byKind.TryGetValue(kind, out PassNode[]? nodes) ? nodes : [];

    /// <summary>True if the kind is the root or a declared scope.</summary>
    public bool Contains(GraphKind kind) => kind == GraphKind.Root || _parents.ContainsKey(kind);

    /// <summary>The kind an instance of the given kind runs under.</summary>
    public GraphKind ParentOf(GraphKind kind)
        => _parents.TryGetValue(kind, out GraphKind parent) ? parent : throw new ArgumentException($"Kind '{kind.Name}' is not declared by graph '{Name}'.", nameof(kind));

    internal bool TryGetOwner(RenderResourceID id, out GraphKind owner) => _owners.TryGetValue(id, out owner);

    internal PassNode NodeAt(int index) => _nodes[index];

    internal int PositionOf(int nodeIndex) => _positions[nodeIndex];

    internal int[] WritersOf(RenderResourceID id) => _writers.TryGetValue(id, out int[]? writers) ? writers : [];

    internal (int Node, RenderResourceID Input)[] PassthroughsTo(RenderResourceID id)
        => _passthroughs.TryGetValue(id, out (int Node, RenderResourceID Input)[]? links) ? links : [];

    /// <summary>Disposes physical resources owned by any history resource here.</summary>
    public void Dispose()
    {
        foreach (GraphResource resource in Resources.Values)
            resource.DisposeOwned();
    }

    /// <summary>
    /// Builds the solved graph: runs pass setup, checks ownership by kind, and orders every pass by what it reads and writes.
    /// Throws if an input has no producer, a resource is written from two kinds, or the dependencies loop.
    /// </summary>
    /// <param name="passes">Every pass, each an execution pass or an instance pass. Order breaks ties and orders writes of one resource.</param>
    /// <param name="scopes">The parent kind of each kind an instance pass runs in. Root is implicit.</param>
    /// <param name="name">Debug name reported to profilers.</param>
    public static RenderGraph Build(
        IReadOnlyList<IPass> passes,
        IReadOnlyList<GraphScope>? scopes = null,
        string name = "")
    {
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentNullException.ThrowIfNull(name);

        Dictionary<GraphKind, GraphKind> parents = ValidateScopes(scopes);

        int count = passes.Count;
        var resources = new Dictionary<RenderResourceID, GraphResource>();
        var nodes = new PassNode[count];
        var accesses = new ResourceAccess[count][];
        var executionBuilder = new RenderContextBuilder();
        var instanceBuilder = new InstanceContextBuilder();
        for (int i = 0; i < count; i++)
        {
            IPass pass = passes[i] ?? throw new ArgumentException("Pass list contains null.", nameof(passes));
            RenderContextBuilder builder = SetupPass(pass, parents, executionBuilder, instanceBuilder);
            foreach (ResourceAccess access in builder.Accesses)
            {
                GraphResource? declared = access.Description;
                if (declared != null && !resources.TryAdd(declared.Id, declared) && !SameDeclaration(resources[declared.Id], declared))
                    throw new RenderGraphException(
                        $"Pass '{pass.Name}' declares resource '{RenderResourceID.ToString(declared.Id)}' with a different description than an earlier declaration.", pass);
            }

            accesses[i] = builder.Accesses.ToArray();
            nodes[i] = new PassNode(pass, i, accesses[i], builder.Passthroughs.ToArray());
        }

        Dictionary<RenderResourceID, GraphKind> owners = AssignOwners(nodes, resources, parents);
        ValidateInputs(nodes, resources);
        ValidatePassthroughs(nodes, resources);
        ApplyStorageUsage(nodes, resources);

        int[] order = GraphSorter.Sort(passes, accesses, resources);
        var sorted = new PassNode[count];
        var lists = new Dictionary<GraphKind, List<PassNode>> { [GraphKind.Root] = [] };
        foreach (GraphKind kind in parents.Keys)
            lists[kind] = [];
        for (int i = 0; i < count; i++)
        {
            sorted[i] = nodes[order[i]];
            lists[sorted[i].Pass.Kind].Add(sorted[i]);
        }

        var byKind = new Dictionary<GraphKind, PassNode[]>();
        var positions = new int[count];
        foreach ((GraphKind kind, List<PassNode> list) in lists)
        {
            byKind[kind] = list.ToArray();
            for (int i = 0; i < list.Count; i++)
                positions[list[i].Index] = i;
        }

        var writers = new Dictionary<RenderResourceID, List<int>>();
        var links = new Dictionary<RenderResourceID, List<(int, RenderResourceID)>>();
        foreach (PassNode node in nodes)
        {
            foreach (ResourceAccess access in node.Accesses)
            {
                if (!access.IsOutput)
                    continue;
                if (!writers.TryGetValue(access.Id, out List<int>? list))
                    writers[access.Id] = list = [];
                if (!list.Contains(node.Index))
                    list.Add(node.Index);
            }

            foreach (PassthroughLink link in node.Passthroughs)
            {
                if (!links.TryGetValue(link.Output, out List<(int, RenderResourceID)>? list))
                    links[link.Output] = list = [];
                list.Add((node.Index, link.Input));
            }
        }

        return new RenderGraph(
            name,
            nodes,
            sorted,
            positions,
            byKind,
            parents,
            resources,
            owners,
            writers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()),
            links.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()));
    }

    private static Dictionary<GraphKind, GraphKind> ValidateScopes(IReadOnlyList<GraphScope>? scopes)
    {
        var parents = new Dictionary<GraphKind, GraphKind>();
        if (scopes == null)
            return parents;

        foreach (GraphScope scope in scopes)
        {
            if (!scope.Kind.IsValid || scope.Kind == GraphKind.Root)
                throw new ArgumentException("A scope needs a kind of its own, not the default or root kind.", nameof(scopes));
            if (!scope.Parent.IsValid)
                throw new ArgumentException($"Scope '{scope.Kind.Name}' has no parent kind.", nameof(scopes));
            if (parents.TryGetValue(scope.Kind, out GraphKind existing) && existing != scope.Parent)
                throw new ArgumentException($"Kind '{scope.Kind.Name}' names two parents, '{existing.Name}' and '{scope.Parent.Name}'.", nameof(scopes));
            parents[scope.Kind] = scope.Parent;
        }

        foreach ((GraphKind kind, GraphKind parent) in parents)
        {
            GraphKind walk = parent;
            int steps = 0;
            while (walk != GraphKind.Root)
            {
                if (!parents.TryGetValue(walk, out GraphKind up))
                    throw new ArgumentException($"Kind '{kind.Name}' has parent '{walk.Name}', which no scope declares.", nameof(scopes));
                if (walk == kind || ++steps > parents.Count)
                    throw new ArgumentException($"Kind '{kind.Name}' is its own ancestor.", nameof(scopes));
                walk = up;
            }
        }

        return parents;
    }

    private static RenderContextBuilder SetupPass(
        IPass pass,
        Dictionary<GraphKind, GraphKind> parents,
        RenderContextBuilder executionBuilder,
        InstanceContextBuilder instanceBuilder)
    {
        switch (pass)
        {
            case IExecutionPass and IInstancePass:
                throw new RenderGraphException($"Pass '{pass.Name}' is both an execution pass and an instance pass.", pass);

            case IExecutionPass execution:
                executionBuilder.Reset();
                execution.Setup(executionBuilder);
                return executionBuilder;

            case IInstancePass instance:
                if (!pass.Kind.IsValid || pass.Kind == GraphKind.Root)
                    throw new RenderGraphException($"Instance pass '{pass.Name}' needs a kind of its own, not the default or root kind.", pass);
                if (!parents.ContainsKey(pass.Kind))
                    throw new RenderGraphException($"Pass '{pass.Name}' has kind '{pass.Kind.Name}', which no scope declares.", pass);
                instanceBuilder.Reset();
                instance.Setup(instanceBuilder);
                return instanceBuilder;

            default:
                throw new RenderGraphException($"Pass '{pass.Name}' is neither an execution pass nor an instance pass.", pass);
        }
    }

    private static Dictionary<RenderResourceID, GraphKind> AssignOwners(
        PassNode[] nodes,
        Dictionary<RenderResourceID, GraphResource> resources,
        Dictionary<GraphKind, GraphKind> parents)
    {
        var owners = new Dictionary<RenderResourceID, GraphKind>();
        var ownerPasses = new Dictionary<RenderResourceID, IPass>();
        foreach (PassNode node in nodes)
        {
            foreach (ResourceAccess access in node.Accesses)
            {
                if (access.Description == null || !resources.TryGetValue(access.Id, out GraphResource? resource) || !IsScoped(resource))
                    continue;

                if (!owners.TryAdd(access.Id, node.Pass.Kind) && owners[access.Id] != node.Pass.Kind)
                    throw new RenderGraphException(
                        $"Resource '{RenderResourceID.ToString(access.Id)}' is written by pass '{ownerPasses[access.Id].Name}' in kind '{owners[access.Id].Name}' " +
                        $"and by pass '{node.Pass.Name}' in kind '{node.Pass.Kind.Name}'. A resource is written from one kind.", node.Pass);
                ownerPasses.TryAdd(access.Id, node.Pass);
            }
        }

        foreach (PassNode node in nodes)
        {
            foreach (ResourceAccess access in node.Accesses)
            {
                if (!owners.TryGetValue(access.Id, out GraphKind owner))
                    continue;

                if (access.IsOutput && node.Pass.Kind != owner)
                    throw new RenderGraphException(
                        $"Resource '{RenderResourceID.ToString(access.Id)}' is written by pass '{ownerPasses[access.Id].Name}' in kind '{owner.Name}' " +
                        $"and by pass '{node.Pass.Name}' in kind '{node.Pass.Kind.Name}'. A resource is written from one kind.", node.Pass);

                if (IsSameOrBelow(node.Pass.Kind, owner, parents))
                    continue;

                throw new RenderGraphException(
                    $"Pass '{node.Pass.Name}' in kind '{node.Pass.Kind.Name}' reads resource '{RenderResourceID.ToString(access.Id)}', " +
                    $"which pass '{ownerPasses[access.Id].Name}' writes in kind '{owner.Name}'. A resource is read from its own kind or one below it.", node.Pass);
            }
        }

        return owners;
    }

    private static bool IsSameOrBelow(GraphKind kind, GraphKind ancestor, Dictionary<GraphKind, GraphKind> parents)
    {
        while (kind != ancestor)
        {
            if (kind == GraphKind.Root || !parents.TryGetValue(kind, out kind))
                return false;
        }
        return true;
    }

    private static bool IsScoped(GraphResource resource) => resource is GraphTextureResource or GraphBufferResource or GraphTextureSlotResource or GraphBufferSlotResource;

    private static bool SameDeclaration(GraphResource existing, GraphResource declared) => (existing, declared) switch
    {
        (GraphTextureResource a, GraphTextureResource b) => a.HistoryDepth == b.HistoryDepth && a.Persistent == b.Persistent && SameTextureDesc(a.Description, b.Description),
        (GraphBufferResource a, GraphBufferResource b) => a.HistoryDepth == b.HistoryDepth && a.Persistent == b.Persistent
            && a.Description.SizeInBytes == b.Description.SizeInBytes
            && a.Description.Usage == b.Description.Usage,
        (GraphImportedTextureResource a, GraphImportedTextureResource b) => ReferenceEquals(a.Texture, b.Texture),
        (GraphImportedBufferResource a, GraphImportedBufferResource b) => ReferenceEquals(a.Buffer, b.Buffer),
        (GraphTextureSlotResource, GraphTextureSlotResource) => true,
        (GraphBufferSlotResource, GraphBufferSlotResource) => true,
        (GraphInstanceTargetResource a, GraphInstanceTargetResource b) => a.DepthFormat == b.DepthFormat,
        _ => false,
    };

    private static bool SameTextureDesc(in GraphTextureDesc a, in GraphTextureDesc b)
        => a.SizeMode == b.SizeMode
            && a.Scale == b.Scale
            && a.Width == b.Width
            && a.Height == b.Height
            && a.EnableDepth == b.EnableDepth
            && Math.Max(1, a.ArrayLayers) == Math.Max(1, b.ArrayLayers)
            && a.SampleCount == b.SampleCount
            && (a.ColorFormats ?? []).AsSpan().SequenceEqual(b.ColorFormats ?? []);

    private static void ApplyStorageUsage(
        PassNode[] nodes,
        Dictionary<RenderResourceID, GraphResource> resources)
    {
        foreach (PassNode node in nodes)
        {
            foreach (ResourceAccess access in node.Accesses)
                ApplyStorageUsage(node.Pass, access, resources);
        }
    }

    private static void ApplyStorageUsage(IPass pass, in ResourceAccess access, Dictionary<RenderResourceID, GraphResource> resources)
    {
        string passName = pass.Name;
        GraphResource resource = resources[access.Id];
        if (resource is GraphInstanceTargetResource)
        {
            if (!access.IsOutput)
                throw new RenderGraphException($"Pass '{passName}' reads the instance target; it can only be written.", pass);
            return;
        }

        if (access.IsTexture != (resource is GraphTextureResource or GraphImportedTextureResource or GraphTextureSlotResource))
            throw new RenderGraphException(
                $"Pass '{passName}' declares resource '{RenderResourceID.ToString(access.Id)}' as a " +
                $"{(access.IsTexture ? "texture" : "buffer")}, but it is a {(access.IsTexture ? "buffer" : "texture")}.", pass);

        if (!access.IsTexture || (access.TextureUsage & TextureState.Storage) == 0)
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
                        throw new RenderGraphException(
                            $"Pass '{passName}' declares imported texture '{RenderResourceID.ToString(access.Id)}' as Storage, " +
                            "but its color textures were not created with TextureUsage.Storage.", pass);
                }
                break;
        }
    }

    private static void ValidateInputs(PassNode[] nodes, Dictionary<RenderResourceID, GraphResource> resources)
    {
        foreach (PassNode node in nodes)
        {
            foreach (ResourceAccess access in node.Accesses)
            {
                if (resources.ContainsKey(access.Id))
                    continue;

                if (access.IsOutput)
                    throw new RenderGraphException(
                        $"Pass '{node.Pass.Name}' writes resource '{RenderResourceID.ToString(access.Id)}' without describing it, and no pass declares it.", node.Pass);

                throw new RenderGraphException(
                    $"Pass '{node.Pass.Name}' reads resource '{RenderResourceID.ToString(access.Id)}' but no pass " +
                    "outputs it and it is not imported.", node.Pass);
            }
        }
    }

    private static void ValidatePassthroughs(PassNode[] nodes, Dictionary<RenderResourceID, GraphResource> resources)
    {
        var inputsOf = new Dictionary<RenderResourceID, List<RenderResourceID>>();
        foreach (PassNode node in nodes)
        {
            foreach (PassthroughLink link in node.Passthroughs)
            {
                string pair = $"'{RenderResourceID.ToString(link.Input)}' to '{RenderResourceID.ToString(link.Output)}'";
                if (!Reads(node, link.Input))
                    throw new RenderGraphException($"Pass '{node.Pass.Name}' passes {pair} through but does not read the input.", node.Pass);
                if (!Writes(node, link.Output))
                    throw new RenderGraphException($"Pass '{node.Pass.Name}' passes {pair} through but does not write the output.", node.Pass);

                GraphResource input = resources[link.Input], output = resources[link.Output];
                bool matches = link.IsTexture
                    ? input is GraphTextureResource { Retained: false } a && output is GraphTextureResource { Retained: false } b && SameDeclaration(a, b)
                    : input is GraphBufferResource { Retained: false } c && output is GraphBufferResource { Retained: false } d && SameDeclaration(c, d);
                if (!matches)
                    throw new RenderGraphException(
                        $"Pass '{node.Pass.Name}' passes {pair} through, which needs two transient {(link.IsTexture ? "textures" : "buffers")} with the same description.", node.Pass);

                if (!inputsOf.TryGetValue(link.Output, out List<RenderResourceID>? list))
                    inputsOf[link.Output] = list = [];
                list.Add(link.Input);
            }
        }

        foreach (RenderResourceID start in inputsOf.Keys)
        {
            if (Loops(start, start, inputsOf, new HashSet<RenderResourceID>()))
                throw new RenderGraphException($"Passthroughs loop back to resource '{RenderResourceID.ToString(start)}'.", null);
        }
    }

    private static bool Loops(RenderResourceID start, RenderResourceID at, Dictionary<RenderResourceID, List<RenderResourceID>> inputsOf, HashSet<RenderResourceID> seen)
    {
        if (!inputsOf.TryGetValue(at, out List<RenderResourceID>? inputs))
            return false;

        foreach (RenderResourceID input in inputs)
        {
            if (input == start || (seen.Add(input) && Loops(start, input, inputsOf, seen)))
                return true;
        }
        return false;
    }

    private static bool Reads(in PassNode node, RenderResourceID id)
    {
        foreach (ResourceAccess access in node.Accesses)
        {
            if (!access.IsOutput && access.Id == id)
                return true;
        }
        return false;
    }

    private static bool Writes(in PassNode node, RenderResourceID id)
    {
        foreach (ResourceAccess access in node.Accesses)
        {
            if (access.IsOutput && access.Id == id)
                return true;
        }
        return false;
    }
}
