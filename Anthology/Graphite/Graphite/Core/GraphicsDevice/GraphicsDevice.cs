using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Prowl.Graphite;

/// <summary>
/// Represents an abstract graphics device, capable of creating device resources and executing commands.
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

    /// <summary>The maximum number of frames that can be in flight simultaneously.</summary>
    protected internal uint _maxFramesInFlight;

    /// <summary>The initial size of each slot's transient bump-allocator buffer, in bytes.</summary>
    protected internal uint _transientInitialSize;

    /// <summary>The soft cap for per-frame transient usage, in bytes.</summary>
    protected internal uint _transientSoftCapBytes;

    /// <summary>The hard cap for per-frame transient usage, in bytes.</summary>
    protected internal uint _transientHardCapBytes;

    /// <summary>Monotonically increasing frame counter; 0 means no frame has ever started.</summary>
    protected ulong _frameIdCounter;

    /// <summary>The FrameId of the most recently completed frame, updated opportunistically.</summary>
    protected ulong _lastCompletedFrameId;

    /// <summary>Set to true after the soft cap warning has been emitted once.</summary>
    protected internal bool _transientSoftCapWarned;

    internal GraphicsDevice() { }

    /// <summary>
    /// Gets the name of the device.
    /// </summary>
    public abstract string DeviceName { get; }

    /// <summary>
    /// Gets the name of the device vendor.
    /// </summary>
    public abstract string VendorName { get; }

    /// <summary>
    /// Gets the API version of the graphics backend.
    /// </summary>
    public abstract GraphicsApiVersion ApiVersion { get; }

    /// <summary>
    /// Gets a value identifying the specific graphics API used by this instance.
    /// </summary>
    public abstract GraphicsBackend BackendType { get; }

    /// <summary>
    /// Gets a value identifying whether texture coordinates begin in the top left corner of a Texture.
    /// If true, (0, 0) refers to the top-left texel of a Texture. If false, (0, 0) refers to the bottom-left
    /// texel of a Texture. This property is useful for determining how the output of a Framebuffer should be sampled.
    /// </summary>
    public abstract bool IsUvOriginTopLeft { get; }

    /// <summary>
    /// Gets a value indicating whether this device's depth values range from 0 to 1.
    /// If false, depth values instead range from -1 to 1.
    /// </summary>
    public abstract bool IsDepthRangeZeroToOne { get; }

    /// <summary>
    /// Gets a value indicating whether this device's clip space Y values increase from top (-1) to bottom (1).
    /// If false, clip space Y values instead increase from bottom (-1) to top (1).
    /// </summary>
    public abstract bool IsClipSpaceYInverted { get; }

    /// <summary>
    /// Gets the <see cref="ResourceFactory"/> controlled by this instance.
    /// </summary>
    public abstract ResourceFactory ResourceFactory { get; }

    /// <summary>
    /// Retrieves the main Swapchain for this device. This property is only valid if the device was created with a main
    /// Swapchain, and will return null otherwise.
    /// </summary>
    public abstract Swapchain MainSwapchain { get; }

    /// <summary>
    /// Gets a <see cref="GraphicsDeviceFeatures"/> which enumerates the optional features supported by this instance.
    /// </summary>
    public abstract GraphicsDeviceFeatures Features { get; }

    /// <summary>
    /// Gets or sets whether the main Swapchain's <see cref="SwapBuffers()"/> should be synchronized to the window system's
    /// vertical refresh rate.
    /// This is equivalent to <see cref="MainSwapchain"/>.<see cref="Swapchain.SyncToVerticalBlank"/>.
    /// This property cannot be set if this GraphicsDevice was created without a main Swapchain.
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
    /// The required alignment, in bytes, for uniform buffer offsets. <see cref="DeviceBufferRange.Offset"/> must be a
    /// multiple of this value. When binding a <see cref="PropertySet"/> to a <see cref="CommandBuffer"/> with an overload
    /// accepting dynamic offsets, each offset must be a multiple of this value.
    /// </summary>
    public uint UniformBufferMinOffsetAlignment => GetUniformBufferMinOffsetAlignmentCore();

    /// <summary>
    /// The required alignment, in bytes, for structured buffer offsets. <see cref="DeviceBufferRange.Offset"/> must be a
    /// multiple of this value. When binding a <see cref="PropertySet"/> to a <see cref="CommandBuffer"/> with an overload
    /// accepting dynamic offsets, each offset must be a multiple of this value.
    /// </summary>
    public uint StructuredBufferMinOffsetAlignment => GetStructuredBufferMinOffsetAlignmentCore();

    internal abstract uint GetUniformBufferMinOffsetAlignmentCore();
    internal abstract uint GetStructuredBufferMinOffsetAlignmentCore();

    private Frame _currentFrame;

    /// <summary>
    /// Gets the currently active <see cref="Frame"/>.
    /// <para>
    /// This is the single guarded entry point for frame access from recording code: when usage
    /// validation is enabled, reading it while no frame is open throws a
    /// <see cref="RenderException"/>, so backends never need to null-check before allocating
    /// transient memory or per-frame resources. Without validation it returns
    /// <see langword="null"/> if no frame is in progress.
    /// </para>
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
    /// Begins a new frame and returns the active <see cref="Frame"/> object.
    /// If the oldest in-flight ring slot has not yet completed, this method blocks until it does.
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
    /// Blocks the calling thread until the given <see cref="Fence"/> becomes signaled, or until a time greater than the
    /// given TimeSpan has elapsed.
    /// </summary>
    /// <param name="fence">The <see cref="Fence"/> instance to wait on.</param>
    /// <param name="timeout">A TimeSpan indicating the maximum time to wait on the Fence.</param>
    /// <returns>True if the Fence was signaled. False if the timeout was reached instead.</returns>
    public bool WaitForFence(Fence fence, TimeSpan timeout)
        => WaitForFence(fence, (ulong)timeout.TotalMilliseconds * 1_000_000);
    /// <summary>
    /// Blocks the calling thread until the given <see cref="Fence"/> becomes signaled, or until a time greater than the
    /// given TimeSpan has elapsed.
    /// </summary>
    /// <param name="fence">The <see cref="Fence"/> instance to wait on.</param>
    /// <param name="nanosecondTimeout">A value in nanoseconds, indicating the maximum time to wait on the Fence.</param>
    /// <returns>True if the Fence was signaled. False if the timeout was reached instead.</returns>
    public abstract bool WaitForFence(Fence fence, ulong nanosecondTimeout);

    /// <summary>
    /// Resets the given <see cref="Fence"/> to the unsignaled state.
    /// </summary>
    /// <param name="fence">The <see cref="Fence"/> instance to reset.</param>
    public abstract void ResetFence(Fence fence);

    /// <summary>
    /// Swaps the buffers of the main swapchain and presents the rendered image to the screen.
    /// This is equivalent to passing <see cref="MainSwapchain"/> to <see cref="SwapBuffers(Swapchain)"/>.
    /// This method can only be called if this GraphicsDevice was created with a main Swapchain.
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
    /// <param name="swapchain">The <see cref="Swapchain"/> to swap and present.</param>
    public void SwapBuffers(Swapchain swapchain)
    {
        SwapBuffersCore(swapchain);
        RecordSwap(SwapBin.Present, 0);
    }

    private protected abstract void SwapBuffersCore(Swapchain swapchain);

    /// <summary>
    /// Gets a <see cref="Framebuffer"/> object representing the render targets of the main swapchain.
    /// This is equivalent to <see cref="MainSwapchain"/>.<see cref="Swapchain.Framebuffer"/>.
    /// If this GraphicsDevice was created without a main Swapchain, then this returns null.
    /// </summary>
    public Framebuffer? SwapchainFramebuffer => MainSwapchain?.Framebuffer;

    /// <summary>
    /// Notifies this instance that the main window has been resized. This causes the <see cref="SwapchainFramebuffer"/> to
    /// be appropriately resized and recreated.
    /// This is equivalent to calling <see cref="MainSwapchain"/>.<see cref="Swapchain.Resize(uint, uint)"/>.
    /// This method can only be called if this GraphicsDevice was created with a main Swapchain.
    /// </summary>
    /// <param name="width">The new width of the main window.</param>
    /// <param name="height">The new height of the main window.</param>
    public void ResizeMainWindow(uint width, uint height)
    {
        if (MainSwapchain == null)
        {
            throw new RenderException("This GraphicsDevice was created without a main Swapchain, so the requested operation cannot be performed.");
        }

        MainSwapchain.Resize(width, height);
    }

    /// <summary>
    /// A blocking method that returns when all submitted <see cref="CommandBuffer"/> objects have fully completed
    /// and all in-flight frames have been signaled as complete.
    /// </summary>
    /// <exception cref="RenderException">Thrown if <see cref="CurrentFrame"/> is not null. Call <see cref="EndFrame()"/> before <see cref="WaitForIdle"/>.</exception>
    public void WaitForIdle()
    {
        if (_currentFrame != null)
            throw new RenderException("WaitForIdle cannot be called while a frame is active. Call EndFrame first.");
        WaitForIdleCore();
        Volatile.Write(ref _lastCompletedFrameId, _frameIdCounter);
        FlushDeferredDisposals();
    }

    private protected abstract void WaitForIdleCore();

    /// <summary>
    /// Gets the maximum sample count supported by the given <see cref="PixelFormat"/>.
    /// </summary>
    /// <param name="format">The format to query.</param>
    /// <param name="depthFormat">Whether the format will be used in a depth texture.</param>
    /// <returns>A <see cref="TextureSampleCount"/> value representing the maximum count that a <see cref="Texture"/> of that
    /// format can be created with.</returns>
    public abstract TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat);

    /// <summary>
    /// Maps a <see cref="DeviceBuffer"/> or <see cref="Texture"/> into a CPU-accessible data region. For Texture resources, this
    /// overload maps the first subresource.
    /// </summary>
    /// <param name="resource">The <see cref="DeviceBuffer"/> or <see cref="Texture"/> resource to map.</param>
    /// <param name="mode">The <see cref="MapMode"/> to use.</param>
    /// <returns>A <see cref="MappedResource"/> structure describing the mapped data region.</returns>
    public MappedResource Map(MappableResource resource, MapMode mode) => Map(resource, mode, 0);
    /// <summary>
    /// Maps a <see cref="DeviceBuffer"/> or <see cref="Texture"/> into a CPU-accessible data region.
    /// </summary>
    /// <param name="resource">The <see cref="DeviceBuffer"/> or <see cref="Texture"/> resource to map.</param>
    /// <param name="mode">The <see cref="MapMode"/> to use.</param>
    /// <param name="subresource">The subresource to map. Subresources are indexed first by mip slice, then by array layer.
    /// For <see cref="DeviceBuffer"/> resources, this parameter must be 0.</param>
    /// <returns>A <see cref="MappedResource"/> structure describing the mapped data region.</returns>
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
    /// </summary>
    /// <param name="resource"></param>
    /// <param name="mode"></param>
    /// <param name="subresource"></param>
    /// <returns></returns>
    protected abstract MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource);

    /// <summary>
    /// Maps a <see cref="DeviceBuffer"/> or <see cref="Texture"/> into a CPU-accessible data region, and returns a structured
    /// view over that region. For Texture resources, this overload maps the first subresource.
    /// </summary>
    /// <param name="resource">The <see cref="DeviceBuffer"/> or <see cref="Texture"/> resource to map.</param>
    /// <param name="mode">The <see cref="MapMode"/> to use.</param>
    /// <typeparam name="T">The blittable value type which mapped data is viewed as.</typeparam>
    /// <returns>A <see cref="MappedResource"/> structure describing the mapped data region.</returns>
    public MappedResourceView<T> Map<T>(MappableResource resource, MapMode mode) where T : unmanaged
        => Map<T>(resource, mode, 0);
    /// <summary>
    /// Maps a <see cref="DeviceBuffer"/> or <see cref="Texture"/> into a CPU-accessible data region, and returns a structured
    /// view over that region.
    /// </summary>
    /// <param name="resource">The <see cref="DeviceBuffer"/> or <see cref="Texture"/> resource to map.</param>
    /// <param name="mode">The <see cref="MapMode"/> to use.</param>
    /// <param name="subresource">The subresource to map. Subresources are indexed first by mip slice, then by array layer.</param>
    /// <typeparam name="T">The blittable value type which mapped data is viewed as.</typeparam>
    /// <returns>A <see cref="MappedResource"/> structure describing the mapped data region.</returns>
    public MappedResourceView<T> Map<T>(MappableResource resource, MapMode mode, uint subresource) where T : unmanaged
    {
        MappedResource mappedResource = Map(resource, mode, subresource);
        return new MappedResourceView<T>(mappedResource);
    }

    /// <summary>
    /// Invalidates a previously-mapped data region for the given <see cref="DeviceBuffer"/> or <see cref="Texture"/>.
    /// For <see cref="Texture"/> resources, this unmaps the first subresource.
    /// </summary>
    /// <param name="resource">The resource to unmap.</param>
    public void Unmap(MappableResource resource) => Unmap(resource, 0);
    /// <summary>
    /// Invalidates a previously-mapped data region for the given <see cref="DeviceBuffer"/> or <see cref="Texture"/>.
    /// </summary>
    /// <param name="resource">The resource to unmap.</param>
    /// <param name="subresource">The subresource to unmap. Subresources are indexed first by mip slice, then by array layer.
    /// For <see cref="DeviceBuffer"/> resources, this parameter must be 0.</param>
    public void Unmap(MappableResource resource, uint subresource)
    {
        UnmapCore(resource, subresource);
        RecordBufferOp(BufferOpBin.Unmap, 0);
    }

    /// <summary>
    /// </summary>
    /// <param name="resource"></param>
    /// <param name="subresource"></param>
    protected abstract void UnmapCore(MappableResource resource, uint subresource);

    /// <summary>
    /// Updates a portion of a <see cref="Texture"/> resource with new data.
    /// </summary>
    /// <param name="texture">The resource to update.</param>
    /// <param name="source">A pointer to the start of the data to upload. This must point to tightly-packed pixel data for
    /// the region specified.</param>
    /// <param name="sizeInBytes">The number of bytes to upload. This value must match the total size of the texture region
    /// specified.</param>
    /// <param name="x">The minimum X value of the updated region.</param>
    /// <param name="y">The minimum Y value of the updated region.</param>
    /// <param name="z">The minimum Z value of the updated region.</param>
    /// <param name="width">The width of the updated region, in texels.</param>
    /// <param name="height">The height of the updated region, in texels.</param>
    /// <param name="depth">The depth of the updated region, in texels.</param>
    /// <param name="mipLevel">The mipmap level to update. Must be less than the total number of mipmaps contained in the
    /// <see cref="Texture"/>.</param>
    /// <param name="arrayLayer">The array layer to update. Must be less than the total array layer count contained in the
    /// <see cref="Texture"/>.</param>
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
    /// Updates a portion of a <see cref="Texture"/> resource with new data contained in an array
    /// </summary>
    /// <param name="texture">The resource to update.</param>
    /// <param name="source">An array containing the data to upload. This must contain tightly-packed pixel data for the
    /// region specified.</param>
    /// <param name="x">The minimum X value of the updated region.</param>
    /// <param name="y">The minimum Y value of the updated region.</param>
    /// <param name="z">The minimum Z value of the updated region.</param>
    /// <param name="width">The width of the updated region, in texels.</param>
    /// <param name="height">The height of the updated region, in texels.</param>
    /// <param name="depth">The depth of the updated region, in texels.</param>
    /// <param name="mipLevel">The mipmap level to update. Must be less than the total number of mipmaps contained in the
    /// <see cref="Texture"/>.</param>
    /// <param name="arrayLayer">The array layer to update. Must be less than the total array layer count contained in the
    /// <see cref="Texture"/>.</param>
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
    /// Updates a portion of a <see cref="Texture"/> resource with new data contained in an array
    /// </summary>
    /// <param name="texture">The resource to update.</param>
    /// <param name="source">A readonly span containing the data to upload. This must contain tightly-packed pixel data for the
    /// region specified.</param>
    /// <param name="x">The minimum X value of the updated region.</param>
    /// <param name="y">The minimum Y value of the updated region.</param>
    /// <param name="z">The minimum Z value of the updated region.</param>
    /// <param name="width">The width of the updated region, in texels.</param>
    /// <param name="height">The height of the updated region, in texels.</param>
    /// <param name="depth">The depth of the updated region, in texels.</param>
    /// <param name="mipLevel">The mipmap level to update. Must be less than the total number of mipmaps contained in the
    /// <see cref="Texture"/>.</param>
    /// <param name="arrayLayer">The array layer to update. Must be less than the total array layer count contained in the
    /// <see cref="Texture"/>.</param>
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
    /// Updates a portion of a <see cref="Texture"/> resource with new data contained in an array
    /// </summary>
    /// <param name="texture">The resource to update.</param>
    /// <param name="source">A readonly span containing the data to upload. This must contain tightly-packed pixel data for the
    /// region specified.</param>
    /// <param name="x">The minimum X value of the updated region.</param>
    /// <param name="y">The minimum Y value of the updated region.</param>
    /// <param name="z">The minimum Z value of the updated region.</param>
    /// <param name="width">The width of the updated region, in texels.</param>
    /// <param name="height">The height of the updated region, in texels.</param>
    /// <param name="depth">The depth of the updated region, in texels.</param>
    /// <param name="mipLevel">The mipmap level to update. Must be less than the total number of mipmaps contained in the
    /// <see cref="Texture"/>.</param>
    /// <param name="arrayLayer">The array layer to update. Must be less than the total array layer count contained in the
    /// <see cref="Texture"/>.</param>
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
    /// Updates a <see cref="DeviceBuffer"/> region with new data.
    /// This function must be used with a blittable value type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of data to upload.</typeparam>
    /// <param name="buffer">The resource to update.</param>
    /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/> storage, at
    /// which new data will be uploaded.</param>
    /// <param name="source">The value to upload.</param>
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
    /// Updates a <see cref="DeviceBuffer"/> region with new data.
    /// This function must be used with a blittable value type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of data to upload.</typeparam>
    /// <param name="buffer">The resource to update.</param>
    /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
    /// which new data will be uploaded.</param>
    /// <param name="source">A reference to the single value to upload.</param>
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
    /// Updates a <see cref="DeviceBuffer"/> region with new data.
    /// This function must be used with a blittable value type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of data to upload.</typeparam>
    /// <param name="buffer">The resource to update.</param>
    /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
    /// which new data will be uploaded.</param>
    /// <param name="source">A reference to the first of a series of values to upload.</param>
    /// <param name="sizeInBytes">The total size of the uploaded data, in bytes.</param>
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
    /// Updates a <see cref="DeviceBuffer"/> region with new data.
    /// This function must be used with a blittable value type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of data to upload.</typeparam>
    /// <param name="buffer">The resource to update.</param>
    /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
    /// which new data will be uploaded.</param>
    /// <param name="source">An array containing the data to upload.</param>
    public void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        T[] source) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
    }

    /// <summary>
    /// Updates a <see cref="DeviceBuffer"/> region with new data.
    /// This function must be used with a blittable value type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of data to upload.</typeparam>
    /// <param name="buffer">The resource to update.</param>
    /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
    /// which new data will be uploaded.</param>
    /// <param name="source">A readonly span containing the data to upload.</param>
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
    /// Updates a <see cref="DeviceBuffer"/> region with new data.
    /// This function must be used with a blittable value type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of data to upload.</typeparam>
    /// <param name="buffer">The resource to update.</param>
    /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
    /// which new data will be uploaded.</param>
    /// <param name="source">A span containing the data to upload.</param>
    public void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        Span<T> source) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
    }

    /// <summary>
    /// Updates a <see cref="DeviceBuffer"/> region with new data.
    /// </summary>
    /// <param name="buffer">The resource to update.</param>
    /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
    /// which new data will be uploaded.</param>
    /// <param name="source">A pointer to the start of the data to upload.</param>
    /// <param name="sizeInBytes">The total size of the uploaded data, in bytes.</param>
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
    /// Gets whether or not the given <see cref="PixelFormat"/>, <see cref="TextureType"/>, and <see cref="TextureUsage"/>
    /// combination is supported by this instance.
    /// </summary>
    /// <param name="format">The PixelFormat to query.</param>
    /// <param name="type">The TextureType to query.</param>
    /// <param name="usage">The TextureUsage to query.</param>
    /// <returns>True if the given combination is supported; false otherwise.</returns>
    public bool GetPixelFormatSupport(
        PixelFormat format,
        TextureType type,
        TextureUsage usage)
    {
        return GetPixelFormatSupportCore(format, type, usage, out _);
    }

    /// <summary>
    /// Gets whether or not the given <see cref="PixelFormat"/>, <see cref="TextureType"/>, and <see cref="TextureUsage"/>
    /// combination is supported by this instance, and also gets the device-specific properties supported by this instance.
    /// </summary>
    /// <param name="format">The PixelFormat to query.</param>
    /// <param name="type">The TextureType to query.</param>
    /// <param name="usage">The TextureUsage to query.</param>
    /// <param name="properties">If the combination is supported, then this parameter describes the limits of a Texture
    /// created using the given combination of attributes.</param>
    /// <returns>True if the given combination is supported; false otherwise. If the combination is supported,
    /// then <paramref name="properties"/> contains the limits supported by this instance.</returns>
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
    /// Adds the given object to a deferred disposal list, which will be processed when this GraphicsDevice becomes idle.
    /// This method can be used to safely dispose a device resource which may be in use at the time this method is called,
    /// but which will no longer be in use when the device is idle.
    /// </summary>
    /// <param name="disposable">An object to dispose when this instance becomes idle.</param>
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
    /// Performs API-specific disposal of resources controlled by this instance.
    /// </summary>
    protected abstract void PlatformDispose();

    /// <summary>
    /// Creates and caches common device resources after device creation completes.
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
    /// Gets a simple point-filtered <see cref="Sampler"/> object owned by this instance.
    /// This object is created with <see cref="SamplerDescription.Point"/>.
    /// </summary>
    public Sampler PointSampler { get; private set; }

    /// <summary>
    /// Gets a simple linear-filtered <see cref="Sampler"/> object owned by this instance.
    /// This object is created with <see cref="SamplerDescription.Linear"/>.
    /// </summary>
    public Sampler LinearSampler { get; private set; }

    /// <summary>
    /// Gets a 1x1 black transparent <see cref="Texture"/> used as the fallback when a
    /// <see cref="ResourceKind.TextureReadOnly"/> slot has no matching entry in the merged property table.
    /// </summary>
    public Texture NullTexture2D { get; private set; }

    /// <summary>
    /// Gets a 1x1 black transparent read-write <see cref="Texture"/> used as the fallback when a
    /// <see cref="ResourceKind.TextureReadWrite"/> slot has no matching entry in the merged property table.
    /// </summary>
    public Texture NullTextureRW2D { get; private set; }

    /// <summary>
    /// Gets a 16-byte length <see cref="DeviceBuffer"/> used as the fallback when a
    /// <see cref="ResourceKind.UniformBuffer"/> slot has no matching entry in the merged property table.
    /// </summary>
    public DeviceBuffer NullUniform { get; private set; }

    /// <summary>
    /// Gets a 16-byte length <see cref="DeviceBuffer"/> used as the fallback when a 
    /// <see cref="ResourceKind.StructuredBufferReadOnly"/> slot has no matching entry in the merged property table.
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
    /// Gets a 16-byte length <see cref="DeviceBuffer"/> used as the fallback when a 
    /// <see cref="ResourceKind.StructuredBufferReadWrite"/> slot has no matching entry in the merged property table.
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
    /// Optional callback invoked at draw/dispatch time when a reflected resource slot has no matching entry
    /// in the merged property table and a default value is substituted. Null by default (silent).
    /// </summary>
    public MissingPropertyHandler? OnMissingProperty { get; set; }

    /// <summary>
    /// Callback invoked when this instance wants to surface a non-fatal warning, such as an implicit
    /// <see cref="DeviceBuffer"/> reallocation or a transient buffer soft cap being exceeded. Writes to
    /// <see cref="Console.Error"/> by default; assign <see langword="null"/> to silence warnings, or replace it to
    /// route them elsewhere.
    /// </summary>
    public GraphicsDeviceWarningHandler? OnWarning { get; set; } = message => Console.Error.WriteLine(message);

    /// <summary>
    /// Gets a simple 4x anisotropic-filtered <see cref="Sampler"/> object owned by this instance.
    /// This object is created with <see cref="SamplerDescription.Aniso4x"/>.
    /// This property can only be used when <see cref="GraphicsDeviceFeatures.SamplerAnisotropy"/> is supported.
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
    /// A bool indicating whether this instance has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Frees unmanaged resources controlled by this device.
    /// All created child resources must be Disposed prior to calling this method.
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

#if !EXCLUDE_D3D11_BACKEND
    /// <summary>
    /// Tries to get a <see cref="BackendInfoD3D11"/> for this instance. This method will only succeed if this is a D3D11
    /// GraphicsDevice.
    /// </summary>
    /// <param name="info">If successful, this will contain the <see cref="BackendInfoD3D11"/> for this instance.</param>
    /// <returns>True if this is a D3D11 GraphicsDevice and the operation was successful. False otherwise.</returns>
    public virtual bool GetD3D11Info([NotNullWhen(true)] out BackendInfoD3D11? info)
    {
        info = null;
        return false;
    }

    /// <summary>
    /// Gets a <see cref="BackendInfoD3D11"/> for this instance. This method will only succeed if this is a D3D11
    /// GraphicsDevice. Otherwise, this method will throw an exception.
    /// </summary>
    /// <returns>The <see cref="BackendInfoD3D11"/> for this instance.</returns>
    public BackendInfoD3D11 GetD3D11Info()
    {
        if (!GetD3D11Info(out BackendInfoD3D11? info))
            throw new RenderException($"{nameof(GetD3D11Info)} can only be used on a D3D11 GraphicsDevice.");

        return info;
    }
#endif

#if !EXCLUDE_VULKAN_BACKEND
    /// <summary>
    /// Tries to get a <see cref="BackendInfoVulkan"/> for this instance. This method will only succeed if this is a Vulkan
    /// GraphicsDevice.
    /// </summary>
    /// <param name="info">If successful, this will contain the <see cref="BackendInfoVulkan"/> for this instance.</param>
    /// <returns>True if this is a Vulkan GraphicsDevice and the operation was successful. False otherwise.</returns>
    public virtual bool GetVulkanInfo([NotNullWhen(true)] out BackendInfoVulkan? info)
    {
        info = null;
        return false;
    }

    /// <summary>
    /// Gets a <see cref="BackendInfoVulkan"/> for this instance. This method will only succeed if this is a Vulkan
    /// GraphicsDevice. Otherwise, this method will throw an exception.
    /// </summary>
    /// <returns>The <see cref="BackendInfoVulkan"/> for this instance.</returns>
    public BackendInfoVulkan GetVulkanInfo()
    {
        if (!GetVulkanInfo(out BackendInfoVulkan? info))
            throw new RenderException($"{nameof(GetVulkanInfo)} can only be used on a Vulkan GraphicsDevice.");

        return info;
    }
#endif

#if !EXCLUDE_OPENGL_BACKEND
    /// <summary>
    /// Tries to get a <see cref="BackendInfoOpenGL"/> for this instance. This method will only succeed if this is an OpenGL
    /// GraphicsDevice.
    /// </summary>
    /// <param name="info">If successful, this will contain the <see cref="BackendInfoOpenGL"/> for this instance.</param>
    /// <returns>True if this is an OpenGL GraphicsDevice and the operation was successful. False otherwise.</returns>
    public virtual bool GetOpenGLInfo([NotNullWhen(true)] out BackendInfoOpenGL? info)
    {
        info = null;
        return false;
    }

    /// <summary>
    /// Gets a <see cref="BackendInfoOpenGL"/> for this instance. This method will only succeed if this is an OpenGL
    /// GraphicsDevice. Otherwise, this method will throw an exception.
    /// </summary>
    /// <returns>The <see cref="BackendInfoOpenGL"/> for this instance.</returns>
    public BackendInfoOpenGL GetOpenGLInfo()
    {
        if (!GetOpenGLInfo(out BackendInfoOpenGL? info))
        {
            throw new RenderException($"{nameof(GetOpenGLInfo)} can only be used on an OpenGL GraphicsDevice.");
        }

        return info;
    }
#endif


    /// <summary>
    /// Checks whether the given <see cref="GraphicsBackend"/> is supported on this system.
    /// </summary>
    /// <param name="backend">The GraphicsBackend to check.</param>
    /// <returns>True if the GraphicsBackend is supported; false otherwise.</returns>
    public static bool IsBackendSupported(GraphicsBackend backend)
    {
        switch (backend)
        {
            case GraphicsBackend.Direct3D11:
#if !EXCLUDE_D3D11_BACKEND
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
                return false;
#endif
            case GraphicsBackend.Vulkan:
#if !EXCLUDE_VULKAN_BACKEND
                return Vk.VkGraphicsDevice.IsSupported();
#else
                return false;
#endif
            case GraphicsBackend.OpenGL:
#if !EXCLUDE_OPENGL_BACKEND
                return true;
#else
                return false;
#endif
            case GraphicsBackend.OpenGLES:
#if !EXCLUDE_OPENGL_BACKEND
                return !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#else
                return false;
#endif
            default:
                throw Illegal.Value<GraphicsBackend>();
        }
    }

#if !EXCLUDE_D3D11_BACKEND
    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> using Direct3D 11.
    /// </summary>
    /// <param name="options">Describes several common properties of the GraphicsDevice.</param>
    /// <returns>A new <see cref="GraphicsDevice"/> using the Direct3D 11 API.</returns>
    public static GraphicsDevice CreateD3D11(GraphicsDeviceOptions options)
    {
        return new D3D11.D3D11GraphicsDevice(options, new D3D11DeviceOptions(), null);
    }

    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> using Direct3D 11, with a main Swapchain.
    /// </summary>
    /// <param name="options">Describes several common properties of the GraphicsDevice.</param>
    /// <param name="swapchainDescription">A description of the main Swapchain to create.</param>
    /// <returns>A new <see cref="GraphicsDevice"/> using the Direct3D 11 API.</returns>
    public static GraphicsDevice CreateD3D11(GraphicsDeviceOptions options, SwapchainDescription swapchainDescription)
    {
        return new D3D11.D3D11GraphicsDevice(options, new D3D11DeviceOptions(), swapchainDescription);
    }

    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> using Direct3D 11.
    /// </summary>
    /// <param name="options">Describes several common properties of the GraphicsDevice.</param>
    /// <param name="d3d11Options">The Direct3D11-specific options used to create the device.</param>
    /// <returns>A new <see cref="GraphicsDevice"/> using the Direct3D 11 API.</returns>
    public static GraphicsDevice CreateD3D11(GraphicsDeviceOptions options, D3D11DeviceOptions d3d11Options)
    {
        return new D3D11.D3D11GraphicsDevice(options, d3d11Options, null);
    }

    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> using Direct3D 11, with a main Swapchain.
    /// </summary>
    /// <param name="options">Describes several common properties of the GraphicsDevice.</param>
    /// <param name="d3d11Options">The Direct3D11-specific options used to create the device.</param>
    /// <param name="swapchainDescription">A description of the main Swapchain to create.</param>
    /// <returns>A new <see cref="GraphicsDevice"/> using the Direct3D 11 API.</returns>
    public static GraphicsDevice CreateD3D11(GraphicsDeviceOptions options, D3D11DeviceOptions d3d11Options, SwapchainDescription swapchainDescription)
    {
        return new D3D11.D3D11GraphicsDevice(options, d3d11Options, swapchainDescription);
    }

    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> using Direct3D 11, with a main Swapchain.
    /// </summary>
    /// <param name="options">Describes several common properties of the GraphicsDevice.</param>
    /// <param name="hwnd">The Win32 window handle to render into.</param>
    /// <param name="width">The initial width of the window.</param>
    /// <param name="height">The initial height of the window.</param>
    /// <returns>A new <see cref="GraphicsDevice"/> using the Direct3D 11 API.</returns>
    public static GraphicsDevice CreateD3D11(GraphicsDeviceOptions options, IntPtr hwnd, uint width, uint height)
    {
        SwapchainDescription swapchainDescription = new(
            SwapchainSource.CreateWin32(hwnd, IntPtr.Zero),
            width, height,
            options.SwapchainDepthFormat,
            options.SyncToVerticalBlank,
            options.SwapchainSrgbFormat);

        return new D3D11.D3D11GraphicsDevice(options, new D3D11DeviceOptions(), swapchainDescription);
    }
#endif

#if !EXCLUDE_VULKAN_BACKEND
    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> using Vulkan.
    /// </summary>
    /// <param name="options">Describes several common properties of the GraphicsDevice.</param>
    /// <returns>A new <see cref="GraphicsDevice"/> using the Vulkan API.</returns>
    public static GraphicsDevice CreateVulkan(GraphicsDeviceOptions options)
    {
        return new Vk.VkGraphicsDevice(options, null);
    }

    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> using Vulkan.
    /// </summary>
    /// <param name="options">Describes several common properties of the GraphicsDevice.</param>
    /// <param name="vkOptions">The Vulkan-specific options used to create the device.</param>
    /// <returns>A new <see cref="GraphicsDevice"/> using the Vulkan API.</returns>
    public static GraphicsDevice CreateVulkan(GraphicsDeviceOptions options, VulkanDeviceOptions vkOptions)
    {
        return new Vk.VkGraphicsDevice(options, null, vkOptions);
    }

    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> using Vulkan, with a main Swapchain.
    /// </summary>
    /// <param name="options">Describes several common properties of the GraphicsDevice.</param>
    /// <param name="swapchainDescription">A description of the main Swapchain to create.</param>
    /// <returns>A new <see cref="GraphicsDevice"/> using the Vulkan API.</returns>
    public static GraphicsDevice CreateVulkan(GraphicsDeviceOptions options, SwapchainDescription swapchainDescription)
    {
        return new Vk.VkGraphicsDevice(options, swapchainDescription);
    }

    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> using Vulkan, with a main Swapchain.
    /// </summary>
    /// <param name="options">Describes several common properties of the GraphicsDevice.</param>
    /// <param name="vkOptions">The Vulkan-specific options used to create the device.</param>
    /// <param name="swapchainDescription">A description of the main Swapchain to create.</param>
    /// <returns>A new <see cref="GraphicsDevice"/> using the Vulkan API.</returns>
    public static GraphicsDevice CreateVulkan(
        GraphicsDeviceOptions options,
        SwapchainDescription swapchainDescription,
        VulkanDeviceOptions vkOptions)
    {
        return new Vk.VkGraphicsDevice(options, swapchainDescription, vkOptions);
    }
#endif

#if !EXCLUDE_OPENGL_BACKEND
    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> using OpenGL or OpenGL ES, with a main Swapchain.
    /// </summary>
    /// <param name="options">Describes several common properties of the GraphicsDevice.</param>
    /// <param name="platformInfo">An <see cref="OpenGL.OpenGLPlatformInfo"/> object encapsulating necessary OpenGL context
    /// information.</param>
    /// <param name="width">The initial width of the window.</param>
    /// <param name="height">The initial height of the window.</param>
    /// <returns>A new <see cref="GraphicsDevice"/> using the OpenGL or OpenGL ES API.</returns>
    public static GraphicsDevice CreateOpenGL(
        GraphicsDeviceOptions options,
        OpenGL.OpenGLPlatformInfo platformInfo,
        uint width,
        uint height)
    {
        return new OpenGL.OpenGLGraphicsDevice(options, platformInfo, width, height);
    }
#endif

}
