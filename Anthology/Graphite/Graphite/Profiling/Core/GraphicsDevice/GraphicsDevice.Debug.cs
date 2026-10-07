using Prowl.Graphite.Debugging;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>Debug settings of this device.</summary>
    public DeviceDebug Debug => _debug ??= new DeviceDebug(this);

    private DeviceDebug? _debug;
}
