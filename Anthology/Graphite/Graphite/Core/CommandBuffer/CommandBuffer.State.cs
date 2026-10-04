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
        ValidationHelpers.RequireNotNullRender(program, nameof(GraphicsProgram), nameof(SetShader));
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
        ValidationHelpers.RequireNotNullRender(program, nameof(ComputeProgram), nameof(SetComputeShader));
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
    /// <para>No-op when the set is unchanged and its entries are still the active ones.</para>
    /// </summary>
    /// <param name="properties">Set to merge in.</param>
    public void SetProperties(PropertySet properties)
    {
        ValidationHelpers.RequireNotNull(properties, nameof(properties), nameof(SetProperties));

        if (ReferenceEquals(properties, _lastAppliedSource) && properties.Version == _lastAppliedSourceVersion)
            return;

        if (_mergedSourceVersions.TryGetValue(properties, out uint mergedVersion)
            && mergedVersion == properties.Version
            && properties.EntriesActiveIn(_activeProperties))
        {
            _lastAppliedSource = properties;
            _lastAppliedSourceVersion = properties.Version;
            return;
        }

        _activeProperties.MergeFrom(properties, _changedPropertyKeys);
        _mergedSourceVersions[properties] = properties.Version;
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
        _activeProperties.Clear();
        _lastAppliedSource = null;
        _lastAppliedSourceVersion = 0;
        _mergedSourceVersions.Clear();
        _changedPropertyKeys.Clear();
        _allPropertiesChanged = true;
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
            if (fb != null)
            {
                SetViewport(new Viewport(0, 0, fb.Width, fb.Height, 0, 1));
                SetScissorRect(0, 0, fb.Width, fb.Height);
            }
        }
    }

    /// <summary>Backend framebuffer set.</summary>
    /// <param name="fb">Framebuffer.</param>
    private protected abstract void SetFramebufferCore(Framebuffer fb);

    internal void SetAttachmentOps(in TargetLoadStoreOps ops)
        => SetAttachmentOpsCore(ops.Color.Load, ops.Color.Store, ops.Depth.Load, ops.Depth.Store);

    private protected abstract void SetAttachmentOpsCore(LoadAction colorLoad, StoreAction colorStore, LoadAction depthLoad, StoreAction depthStore);

    /// <summary>Sets render texture's framebuffer as render target.</summary>
    /// <param name="renderTexture">Render texture.</param>
    public void SetFramebuffer(RenderTexture renderTexture)
        => SetFramebuffer(renderTexture.Framebuffer);

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
    public abstract void SetScissorRect(uint x, uint y, uint width, uint height);
}
