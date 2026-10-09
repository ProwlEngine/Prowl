namespace Prowl.Graphite;

/// <summary>Root of every profiler capability. One instance observes one execution, from BeginExecution to EndExecution.</summary>
public interface IProfiler
{
    void BeginExecution(ulong executionId);

    /// <summary>Called once every event and GPU result of the execution has been delivered.</summary>
    void EndExecution();
}

/// <summary>Render graph events: views, passes, and pass resource reads and writes.</summary>
public interface IGraphProfiler : IProfiler
{
    void BeginView(in ViewInfo view);
    void EndView(in ViewInfo view);
    void BeginPass(in PassInfo pass);
    void EndPass(in PassInfo pass, in PassStats stats);
    void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer);
    void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer);
}

/// <summary>Native GPU stats. Implementing this opts in to timestamp and pipeline statistic queries.</summary>
public interface IGpuStatsProfiler : IProfiler
{
    void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds);
    void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats);
}
