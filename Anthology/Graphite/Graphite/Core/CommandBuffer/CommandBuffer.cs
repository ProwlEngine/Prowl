using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


using Prowl.Vector;


namespace Prowl.Graphite;

/// <summary>
/// Records graphics commands for later execution by the device. Call Begin before recording, End when done,
/// then submit. Not thread-safe. Some commands need stuff bound first (framebuffer, shader, vertex source) -
/// see each method. Can't run twice without a reset in between.
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


    /// <summary>Merged property table for this buffer. Backend reads it at draw time.</summary>
    private protected readonly PropertySet _activeProperties = new();

    /// <summary>
    /// Gets whether <see cref="End"/> has been called on this <see cref="CommandBuffer"/> since the last
    /// <see cref="Begin"/> call. Used by the frame system to validate commands before submission.
    /// </summary>
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
    }

    /// <summary>
    /// Resets to the initial state. Call before issuing commands. Only call this if you haven't already, or
    /// after End, or after submitting.
    /// </summary>
    public abstract void Begin();

    /// <summary>
    /// Finishes recording, makes the buffer executable. Only call after Begin. Calling twice in a row without
    /// a Begin in between is an error.
    /// </summary>
    public abstract void End();

    /// <summary>
    /// Sets the active shader for rendering. Must be compatible with the bound framebuffer and buffers.
    /// Rebinds - previously bound resource sets get invalidated and need re-binding.
    /// </summary>
    /// <param name="program">The shader to set.</param>
    public void SetShader(GraphicsProgram program)
    {
        ValidationHelpers.RequireNotNullRender(program, nameof(GraphicsProgram), nameof(SetShader));
        SetShaderCore(program);
        _shaderProgram = program;
    }

    private protected abstract void SetShaderCore(GraphicsProgram program);

    /// <summary>
    /// Sets the active compute shader. Rebinds - previously bound compute resource sets get invalidated.
    /// </summary>
    /// <param name="program">The compute shader to set.</param>
    public void SetComputeShader(ComputeProgram program)
    {
        ValidationHelpers.RequireNotNullRender(program, nameof(ComputeProgram), nameof(SetComputeShader));
        SetComputeShaderCore(program);
        _computeProgram = program;
    }

    private protected abstract void SetComputeShaderCore(ComputeProgram program);

    /// <summary>
    /// Binds vertex buffers, index buffer, and topology for the next draws. Fully replaces the old source,
    /// nothing carries over.
    /// </summary>
    /// <param name="source">Source to bind. Can't be null - use an empty one if you need no vertex source.</param>
    public void SetVertexSource(IVertexSource source)
    {
        SetVertexSource_CheckNonNull(source);
        _currentVertexSource = source;
        SetVertexSourceCore(source);
    }

    private protected abstract void SetVertexSourceCore(IVertexSource source);

    /// <summary>
    /// Merges the given properties into the bind table. Last write by name wins. Sticks around until
    /// ClearProperties or Begin.
    /// <para>
    /// Calling this twice with the same unchanged set is a no-op, it's tracked and skipped.
    /// </para>
    /// </summary>
    /// <param name="properties">Property set to merge in.</param>
    public void SetProperties(PropertySet properties)
    {
        ValidationHelpers.RequireNotNull(properties, nameof(properties), nameof(SetProperties));
        _activeProperties.ApplyOther(properties);
        SetPropertiesCore(properties);
    }

    /// <summary>Backend-specific work when a property set gets merged in. Base class already updated its table.</summary>
    private protected abstract void SetPropertiesCore(PropertySet properties);

    /// <summary>
    /// Clears all merged property state. No GPU calls made.
    /// <para>
    /// Begin calls this for you.
    /// </para>
    /// </summary>
    public void ClearProperties()
    {
        _activeProperties.Clear();     // bump merged resource version
        ClearPropertiesCore();
    }

    /// <summary>Backend-specific work when property state is cleared.</summary>
    private protected abstract void ClearPropertiesCore();

    /// <summary>
    /// Sets the framebuffer to render to. Must match the active shader's output attachment count and formats.
    /// </summary>
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

    /// <summary>API-specific framebuffer handling.</summary>
    /// <param name="fb">Framebuffer.</param>
    private protected abstract void SetFramebufferCore(Framebuffer fb);

    /// <summary>Clears one color target. Index must be within the framebuffer's color attachment count.</summary>
    /// <param name="index">Color target index.</param>
    /// <param name="clearColor">Clear value.</param>
    public void ClearColorTarget(uint index, Color clearColor)
    {
        ClearColorTarget_CheckFramebuffer(index);
        ClearColorTargetCore(index, clearColor);
    }

    private protected abstract void ClearColorTargetCore(uint index, Color clearColor);

    /// <summary>Clears the depth-stencil target. Framebuffer needs a depth attachment. Stencil cleared to 0.</summary>
    /// <param name="depth">Depth clear value.</param>
    public void ClearDepthStencil(float depth)
    {
        ClearDepthStencil(depth, 0);
    }

    /// <summary>Clears the depth-stencil target. Framebuffer needs a depth attachment.</summary>
    /// <param name="depth">Depth clear value.</param>
    /// <param name="stencil">Stencil clear value.</param>
    public void ClearDepthStencil(float depth, byte stencil)
    {
        ClearDepthStencil_CheckFramebuffer();
        ClearDepthStencilCore(depth, stencil);
    }

    private protected abstract void ClearDepthStencilCore(float depth, byte stencil);

    /// <summary>Sets all viewports to cover the whole framebuffer.</summary>
    public void SetFullViewports()
    {
        CheckFramebuffer(nameof(SetFullViewports));
        SetViewport(0, new Viewport(0, 0, _framebuffer!.Width, _framebuffer.Height, 0, 1));

        for (uint index = 1; index < _framebuffer.ColorTargets.Count; index++)
            SetViewport(index, new Viewport(0, 0, _framebuffer.Width, _framebuffer.Height, 0, 1));
    }

    /// <summary>Sets one viewport to cover the whole framebuffer.</summary>
    /// <param name="index">Color target index.</param>
    public void SetFullViewport(uint index)
    {
        CheckFramebuffer(nameof(SetFullViewport));
        SetViewport(index, new Viewport(0, 0, _framebuffer!.Width, _framebuffer.Height, 0, 1));
    }

    /// <summary>Sets the viewport at the given index. Index must be within the framebuffer's color attachment count.</summary>
    /// <param name="index">Color target index.</param>
    /// <param name="viewport">New viewport.</param>
    public void SetViewport(uint index, Viewport viewport) => SetViewport(index, ref viewport);

    /// <summary>Sets the viewport at the given index. Index must be within the framebuffer's color attachment count.</summary>
    /// <param name="index">Color target index.</param>
    /// <param name="viewport">New viewport.</param>
    public abstract void SetViewport(uint index, ref Viewport viewport);

    /// <summary>Sets all scissor rects to cover the whole framebuffer.</summary>
    public void SetFullScissorRects()
    {
        CheckFramebuffer(nameof(SetFullScissorRects));
        SetScissorRect(0, 0, 0, _framebuffer!.Width, _framebuffer.Height);

        for (uint index = 1; index < _framebuffer.ColorTargets.Count; index++)
        {
            SetScissorRect(index, 0, 0, _framebuffer.Width, _framebuffer.Height);
        }
    }

    /// <summary>Sets one scissor rect to cover the whole framebuffer.</summary>
    /// <param name="index">Color target index.</param>
    public void SetFullScissorRect(uint index)
    {
        CheckFramebuffer(nameof(SetFullScissorRect));
        SetScissorRect(index, 0, 0, _framebuffer!.Width, _framebuffer.Height);
    }

    /// <summary>Sets the scissor rect at the given index. Index must be within the framebuffer's color attachment count.</summary>
    /// <param name="index">Color target index.</param>
    /// <param name="x">X of the rect.</param>
    /// <param name="y">Y of the rect.</param>
    /// <param name="width">Width of the rect.</param>
    /// <param name="height">Height of the rect.</param>
    public abstract void SetScissorRect(uint index, uint x, uint y, uint width, uint height);

    /// <summary>Draws using the currently bound state. No index buffer used.</summary>
    /// <param name="vertexCount">Vertex count.</param>
    public void Draw(uint vertexCount) => Draw(vertexCount, 1, 0, 0);

    /// <summary>Draws using the currently bound state. No index buffer used.</summary>
    /// <param name="vertexCount">Vertex count.</param>
    /// <param name="instanceCount">Instance count.</param>
    /// <param name="vertexStart">First vertex to draw.</param>
    /// <param name="instanceStart">First instance value.</param>
    public void Draw(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
    {
        Draw_PreDrawValidation();
        DrawCore(vertexCount, instanceCount, vertexStart, instanceStart);
    }

    private protected abstract void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart);

    /// <summary>Draws indexed primitives using the currently bound state.</summary>
    public void DrawIndexed() => DrawIndexed(1, 0, 0, 0);

    /// <summary>Draws indexed primitives using the currently bound state.</summary>
    /// <param name="instanceCount">Instance count.</param>
    /// <param name="indexStart">Indices to skip in the index buffer.</param>
    /// <param name="vertexOffset">Value added to each index read from the index buffer.</param>
    /// <param name="instanceStart">First instance value.</param>
    public void DrawIndexed(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        DrawIndexed_CheckIndexBuffer();
        Draw_PreDrawValidation();
        DrawIndexed_CheckBaseVertexInstance(vertexOffset, instanceStart);

        DrawIndexedCore(instanceCount, indexStart, vertexOffset, instanceStart);
    }

    private protected abstract void DrawIndexedCore(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart);

    /// <summary>Issues indirect draw commands read from the given buffer. Data must match IndirectDrawArguments layout.</summary>
    /// <param name="indirectBuffer">Buffer to read from. Needs the IndirectBuffer usage flag.</param>
    /// <param name="offset">Byte offset into the buffer to start reading. Must be a multiple of 4.</param>
    /// <param name="drawCount">Number of draw commands to issue.</param>
    /// <param name="stride">Byte stride between draw commands. Multiple of 4, bigger than IndirectDrawArguments.</param>
    public unsafe void DrawIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        DrawIndirect_CheckSupport();
        DrawIndirect_CheckBuffer(indirectBuffer);
        DrawIndirect_CheckOffset(offset);
        DrawIndirect_CheckStride(stride, sizeof(IndirectDrawArguments));
        Draw_PreDrawValidation();

        DrawIndirectCore(indirectBuffer, offset, drawCount, stride);
    }


    /// <summary>Backend indirect draw.</summary>
    /// <param name="indirectBuffer">Indirect buffer.</param>
    /// <param name="offset">Byte offset.</param>
    /// <param name="drawCount">Draw count.</param>
    /// <param name="stride">Byte stride.</param>
    private protected abstract void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride);

    /// <summary>Issues indirect indexed draw commands read from the given buffer. Data must match IndirectDrawIndexedArguments layout.</summary>
    /// <param name="indirectBuffer">Buffer to read from. Needs the IndirectBuffer usage flag.</param>
    /// <param name="offset">Byte offset into the buffer to start reading. Must be a multiple of 4.</param>
    /// <param name="drawCount">Number of draw commands to issue.</param>
    /// <param name="stride">Byte stride between draw commands. Multiple of 4, bigger than IndirectDrawIndexedArguments.</param>
    public unsafe void DrawIndexedIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        DrawIndirect_CheckSupport();
        DrawIndirect_CheckBuffer(indirectBuffer);
        DrawIndirect_CheckOffset(offset);
        DrawIndirect_CheckStride(stride, sizeof(IndirectDrawIndexedArguments));
        Draw_PreDrawValidation();

        DrawIndexedIndirectCore(indirectBuffer, offset, drawCount, stride);
    }


    /// <summary>Backend indirect indexed draw.</summary>
    /// <param name="indirectBuffer">Indirect buffer.</param>
    /// <param name="offset">Byte offset.</param>
    /// <param name="drawCount">Draw count.</param>
    /// <param name="stride">Byte stride.</param>
    private protected abstract void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride);

    /// <summary>Dispatches a compute operation using the currently bound compute state.</summary>
    /// <param name="groupCountX">Thread group count, X.</param>
    /// <param name="groupCountY">Thread group count, Y.</param>
    /// <param name="groupCountZ">Thread group count, Z.</param>
    public abstract void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ);

    /// <summary>Issues an indirect compute dispatch read from the given buffer. Data must match IndirectDispatchArguments layout.</summary>
    /// <param name="indirectBuffer">Buffer to read from. Needs the IndirectBuffer usage flag.</param>
    /// <param name="offset">Byte offset into the buffer to start reading. Must be a multiple of 4.</param>
    public void DispatchIndirect(DeviceBuffer indirectBuffer, uint offset)
    {
        DrawIndirect_CheckBuffer(indirectBuffer);
        DrawIndirect_CheckOffset(offset);
        DispatchIndirectCore(indirectBuffer, offset);
    }


    /// <summary>Backend indirect dispatch.</summary>
    /// <param name="indirectBuffer">Indirect buffer.</param>
    /// <param name="offset">Byte offset.</param>
    private protected abstract void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset);

    /// <summary>Resolves a multisampled texture into a non-multisampled one.</summary>
    /// <param name="source">Source. Must be multisampled (sample count > 1).</param>
    /// <param name="destination">Destination. Must not be multisampled (sample count == 1).</param>
    public void ResolveTexture(Texture source, Texture destination)
    {
        ResolveTexture_CheckSampleCounts(source, destination);
        ResolveTextureCore(source, destination);
    }

    /// <summary>Resolves a multisampled texture into a non-multisampled one.</summary>
    /// <param name="source">Source. Must be multisampled (sample count > 1).</param>
    /// <param name="destination">Destination. Must not be multisampled (sample count == 1).</param>
    protected abstract void ResolveTextureCore(Texture source, Texture destination);

    /// <summary>Updates a buffer region with new data. T must be blittable.</summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer.</param>
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

    /// <summary>Updates a buffer region with new data. T must be blittable.</summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer.</param>
    /// <param name="source">Reference to the value to upload.</param>
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

    /// <summary>Updates a buffer region with new data. T must be blittable.</summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer.</param>
    /// <param name="source">Reference to the first value in a series to upload.</param>
    /// <param name="sizeInBytes">Total upload size in bytes.</param>
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

    /// <summary>Updates a buffer region with new data. T must be blittable.</summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer.</param>
    /// <param name="source">Array of data to upload.</param>
    public void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        T[] source) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
    }

    /// <summary>Updates a buffer region with new data. T must be blittable.</summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer.</param>
    /// <param name="source">Read-only span of data to upload.</param>
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

    /// <summary>Updates a buffer region with new data. T must be blittable.</summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer.</param>
    /// <param name="source">Span of data to upload.</param>
    public void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        Span<T> source) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
    }

    /// <summary>Updates a buffer region with new data.</summary>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer.</param>
    /// <param name="source">Pointer to the data to upload.</param>
    /// <param name="sizeInBytes">Total upload size in bytes.</param>
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

    /// <summary>Copies a region from one buffer to another.</summary>
    /// <param name="source">Source buffer.</param>
    /// <param name="sourceOffset">Byte offset into source where the copy starts.</param>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="destinationOffset">Byte offset into destination where the copy starts.</param>
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

    /// <summary>Array layer count for a texture, counting cubemap faces (6 per layer).</summary>
    private static uint GetEffectiveArrayLayers(Texture texture)
        => ValidationHelpers.GetEffectiveArrayLayers(texture);

    /// <summary>Copies all subresources from one texture to another.</summary>
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

    /// <summary>Copies one subresource from one texture to another.</summary>
    /// <param name="source">Source texture.</param>
    /// <param name="destination">Destination texture.</param>
    /// <param name="mipLevel">Mip level to copy.</param>
    /// <param name="arrayLayer">Array layer to copy.</param>
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

    /// <summary>Copies a region from one texture to another.</summary>
    /// <param name="source">Source texture.</param>
    /// <param name="srcX">Source region X.</param>
    /// <param name="srcY">Source region Y.</param>
    /// <param name="srcZ">Source region Z.</param>
    /// <param name="srcMipLevel">Source mip level.</param>
    /// <param name="srcBaseArrayLayer">First source array layer.</param>
    /// <param name="destination">Destination texture.</param>
    /// <param name="dstX">Destination region X.</param>
    /// <param name="dstY">Destination region Y.</param>
    /// <param name="dstZ">Destination region Z.</param>
    /// <param name="dstMipLevel">Destination mip level.</param>
    /// <param name="dstBaseArrayLayer">First destination array layer.</param>
    /// <param name="width">Region width in texels.</param>
    /// <param name="height">Region height in texels.</param>
    /// <param name="depth">Region depth in texels.</param>
    /// <param name="layerCount">Array layers to copy.</param>
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
    /// <param name="srcX">Source region X.</param>
    /// <param name="srcY">Source region Y.</param>
    /// <param name="srcZ">Source region Z.</param>
    /// <param name="srcMipLevel">Source mip level.</param>
    /// <param name="srcBaseArrayLayer">First source array layer.</param>
    /// <param name="destination">Destination texture.</param>
    /// <param name="dstX">Destination region X.</param>
    /// <param name="dstY">Destination region Y.</param>
    /// <param name="dstZ">Destination region Z.</param>
    /// <param name="dstMipLevel">Destination mip level.</param>
    /// <param name="dstBaseArrayLayer">First destination array layer.</param>
    /// <param name="width">Region width in texels.</param>
    /// <param name="height">Region height in texels.</param>
    /// <param name="depth">Region depth in texels.</param>
    /// <param name="layerCount">Array layers to copy.</param>
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

    /// <summary>
    /// Generates mipmaps from the top level, overwriting lower levels. Texture needs the GenerateMipmaps usage flag.
    /// </summary>
    /// <param name="texture">Texture to generate mipmaps for. Needs the GenerateMipmaps usage flag.</param>
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

    /// <summary>
    /// Pushes a debug group, for grouping/filtering commands in debug tools. Can nest. Every push needs a
    /// matching pop.
    /// </summary>
    /// <param name="name">Group name, shown in debug tools.</param>
    public void PushDebugGroup(string name)
    {
        PushDebugGroupCore(name);
    }

    private protected abstract void PushDebugGroupCore(string name);

    /// <summary>Pops the current debug group. Only call after a matching push.</summary>
    public void PopDebugGroup()
    {
        PopDebugGroupCore();
    }

    private protected abstract void PopDebugGroupCore();

    /// <summary>Inserts a debug marker at the current position, for spotting points of interest in debug tools.</summary>
    /// <param name="name">Marker name, shown in debug tools.</param>
    public void InsertDebugMarker(string name)
    {
        InsertDebugMarkerCore(name);
    }

    private protected abstract void InsertDebugMarkerCore(string name);

    /// <summary>Name for identifying this instance in debug tools.</summary>
    public abstract string Name { get; set; }

    /// <summary>Whether this instance has been disposed.</summary>
    public abstract bool IsDisposed { get; }

    /// <summary>Frees unmanaged resources.</summary>
    public abstract void Dispose();
}
