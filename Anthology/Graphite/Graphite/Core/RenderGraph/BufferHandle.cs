namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Opaque handle a pass holds onto a graph buffer resource. Get it from the builder during setup,
/// resolve it to a real device buffer during rendering with context.GetRenderBuffer.
/// </summary>
public readonly struct BufferHandle
{
    /// <summary>The graph resource this handle points to.</summary>
    public readonly RenderResourceID Id;

    internal BufferHandle(RenderResourceID id) => Id = id;

    /// <summary>False if this is a default handle never obtained from the builder.</summary>
    public bool IsValid => Id.IsValid;
}
