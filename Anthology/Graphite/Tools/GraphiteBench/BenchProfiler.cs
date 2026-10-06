using Prowl.Graphite;

namespace Prowl.Graphite.Bench;

public sealed class BenchProfiler : ICommandProfiler
{
    public long Draws;
    public long ShaderSwitches;
    public long Submits;

    public void Reset()
    {
        Draws = 0;
        ShaderSwitches = 0;
        Submits = 0;
    }

    public void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info) => Draws++;

    public void RecordShaderSwitch(in CommandBufferInfo commandBuffer, in ShaderSwitchInfo info) => ShaderSwitches++;
    public void RecordPipelineBind(in CommandBufferInfo commandBuffer, in PipelineBindInfo info) { }

    public void RecordSubmit(in CommandBufferInfo commandBuffer, bool isTransfer) => Submits++;

    public void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info) { }
}
