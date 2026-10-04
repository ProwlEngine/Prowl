using Prowl.Vector;


namespace Prowl.Graphite;

/// <summary>
/// Records GPU commands. Render context rents/begins/ends/submits it, not passes. Not thread-safe.
/// Some commands need state bound first. Reset before reuse.
/// </summary>
public abstract partial class CommandBuffer : CommandBufferBase
{
    private readonly GraphicsDeviceFeatures _features;

    private protected Framebuffer? _framebuffer;
    private protected OutputDescription? _framebufferOutputs;

    private protected GraphicsProgram? _shaderProgram;
    private protected ComputeProgram? _computeProgram;

    private protected IVertexSource? _currentVertexSource;
    private protected uint _currentIndexCount;


    /// <summary>Merged property table. Backend reads at draw time.</summary>
    private protected readonly PropertySet _activeProperties = new();

    private PropertySet? _lastAppliedSource;
    private uint _lastAppliedSourceVersion;

    private readonly System.Collections.Generic.List<PropertyID> _changedPropertyKeys = new();
    private bool _allPropertiesChanged = true;

    internal System.Collections.Generic.List<PropertyID> ChangedPropertyKeys => _changedPropertyKeys;

    internal bool AllPropertiesChanged => _allPropertiesChanged;

    internal void ConsumePropertyChanges()
    {
        _changedPropertyKeys.Clear();
        _allPropertiesChanged = false;
    }

    internal CommandBuffer(GraphicsDevice device) : base(device)
    {
        _features = device.Features;
    }

    internal void ClearCachedState()
    {
        _framebuffer = null;
        _shaderProgram = null;
        _computeProgram = null;
        _framebufferOutputs = null;
        _currentVertexSource = null;
        _activeProperties.Clear();
        _lastAppliedSource = null;
        _lastAppliedSourceVersion = 0;
        _changedPropertyKeys.Clear();
        _allPropertiesChanged = true;
    }

    /// <summary>Resets and starts recording. Context calls on rent, not passes.</summary>
    internal abstract void Begin();

    /// <summary>Finishes recording, makes buffer executable. Context calls on submit, not passes.</summary>
    internal abstract void End();

    internal abstract void RecordBarriers(System.ReadOnlySpan<TextureBarrier> textures, BufferAccess bufferSrc, BufferAccess bufferDst);

    internal abstract void SealRenderPass();

    internal abstract void RecordFullBarrier();

    internal void RequireGraphExecution(string operation)
    {
        if (Execution == null)
        {
            throw new RenderException(
                $"{operation} needs a command buffer rented from a render context. Work recorded through GraphicsDevice.Record can only transfer: update, copy and mipmap generation.");
        }
    }
}
