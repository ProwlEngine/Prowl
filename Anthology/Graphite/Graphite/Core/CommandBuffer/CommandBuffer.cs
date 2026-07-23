using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


using Prowl.Vector;


namespace Prowl.Graphite;

/// <summary>
/// Records GPU commands. Rented/begun and ended/submitted by the render context, not by passes. Not thread-safe.
/// Some commands need state bound first. Needs a reset before reuse.
/// </summary>
public abstract partial class CommandBuffer : DeviceResource, IDisposable
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

    /// <summary>Bumps every time active properties change. Backend uses it to skip redundant work.</summary>
    private protected uint _activePropertiesEpoch;

    private PropertySet? _lastAppliedSource;
    private uint _lastAppliedSourceVersion;

    /// <summary>Execution this buffer was rented for. Null for one-off stuff not tied to an execution.</summary>
    internal ExecutionTask? Execution { get; set; }

    /// <summary>Pass this buffer was rented during, for profiler timing. Null outside a pass.</summary>
    internal PassInfo? Pass { get; set; }

    /// <summary>Bound execution's id, or 0.</summary>
    internal ulong ExecutionId => Execution?.Id ?? 0;

    /// <summary>Monotonic id stamped fresh on every rental, so profiler consumers can tell distinct rentals of a pooled/reused instance apart.</summary>
    internal ulong RentalId { get; set; }

    private CommandBufferInfo ProfilerInfo => new(RentalId, Name, Pass);

    /// <summary>Reports a resource-set bind to the profiler, if any.</summary>
    internal void RecordResourceSetBind(uint setCount) => Execution?.Device.Profiler?.RecordResourceSetBind(setCount);

    private readonly List<BufferBindingInfo> _capturedVertexBuffers = new();
    private BufferBindingInfo? _capturedIndexBuffer;

    /// <summary>
    /// True when the profiler wants draw-time buffer bindings captured. Backends must check this
    /// before reporting resolved bindings via CaptureResolvedVertexBinding/CaptureResolvedIndexBinding,
    /// since building BufferBindingInfo for every draw is pure overhead when nothing consumes it.
    /// </summary>
    internal bool WantsDrawBufferCapture => Execution?.Device.Profiler?.RequestCapture ?? false;

    /// <summary>Clears capture state before a backend resolves buffers for a new draw. Only call when WantsDrawBufferCapture is true.</summary>
    internal void BeginDrawBufferCapture()
    {
        _capturedVertexBuffers.Clear();
        _capturedIndexBuffer = null;
    }

    /// <summary>
    /// Reports the vertex buffer a backend just resolved (via IVertexSource) and bound to the GPU for
    /// the current draw. Only call when WantsDrawBufferCapture is true - this must be the same
    /// resolution used for the actual GPU bind, not a second, independent query of IVertexSource.
    /// </summary>
    internal void CaptureResolvedVertexBinding(in VertexBinding binding)
    {
        _capturedVertexBuffers.Add(new BufferBindingInfo(
            binding.Buffer.Name, binding.Buffer, binding.Offset, binding.Buffer.SizeInBytes - binding.Offset,
            binding.Buffer.ContentVersion, readOnly: true));
    }

    /// <summary>
    /// Reports the index buffer a backend just resolved (via IVertexSource) and bound to the GPU for
    /// the current draw. Only call when WantsDrawBufferCapture is true - this must be the same
    /// resolution used for the actual GPU bind, not a second, independent query of IVertexSource.
    /// </summary>
    internal void CaptureResolvedIndexBinding(DeviceBuffer buffer, IndexFormat format, uint indexCount)
    {
        uint indexSize = format == IndexFormat.UInt16 ? 2u : 4u;
        _capturedIndexBuffer = new BufferBindingInfo(buffer.Name, buffer, offset: 0, indexSize * indexCount, buffer.ContentVersion, readOnly: true);
    }

    /// <summary>
    /// Reports the buffers already resolved and bound for the draw that just recorded (by the backend,
    /// via CaptureResolvedVertexBinding/CaptureResolvedIndexBinding) plus any buffer-kind entries in the
    /// active PropertySet, to the profiler. Only runs when the profiler actually requested a capture.
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

    /// <summary>True if End was called since last Begin.</summary>
    internal bool HasEnded { get; private protected set; }


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

    /// <summary>Resets and starts recording. Render context calls this on rent, not passes.</summary>
    internal abstract void Begin();

    /// <summary>Finishes recording, makes buffer executable. Render context calls this on submit, not passes.</summary>
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
    /// <param name="source">Source to bind. Not null, use an empty one for none.</param>
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

    /// <summary>Backend work for a property merge. Base class table already updated.</summary>
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
            ProfilerInfo, new DrawCallInfo(DrawKind.Draw, vertexCount, instanceCount, drawCount: 1, isIndirect: false));
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
            ProfilerInfo, new DrawCallInfo(DrawKind.DrawIndexed, _currentIndexCount, instanceCount, drawCount: 1, isIndirect: false));
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
            ProfilerInfo, new DrawCallInfo(DrawKind.DrawIndirect, vertexOrIndexCount: 0, instanceCount: 0, drawCount, isIndirect: true));
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
            ProfilerInfo, new DrawCallInfo(DrawKind.DrawIndexedIndirect, vertexOrIndexCount: 0, instanceCount: 0, drawCount, isIndirect: true));
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

    /// <summary>Updates buffer region. T must be blittable.</summary>
    /// <typeparam name="T">Upload type.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset.</param>
    /// <param name="source">Value to upload.</param>
    public unsafe void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        T source) where T : unmanaged
    {
        ref byte sourceByteRef = ref Unsafe.AsRef<byte>(Unsafe.AsPointer(ref source));
        fixed (byte* ptr = &sourceByteRef)
        {
            UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)ptr, (uint)sizeof(T));
        }
    }

    /// <summary>Updates buffer region. T must be blittable.</summary>
    /// <typeparam name="T">Upload type.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset.</param>
    /// <param name="source">Ref to value to upload.</param>
    public unsafe void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        ref T source) where T : unmanaged
    {
        ref byte sourceByteRef = ref Unsafe.AsRef<byte>(Unsafe.AsPointer(ref source));
        fixed (byte* ptr = &sourceByteRef)
        {
            UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)ptr, Util.USizeOf<T>());
        }
    }

    /// <summary>Updates buffer region. T must be blittable.</summary>
    /// <typeparam name="T">Upload type.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset.</param>
    /// <param name="source">Ref to first value in series.</param>
    /// <param name="sizeInBytes">Total upload bytes.</param>
    public unsafe void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        ref T source,
        uint sizeInBytes) where T : unmanaged
    {
        ref byte sourceByteRef = ref Unsafe.AsRef<byte>(Unsafe.AsPointer(ref source));
        fixed (byte* ptr = &sourceByteRef)
        {
            UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)ptr, sizeInBytes);
        }
    }

    /// <summary>Updates buffer region. T must be blittable.</summary>
    /// <typeparam name="T">Upload type.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset.</param>
    /// <param name="source">Array to upload.</param>
    public void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        T[] source) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
    }

    /// <summary>Updates buffer region. T must be blittable.</summary>
    /// <typeparam name="T">Upload type.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset.</param>
    /// <param name="source">Read-only span to upload.</param>
    public unsafe void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        ReadOnlySpan<T> source) where T : unmanaged
    {
        fixed (void* pin = &MemoryMarshal.GetReference(source))
        {
            UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)pin, (uint)(sizeof(T) * source.Length));
        }
    }

    /// <summary>Updates buffer region. T must be blittable.</summary>
    /// <typeparam name="T">Upload type.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset.</param>
    /// <param name="source">Span to upload.</param>
    public void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        Span<T> source) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
    }

    /// <summary>Updates buffer region.</summary>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset.</param>
    /// <param name="source">Pointer to data.</param>
    /// <param name="sizeInBytes">Total upload bytes.</param>
    public void UpdateBuffer(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        IntPtr source,
        uint sizeInBytes)
    {
        if (bufferOffsetInBytes + sizeInBytes > buffer.SizeInBytes)
        {
            throw new RenderException(
                $"The DeviceBuffer's capacity ({buffer.SizeInBytes}) is not large enough to store the amount of " +
                $"data specified ({sizeInBytes}) at the given offset ({bufferOffsetInBytes}).");
        }
        if (sizeInBytes == 0)
        {
            return;
        }

        UpdateBufferCore(buffer, bufferOffsetInBytes, source, sizeInBytes);
    }

    private protected abstract void UpdateBufferCore(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        IntPtr source,
        uint sizeInBytes);

    /// <summary>Copies a region between buffers.</summary>
    /// <param name="source">Source buffer.</param>
    /// <param name="sourceOffset">Source start offset.</param>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="destinationOffset">Destination start offset.</param>
    /// <param name="sizeInBytes">Bytes to copy.</param>
    public void CopyBuffer(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes)
    {
        ValidationHelpers.RequireNotNull(source, nameof(source), nameof(CopyBuffer));
        ValidationHelpers.RequireNotNull(destination, nameof(destination), nameof(CopyBuffer));
        if (sizeInBytes == 0)
        {
            return;
        }
        CopyBuffer_CheckRange(source, sourceOffset, destination, destinationOffset, sizeInBytes);

        CopyBufferCore(source, sourceOffset, destination, destinationOffset, sizeInBytes);
    }

    /// <summary>Backend buffer copy.</summary>
    /// <param name="source">Source buffer.</param>
    /// <param name="sourceOffset">Source byte offset.</param>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="destinationOffset">Destination byte offset.</param>
    /// <param name="sizeInBytes">Bytes to copy.</param>
    private protected abstract void CopyBufferCore(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes);

    /// <summary>Array layer count, counting cubemap faces (6 per layer).</summary>
    private static uint GetEffectiveArrayLayers(Texture texture)
        => ValidationHelpers.GetEffectiveArrayLayers(texture);

    /// <summary>Copies all subresources between textures.</summary>
    /// <param name="source">Source texture.</param>
    /// <param name="destination">Destination texture.</param>
    public void CopyTexture(Texture source, Texture destination)
    {
        CopyTexture_CheckNotNull(source, destination);
        uint effectiveSrcArrayLayers = GetEffectiveArrayLayers(source);
        CopyTexture_CheckCompatibilityAll(source, destination, effectiveSrcArrayLayers);

        for (uint level = 0; level < source.MipLevels; level++)
        {
            Util.GetMipDimensions(source, level, out uint mipWidth, out uint mipHeight, out uint mipDepth);
            CopyTexture(
                source, 0, 0, 0, level, 0,
                destination, 0, 0, 0, level, 0,
                mipWidth, mipHeight, mipDepth,
                effectiveSrcArrayLayers);
        }
    }

    /// <summary>Copies one subresource between textures.</summary>
    /// <param name="source">Source texture.</param>
    /// <param name="destination">Destination texture.</param>
    /// <param name="mipLevel">Mip level.</param>
    /// <param name="arrayLayer">Array layer.</param>
    public void CopyTexture(Texture source, Texture destination, uint mipLevel, uint arrayLayer)
    {
        CopyTexture_CheckNotNull(source, destination);
        CopyTexture_CheckCompatibilityForSubresource(source, destination, mipLevel, arrayLayer);

        Util.GetMipDimensions(source, mipLevel, out uint width, out uint height, out uint depth);
        CopyTexture(
            source, 0, 0, 0, mipLevel, arrayLayer,
            destination, 0, 0, 0, mipLevel, arrayLayer,
            width, height, depth,
            1);
    }

    /// <summary>Copies a region between textures.</summary>
    /// <param name="source">Source texture.</param>
    /// <param name="srcX">Source X.</param>
    /// <param name="srcY">Source Y.</param>
    /// <param name="srcZ">Source Z.</param>
    /// <param name="srcMipLevel">Source mip level.</param>
    /// <param name="srcBaseArrayLayer">First source layer.</param>
    /// <param name="destination">Destination texture.</param>
    /// <param name="dstX">Destination X.</param>
    /// <param name="dstY">Destination Y.</param>
    /// <param name="dstZ">Destination Z.</param>
    /// <param name="dstMipLevel">Destination mip level.</param>
    /// <param name="dstBaseArrayLayer">First destination layer.</param>
    /// <param name="width">Region width, texels.</param>
    /// <param name="height">Region height, texels.</param>
    /// <param name="depth">Region depth, texels.</param>
    /// <param name="layerCount">Layers to copy.</param>
    public void CopyTexture(
        Texture source,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel,
        uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel,
        uint dstBaseArrayLayer,
        uint width, uint height, uint depth,
        uint layerCount)
    {
        CopyTexture_CheckNotNull(source, destination);
        CopyTexture_CheckRegion(
            source,
            srcX, srcY, srcZ,
            srcMipLevel,
            srcBaseArrayLayer,
            destination,
            dstX, dstY, dstZ,
            dstMipLevel,
            dstBaseArrayLayer,
            width, height, depth,
            layerCount);
        CopyTextureCore(
            source,
            srcX, srcY, srcZ,
            srcMipLevel,
            srcBaseArrayLayer,
            destination,
            dstX, dstY, dstZ,
            dstMipLevel,
            dstBaseArrayLayer,
            width, height, depth,
            layerCount);
    }

    /// <summary>Backend texture copy.</summary>
    /// <param name="source">Source texture.</param>
    /// <param name="srcX">Source X.</param>
    /// <param name="srcY">Source Y.</param>
    /// <param name="srcZ">Source Z.</param>
    /// <param name="srcMipLevel">Source mip level.</param>
    /// <param name="srcBaseArrayLayer">First source layer.</param>
    /// <param name="destination">Destination texture.</param>
    /// <param name="dstX">Destination X.</param>
    /// <param name="dstY">Destination Y.</param>
    /// <param name="dstZ">Destination Z.</param>
    /// <param name="dstMipLevel">Destination mip level.</param>
    /// <param name="dstBaseArrayLayer">First destination layer.</param>
    /// <param name="width">Region width, texels.</param>
    /// <param name="height">Region height, texels.</param>
    /// <param name="depth">Region depth, texels.</param>
    /// <param name="layerCount">Layers to copy.</param>
    private protected abstract void CopyTextureCore(
        Texture source,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel,
        uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel,
        uint dstBaseArrayLayer,
        uint width, uint height, uint depth,
        uint layerCount);

    /// <summary>Generates mipmaps from top level, overwrites lower levels. Needs GenerateMipmaps usage flag.</summary>
    /// <param name="texture">Texture to mipmap. Needs GenerateMipmaps usage flag.</param>
    public void GenerateMipmaps(Texture texture)
    {
        if ((texture.Usage & TextureUsage.GenerateMipmaps) == 0)
        {
            throw new RenderException(
                $"{nameof(GenerateMipmaps)} requires a target Texture with {nameof(TextureUsage)}.{nameof(TextureUsage.GenerateMipmaps)}");
        }

        if (texture.MipLevels > 1)
        {
            GenerateMipmapsCore(texture);
        }
    }

    private protected abstract void GenerateMipmapsCore(Texture texture);

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

    /// <summary>Name shown in debug tools.</summary>
    public abstract string Name { get; set; }

    /// <summary>True if disposed.</summary>
    public abstract bool IsDisposed { get; }

    /// <summary>Frees unmanaged resources.</summary>
    public abstract void Dispose();
}
