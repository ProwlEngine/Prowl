using System.Collections.Generic;


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

    /// <summary>Execution this buffer was rented for. Null if not tied to one.</summary>
    internal ExecutionTask? Execution { get; set; }

    /// <summary>Pass this buffer was rented during, for profiler timing. Null outside a pass.</summary>
    internal PassInfo? Pass { get; set; }

    /// <summary>Bound execution's id, or 0.</summary>
    internal ulong ExecutionId => Execution?.Id ?? 0;

    /// <summary>Fresh id stamped per rental, so profiler can tell reused instances apart.</summary>
    internal ulong RentalId { get; set; }

    internal CommandBufferInfo ProfilerInfo => new(RentalId, Name, Pass);

    /// <summary>
    /// True if profiler wants metadata via RecordMetadata. Check before building a metadata object.
    /// </summary>
    public bool WantsMetadata => Execution?.Device.Profiler?.RequestMetadata ?? false;

    /// <summary>
    /// Attaches metadata to every draw/dispatch since last call (or since open).
    /// </summary>
    public void RecordMetadata(object metadata) => Execution?.Device.Profiler?.RecordDrawMetadata(ProfilerInfo, metadata);

    /// <summary>Reports a resource-set bind to the profiler, if any.</summary>
    internal void RecordResourceSetBind(uint setCount) => Execution?.Device.Profiler?.RecordResourceSetBind(setCount);

    private readonly List<BufferBindingInfo> _capturedVertexBuffers = new();
    private BufferBindingInfo? _capturedIndexBuffer;

    /// <summary>
    /// True if profiler wants draw-time buffer bindings captured. Check before reporting via
    /// CaptureResolvedVertexBinding/CaptureResolvedIndexBinding, building BufferBindingInfo unread is wasted work.
    /// </summary>
    internal bool WantsDrawBufferCapture => Execution?.Device.Profiler?.RequestCapture ?? false;

    /// <summary>Clears capture state before backend resolves a new draw's buffers. Only if WantsDrawBufferCapture.</summary>
    internal void BeginDrawBufferCapture()
    {
        _capturedVertexBuffers.Clear();
        _capturedIndexBuffer = null;
    }

    /// <summary>
    /// Reports the vertex buffer backend just resolved and bound for the current draw. Only if
    /// WantsDrawBufferCapture - must be same resolution used for the real GPU bind, not a second query.
    /// </summary>
    internal void CaptureResolvedVertexBinding(in VertexBinding binding)
    {
        _capturedVertexBuffers.Add(new BufferBindingInfo(
            binding.Buffer.Name, binding.Buffer, binding.Offset, binding.Buffer.SizeInBytes - binding.Offset,
            binding.Buffer.ContentVersion, readOnly: true));
    }

    /// <summary>
    /// Reports the index buffer backend just resolved and bound for the current draw. Only if
    /// WantsDrawBufferCapture - must be same resolution used for the real GPU bind, not a second query.
    /// </summary>
    internal void CaptureResolvedIndexBinding(DeviceBuffer buffer, IndexFormat format, uint indexCount)
    {
        uint indexSize = format == IndexFormat.UInt16 ? 2u : 4u;
        _capturedIndexBuffer = new BufferBindingInfo(buffer.Name, buffer, offset: 0, indexSize * indexCount, buffer.ContentVersion, readOnly: true);
    }

    /// <summary>
    /// Reports buffers already bound for the just-recorded draw (captured earlier) plus any buffer-kind
    /// entries in active properties, to the profiler. No-op unless profiler requested a capture.
    /// </summary>
    private void RecordDrawBuffersIfRequested()
    {
        if (Execution?.Device.Profiler is not { RequestCapture: true } profiler)
            return;

        var boundBuffers = new List<BufferBindingInfo>();
        foreach (KeyValuePair<PropertyID, PropertyEntry> kv in _activeProperties.Entries)
        {
            if (kv.Value.Kind == PropertyEntryKind.Buffer && kv.Value.Buffer is { } range)
            {
                string name = PropertyID.ToString(kv.Key) ?? kv.Key.ToString();
                boundBuffers.Add(new BufferBindingInfo(
                    name, range.Buffer, range.Offset, range.SizeInBytes, range.Buffer.ContentVersion, kv.Value.ReadOnly));
            }
        }

        var vertexBuffers = new List<BufferBindingInfo>(_capturedVertexBuffers);
        profiler.RecordDrawBuffers(ProfilerInfo, new DrawBufferInfo(vertexBuffers, _capturedIndexBuffer, boundBuffers));
    }

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

    /// <summary>Draws with current bound state, no index buffer.</summary>
    /// <param name="vertexCount">Vertex count.</param>
    public void Draw(uint vertexCount) => Draw(vertexCount, 1, 0, 0);

    /// <summary>Draws with current bound state, no index buffer.</summary>
    /// <param name="vertexCount">Vertex count.</param>
    /// <param name="instanceCount">Instance count.</param>
    /// <param name="vertexStart">First vertex.</param>
    /// <param name="instanceStart">First instance.</param>
    public void Draw(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
    {
        Draw_PreDrawValidation();
        DrawCore(vertexCount, instanceCount, vertexStart, instanceStart);

        Execution?.Device.Profiler?.RecordDraw(
            ProfilerInfo, new DrawCallInfo(DrawKind.Draw, vertexCount, instanceCount, drawCount: 1, isIndirect: false, _currentVertexSource?.Topology ?? PrimitiveTopology.TriangleList));
        RecordDrawBuffersIfRequested();
    }

    private protected abstract void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart);

    /// <summary>Draws indexed primitives with current bound state.</summary>
    public void DrawIndexed() => DrawIndexed(1, 0, 0, 0);

    /// <summary>Draws indexed primitives with current bound state.</summary>
    /// <param name="instanceCount">Instance count.</param>
    /// <param name="indexStart">Indices to skip in index buffer.</param>
    /// <param name="vertexOffset">Added to each index read.</param>
    /// <param name="instanceStart">First instance.</param>
    public void DrawIndexed(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        DrawIndexed_CheckIndexBuffer();
        Draw_PreDrawValidation();
        DrawIndexed_CheckBaseVertexInstance(vertexOffset, instanceStart);

        DrawIndexedCore(instanceCount, indexStart, vertexOffset, instanceStart);

        Execution?.Device.Profiler?.RecordDraw(
            ProfilerInfo, new DrawCallInfo(DrawKind.DrawIndexed, _currentIndexCount, instanceCount, drawCount: 1, isIndirect: false, _currentVertexSource?.Topology ?? PrimitiveTopology.TriangleList));
        RecordDrawBuffersIfRequested();
    }

    private protected abstract void DrawIndexedCore(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart);

    /// <summary>Issues indirect draws from buffer. Data must match IndirectDrawArguments layout.</summary>
    /// <param name="indirectBuffer">Buffer to read. Needs IndirectBuffer usage flag.</param>
    /// <param name="offset">Byte offset to start reading. Multiple of 4.</param>
    /// <param name="drawCount">Draw commands to issue.</param>
    /// <param name="stride">Byte stride between commands. Multiple of 4, bigger than IndirectDrawArguments.</param>
    public unsafe void DrawIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        DrawIndirect_CheckSupport();
        DrawIndirect_CheckBuffer(indirectBuffer);
        DrawIndirect_CheckOffset(offset);
        DrawIndirect_CheckStride(stride, sizeof(IndirectDrawArguments));
        Draw_PreDrawValidation();

        DrawIndirectCore(indirectBuffer, offset, drawCount, stride);

        Execution?.Device.Profiler?.RecordDraw(
            ProfilerInfo, new DrawCallInfo(DrawKind.DrawIndirect, vertexOrIndexCount: 0, instanceCount: 0, drawCount, isIndirect: true, _currentVertexSource?.Topology ?? PrimitiveTopology.TriangleList));
        RecordDrawBuffersIfRequested();
    }


    /// <summary>Backend indirect draw.</summary>
    /// <param name="indirectBuffer">Indirect buffer.</param>
    /// <param name="offset">Byte offset.</param>
    /// <param name="drawCount">Draw count.</param>
    /// <param name="stride">Byte stride.</param>
    private protected abstract void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride);

    /// <summary>Issues indirect indexed draws from buffer. Data must match IndirectDrawIndexedArguments layout.</summary>
    /// <param name="indirectBuffer">Buffer to read. Needs IndirectBuffer usage flag.</param>
    /// <param name="offset">Byte offset to start reading. Multiple of 4.</param>
    /// <param name="drawCount">Draw commands to issue.</param>
    /// <param name="stride">Byte stride between commands. Multiple of 4, bigger than IndirectDrawIndexedArguments.</param>
    public unsafe void DrawIndexedIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        DrawIndirect_CheckSupport();
        DrawIndirect_CheckBuffer(indirectBuffer);
        DrawIndirect_CheckOffset(offset);
        DrawIndirect_CheckStride(stride, sizeof(IndirectDrawIndexedArguments));
        Draw_PreDrawValidation();

        DrawIndexedIndirectCore(indirectBuffer, offset, drawCount, stride);

        Execution?.Device.Profiler?.RecordDraw(
            ProfilerInfo, new DrawCallInfo(DrawKind.DrawIndexedIndirect, vertexOrIndexCount: 0, instanceCount: 0, drawCount, isIndirect: true, _currentVertexSource?.Topology ?? PrimitiveTopology.TriangleList));
        RecordDrawBuffersIfRequested();
    }


    /// <summary>Backend indirect indexed draw.</summary>
    /// <param name="indirectBuffer">Indirect buffer.</param>
    /// <param name="offset">Byte offset.</param>
    /// <param name="drawCount">Draw count.</param>
    /// <param name="stride">Byte stride.</param>
    private protected abstract void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride);

    /// <summary>Dispatches compute with current bound state.</summary>
    /// <param name="groupCountX">Thread group count X.</param>
    /// <param name="groupCountY">Thread group count Y.</param>
    /// <param name="groupCountZ">Thread group count Z.</param>
    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        DispatchCore(groupCountX, groupCountY, groupCountZ);

        Execution?.Device.Profiler?.RecordDispatch(
            ProfilerInfo, new DispatchCallInfo(groupCountX, groupCountY, groupCountZ, isIndirect: false));
    }

    private protected abstract void DispatchCore(uint groupCountX, uint groupCountY, uint groupCountZ);

    /// <summary>Issues indirect compute dispatch from buffer. Data must match IndirectDispatchArguments layout.</summary>
    /// <param name="indirectBuffer">Buffer to read. Needs IndirectBuffer usage flag.</param>
    /// <param name="offset">Byte offset to start reading. Multiple of 4.</param>
    public void DispatchIndirect(DeviceBuffer indirectBuffer, uint offset)
    {
        DrawIndirect_CheckBuffer(indirectBuffer);
        DrawIndirect_CheckOffset(offset);
        DispatchIndirectCore(indirectBuffer, offset);

        Execution?.Device.Profiler?.RecordDispatch(
            ProfilerInfo, new DispatchCallInfo(0, 0, 0, isIndirect: true));
    }


    /// <summary>Backend indirect dispatch.</summary>
    /// <param name="indirectBuffer">Indirect buffer.</param>
    /// <param name="offset">Byte offset.</param>
    private protected abstract void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset);

    /// <summary>Resolves multisampled texture into non-multisampled one.</summary>
    /// <param name="source">Source, sample count > 1.</param>
    /// <param name="destination">Destination, sample count 1.</param>
    public void ResolveTexture(Texture source, Texture destination)
    {
        ResolveTexture_CheckSampleCounts(source, destination);
        ResolveTextureCore(source, destination);
    }

    /// <summary>Resolves multisampled texture into non-multisampled one.</summary>
    /// <param name="source">Source, sample count > 1.</param>
    /// <param name="destination">Destination, sample count 1.</param>
    protected abstract void ResolveTextureCore(Texture source, Texture destination);


    /// <summary>Pushes a debug group for grouping commands in debug tools. Nestable. Every push needs a pop.</summary>
    /// <param name="name">Group name shown in debug tools.</param>
    public void PushDebugGroup(string name)
    {
        PushDebugGroupCore(name);
    }

    private protected abstract void PushDebugGroupCore(string name);

    /// <summary>Pops current debug group. Only after a matching push.</summary>
    public void PopDebugGroup()
    {
        PopDebugGroupCore();
    }

    private protected abstract void PopDebugGroupCore();

    /// <summary>Inserts a debug marker for spotting points of interest in debug tools.</summary>
    /// <param name="name">Marker name shown in debug tools.</param>
    public void InsertDebugMarker(string name)
    {
        InsertDebugMarkerCore(name);
    }

    private protected abstract void InsertDebugMarkerCore(string name);
}
