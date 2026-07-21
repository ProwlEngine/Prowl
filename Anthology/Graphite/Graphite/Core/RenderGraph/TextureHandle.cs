namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Opaque handle to a graph texture. Get from builder in Setup, resolve to real target in Render.
/// </summary>
public readonly struct TextureHandle
{
    /// <summary>Resource this points to.</summary>
    public readonly RenderResourceID Id;

    internal TextureHandle(RenderResourceID id) => Id = id;

    /// <summary>False if default/never obtained from builder.</summary>
    public bool IsValid => Id.IsValid;
}
