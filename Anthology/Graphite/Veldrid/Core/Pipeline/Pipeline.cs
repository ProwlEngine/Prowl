using System;

namespace Prowl.Veldrid;

/// <summary>
/// A device resource encapsulating all state in a graphics pipeline. Used in
/// <see cref="CommandBuffer.SetPipeline(Pipeline)"/> to prepare a <see cref="CommandBuffer"/> for draw commands.
/// See <see cref="GraphicsPipelineDescription"/>.
/// </summary>
public abstract partial class Pipeline : DeviceResource, IDisposable
{
    private readonly ResourceLayout[] _adapterResourceLayouts;

    internal Pipeline(ResourceFactory factory, ref GraphicsPipelineDescription graphicsDescription)
    {
        _adapterResourceLayouts = MaterializeResourceLayouts(factory, graphicsDescription.Program?.ResourceLayoutsArray);
        Pipeline_StoreGraphicsOutputDescription(ref graphicsDescription);
        Pipeline_StoreResourceLayouts(_adapterResourceLayouts);
    }

    internal Pipeline(ResourceFactory factory, ref ComputePipelineDescription computeDescription)
    {
        _adapterResourceLayouts = MaterializeResourceLayouts(factory, computeDescription.Program?.ResourceLayoutsArray);
        Pipeline_StoreResourceLayouts(_adapterResourceLayouts);
    }

    private static ResourceLayout[] MaterializeResourceLayouts(ResourceFactory factory, ResourceLayoutDescription[] descs)
    {
        if (descs == null || descs.Length == 0)
        {
            return Array.Empty<ResourceLayout>();
        }
        ResourceLayout[] layouts = new ResourceLayout[descs.Length];
        for (int i = 0; i < descs.Length; i++)
        {
            layouts[i] = factory.CreateResourceLayout(ref descs[i]);
        }
        return layouts;
    }

    /// <summary>
    /// Releases the adapter <see cref="ResourceLayout"/> instances this pipeline created from its wrapped program.
    /// Backends must call this from their <see cref="Dispose"/> implementations.
    /// </summary>
    protected void DisposeAdapterResourceLayouts()
    {
        if (_adapterResourceLayouts == null)
        {
            return;
        }
        for (int i = 0; i < _adapterResourceLayouts.Length; i++)
        {
            _adapterResourceLayouts[i]?.Dispose();
        }
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
