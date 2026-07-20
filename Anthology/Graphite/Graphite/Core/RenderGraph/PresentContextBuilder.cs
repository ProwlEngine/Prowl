using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Handed to a present pass's setup so it can declare textures it reads from the graph and whether it
/// needs the window's swapchain this run. Unlike <see cref="RenderContextBuilder"/>, a present pass
/// cannot declare output textures: it is the terminal step of the graph, not a producer other passes
/// can depend on.
/// </summary>
public sealed class PresentContextBuilder
{
    internal readonly List<ResourceDecl> Inputs = new();
    internal bool RequestsSwapchain;

    /// <summary>Declares a texture this present pass samples (e.g. to composite into the swapchain).</summary>
    public TextureHandle GetInputTexture(RenderResourceID id, GraphTextureDesc desc)
    {
        Inputs.Add(new ResourceDecl(id, desc));
        return new TextureHandle(id);
    }

    /// <summary>Declares that this present pass needs the window's swapchain target this run.</summary>
    public void RequestSwapchain() => RequestsSwapchain = true;
}
