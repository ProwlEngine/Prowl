namespace Prowl.Wicked.Network;

/// <summary>
/// Represents the connection state of a network client.
/// </summary>
public enum ConnectState
{
    /// <summary>
    /// Not connected and not attempting to connect.
    /// </summary>
    None,

    /// <summary>
    /// Currently attempting to connect (between Connect() and OnTransportConnected()).
    /// </summary>
    Connecting,

    /// <summary>
    /// Successfully connected to the server.
    /// </summary>
    Connected,

    /// <summary>
    /// Currently disconnecting (between Disconnect() and OnTransportDisconnected()).
    /// </summary>
    Disconnecting,

    /// <summary>
    /// Disconnected from the server.
    /// </summary>
    Disconnected
}
