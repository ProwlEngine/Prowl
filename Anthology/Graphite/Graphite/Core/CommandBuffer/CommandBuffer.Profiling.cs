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

    internal CommandBufferInfo ProfilerInfo => new(RentalId, Name, Pass);

    internal void RecordResourceSetBind(uint setCount) => Execution?.Device.Counters.RecordResourceSetBind(setCount);
}
