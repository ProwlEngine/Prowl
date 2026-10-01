using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Lets a pass declare reads/writes during setup. Graph uses this to allocate resources, order passes
/// (readers after writers) and plan the state each resource is in when the pass runs.
/// </summary>
public sealed class RenderContextBuilder
{
    internal readonly List<RenderResourceID> Inputs = new();
    internal readonly List<GraphResource> Outputs = new();
    internal readonly List<ResourceAccess> Accesses = new();

    internal void Reset()
    {
        Inputs.Clear();
        Outputs.Clear();
        Accesses.Clear();
    }

    /// <summary>Declares a texture this pass reads. Writer owns the description.</summary>
    public TextureHandle GetInputTexture(
        RenderResourceID id,
        TextureUsageKind usage = TextureUsageKind.Sampled,
        TextureUsageKind? initial = null,
        TextureUsageKind? depthUsage = null)
    {
        Accesses.Add(ResourceAccess.Texture(id, usage, initial, depthUsage, isOutput: false));
        Inputs.Add(id);
        return new TextureHandle(id);
    }

    /// <summary>
    /// Declares a texture this pass writes, creating it if new. Non-zero history makes it a ring buffer
    /// of history+1 copies, rotated each execution so reads can pull prior frames by age.
    /// </summary>
    public TextureHandle GetOutputTexture(
        RenderResourceID id,
        GraphTextureDesc desc,
        int history = 0,
        TargetLoadStoreOps? ops = null,
        TextureUsageKind usage = TextureUsageKind.Attachment,
        TextureUsageKind? initial = null,
        TextureUsageKind? depthUsage = null)
    {
        Accesses.Add(ResourceAccess.Texture(id, usage, initial, depthUsage, isOutput: true));
        Outputs.Add(new GraphTextureResource(id, desc, history, ops));
        return new TextureHandle(id);
    }

    /// <summary>
    /// Imports an external render target under an ID so passes can read/order around it. Caller keeps ownership.
    /// </summary>
    public TextureHandle ImportTexture(
        RenderResourceID id,
        RenderTexture existing,
        TextureUsageKind usage = TextureUsageKind.Attachment,
        TextureUsageKind? initial = null,
        TextureUsageKind? depthUsage = null)
    {
        Accesses.Add(ResourceAccess.Texture(id, usage, initial, depthUsage, isOutput: true));
        Outputs.Add(new GraphImportedTextureResource(id, existing));
        return new TextureHandle(id);
    }

    /// <summary>Declares a buffer this pass reads. Writer owns the description.</summary>
    public BufferHandle GetInputBuffer(RenderResourceID id, BufferUsageKind usage = BufferUsageKind.AnyRead)
    {
        Accesses.Add(ResourceAccess.Buffer(id, usage, isOutput: false));
        Inputs.Add(id);
        return new BufferHandle(id);
    }

    /// <summary>
    /// Declares a buffer this pass writes, creating it if new. Non-zero history makes it a ring buffer of
    /// history+1 copies, rotated each execution so reads can pull prior frames by age.
    /// </summary>
    public BufferHandle GetOutputBuffer(RenderResourceID id, GraphBufferDesc desc, int history = 0, BufferUsageKind usage = BufferUsageKind.Storage)
    {
        Accesses.Add(ResourceAccess.Buffer(id, usage, isOutput: true));
        Outputs.Add(new GraphBufferResource(id, desc, history));
        return new BufferHandle(id);
    }
}
