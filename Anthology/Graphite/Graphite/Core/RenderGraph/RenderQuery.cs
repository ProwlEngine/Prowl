using Prowl.Vector;

namespace Prowl.Graphite.RenderGraph;

/// <summary>Ordering for a set of draw commands relative to the view.</summary>
public enum SortMode
{
    /// <summary>No sort, keeps collection order.</summary>
    None,

    /// <summary>Nearest first. Good for opaques (early-Z).</summary>
    FrontToBack,

    /// <summary>Farthest first. Needed for transparent blending.</summary>
    BackToFront
}

/// <summary>
/// Base cull request a pass hands to the culler to pull a slice of the scene. Subclass to add your own
/// selection fields (tags, layers) and downcast on receipt.
/// </summary>
public abstract class RenderQuery
{
    /// <summary>How returned commands are ordered relative to the view.</summary>
    public SortMode Sort { get; set; }

    /// <summary>
    /// Frustum to cull against instead of the current view's. Set this to cull from a different
    /// viewpoint, e.g. a shadow pass culling from a light. Null uses the current view.
    /// </summary>
    public Frustum? FrustumOverride { get; set; }
}
