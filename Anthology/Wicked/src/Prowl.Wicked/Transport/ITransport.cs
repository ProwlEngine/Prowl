namespace Prowl.Wicked.Transport;

/// <summary>
/// Interface for network transport implementations.
/// Transports handle the low-level sending and receiving of bytes.
/// </summary>
public interface ITransport
{
    /// <summary>
    /// True if the server is currently active.
    /// </summary>
    bool ServerActive { get; }

    /// <summary>
    /// True if the client is currently connected.
    /// </summary>
    bool ClientConnected { get; }

    /// <summary>
    /// Maximum message size in bytes.
    /// </summary>
    int MaxMessageSize { get; }

    /// <summary>
    /// Initializes the transport.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Shuts down the transport and releases resources.
    /// </summary>
    void Shutdown();

    // Server methods

    /// <summary>
    /// Starts the server on the specified port.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    void ServerStart(int port);

    /// <summary>
    /// Stops the server.
    /// </summary>
    void ServerStop();

    /// <summary>
    /// Sends data to a specific client.
    /// </summary>
    /// <param name="connectionId">The client's connection ID.</param>
    /// <param name="data">The data to send.</param>
    /// <returns>True if the send was queued successfully.</returns>
    bool ServerSend(int connectionId, ArraySegment<byte> data);

    /// <summary>
    /// Disconnects a client from the server.
    /// </summary>
    /// <param name="connectionId">The client's connection ID.</param>
    void ServerDisconnect(int connectionId);

    /// <summary>
    /// Gets the address of a connected client.
    /// </summary>
    /// <param name="connectionId">The client's connection ID.</param>
    /// <returns>The client's IP address.</returns>
    string GetClientAddress(int connectionId);

    // Client methods

    /// <summary>
    /// Connects to a server.
    /// </summary>
    /// <param name="address">The server address.</param>
    /// <param name="port">The server port.</param>
    void ClientConnect(string address, int port);

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    void ClientDisconnect();

    /// <summary>
    /// Sends data to the server.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <returns>True if the send was queued successfully.</returns>
    bool ClientSend(ArraySegment<byte> data);

    // Events

    /// <summary>
    /// Raised when a client connects to the server.
    /// Parameters: connectionId, clientAddress
    /// </summary>
    event Action<int, string>? OnServerConnect;

    /// <summary>
    /// Raised when a client disconnects from the server.
    /// Parameters: connectionId
    /// </summary>
    event Action<int>? OnServerDisconnect;

    /// <summary>
    /// Raised when the server receives data from a client.
    /// Parameters: connectionId, data
    /// </summary>
    event Action<int, ArraySegment<byte>>? OnServerData;

    /// <summary>
    /// Raised when the client connects to the server.
    /// </summary>
    event Action? OnClientConnect;

    /// <summary>
    /// Raised when the client disconnects from the server.
    /// </summary>
    event Action? OnClientDisconnect;

    /// <summary>
    /// Raised when the client receives data from the server.
    /// Parameters: data
    /// </summary>
    event Action<ArraySegment<byte>>? OnClientData;

    /// <summary>
    /// Raised when a server error occurs.
    /// Parameters: connectionId, errorMessage
    /// </summary>
    event Action<int, string>? OnServerError;

    /// <summary>
    /// Raised when a client error occurs.
    /// Parameters: errorMessage
    /// </summary>
    event Action<string>? OnClientError;

    /// <summary>
    /// Processes incoming messages. Call this every frame.
    /// </summary>
    /// <param name="maxMessages">Maximum number of messages to process per call.</param>
    void Tick(int maxMessages = 100);
}
