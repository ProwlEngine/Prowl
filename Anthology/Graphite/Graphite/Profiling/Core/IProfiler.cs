using System.Collections.Generic;

namespace Prowl.Graphite;

public interface IProfiler
{
    // hardware-level counters
    void Allocate(AllocBin type, long bytes);
    void Free(AllocBin type, long bytes);

    void AllocateMemory(BufferRoleBin role, long bytes);
    void FreeMemory(BufferRoleBin role, long bytes);

    void Record(BufferOpBin op, long bytes);
    void RecordSwap(SwapBin evt, long bytes);

    void RecordResourceSetBind(uint setCount);
    void RecordBarrier(BarrierBin kind, uint count);


    // pipelines/executing views
    void BeginView(in ViewInfo view);
    void EndView(in ViewInfo view);

    // passes for a pipeline
    void BeginPass(in PassInfo pass);
    void EndPass(in PassInfo pass);
    void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer);

    // command buffer submit
    void RecordSubmit(in CommandBufferInfo commandBuffer, bool isTransfer);

    // pipeline switches
    void RecordPipelineSwitch(in CommandBufferInfo commandBuffer, in PipelineBindInfo info);

    // draws
    void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info);
    void RecordDrawBuffers(in CommandBufferInfo commandBuffer, in DrawBufferInfo info);

    // dispatches
    void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info);

    // caller metadata hooks
    bool RequestMetadata { get; }
    void RecordPassMetadata(in PassInfo pass, object metadata);
    void RecordDrawMetadata(in CommandBufferInfo commandBuffer, object metadata);

    // native GPU stats for GPU execution timing and true primitives/triangles drawn
    bool RequestGPUStatistics { get; }
    void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds);
    void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats);

    // hookpoint in between pass execution to copy a pass's outputs in-flight before full submit
    bool RequestCapture { get; }
    void Capture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer);
}
