using System;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>Debug settings of this device.</summary>
    public DeviceDebug Debug => _debug ??= new DeviceDebug(this);

    private DeviceDebug? _debug;

    internal ICaptureHook? CaptureHook { get; private set; }

    internal ICommandStreamSink? CommandSink { get; private set; }

    internal IResourceWriteSink? WriteSink { get; private set; }

    internal ulong CaptureByteBudget { get; private set; }

    internal void RequireNotDispatching(string name)
    {
        if (_graphDispatchDepth != 0)
            throw new InvalidOperationException($"{name} cannot be set while a graph is dispatching.");
    }

    internal void ApplyCapture(CaptureSetup? capture)
    {
        CaptureHook = capture?.Hook;
        CommandSink = capture?.Commands;
        WriteSink = capture?.Writes;
        CaptureByteBudget = capture?.ByteBudget ?? 0;
    }
}
