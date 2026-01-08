namespace Prowl.Wicked.Network;

/// <summary>
/// Represents the current network mode.
/// </summary>
public enum NetworkMode
{
    /// <summary>
    /// Not connected to any network.
    /// </summary>
    Offline,

    /// <summary>
    /// Running as a dedicated server only.
    /// </summary>
    Server,

    /// <summary>
    /// Running as a client connected to a remote server.
    /// </summary>
    Client,

    /// <summary>
    /// Running as both server and client (host mode).
    /// </summary>
    Host
}
