using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>Forwards every event to several profilers.</summary>
public sealed class CompositeProfiler : IGraphProfiler, IGpuStatsProfiler
{
    private readonly IGraphProfiler[] _graph;
    private readonly IGpuStatsProfiler[] _gpuStats;
    private readonly IProfiler[] _leaves;

    /// <summary>Creates a composite over the sinks. Sinks are fixed at construction.</summary>
    /// <param name="sinks">Profilers to forward to.</param>
    public CompositeProfiler(params IProfiler[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        List<IGraphProfiler> graph = new();
        List<IGpuStatsProfiler> gpuStats = new();
        List<IProfiler> leaves = new();

        foreach (IProfiler sink in sinks)
        {
            if (sink == null)
                throw new ArgumentException("Sink list contains null.", nameof(sinks));

            if (sink is CompositeProfiler nested)
            {
                graph.AddRange(nested._graph);
                gpuStats.AddRange(nested._gpuStats);
                leaves.AddRange(nested._leaves);
                continue;
            }

            leaves.Add(sink);
            if (sink is IGraphProfiler g)
                graph.Add(g);
            if (sink is IGpuStatsProfiler s)
                gpuStats.Add(s);
        }

        _graph = graph.ToArray();
        _gpuStats = gpuStats.ToArray();
        _leaves = leaves.ToArray();
    }

    internal IProfiler[] Leaves => _leaves;

    public void BeginView(in ViewInfo view)
    {
        foreach (IGraphProfiler sink in _graph)
            sink.BeginView(in view);
    }

    public void EndView(in ViewInfo view)
    {
        foreach (IGraphProfiler sink in _graph)
            sink.EndView(in view);
    }

    public void BeginPass(in PassInfo pass)
    {
        foreach (IGraphProfiler sink in _graph)
            sink.BeginPass(in pass);
    }

    public void EndPass(in PassInfo pass, in PassStats stats)
    {
        foreach (IGraphProfiler sink in _graph)
            sink.EndPass(in pass, in stats);
    }

    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
    {
        foreach (IGraphProfiler sink in _graph)
            sink.RecordPassRead(in pass, resource, texture, buffer);
    }

    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
    {
        foreach (IGraphProfiler sink in _graph)
            sink.RecordPassWrite(in pass, resource, texture, buffer);
    }

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds)
    {
        foreach (IGpuStatsProfiler sink in _gpuStats)
            sink.RecordExecutionTime(in commandBuffer, isTransfer, milliseconds);
    }

    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats)
    {
        foreach (IGpuStatsProfiler sink in _gpuStats)
            sink.RecordGpuVertexStats(in commandBuffer, in stats);
    }

    public void RecordExecutionResolved(ulong executionId)
    {
        foreach (IGpuStatsProfiler sink in _gpuStats)
            sink.RecordExecutionResolved(executionId);
    }
}
