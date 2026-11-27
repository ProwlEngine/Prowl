using System;
using System.Threading;

namespace Prowl.Graphite;

/// <summary>Always-on backend counters. Updated inline by the device, read through <see cref="Snapshot"/>.</summary>
public sealed class GraphicsCounters
{
    private readonly long[] _live = new long[Enum.GetValues<AllocBin>().Length];
    private readonly long[] _liveBytes = new long[Enum.GetValues<AllocBin>().Length];
    private readonly long[] _resident = new long[Enum.GetValues<BufferRoleBin>().Length];
    private readonly long[] _barriers = new long[Enum.GetValues<BarrierBin>().Length];
    private readonly long[] _swaps = new long[Enum.GetValues<SwapBin>().Length];
    private readonly long[] _bufferOps = new long[Enum.GetValues<BufferOpBin>().Length];
    private readonly long[] _bufferOpBytes = new long[Enum.GetValues<BufferOpBin>().Length];
    private long _setBinds;
    private long _setsBound;

    internal void Allocate(AllocBin bin, long bytes)
    {
        Interlocked.Increment(ref _live[(int)bin]);
        Interlocked.Add(ref _liveBytes[(int)bin], bytes);
    }

    internal void Free(AllocBin bin, long bytes)
    {
        Interlocked.Decrement(ref _live[(int)bin]);
        Interlocked.Add(ref _liveBytes[(int)bin], -bytes);
    }

    internal void Allocate(AllocBin bin) => Interlocked.Increment(ref _live[(int)bin]);

    internal void Free(AllocBin bin) => Interlocked.Decrement(ref _live[(int)bin]);

    internal void AllocateMemory(BufferRoleBin role, long bytes) => Interlocked.Add(ref _resident[(int)role], bytes);

    internal void FreeMemory(BufferRoleBin role, long bytes) => Interlocked.Add(ref _resident[(int)role], -bytes);

    internal void RecordBufferOp(BufferOpBin op, long bytes)
    {
        Interlocked.Increment(ref _bufferOps[(int)op]);
        Interlocked.Add(ref _bufferOpBytes[(int)op], bytes);
    }

    internal void RecordSwap(SwapBin evt) => Interlocked.Increment(ref _swaps[(int)evt]);

    internal void RecordBarrier(BarrierBin kind) => Interlocked.Increment(ref _barriers[(int)kind]);

    internal void RecordBarrier(BarrierBin kind, uint count) => Interlocked.Add(ref _barriers[(int)kind], count);

    internal void RecordResourceSetBind(uint setCount)
    {
        Interlocked.Increment(ref _setBinds);
        Interlocked.Add(ref _setsBound, setCount);
    }

    internal void AllocateBuffer(BufferUsage usage, long bytes)
    {
        Allocate(AllocBin.DeviceBuffer, bytes);
        ForEachBufferRole(usage, bytes, 1);
    }

    internal void FreeBuffer(BufferUsage usage, long bytes)
    {
        Free(AllocBin.DeviceBuffer, bytes);
        ForEachBufferRole(usage, bytes, -1);
    }

    private void ForEachBufferRole(BufferUsage usage, long bytes, int sign)
    {
        long delta = bytes * sign;
        if ((usage & BufferUsage.VertexBuffer) != 0)
            Interlocked.Add(ref _resident[(int)BufferRoleBin.Vertex], delta);
        if ((usage & BufferUsage.IndexBuffer) != 0)
            Interlocked.Add(ref _resident[(int)BufferRoleBin.Index], delta);
        if ((usage & BufferUsage.UniformBuffer) != 0)
            Interlocked.Add(ref _resident[(int)BufferRoleBin.Uniform], delta);
        if ((usage & BufferUsage.StructuredBufferReadOnly) != 0)
            Interlocked.Add(ref _resident[(int)BufferRoleBin.StructuredReadOnly], delta);
        if ((usage & BufferUsage.StructuredBufferReadWrite) != 0)
            Interlocked.Add(ref _resident[(int)BufferRoleBin.StructuredReadWrite], delta);
        if ((usage & BufferUsage.IndirectBuffer) != 0)
            Interlocked.Add(ref _resident[(int)BufferRoleBin.Indirect], delta);
        if ((usage & BufferUsage.Dynamic) != 0)
            Interlocked.Add(ref _resident[(int)BufferRoleBin.Dynamic], delta);
        if ((usage & BufferUsage.Staging) != 0)
            Interlocked.Add(ref _resident[(int)BufferRoleBin.Staging], delta);
    }

    /// <summary>Copies the current values. Each field is read atomically, the set as a whole is not.</summary>
    public GraphicsCountersSnapshot Snapshot() => new(
        Copy(_live), Copy(_liveBytes), Copy(_resident), Copy(_barriers), Copy(_swaps), Copy(_bufferOps), Copy(_bufferOpBytes),
        Interlocked.Read(ref _setBinds), Interlocked.Read(ref _setsBound));

    private static long[] Copy(long[] source)
    {
        long[] copy = new long[source.Length];
        for (int i = 0; i < source.Length; i++)
            copy[i] = Interlocked.Read(ref source[i]);
        return copy;
    }
}

/// <summary>Immutable copy of <see cref="GraphicsCounters"/> at one point in time.</summary>
public readonly struct GraphicsCountersSnapshot
{
    private readonly long[] _live;
    private readonly long[] _liveBytes;
    private readonly long[] _resident;
    private readonly long[] _barriers;
    private readonly long[] _swaps;
    private readonly long[] _bufferOps;
    private readonly long[] _bufferOpBytes;

    /// <summary>Number of resource set bind calls.</summary>
    public long ResourceSetBinds { get; }

    /// <summary>Total sets bound across all bind calls.</summary>
    public long ResourceSetsBound { get; }

    internal GraphicsCountersSnapshot(
        long[] live, long[] liveBytes, long[] resident, long[] barriers, long[] swaps, long[] bufferOps, long[] bufferOpBytes,
        long setBinds, long setsBound)
    {
        _live = live;
        _liveBytes = liveBytes;
        _resident = resident;
        _barriers = barriers;
        _swaps = swaps;
        _bufferOps = bufferOps;
        _bufferOpBytes = bufferOpBytes;
        ResourceSetBinds = setBinds;
        ResourceSetsBound = setsBound;
    }

    /// <summary>Live object count for the bin.</summary>
    public long Live(AllocBin bin) => _live[(int)bin];

    /// <summary>Live bytes for the bin. Zero for bins that do not report a size.</summary>
    public long LiveBytes(AllocBin bin) => _liveBytes[(int)bin];

    /// <summary>Resident buffer bytes for the role. Multi-usage buffers count in every matching role.</summary>
    public long ResidentBytes(BufferRoleBin role) => _resident[(int)role];

    /// <summary>Barriers recorded for the kind.</summary>
    public long Barriers(BarrierBin kind) => _barriers[(int)kind];

    /// <summary>Swapchain events recorded for the kind.</summary>
    public long Swaps(SwapBin evt) => _swaps[(int)evt];

    /// <summary>Buffer operations recorded for the op.</summary>
    public long BufferOps(BufferOpBin op) => _bufferOps[(int)op];

    /// <summary>Bytes moved by buffer operations of the op.</summary>
    public long BufferOpBytes(BufferOpBin op) => _bufferOpBytes[(int)op];
}
