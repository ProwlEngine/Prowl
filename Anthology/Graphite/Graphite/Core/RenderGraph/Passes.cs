namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Minimal size info for view-relative render targets. Concrete views add richer data.
/// </summary>
public interface IRenderView
{
    /// <summary>Width in pixels.</summary>
    uint PixelWidth { get; }

    /// <summary>Height in pixels.</summary>
    uint PixelHeight { get; }

    /// <summary>Stable identity across frames. Temporal history rings are kept per view id.</summary>
    int ViewId { get; }

    /// <summary>
    /// Display name for profiler/debug tooling. Defaults to the type name; override to tell instances apart (e.g. per camera).
    /// </summary>
    string Name => GetType().Name;

    /// <summary>
    /// Framebuffer that passes declaring the view target draw into, or null. A swapchain's framebuffer is valid and is presented after dispatch.
    /// Write-only to passes. Should match <see cref="PixelWidth"/> and <see cref="PixelHeight"/>.
    /// </summary>
    Framebuffer? Target => null;
}

/// <summary>
/// One pipeline pass. Declares texture in/out via Setup so the graph can order and resolve deps. Pipeline calls Render to execute.
/// </summary>
public interface IPass
{
    /// <summary>Debug name.</summary>
    string Name { get; }

    /// <summary>Declare in/out textures.</summary>
    void Setup(RenderContextBuilder builder);

    /// <summary>Record rendering into cmd, already begun. The graph submits it after Render returns. Get textures via context.GetRenderTexture.</summary>
    void Render(RenderContext context, CommandBuffer cmd);
}
