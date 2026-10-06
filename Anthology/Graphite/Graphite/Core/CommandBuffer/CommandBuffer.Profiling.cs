namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
    /// <summary>Execution this buffer was rented for. Null if not tied to one.</summary>
    internal ExecutionTask? Execution { get; set; }

    /// <summary>Pass this buffer was rented during, for profiler timing. Null outside a pass.</summary>
    internal PassInfo? Pass { get; set; }

    /// <summary>Bound execution's id, or 0.</summary>
    internal ulong ExecutionId => Execution?.Id ?? 0;

    /// <summary>Fresh id stamped per rental, so profiler can tell reused instances apart.</summary>
    internal ulong RentalId { get; set; }

    internal CommandBufferInfo ProfilerInfo => new(RentalId, Name, ExecutionId, Pass);

    private uint _statDraws;
    private uint _statIndirectDraws;
    private uint _statDispatches;
    private uint _statShaderSwitches;
    private uint _statPipelineBinds;
    private uint _statResourceSetBinds;
    private uint _statBarriers;

    internal PassStats Stats => new(
        _statDraws, _statIndirectDraws, _statDispatches, _statShaderSwitches, _statPipelineBinds, _statResourceSetBinds, _statBarriers);

    internal void ResetStats()
    {
        _statDraws = 0;
        _statIndirectDraws = 0;
        _statDispatches = 0;
        _statShaderSwitches = 0;
        _statPipelineBinds = 0;
        _statResourceSetBinds = 0;
        _statBarriers = 0;
    }

    internal void AddBarrierStats(uint count) => _statBarriers += count;

    internal void ReportPipelineBind(ShaderProgram program, ulong pipelineId, bool isCompute, OutputDescription? outputs, PrimitiveTopology? topology)
    {
        _statPipelineBinds++;
        if (Device.CommandProfiler is { } profiler)
            profiler.RecordPipelineBind(ProfilerInfo, new PipelineBindInfo(program, pipelineId, isCompute, outputs, topology));
    }

    internal void RecordResourceSetBind(uint setCount)
    {
        _statResourceSetBinds++;
        Device.Counters.RecordResourceSetBind(setCount);
    }
}
