using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger;

/// <summary>Views, passes, and GPU timings for the executions of one recording. Reading data before it is done throws.</summary>
public sealed class Recording : RecordingHandle
{
    private readonly GraphicsDevice? _device;
    private RecordingSink? _sink;
    private EquatableArray<RecordedExecution> _executions = EquatableArray<RecordedExecution>.Empty;

    internal Recording(GraphicsDevice device, RecordingSink sink, RecordingHandle? previous)
        : base(previous)
    {
        _device = device;
        _sink = sink;
    }

    /// <summary>The recorded executions in start order.</summary>
    public EquatableArray<RecordedExecution> Executions
    {
        get
        {
            RequireDone();
            return _executions;
        }
    }

    internal override bool PollReady()
    {
        RecordingSink sink = _sink!;
        if (sink.LastExecutionId is { } last)
            _device!.IsExecutionComplete(last);

        return sink.AllResolved;
    }

    internal override bool WaitForExecutions()
    {
        RecordingSink sink = _sink!;
        return sink.FirstUnresolvedExecutionId is not { } first || _device!.WaitForExecution(first, 5_000_000);
    }

    internal override void Finish()
    {
        _executions = _sink!.Build();
        _sink = null;
    }
}
