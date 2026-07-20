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
    private readonly List<(ulong ExecutionId, IDisposable Disposable)> _executionRetiredDisposables = [];
    private Sampler _aniso4xSampler;
    private bool _disposed;
    private readonly object _nullTextureLock = new();
    private DeviceBuffer _nullStructuredRead;
    private DeviceBuffer _nullStructuredReadWrite;

    /// <summary>Max graph executions in flight at once.</summary>
    protected internal uint _maxExecutingTasks;

    /// <summary>Starting size of each slot's transient bump-allocator buffer, in bytes.</summary>
    protected internal uint _transientInitialSize;

    /// <summary>Soft cap for per-execution transient usage, in bytes.</summary>
    protected internal uint _transientSoftCapBytes;

    /// <summary>Hard cap for per-execution transient usage, in bytes.</summary>
    protected internal uint _transientHardCapBytes;

    /// <summary>Execution counter, always going up. 0 means nothing has started yet.</summary>
    protected ulong _executionIdCounter;

    /// <summary>Id of the last execution known to be done. Updated when convenient.</summary>
    protected ulong _lastCompletedExecutionId;

    private readonly object _executionLock = new();
    private readonly List<ExecutionTask> _activeTasks = [];
    private Queue<uint> _freeSlots;

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

    /// <summary>
    /// Id of the most recent GPU-completed execution. Advances as executions get reclaimed. 0 means nothing done yet.
    /// </summary>
    public ulong LastCompletedExecutionId => Volatile.Read(ref _lastCompletedExecutionId);

    /// <summary>
    /// Hard cap on how many graph executions can be in flight at once. Beyond this, BeginExecution blocks until the oldest finishes.
    /// </summary>
    public uint MaxExecutingTasks => _maxExecutingTasks;

    /// <summary>
    /// Number of graph executions currently in flight. Reclaims finished ones as a side effect.
    /// </summary>
    public uint ExecutingTasks
    {
        get
        {
            lock (_executionLock)
            {
                ReclaimCompletedExecutions_NoLock();
                return (uint)_activeTasks.Count;
            }
        }
    }

    /// <summary>
    /// Snapshot of in-flight executions, oldest first.
    /// </summary>
    public IReadOnlyList<ExecutionTask> ActiveExecutions
    {
        get
        {
            lock (_executionLock)
            {
                ReclaimCompletedExecutions_NoLock();
                return _activeTasks.ToArray();
            }
        }
    }

    /// <summary>
    /// Starts a new graph execution and returns its handle. Grabs a free ring slot; if all slots are busy, blocks on the oldest execution first.
    /// <para>
    /// Frame-less replacement for the old BeginFrame/EndFrame pair. There's no "current" execution - the render graph builds on this directly.
    /// </para>
    /// </summary>
    /// <returns>Handle to the new in-flight execution.</returns>
    public ExecutionTask BeginExecution()
    {
        lock (_executionLock)
        {
            ReclaimCompletedExecutions_NoLock();

            if (_freeSlots.Count == 0)
            {
                ExecutionTask oldest = _activeTasks[0];
                WaitForExecutionCore(oldest, ulong.MaxValue);
                ReclaimCompletedExecutions_NoLock();
            }

            uint ringSlot = _freeSlots.Dequeue();
            ulong id = ++_executionIdCounter;
            SnapshotExecutionCounters();

            ExecutionTask task = BeginExecutionCore(id, ringSlot);
            _activeTasks.Add(task);
            return task;
        }
    }

    /// <summary>
    /// Marks an execution done: its fence signals once submitted work finishes on the GPU. Non-blocking. Stays in flight until the fence fires and the slot is reclaimed. Frame-less replacement for old EndFrame.
    /// </summary>
    /// <param name="task">Execution to complete. Must come from BeginExecution.</param>
    /// <exception cref="ArgumentNullException">Thrown if task is null.</exception>
    public void CompleteExecution(ExecutionTask task)
    {
        ValidationHelpers.RequireNotNull(task, nameof(task), nameof(CompleteExecution));
        CompleteExecutionCore(task);
    }

    /// <summary>
    /// Whether the given execution has finished on the GPU. Advances LastCompletedExecutionId as a side effect.
    /// </summary>
    /// <param name="task">Execution to check.</param>
    /// <returns>True if complete, false if still in flight.</returns>
    public bool IsExecutionComplete(ExecutionTask task)
    {
        ValidationHelpers.RequireNotNull(task, nameof(task), nameof(IsExecutionComplete));
        bool complete = IsExecutionCompleteCore(task);
        if (complete)
            Volatile.Write(ref _lastCompletedExecutionId, Math.Max(Volatile.Read(ref _lastCompletedExecutionId), task.Id));
        return complete;
    }

    /// <summary>
    /// Whether the execution with this id has finished. Used by the transient texture pool for reclaim checks. An id that never started counts as not complete.
    /// </summary>
    internal bool IsExecutionIdComplete(ulong executionId)
    {
        if (executionId == 0)
            return true;

        lock (_executionLock)
        {
            foreach (ExecutionTask task in _activeTasks)
            {
                if (task.Id == executionId)
                    return IsExecutionCompleteCore(task);
            }

            // Not in flight: either it started and was already reclaimed (complete), or it was never started.
            return executionId <= _executionIdCounter;
        }
    }

    /// <summary>
    /// Blocks until the given execution finishes on the GPU.
    /// </summary>
    /// <param name="task">Execution to wait for.</param>
    public void WaitForExecution(ExecutionTask task)
    {
        if (!WaitForExecution(task, ulong.MaxValue))
            throw new RenderException("The operation timed out before the execution completed.");
    }

    /// <summary>
    /// Blocks until the given execution finishes on the GPU, or until timeout.
    /// </summary>
    /// <param name="task">Execution to wait for.</param>
    /// <param name="nanosecondTimeout">Max wait time in nanoseconds. Use ulong.MaxValue for no timeout.</param>
    /// <returns>True if it finished before timeout, false otherwise.</returns>
    public bool WaitForExecution(ExecutionTask task, ulong nanosecondTimeout)
    {
        ValidationHelpers.RequireNotNull(task, nameof(task), nameof(WaitForExecution));
        bool completed = WaitForExecutionCore(task, nanosecondTimeout);
        if (completed)
            Volatile.Write(ref _lastCompletedExecutionId, Math.Max(Volatile.Read(ref _lastCompletedExecutionId), task.Id));
        return completed;
    }

    /// <summary>
    /// Disposes the object once the given execution finishes on the GPU. Freed the next time the device reclaims completed executions (next BeginExecution or WaitForIdle).
    /// </summary>
    /// <param name="executionId">Execution whose completion gates the disposal.</param>
    /// <param name="disposable">Object to dispose once done.</param>
    internal void DisposeWhenFrameComplete(ulong executionId, IDisposable disposable)
    {
        if (executionId == 0)
        {
            disposable.Dispose();
            return;
        }

        lock (_deferredDisposalLock)
        {
            _executionRetiredDisposables.Add((executionId, disposable));
        }
    }

    private void ReclaimCompletedExecutions_NoLock()
    {
        for (int i = _activeTasks.Count - 1; i >= 0; i--)
        {
            ExecutionTask task = _activeTasks[i];
            if (!IsExecutionCompleteCore(task))
                continue;

            _activeTasks.RemoveAt(i);
            _freeSlots.Enqueue(task.RingSlot);
            Volatile.Write(ref _lastCompletedExecutionId, Math.Max(Volatile.Read(ref _lastCompletedExecutionId), task.Id));
        }

        FlushExecutionRetiredDisposables();
    }

    private void FlushExecutionRetiredDisposables()
    {
        lock (_deferredDisposalLock)
        {
            for (int i = _executionRetiredDisposables.Count - 1; i >= 0; i--)
            {
                (ulong executionId, IDisposable disposable) = _executionRetiredDisposables[i];
                if (!IsExecutionIdCompleteFromReclaim(executionId))
                    continue;

                _executionRetiredDisposables.RemoveAt(i);
                disposable.Dispose();
            }
        }
    }

    // Reclaim-time completeness check that does not take _executionLock (the caller already holds it):
    // an id no longer among the active tasks has been reclaimed and is therefore complete.
    private bool IsExecutionIdCompleteFromReclaim(ulong executionId)
    {
        foreach (ExecutionTask task in _activeTasks)
        {
            if (task.Id == executionId)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Submits a recorded transfer command buffer right away and blocks until the GPU finishes it. Doesn't touch the execution ring or any fences - not tied to a graph execution at all. For one-off transfer work like readback or streaming uploads.
    /// </summary>
    /// <param name="commandBuffer">Recorded transfer command buffer to submit. Must have had End called already.</param>
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
    /// Submits a recorded transfer command buffer without blocking the calling thread. Doesn't touch
    /// the execution ring or any fences - not tied to a graph execution at all.
    /// </summary>
    /// <param name="commandBuffer">Recorded transfer command buffer to submit. Must have had End called already.</param>
    internal void SubmitTransfer(TransferCommandBuffer commandBuffer)
    {
        SubmitAndWait_CheckEnded(commandBuffer);
        SubmitTransferCore(commandBuffer);
    }

    private protected virtual void SubmitTransferCore(TransferCommandBuffer commandBuffer)
    {
        throw new RenderException($"{GetType().Name} does not support {nameof(SubmitTransfer)}.");
    }

    private protected abstract ExecutionTask BeginExecutionCore(ulong executionId, uint ringSlot);
    private protected abstract void CompleteExecutionCore(ExecutionTask task);
    private protected abstract bool IsExecutionCompleteCore(ExecutionTask task);
    private protected abstract bool WaitForExecutionCore(ExecutionTask task, ulong nanosecondTimeout);

    /// <summary>
    /// Sets up execution/transient options from the given options. Call before PostDeviceCreated in each backend constructor.
    /// </summary>
    /// <param name="options">Options to read from.</param>
    protected void InitializeFrameOptions(GraphicsDeviceOptions options)
    {
        _maxExecutingTasks = options.MaxFramesInFlight == 0 ? 3 : options.MaxFramesInFlight;
        _freeSlots = new Queue<uint>((int)_maxExecutingTasks);
        for (uint i = 0; i < _maxExecutingTasks; i++)
            _freeSlots.Enqueue(i);
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
    /// Blocks until the given fence signals.
    /// </summary>
    /// <param name="fence">Fence to wait on.</param>
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
            for (uint i = 0; i < _maxExecutingTasks; i++)
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
        _transientTexturePool?.Dispose();
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
