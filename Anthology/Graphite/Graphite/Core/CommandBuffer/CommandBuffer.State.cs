using System;
using System.Runtime.InteropServices;

using Prowl.Graphite.Debugging;
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
        CheckNotDisposed(program, "The graphics program");
        bool changed = !ReferenceEquals(_shaderProgram, program);
        if (!changed) return;

        SetShaderCore(program);
        _uniformShadows.UsePlan(program.BindingPlan);
        _shaderProgram = program;
        _statShaderSwitches++;
    }

    private protected abstract void SetShaderCore(GraphicsProgram program);

    /// <summary>Sets active compute shader. Merged properties stay bound; no need to reapply them.</summary>
    /// <param name="program">Compute shader to set.</param>
    public void SetComputeShader(ComputeProgram program)
    {
        RequireGraphExecution(nameof(SetComputeShader));
        ValidationHelpers.RequireNotNullRender(Device, program, nameof(ComputeProgram), nameof(SetComputeShader));
        CheckNotDisposed(program, "The compute program");
        if (ReferenceEquals(_computeProgram, program)) return;

        _statShaderSwitches++;
        SetComputeShaderCore(program);
        _uniformShadows.UsePlan(program.BindingPlan);
        _computeProgram = program;
    }

    private protected abstract void SetComputeShaderCore(ComputeProgram program);

    /// <summary>
    /// Writes push constants, kept until overwritten and pushed to every later draw and dispatch whose program declares a block.
    /// </summary>
    /// <param name="data">Bytes to write, a multiple of 4.</param>
    /// <param name="offset">Byte offset into the block, a multiple of 4.</param>
    public void SetPushConstants(ReadOnlySpan<byte> data, uint offset = 0)
    {
        RequireGraphExecution(nameof(SetPushConstants));
        if (offset % 4 != 0 || data.Length % 4 != 0 || offset + (uint)data.Length > PushConstantCapacity)
            throw new RenderException($"Push constants of {data.Length} bytes at offset {offset} must be 4 byte aligned and fit in {PushConstantCapacity} bytes.");

        data.CopyTo(_pushConstants.AsSpan((int)offset));
        unchecked { _pushConstantVersion++; }
        PassSink?.SetPushConstants(offset, data);
    }

    /// <summary>
    /// Writes a value as push constants, see <see cref="SetPushConstants(ReadOnlySpan{byte}, uint)"/>.
    /// </summary>
    /// <param name="data">Value to write, its size a multiple of 4.</param>
    /// <param name="offset">Byte offset into the block, a multiple of 4.</param>
    public void SetPushConstants<TData>(in TData data, uint offset = 0) where TData : unmanaged
        => SetPushConstants(MemoryMarshal.AsBytes(new ReadOnlySpan<TData>(in data)), offset);

    /// <summary>
    /// Binds a descriptor table to a set index for the following draws and dispatches, or unbinds it with null. Every
    /// program used while it is bound must declare that set with the table's layout.
    /// </summary>
    /// <param name="set">Descriptor set index.</param>
    /// <param name="table">Table to bind, null to unbind.</param>
    public void SetDescriptorTable(uint set, DescriptorTable? table)
    {
        RequireGraphExecution(nameof(SetDescriptorTable));
        if (table != null)
            SetDescriptorTable_CheckNotDisposed(set, table);
        if (set >= _descriptorTables.Length)
        {
            if (table == null)
                return;
            Array.Resize(ref _descriptorTables, (int)set + 1);
        }

        _descriptorTables[set] = table;
    }

    internal DescriptorTable? DescriptorTableAt(int set) => set < _descriptorTables.Length ? _descriptorTables[set] : null;

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

        _activeProperties.MergeFrom(properties, _changedPropertyKeys, _defaultPropertyKeys);
        _uniformShadows.Apply(properties);
        _lastAppliedSource = properties;
        _lastAppliedSourceVersion = properties.Version;
        SetPropertiesCore(properties);
    }

    /// <summary>
    /// Merges defaults into the bind table without replacing any key already set through SetProperties.
    /// <para>Later SetProperties calls replace defaults, and a new defaults set replaces earlier defaults only.</para>
    /// </summary>
    /// <param name="defaults">Default values to merge in.</param>
    public void SetDefaultProperties(PropertySet defaults)
    {
        ValidationHelpers.RequireNotNull(Device, defaults, nameof(defaults), nameof(SetDefaultProperties));

        if (ReferenceEquals(defaults, _lastAppliedDefaults) && defaults.Version == _lastAppliedDefaultsVersion)
            return;

        _activeProperties.MergeDefaults(defaults, _changedPropertyKeys, _defaultPropertyKeys);
        _uniformShadows.ApplyDefaults(defaults);
        _lastAppliedDefaults = defaults;
        _lastAppliedDefaultsVersion = defaults.Version;
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
        _uniformShadows.Clear();
        _lastAppliedSource = null;
        _lastAppliedSourceVersion = 0;
        _lastAppliedDefaults = null;
        _lastAppliedDefaultsVersion = 0;
        _defaultPropertyKeys.Clear();
        _changedPropertyKeys.Clear();
        _allPropertiesChanged = true;
        ClearPropertiesCore();
    }

    /// <summary>Backend work for clearing properties.</summary>
    private protected abstract void ClearPropertiesCore();

    /// <summary>Sets render target framebuffer with load/store ops for the pass it starts. Defaults to load and store.</summary>
    /// <param name="fb">Framebuffer to set.</param>
    /// <param name="ops">Load/store/clear ops, or null for the ops the pass declared for it, else load and store. Explicit ops consume the declared ones.</param>
    public void SetFramebuffer(Framebuffer fb, TargetLoadStoreOps? ops = null)
    {
        RequireGraphExecution(nameof(SetFramebuffer));
        SetFramebuffer_CheckNotDisposed(fb);
        TargetLoadStoreOps? declared = fb != null ? Bindings?.TakeOps(fb) : null;
        ops ??= declared;
        bool changed = _framebuffer != fb;
        if (!changed && !ops.HasValue)
            return;

        _framebuffer = fb;
        TargetLoadStoreOps resolvedOps = ops ?? new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded);
        ReportFramebuffer(fb, in resolvedOps);
        SetFramebufferCore(fb, resolvedOps);
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
        PassSink?.ClearColorTarget(index, clearColor);
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
        PassSink?.ClearDepthStencil(depth, stencil);
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
    public void SetViewport(Viewport viewport)
    {
        SetViewportCore(viewport);
        PassSink?.SetViewport(in viewport);
    }

    private protected abstract void SetViewportCore(Viewport viewport);

    /// <summary>Sets scissor rect.</summary>
    /// <param name="x">Rect X.</param>
    /// <param name="y">Rect Y.</param>
    /// <param name="width">Rect width.</param>
    /// <param name="height">Rect height.</param>
    public void SetScissor(uint x, uint y, uint width, uint height)
    {
        SetScissorCore(x, y, width, height);
        PassSink?.SetScissor(x, y, width, height);
    }

    private protected abstract void SetScissorCore(uint x, uint y, uint width, uint height);

    /// <summary>Sets stencil reference for subsequent draws. Applied from the program on SetShader.</summary>
    /// <param name="reference">Stencil reference value.</param>
    public void SetStencilReference(uint reference)
    {
        SetStencilReferenceCore(reference);
        PassSink?.SetStencilReference(reference);
    }

    private protected abstract void SetStencilReferenceCore(uint reference);

    /// <summary>Sets blend constants for subsequent draws. Applied from the program on SetShader.</summary>
    /// <param name="constants">Blend constant color.</param>
    public void SetBlendConstants(Color constants)
    {
        SetBlendConstantsCore(constants);
        PassSink?.SetBlendConstants(constants);
    }

    private protected abstract void SetBlendConstantsCore(Color constants);
}
