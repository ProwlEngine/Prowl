using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.RenderGraph;

internal sealed class CaptureContext : ICaptureContext
{
    public static readonly CaptureContext Instance = new();
}

/// <summary>Per view state that turns graph execution into capture hook callbacks.</summary>
internal sealed class GraphCapture
{
    private readonly ICaptureHook _hook;
    private readonly RenderContext _context;
    private readonly RenderGraph _graph;
    private readonly PassInfo[] _passInfos;
    private readonly RenderGraph.PassNode[] _nodes;
    private readonly Dictionary<RenderResourceID, GraphBacking[]> _backings = new();
    private readonly HashSet<ResourceId> _graphIds = new();
    private readonly HashSet<ResourceId> _viewTargetIds = new();
    private readonly HashSet<ResourceId> _produced = new();
    private readonly HashSet<ResourceId> _externalsSeen = new();
    private readonly List<ResourceUse> _inputs = new();
    private readonly List<ResourceUse> _outputs = new();
    private readonly List<ResourceUse> _loaded = new();
    private readonly List<ExternalResourceInfo> _externals = new();

    public GraphCapture(ICaptureHook hook, RenderContext context, RenderGraph graph, RenderGraph.PassNode[] nodes, PassInfo[] passInfos)
    {
        _hook = hook;
        _context = context;
        _graph = graph;
        _nodes = nodes;
        _passInfos = passInfos;
    }

    public void BeginView(string viewName, ulong executionId)
    {
        List<GraphResourceInfo> resources = new();
        HashSet<RenderResourceID> seen = new();
        foreach (RenderGraph.PassNode node in _nodes)
        {
            foreach (ResourceAccess access in node.Accesses)
            {
                if (seen.Add(access.Id))
                    resources.Add(DescribeResource(access.Id, access.IsTexture));
            }
        }

        PassCaptureInfo[] passes = new PassCaptureInfo[_nodes.Length];
        for (int i = 0; i < _nodes.Length; i++)
            passes[i] = new PassCaptureInfo(_passInfos[i], ToPublic(_nodes[i].Accesses));

        ViewCaptureInfo info = new(executionId, viewName, resources.ToArray(), passes);
        _hook.OnViewBegin(in info, CaptureContext.Instance);
    }

    public void BeginPass(int index)
    {
        _inputs.Clear();
        foreach (ResourceAccess access in _nodes[index].Accesses)
        {
            if (!access.IsOutput)
                AddUses(_inputs, access);
        }

        _hook.OnPassBegin(in _passInfos[index], CollectionsMarshal.AsSpan(_inputs), CaptureContext.Instance);
    }

    public void EndPass(int index, CommandBuffer commands)
    {
        _outputs.Clear();
        foreach (ResourceAccess access in _nodes[index].Accesses)
        {
            if (access.IsOutput)
                AddUses(_outputs, access);
        }

        _loaded.Clear();
        _externals.Clear();
        foreach (LoadedAttachmentUse load in commands.LoadedAttachments)
        {
            ResourceId id = load.Texture.ResourceId;
            if (_viewTargetIds.Contains(id) || _produced.Contains(id) || !_graphIds.Contains(id))
                continue;

            _loaded.Add(new ResourceUse(
                OwnerOf(id), load.Version, TextureRange(load.Texture),
                ResourceUsage.Attachment));
        }

        foreach (ReferencedResource referenced in commands.ReferencedResources)
        {
            ResourceId id = referenced.Buffer?.ResourceId ?? referenced.Texture!.ResourceId;
            if (_graphIds.Contains(id) || !_externalsSeen.Add(id))
                continue;

            _externals.Add(referenced.Buffer is { } buffer
                ? new ExternalResourceInfo(id, buffer.Name, referenced.FirstVersion, null, buffer.DescriptionValue)
                : new ExternalResourceInfo(id, referenced.Texture!.Name, referenced.FirstVersion, referenced.Texture.DescriptionValue, null));
        }

        _hook.OnPassEnd(
            in _passInfos[index],
            CollectionsMarshal.AsSpan(_outputs),
            CollectionsMarshal.AsSpan(_loaded),
            CollectionsMarshal.AsSpan(_externals),
            CaptureContext.Instance);

        foreach (ResourceUse output in _outputs)
            _produced.Add(output.Version.Resource);
    }

    public void EndView() => _hook.OnViewEnd(CaptureContext.Instance);

    private GraphResourceInfo DescribeResource(RenderResourceID id, bool isTexture)
    {
        GraphResource resource = _graph.Resources[id];
        string name = RenderResourceID.ToString(id) ?? string.Empty;
        List<GraphBacking> backings = new();
        GraphTextureDesc? textureDesc = null;
        GraphBufferDesc? bufferDesc = null;
        bool imported = false;

        if (isTexture)
        {
            RenderTexture texture = _context.GetRenderTexture(new TextureHandle(id));
            for (int i = 0; i < texture.ColorTextures.Length; i++)
                backings.Add(new GraphBacking(texture.ColorTextures[i].ResourceId, texture.ColorTextures[i].CurrentVersion, BackingRole.Color, (uint)i));
            if (texture.DepthTexture is { } depth)
                backings.Add(new GraphBacking(depth.ResourceId, depth.CurrentVersion, BackingRole.Depth, 0));

            switch (resource)
            {
                case GraphTextureResource transient:
                    textureDesc = transient.Description;
                    break;
                case GraphViewTargetResource:
                    imported = true;
                    foreach (GraphBacking backing in backings)
                        _viewTargetIds.Add(backing.Id);
                    break;
                case GraphImportedTextureResource:
                    imported = true;
                    break;
            }
        }
        else
        {
            DeviceBuffer buffer = _context.GetRenderBuffer(new BufferHandle(id));
            backings.Add(new GraphBacking(buffer.ResourceId, buffer.CurrentVersion, BackingRole.Buffer, 0));
            if (resource is GraphBufferResource bufferResource)
                bufferDesc = bufferResource.Description;
        }

        GraphBacking[] array = backings.ToArray();
        _backings[id] = array;
        foreach (GraphBacking backing in array)
            _graphIds.Add(backing.Id);

        return new GraphResourceInfo(
            id, name, isTexture ? GraphResourceKind.Texture : GraphResourceKind.Buffer, array, imported, textureDesc, bufferDesc);
    }

    private void AddUses(List<ResourceUse> uses, ResourceAccess access)
    {
        GraphBacking[] backings = _backings[access.Id];
        if (!access.IsTexture)
        {
            DeviceBuffer buffer = _context.GetRenderBuffer(new BufferHandle(access.Id));
            uses.Add(new ResourceUse(access.Id, buffer.CurrentVersion, ResourceRange.Bytes(0, buffer.SizeInBytes), BufferUsage(access.BufferUsage)));
            return;
        }

        RenderTexture texture = _context.GetRenderTexture(new TextureHandle(access.Id));
        foreach (Texture color in texture.ColorTextures)
            uses.Add(new ResourceUse(access.Id, color.CurrentVersion, TextureRange(color), TextureUsage(access.TextureUsage)));

        if (texture.DepthTexture is { } depth && access.DepthState(access.TextureUsage) is { } depthState)
            uses.Add(new ResourceUse(access.Id, depth.CurrentVersion, TextureRange(depth), TextureUsage(depthState)));
    }

    private RenderResourceID OwnerOf(ResourceId backing)
    {
        foreach ((RenderResourceID id, GraphBacking[] backings) in _backings)
        {
            foreach (GraphBacking candidate in backings)
            {
                if (candidate.Id == backing)
                    return id;
            }
        }

        return default;
    }

    private static ResourceRange TextureRange(Texture texture)
        => ResourceRange.Subresources(0, texture.MipLevels, 0, ValidationHelpers.GetEffectiveArrayLayers(texture));

    private static ResourceUsage TextureUsage(TextureState state) => state switch
    {
        TextureState.Sampled => ResourceUsage.Sampled,
        TextureState.Storage => ResourceUsage.Storage,
        TextureState.Attachment => ResourceUsage.Attachment,
        TextureState.TransferSrc => ResourceUsage.CopySource,
        TextureState.TransferDst => ResourceUsage.CopyDestination,
        TextureState.DepthReadOnly => ResourceUsage.Attachment | ResourceUsage.Sampled,
        _ => ResourceUsage.None,
    };

    private static ResourceUsage BufferUsage(BufferAccess access)
    {
        ResourceUsage usage = ResourceUsage.None;
        if ((access & (BufferAccess.ShaderRead | BufferAccess.ShaderWrite)) != 0) usage |= ResourceUsage.Storage;
        if ((access & BufferAccess.Uniform) != 0) usage |= ResourceUsage.Uniform;
        if ((access & BufferAccess.Vertex) != 0) usage |= ResourceUsage.Vertex;
        if ((access & BufferAccess.Index) != 0) usage |= ResourceUsage.Index;
        if ((access & BufferAccess.Indirect) != 0) usage |= ResourceUsage.Indirect;
        if ((access & BufferAccess.TransferRead) != 0) usage |= ResourceUsage.CopySource;
        if ((access & BufferAccess.TransferWrite) != 0) usage |= ResourceUsage.CopyDestination;
        return usage;
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
