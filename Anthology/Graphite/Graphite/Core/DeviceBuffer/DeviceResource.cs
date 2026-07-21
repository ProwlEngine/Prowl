namespace Prowl.Graphite;

/// <summary>
/// A GraphicsDevice-owned resource with a debug name.
/// </summary>
public interface DeviceResource
{
    /// <summary>
    /// Debug name, shows up in graphics debuggers.
    /// </summary>
    string Name { get; set; }
}
