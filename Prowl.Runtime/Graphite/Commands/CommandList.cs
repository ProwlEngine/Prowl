// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Records GPU commands for later submission.
/// Command lists must be recorded (Begin/End) before submission.
/// </summary>
public abstract class CommandList : IDisposable
{
    private bool _disposed;
    private bool _isRecording;
    private bool _inRenderPass;

    /// <summary>Whether the command list is currently recording.</summary>
    public bool IsRecording => _isRecording;

    /// <summary>Whether a render pass is currently active.</summary>
    public bool InRenderPass => _inRenderPass;

    #region Recording

    /// <summary>
    /// Begins recording commands. Must be called before any other commands.
    /// </summary>
    public void Begin()
    {
        ThrowIfDisposed();
        if (_isRecording)
            throw new InvalidOperationException("Command list is already recording.");

        _isRecording = true;
        BeginRecording();
    }

    /// <summary>
    /// Ends recording commands. Must be called before submission.
    /// </summary>
    public void End()
    {
        ThrowIfDisposed();
        if (!_isRecording)
            throw new InvalidOperationException("Command list is not recording.");
        if (_inRenderPass)
            throw new InvalidOperationException("Cannot end command list while a render pass is active.");

        EndRecording();
        _isRecording = false;
    }

    protected abstract void BeginRecording();
    protected abstract void EndRecording();

    #endregion

    #region Render Pass

    /// <summary>
    /// Begins a render pass with the specified attachments.
    /// </summary>
    public void BeginRenderPass(in RenderPassDescriptor descriptor)
    {
        ThrowIfNotRecording();
        if (_inRenderPass)
            throw new InvalidOperationException("A render pass is already active.");

        _inRenderPass = true;
        BeginRenderPassCore(in descriptor);
    }

    /// <summary>
    /// Ends the current render pass.
    /// </summary>
    public void EndRenderPass()
    {
        ThrowIfNotRecording();
        if (!_inRenderPass)
            throw new InvalidOperationException("No render pass is active.");

        EndRenderPassCore();
        _inRenderPass = false;
    }

    protected abstract void BeginRenderPassCore(in RenderPassDescriptor descriptor);
    protected abstract void EndRenderPassCore();

    #endregion

    #region Pipeline & Binding

    /// <summary>
    /// Sets the graphics pipeline state. Must be called within a render pass.
    /// </summary>
    public void SetPipeline(PipelineState pipeline)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        SetPipelineCore(pipeline);
    }

    /// <summary>
    /// Binds a bind group at the specified index.
    /// </summary>
    public void SetBindGroup(uint index, BindGroup bindGroup, ReadOnlySpan<uint> dynamicOffsets = default)
    {
        ThrowIfNotRecording();
        SetBindGroupCore(index, bindGroup, dynamicOffsets);
    }

    /// <summary>
    /// Sets a vertex buffer at the specified slot.
    /// </summary>
    public void SetVertexBuffer(uint slot, Buffer buffer, uint offset = 0)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        SetVertexBufferCore(slot, buffer, offset);
    }

    /// <summary>
    /// Sets the index buffer.
    /// </summary>
    public void SetIndexBuffer(Buffer buffer, IndexFormat format, uint offset = 0)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        SetIndexBufferCore(buffer, format, offset);
    }

    protected abstract void SetPipelineCore(PipelineState pipeline);
    protected abstract void SetBindGroupCore(uint index, BindGroup bindGroup, ReadOnlySpan<uint> dynamicOffsets);
    protected abstract void SetVertexBufferCore(uint slot, Buffer buffer, uint offset);
    protected abstract void SetIndexBufferCore(Buffer buffer, IndexFormat format, uint offset);

    #endregion

    #region Dynamic State

    /// <summary>
    /// Sets the viewport. Must be called within a render pass.
    /// </summary>
    public void SetViewport(float x, float y, float width, float height, float minDepth = 0, float maxDepth = 1)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        SetViewportCore(x, y, width, height, minDepth, maxDepth);
    }

    /// <summary>
    /// Sets the scissor rectangle. Must be called within a render pass.
    /// </summary>
    public void SetScissor(int x, int y, uint width, uint height)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        SetScissorCore(x, y, width, height);
    }

    /// <summary>
    /// Sets the blend constant color.
    /// </summary>
    public void SetBlendConstants(Float4 color)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        SetBlendConstantsCore(color);
    }

    /// <summary>
    /// Sets the stencil reference value.
    /// </summary>
    public void SetStencilReference(uint reference)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        SetStencilReferenceCore(reference);
    }

    protected abstract void SetViewportCore(float x, float y, float width, float height, float minDepth, float maxDepth);
    protected abstract void SetScissorCore(int x, int y, uint width, uint height);
    protected abstract void SetBlendConstantsCore(Float4 color);
    protected abstract void SetStencilReferenceCore(uint reference);

    #endregion

    #region Draw Commands

    /// <summary>
    /// Draws primitives. Must be called within a render pass.
    /// </summary>
    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        DrawCore(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    /// <summary>
    /// Draws indexed primitives. Must be called within a render pass.
    /// </summary>
    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        DrawIndexedCore(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    /// <summary>
    /// Draws primitives using indirect arguments from a buffer.
    /// </summary>
    public void DrawIndirect(Buffer indirectBuffer, uint offset = 0)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        DrawIndirectCore(indirectBuffer, offset);
    }

    /// <summary>
    /// Draws indexed primitives using indirect arguments from a buffer.
    /// </summary>
    public void DrawIndexedIndirect(Buffer indirectBuffer, uint offset = 0)
    {
        ThrowIfNotRecording();
        ThrowIfNotInRenderPass();
        DrawIndexedIndirectCore(indirectBuffer, offset);
    }

    protected abstract void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);
    protected abstract void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);
    protected abstract void DrawIndirectCore(Buffer indirectBuffer, uint offset);
    protected abstract void DrawIndexedIndirectCore(Buffer indirectBuffer, uint offset);

    #endregion

    #region Compute Commands

    /// <summary>
    /// Sets the compute pipeline state. Must be called outside a render pass.
    /// </summary>
    public void SetComputePipeline(ComputePipelineState pipeline)
    {
        ThrowIfNotRecording();
        ThrowIfInRenderPass("Compute commands cannot be issued inside a render pass.");
        SetComputePipelineCore(pipeline);
    }

    /// <summary>
    /// Dispatches a compute workload. Must be called outside a render pass.
    /// </summary>
    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        ThrowIfNotRecording();
        ThrowIfInRenderPass("Compute commands cannot be issued inside a render pass.");
        DispatchCore(groupCountX, groupCountY, groupCountZ);
    }

    /// <summary>
    /// Dispatches a compute workload using indirect arguments.
    /// </summary>
    public void DispatchIndirect(Buffer indirectBuffer, uint offset = 0)
    {
        ThrowIfNotRecording();
        ThrowIfInRenderPass("Compute commands cannot be issued inside a render pass.");
        DispatchIndirectCore(indirectBuffer, offset);
    }

    protected abstract void SetComputePipelineCore(ComputePipelineState pipeline);
    protected abstract void DispatchCore(uint groupCountX, uint groupCountY, uint groupCountZ);
    protected abstract void DispatchIndirectCore(Buffer indirectBuffer, uint offset);

    #endregion

    #region Copy Commands

    /// <summary>
    /// Copies data between buffers. Must be called outside a render pass.
    /// </summary>
    public void CopyBufferToBuffer(Buffer source, uint sourceOffset, Buffer destination, uint destinationOffset, uint size)
    {
        ThrowIfNotRecording();
        ThrowIfInRenderPass("Copy commands cannot be issued inside a render pass.");
        CopyBufferToBufferCore(source, sourceOffset, destination, destinationOffset, size);
    }

    /// <summary>
    /// Copies data from a buffer to a texture. Must be called outside a render pass.
    /// </summary>
    public void CopyBufferToTexture(in BufferTextureCopy copy)
    {
        ThrowIfNotRecording();
        ThrowIfInRenderPass("Copy commands cannot be issued inside a render pass.");
        CopyBufferToTextureCore(in copy);
    }

    /// <summary>
    /// Copies data from a texture to a buffer. Must be called outside a render pass.
    /// </summary>
    public void CopyTextureToBuffer(in BufferTextureCopy copy)
    {
        ThrowIfNotRecording();
        ThrowIfInRenderPass("Copy commands cannot be issued inside a render pass.");
        CopyTextureToBufferCore(in copy);
    }

    /// <summary>
    /// Copies data between textures. Must be called outside a render pass.
    /// </summary>
    public void CopyTextureToTexture(in TextureTextureCopy copy)
    {
        ThrowIfNotRecording();
        ThrowIfInRenderPass("Copy commands cannot be issued inside a render pass.");
        CopyTextureToTextureCore(in copy);
    }

    protected abstract void CopyBufferToBufferCore(Buffer source, uint sourceOffset, Buffer destination, uint destinationOffset, uint size);
    protected abstract void CopyBufferToTextureCore(in BufferTextureCopy copy);
    protected abstract void CopyTextureToBufferCore(in BufferTextureCopy copy);
    protected abstract void CopyTextureToTextureCore(in TextureTextureCopy copy);

    #endregion

    #region Synchronization

    /// <summary>
    /// Inserts a resource barrier for explicit synchronization.
    /// </summary>
    public void ResourceBarrier(in ResourceBarrier barrier)
    {
        ThrowIfNotRecording();
        ResourceBarrierCore(in barrier);
    }

    /// <summary>
    /// Inserts a memory barrier to ensure all previous writes are visible.
    /// </summary>
    public void MemoryBarrier()
    {
        ThrowIfNotRecording();
        MemoryBarrierCore();
    }

    protected abstract void ResourceBarrierCore(in ResourceBarrier barrier);
    protected abstract void MemoryBarrierCore();

    #endregion

    #region Debug Markers

    /// <summary>
    /// Begins a debug group for GPU profilers.
    /// </summary>
    public void PushDebugGroup(string name)
    {
        ThrowIfNotRecording();
        PushDebugGroupCore(name);
    }

    /// <summary>
    /// Ends the current debug group.
    /// </summary>
    public void PopDebugGroup()
    {
        ThrowIfNotRecording();
        PopDebugGroupCore();
    }

    /// <summary>
    /// Inserts a debug marker for GPU profilers.
    /// </summary>
    public void InsertDebugMarker(string name)
    {
        ThrowIfNotRecording();
        InsertDebugMarkerCore(name);
    }

    protected abstract void PushDebugGroupCore(string name);
    protected abstract void PopDebugGroupCore();
    protected abstract void InsertDebugMarkerCore(string name);

    #endregion

    #region Disposal

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            DisposeResources();
            GC.SuppressFinalize(this);
        }
    }

    protected abstract void DisposeResources();

    ~CommandList()
    {
        if (!_disposed)
        {
            Debug.LogWarning("CommandList was not disposed before finalization.");
        }
    }

    #endregion

    #region Helpers

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfNotRecording()
    {
        ThrowIfDisposed();
        if (!_isRecording)
            throw new InvalidOperationException("Command list is not recording. Call Begin() first.");
    }

    private void ThrowIfNotInRenderPass()
    {
        if (!_inRenderPass)
            throw new InvalidOperationException("This command requires an active render pass.");
    }

    private void ThrowIfInRenderPass(string message)
    {
        if (_inRenderPass)
            throw new InvalidOperationException(message);
    }

    #endregion
}

/// <summary>
/// Describes a buffer-to-texture or texture-to-buffer copy operation.
/// </summary>
public struct BufferTextureCopy
{
    public Buffer Buffer;
    public uint BufferOffset;
    public uint BufferRowLength;    // 0 = tightly packed
    public uint BufferImageHeight;  // 0 = tightly packed

    public Texture Texture;
    public uint MipLevel;
    public uint ArrayLayer;
    public uint X, Y, Z;
    public uint Width, Height, Depth;
}

/// <summary>
/// Describes a texture-to-texture copy operation.
/// </summary>
public struct TextureTextureCopy
{
    public Texture Source;
    public uint SourceMipLevel;
    public uint SourceArrayLayer;
    public uint SourceX, SourceY, SourceZ;

    public Texture Destination;
    public uint DestinationMipLevel;
    public uint DestinationArrayLayer;
    public uint DestinationX, DestinationY, DestinationZ;

    public uint Width, Height, Depth;
}

/// <summary>
/// Describes a resource state transition barrier.
/// </summary>
public struct ResourceBarrier
{
    public GraphiteResource Resource;
    public ResourceState StateBefore;
    public ResourceState StateAfter;

    public ResourceBarrier(GraphiteResource resource, ResourceState before, ResourceState after)
    {
        Resource = resource;
        StateBefore = before;
        StateAfter = after;
    }
}
