using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Full graphics shader program with all stages and pipeline state.
/// </summary>
public abstract class GraphicsProgram : ShaderProgram
{
    private readonly ShaderStages[] _stages;
    private readonly ShaderStageDescription[] _stageDescriptions;
    private readonly BlendStateDescription _blendState;
    private readonly DepthStencilStateDescription _depthStencilState;
    private readonly RasterizerStateDescription _rasterizerState;
    private readonly VertexLayoutDescription[] _vertexLayouts;
    private readonly SpecializationConstant[] _specializations;

    internal GraphicsProgram(in ShaderDescription description)
        : base(description.ResourceLayouts, description.PushConstants)
    {
        ShaderStageDescription[] stageDescs = description.Stages ?? Array.Empty<ShaderStageDescription>();
        _stageDescriptions = Util.ShallowClone(stageDescs);
        Key = ProgramKey.Compute(_stageDescriptions);
        _stages = new ShaderStages[stageDescs.Length];
        for (int i = 0; i < stageDescs.Length; i++)
        {
            _stages[i] = stageDescs[i].Stage;
        }
        _blendState = description.BlendState;
        _depthStencilState = description.DepthStencilState;
        _rasterizerState = description.RasterizerState;
        _vertexLayouts = Util.ShallowClone(description.VertexLayouts);
        _specializations = description.Specializations is { } specializations ? Util.ShallowClone(specializations) : [];
    }

    /// <summary>
    /// Stages in this program, in description order.
    /// </summary>
    public IReadOnlyList<ShaderStages> Stages => _stages;

    /// <summary>
    /// Stage descriptions with SPIR-V and entry points, in description order.
    /// </summary>
    public IReadOnlyList<ShaderStageDescription> StageDescriptions => _stageDescriptions;

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

    /// <summary>
    /// Specialization constant values every stage is built with.
    /// </summary>
    public IReadOnlyList<SpecializationConstant> Specializations => _specializations;

    /// <summary>
    /// Builds the pipeline for drawing into targets of this layout with this topology now, so the first draw that
    /// needs it does not. Thread safe, and does nothing once built.
    /// </summary>
    /// <param name="outputs">Attachment formats and sample count of the targets the program will draw into.</param>
    /// <param name="topology">Primitive topology of the draws.</param>
    public void BuildPipeline(in OutputDescription outputs, PrimitiveTopology topology) => BuildPipelineCore(outputs, topology);

    private protected abstract void BuildPipelineCore(in OutputDescription outputs, PrimitiveTopology topology);

    internal VertexLayoutDescription[] VertexLayoutsArray => _vertexLayouts;
    internal SpecializationConstant[] SpecializationsArray => _specializations;
    internal ref readonly BlendStateDescription BlendStateRef => ref _blendState;
    internal ref readonly DepthStencilStateDescription DepthStencilStateRef => ref _depthStencilState;
    internal ref readonly RasterizerStateDescription RasterizerStateRef => ref _rasterizerState;
}
