using Prowl.Graphite;

namespace Prowl.Graphite.Bench;

public sealed record BenchSnapshot(
    double GpuUsPerFrame,
    double RecordUsPerFrame,
    double PassesPerFrame,
    double DrawsPerFrame,
    double DispatchesPerFrame,
    double ShaderSwitchesPerFrame,
    double PipelineBindsPerFrame,
    double SetBindsPerFrame,
    double BarriersPerFrame);

public sealed class BenchProfiler : IGraphProfiler, IGpuStatsProfiler
{
    private long _frames;
    private long _gpuFrames;
    private double _gpuMs;
    private double _recordMs;
    private long _passes;
    private long _draws;
    private long _dispatches;
    private long _shaderSwitches;
    private long _pipelineBinds;
    private long _setBinds;
    private long _barriers;

    public void Reset()
    {
        _frames = 0;
        _gpuFrames = 0;
        _gpuMs = 0;
        _recordMs = 0;
        _passes = 0;
        _draws = 0;
        _dispatches = 0;
        _shaderSwitches = 0;
        _pipelineBinds = 0;
        _setBinds = 0;
        _barriers = 0;
    }

    public BenchSnapshot Snapshot()
    {
        double frames = _frames == 0 ? 1 : _frames;
        double gpuFrames = _gpuFrames == 0 ? 1 : _gpuFrames;
        return new BenchSnapshot(
            _gpuMs * 1000.0 / gpuFrames,
            _recordMs * 1000.0 / frames,
            _passes / frames,
            _draws / frames,
            _dispatches / frames,
            _shaderSwitches / frames,
            _pipelineBinds / frames,
            _setBinds / frames,
            _barriers / frames);
    }

    public void BeginExecution(ulong executionId, string graphName) => _frames++;
    public void EndExecution() => _gpuFrames++;
    public void BeginInstance(in InstanceInfo instance) { }
    public void EndInstance(in InstanceInfo instance) { }
    public void BeginPass(in PassInfo pass) { }
    public void SkipPass(string name, int instanceIndex, PassSkipReason reason) { }

    public void EndPass(in PassInfo pass, in PassStats stats)
    {
        _passes++;
        _recordMs += stats.CpuMilliseconds;
        _draws += stats.Draws + stats.IndirectDraws;
        _dispatches += stats.Dispatches;
        _shaderSwitches += stats.ShaderSwitches;
        _pipelineBinds += stats.PipelineBinds;
        _setBinds += stats.ResourceSetBinds;
        _barriers += stats.Barriers;
    }

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, double milliseconds) => _gpuMs += milliseconds;
    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }
}
