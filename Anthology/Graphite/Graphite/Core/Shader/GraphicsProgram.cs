using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// A device resource encapsulating a complete graphics shader program: all stages plus the pipeline-level state owned by
/// the program (blend / depth / rasterizer / vertex layouts / resource layouts).
/// See <see cref="ShaderDescription"/>.
/// </summary>
public abstract class GraphicsProgram : ShaderProgram
{
    private readonly ShaderStages[] _stages;
    private readonly BlendStateDescription _blendState;
    private readonly DepthStencilStateDescription _depthStencilState;
    private readonly RasterizerStateDescription _rasterizerState;
    private readonly VertexLayoutDescription[] _vertexLayouts;

    /// <summary>
    /// Constructs a new <see cref="GraphicsProgram"/> from the supplied description.
    /// </summary>
    /// <param name="description">The description that drives program creation.</param>
    internal GraphicsProgram(ref ShaderDescription description)
        : base(description.ResourceLayouts)
    {
        ShaderStageDescription[] stageDescs = description.Stages ?? Array.Empty<ShaderStageDescription>();
        _stages = new ShaderStages[stageDescs.Length];
        for (int i = 0; i < stageDescs.Length; i++)
        {
            _stages[i] = stageDescs[i].Stage;
        }
        _blendState = description.BlendState;
        _depthStencilState = description.DepthStencilState;
        _rasterizerState = description.RasterizerState;
        _vertexLayouts = Util.ShallowClone(description.VertexLayouts) ?? Array.Empty<VertexLayoutDescription>();
    }

    /// <summary>
    /// The shader stages present in this program, in the order they appeared in the description.
    /// </summary>
    public IReadOnlyList<ShaderStages> Stages => _stages;

    /// <summary>
    /// The blend state owned by this program.
    /// </summary>
    public BlendStateDescription BlendState => _blendState;

    /// <summary>
    /// The depth/stencil state owned by this program.
    /// </summary>
    public DepthStencilStateDescription DepthStencilState => _depthStencilState;

    /// <summary>
    /// The rasterizer state owned by this program.
    /// </summary>
    public RasterizerStateDescription RasterizerState => _rasterizerState;

    /// <summary>
    /// The vertex input layouts declared by this program.
    /// </summary>
    public IReadOnlyList<VertexLayoutDescription> VertexLayouts => _vertexLayouts;

    internal VertexLayoutDescription[] VertexLayoutsArray => _vertexLayouts;
    internal ref readonly BlendStateDescription BlendStateRef => ref _blendState;
    internal ref readonly DepthStencilStateDescription DepthStencilStateRef => ref _depthStencilState;
    internal ref readonly RasterizerStateDescription RasterizerStateRef => ref _rasterizerState;
}
