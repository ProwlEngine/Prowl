namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Minimal size info for sizing view-relative render targets. Implementers add richer view data on top.
/// </summary>
public interface IRenderView
{
    /// <summary>Width in pixels.</summary>
    uint PixelWidth { get; }

    /// <summary>Height in pixels.</summary>
    uint PixelHeight { get; }
}
