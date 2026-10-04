using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
    /// <summary>
    /// Sets active shader. Must match bound framebuffer/buffers. Merged properties stay bound; no need to reapply them.
    /// </summary>
    /// <param name="program">Shader to set.</param>
    public void SetShader(GraphicsProgram program)
    {
        RequireGraphExecution(nameof(SetShader));
        ValidationHelpers.RequireNotNullRender(Device, program, nameof(GraphicsProgram), nameof(SetShader));
        bool changed = !ReferenceEquals(_shaderProgram, program);
        if (!changed) return;

        SetShaderCore(program);
        _shaderProgram = program;

        if (Execution?.Device.Profiler is { } profiler)
        {
            ShaderStages stages = ShaderStages.None;
            foreach (ShaderStages stage in program.Stages)
                stages |= stage;

            profiler.RecordPipelineSwitch(ProfilerInfo, new PipelineBindInfo(program.Name, isCompute: false, stages, program));
        }
    }

    private protected abstract void SetShaderCore(GraphicsProgram program);

    /// <summary>Sets active compute shader. Merged properties stay bound; no need to reapply them.</summary>
    /// <param name="program">Compute shader to set.</param>
    public void SetComputeShader(ComputeProgram program)
    {
        RequireGraphExecution(nameof(SetComputeShader));
        ValidationHelpers.RequireNotNullRender(Device, program, nameof(ComputeProgram), nameof(SetComputeShader));
        if (ReferenceEquals(_computeProgram, program)) return;

        SetComputeShaderCore(program);
        _computeProgram = program;

        Execution?.Device.Profiler?.RecordPipelineSwitch(
            ProfilerInfo, new PipelineBindInfo(program.Name, isCompute: true, ShaderStages.Compute, program));
    }

    private protected abstract void SetComputeShaderCore(ComputeProgram program);

    /// <summary>Binds vertex/index buffers and topology for next draws. Fully replaces old source.</summary>
    /// <param name="source">Source to bind. Not null, pass an empty one for none.</param>
    public void SetVertexSource(IVertexSource source)
    {
        SetVertexSource_CheckNonNull(source);
        _currentVertexSource = source;
    }

    /// <summary>
    /// Merges properties into bind table, last write wins, sticks until ClearProperties or Begin.
    /// <para>No-op when the same set was applied last and is unchanged.</para>
    /// </summary>
    /// <param name="properties">Set to merge in.</param>
    public void SetProperties(PropertySet properties)
    {
        ValidationHelpers.RequireNotNull(Device, properties, nameof(properties), nameof(SetProperties));

        if (ReferenceEquals(properties, _lastAppliedSource) && properties.Version == _lastAppliedSourceVersion)
            return;

        _activeProperties.MergeFrom(properties, _changedPropertyKeys);
        _lastAppliedSource = properties;
        _lastAppliedSourceVersion = properties.Version;
        SetPropertiesCore(properties);
    }

    /// <summary>Backend work for a property merge. Base table already updated.</summary>
    private protected abstract void SetPropertiesCore(PropertySet properties);

    /// <summary>
    /// Clears all merged properties. No GPU calls.
    /// <para>Begin does this for you.</para>
    /// </summary>
    public void ClearProperties()
    {
        _activeProperties.Clear();
        _lastAppliedSource = null;
        _lastAppliedSourceVersion = 0;
        _changedPropertyKeys.Clear();
        _allPropertiesChanged = true;
        ClearPropertiesCore();
    }

    /// <summary>Backend work for clearing properties.</summary>
    private protected abstract void ClearPropertiesCore();

    /// <summary>Sets render target framebuffer with load/store ops for the pass it starts. Defaults to load and store.</summary>
    /// <param name="fb">Framebuffer to set.</param>
    /// <param name="ops">Load/store/clear ops, or null to load and store.</param>
    public void SetFramebuffer(Framebuffer fb, TargetLoadStoreOps? ops = null)
    {
        RequireGraphExecution(nameof(SetFramebuffer));
        bool changed = _framebuffer != fb;
        if (!changed && !ops.HasValue)
            return;

        _framebuffer = fb;
        SetFramebufferCore(fb, ops ?? new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded));
        if (!changed)
            return;

        _framebufferOutputs = fb != null ? fb.OutputDescription : default;
        if (fb != null)
        {
            SetViewport(new Viewport(0, 0, fb.Width, fb.Height, 0, 1));
            SetScissor(0, 0, fb.Width, fb.Height);
        }
    }

    /// <summary>Backend framebuffer set.</summary>
    /// <param name="fb">Framebuffer.</param>
    /// <param name="ops">Load/store/clear ops for the pass.</param>
    private protected abstract void SetFramebufferCore(Framebuffer fb, in TargetLoadStoreOps ops);

    /// <summary>Sets render texture's framebuffer as render target.</summary>
    /// <param name="renderTexture">Render texture.</param>
    /// <param name="ops">Load/store/clear ops, or null to load and store.</param>
    public void SetFramebuffer(RenderTexture renderTexture, TargetLoadStoreOps? ops = null)
        => SetFramebuffer(renderTexture.Framebuffer, ops);

    /// <summary>Clears one color target inside the current pass. Index must be within framebuffer's color attachment count.</summary>
    /// <param name="index">Color target index.</param>
    /// <param name="clearColor">Clear value.</param>
    public void ClearColorTarget(uint index, Color clearColor)
    {
        RequireGraphExecution(nameof(ClearColorTarget));
        ClearColorTarget_CheckFramebuffer(index);
        ClearColorTargetCore(index, clearColor);
    }

    private protected abstract void ClearColorTargetCore(uint index, Color clearColor);

    /// <summary>Clears depth-stencil target inside the current pass. Needs a depth attachment.</summary>
    /// <param name="depth">Depth clear value.</param>
    /// <param name="stencil">Stencil clear value.</param>
    public void ClearDepthStencil(float depth, byte stencil = 0)
    {
        RequireGraphExecution(nameof(ClearDepthStencil));
        ClearDepthStencil_CheckFramebuffer();
        ClearDepthStencilCore(depth, stencil);
    }

    private protected abstract void ClearDepthStencilCore(float depth, byte stencil);

    /// <summary>Sets viewport to cover whole framebuffer.</summary>
    public void SetFullViewport()
    {
        CheckFramebuffer(nameof(SetFullViewport));
        SetViewport(new Viewport(0, 0, _framebuffer!.Width, _framebuffer.Height, 0, 1));
    }

    /// <summary>Sets viewport.</summary>
    /// <param name="viewport">New viewport.</param>
    public abstract void SetViewport(Viewport viewport);

    /// <summary>Sets scissor rect.</summary>
    /// <param name="x">Rect X.</param>
    /// <param name="y">Rect Y.</param>
    /// <param name="width">Rect width.</param>
    /// <param name="height">Rect height.</param>
    public abstract void SetScissor(uint x, uint y, uint width, uint height);

    /// <summary>Sets stencil reference for subsequent draws. Applied from the program on SetShader.</summary>
    /// <param name="reference">Stencil reference value.</param>
    public abstract void SetStencilReference(uint reference);

    /// <summary>Sets blend constants for subsequent draws. Applied from the program on SetShader.</summary>
    /// <param name="constants">Blend constant color.</param>
    public abstract void SetBlendConstants(Color constants);
}
