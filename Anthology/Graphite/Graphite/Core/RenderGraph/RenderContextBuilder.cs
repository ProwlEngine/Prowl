using System;
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
    public TextureHandle DeclareInputTexture(
        RenderResourceID id,
        TextureUsageKind usage = TextureUsageKind.Sampled,
        TextureUsageKind? depthUsage = null)
    {
        Accesses.Add(ResourceAccess.Texture(id, usage, depthUsage, isOutput: false));
        Inputs.Add(id);
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
        TextureUsageKind usage = TextureUsageKind.Attachment,
        TextureUsageKind? depthUsage = null)
    {
        Accesses.Add(ResourceAccess.Texture(id, usage, depthUsage, isOutput: true));
        Outputs.Add(new GraphTextureResource(id, desc, history, ops));
        return new TextureHandle(id);
    }

    /// <summary>
    /// Imports an external render target under an ID so passes can read/order around it. Caller keeps ownership.
    /// </summary>
    public TextureHandle DeclareImportedTexture(
        RenderResourceID id,
        RenderTexture existing,
        TextureUsageKind usage = TextureUsageKind.Attachment,
        TextureUsageKind? depthUsage = null)
    {
        Accesses.Add(ResourceAccess.Texture(id, usage, depthUsage, isOutput: true));
        Outputs.Add(new GraphImportedTextureResource(id, existing));
        return new TextureHandle(id);
    }

    /// <summary>Declares a buffer this pass reads. Writer owns the description.</summary>
    public BufferHandle DeclareInputBuffer(RenderResourceID id, BufferUsageKind usage = BufferUsageKind.AnyRead)
    {
        Accesses.Add(ResourceAccess.Buffer(id, usage, isOutput: false));
        Inputs.Add(id);
        return new BufferHandle(id);
    }

    /// <summary>
    /// Declares a buffer this pass writes, creating it if new. Non-zero history makes it a ring buffer of
    /// history+1 copies, rotated each execution so reads can pull prior frames by age.
    /// </summary>
    public BufferHandle DeclareOutputBuffer(RenderResourceID id, GraphBufferDesc desc, int history = 0, BufferUsageKind usage = BufferUsageKind.Storage)
    {
        Accesses.Add(ResourceAccess.Buffer(id, usage, isOutput: true));
        Outputs.Add(new GraphBufferResource(id, desc, history));
        return new BufferHandle(id);
    }

    /// <summary>
    /// Declares a write to the current view's target: <see cref="IRenderView.TargetFramebuffer"/>, or the main swapchain image
    /// when <see cref="IRenderView.TargetSwapchain"/> is set, which presents after dispatch. The pass is skipped for a view with
    /// neither. Clears by default; pass Loaded ops for a pass that draws over an earlier view target pass.
    /// </summary>
    public TextureHandle DeclareViewTarget(
        TargetLoadStoreOps? ops = null,
        TextureUsageKind usage = TextureUsageKind.Attachment)
    {
        if ((usage & ~(TextureUsageKind.Attachment | TextureUsageKind.TransferDst)) != 0)
            throw new ArgumentException($"The view target only supports Attachment and TransferDst, not {usage}.", nameof(usage));

        Accesses.Add(ResourceAccess.Texture(GraphViewTargetResource.ViewTargetId, usage, null, isOutput: true));
        Outputs.Add(new GraphViewTargetResource(ops));
        return new TextureHandle(GraphViewTargetResource.ViewTargetId);
    }
}
