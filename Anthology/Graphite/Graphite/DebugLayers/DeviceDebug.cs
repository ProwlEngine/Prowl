using System;

namespace Prowl.Graphite.Debugging;

/// <summary>Debug settings of a device. Reached through <see cref="GraphicsDevice.Debug"/>.</summary>
public sealed class DeviceDebug
{
    private readonly GraphicsDevice _device;
    private CaptureSetup? _capture;

    internal DeviceDebug(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>Capture taps, null if none. Assigning throws while a <see cref="GraphicsDevice.DispatchGraph{T}"/> is executing.</summary>
    public CaptureSetup? Capture
    {
        get => _capture;
        set
        {
            _device.RequireNotDispatching("Debug.Capture");
            _capture = value;
            _device.ApplyCapture(value);
        }
    }
}
