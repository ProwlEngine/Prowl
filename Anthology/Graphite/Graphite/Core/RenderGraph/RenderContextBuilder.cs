using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Lets a pass declare reads/writes during setup. Graph uses this to allocate resources, order passes
/// (readers after writers) and plan the state each resource is in when the pass runs.
/// </summary>
public class RenderContextBuilder
{
    internal readonly List<ResourceAccess> Accesses = new();
    internal readonly List<PassthroughLink> Passthroughs = new();

    internal RenderContextBuilder() { }

    internal virtual bool AllowsInstanceSizes => false;

    internal void Reset()
    {
        Accesses.Clear();
        Passthroughs.Clear();
    }

    /// <summary>Declares a texture this pass reads. Writer owns the description.</summary>
    public TextureHandle DeclareInputTexture(
        RenderResourceID id,
        TextureState usage = TextureState.Sampled,
        TextureState? depthUsage = null)
    {
        Accesses.Add(ResourceAccess.Texture(id, usage, depthUsage, isOutput: false));
        return new TextureHandle(id);
    }

    /// <summary>
    /// Declares a texture this pass writes, creating it if new. History keeps a ring of history+1 copies rotated each execution;
    /// persistent keeps one copy whose contents carry across executions.
    /// </summary>
    public TextureHandle DeclareOutputTexture(
        RenderResourceID id,
        GraphTextureDesc desc,
        int history = 0,
        TargetLoadStoreOps? ops = null,
        TextureState usage = TextureState.Attachment,
        TextureState? depthUsage = null,
        bool persistent = false)
    {
        if (persistent && history > 0)
            throw new ArgumentException("A resource is persistent or has history, not both.", nameof(persistent));
        if (desc.SizeMode == TextureSizeMode.InstanceRelative && !AllowsInstanceSizes)
            throw new ArgumentException("An execution pass cannot size a texture relative to an instance.", nameof(desc));
        Accesses.Add(ResourceAccess.Texture(id, usage, depthUsage, isOutput: true, new GraphTextureResource(id, desc, history, ops, persistent)));
        return new TextureHandle(id);
    }

    /// <summary>Declares another write to a texture an earlier pass declared, for read-modify-write without describing it again.</summary>
    public TextureHandle DeclareWriteTexture(RenderResourceID id, TextureState usage = TextureState.Attachment, TextureState? depthUsage = null)
    {
        Accesses.Add(ResourceAccess.Texture(id, usage, depthUsage, isOutput: true));
        return new TextureHandle(id);
    }

    /// <summary>
    /// Imports an external render target under an ID so passes can read/order around it. Caller keeps ownership.
    /// </summary>
    public TextureHandle DeclareImportedTexture(
        RenderResourceID id,
        RenderTexture existing,
        TextureState usage = TextureState.Attachment,
        TextureState? depthUsage = null)
    {
        Accesses.Add(ResourceAccess.Texture(id, usage, depthUsage, isOutput: true, new GraphImportedTextureResource(id, existing)));
        return new TextureHandle(id);
    }

    /// <summary>Declares a buffer this pass reads. Writer owns the description.</summary>
    public BufferHandle DeclareInputBuffer(RenderResourceID id, BufferAccess usage = BufferAccess.AllReads)
    {
        Accesses.Add(ResourceAccess.Buffer(id, usage, isOutput: false));
        return new BufferHandle(id);
    }

    /// <summary>
    /// Declares an existing buffer this pass reads or writes, for buffers that outlive the graph such as ones another
    /// pipeline in the execution wrote. Usage with a write flag makes it an output, otherwise an input.
    /// </summary>
    public BufferHandle DeclareImportedBuffer(RenderResourceID id, DeviceBuffer existing, BufferAccess usage = BufferAccess.AllReads)
    {
        ArgumentNullException.ThrowIfNull(existing);
        Accesses.Add(ResourceAccess.Buffer(id, usage, isOutput: (usage & BufferAccess.AllWrites) != 0, new GraphImportedBufferResource(id, existing)));
        return new BufferHandle(id);
    }

    /// <summary>Declares another write to a buffer an earlier pass declared, for read-modify-write without describing it again.</summary>
    public BufferHandle DeclareWriteBuffer(RenderResourceID id, BufferAccess usage = BufferAccess.ShaderRead | BufferAccess.ShaderWrite)
    {
        Accesses.Add(ResourceAccess.Buffer(id, usage, isOutput: true));
        return new BufferHandle(id);
    }

    /// <summary>
    /// Declares a buffer this pass writes, creating it if new. History keeps a ring of history+1 copies rotated each execution;
    /// persistent keeps one copy whose contents carry across executions and survive a change of size.
    /// </summary>
    public BufferHandle DeclareOutputBuffer(RenderResourceID id, GraphBufferDesc desc, int history = 0, BufferAccess usage = BufferAccess.ShaderRead | BufferAccess.ShaderWrite, bool persistent = false)
    {
        if (persistent && history > 0)
            throw new ArgumentException("A resource is persistent or has history, not both.", nameof(persistent));
        Accesses.Add(ResourceAccess.Buffer(id, usage, isOutput: true, new GraphBufferResource(id, desc, history, persistent)));
        return new BufferHandle(id);
    }

    /// <summary>
    /// Declares a texture the instance binds at dispatch under this id, for textures the caller owns and changes between executions.
    /// A write usage makes it an output, otherwise an input.
    /// </summary>
    public TextureHandle DeclareImportSlotTexture(
        RenderResourceID id,
        TextureState usage = TextureState.Sampled,
        TextureState? depthUsage = null,
        TargetLoadStoreOps? ops = null)
    {
        bool isOutput = (usage & (TextureState.Attachment | TextureState.Storage | TextureState.TransferDst)) != 0;
        Accesses.Add(ResourceAccess.Texture(id, usage, depthUsage, isOutput, new GraphTextureSlotResource(id, ops)));
        return new TextureHandle(id);
    }

    /// <summary>
    /// Declares a buffer the instance binds at dispatch under this id, for buffers the caller owns and changes between executions.
    /// Usage with a write flag makes it an output, otherwise an input.
    /// </summary>
    public BufferHandle DeclareImportSlotBuffer(RenderResourceID id, BufferAccess usage = BufferAccess.AllReads)
    {
        Accesses.Add(ResourceAccess.Buffer(id, usage, isOutput: (usage & BufferAccess.AllWrites) != 0, new GraphBufferSlotResource(id)));
        return new BufferHandle(id);
    }

    /// <summary>
    /// Says that when this pass is disabled for an instance, reading the output gives the input instead. Both must be
    /// transient textures that match, and the pass must read the input and write the output.
    /// </summary>
    public void Passthrough(TextureHandle input, TextureHandle output)
    {
        Passthroughs.Add(new PassthroughLink(input.Id, output.Id, true));
    }

    /// <summary>
    /// Says that when this pass is disabled for an instance, reading the output gives the input instead. Both must be
    /// transient buffers that match, and the pass must read the input and write the output.
    /// </summary>
    public void Passthrough(BufferHandle input, BufferHandle output)
    {
        Passthroughs.Add(new PassthroughLink(input.Id, output.Id, false));
    }
}

internal readonly record struct PassthroughLink(RenderResourceID Input, RenderResourceID Output, bool IsTexture);

/// <summary>The builder an instance pass declares with. Adds what only an instance has: its target and sizes relative to it.</summary>
public sealed class InstanceContextBuilder : RenderContextBuilder
{
    internal InstanceContextBuilder() { }

    internal override bool AllowsInstanceSizes => true;

    /// <summary>
    /// Declares a write to <see cref="GraphInstance.Target"/>, presented after dispatch when it is a swapchain's. Skipped for an instance with none.
    /// Clears by default. A depth format gives the target depth, made on demand for a swapchain; any other target must have it.
    /// </summary>
    public TextureHandle DeclareInstanceTarget(
        TargetLoadStoreOps? ops = null,
        TextureState usage = TextureState.Attachment,
        PixelFormat? depthFormat = null)
    {
        if (usage is not (TextureState.Attachment or TextureState.TransferDst))
            throw new ArgumentException($"The instance target only supports Attachment and TransferDst, not {usage}.", nameof(usage));

        Accesses.Add(ResourceAccess.Texture(GraphInstanceTargetResource.InstanceTargetId, usage, null, isOutput: true, new GraphInstanceTargetResource(ops, depthFormat)));
        return new TextureHandle(GraphInstanceTargetResource.InstanceTargetId);
    }
}
