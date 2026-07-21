using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Passed to a present pass's setup to declare graph inputs and swapchain need. No outputs
/// allowed here, present is always the terminal step. Inputs are declared by ID only.
/// </summary>
public sealed class PresentContextBuilder
{
    internal readonly List<RenderResourceID> Inputs = new();
    internal bool RequestsSwapchain;

    /// <summary>Declares a texture this pass samples.</summary>
    public TextureHandle GetInputTexture(RenderResourceID id)
    {
        Inputs.Add(id);
        return new TextureHandle(id);
    }

    /// <summary>Declares a buffer this pass reads.</summary>
    public BufferHandle GetInputBuffer(RenderResourceID id)
    {
        Inputs.Add(id);
        return new BufferHandle(id);
    }

    /// <summary>Marks that this pass needs the swapchain target this run.</summary>
    public void RequestSwapchain() => RequestsSwapchain = true;
}
