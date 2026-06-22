using System.Diagnostics;

namespace Prowl.Graphite;

public abstract partial class DeviceBuffer
{
#if VALIDATE_USAGE
    private GraphicsDevice? _inFlightDevice;
    private ulong _inFlightFrameId;
    private bool _transientWrites;
#endif

    [Conditional("VALIDATE_USAGE")]
    internal void CreateBuffer_SetTransientWrites(bool transientWrites)
    {
#if VALIDATE_USAGE
        _transientWrites = transientWrites;
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    internal void SubmitCommands_MarkInFlight(GraphicsDevice device, ulong frameId)
    {
#if VALIDATE_USAGE
        if (_transientWrites)
            return;

        _inFlightDevice = device;
        _inFlightFrameId = frameId;
#endif
    }

#if VALIDATE_USAGE
    private bool IsStillInFlight()
    {
        if (_inFlightDevice == null || _inFlightFrameId == 0)
            return false;

        return !_inFlightDevice.IsFrameComplete(_inFlightFrameId);
    }

    internal void CheckNotInFlightForWrite(string operation)
    {
        if (IsStillInFlight())
        {
            throw new RenderException(
                $"{operation} was called on DeviceBuffer '{Name}' while it is still in flight on frame {_inFlightFrameId}. " +
                "The GPU may still be reading this buffer, so overwriting it now is a data race. " +
                "Use a StreamingBuffer for per-frame uploads, or wait for the frame to complete before writing.");
        }
    }
#endif
}
