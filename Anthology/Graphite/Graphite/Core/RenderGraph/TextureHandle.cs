namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Opaque handle a pass holds onto a graph texture resource. Get it from the builder during
/// setup, resolve it to a real render target during rendering.
/// </summary>
public readonly struct TextureHandle
{
    /// <summary>The graph resource this handle points to.</summary>
    public readonly RenderResourceID Id;

    internal TextureHandle(RenderResourceID id) => Id = id;

    /// <summary>False if this is a default handle never obtained from the builder.</summary>
    public bool IsValid => Id.IsValid;
}
