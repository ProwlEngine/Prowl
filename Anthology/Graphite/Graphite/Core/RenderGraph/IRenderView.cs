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

    /// <summary>
    /// Display name for profiler/debug tooling. Defaults to the type name; override to tell instances apart (e.g. per camera).
    /// </summary>
    string Name => GetType().Name;
}
