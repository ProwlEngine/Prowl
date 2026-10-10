using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.RenderGraph;

internal sealed class CaptureContext : ICaptureContext
{
    private readonly GraphCapture _owner;

    public CaptureContext(GraphCapture owner) => _owner = owner;

    public CaptureCopy Copy(in PassReference reference, CopyPlacement placement) => _owner.Copy(in reference, placement);
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
    private readonly Dictionary<ResourceId, ReferencedResource> _firstReferenced = new();
    private readonly HashSet<ResourceId> _listed = new();
    private readonly List<PassReference> _references = new();
    private readonly Dictionary<ResourceId, (Texture? Texture, DeviceBuffer? Buffer)> _live = new();
    private readonly HashSet<ResourceId> _viewTargets = new();
    private readonly CaptureContext _captureContext;
    private GraphTextureStates? _preStates;
    private CommandBuffer? _passCommands;

    public GraphCapture(ICaptureProfiler hook, RenderContext context, RenderGraph graph, RenderGraph.PassNode[] nodes, PassInfo[] passInfos)
    {
        _hook = hook;
        _context = context;
        _graph = graph;
        _nodes = nodes;
        _passInfos = passInfos;
        _captureContext = new CaptureContext(this);
    }

    public void DescribeView(int viewIndex)
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

        ViewCaptureInfo info = new(viewIndex, resources.ToArray(), _passInfos);
        _hook.DescribeView(in info);
    }

    public void BeginPass(int index)
    {
        _preStates = _context.CurrentTextureStates;
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
        _live.Clear();
        _passCommands = commands;
        foreach (ReferencedResource referenced in commands.ReferencedResources)
            _firstReferenced[referenced.Buffer?.ResourceId ?? referenced.Texture!.ResourceId] = referenced;

        foreach (ResourceAccess access in _nodes[index].Accesses)
        {
            foreach (GraphBacking backing in _backings[access.Id])
            {
                if (backing.Role == BackingRole.Depth && access.DepthState(access.TextureUsage) is null)
                    continue;

                if (!_listed.Add(backing.Id))
                    continue;

                if (_firstReferenced.TryGetValue(backing.Id, out ReferencedResource referenced))
                    AddReference(referenced.FirstVersion, !referenced.Overwritten, access, backing);
                else
                    AddReference(new ResourceVersion(backing.Id, _passEntry[backing.Id]), false, access, backing);
            }
        }

        foreach (ReferencedResource referenced in commands.ReferencedResources)
        {
            if (!_listed.Add(referenced.Buffer?.ResourceId ?? referenced.Texture!.ResourceId))
                continue;

            if (referenced.Buffer is { } buffer)
            {
                _live[buffer.ResourceId] = (null, buffer);
                _references.Add(new PassReference(referenced.FirstVersion, buffer.CurrentVersion.Version, buffer.Name, null, buffer.DescriptionValue, !referenced.Overwritten));
            }
            else
            {
                _live[referenced.Texture!.ResourceId] = (referenced.Texture, null);
                _references.Add(new PassReference(
                    referenced.FirstVersion, referenced.Texture.CurrentVersion.Version, referenced.Texture.Name, referenced.Texture.DescriptionValue, null, !referenced.Overwritten));
            }
        }

        try
        {
            _hook.OnPassEnd(in _passInfos[index], CollectionsMarshal.AsSpan(_references), _captureContext);
        }
        finally
        {
            _live.Clear();
            _passCommands = null;
        }
    }

    internal CaptureCopy Copy(in PassReference reference, CopyPlacement placement)
    {
        ResourceId id = reference.First.Resource;
        if (_passCommands is not { } passCommands || !_live.TryGetValue(id, out (Texture? Texture, DeviceBuffer? Buffer) live))
            throw new ArgumentException("The reference does not belong to the pass being captured.", nameof(reference));

        if (_viewTargets.Contains(id))
            throw new InvalidOperationException("A view target backing cannot be copied.");

        ICaptureBackend backend = _context.Device.CaptureBackend;
        ResourceFactory factory = _context.Device.ResourceFactory;
        string name = live.Buffer?.Name ?? live.Texture!.Name;
        CommandBuffer cb;
        DeviceBuffer staging;
        CopyRegion[] regions = Array.Empty<CopyRegion>();

        if (live.Buffer is { } buffer)
        {
            staging = factory.CreateBuffer(new BufferDescription(buffer.SizeInBytes, BufferUsage.Staging));
            cb = _context.BeginCommandBuffer("Capture " + name);
            backend.RecordBufferCopy(cb, buffer, staging);
        }
        else
        {
            Texture texture = live.Texture!;
            regions = CaptureCopyPlanner.PlanTexture(texture.DescriptionValue, out uint size);
            staging = factory.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
            cb = _context.BeginCommandBuffer("Capture " + name);
            TextureState? before = placement == CopyPlacement.BeforePass ? _preStates?.StateOf(texture) : cb.StateOf(texture);
            backend.RecordTextureCopy(cb, texture, before, staging, regions);
        }

        staging.Name = $"Capture staging {name} v{(placement == CopyPlacement.BeforePass ? reference.First.Version : reference.LastVersion)}";
        if (placement == CopyPlacement.BeforePass)
            _context.EndCommandBufferAhead(cb, passCommands);
        else
            _context.EndCommandBuffer(cb);

        return new CaptureCopy(staging, regions);
    }

    private void AddReference(ResourceVersion first, bool needsContents, ResourceAccess access, GraphBacking backing)
    {
        if (!access.IsTexture)
        {
            DeviceBuffer buffer = _context.GetRenderBuffer(new BufferHandle(access.Id));
            _live[buffer.ResourceId] = (null, buffer);
            _references.Add(new PassReference(first, buffer.CurrentVersion.Version, buffer.Name, null, buffer.DescriptionValue, needsContents));
            return;
        }

        RenderTexture target = _context.GetRenderTexture(new TextureHandle(access.Id));
        Texture texture = backing.Role == BackingRole.Depth ? target.DepthTexture! : target.ColorTextures[(int)backing.Index];
        _live[texture.ResourceId] = (texture, null);
        _references.Add(new PassReference(first, texture.CurrentVersion.Version, texture.Name, texture.DescriptionValue, null, needsContents));
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
        int historyDepth = 0;
        int historySlot = -1;
        bool historyValid = false;

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
                    historyDepth = transient.HistoryDepth;
                    historySlot = transient.HistorySlot(_context.View.ViewId);
                    historyValid = _context.IsHistoryValid(new TextureHandle(id));
                    break;
                case GraphViewTargetResource:
                    origin = GraphResourceOrigin.ViewTarget;
                    foreach (GraphBacking backing in backings)
                        _viewTargets.Add(backing.Id);
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
            {
                bufferDesc = bufferResource.Description;
                historyDepth = bufferResource.HistoryDepth;
                historySlot = bufferResource.HistorySlot(_context.View.ViewId);
                historyValid = _context.IsHistoryValid(new BufferHandle(id));
            }
        }

        GraphBacking[] array = backings.ToArray();
        _backings[id] = array;
        return new GraphResourceInfo(
            id, name, isTexture ? GraphResourceKind.Texture : GraphResourceKind.Buffer, array, origin, textureDesc, bufferDesc, historyDepth, historySlot, historyValid);
    }
}
