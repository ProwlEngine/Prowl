using System;

namespace Prowl.Veldrid;

/// <summary>
/// Describes a monolithic <see cref="GraphicsProgram"/>, for creation using a <see cref="ResourceFactory"/>.
/// A program bundles every shader stage of a single graphics program plus the pipeline-level state owned by the shader
/// (blend / depth / rasterizer / vertex layouts / resource layouts).
/// </summary>
public struct ShaderDescription : IEquatable<ShaderDescription>
{
    /// <summary>
    /// The per-stage descriptions that make up this program. Each entry must have a unique <see cref="ShaderStages"/> value.
    /// </summary>
    public ShaderStageDescription[] Stages;

    /// <summary>
    /// A description of the blend state, which controls how color values are blended into each color target.
    /// </summary>
    public BlendStateDescription BlendState;

    /// <summary>
    /// A description of the depth stencil state, which controls depth tests, writing, and comparisons.
    /// </summary>
    public DepthStencilStateDescription DepthStencilState;

    /// <summary>
    /// A description of the rasterizer state, which controls culling, clipping, scissor, and polygon-fill behavior.
    /// </summary>
    public RasterizerStateDescription RasterizerState;

    /// <summary>
    /// The vertex input layouts understood by this program. Each element describes the layout of a single vertex
    /// <see cref="DeviceBuffer"/> to be bound when drawing.
    /// </summary>
    public VertexLayoutDescription[] VertexLayouts;

    /// <summary>
    /// The resource layouts declared by this program.
    /// </summary>
    public ResourceLayoutDescription[] ResourceLayouts;

    /// <summary>
    /// Constructs a new <see cref="ShaderDescription"/> with default state and the given stages.
    /// </summary>
    /// <param name="stages">The per-stage descriptions.</param>
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
    /// Constructs a new <see cref="ShaderDescription"/>.
    /// </summary>
    /// <param name="stages">The per-stage descriptions.</param>
    /// <param name="blendState">The blend state owned by the program.</param>
    /// <param name="depthStencilState">The depth/stencil state owned by the program.</param>
    /// <param name="rasterizerState">The rasterizer state owned by the program.</param>
    /// <param name="vertexLayouts">The vertex input layouts.</param>
    /// <param name="resourceLayouts">The resource layouts declared by the program.</param>
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
    /// Returns the hash code for this instance.
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
