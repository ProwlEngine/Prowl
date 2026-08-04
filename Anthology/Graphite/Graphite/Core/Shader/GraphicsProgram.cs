using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Full graphics shader program with all stages and pipeline state.
/// </summary>
public abstract class GraphicsProgram : ShaderProgram
{
    private readonly ShaderStages[] _stages;
    private readonly BlendStateDescription _blendState;
    private readonly DepthStencilStateDescription _depthStencilState;
    private readonly RasterizerStateDescription _rasterizerState;
    private readonly VertexLayoutDescription[] _vertexLayouts;

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
    /// Stages in this program, in description order.
    /// </summary>
    public IReadOnlyList<ShaderStages> Stages => _stages;

    /// <summary>
    /// Blend state.
    /// </summary>
    public BlendStateDescription BlendState => _blendState;

    /// <summary>
    /// Depth/stencil state.
    /// </summary>
    public DepthStencilStateDescription DepthStencilState => _depthStencilState;

    /// <summary>
    /// Rasterizer state.
    /// </summary>
    public RasterizerStateDescription RasterizerState => _rasterizerState;

    /// <summary>
    /// Vertex input layouts.
    /// </summary>
    public IReadOnlyList<VertexLayoutDescription> VertexLayouts => _vertexLayouts;

    internal VertexLayoutDescription[] VertexLayoutsArray => _vertexLayouts;
    internal ref readonly BlendStateDescription BlendStateRef => ref _blendState;
    internal ref readonly DepthStencilStateDescription DepthStencilStateRef => ref _depthStencilState;
    internal ref readonly RasterizerStateDescription RasterizerStateRef => ref _rasterizerState;
}
