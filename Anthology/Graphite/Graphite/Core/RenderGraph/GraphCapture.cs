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
    private readonly ICaptureProfiler _hook;
    private readonly RenderContext _context;
    private readonly RenderGraph _graph;
    private readonly PassInfo[] _passInfos;
    private readonly RenderGraph.PassNode[] _nodes;
    private readonly Dictionary<RenderResourceID, GraphBacking[]> _backings = new();
    private readonly Dictionary<ResourceId, uint> _passEntry = new();
    private readonly Dictionary<ResourceId, ResourceVersion> _firstReferenced = new();
    private readonly HashSet<ResourceId> _listed = new();
    private readonly List<PassReference> _references = new();

    public GraphCapture(ICaptureProfiler hook, RenderContext context, RenderGraph graph, RenderGraph.PassNode[] nodes, PassInfo[] passInfos)
    {
        _hook = hook;
        _context = context;
        _graph = graph;
        _nodes = nodes;
        _passInfos = passInfos;
    }

    public void BeginView(string viewName, int viewIndex, uint pixelWidth, uint pixelHeight, ulong executionId)
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

        ViewCaptureInfo info = new(executionId, viewName, viewIndex, pixelWidth, pixelHeight, resources.ToArray(), passes);
        _hook.OnViewBegin(in info);
    }

    public void BeginPass(int index)
    {
        _passEntry.Clear();
        foreach (ResourceAccess access in _nodes[index].Accesses)
        {
            foreach (GraphBacking backing in _backings[access.Id])
                _passEntry[backing.Id] = ResolveBacking(access, backing).Version;
        }
    }

    public void EndPass(int index, CommandBuffer commands)
    {
        _references.Clear();
        _listed.Clear();
        _firstReferenced.Clear();
        foreach (ReferencedResource referenced in commands.ReferencedResources)
            _firstReferenced[referenced.Buffer?.ResourceId ?? referenced.Texture!.ResourceId] = referenced.FirstVersion;

        foreach (ResourceAccess access in _nodes[index].Accesses)
        {
            foreach (GraphBacking backing in _backings[access.Id])
            {
                if (backing.Role == BackingRole.Depth && access.DepthState(access.TextureUsage) is null)
                    continue;

                if (!_listed.Add(backing.Id))
                    continue;

                ResourceVersion first = _firstReferenced.TryGetValue(backing.Id, out ResourceVersion referenced)
                    ? referenced
                    : new ResourceVersion(backing.Id, _passEntry[backing.Id]);
                AddReference(first, access, backing);
            }
        }

        foreach (ReferencedResource referenced in commands.ReferencedResources)
        {
            if (!_listed.Add(referenced.Buffer?.ResourceId ?? referenced.Texture!.ResourceId))
                continue;

            if (referenced.Buffer is { } buffer)
                _references.Add(new PassReference(referenced.FirstVersion, buffer.CurrentVersion.Version, buffer.Name, null, buffer.DescriptionValue));
            else
                _references.Add(new PassReference(referenced.FirstVersion, referenced.Texture!.CurrentVersion.Version, referenced.Texture.Name, referenced.Texture.DescriptionValue, null));
        }

        _hook.OnPassEnd(in _passInfos[index], CollectionsMarshal.AsSpan(_references), CaptureContext.Instance);
    }

    public void EndView() => _hook.OnViewEnd();

    private void AddReference(ResourceVersion first, ResourceAccess access, GraphBacking backing)
    {
        if (!access.IsTexture)
        {
            DeviceBuffer buffer = _context.GetRenderBuffer(new BufferHandle(access.Id));
            _references.Add(new PassReference(first, buffer.CurrentVersion.Version, buffer.Name, null, buffer.DescriptionValue));
            return;
        }

        RenderTexture target = _context.GetRenderTexture(new TextureHandle(access.Id));
        Texture texture = backing.Role == BackingRole.Depth ? target.DepthTexture! : target.ColorTextures[(int)backing.Index];
        _references.Add(new PassReference(first, texture.CurrentVersion.Version, texture.Name, texture.DescriptionValue, null));
    }

    private ResourceVersion ResolveBacking(ResourceAccess access, GraphBacking backing)
    {
        if (!access.IsTexture)
            return _context.GetRenderBuffer(new BufferHandle(access.Id)).CurrentVersion;

        RenderTexture texture = _context.GetRenderTexture(new TextureHandle(access.Id));
        return (backing.Role == BackingRole.Depth ? texture.DepthTexture! : texture.ColorTextures[(int)backing.Index]).CurrentVersion;
    }

    private GraphResourceInfo DescribeResource(RenderResourceID id, bool isTexture)
    {
        GraphResource resource = _graph.Resources[id];
        string name = RenderResourceID.ToString(id) ?? string.Empty;
        List<GraphBacking> backings = new();
        GraphTextureDesc? textureDesc = null;
        GraphBufferDesc? bufferDesc = null;
        GraphResourceOrigin origin = GraphResourceOrigin.Transient;

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
                    origin = GraphResourceOrigin.ViewTarget;
                    break;
                case GraphImportedTextureResource:
                    origin = GraphResourceOrigin.Imported;
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
        return new GraphResourceInfo(
            id, name, isTexture ? GraphResourceKind.Texture : GraphResourceKind.Buffer, array, origin, textureDesc, bufferDesc);
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
