using Prowl.Graphite;

namespace Prowl.Graphite.Bench;

public sealed class BenchProfiler : IGraphProfiler
{
    public long Draws;
    public long ShaderSwitches;

    public void Reset()
    {
        Draws = 0;
        ShaderSwitches = 0;
    }

    public void BeginExecution(ulong executionId, string graphName) { }
    public void EndExecution() { }
    public void BeginView(in ViewInfo view) { }
    public void EndView(in ViewInfo view) { }
    public void BeginPass(in PassInfo pass) { }

    public void EndPass(in PassInfo pass, in PassStats stats)
    {
        Draws += stats.Draws + stats.IndirectDraws;
        ShaderSwitches += stats.ShaderSwitches;
    }

    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }
    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }
}
