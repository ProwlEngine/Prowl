using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite;

internal sealed class CaptureContext : ICaptureContext
{
    private readonly GraphCapture _owner;

    public CaptureContext(GraphCapture owner) => _owner = owner;

    public CaptureCopy Copy(in PassReference reference, CopyPlacement placement) => _owner.Copy(in reference, placement);
}

/// <summary>Per group state that turns graph execution into capture hook callbacks.</summary>
internal sealed class GraphCapture
{
    private readonly ICaptureProfiler _hook;
    private readonly GraphExecution _execution;
    private readonly GraphLayout _layout;
    private readonly int[] _members;
    private readonly Dictionary<int, GraphBacking[]> _backings = new();
    private readonly Dictionary<ResourceId, uint> _passEntry = new();
    private readonly Dictionary<ResourceId, ReferencedResource> _firstReferenced = new();
    private readonly HashSet<ResourceId> _listed = new();
    private readonly List<PassReference> _references = new();
    private readonly Dictionary<ResourceId, (Texture? Texture, DeviceBuffer? Buffer)> _live = new();
    private readonly HashSet<ResourceId> _instanceTargets = new();
    private readonly CaptureContext _captureContext;
    private GraphTextureStates? _preStates;
    private CommandBuffer? _passCommands;

    public GraphCapture(ICaptureProfiler hook, GraphExecution execution, GraphLayout layout, int[] members)
    {
        _hook = hook;
        _execution = execution;
        _layout = layout;
        _members = members;
        _captureContext = new CaptureContext(this);
    }

    public void DescribeGroup(int groupIndex)
    {
        List<GraphResourceInfo> resources = new();
        HashSet<int> seen = new();
        PassInfo[] passes = new PassInfo[_members.Length];
        for (int i = 0; i < _members.Length; i++)
        {
            passes[i] = _layout.PassInfos[_members[i]];
            foreach (ResourceAccess access in _execution.Graph.PassAt(_members[i]).Accesses)
            {
                if (seen.Add(access.Resource))
                    resources.Add(DescribeResource(access.Resource));
            }
        }

        GroupCaptureInfo info = new(groupIndex, resources.ToArray(), passes);
        _hook.DescribeGroup(in info);
    }

    public void BeginPass(int index)
    {
        _preStates = _execution.CurrentTextureStates;
        _passEntry.Clear();
        foreach (ResourceAccess access in _execution.Graph.PassAt(index).Accesses)
        {
            foreach (GraphBacking backing in _backings[access.Resource])
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

        foreach (ResourceAccess access in _execution.Graph.PassAt(index).Accesses)
        {
            foreach (GraphBacking backing in _backings[access.Resource])
            {
                if (backing.Role == BackingRole.Depth && access.DepthAllowed == 0)
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
            _hook.OnPassEnd(in _layout.PassInfos[index], CollectionsMarshal.AsSpan(_references), _captureContext);
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

        if (_instanceTargets.Contains(id))
            throw new InvalidOperationException("A target backing cannot be copied.");

        ICaptureBackend backend = _execution.Device.CaptureBackend;
        ResourceFactory factory = _execution.Device.ResourceFactory;
        string name = live.Buffer?.Name ?? live.Texture!.Name;
        CommandBuffer cb;
        DeviceBuffer staging;
        CopyRegion[] regions = Array.Empty<CopyRegion>();

        if (live.Buffer is { } buffer)
        {
            staging = factory.CreateBuffer(new BufferDescription(buffer.SizeInBytes, BufferUsage.Staging));
            cb = _execution.BeginCommandBuffer(CaptureCopy.BufferPrefix + name, null);
            backend.RecordBufferCopy(cb, buffer, staging);
        }
        else
        {
            Texture texture = live.Texture!;
            regions = CaptureCopyPlanner.PlanTexture(texture.DescriptionValue, out uint size);
            staging = factory.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
            cb = _execution.BeginCommandBuffer(CaptureCopy.BufferPrefix + name, null);
            TextureState? before = placement == CopyPlacement.BeforePass ? _preStates?.StateOf(texture) : cb.StateOf(texture);
            backend.RecordTextureCopy(cb, texture, before, staging, regions);
        }

        staging.Name = $"Capture staging {name} v{(placement == CopyPlacement.BeforePass ? reference.First.Version : reference.LastVersion)}";
        if (placement == CopyPlacement.BeforePass)
            _execution.EndCommandBufferAhead(cb, passCommands);
        else
            _execution.EndCommandBuffer(cb);

        return new CaptureCopy(staging, regions);
    }

    private void AddReference(ResourceVersion first, bool needsContents, ResourceAccess access, GraphBacking backing)
    {
        if (!access.IsTexture)
        {
            DeviceBuffer buffer = _execution.BufferAt(access.Resource);
            _live[buffer.ResourceId] = (null, buffer);
            _references.Add(new PassReference(first, buffer.CurrentVersion.Version, buffer.Name, null, buffer.DescriptionValue, needsContents));
            return;
        }

        RenderTexture target = _execution.TextureAt(access.Resource);
        Texture texture = backing.Role == BackingRole.Depth ? target.DepthTexture! : target.ColorTextures[(int)backing.Index];
        _live[texture.ResourceId] = (texture, null);
        _references.Add(new PassReference(first, texture.CurrentVersion.Version, texture.Name, texture.DescriptionValue, null, needsContents));
    }

    private ResourceVersion ResolveBacking(ResourceAccess access, GraphBacking backing)
    {
        if (!access.IsTexture)
            return _execution.BufferAt(access.Resource).CurrentVersion;

        RenderTexture texture = _execution.TextureAt(access.Resource);
        return (backing.Role == BackingRole.Depth ? texture.DepthTexture! : texture.ColorTextures[(int)backing.Index]).CurrentVersion;
    }

    private GraphResourceInfo DescribeResource(int index)
    {
        FrameResource resource = _execution.Graph.ResourceAt(index);
        bool isTexture = resource.IsTexture;
        List<GraphBacking> backings = new();
        GraphTextureDesc? textureDesc = null;
        GraphBufferDesc? bufferDesc = null;
        GraphResourceOrigin origin = resource.IsTarget ? GraphResourceOrigin.Target
            : resource.Imported ? GraphResourceOrigin.Imported
            : resource.Lifetime.IsPersistent ? GraphResourceOrigin.Persistent
            : GraphResourceOrigin.Transient;

        if (isTexture)
        {
            RenderTexture texture = _execution.TextureAt(index);
            for (int i = 0; i < texture.ColorTextures.Length; i++)
                backings.Add(new GraphBacking(texture.ColorTextures[i].ResourceId, texture.ColorTextures[i].CurrentVersion, BackingRole.Color, (uint)i));
            if (texture.DepthTexture is { } depth)
                backings.Add(new GraphBacking(depth.ResourceId, depth.CurrentVersion, BackingRole.Depth, 0));

            if (resource.IsTarget)
            {
                foreach (GraphBacking backing in backings)
                    _instanceTargets.Add(backing.Id);
            }
            else if (!resource.Imported)
            {
                textureDesc = resource.Texture;
            }
        }
        else
        {
            DeviceBuffer buffer = _execution.BufferAt(index);
            backings.Add(new GraphBacking(buffer.ResourceId, buffer.CurrentVersion, BackingRole.Buffer, 0));
            bufferDesc = resource.Imported ? GraphBufferDesc.Of(buffer.SizeInBytes, buffer.Usage) : resource.Buffer;
        }

        GraphBacking[] array = backings.ToArray();
        _backings[index] = array;
        return new GraphResourceInfo(
            resource.Id, resource.Name, resource.Kind, array, origin, textureDesc, bufferDesc,
            resource.Lifetime.HistoryFrames, _execution.HistorySlot(index), _execution.IsHistoryValid(index));
    }
}
