using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Lets a pass declare reads/writes during setup. Graph uses this to allocate resources and order passes
/// (readers after writers). Inputs are ID only - the writer owns the description.
/// </summary>
public sealed class RenderContextBuilder
{
    internal readonly List<RenderResourceID> Inputs = new();
    internal readonly List<GraphResource> Outputs = new();

    internal void Reset()
    {
        Inputs.Clear();
        Outputs.Clear();
    }

    /// <summary>Declares a texture this pass samples. Writer owns the description.</summary>
    public TextureHandle GetInputTexture(RenderResourceID id)
    {
        Inputs.Add(id);
        return new TextureHandle(id);
    }

    /// <summary>
    /// Declares a texture this pass renders into, creating it if new. Non-zero history makes it a ring buffer
    /// of history+1 copies, rotated each execution so reads can pull prior frames by age.
    /// </summary>
    public TextureHandle GetOutputTexture(RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)
    {
        Outputs.Add(new GraphTextureResource(id, desc, history, ops));
        return new TextureHandle(id);
    }

    /// <summary>
    /// Imports an external render target under an ID so passes can read/order around it. Caller keeps ownership.
    /// </summary>
    public TextureHandle ImportTexture(RenderResourceID id, RenderTexture existing)
    {
        Outputs.Add(new GraphImportedTextureResource(id, existing));
        return new TextureHandle(id);
    }

    /// <summary>Declares a buffer this pass reads. Writer owns the description.</summary>
    public BufferHandle GetInputBuffer(RenderResourceID id)
    {
        Inputs.Add(id);
        return new BufferHandle(id);
    }

    /// <summary>
    /// Declares a buffer this pass writes, creating it if new. Non-zero history makes it a ring buffer of
    /// history+1 copies, rotated each execution so reads can pull prior frames by age.
    /// </summary>
    public BufferHandle GetOutputBuffer(RenderResourceID id, GraphBufferDesc desc, int history = 0)
    {
        Outputs.Add(new GraphBufferResource(id, desc, history));
        return new BufferHandle(id);
    }
}
