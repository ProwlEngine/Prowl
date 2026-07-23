using System;
using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;

public sealed class CountersCollector
{
    private static readonly AllocBin[] s_allocBins = Enum.GetValues<AllocBin>();
    private static readonly BufferRoleBin[] s_bufferRoleBins = Enum.GetValues<BufferRoleBin>();
    private static readonly BufferOpBin[] s_bufferOpBins = Enum.GetValues<BufferOpBin>();
    private static readonly SwapBin[] s_swapBins = Enum.GetValues<SwapBin>();
    private static readonly BarrierBin[] s_barrierBins = Enum.GetValues<BarrierBin>();
    private static readonly SubmitKind[] s_submitKinds = Enum.GetValues<SubmitKind>();

    private readonly long[] _liveCount = new long[s_allocBins.Length];
    private readonly long[] _residentBytes = new long[s_allocBins.Length];
    private readonly long[] _allocDelta = new long[s_allocBins.Length];
    private readonly long[] _freeDelta = new long[s_allocBins.Length];

    private readonly long[] _bufferRoleResidentBytes = new long[s_bufferRoleBins.Length];

    private readonly long[] _bufferOpCount = new long[s_bufferOpBins.Length];
    private readonly long[] _bufferOpBytes = new long[s_bufferOpBins.Length];

    private readonly long[] _swapCount = new long[s_swapBins.Length];
    private readonly long[] _barrierCount = new long[s_barrierBins.Length];
    private readonly long[] _submitCount = new long[s_submitKinds.Length];

    private uint _resourceSetBinds;
    private long _drawCount;
    private long _dispatchCount;

    private static readonly List<CounterDef> s_registry = BuildRegistry();

    private readonly double[] _values = new double[s_registry.Count];

    private readonly List<SubmitRecord> _pendingSubmits = [];

    public static List<CounterDef> Registry => s_registry;

    private static List<CounterDef> BuildRegistry()
    {
        var defs = new List<CounterDef>(
            s_allocBins.Length * 4 +
            s_bufferRoleBins.Length +
            s_bufferOpBins.Length * 2 +
            s_swapBins.Length +
            s_barrierBins.Length +
            s_submitKinds.Length +
            3);

        foreach (AllocBin bin in s_allocBins)
        {
            defs.Add(new CounterDef($"Live/{bin}", CounterCategory.EngineObject, CounterUnit.Count));
            defs.Add(new CounterDef($"Resident/{bin}", CounterCategory.EngineObject, CounterUnit.Bytes));
            defs.Add(new CounterDef($"Alloc/{bin}", CounterCategory.AllocFree, CounterUnit.Count));
            defs.Add(new CounterDef($"Free/{bin}", CounterCategory.AllocFree, CounterUnit.Count));
        }

        foreach (BufferRoleBin role in s_bufferRoleBins)
            defs.Add(new CounterDef($"Resident/{role}", CounterCategory.BufferMemory, CounterUnit.Bytes));

        foreach (BufferOpBin op in s_bufferOpBins)
        {
            defs.Add(new CounterDef($"BufferOp/{op}", CounterCategory.BufferUpdate, CounterUnit.Count));
            defs.Add(new CounterDef($"BufferOpBytes/{op}", CounterCategory.BufferUpdate, CounterUnit.Bytes));
        }

        foreach (SwapBin bin in s_swapBins)
            defs.Add(new CounterDef($"Swap/{bin}", CounterCategory.Swapchain, CounterUnit.Count));

        foreach (BarrierBin bin in s_barrierBins)
            defs.Add(new CounterDef($"Barrier/{bin}", CounterCategory.Barrier, CounterUnit.Count));

        foreach (SubmitKind kind in s_submitKinds)
            defs.Add(new CounterDef($"Submit/{kind}", CounterCategory.Submit, CounterUnit.Count));

        defs.Add(new CounterDef("ResourceSet/Binds", CounterCategory.ResourceSet, CounterUnit.Count));
        defs.Add(new CounterDef("Draw/Count", CounterCategory.DrawDispatch, CounterUnit.Count));
        defs.Add(new CounterDef("Dispatch/Count", CounterCategory.DrawDispatch, CounterUnit.Count));

        return defs;
    }

    public void OnFrameBegin()
    {
        Array.Clear(_allocDelta, 0, _allocDelta.Length);
        Array.Clear(_freeDelta, 0, _freeDelta.Length);
        Array.Clear(_bufferOpCount, 0, _bufferOpCount.Length);
        Array.Clear(_bufferOpBytes, 0, _bufferOpBytes.Length);
        Array.Clear(_swapCount, 0, _swapCount.Length);
        Array.Clear(_barrierCount, 0, _barrierCount.Length);
        Array.Clear(_submitCount, 0, _submitCount.Length);

        _resourceSetBinds = 0;
        _drawCount = 0;
        _dispatchCount = 0;
        _pendingSubmits.Clear();
    }

    public void OnAllocate(AllocBin t, long bytes)
    {
        int i = (int)t;
        _liveCount[i]++;
        _residentBytes[i] += bytes;
        _allocDelta[i]++;
    }

    public void OnFree(AllocBin t, long bytes)
    {
        int i = (int)t;
        _liveCount[i]--;
        _residentBytes[i] -= bytes;
        _freeDelta[i]++;
    }

    public void OnAllocateMemory(BufferRoleBin r, long bytes)
    {
        _bufferRoleResidentBytes[(int)r] += bytes;
    }

    public void OnFreeMemory(BufferRoleBin r, long bytes)
    {
        _bufferRoleResidentBytes[(int)r] -= bytes;
    }

    public void OnBufferOp(BufferOpBin op, long bytes)
    {
        int i = (int)op;
        _bufferOpCount[i]++;
        _bufferOpBytes[i] += bytes;
    }

    public void OnSwap(SwapBin e, long bytes)
    {
        _swapCount[(int)e]++;
    }

    public void OnResourceSetBind(uint setCount)
    {
        _resourceSetBinds += setCount;
    }

    public void OnBarrier(BarrierBin kind, uint count)
    {
        _barrierCount[(int)kind] += count;
    }

    public void OnSubmit(in ProfilerSubmitInfo s)
    {
        _submitCount[(int)s.Kind]++;
        _pendingSubmits.Add(new SubmitRecord(s.Kind, s.Name, s.CommandBufferCount));
    }

    public void OnDraw(in DrawCallInfo d)
    {
        _drawCount++;
    }

    public void OnDispatch(in DispatchCallInfo d)
    {
        _dispatchCount++;
    }

    public void Contribute(ProfiledFrame build)
    {
        int idx = 0;

        for (int i = 0; i < s_allocBins.Length; i++)
        {
            _values[idx++] = _liveCount[i];
            _values[idx++] = _residentBytes[i];
            _values[idx++] = _allocDelta[i];
            _values[idx++] = _freeDelta[i];
        }

        for (int i = 0; i < s_bufferRoleBins.Length; i++)
            _values[idx++] = _bufferRoleResidentBytes[i];

        for (int i = 0; i < s_bufferOpBins.Length; i++)
        {
            _values[idx++] = _bufferOpCount[i];
            _values[idx++] = _bufferOpBytes[i];
        }

        for (int i = 0; i < s_swapBins.Length; i++)
            _values[idx++] = _swapCount[i];

        for (int i = 0; i < s_barrierBins.Length; i++)
            _values[idx++] = _barrierCount[i];

        for (int i = 0; i < s_submitKinds.Length; i++)
            _values[idx++] = _submitCount[i];

        _values[idx++] = _resourceSetBinds;
        _values[idx++] = _drawCount;
        _values[idx++] = _dispatchCount;

        build.SetCounterValues(_values);

        foreach (SubmitRecord s in _pendingSubmits)
            build.AddSubmit(s);
    }
}
