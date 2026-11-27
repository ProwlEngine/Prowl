using System.Collections.Generic;
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
    /// <summary>Group index within the recorded execution.</summary>
    public int GroupIndex { get; init; }

    /// <summary>Pass index within the group.</summary>
    public int PassIndex { get; init; }

    /// <summary>Last event of the pass to run, counted from 0. -1 runs none, giving the contents the pass starts from. Null runs the whole pass.</summary>
    public int? EventIndex { get; init; }

    /// <summary>Narrows the last event, when it is an indirect draw, to some of its commands.</summary>
    public IndirectSlice? Indirect { get; init; }

    /// <summary>Bytes written over recorded buffers before the first replayed pass reads them. Buffers the replay does not use are ignored.</summary>
    public IReadOnlyList<BufferPatch>? Patches { get; init; }
}

/// <summary>The commands of an indirect draw to run: Count of them from First. Counts past the end are clamped.</summary>
public readonly record struct IndirectSlice(uint First, uint Count);

/// <summary>Data written at Offset into a recorded buffer, such as an indirect argument buffer, before the replay.</summary>
public sealed record BufferPatch(TraceResourceId Resource, uint Offset, byte[] Data);

/// <summary>A pass output read back after replay, laid out like the recorded copy.</summary>
public sealed record ReplayOutput(TraceResourceId Resource, EquatableArray<Debugging.CopyRegion> Regions, EquatableArray<byte> Data);

/// <summary>The outcome of a replay. Reason explains a NotReplayable status or what was executed.</summary>
public sealed record ReplayResult(ReplayStatus Status, string Reason, EquatableArray<ReplayOutput> Outputs);
