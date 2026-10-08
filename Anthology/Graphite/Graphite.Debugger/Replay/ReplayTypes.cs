using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger;

/// <summary>How a replay produced its outputs.</summary>
public enum ReplayStatus : byte
{
    Restored,
    Reexecuted,
    NotReplayable,
}

/// <summary>Which pass to replay.</summary>
public sealed class ReplayRequest
{
    /// <summary>Execution id as recorded.</summary>
    public ulong ExecutionId { get; init; }

    /// <summary>View index within the execution.</summary>
    public int ViewIndex { get; init; }

    /// <summary>Pass index within the view.</summary>
    public int PassIndex { get; init; }
}

/// <summary>A pass output read back after replay, laid out like the recorded copy.</summary>
public sealed record ReplayOutput(TraceResourceId Resource, EquatableArray<Debugging.CopyRegion> Regions, EquatableArray<byte> Data);

/// <summary>The outcome of a replay. Reason explains a NotReplayable status or what was executed.</summary>
public sealed record ReplayResult(ReplayStatus Status, string Reason, EquatableArray<ReplayOutput> Outputs);
