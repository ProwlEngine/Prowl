using System;
using System.Linq;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger;

/// <summary>A pass of a recorded view, with the work its command buffer recorded.</summary>
public sealed record RecordedPass(string Name, int Index, PassStats Stats);

/// <summary>A view of the recorded execution, with its passes in order.</summary>
public sealed record RecordedView(string Name, int Index, uint PixelWidth, uint PixelHeight, EquatableArray<RecordedPass> Passes);

/// <summary>GPU results for one command buffer. View and pass indices are -1 for work outside a pass.</summary>
public sealed record RecordedCommandBuffer(
    ulong Id,
    string Name,
    int ViewIndex,
    int PassIndex,
    double? Milliseconds,
    GpuVertexStats? VertexStats);

/// <summary>The optional features of the recording device.</summary>
public sealed record RecordedFeatures(
    bool GeometryShader,
    bool TessellationShaders,
    bool DrawIndirectBaseInstance,
    bool SamplerAnisotropy,
    bool DepthClipDisable,
    bool IndependentBlend,
    bool CommandBufferDebugMarkers,
    bool ShaderFloat64)
{
    internal static RecordedFeatures From(GraphicsDeviceFeatures f)
        => new(f.GeometryShader, f.TessellationShaders, f.DrawIndirectBaseInstance, f.SamplerAnisotropy, f.DepthClipDisable, f.IndependentBlend, f.CommandBufferDebugMarkers, f.ShaderFloat64);
}

/// <summary>Device counter deltas over the CPU side of an execution and the memory budget at its end.</summary>
/// <remarks>Arrays are indexed by AllocBin, BufferRoleBin, BarrierBin, SwapBin and BufferOpBin. Counters are device wide.</remarks>
public sealed record RecordedCounters(
    EquatableArray<long> Live,
    EquatableArray<long> LiveBytes,
    EquatableArray<long> ResidentBytes,
    EquatableArray<long> Barriers,
    EquatableArray<long> Swaps,
    EquatableArray<long> BufferOps,
    EquatableArray<long> BufferOpBytes,
    long ResourceSetBinds,
    long ResourceSetsBound,
    MemoryBudgetInfo MemoryBudget)
{
    internal static RecordedCounters Delta(GraphicsCountersSnapshot start, GraphicsCountersSnapshot end, MemoryBudgetInfo budget)
        => new(
            Per<AllocBin>(bin => end.Live(bin) - start.Live(bin)),
            Per<AllocBin>(bin => end.LiveBytes(bin) - start.LiveBytes(bin)),
            Per<BufferRoleBin>(role => end.ResidentBytes(role) - start.ResidentBytes(role)),
            Per<BarrierBin>(kind => end.Barriers(kind) - start.Barriers(kind)),
            Per<SwapBin>(evt => end.Swaps(evt) - start.Swaps(evt)),
            Per<BufferOpBin>(op => end.BufferOps(op) - start.BufferOps(op)),
            Per<BufferOpBin>(op => end.BufferOpBytes(op) - start.BufferOpBytes(op)),
            end.ResourceSetBinds - start.ResourceSetBinds,
            end.ResourceSetsBound - start.ResourceSetsBound,
            budget);

    private static EquatableArray<long> Per<TBin>(Func<TBin, long> value) where TBin : struct, Enum
        => Enum.GetValues<TBin>().Select(value).ToEquatableArray();
}
