using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Graphite.Bench;

// Counts only the events the binding work is judged on. Everything else is a no-op so the
// profiler adds a predictable, near-zero cost to the recording path being measured.
public sealed class BenchProfiler : IProfiler
{
    public long Draws;
    public long SetBinds;
    public long BoundSets;
    public long PipelineSwitches;
    public long Submits;

    public void Reset()
    {
        Draws = 0;
        SetBinds = 0;
        BoundSets = 0;
        PipelineSwitches = 0;
        Submits = 0;
    }

    public void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info) => Draws++;

    public void RecordResourceSetBind(uint setCount)
    {
        SetBinds++;
        BoundSets += setCount;
    }

    public void RecordPipelineSwitch(in CommandBufferInfo commandBuffer, in PipelineBindInfo info) => PipelineSwitches++;

    public void RecordSubmit(in CommandBufferInfo commandBuffer, bool isTransfer) => Submits++;

    public void Allocate(AllocBin type, long bytes) { }
    public void Free(AllocBin type, long bytes) { }
    public void AllocateMemory(BufferRoleBin role, long bytes) { }
    public void FreeMemory(BufferRoleBin role, long bytes) { }
    public void Record(BufferOpBin op, long bytes) { }
    public void RecordSwap(SwapBin evt, long bytes) { }
    public void RecordBarrier(BarrierBin kind, uint count) { }

    public void BeginView(in ViewInfo view) { }
    public void EndView(in ViewInfo view) { }
    public void BeginPass(in PassInfo pass) { }
    public void EndPass(in PassInfo pass) { }
    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }

    public void RecordDrawBuffers(in CommandBufferInfo commandBuffer, in DrawBufferInfo info) { }
    public void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info) { }

    public bool RequestMetadata => false;
    public void RecordPassMetadata(in PassInfo pass, object metadata) { }
    public void RecordDrawMetadata(in CommandBufferInfo commandBuffer, object metadata) { }

    public bool RequestGPUStatistics => false;
    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds) { }
    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }

    public bool RequestCapture => false;
    public void Capture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer) { }
}
