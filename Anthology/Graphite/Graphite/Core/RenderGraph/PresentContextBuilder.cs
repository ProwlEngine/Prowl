using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Handed to a present pass's setup so it can declare resources it reads from the graph and whether it
/// needs the window's swapchain this run. Unlike <see cref="RenderContextBuilder"/>, a present pass cannot
/// declare outputs: it is the terminal step of the graph, not a producer other passes can depend on.
/// Inputs are declared by ID only; the producing pass owns the description.
/// </summary>
public sealed class PresentContextBuilder
{
    internal readonly List<RenderResourceID> Inputs = new();
    internal bool RequestsSwapchain;

    /// <summary>Declares a texture this present pass samples (e.g. to composite into the swapchain).</summary>
    public TextureHandle GetInputTexture(RenderResourceID id)
    {
        Inputs.Add(id);
        return new TextureHandle(id);
    }

    /// <summary>Declares a buffer this present pass reads.</summary>
    public BufferHandle GetInputBuffer(RenderResourceID id)
    {
        Inputs.Add(id);
        return new BufferHandle(id);
    }

    /// <summary>Declares that this present pass needs the window's swapchain target this run.</summary>
    public void RequestSwapchain() => RequestsSwapchain = true;
}
