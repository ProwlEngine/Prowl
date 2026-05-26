using System;
using System.Collections.Generic;

namespace Prowl.Veldrid;

/// <summary>
/// A device resource encapsulating a complete graphics shader program: all stages plus the pipeline-level state owned by
/// the program (blend / depth / rasterizer / vertex layouts / resource layouts).
/// See <see cref="ShaderDescription"/>.
/// </summary>
public abstract class ShaderProgram : DeviceResource, IDisposable
{
    private readonly ShaderStages[] _stages;
    private readonly BlendStateDescription _blendState;
    private readonly DepthStencilStateDescription _depthStencilState;
    private readonly RasterizerStateDescription _rasterizerState;
    private readonly VertexLayoutDescription[] _vertexLayouts;
    private readonly ResourceLayoutDescription[] _resourceLayouts;

    /// <summary>
    /// Constructs a new <see cref="ShaderProgram"/> from the supplied description.
    /// </summary>
    /// <param name="description">The description that drives program creation.</param>
    internal ShaderProgram(ref ShaderDescription description)
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
        _resourceLayouts = Util.ShallowClone(description.ResourceLayouts) ?? Array.Empty<ResourceLayoutDescription>();
        DeepCloneUniformFields(_resourceLayouts);
    }

    internal static void DeepCloneUniformFields(ResourceLayoutDescription[] layouts)
    {
        for (int i = 0; i < layouts.Length; i++)
        {
            ResourceLayoutElementDescription[] elements = layouts[i].Elements;
            if (elements == null) continue;
            ResourceLayoutElementDescription[] clonedElements = new ResourceLayoutElementDescription[elements.Length];
            for (int j = 0; j < elements.Length; j++)
            {
                ResourceLayoutElementDescription elem = elements[j];
                if (elem.UniformFields != null)
                {
                    elem.UniformFields = (UniformBlockField[])elem.UniformFields.Clone();
                }
                clonedElements[j] = elem;
            }
            layouts[i].Elements = clonedElements;
        }
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

    /// <summary>
    /// The resource layouts declared by this program.
    /// </summary>
    public IReadOnlyList<ResourceLayoutDescription> ResourceLayouts => _resourceLayouts;

    internal VertexLayoutDescription[] VertexLayoutsArray => _vertexLayouts;
    internal ResourceLayoutDescription[] ResourceLayoutsArray => _resourceLayouts;
    internal ref readonly BlendStateDescription BlendStateRef => ref _blendState;
    internal ref readonly DepthStencilStateDescription DepthStencilStateRef => ref _depthStencilState;
    internal ref readonly RasterizerStateDescription RasterizerStateRef => ref _rasterizerState;

    /// <summary>
    /// A string identifying this instance. Can be used to differentiate between objects in graphics debuggers and other
    /// tools.
    /// </summary>
    public abstract string Name { get; set; }

    /// <summary>
    /// A bool indicating whether this instance has been disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    /// Frees unmanaged device resources controlled by this instance.
    /// </summary>
    public abstract void Dispose();
}
