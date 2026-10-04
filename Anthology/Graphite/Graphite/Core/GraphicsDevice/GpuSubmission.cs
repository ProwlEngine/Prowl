namespace Prowl.Graphite;

/// <summary>
/// Handle to work recorded through GraphicsDevice.Record. Wait on it or drop it, the work runs either way.
/// </summary>
public abstract class GpuSubmission
{
    /// <summary>True once the GPU finished the work.</summary>
    public abstract bool IsComplete { get; }

    /// <summary>Blocks until the GPU finishes the work, or until timeout.</summary>
    /// <param name="nanosecondTimeout">Max wait in ns. ulong.MaxValue = no timeout.</param>
    /// <returns>True if it finished before timeout.</returns>
    public abstract bool Wait(ulong nanosecondTimeout = ulong.MaxValue);
}
