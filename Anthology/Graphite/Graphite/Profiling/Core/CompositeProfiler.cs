using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>Forwards every event to several profilers. A device only enables the categories at least one sink implements.</summary>
public sealed class CompositeProfiler : ICommandProfiler, IGraphProfiler, IGpuStatsProfiler
{
    private readonly ICommandProfiler[] _command;
    private readonly IGraphProfiler[] _graph;
    private readonly IGpuStatsProfiler[] _gpuStats;

    /// <summary>Creates a composite over the sinks. Sinks are fixed at construction.</summary>
    /// <param name="sinks">Profilers to forward to.</param>
    public CompositeProfiler(params IProfiler[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        List<ICommandProfiler> command = new();
        List<IGraphProfiler> graph = new();
        List<IGpuStatsProfiler> gpuStats = new();

        foreach (IProfiler sink in sinks)
        {
            if (sink == null)
                throw new ArgumentException("Sink list contains null.", nameof(sinks));

            if (sink is CompositeProfiler nested)
            {
                command.AddRange(nested._command);
                graph.AddRange(nested._graph);
                gpuStats.AddRange(nested._gpuStats);
                continue;
            }

            if (sink is ICommandProfiler c)
                command.Add(c);
            if (sink is IGraphProfiler g)
                graph.Add(g);
            if (sink is IGpuStatsProfiler s)
                gpuStats.Add(s);
        }

        _command = command.ToArray();
        _graph = graph.ToArray();
        _gpuStats = gpuStats.ToArray();
    }

    internal bool HasCommand => _command.Length != 0;

    internal bool HasGraph => _graph.Length != 0;

    internal bool HasGpuStats => _gpuStats.Length != 0;

    public void RecordSubmit(in CommandBufferInfo commandBuffer, bool isTransfer)
    {
        foreach (ICommandProfiler sink in _command)
            sink.RecordSubmit(in commandBuffer, isTransfer);
    }

    public void RecordShaderSwitch(in CommandBufferInfo commandBuffer, in ShaderSwitchInfo info)
    {
        foreach (ICommandProfiler sink in _command)
            sink.RecordShaderSwitch(in commandBuffer, in info);
    }

    public void RecordPipelineBind(in CommandBufferInfo commandBuffer, in PipelineBindInfo info)
    {
        foreach (ICommandProfiler sink in _command)
            sink.RecordPipelineBind(in commandBuffer, in info);
    }

    public void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info)
    {
        foreach (ICommandProfiler sink in _command)
            sink.RecordDraw(in commandBuffer, in info);
    }

    public void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info)
    {
        foreach (ICommandProfiler sink in _command)
            sink.RecordDispatch(in commandBuffer, in info);
    }

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
