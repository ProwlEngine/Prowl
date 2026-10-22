using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Per instance context for passes. Resolves the instance's resources and those of its ancestors; states are shared by the whole execution.
/// </summary>
public class RenderContext
{
    private readonly GraphExecution _execution;
    private readonly Dictionary<RenderResourceID, RenderTexture> _resolved = new();
    private readonly Dictionary<RenderResourceID, DeviceBuffer> _resolvedBuffers = new();

    private PassInfo? _currentPass;
    private ResourceAccess[]? _currentAccesses;

    private readonly HashSet<RenderResourceID> _declared = new();
    private readonly HashSet<RenderResourceID> _textureOutputs = new();
    private string? _currentScopeName;

    private bool[]? _enabled;
    private Dictionary<RenderResourceID, RenderResourceID>? _aliases;

    internal RenderContext(GraphExecution execution, GraphInstance instance)
    {
        _execution = execution;
        GraphInstance = instance;
    }

    /// <summary>Execution this context records into.</summary>
    public ExecutionTask Task => _execution.Task;

    internal GraphInstance GraphInstance { get; }

    internal GraphExecution Execution => _execution;

    internal GraphicsDevice Device => _execution.Device;

    internal RenderGraph Graph => _execution.Graph;

    internal bool HasInstanceTarget => GraphInstance.Target != null;

    internal ResourceAccess[]? CurrentAccesses => _currentAccesses;

    internal GraphTextureStates? CurrentTextureStates => _execution.CurrentTextureStates;

    internal void SetCurrentPass(in PassInfo? pass) => SetCurrentPass(pass, null, null);

    internal void SetCurrentPass(in PassInfo? pass, ResourceAccess[]? accesses, string? scopeName)
    {
        _currentPass = pass;
        _currentAccesses = accesses;
        _currentScopeName = scopeName;
        _declared.Clear();
        _textureOutputs.Clear();
        foreach (ResourceAccess access in accesses ?? [])
        {
            _declared.Add(access.Id);
            if (access.IsTexture && access.IsOutput)
                _textureOutputs.Add(access.Id);
        }
    }

    internal bool IsTextureOutput(ResourceAccess[] accesses, RenderResourceID id)
    {
        if (ReferenceEquals(accesses, _currentAccesses))
            return _textureOutputs.Contains(id);

        foreach (ResourceAccess access in accesses)
        {
            if (access.IsTexture && access.IsOutput && access.Id == id)
                return true;
        }
        return false;
    }

    internal bool IsTransient(RenderResourceID id)
        => Graph.Resources.TryGetValue(id, out GraphResource? resource) && resource is GraphTextureResource { Retained: false };

    internal void MarkAttachmentWrites(ResourceAccess[] accesses)
    {
        foreach (ResourceAccess access in accesses)
        {
            if (!access.IsTexture || !access.IsOutput)
                continue;

            RenderTexture texture = GetRenderTexture(new TextureHandle(access.Id));
            if ((access.TextureUsage & TextureState.Attachment) != 0)
            {
                foreach (Texture color in texture.ColorTextures)
                    color.MarkContentChanged();
            }

            if (texture.DepthTexture != null && (access.DepthAllowed & TextureState.Attachment) != 0)
                texture.DepthTexture.MarkContentChanged();
        }
    }

    private void CheckDeclared(RenderResourceID id)
    {
        if (_currentAccesses == null || _declared.Contains(id))
            return;

        throw new InvalidOperationException(
            $"Pass '{_currentScopeName}' uses resource '{RenderResourceID.ToString(id)}' without declaring it in Setup.");
    }

    internal CommandBuffer BeginCommandBuffer(string name) => _execution.BeginCommandBuffer(name, _currentPass);

    internal CommandBuffer BeginPassCommandBuffer(string passName) => _execution.BeginPassCommandBuffer(this, passName, _currentPass);

    internal void EndCommandBuffer(CommandBuffer cmd) => _execution.EndCommandBuffer(cmd);

    internal void EndCommandBufferAhead(CommandBuffer cmd, CommandBuffer before) => _execution.EndCommandBufferAhead(cmd, before);

    internal void BindDeclaredTarget(CommandBuffer cmd, ResourceAccess[] accesses)
    {
        RenderResourceID target = default;
        bool found = false;
        foreach (ResourceAccess access in accesses)
        {
            if (!access.IsTexture || !access.IsOutput || access.Entry != TextureState.Attachment)
                continue;

            if (found && access.Id != target)
                return;

            target = access.Id;
            found = true;
        }

        if (!found)
            return;

        cmd.SetFramebuffer(GetRenderTexture(new TextureHandle(target)).Framebuffer, GetTargetOps(target));
    }

    /// <summary>Allocates a transient uniform buffer range from this execution's bump allocator.</summary>
    /// <param name="sizeInBytes">Bytes to allocate.</param>
    public DeviceBufferRange AllocateTransient(uint sizeInBytes) => Task.AllocateTransientInternal(sizeInBytes);

    private RenderContext OwnerOf(RenderResourceID id)
    {
        if (!Graph.TryGetOwner(id, out GraphKind owner) || owner == GraphInstance.Kind)
            return this;

        for (GraphInstance? ancestor = GraphInstance.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor.Kind == owner)
                return _execution.ContextOf(ancestor);
        }

        throw new InvalidOperationException(
            $"Instance '{GraphInstance.Name}' uses resource '{RenderResourceID.ToString(id)}', which kind '{owner.Name}' owns, but no instance above it has that kind.");
    }

    private RenderContext Resolve(ref RenderResourceID id)
    {
        RenderContext context = OwnerOf(id);
        while (context._aliases != null && context._aliases.TryGetValue(id, out RenderResourceID source))
        {
            id = source;
            context = context.OwnerOf(source);
        }
        return context;
    }

    internal void Prepare()
    {
        IReadOnlyList<RenderGraph.PassNode> nodes = Graph.PassesOf(GraphInstance.Kind);
        _enabled = new bool[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
            _enabled[i] = nodes[i].Pass switch
            {
                IExecutionPass execution => execution.IsEnabled(),
                IInstancePass instance => instance.IsEnabled(GraphInstance),
                _ => true,
            };

        for (int i = 0; i < nodes.Count; i++)
        {
            if (_enabled[i])
                continue;

            foreach (PassthroughLink link in nodes[i].Passthroughs)
            {
                if (HasEnabledWriter(link.Output))
                    continue;

                _aliases ??= new Dictionary<RenderResourceID, RenderResourceID>();
                _aliases.TryAdd(link.Output, link.Input);
            }
        }
    }

    internal bool IsEnabled(RenderGraph.PassNode node) => _enabled![Graph.PositionOf(node.Index)];

    private bool HasEnabledWriter(RenderResourceID id)
    {
        foreach (int writer in Graph.WritersOf(id))
        {
            if (_enabled![Graph.PositionOf(writer)])
                return true;
        }
        return false;
    }

    internal void ValidateReads(IReadOnlyList<RenderGraph.PassNode> nodes)
    {
        foreach (RenderGraph.PassNode node in nodes)
        {
            if (!IsEnabled(node))
                continue;

            foreach (ResourceAccess access in node.Accesses)
            {
                if (!access.IsOutput || Graph.Resources[access.Id] is GraphTextureSlotResource or GraphBufferSlotResource)
                    CheckRead(node, access.Id);
            }
        }
    }

    private void CheckRead(RenderGraph.PassNode node, RenderResourceID read)
    {
        RenderResourceID id = read;
        RenderContext owner = OwnerOf(id);
        while (true)
        {
            if (!Graph.TryGetOwner(id, out _))
                return;

            if (Graph.Resources[id] is GraphTextureSlotResource or GraphBufferSlotResource)
            {
                if (!owner.GraphInstance.TryGetImport(id, out _))
                    throw new RenderGraphException(
                        $"Pass '{node.Pass.Name}' in instance '{GraphInstance.Name}' reads import slot '{RenderResourceID.ToString(id)}', which instance '{owner.GraphInstance.Name}' does not bind.", node.Pass);
                return;
            }

            if (owner.HasEnabledWriter(id))
                return;

            if (owner._aliases != null && owner._aliases.TryGetValue(id, out RenderResourceID source))
            {
                id = source;
                owner = owner.OwnerOf(source);
                continue;
            }

            List<string> disabled = new();
            foreach (int writer in Graph.WritersOf(id))
                disabled.Add($"'{Graph.NodeAt(writer).Pass.Name}'");
            throw new RenderGraphException(
                $"Pass '{node.Pass.Name}' in instance '{GraphInstance.Name}' reads resource '{RenderResourceID.ToString(read)}', but every pass that writes " +
                $"'{RenderResourceID.ToString(id)}' is disabled in instance '{owner.GraphInstance.Name}' ({string.Join(", ", disabled)}) and none passes another resource through.", node.Pass);
        }
    }

    /// <summary>Resolves a declared texture handle to its allocated render target.</summary>
    /// <param name="handle">Handle from the builder.</param>
    public RenderTexture GetRenderTexture(TextureHandle handle) => GetRenderTexture(handle, 0);

    /// <summary>
    /// Resolves a texture handle by age. 0 is current, higher values are older history copies up to declared depth.
    /// </summary>
    /// <param name="handle">Handle from the builder.</param>
    /// <param name="framesAgo">Executions back; 0 is current.</param>
    public RenderTexture GetRenderTexture(TextureHandle handle, int framesAgo)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default texture handle.", nameof(handle));

        CheckDeclared(handle.Id);
        RenderResourceID id = handle.Id;
        return Resolve(ref id).ResolveTexture(id, framesAgo);
    }

    private RenderTexture ResolveTexture(RenderResourceID id, int framesAgo)
    {
        if (framesAgo == 0 && _resolved.TryGetValue(id, out RenderTexture? existing))
            return existing;

        if (!Graph.Resources.TryGetValue(id, out GraphResource? resource))
            throw new InvalidOperationException($"Texture handle '{RenderResourceID.ToString(id)}' was not declared by any pass in this graph.");

        switch (resource)
        {
            case GraphInstanceTargetResource targetResource:
                if (framesAgo != 0)
                    throw new ArgumentOutOfRangeException(nameof(framesAgo), "The instance target has no history.");
                Framebuffer instanceTarget = GraphInstance.Target
                    ?? throw new InvalidOperationException($"A pass resolved the instance target, but instance '{GraphInstance.Name}' has none.");
                if (targetResource.DepthFormat is PixelFormat requiredDepth)
                    instanceTarget.OwningSwapchain?.RequireDepth(requiredDepth);
                if (targetResource.DepthFormat != null && instanceTarget.DepthTarget == null)
                    throw new InvalidOperationException($"A pass declared an instance target depth attachment, but instance '{GraphInstance.Name}' has a Target without one.");
                RenderTexture target = new(instanceTarget);
                _resolved[id] = target;
                return target;

            case GraphImportedTextureResource imported:
                if (framesAgo != 0)
                    throw new ArgumentOutOfRangeException(nameof(framesAgo), "An imported texture has no history.");
                _resolved[id] = imported.Texture;
                return imported.Texture;

            case GraphTextureSlotResource:
                if (framesAgo != 0)
                    throw new ArgumentOutOfRangeException(nameof(framesAgo), "An import slot has no history.");
                RenderTexture slotted = GraphInstance.TryGetImport(id, out GraphImport slotTexture) && slotTexture.Texture != null
                    ? slotTexture.Texture
                    : throw new InvalidOperationException($"Instance '{GraphInstance.Name}' binds no texture to import slot '{RenderResourceID.ToString(id)}'.");
                _resolved[id] = slotted;
                return slotted;

            case GraphTextureResource { Retained: false } textureResource:
                if (framesAgo != 0)
                    throw new ArgumentOutOfRangeException(nameof(framesAgo), $"Resource '{RenderResourceID.ToString(id)}' was not declared with history.");
                RenderTexture rented = Device.RentGraphTransientRenderTexture(Task, textureResource.Allocation(GraphInstance));
                _resolved[id] = rented;
                return rented;

            case GraphTextureResource historyResource:
                RenderTexture copy = historyResource.ResolveHistory(Device, GraphInstance.Key, Task.Id, framesAgo, historyResource.Allocation(GraphInstance));
                if (framesAgo == 0)
                    _resolved[id] = copy;
                return copy;

            default:
                throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(id)}' is not a texture. Resolve it with GetRenderBuffer.");
        }
    }

    /// <summary>True once the history ring holds an earlier execution. False on an instance's first execution and after a resize reallocates its ring.</summary>
    /// <param name="handle">Handle from the builder.</param>
    public bool IsHistoryValid(TextureHandle handle)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default texture handle.", nameof(handle));

        if (!Graph.Resources.TryGetValue(handle.Id, out GraphResource? resource))
            throw new InvalidOperationException($"Texture handle '{RenderResourceID.ToString(handle.Id)}' was not declared by any pass in this graph.");

        RenderContext owner = OwnerOf(handle.Id);
        return resource is GraphTextureResource texture
            && texture.IsHistoryValid(owner.GraphInstance.Key, Task.Id, texture.Allocation(owner.GraphInstance));
    }

    /// <summary>Resolves a declared buffer handle to its allocated device buffer.</summary>
    /// <param name="handle">Handle from the builder.</param>
    public DeviceBuffer GetRenderBuffer(BufferHandle handle) => GetRenderBuffer(handle, 0);

    /// <summary>
    /// Resolves a buffer handle by age. 0 is current, higher values are older history copies up to declared depth.
    /// </summary>
    /// <param name="handle">Handle from the builder.</param>
    /// <param name="framesAgo">Executions back; 0 is current.</param>
    public DeviceBuffer GetRenderBuffer(BufferHandle handle, int framesAgo)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default buffer handle.", nameof(handle));

        CheckDeclared(handle.Id);
        RenderResourceID id = handle.Id;
        return Resolve(ref id).ResolveBuffer(id, framesAgo);
    }

    private DeviceBuffer ResolveBuffer(RenderResourceID id, int framesAgo)
    {
        if (framesAgo == 0 && _resolvedBuffers.TryGetValue(id, out DeviceBuffer? existing))
            return existing;

        if (!Graph.Resources.TryGetValue(id, out GraphResource? resource))
            throw new InvalidOperationException($"Buffer handle '{RenderResourceID.ToString(id)}' was not declared by any pass in this graph.");

        if (resource is GraphImportedBufferResource imported)
        {
            if (framesAgo != 0)
                throw new ArgumentOutOfRangeException(nameof(framesAgo), $"Imported buffer '{RenderResourceID.ToString(id)}' has no history.");
            return imported.Buffer;
        }

        if (resource is GraphBufferSlotResource)
        {
            if (framesAgo != 0)
                throw new ArgumentOutOfRangeException(nameof(framesAgo), $"Import slot '{RenderResourceID.ToString(id)}' has no history.");
            return GraphInstance.TryGetImport(id, out GraphImport slotBuffer) && slotBuffer.Buffer != null
                ? slotBuffer.Buffer
                : throw new InvalidOperationException($"Instance '{GraphInstance.Name}' binds no buffer to import slot '{RenderResourceID.ToString(id)}'.");
        }

        if (resource is not GraphBufferResource bufferResource)
            throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(id)}' is not a buffer. Resolve it with GetRenderTexture.");

        if (!bufferResource.Retained)
        {
            if (framesAgo != 0)
                throw new ArgumentOutOfRangeException(nameof(framesAgo), $"Resource '{RenderResourceID.ToString(id)}' was not declared with history.");
            DeviceBuffer rented = Device.RentTransientBuffer(Task, bufferResource.Description.ToBufferDescription());
            _resolvedBuffers[id] = rented;
            return rented;
        }

        DeviceBuffer copy = bufferResource.ResolveHistory(Device, GraphInstance.Key, Task.Id, framesAgo, bufferResource.Description.ToBufferDescription());
        _execution.RecordResizes();
        if (framesAgo == 0)
            _resolvedBuffers[id] = copy;
        return copy;
    }

    /// <summary>True once the history ring holds an earlier execution. False on an instance's first execution and after a resize reallocates its ring.</summary>
    /// <param name="handle">Handle from the builder.</param>
    public bool IsHistoryValid(BufferHandle handle)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Cannot resolve a default buffer handle.", nameof(handle));

        if (!Graph.Resources.TryGetValue(handle.Id, out GraphResource? resource))
            throw new InvalidOperationException($"Buffer handle '{RenderResourceID.ToString(handle.Id)}' was not declared by any pass in this graph.");

        RenderContext owner = OwnerOf(handle.Id);
        return resource is GraphBufferResource buffer
            && buffer.IsHistoryValid(owner.GraphInstance.Key, Task.Id, buffer.Description.ToBufferDescription());
    }

    internal bool IsTextureResource(RenderResourceID id)
        => Graph.Resources.TryGetValue(id, out GraphResource? resource)
            && resource is GraphTextureResource or GraphImportedTextureResource or GraphTextureSlotResource or GraphInstanceTargetResource;

    internal TargetLoadStoreOps GetTargetOps(RenderResourceID id)
    {
        if (_currentAccesses != null)
        {
            foreach (ResourceAccess access in _currentAccesses)
            {
                if (access.Id != id || access.Description is not { } declared)
                    continue;

                switch (declared)
                {
                    case GraphTextureResource texture:
                        return texture.Ops;
                    case GraphImportedTextureResource imported:
                        return imported.Ops;
                    case GraphTextureSlotResource slot:
                        return slot.Ops;
                    case GraphInstanceTargetResource instanceTarget:
                        return instanceTarget.Ops;
                }
            }
        }

        if (!Graph.Resources.TryGetValue(id, out GraphResource? resource))
            throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(id)}' was not declared by any pass in this graph.");

        return resource switch
        {
            GraphTextureResource texture => texture.Ops,
            GraphImportedTextureResource imported => imported.Ops,
            GraphTextureSlotResource slot => slot.Ops,
            GraphInstanceTargetResource instanceTarget => instanceTarget.Ops,
            _ => throw new InvalidOperationException($"Resource '{RenderResourceID.ToString(id)}' is not a render target.")
        };
    }
}

/// <summary>The context an instance pass gets. Adds the instance being run to what an execution pass has.</summary>
public sealed class InstanceContext : RenderContext
{
    internal InstanceContext(GraphExecution execution, GraphInstance instance) : base(execution, instance) { }

    /// <summary>Instance being run.</summary>
    public GraphInstance Instance => GraphInstance;
}
