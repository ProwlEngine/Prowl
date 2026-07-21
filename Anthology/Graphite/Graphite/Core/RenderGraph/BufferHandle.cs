namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Opaque handle to a graph buffer. Get from builder in Setup, resolve via context.GetRenderBuffer in Render.
/// </summary>
public readonly struct BufferHandle
{
    /// <summary>Resource this points to.</summary>
    public readonly RenderResourceID Id;

    internal BufferHandle(RenderResourceID id) => Id = id;

    /// <summary>False if default/never obtained from builder.</summary>
    public bool IsValid => Id.IsValid;
}
