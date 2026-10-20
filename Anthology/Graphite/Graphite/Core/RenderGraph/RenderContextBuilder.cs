using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Lets a pass declare reads/writes during setup. Graph uses this to allocate resources, order passes
/// (readers after writers) and plan the state each resource is in when the pass runs.
/// </summary>
public sealed class RenderContextBuilder
{
    internal readonly List<ResourceAccess> Accesses = new();

    internal void Reset()
    {
        Accesses.Clear();
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
    /// Declares a texture this pass writes, creating it if new. Non-zero history makes it a ring buffer
    /// of history+1 copies, rotated each execution so reads can pull prior frames by age.
    /// </summary>
    public TextureHandle DeclareOutputTexture(
        RenderResourceID id,
        GraphTextureDesc desc,
        int history = 0,
        TargetLoadStoreOps? ops = null,
        TextureState usage = TextureState.Attachment,
        TextureState? depthUsage = null)
    {
        Accesses.Add(ResourceAccess.Texture(id, usage, depthUsage, isOutput: true, new GraphTextureResource(id, desc, history, ops)));
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

    /// <summary>
    /// Declares a buffer this pass writes, creating it if new. Non-zero history makes it a ring buffer of
    /// history+1 copies, rotated each execution so reads can pull prior frames by age.
    /// </summary>
    public BufferHandle DeclareOutputBuffer(RenderResourceID id, GraphBufferDesc desc, int history = 0, BufferAccess usage = BufferAccess.ShaderRead | BufferAccess.ShaderWrite)
    {
        Accesses.Add(ResourceAccess.Buffer(id, usage, isOutput: true, new GraphBufferResource(id, desc, history)));
        return new BufferHandle(id);
    }

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
