using System;

namespace Prowl.Graphite;

/// <summary>
/// Stencil test behavior for a program's depth-stencil state.
/// </summary>
public record struct StencilBehaviorDescription
{
    /// <summary>
    /// Op on stencil fail.
    /// </summary>
    public StencilOperation Fail;
    /// <summary>
    /// Op on stencil pass.
    /// </summary>
    public StencilOperation Pass;
    /// <summary>
    /// Op on stencil pass, depth fail.
    /// </summary>
    public StencilOperation DepthFail;
    /// <summary>
    /// Stencil comparison op.
    /// </summary>
    public ComparisonKind Comparison;

    /// <summary>
    /// Makes a StencilBehaviorDescription.
    /// </summary>
    /// <param name="fail">Op on stencil fail.</param>
    /// <param name="pass">Op on stencil pass.</param>
    /// <param name="depthFail">Op on stencil pass, depth fail.</param>
    /// <param name="comparison">Stencil comparison op.</param>
    public StencilBehaviorDescription(
        StencilOperation fail,
        StencilOperation pass,
        StencilOperation depthFail,
        ComparisonKind comparison)
    {
        Fail = fail;
        Pass = pass;
        DepthFail = depthFail;
        Comparison = comparison;
    }
}
