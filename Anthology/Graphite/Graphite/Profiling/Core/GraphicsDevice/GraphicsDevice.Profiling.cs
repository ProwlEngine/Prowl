using System.Collections.Generic;
using System.Threading;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>Always-on counters of what the backend is doing.</summary>
    public GraphicsCounters Counters { get; } = new();

    private readonly object _profilerLock = new();
    private readonly List<IProfiler> _attachedProfilers = new();
    private ProfilerSet _pendingProfilers = ProfilerSet.Empty;

    private long _pipelineIdCounter;

    internal ProfilerSet Profilers => Volatile.Read(ref _pendingProfilers);

    internal ulong NextPipelineId() => (ulong)Interlocked.Increment(ref _pipelineIdCounter);

    private void InitializeFrameOptions_InitializeProfiling(in GraphicsDeviceOptions options)
    {
        AttachProfiler(options.Profiler);
    }

    internal void AttachProfiler(IProfiler? profiler)
    {
        if (profiler == null)
            return;

        lock (_profilerLock)
        {
            if (_attachedProfilers.Contains(profiler))
                return;

            _attachedProfilers.Add(profiler);
            Volatile.Write(ref _pendingProfilers, new ProfilerSet(_attachedProfilers));
        }
    }

    internal void DetachProfiler(IProfiler? profiler)
    {
        if (profiler == null)
            return;

        lock (_profilerLock)
        {
            if (!_attachedProfilers.Remove(profiler))
                return;

            Volatile.Write(ref _pendingProfilers, new ProfilerSet(_attachedProfilers));
        }
    }
}
