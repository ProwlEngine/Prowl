using System.Collections.Generic;

namespace Prowl.Graphite;

public interface IProfiler
{
    void Allocate(AllocBin type, long bytes);
    void Free(AllocBin type, long bytes);

    void AllocateMemory(BufferRoleBin role, long bytes);
    void FreeMemory(BufferRoleBin role, long bytes);

    void Record(BufferOpBin op, long bytes);

    void RecordSwap(SwapBin evt, long bytes);

    void BeginPass(in PassInfo pass);
    void EndPass(in PassInfo pass);

    void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer);

    void BeginSample(string name);
    void EndSample();

    bool RequestCapture { get; }
    void Capture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer);

    void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info);
    void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info);

    /// <summary>
    /// Reports the vertex/index/bound buffers used by the draw call that was just recorded. Only
    /// called when RequestCapture is true, since resolving the current bindings is not free.
    /// </summary>
    void RecordDrawBuffers(in CommandBufferInfo commandBuffer, in DrawBufferInfo info);

    void RecordPipelineSwitch(in CommandBufferInfo commandBuffer, in PipelineBindInfo info);

    void RecordResourceSetBind(uint setCount);

    void RecordBarrier(BarrierBin kind, uint count);

    void RecordSubmit(in ProfilerSubmitInfo info);

    bool RequestExecutionTiming { get; }
    void RecordExecutionTime(PassInfo? pass, ulong commandBufferId, string bufferName, bool isTransfer, double milliseconds);
}
