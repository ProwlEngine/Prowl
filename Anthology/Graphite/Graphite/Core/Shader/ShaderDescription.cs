using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a full graphics program: all shader stages plus the pipeline state it owns (blend, depth, rasterizer, vertex layouts, resource layouts).
/// </summary>
public struct ShaderDescription : IEquatable<ShaderDescription>
{
    /// <summary>
    /// Per-stage descriptions. Each needs a unique stage value.
    /// </summary>
    public ShaderStageDescription[] Stages;

    /// <summary>
    /// Blend state, controls how colors blend into targets.
    /// </summary>
    public BlendStateDescription BlendState;

    /// <summary>
    /// Depth/stencil state, controls depth test/write/compare.
    /// </summary>
    public DepthStencilStateDescription DepthStencilState;

    /// <summary>
    /// Rasterizer state, controls culling/clip/scissor/fill.
    /// </summary>
    public RasterizerStateDescription RasterizerState;

    /// <summary>
    /// Vertex input layouts, one per vertex buffer bound at draw time.
    /// </summary>
    public VertexLayoutDescription[] VertexLayouts;

    /// <summary>
    /// Resource layouts declared by this program.
    /// </summary>
    public ResourceLayoutDescription[] ResourceLayouts;

    /// <summary>
    /// Makes a new ShaderDescription with default state and given stages.
    /// </summary>
    /// <param name="stages">Per-stage descriptions.</param>
    public ShaderDescription(params ShaderStageDescription[] stages)
    {
        Stages = stages;
        BlendState = default;
        DepthStencilState = default;
        RasterizerState = default;
        VertexLayouts = Array.Empty<VertexLayoutDescription>();
        ResourceLayouts = Array.Empty<ResourceLayoutDescription>();
    }

    /// <summary>
    /// Makes a new ShaderDescription.
    /// </summary>
    /// <param name="stages">Per-stage descriptions.</param>
    /// <param name="blendState">Blend state.</param>
    /// <param name="depthStencilState">Depth/stencil state.</param>
    /// <param name="rasterizerState">Rasterizer state.</param>
    /// <param name="vertexLayouts">Vertex input layouts.</param>
    /// <param name="resourceLayouts">Resource layouts.</param>
    public ShaderDescription(
        ShaderStageDescription[] stages,
        BlendStateDescription blendState,
        DepthStencilStateDescription depthStencilState,
        RasterizerStateDescription rasterizerState,
        VertexLayoutDescription[] vertexLayouts,
        ResourceLayoutDescription[] resourceLayouts)
    {
        Stages = stages;
        BlendState = blendState;
        DepthStencilState = depthStencilState;
        RasterizerState = rasterizerState;
        VertexLayouts = vertexLayouts;
        ResourceLayouts = resourceLayouts;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    public bool Equals(ShaderDescription other)
    {
        return Util.ArrayEqualsEquatable(Stages, other.Stages)
            && BlendState.Equals(other.BlendState)
            && DepthStencilState.Equals(other.DepthStencilState)
            && RasterizerState.Equals(other.RasterizerState)
            && Util.ArrayEqualsEquatable(VertexLayouts, other.VertexLayouts)
            && Util.ArrayEqualsEquatable(ResourceLayouts, other.ResourceLayouts);
    }

    /// <summary>
    /// Hash code for this instance.
    /// </summary>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            Stages.ArrayHash(),
            BlendState,
            DepthStencilState,
            RasterizerState,
            VertexLayouts.ArrayHash(),
            ResourceLayouts.ArrayHash());
    }
}
