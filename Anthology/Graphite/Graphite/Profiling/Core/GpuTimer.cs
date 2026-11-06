namespace Prowl.Graphite;

/// <summary>
/// The GPU time of the passes that measure with it, read back once the GPU has run them, a few frames after they were
/// recorded. The passes of one execution add up. Timing only the passes that ask leaves the rest unmeasured.
/// </summary>
public sealed class GpuTimer
{
    private readonly object _lock = new();
    private ulong _execution;
    private double _sum, _last;
    private long _measurements;

    /// <summary>Milliseconds the GPU took over the last execution measured in full, 0 before any.</summary>
    public double Milliseconds
    {
        get { lock (_lock) return _last; }
    }

    /// <summary>How many executions have been measured in full, so a reader can tell a new measurement from the last.</summary>
    public long Measurements
    {
        get { lock (_lock) return _measurements; }
    }

    internal void Record(ulong execution, double milliseconds)
    {
        lock (_lock)
        {
            if (execution != _execution)
            {
                if (_execution != 0)
                {
                    _last = _sum;
                    _measurements++;
                }
                (_execution, _sum) = (execution, 0);
            }
            _sum += milliseconds;
        }
    }
}
