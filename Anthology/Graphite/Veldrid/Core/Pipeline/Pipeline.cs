using System;

namespace Prowl.Veldrid;

/// <summary>
/// A device resource encapsulating all state in a graphics pipeline. Used in
/// <see cref="CommandBuffer.SetPipeline(Pipeline)"/> to prepare a <see cref="CommandBuffer"/> for draw commands.
/// See <see cref="GraphicsPipelineDescription"/>.
/// </summary>
public abstract partial class Pipeline : DeviceResource, IDisposable
{
    internal Pipeline(ref GraphicsPipelineDescription graphicsDescription)
        : this(graphicsDescription.ResourceLayouts)
    {
        Pipeline_StoreGraphicsOutputDescription(ref graphicsDescription);
    }

    internal Pipeline(ref ComputePipelineDescription computeDescription)
        : this(computeDescription.ResourceLayouts)
    { }

    internal Pipeline(ResourceLayout[] resourceLayouts)
    {
        Pipeline_StoreResourceLayouts(resourceLayouts);
    }

    /// <summary>
    /// Gets a value indicating whether this instance represents a compute Pipeline.
    /// If false, this instance is a graphics pipeline.
    /// </summary>
    public abstract bool IsComputePipeline { get; }

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
