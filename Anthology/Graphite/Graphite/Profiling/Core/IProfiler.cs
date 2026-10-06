namespace Prowl.Graphite;

/// <summary>Command-level events: draws, dispatches, pipeline switches, submits.</summary>
public interface ICommandProfiler
{
    void RecordSubmit(in CommandBufferInfo commandBuffer, bool isTransfer);
    void RecordPipelineSwitch(in CommandBufferInfo commandBuffer, in PipelineBindInfo info);
    void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info);
    void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info);
}

/// <summary>Render graph events: views, passes, and pass resource reads and writes.</summary>
public interface IGraphProfiler
{
    void BeginView(in ViewInfo view);
    void EndView(in ViewInfo view);
    void BeginPass(in PassInfo pass);
    void EndPass(in PassInfo pass);
    void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer);
    void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer);
}

/// <summary>Native GPU stats. Implementing this opts in to timestamp and pipeline statistic queries.</summary>
public interface IGpuStatsProfiler
{
    void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds);
    void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats);
}

/// <summary>Aggregate of every profiler capability.</summary>
public interface IProfiler : ICommandProfiler, IGraphProfiler, IGpuStatsProfiler
{
}
