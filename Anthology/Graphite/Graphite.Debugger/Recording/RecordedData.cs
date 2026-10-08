using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger;

/// <summary>A pass of a recorded view, with the work its command buffer recorded.</summary>
public sealed record RecordedPass(string Name, int Index, PassStats Stats);

/// <summary>A view of a recorded execution, with its passes in order.</summary>
public sealed record RecordedView(string Name, int Index, uint PixelWidth, uint PixelHeight, EquatableArray<RecordedPass> Passes);

/// <summary>GPU results for one command buffer. View and pass indices are -1 for work outside a pass.</summary>
public sealed record RecordedCommandBuffer(
    ulong Id,
    string Name,
    int ViewIndex,
    int PassIndex,
    bool IsTransfer,
    double? Milliseconds,
    GpuVertexStats? VertexStats);

/// <summary>One graph execution with its views and GPU results.</summary>
public sealed record RecordedExecution(
    ulong ExecutionId,
    EquatableArray<RecordedView> Views,
    EquatableArray<RecordedCommandBuffer> CommandBuffers);
