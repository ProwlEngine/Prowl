using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

internal sealed class RenderStore : IDisposable
{
    private readonly List<(DeviceBuffer From, DeviceBuffer To)> _resizes = new();
    private readonly List<IDisposable> _retired = new();

    public bool HasResizes => _resizes.Count > 0;

    public void Resized(DeviceBuffer from, DeviceBuffer to) => _resizes.Add((from, to));

    public void Retire(IDisposable resource) => _retired.Add(resource);

    public void RecordResizes(CommandBuffer cmd)
    {
        cmd.RecordFullBarrier();
        foreach ((DeviceBuffer from, DeviceBuffer to) in _resizes)
            cmd.CopyBuffer(from, 0, to, 0, Math.Min(from.SizeInBytes, to.SizeInBytes));
        cmd.RecordFullBarrier();
        foreach ((DeviceBuffer from, _) in _resizes)
            _retired.Add(from);
        _resizes.Clear();
    }

    public void RetireAll(ExecutionTask task)
    {
        foreach (IDisposable resource in _retired)
            task.Retire(resource);
        _retired.Clear();
    }

    public void Dispose()
    {
        foreach ((DeviceBuffer from, _) in _resizes)
            from.Dispose();
        _resizes.Clear();
        foreach (IDisposable resource in _retired)
            resource.Dispose();
        _retired.Clear();
    }
}
