using System.Diagnostics;

#if PROFILE_USAGE
using System;
using System.Threading;
#endif


namespace Prowl.Veldrid;


public abstract partial class GraphicsDevice
{
#if PROFILE_USAGE
    // Flow accumulators - mutated during the frame, snapshotted and zeroed each BeginFrame.
    private readonly ProfileCell[] _allocated = NewBins<AllocBin>();
    private readonly ProfileCell[] _freed = NewBins<AllocBin>();
    private readonly ProfileCell[] _bufferOps = NewBins<BufferOpBin>();
    private readonly ProfileCell[] _swaps = NewBins<SwapBin>();

    // Gauges - live resident state, persist across frames until ResetProfile.
    private readonly ProfileCell[] _live = NewBins<AllocBin>();
    private readonly ProfileCell[] _bufferMem = NewBins<BufferRoleBin>();

    // Last completed frame's flows. Replaced (never mutated) each BeginFrame, so any
    // ProfileSnapshot handed out keeps pointing at the array it was built with.
    private ProfileCounter[] _allocatedLast = NewFrame<AllocBin>();
    private ProfileCounter[] _freedLast = NewFrame<AllocBin>();
    private ProfileCounter[] _bufferOpsLast = NewFrame<BufferOpBin>();
    private ProfileCounter[] _swapsLast = NewFrame<SwapBin>();

    private static ProfileCell[] NewBins<TBin>() where TBin : struct, Enum
        => new ProfileCell[Enum.GetValues<TBin>().Length];

    private static ProfileCounter[] NewFrame<TBin>() where TBin : struct, Enum
        => new ProfileCounter[Enum.GetValues<TBin>().Length];
#endif

    /// <summary>
    /// Records the creation of a resource: bumps the per-frame allocation flow and the live
    /// resident gauge for the given type.
    /// </summary>
    [Conditional("PROFILE_USAGE")]
    internal void RecordAllocation(AllocBin type, long bytes)
    {
#if PROFILE_USAGE
        Add(_allocated, (int)type, 1, bytes);
        Add(_live, (int)type, 1, bytes);
#endif
    }

    /// <summary>
    /// Records the creation of a buffer: a single <see cref="AllocBin.DeviceBuffer"/> allocation
    /// (the authoritative, non-double-counted total) plus resident memory under every role gauge
    /// matching a set <see cref="BufferUsage"/> flag. The role gauges intentionally overlap for
    /// multi-flag buffers, so they must not be summed; use the DeviceBuffer bin for a real total.
    /// </summary>
    [Conditional("PROFILE_USAGE")]
    internal void RecordBufferAllocation(BufferUsage usage, long bytes)
    {
#if PROFILE_USAGE
        RecordAllocation(AllocBin.DeviceBuffer, bytes);
        AddBufferRoles(usage, 1, bytes);
#endif
    }

    /// <summary>
    /// Records the destruction of a resource: bumps the per-frame free flow and decrements the
    /// live resident gauge for the given type.
    /// </summary>
    [Conditional("PROFILE_USAGE")]
    internal void RecordFree(AllocBin type, long bytes)
    {
#if PROFILE_USAGE
        Add(_freed, (int)type, 1, bytes);
        Add(_live, (int)type, -1, -bytes);
#endif
    }

    /// <summary>
    /// Records the destruction of a buffer: a single <see cref="AllocBin.DeviceBuffer"/> free
    /// plus a decrement of resident memory under every role gauge matching a set
    /// <see cref="BufferUsage"/> flag. Mirrors <see cref="RecordBufferAllocation"/>.
    /// </summary>
    [Conditional("PROFILE_USAGE")]
    internal void RecordBufferFree(BufferUsage usage, long bytes)
    {
#if PROFILE_USAGE
        RecordFree(AllocBin.DeviceBuffer, bytes);
        AddBufferRoles(usage, -1, -bytes);
#endif
    }

    /// <summary>Records a buffer data-transfer operation into the per-frame flow.</summary>
    [Conditional("PROFILE_USAGE")]
    internal void RecordBufferOp(BufferOpBin op, long bytes)
    {
#if PROFILE_USAGE
        Add(_bufferOps, (int)op, 1, bytes);
#endif
    }

    /// <summary>Records a swapchain event into the per-frame flow.</summary>
    [Conditional("PROFILE_USAGE")]
    internal void RecordSwap(SwapBin swap, long bytes)
    {
#if PROFILE_USAGE
        Add(_swaps, (int)swap, 1, bytes);
#endif
    }

    /// <summary>
    /// Rotates the per-frame flow accumulators: freezes them into the last-frame view and
    /// zeroes them for the new frame. Gauges are left untouched.
    /// </summary>
    [Conditional("PROFILE_USAGE")]
    private void BeginFrame_SnapshotFrameCounters()
    {
#if PROFILE_USAGE
        _allocatedLast = Capture(_allocated);
        ZeroBins(_allocated);
        _freedLast = Capture(_freed);
        ZeroBins(_freed);
        _bufferOpsLast = Capture(_bufferOps);
        ZeroBins(_bufferOps);
        _swapsLast = Capture(_swaps);
        ZeroBins(_swaps);
#endif
    }

    /// <summary>
    /// Returns an immutable snapshot of the profiling counters: the last completed frame's flows
    /// plus the current live gauges. Returns a zeroed snapshot when profiling is disabled.
    /// </summary>
    public ProfileSnapshot GetProfile()
    {
#if PROFILE_USAGE
        return new ProfileSnapshot(
            new ProfileBinGroup<AllocBin>(_allocatedLast),
            new ProfileBinGroup<AllocBin>(_freedLast),
            new ProfileBinGroup<BufferOpBin>(_bufferOpsLast),
            new ProfileBinGroup<SwapBin>(_swapsLast),
            new ProfileBinGroup<AllocBin>(Capture(_live)),
            new ProfileBinGroup<BufferRoleBin>(Capture(_bufferMem)));
#else
        return default;
#endif
    }

    /// <summary>Zeroes every profiling counter, including the live gauges and frame history.</summary>
    public void ResetProfile()
    {
#if PROFILE_USAGE
        ZeroBins(_allocated);
        ZeroBins(_freed);
        ZeroBins(_bufferOps);
        ZeroBins(_swaps);
        ZeroBins(_live);
        ZeroBins(_bufferMem);

        _allocatedLast = NewFrame<AllocBin>();
        _freedLast = NewFrame<AllocBin>();
        _bufferOpsLast = NewFrame<BufferOpBin>();
        _swapsLast = NewFrame<SwapBin>();
#endif
    }

#if PROFILE_USAGE
    // Records the given count/bytes delta into every BufferMem role bin matching a set usage flag.
    // The bins overlap by design for multi-flag buffers (see RecordBufferAllocation).
    private void AddBufferRoles(BufferUsage usage, long count, long bytes)
    {
        if ((usage & BufferUsage.VertexBuffer) != 0)
            Add(_bufferMem, (int)BufferRoleBin.Vertex, count, bytes);
        if ((usage & BufferUsage.IndexBuffer) != 0)
            Add(_bufferMem, (int)BufferRoleBin.Index, count, bytes);
        if ((usage & BufferUsage.UniformBuffer) != 0)
            Add(_bufferMem, (int)BufferRoleBin.Uniform, count, bytes);
        if ((usage & BufferUsage.StructuredBufferReadOnly) != 0)
            Add(_bufferMem, (int)BufferRoleBin.StructuredReadOnly, count, bytes);
        if ((usage & BufferUsage.StructuredBufferReadWrite) != 0)
            Add(_bufferMem, (int)BufferRoleBin.StructuredReadWrite, count, bytes);
        if ((usage & BufferUsage.IndirectBuffer) != 0)
            Add(_bufferMem, (int)BufferRoleBin.Indirect, count, bytes);
        if ((usage & BufferUsage.Dynamic) != 0)
            Add(_bufferMem, (int)BufferRoleBin.Dynamic, count, bytes);
        if ((usage & BufferUsage.Staging) != 0)
            Add(_bufferMem, (int)BufferRoleBin.Staging, count, bytes);
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
#endif
}
