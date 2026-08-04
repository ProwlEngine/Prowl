using Prowl.Vector;

namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
    /// <summary>
    /// Sets active shader. Must match bound framebuffer/buffers. Invalidates bound resource sets, rebind after.
    /// </summary>
    /// <param name="program">Shader to set.</param>
    public void SetShader(GraphicsProgram program)
    {
        ValidationHelpers.RequireNotNullRender(program, nameof(GraphicsProgram), nameof(SetShader));
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

    /// <summary>Sets active compute shader. Invalidates bound compute resource sets.</summary>
    /// <param name="program">Compute shader to set.</param>
    public void SetComputeShader(ComputeProgram program)
    {
        ValidationHelpers.RequireNotNullRender(program, nameof(ComputeProgram), nameof(SetComputeShader));
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
        SetVertexSourceCore(source);
    }

    private protected abstract void SetVertexSourceCore(IVertexSource source);

    /// <summary>
    /// Merges properties into bind table, last write wins, sticks until ClearProperties or Begin.
    /// <para>Same unchanged set twice in a row is a no-op.</para>
    /// </summary>
    /// <param name="properties">Set to merge in.</param>
    public void SetProperties(PropertySet properties)
    {
        ValidationHelpers.RequireNotNull(properties, nameof(properties), nameof(SetProperties));

        // Re-applying the very same set with no changes since is a no-op: the merge is idempotent
        // when nothing else was applied in between, so skip it and leave the epoch untouched.
        if (ReferenceEquals(properties, _lastAppliedSource) && properties.Version == _lastAppliedSourceVersion)
            return;

        _activeProperties.ApplyOther(properties);
        _lastAppliedSource = properties;
        _lastAppliedSourceVersion = properties.Version;
        unchecked { _activePropertiesEpoch++; }
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
        _activeProperties.Clear();     // bump merged resource version
        _lastAppliedSource = null;
        _lastAppliedSourceVersion = 0;
        unchecked { _activePropertiesEpoch++; }
        ClearPropertiesCore();
    }

    /// <summary>Backend work for clearing properties.</summary>
    private protected abstract void ClearPropertiesCore();

    /// <summary>Sets render target framebuffer. Must match active shader's output count/formats.</summary>
    /// <param name="fb">Framebuffer to set.</param>
    public void SetFramebuffer(Framebuffer fb)
    {
        if (_framebuffer != fb)
        {
            _framebuffer = fb;
            SetFramebufferCore(fb);
            _framebufferOutputs = fb != null ? fb.OutputDescription : default;
            SetFullViewports();
            SetFullScissorRects();
        }
    }

    /// <summary>Backend framebuffer set.</summary>
    /// <param name="fb">Framebuffer.</param>
    private protected abstract void SetFramebufferCore(Framebuffer fb);

    /// <summary>Sets render texture's framebuffer as render target.</summary>
    /// <param name="renderTexture">Render texture.</param>
    public void SetFramebuffer(RenderTexture renderTexture)
        => SetFramebuffer(renderTexture.Framebuffer);

    /// <summary>Sets render texture's framebuffer as render target.</summary>
    /// <param name="renderTexture">Render texture.</param>
    public void SetRenderTarget(RenderTexture renderTexture)
        => SetFramebuffer(renderTexture.Framebuffer);

    /// <summary>Sets framebuffer as render target.</summary>
    /// <param name="fb">Framebuffer to set.</param>
    public void SetRenderTarget(Framebuffer fb)
        => SetFramebuffer(fb);

    /// <summary>Clears one color target. Index must be within framebuffer's color attachment count.</summary>
    /// <param name="index">Color target index.</param>
    /// <param name="clearColor">Clear value.</param>
    public void ClearColorTarget(uint index, Color clearColor)
    {
        ClearColorTarget_CheckFramebuffer(index);
        ClearColorTargetCore(index, clearColor);
    }

    private protected abstract void ClearColorTargetCore(uint index, Color clearColor);

    /// <summary>Clears depth-stencil target, stencil to 0. Needs a depth attachment.</summary>
    /// <param name="depth">Depth clear value.</param>
    public void ClearDepthStencil(float depth)
    {
        ClearDepthStencil(depth, 0);
    }

    /// <summary>Clears depth-stencil target. Needs a depth attachment.</summary>
    /// <param name="depth">Depth clear value.</param>
    /// <param name="stencil">Stencil clear value.</param>
    public void ClearDepthStencil(float depth, byte stencil)
    {
        ClearDepthStencil_CheckFramebuffer();
        ClearDepthStencilCore(depth, stencil);
    }

    private protected abstract void ClearDepthStencilCore(float depth, byte stencil);

    /// <summary>Sets all viewports to cover whole framebuffer.</summary>
    public void SetFullViewports()
    {
        CheckFramebuffer(nameof(SetFullViewports));
        SetViewport(0, new Viewport(0, 0, _framebuffer!.Width, _framebuffer.Height, 0, 1));

        for (uint index = 1; index < _framebuffer.ColorTargets.Count; index++)
            SetViewport(index, new Viewport(0, 0, _framebuffer.Width, _framebuffer.Height, 0, 1));
    }

    /// <summary>Sets one viewport to cover whole framebuffer.</summary>
    /// <param name="index">Color target index.</param>
    public void SetFullViewport(uint index)
    {
        CheckFramebuffer(nameof(SetFullViewport));
        SetViewport(index, new Viewport(0, 0, _framebuffer!.Width, _framebuffer.Height, 0, 1));
    }

    /// <summary>Sets viewport at index. Index must be within framebuffer's color attachment count.</summary>
    /// <param name="index">Color target index.</param>
    /// <param name="viewport">New viewport.</param>
    public void SetViewport(uint index, Viewport viewport) => SetViewport(index, ref viewport);

    /// <summary>Sets viewport at index. Index must be within framebuffer's color attachment count.</summary>
    /// <param name="index">Color target index.</param>
    /// <param name="viewport">New viewport.</param>
    public abstract void SetViewport(uint index, ref Viewport viewport);

    /// <summary>Sets all scissor rects to cover whole framebuffer.</summary>
    public void SetFullScissorRects()
    {
        CheckFramebuffer(nameof(SetFullScissorRects));
        SetScissorRect(0, 0, 0, _framebuffer!.Width, _framebuffer.Height);

        for (uint index = 1; index < _framebuffer.ColorTargets.Count; index++)
        {
            SetScissorRect(index, 0, 0, _framebuffer.Width, _framebuffer.Height);
        }
    }

    /// <summary>Sets one scissor rect to cover whole framebuffer.</summary>
    /// <param name="index">Color target index.</param>
    public void SetFullScissorRect(uint index)
    {
        CheckFramebuffer(nameof(SetFullScissorRect));
        SetScissorRect(index, 0, 0, _framebuffer!.Width, _framebuffer.Height);
    }

    /// <summary>Sets scissor rect at index. Index must be within framebuffer's color attachment count.</summary>
    /// <param name="index">Color target index.</param>
    /// <param name="x">Rect X.</param>
    /// <param name="y">Rect Y.</param>
    /// <param name="width">Rect width.</param>
    /// <param name="height">Rect height.</param>
    public abstract void SetScissorRect(uint index, uint x, uint y, uint width, uint height);
}
