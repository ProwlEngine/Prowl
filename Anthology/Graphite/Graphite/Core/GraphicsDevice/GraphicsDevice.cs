using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Prowl.Graphite;

/// <summary>
/// Abstract graphics device. Creates resources and runs commands.
/// </summary>
public abstract partial class GraphicsDevice : IDisposable
{
    private readonly object _deferredDisposalLock = new();
    private readonly List<IDisposable> _disposables = [];
    private List<IDisposable>[] _frameRetiredDisposables;
    private Sampler _aniso4xSampler;
    private bool _disposed;
    private readonly object _nullTextureLock = new();
    private DeviceBuffer _nullStructuredRead;
    private DeviceBuffer _nullStructuredReadWrite;

    /// <summary>Max graph executions in flight at once.</summary>
    protected internal uint _maxExecutingGraphs;

    /// <summary>Starting size of each slot's transient bump-allocator buffer, in bytes.</summary>
    protected internal uint _transientInitialSize;

    /// <summary>Soft cap for per-execution transient usage, in bytes.</summary>
    protected internal uint _transientSoftCapBytes;

    /// <summary>Hard cap for per-execution transient usage, in bytes.</summary>
    protected internal uint _transientHardCapBytes;

    /// <summary>Execution counter, always going up. 0 means nothing has started yet.</summary>
    protected ulong _executionIdCounter;

    /// <summary>The FrameId of the most recently completed frame, updated opportunistically.</summary>
    protected ulong _lastCompletedFrameId;

    /// <summary>True once the soft cap warning has fired once.</summary>
    protected internal bool _transientSoftCapWarned;

    internal GraphicsDevice() { }

    /// <summary>
    /// Device name.
    /// </summary>
    public abstract string DeviceName { get; }

    /// <summary>
    /// Device vendor name.
    /// </summary>
    public abstract string VendorName { get; }

    /// <summary>
    /// API version of the backend.
    /// </summary>
    public abstract GraphicsApiVersion ApiVersion { get; }

    /// <summary>
    /// Which graphics API this device is.
    /// </summary>
    public abstract GraphicsBackend BackendType { get; }

    /// <summary>
    /// True if texture (0,0) is top-left. False if it's bottom-left. Matters for sampling framebuffer output.
    /// </summary>
    public abstract bool IsUvOriginTopLeft { get; }

    /// <summary>
    /// True if depth range is 0 to 1. False means -1 to 1.
    /// </summary>
    public abstract bool IsDepthRangeZeroToOne { get; }

    /// <summary>
    /// True if clip space Y goes top(-1) to bottom(1). False means bottom(-1) to top(1).
    /// </summary>
    public abstract bool IsClipSpaceYInverted { get; }

    /// <summary>
    /// The resource factory this device owns.
    /// </summary>
    public abstract ResourceFactory ResourceFactory { get; }

    /// <summary>
    /// Main swapchain for this device. Null if the device has no main swapchain.
    /// </summary>
    public abstract Swapchain MainSwapchain { get; }

    /// <summary>
    /// Optional features this device supports.
    /// </summary>
    public abstract GraphicsDeviceFeatures Features { get; }

    /// <summary>
    /// Whether the main swapchain syncs to vertical refresh. Can't set this without a main swapchain.
    /// </summary>
    public virtual bool SyncToVerticalBlank
    {
        get => MainSwapchain?.SyncToVerticalBlank ?? false;
        set
        {
            SyncToVerticalBlank_CheckMainSwapchain();
            MainSwapchain.SyncToVerticalBlank = value;
        }
    }

    /// <summary>
    /// Required byte alignment for uniform buffer offsets. Offsets, including dynamic offsets, must be a multiple of this.
    /// </summary>
    public uint UniformBufferMinOffsetAlignment => GetUniformBufferMinOffsetAlignmentCore();

    /// <summary>
    /// Required byte alignment for structured buffer offsets. Offsets, including dynamic offsets, must be a multiple of this.
    /// </summary>
    public uint StructuredBufferMinOffsetAlignment => GetStructuredBufferMinOffsetAlignmentCore();

    internal abstract uint GetUniformBufferMinOffsetAlignmentCore();
    internal abstract uint GetStructuredBufferMinOffsetAlignmentCore();

    private Frame _currentFrame;

    /// <summary>
    /// Hard cap on how many graph executions can be in flight at once. Beyond this, BeginGraphExecution blocks until the oldest finishes.
    /// </summary>
    public Frame CurrentFrame
    {
        get
        {
            CurrentFrame_CheckActive();
            return _currentFrame;
        }
    }

    /// <summary>
    /// Gets the <see cref="Frame.FrameId"/> of the most recently GPU-completed frame.
    /// This value advances opportunistically during <see cref="IsFrameComplete(ulong)"/>,
    /// <see cref="WaitForFrame(ulong)"/>, and <see cref="BeginFrame"/> calls.
    /// Returns 0 before any frame has completed.
    /// </summary>
    public ulong LastCompletedFrameId => Volatile.Read(ref _lastCompletedFrameId);

    /// <summary>
    /// Gets the maximum number of frames that may be simultaneously in flight on the GPU.
    /// </summary>
    public uint MaxFramesInFlight => _maxFramesInFlight;

    /// <summary>
    /// Gets the number of frames currently in flight (submitted to the GPU but not yet signaled as complete).
    /// </summary>
    public uint FramesInFlight => (uint)(_frameIdCounter - Volatile.Read(ref _lastCompletedFrameId));

    /// <summary>
    /// Whether the given execution has finished on the GPU. Advances LastCompletedExecutionId as a side effect.
    /// </summary>
    /// <remarks>
    /// Typical usage:
    /// <code>
    /// Frame frame = device.BeginFrame();
    /// frame.SubmitCommands(commandBuffer);
    /// device.EndFrame(frame);
    /// device.SwapBuffers();
    /// </code>
    /// </remarks>
    /// <returns>The new active <see cref="Frame"/>.</returns>
    /// <exception cref="RenderException">Thrown if a frame is already active.</exception>
    public Frame BeginFrame()
    {
        BeginFrame_CheckNoActive();
        BeginFrame_SnapshotFrameCounters();

        ulong frameId = ++_frameIdCounter;
        uint ringSlot = (uint)((frameId - 1) % _maxFramesInFlight);
        Frame frame = BeginFrameCore(frameId, ringSlot);
        FlushFrameRetiredDisposables(ringSlot);
        _currentFrame = frame;
        return frame;
    }

    /// <summary>
    /// Schedules the given object for disposal once the frame identified by <paramref name="frameId"/> has completed
    /// on the GPU. Unlike <see cref="DisposeWhenIdle"/>, this does not wait for the whole device to go idle: it is
    /// freed the next time this frame's ring slot is reused, which <see cref="BeginFrame"/> already guarantees means
    /// the prior occupant of that slot has finished on the GPU.
    /// </summary>
    /// <param name="frameId">The frame whose completion should gate disposal.</param>
    /// <param name="disposable">An object to dispose once <paramref name="frameId"/> has completed.</param>
    internal void DisposeWhenFrameComplete(ulong frameId, IDisposable disposable)
    {
        uint ringSlot = (uint)((frameId - 1) % _maxFramesInFlight);
        lock (_deferredDisposalLock)
        {
            _frameRetiredDisposables[ringSlot].Add(disposable);
        }
    }

    private void FlushFrameRetiredDisposables(uint ringSlot)
    {
        lock (_deferredDisposalLock)
        {
            List<IDisposable> pending = _frameRetiredDisposables[ringSlot];
            foreach (IDisposable disposable in pending)
                disposable.Dispose();
            pending.Clear();
        }
    }

    /// <summary>
    /// Ends the currently active frame and signals the GPU to mark its completion fence.
    /// Equivalent to <c>EndFrame(CurrentFrame)</c>.
    /// This method does not block.
    /// </summary>
    /// <exception cref="RenderException">Thrown if no frame is currently active.</exception>
    public void EndFrame()
    {
        EndFrame_CheckHasActive();
        EndFrame(_currentFrame);
    }

    /// <summary>
    /// Ends the specified frame and signals the GPU to mark its completion fence.
    /// This method does not block.
    /// </summary>
    /// <param name="frame">The frame to end. Must be the currently active frame.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="frame"/> is null.</exception>
    /// <exception cref="RenderException">Thrown if <paramref name="frame"/> is not the currently active frame.</exception>
    public void EndFrame(Frame frame)
    {
        ValidationHelpers.RequireNotNull(frame, nameof(frame), nameof(EndFrame));
        EndFrame_CheckIsActive(frame);
        _currentFrame = null;
        EndFrameCore(frame);
    }

    /// <summary>
    /// Returns whether the frame with the given <see cref="Frame.FrameId"/> has completed on the GPU.
    /// Also opportunistically advances <see cref="LastCompletedFrameId"/> when new completions are detected.
    /// </summary>
    /// <param name="frameId">The frame ID to query. Must be greater than 0 and at most <see cref="LastCompletedFrameId"/> + <see cref="MaxFramesInFlight"/>.</param>
    /// <returns>True if the frame has completed; false if it is still in flight or currently open.</returns>
    /// <exception cref="RenderException">Thrown if <paramref name="frameId"/> is 0 or has not yet been started.</exception>
    public bool IsFrameComplete(ulong frameId)
    {
        if (frameId == 0 || frameId > _frameIdCounter)
            throw new RenderException($"Cannot query frame {frameId}: it has not been started yet.");
        if (frameId <= Volatile.Read(ref _lastCompletedFrameId))
            return true;
        bool complete = IsFrameCompleteCore(frameId);
        if (complete)
            Volatile.Write(ref _lastCompletedFrameId, Math.Max(Volatile.Read(ref _lastCompletedFrameId), frameId));
        return complete;
    }

    /// <summary>
    /// Returns whether the given <see cref="Frame"/> has completed on the GPU.
    /// </summary>
    /// <param name="frame">The frame to query.</param>
    /// <returns>True if the frame has completed; false otherwise.</returns>
    public bool IsFrameComplete(Frame frame) => IsFrameComplete(frame.FrameId);

    /// <summary>
    /// Returns whether <paramref name="frameId"/> identifies the currently open (still being recorded) frame.
    /// A frame in this state has not been submitted yet, so nothing referencing it is actually in flight on
    /// the GPU, even though it has not "completed" either.
    /// </summary>
    internal bool IsFrameOpen(ulong frameId) => _currentFrame != null && _currentFrame.FrameId == frameId;

    /// <summary>
    /// Blocks the calling thread until the frame with the given <see cref="Frame.FrameId"/> has completed on the GPU.
    /// </summary>
    /// <param name="frameId">The frame ID to wait for.</param>
    /// <exception cref="RenderException">Thrown if <paramref name="frameId"/> is the currently open frame, is 0, or has not been started.</exception>
    public void WaitForFrame(ulong frameId)
    {
        if (!WaitForFrame(frameId, ulong.MaxValue))
            throw new RenderException("The operation timed out before the frame completed.");
    }

    /// <summary>
    /// Blocks the calling thread until the frame with the given <see cref="Frame.FrameId"/> has completed on the GPU,
    /// or until the timeout elapses.
    /// </summary>
    /// <param name="frameId">The frame ID to wait for.</param>
    /// <param name="nanosecondTimeout">Maximum time to wait, in nanoseconds. Pass <see cref="ulong.MaxValue"/> for infinite wait.</param>
    /// <returns>True if the frame completed before the timeout; false otherwise.</returns>
    /// <exception cref="RenderException">Thrown if <paramref name="frameId"/> is the currently open frame, is 0, or has not been started.</exception>
    public bool WaitForFrame(ulong frameId, ulong nanosecondTimeout)
    {
        if (frameId == 0 || frameId > _frameIdCounter)
            throw new RenderException($"Cannot wait on frame {frameId}: it has not been started yet.");
        if (_currentFrame != null && _currentFrame.FrameId == frameId)
            throw new RenderException("Cannot wait on the currently open frame. Call EndFrame first.");
        if (frameId <= Volatile.Read(ref _lastCompletedFrameId))
            return true;
        bool completed = WaitForFrameCore(frameId, nanosecondTimeout);
        if (completed)
            Volatile.Write(ref _lastCompletedFrameId, Math.Max(Volatile.Read(ref _lastCompletedFrameId), frameId));
        return completed;
    }

    /// <summary>
    /// Blocks the calling thread until the given <see cref="Frame"/> has completed on the GPU.
    /// </summary>
    /// <param name="frame">The frame to wait for.</param>
    public void WaitForFrame(Frame frame) => WaitForFrame(frame.FrameId);

    /// <summary>
    /// Blocks the calling thread until the given <see cref="Frame"/> has completed on the GPU,
    /// or until the timeout elapses.
    /// </summary>
    /// <param name="frame">The frame to wait for.</param>
    /// <param name="nanosecondTimeout">Maximum time to wait, in nanoseconds.</param>
    /// <returns>True if the frame completed before the timeout; false otherwise.</returns>
    public bool WaitForFrame(Frame frame, ulong nanosecondTimeout) => WaitForFrame(frame.FrameId, nanosecondTimeout);

    /// <summary>
    /// Submits a recorded <see cref="TransferCommandBuffer"/> for immediate execution and blocks the calling
    /// thread until the GPU has finished executing it. Unlike <see cref="Frame.SubmitCommands(CommandBuffer)"/>,
    /// this does not require a <see cref="Frame"/> to be open, and may be called whether or not one is: it does
    /// not touch the frame ring-buffer or its fences at all. Intended for one-off transfer work such as texture
    /// read-back or streaming uploads that would otherwise require opening a throwaway Frame.
    /// </summary>
    /// <param name="commandBuffer">The recorded <see cref="TransferCommandBuffer"/> to submit. <see cref="TransferCommandBuffer.End"/>
    /// must have been called on it first.</param>
    public void SubmitAndWait(TransferCommandBuffer commandBuffer)
    {
        SubmitAndWait_CheckEnded(commandBuffer);
        SubmitAndWaitCore(commandBuffer);
    }

    private protected virtual void SubmitAndWaitCore(TransferCommandBuffer commandBuffer)
    {
        throw new RenderException($"{GetType().Name} does not support {nameof(SubmitAndWait)}.");
    }

    /// <summary>
    /// Allocates a transient <see cref="DeviceBufferRange"/> from the currently active frame's bump allocator.
    /// Convenience wrapper over <see cref="Frame.AllocateTransient"/>. A frame must be active.
    /// </summary>
    /// <param name="sizeInBytes">The number of bytes to allocate.</param>
    /// <returns>A <see cref="DeviceBufferRange"/> pointing into the frame's transient buffer.</returns>
    /// <exception cref="RenderException">Thrown if no frame is currently active.</exception>
    public DeviceBufferRange AllocateTransient(uint sizeInBytes)
    {
        if (_currentFrame == null)
            throw new RenderException("AllocateTransient requires an active frame. Call BeginFrame first.");
        return _currentFrame.AllocateTransient(sizeInBytes);
    }

    private protected abstract Frame BeginFrameCore(ulong frameId, uint ringSlot);
    private protected abstract void EndFrameCore(Frame frame);
    private protected abstract bool IsFrameCompleteCore(ulong frameId);
    private protected abstract bool WaitForFrameCore(ulong frameId, ulong nanosecondTimeout);

    /// <summary>
    /// Initializes the frame system options from the given <see cref="GraphicsDeviceOptions"/>.
    /// Call this before <see cref="PostDeviceCreated"/> in each backend constructor.
    /// </summary>
    /// <param name="options">The options to read from.</param>
    /// <exception cref="RenderException">Thrown if <see cref="GraphicsDeviceOptions.MaxFramesInFlight"/> is 0.</exception>
    protected void InitializeFrameOptions(GraphicsDeviceOptions options)
    {
        _maxFramesInFlight = options.MaxFramesInFlight == 0 ? 3 : options.MaxFramesInFlight;
        _frameRetiredDisposables = new List<IDisposable>[_maxFramesInFlight];
        for (int i = 0; i < _frameRetiredDisposables.Length; i++)
            _frameRetiredDisposables[i] = [];
        _transientInitialSize = options.TransientBufferInitialSize == 0 ? 4 * 1024 * 1024 : options.TransientBufferInitialSize;
        _transientSoftCapBytes = options.TransientBufferSoftCapBytes == 0 ? 64 * 1024 * 1024 : options.TransientBufferSoftCapBytes;
        _transientHardCapBytes = options.TransientBufferHardCapBytes == 0 ? 256 * 1024 * 1024 : options.TransientBufferHardCapBytes;

        if (_transientSoftCapBytes < _transientInitialSize)
            _transientSoftCapBytes = _transientInitialSize;
        if (_transientHardCapBytes < _transientSoftCapBytes)
            _transientHardCapBytes = _transientSoftCapBytes;

        InitializeFrameOptions_SetValidationEnabled(options);
        InitializeFrameOptions_InitializeProfiling(options);
    }

    /// <summary>
    /// Blocks the calling thread until the given <see cref="Fence"/> becomes signaled.
    /// </summary>
    /// <param name="fence">The <see cref="Fence"/> instance to wait on.</param>
    public void WaitForFence(Fence fence)
    {
        if (!WaitForFence(fence, ulong.MaxValue))
        {
            throw new RenderException("The operation timed out before the Fence was signaled.");
        }
    }

    /// <summary>
    /// Blocks until the given fence signals, or until the timeout passes.
    /// </summary>
    /// <param name="fence">Fence to wait on.</param>
    /// <param name="timeout">Max time to wait.</param>
    /// <returns>True if signaled, false if timed out.</returns>
    public bool WaitForFence(Fence fence, TimeSpan timeout)
        => WaitForFence(fence, (ulong)timeout.TotalMilliseconds * 1_000_000);
    /// <summary>
    /// Blocks until the given fence signals, or until the timeout passes.
    /// </summary>
    /// <param name="fence">Fence to wait on.</param>
    /// <param name="nanosecondTimeout">Max wait time in nanoseconds.</param>
    /// <returns>True if signaled, false if timed out.</returns>
    public abstract bool WaitForFence(Fence fence, ulong nanosecondTimeout);

    /// <summary>
    /// Resets the given fence to unsignaled.
    /// </summary>
    /// <param name="fence">Fence to reset.</param>
    public abstract void ResetFence(Fence fence);

    /// <summary>
    /// Swaps the main swapchain's buffers and presents to screen. Only works if this device has a main swapchain.
    /// </summary>
    public void SwapBuffers()
    {
        if (MainSwapchain == null)
        {
            throw new RenderException("This GraphicsDevice was created without a main Swapchain, so the requested operation cannot be performed.");
        }

        SwapBuffers(MainSwapchain);
    }

    /// <summary>
    /// Swaps the buffers of the given swapchain.
    /// </summary>
    /// <param name="swapchain">Swapchain to swap and present.</param>
    public void SwapBuffers(Swapchain swapchain)
    {
        SwapBuffersCore(swapchain);
        RecordSwap(SwapBin.Present, 0);
    }

    private protected abstract void SwapBuffersCore(Swapchain swapchain);

    /// <summary>
    /// The main swapchain's framebuffer. Null if there's no main swapchain.
    /// </summary>
    public Framebuffer? SwapchainFramebuffer => MainSwapchain?.Framebuffer;

    /// <summary>
    /// Tells this device the main window was resized, so the swapchain framebuffer gets resized and recreated. Only works if this device has a main swapchain.
    /// </summary>
    /// <param name="width">New window width.</param>
    /// <param name="height">New window height.</param>
    public void ResizeMainWindow(uint width, uint height)
    {
        if (MainSwapchain == null)
        {
            throw new RenderException("This GraphicsDevice was created without a main Swapchain, so the requested operation cannot be performed.");
        }

        MainSwapchain.Resize(width, height);
    }

    /// <summary>
    /// Blocks until all submitted command buffers and in-flight graph executions are done. Reclaims every execution slot.
    /// </summary>
    public void WaitForIdle()
    {
        WaitForIdleCore();
        lock (_executionLock)
        {
            Volatile.Write(ref _lastCompletedExecutionId, _executionIdCounter);
            _activeTasks.Clear();
            _freeSlots.Clear();
            for (uint i = 0; i < _maxExecutingGraphs; i++)
                _freeSlots.Enqueue(i);
        }
        FlushExecutionRetiredDisposables();
        FlushDeferredDisposals();
    }

    private protected abstract void WaitForIdleCore();

    /// <summary>
    /// Max sample count this pixel format supports.
    /// </summary>
    /// <param name="format">Format to check.</param>
    /// <param name="depthFormat">Whether it's for a depth texture.</param>
    /// <returns>Max sample count a texture of that format can use.</returns>
    public abstract TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat);

    /// <summary>
    /// Maps a buffer or texture into CPU-accessible memory. For textures, maps the first subresource.
    /// </summary>
    /// <param name="resource">Buffer or texture to map.</param>
    /// <param name="mode">Map mode to use.</param>
    /// <returns>The mapped data region.</returns>
    public MappedResource Map(MappableResource resource, MapMode mode) => Map(resource, mode, 0);
    /// <summary>
    /// Maps a buffer or texture into CPU-accessible memory.
    /// </summary>
    /// <param name="resource">Buffer or texture to map.</param>
    /// <param name="mode">Map mode to use.</param>
    /// <param name="subresource">Subresource to map, indexed by mip then array layer. Must be 0 for buffers.</param>
    /// <returns>The mapped data region.</returns>
    public MappedResource Map(MappableResource resource, MapMode mode, uint subresource)
    {
        Map_CheckResource(resource, mode, subresource);

        if ((mode == MapMode.Write || mode == MapMode.ReadWrite) && resource is DeviceBuffer mapBuffer)
            mapBuffer.EnsureWritable();

        MappedResource mapped = MapCore(resource, mode, subresource);
        RecordBufferOp(BufferOpBin.Map, mapped.SizeInBytes);
        return mapped;
    }

    /// <summary>
    /// Maps the resource. Backend-specific.
    /// </summary>
    /// <param name="resource">Resource to map.</param>
    /// <param name="mode">Map mode.</param>
    /// <param name="subresource">Subresource index.</param>
    /// <returns>The mapped data region.</returns>
    protected abstract MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource);

    /// <summary>
    /// Maps a buffer or texture into CPU-accessible memory, viewed as a struct type. For textures, maps the first subresource.
    /// </summary>
    /// <param name="resource">Buffer or texture to map.</param>
    /// <param name="mode">Map mode to use.</param>
    /// <typeparam name="T">Blittable type to view the data as.</typeparam>
    /// <returns>The mapped data region.</returns>
    public MappedResourceView<T> Map<T>(MappableResource resource, MapMode mode) where T : unmanaged
        => Map<T>(resource, mode, 0);
    /// <summary>
    /// Maps a buffer or texture into CPU-accessible memory, viewed as a struct type.
    /// </summary>
    /// <param name="resource">Buffer or texture to map.</param>
    /// <param name="mode">Map mode to use.</param>
    /// <param name="subresource">Subresource to map, indexed by mip then array layer.</param>
    /// <typeparam name="T">Blittable type to view the data as.</typeparam>
    /// <returns>The mapped data region.</returns>
    public MappedResourceView<T> Map<T>(MappableResource resource, MapMode mode, uint subresource) where T : unmanaged
    {
        MappedResource mappedResource = Map(resource, mode, subresource);
        return new MappedResourceView<T>(mappedResource);
    }

    /// <summary>
    /// Unmaps a previously mapped buffer or texture. For textures, unmaps the first subresource.
    /// </summary>
    /// <param name="resource">Resource to unmap.</param>
    public void Unmap(MappableResource resource) => Unmap(resource, 0);
    /// <summary>
    /// Unmaps a previously mapped buffer or texture.
    /// </summary>
    /// <param name="resource">Resource to unmap.</param>
    /// <param name="subresource">Subresource to unmap, indexed by mip then array layer. Must be 0 for buffers.</param>
    public void Unmap(MappableResource resource, uint subresource)
    {
        UnmapCore(resource, subresource);
        RecordBufferOp(BufferOpBin.Unmap, 0);
    }

    /// <summary>
    /// Unmaps the resource. Backend-specific.
    /// </summary>
    /// <param name="resource">Resource to unmap.</param>
    /// <param name="subresource">Subresource index.</param>
    protected abstract void UnmapCore(MappableResource resource, uint subresource);

    /// <summary>
    /// Updates part of a texture with new data.
    /// </summary>
    /// <param name="texture">Texture to update.</param>
    /// <param name="source">Pointer to tightly-packed pixel data for the region.</param>
    /// <param name="sizeInBytes">Byte count to upload. Must match the region's total size.</param>
    /// <param name="x">Min X of the updated region.</param>
    /// <param name="y">Min Y of the updated region.</param>
    /// <param name="z">Min Z of the updated region.</param>
    /// <param name="width">Region width in texels.</param>
    /// <param name="height">Region height in texels.</param>
    /// <param name="depth">Region depth in texels.</param>
    /// <param name="mipLevel">Mip level to update. Must be under the texture's mip count.</param>
    /// <param name="arrayLayer">Array layer to update. Must be under the texture's array layer count.</param>
    public void UpdateTexture(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer)
    {
        UpdateTexture_CheckParameters(texture, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
        UpdateTextureCore(texture, source, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
        RecordBufferOp(BufferOpBin.Update, sizeInBytes);
    }

    /// <summary>
    /// Updates part of a texture with data from an array.
    /// </summary>
    /// <param name="texture">Texture to update.</param>
    /// <param name="source">Array with tightly-packed pixel data for the region.</param>
    /// <param name="x">Min X of the updated region.</param>
    /// <param name="y">Min Y of the updated region.</param>
    /// <param name="z">Min Z of the updated region.</param>
    /// <param name="width">Region width in texels.</param>
    /// <param name="height">Region height in texels.</param>
    /// <param name="depth">Region depth in texels.</param>
    /// <param name="mipLevel">Mip level to update. Must be under the texture's mip count.</param>
    /// <param name="arrayLayer">Array layer to update. Must be under the texture's array layer count.</param>
    public void UpdateTexture<T>(
        Texture texture,
        T[] source,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer) where T : unmanaged
    {
        UpdateTexture(texture, (ReadOnlySpan<T>)source, x, y, z, width, height, depth, mipLevel, arrayLayer);
    }

    /// <summary>
    /// Updates part of a texture with data from a span.
    /// </summary>
    /// <param name="texture">Texture to update.</param>
    /// <param name="source">Span with tightly-packed pixel data for the region.</param>
    /// <param name="x">Min X of the updated region.</param>
    /// <param name="y">Min Y of the updated region.</param>
    /// <param name="z">Min Z of the updated region.</param>
    /// <param name="width">Region width in texels.</param>
    /// <param name="height">Region height in texels.</param>
    /// <param name="depth">Region depth in texels.</param>
    /// <param name="mipLevel">Mip level to update. Must be under the texture's mip count.</param>
    /// <param name="arrayLayer">Array layer to update. Must be under the texture's array layer count.</param>
    public unsafe void UpdateTexture<T>(
        Texture texture,
        ReadOnlySpan<T> source,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer) where T : unmanaged
    {
        uint sizeInBytes = (uint)(sizeof(T) * source.Length);
        UpdateTexture_CheckParameters(texture, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);

        fixed (void* pin = &MemoryMarshal.GetReference(source))
        {
            UpdateTextureCore(
            texture,
            (IntPtr)pin,
            sizeInBytes,
            x, y, z,
            width, height, depth,
            mipLevel, arrayLayer);
        }
    }

    /// <summary>
    /// Updates part of a texture with data from a span.
    /// </summary>
    /// <param name="texture">Texture to update.</param>
    /// <param name="source">Span with tightly-packed pixel data for the region.</param>
    /// <param name="x">Min X of the updated region.</param>
    /// <param name="y">Min Y of the updated region.</param>
    /// <param name="z">Min Z of the updated region.</param>
    /// <param name="width">Region width in texels.</param>
    /// <param name="height">Region height in texels.</param>
    /// <param name="depth">Region depth in texels.</param>
    /// <param name="mipLevel">Mip level to update. Must be under the texture's mip count.</param>
    /// <param name="arrayLayer">Array layer to update. Must be under the texture's array layer count.</param>
    public void UpdateTexture<T>(
        Texture texture,
        Span<T> source,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer) where T : unmanaged
    {
        UpdateTexture(texture, (ReadOnlySpan<T>)source, x, y, z, width, height, depth, mipLevel, arrayLayer);
    }

    private protected abstract void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer);

    /// <summary>
    /// Updates a buffer region with new data. Type T must be blittable.
    /// </summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer to write at.</param>
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

    /// <summary>
    /// Updates a buffer region with new data. Type T must be blittable.
    /// </summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer to write at.</param>
    /// <param name="source">Reference to the value to upload.</param>
    public unsafe void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        ref T source) where T : unmanaged
    {
        ref byte sourceByteRef = ref Unsafe.AsRef<byte>(Unsafe.AsPointer(ref source));
        fixed (byte* ptr = &sourceByteRef)
        {
            UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)ptr, (uint)sizeof(T));
        }
    }

    /// <summary>
    /// Updates a buffer region with new data. Type T must be blittable.
    /// </summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer to write at.</param>
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

    /// <summary>
    /// Updates a buffer region with new data. Type T must be blittable.
    /// </summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer to write at.</param>
    /// <param name="source">Array with the data to upload.</param>
    public void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        T[] source) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
    }

    /// <summary>
    /// Updates a buffer region with new data. Type T must be blittable.
    /// </summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer to write at.</param>
    /// <param name="source">Span with the data to upload.</param>
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

    /// <summary>
    /// Updates a buffer region with new data. Type T must be blittable.
    /// </summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer to write at.</param>
    /// <param name="source">Span with the data to upload.</param>
    public void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        Span<T> source) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
    }

    /// <summary>
    /// Updates a buffer region with new data.
    /// </summary>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset into the buffer to write at.</param>
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
                $"The data size given to UpdateBuffer is too large. The given buffer can only hold {buffer.SizeInBytes} total bytes. The requested update would require {bufferOffsetInBytes + sizeInBytes} bytes.");
        }
        if (sizeInBytes == 0)
        {
            return;
        }
        buffer.EnsureWritable();
        UpdateBufferCore(buffer, bufferOffsetInBytes, source, sizeInBytes);
        RecordBufferOp(BufferOpBin.Update, sizeInBytes);
    }

    private protected abstract void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes);

    /// <summary>
    /// Whether this format/type/usage combo is supported.
    /// </summary>
    /// <param name="format">Pixel format to check.</param>
    /// <param name="type">Texture type to check.</param>
    /// <param name="usage">Texture usage to check.</param>
    /// <returns>True if supported.</returns>
    public bool GetPixelFormatSupport(
        PixelFormat format,
        TextureType type,
        TextureUsage usage)
    {
        return GetPixelFormatSupportCore(format, type, usage, out _);
    }

    /// <summary>
    /// Whether this format/type/usage combo is supported, and its device-specific limits.
    /// </summary>
    /// <param name="format">Pixel format to check.</param>
    /// <param name="type">Texture type to check.</param>
    /// <param name="usage">Texture usage to check.</param>
    /// <param name="properties">If supported, the limits for a texture made with this combo.</param>
    /// <returns>True if supported, with properties filled in.</returns>
    public bool GetPixelFormatSupport(
        PixelFormat format,
        TextureType type,
        TextureUsage usage,
        out PixelFormatProperties properties)
    {
        return GetPixelFormatSupportCore(format, type, usage, out properties);
    }

    private protected abstract bool GetPixelFormatSupportCore(
        PixelFormat format,
        TextureType type,
        TextureUsage usage,
        out PixelFormatProperties properties);

    /// <summary>
    /// Queues the object for disposal once this device goes idle. Use for resources that might still be in use now but won't be by the time the device is idle.
    /// </summary>
    /// <param name="disposable">Object to dispose once idle.</param>
    public void DisposeWhenIdle(IDisposable disposable)
    {
        lock (_deferredDisposalLock)
        {
            _disposables.Add(disposable);
        }
    }

    private void FlushDeferredDisposals()
    {
        lock (_deferredDisposalLock)
        {
            foreach (IDisposable disposable in _disposables)
            {
                disposable.Dispose();
            }
            _disposables.Clear();
        }
    }

    /// <summary>
    /// Backend-specific disposal of this device's resources.
    /// </summary>
    protected abstract void PlatformDispose();

    /// <summary>
    /// Creates and caches common device resources right after device creation.
    /// </summary>
    protected void PostDeviceCreated()
    {
        PointSampler = ResourceFactory.CreateSampler(SamplerDescription.Point);
        LinearSampler = ResourceFactory.CreateSampler(SamplerDescription.Linear);
        NullUniform = ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        NullTexture2D = ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        NullTextureRW2D = ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Storage));

        if (Features.SamplerAnisotropy)
        {
            _aniso4xSampler = ResourceFactory.CreateSampler(SamplerDescription.Aniso4x);
        }

        if (Features.StructuredBuffer)
        {
            _nullStructuredRead = ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly, 16));
            _nullStructuredReadWrite = ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite, 16));
        }
    }

    /// <summary>
    /// Point-filtered sampler owned by this device.
    /// </summary>
    public Sampler PointSampler { get; private set; }

    /// <summary>
    /// Linear-filtered sampler owned by this device.
    /// </summary>
    public Sampler LinearSampler { get; private set; }

    /// <summary>
    /// 1x1 black transparent texture used as a fallback when a read-only texture slot has no match in the merged property table.
    /// </summary>
    public Texture NullTexture2D { get; private set; }

    /// <summary>
    /// 1x1 black transparent read-write texture used as a fallback when a read-write texture slot has no match in the merged property table.
    /// </summary>
    public Texture NullTextureRW2D { get; private set; }

    /// <summary>
    /// 16-byte buffer used as a fallback when a uniform buffer slot has no match in the merged property table.
    /// </summary>
    public DeviceBuffer NullUniform { get; private set; }

    /// <summary>
    /// 16-byte buffer used as a fallback when a structured read-only buffer slot has no match in the merged property table.
    /// </summary>
    public DeviceBuffer NullStructured
    {
        get
        {
            if (!Features.StructuredBuffer)
            {
                throw new RenderException(
                    "GraphicsDevice.NullStructured cannot be used unless GraphicsDeviceFeatures.StructuredBuffer is supported.");
            }

            Debug.Assert(_nullStructuredRead != null);
            return _nullStructuredRead;
        }
    }

    /// <summary>
    /// 16-byte buffer used as a fallback when a structured read-write buffer slot has no match in the merged property table.
    /// </summary>
    public DeviceBuffer NullStructuredRW
    {
        get
        {
            if (!Features.StructuredBuffer)
            {
                throw new RenderException(
                    "GraphicsDevice.NullStructuredRW cannot be used unless GraphicsDeviceFeatures.StructuredBuffer is supported.");
            }

            Debug.Assert(_nullStructuredReadWrite != null);
            return _nullStructuredReadWrite;
        }
    }


    /// <summary>
    /// Called at draw/dispatch time when a reflected resource slot has no match in the merged property table and gets a default value instead. Null (silent) by default.
    /// </summary>
    public MissingPropertyHandler? OnMissingProperty { get; set; }

    /// <summary>
    /// Called on non-fatal warnings, like an implicit buffer reallocation or hitting the transient soft cap. Writes to Console.Error by default; set null to silence, or replace to reroute.
    /// </summary>
    public GraphicsDeviceWarningHandler? OnWarning { get; set; } = message => Console.Error.WriteLine(message);

    /// <summary>
    /// 4x anisotropic-filtered sampler owned by this device. Only usable if SamplerAnisotropy is supported.
    /// </summary>
    public Sampler Aniso4xSampler
    {
        get
        {
            if (!Features.SamplerAnisotropy)
            {
                throw new RenderException(
                    "GraphicsDevice.Aniso4xSampler cannot be used unless GraphicsDeviceFeatures.SamplerAnisotropy is supported.");
            }

            Debug.Assert(_aniso4xSampler != null);
            return _aniso4xSampler;
        }
    }

    /// <summary>
    /// True if this device has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Frees this device's unmanaged resources. All child resources must already be disposed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        WaitForIdle();
        PointSampler.Dispose();
        LinearSampler.Dispose();
        NullTexture2D.Dispose();
        NullTextureRW2D.Dispose();
        NullUniform.Dispose();
        _aniso4xSampler?.Dispose();
        _nullStructuredRead?.Dispose();
        _nullStructuredReadWrite?.Dispose();
        PlatformDispose();
    }

#if !EXCLUDE_VULKAN_BACKEND
    /// <summary>
    /// Tries to get Vulkan backend info for this device. Only succeeds on a Vulkan device.
    /// </summary>
    /// <param name="info">Vulkan backend info if successful.</param>
    /// <returns>True if this is a Vulkan device and it worked.</returns>
    public virtual bool GetVulkanInfo([NotNullWhen(true)] out BackendInfoVulkan? info)
    {
        info = null;
        return false;
    }

    /// <summary>
    /// Gets Vulkan backend info for this device. Only works on a Vulkan device, throws otherwise.
    /// </summary>
    /// <returns>Vulkan backend info for this device.</returns>
    public BackendInfoVulkan GetVulkanInfo()
    {
        if (!GetVulkanInfo(out BackendInfoVulkan? info))
            throw new RenderException($"{nameof(GetVulkanInfo)} can only be used on a Vulkan GraphicsDevice.");

        return info;
    }
#endif


    /// <summary>
    /// Whether the given backend is supported on this system.
    /// </summary>
    /// <param name="backend">Backend to check.</param>
    /// <returns>True if supported.</returns>
    public static bool IsBackendSupported(GraphicsBackend backend)
    {
        switch (backend)
        {
            case GraphicsBackend.Vulkan:
#if !EXCLUDE_VULKAN_BACKEND
                return Vk.VkGraphicsDevice.IsSupported();
#else
                return false;
#endif
            default:
                throw Illegal.Value<GraphicsBackend>();
        }
    }

#if !EXCLUDE_VULKAN_BACKEND
    /// <summary>
    /// Creates a new Vulkan graphics device.
    /// </summary>
    /// <param name="options">Common device properties.</param>
    /// <returns>A new Vulkan graphics device.</returns>
    public static GraphicsDevice CreateVulkan(GraphicsDeviceOptions options)
    {
        return new Vk.VkGraphicsDevice(options, null);
    }

    /// <summary>
    /// Creates a new Vulkan graphics device.
    /// </summary>
    /// <param name="options">Common device properties.</param>
    /// <param name="vkOptions">Vulkan-specific creation options.</param>
    /// <returns>A new Vulkan graphics device.</returns>
    public static GraphicsDevice CreateVulkan(GraphicsDeviceOptions options, VulkanDeviceOptions vkOptions)
    {
        return new Vk.VkGraphicsDevice(options, null, vkOptions);
    }

    /// <summary>
    /// Creates a new Vulkan graphics device with a main swapchain.
    /// </summary>
    /// <param name="options">Common device properties.</param>
    /// <param name="swapchainDescription">Description of the main swapchain to create.</param>
    /// <returns>A new Vulkan graphics device.</returns>
    public static GraphicsDevice CreateVulkan(GraphicsDeviceOptions options, SwapchainDescription swapchainDescription)
    {
        return new Vk.VkGraphicsDevice(options, swapchainDescription);
    }

    /// <summary>
    /// Creates a new Vulkan graphics device with a main swapchain.
    /// </summary>
    /// <param name="options">Common device properties.</param>
    /// <param name="vkOptions">Vulkan-specific creation options.</param>
    /// <param name="swapchainDescription">Description of the main swapchain to create.</param>
    /// <returns>A new Vulkan graphics device.</returns>
    public static GraphicsDevice CreateVulkan(
        GraphicsDeviceOptions options,
        SwapchainDescription swapchainDescription,
        VulkanDeviceOptions vkOptions)
    {
        return new Vk.VkGraphicsDevice(options, swapchainDescription, vkOptions);
    }
#endif

}
