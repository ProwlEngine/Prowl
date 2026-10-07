namespace Prowl.Graphite.Debugging;

/// <summary>Debug settings of a device. Reached through <see cref="GraphicsDevice.Debug"/>.</summary>
public sealed class DeviceDebug
{
    private readonly GraphicsDevice _device;

    internal DeviceDebug(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>Attaches a profiler. Never throws. Applies from the next graph execution.</summary>
    /// <param name="profiler">Profiler to attach.</param>
    public void Attach(IProfiler profiler) => _device.AttachProfiler(profiler);

    /// <summary>Detaches a profiler. Never throws. Applies from the next graph execution.</summary>
    /// <param name="profiler">Profiler to detach.</param>
    public void Detach(IProfiler profiler) => _device.DetachProfiler(profiler);
}
