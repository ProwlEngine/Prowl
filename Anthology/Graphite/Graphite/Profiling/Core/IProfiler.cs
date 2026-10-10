namespace Prowl.Graphite;

/// <summary>Root of every profiler capability. One instance observes one execution, from BeginExecution to EndExecution.</summary>
public interface IProfiler
{
    /// <summary>Called before any event of the execution. The graph name is empty for executions started without a graph.</summary>
    void BeginExecution(ulong executionId, string graphName);

    /// <summary>Called once every event and GPU result of the execution has been delivered.</summary>
    void EndExecution();
}

/// <summary>Render graph timeline: views and passes.</summary>
public interface IGraphProfiler : IProfiler
{
    void BeginView(in ViewInfo view);
    void EndView(in ViewInfo view);
    void BeginPass(in PassInfo pass);
    void EndPass(in PassInfo pass, in PassStats stats);
    void SkipPass(string name, int viewIndex, PassSkipReason reason);
}

/// <summary>Native GPU stats. Implementing this opts in to timestamp and pipeline statistic queries.</summary>
public interface IGpuStatsProfiler : IProfiler
{
    void RecordExecutionTime(in CommandBufferInfo commandBuffer, double milliseconds);
    void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats);
}
