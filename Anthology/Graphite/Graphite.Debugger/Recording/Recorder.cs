using System;

namespace Prowl.Graphite.Debugger;

/// <summary>Records graph executions from a live device. At most one recording is open at a time.</summary>
public sealed class Recorder
{
    private readonly GraphicsDevice _device;
    private readonly object _gate = new();
    private RecordingSink? _open;
    private RecordingHandle? _last;

    /// <summary>Creates a recorder for a device that may already be in use.</summary>
    /// <param name="device">Device to record.</param>
    public Recorder(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    /// <summary>Starts a light recording. Covers the executions that start from now until <see cref="EndRecord"/>.</summary>
    public void BeginRecord()
    {
        lock (_gate)
        {
            if (_open != null)
                throw new InvalidOperationException("A recording is already open.");

            _open = new RecordingSink();
            _device.Debug.Attach(_open);
        }
    }

    /// <summary>Ends the open light recording and returns its handle.</summary>
    public Recording EndRecord()
    {
        lock (_gate)
        {
            if (_open is not { } sink)
                throw new InvalidOperationException("No recording is open.");

            _device.Debug.Detach(sink);
            sink.Close();
            _open = null;
            Recording recording = new(_device, sink, _last);
            _last = recording;
            return recording;
        }
    }
}
