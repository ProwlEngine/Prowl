namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Minimal size info a view must expose so the framework can size view-relative render
/// targets. Implementers add their own richer view data (matrices, frustum, etc) on top.
/// </summary>
public interface IRenderView
{
    /// <summary>Target width in pixels.</summary>
    uint PixelWidth { get; }

    /// <summary>Target height in pixels.</summary>
    uint PixelHeight { get; }
}
