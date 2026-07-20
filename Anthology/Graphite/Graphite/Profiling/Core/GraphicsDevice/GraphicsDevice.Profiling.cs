using System;
using System.Threading;

namespace Prowl.Graphite;


public abstract partial class GraphicsDevice
{
    /// <summary>
    /// True means profiling records counters. Set once at device creation.
    /// </summary>
    internal static bool ProfilingEnabled;

    // Flow accumulators - mutated during the frame, snapshotted and zeroed each BeginFrame.
    // Allocated only when profiling is enabled; null otherwise.
    private ProfileCell[]? _allocated;
    private ProfileCell[]? _freed;
    private ProfileCell[]? _bufferOps;
    private ProfileCell[]? _swaps;

    // Gauges - live resident state, persist across frames until ResetProfile.
    private ProfileCell[]? _live;
    private ProfileCell[]? _bufferMem;

    // Last completed frame's flows. Replaced (never mutated) each BeginFrame, so any
    // ProfileSnapshot handed out keeps pointing at the array it was built with.
    private ProfileCounter[]? _allocatedLast;
    private ProfileCounter[]? _freedLast;
    private ProfileCounter[]? _bufferOpsLast;
    private ProfileCounter[]? _swapsLast;

    private void InitializeFrameOptions_InitializeProfiling(in GraphicsDeviceOptions options)
    {
        ProfilingEnabled = options.EnableProfiling ?? false;
        if (!ProfilingEnabled)
            return;

        _allocated = NewBins<AllocBin>();
        _freed = NewBins<AllocBin>();
        _bufferOps = NewBins<BufferOpBin>();
        _swaps = NewBins<SwapBin>();

        _live = NewBins<AllocBin>();
        _bufferMem = NewBins<BufferRoleBin>();

        _allocatedLast = NewFrame<AllocBin>();
        _freedLast = NewFrame<AllocBin>();
        _bufferOpsLast = NewFrame<BufferOpBin>();
        _swapsLast = NewFrame<SwapBin>();
    }

    private static ProfileCell[] NewBins<TBin>() where TBin : struct, Enum
        => new ProfileCell[Enum.GetValues<TBin>().Length];

    private static ProfileCounter[] NewFrame<TBin>() where TBin : struct, Enum
        => new ProfileCounter[Enum.GetValues<TBin>().Length];

    /// <summary>
    /// Records a resource creation: bumps per-frame allocation flow and live gauge for the type.
    /// </summary>
    internal void RecordAllocation(AllocBin type, long bytes)
    {
        if (!ProfilingEnabled)
            return;

        Add(_allocated!, (int)type, 1, bytes);
        Add(_live!, (int)type, 1, bytes);
    }

    /// <summary>
    /// Records a buffer creation: one DeviceBuffer allocation (the real total, not double-counted)
    /// plus memory under every matching usage-flag role gauge. Role gauges overlap on purpose for
    /// multi-flag buffers, don't sum them - use the DeviceBuffer bin for the total.
    /// </summary>
    internal void RecordBufferAllocation(BufferUsage usage, long bytes)
    {
        if (!ProfilingEnabled)
            return;

        RecordAllocation(AllocBin.DeviceBuffer, bytes);
        AddBufferRoles(usage, 1, bytes);
    }

    /// <summary>
    /// Records a resource destruction: bumps per-frame free flow, decrements live gauge for the type.
    /// </summary>
    internal void RecordFree(AllocBin type, long bytes)
    {
        if (!ProfilingEnabled)
            return;

        Add(_freed!, (int)type, 1, bytes);
        Add(_live!, (int)type, -1, -bytes);
    }

    /// <summary>
    /// Records a buffer destruction: one DeviceBuffer free plus a decrement on every matching
    /// role gauge. Mirrors RecordBufferAllocation.
    /// </summary>
    internal void RecordBufferFree(BufferUsage usage, long bytes)
    {
        if (!ProfilingEnabled)
            return;

        RecordFree(AllocBin.DeviceBuffer, bytes);
        AddBufferRoles(usage, -1, -bytes);
    }

    /// <summary>Records a buffer data-transfer into the per-frame flow.</summary>
    internal void RecordBufferOp(BufferOpBin op, long bytes)
    {
        if (!ProfilingEnabled)
            return;

        Add(_bufferOps!, (int)op, 1, bytes);
    }

    /// <summary>Records a swapchain event.</summary>
    internal void RecordSwap(SwapBin swap, long bytes)
    {
        if (!ProfilingEnabled)
            return;

        Add(_swaps!, (int)swap, 1, bytes);
    }

    /// <summary>
    /// Rotates per-execution flow accumulators: freezes into last-execution view, zeroes for the
    /// new execution. Gauges untouched.
    /// </summary>
    private void SnapshotExecutionCounters()
    {
        if (!ProfilingEnabled)
            return;

        _allocatedLast = Capture(_allocated!);
        ZeroBins(_allocated!);
        _freedLast = Capture(_freed!);
        ZeroBins(_freed!);
        _bufferOpsLast = Capture(_bufferOps!);
        ZeroBins(_bufferOps!);
        _swapsLast = Capture(_swaps!);
        ZeroBins(_swaps!);
    }

    /// <summary>
    /// Immutable snapshot of profiling counters: last frame's flows plus current live gauges.
    /// Zeroed snapshot if profiling disabled.
    /// </summary>
    public ProfileSnapshot GetProfile()
    {
        if (!ProfilingEnabled)
            return default;

        return new ProfileSnapshot(
            new ProfileBinGroup<AllocBin>(_allocatedLast!),
            new ProfileBinGroup<AllocBin>(_freedLast!),
            new ProfileBinGroup<BufferOpBin>(_bufferOpsLast!),
            new ProfileBinGroup<SwapBin>(_swapsLast!),
            new ProfileBinGroup<AllocBin>(Capture(_live!)),
            new ProfileBinGroup<BufferRoleBin>(Capture(_bufferMem!)));
    }

    /// <summary>Zeroes every profiling counter, gauges and frame history included.</summary>
    public void ResetProfile()
    {
        if (!ProfilingEnabled)
            return;

        ZeroBins(_allocated!);
        ZeroBins(_freed!);
        ZeroBins(_bufferOps!);
        ZeroBins(_swaps!);
        ZeroBins(_live!);
        ZeroBins(_bufferMem!);

        _allocatedLast = NewFrame<AllocBin>();
        _freedLast = NewFrame<AllocBin>();
        _bufferOpsLast = NewFrame<BufferOpBin>();
        _swapsLast = NewFrame<SwapBin>();
    }

    // Records the given count/bytes delta into every BufferMem role bin matching a set usage flag.
    // The bins overlap by design for multi-flag buffers (see RecordBufferAllocation).
    private void AddBufferRoles(BufferUsage usage, long count, long bytes)
    {
        if ((usage & BufferUsage.VertexBuffer) != 0)
            Add(_bufferMem!, (int)BufferRoleBin.Vertex, count, bytes);
        if ((usage & BufferUsage.IndexBuffer) != 0)
            Add(_bufferMem!, (int)BufferRoleBin.Index, count, bytes);
        if ((usage & BufferUsage.UniformBuffer) != 0)
            Add(_bufferMem!, (int)BufferRoleBin.Uniform, count, bytes);
        if ((usage & BufferUsage.StructuredBufferReadOnly) != 0)
            Add(_bufferMem!, (int)BufferRoleBin.StructuredReadOnly, count, bytes);
        if ((usage & BufferUsage.StructuredBufferReadWrite) != 0)
            Add(_bufferMem!, (int)BufferRoleBin.StructuredReadWrite, count, bytes);
        if ((usage & BufferUsage.IndirectBuffer) != 0)
            Add(_bufferMem!, (int)BufferRoleBin.Indirect, count, bytes);
        if ((usage & BufferUsage.Dynamic) != 0)
            Add(_bufferMem!, (int)BufferRoleBin.Dynamic, count, bytes);
        if ((usage & BufferUsage.Staging) != 0)
            Add(_bufferMem!, (int)BufferRoleBin.Staging, count, bytes);
    }

    private static void Add(ProfileCell[] bins, int index, long count, long bytes)
    {
        Interlocked.Add(ref bins[index].Count, count);
        Interlocked.Add(ref bins[index].Bytes, bytes);
    }

    private static ProfileCounter[] Capture(ProfileCell[] bins)
    {
        ProfileCounter[] result = new ProfileCounter[bins.Length];
        for (int i = 0; i < bins.Length; i++)
        {
            result[i] = new ProfileCounter(
                Interlocked.Read(ref bins[i].Count),
                Interlocked.Read(ref bins[i].Bytes));
        }
        return result;
    }

    private static void ZeroBins(ProfileCell[] bins)
    {
        for (int i = 0; i < bins.Length; i++)
        {
            Interlocked.Exchange(ref bins[i].Count, 0);
            Interlocked.Exchange(ref bins[i].Bytes, 0);
        }
    }
}
