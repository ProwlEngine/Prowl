using Prowl.Vector;


namespace Prowl.Graphite;

/// <summary>
/// Records GPU commands. Render context rents/begins/ends/submits it, not passes. Not thread-safe.
/// Some commands need state bound first. Reset before reuse.
/// </summary>
public abstract partial class CommandBuffer : CommandBufferBase
{
    private readonly GraphicsDeviceFeatures _features;
    private readonly uint _uniformBufferAlignment;
    private readonly uint _structuredBufferAlignment;

    private protected Framebuffer? _framebuffer;
    private protected OutputDescription? _framebufferOutputs;

    private protected GraphicsProgram? _shaderProgram;
    private protected ComputeProgram? _computeProgram;

    private protected IVertexSource? _currentVertexSource;
    private protected uint _currentIndexCount;


    /// <summary>Merged property table. Backend reads at draw time.</summary>
    private protected readonly PropertySet _activeProperties = new();

    /// <summary>Bumps on every active property change. Backend uses it to skip redundant work.</summary>
    private protected uint _activePropertiesEpoch;

    private PropertySet? _lastAppliedSource;
    private uint _lastAppliedSourceVersion;

    internal CommandBuffer(GraphicsDeviceFeatures features, uint uniformAlignment, uint structuredAlignment)
    {
        _features = features;
        _uniformBufferAlignment = uniformAlignment;
        _structuredBufferAlignment = structuredAlignment;
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
        unchecked { _activePropertiesEpoch++; }
    }

    /// <summary>Resets and starts recording. Context calls on rent, not passes.</summary>
    internal abstract void Begin();

    /// <summary>Finishes recording, makes buffer executable. Context calls on submit, not passes.</summary>
    internal abstract void End();
}
