namespace Prowl.Graphite;

/// <summary>Root of every profiler capability. Implement one or more capability interfaces to receive events.</summary>
public interface IProfiler
{
}

/// <summary>Command-level events: draws, dispatches, shader switches, pipeline binds, submits.</summary>
public interface ICommandProfiler : IProfiler
{
    void RecordSubmit(in CommandBufferInfo commandBuffer, bool isTransfer);
    void RecordShaderSwitch(in CommandBufferInfo commandBuffer, in ShaderSwitchInfo info);
    void RecordPipelineBind(in CommandBufferInfo commandBuffer, in PipelineBindInfo info);
    void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info);
    void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info);
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
    void RecordExecutionResolved(ulong executionId);
}
