using Prowl.Graphite;

namespace Prowl.Graphite.Bench;

public sealed class BenchProfiler : ICommandProfiler
{
    public long Draws;
    public long PipelineSwitches;
    public long Submits;

    public void Reset()
    {
        Draws = 0;
        PipelineSwitches = 0;
        Submits = 0;
    }

    public void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info) => Draws++;

    public void RecordPipelineSwitch(in CommandBufferInfo commandBuffer, in PipelineBindInfo info) => PipelineSwitches++;

    public void RecordSubmit(in CommandBufferInfo commandBuffer, bool isTransfer) => Submits++;

    public void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info) { }
}
