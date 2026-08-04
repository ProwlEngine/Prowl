using System;

namespace Prowl.Graphite;

/// <summary>
/// Full graphics program: shader stages plus its pipeline state.
/// </summary>
public struct ShaderDescription : IEquatable<ShaderDescription>
{
    /// <summary>
    /// Per-stage descs, unique stage each.
    /// </summary>
    public ShaderStageDescription[] Stages;

    /// <summary>
    /// Blend state.
    /// </summary>
    public BlendStateDescription BlendState;

    /// <summary>
    /// Depth/stencil state.
    /// </summary>
    public DepthStencilStateDescription DepthStencilState;

    /// <summary>
    /// Rasterizer state.
    /// </summary>
    public RasterizerStateDescription RasterizerState;

    /// <summary>
    /// Vertex input layouts, one per buffer.
    /// </summary>
    public VertexLayoutDescription[] VertexLayouts;

    /// <summary>
    /// Resource layouts for this program.
    /// </summary>
    public ResourceLayoutDescription[] ResourceLayouts;

    /// <summary>
    /// New ShaderDescription, default state.
    /// </summary>
    /// <param name="stages">Per-stage descs.</param>
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
    /// New ShaderDescription.
    /// </summary>
    /// <param name="stages">Per-stage descs.</param>
    /// <param name="blendState">Blend state.</param>
    /// <param name="depthStencilState">Depth/stencil state.</param>
    /// <param name="rasterizerState">Rasterizer state.</param>
    /// <param name="vertexLayouts">Vertex layouts.</param>
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
    /// Elementwise equality.
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
    /// Hash code.
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
