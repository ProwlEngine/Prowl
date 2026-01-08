namespace Prowl.Wicked.Transport.Telepathy;

using Prowl.Wicked.Transport;

/// <summary>
/// Transport implementation using Telepathy TCP library.
/// </summary>
public class TelepathyTransport : ITransport
{
    private global::Telepathy.Server? _server;
    private global::Telepathy.Client? _client;

    /// <summary>
    /// Maximum message size in bytes (default: 16KB).
    /// </summary>
    public int MaxMessageSize { get; set; } = 16 * 1024;

    /// <summary>
    /// Server send queue limit before disconnecting slow clients.
    /// </summary>
    public int SendQueueLimit { get; set; } = 10000;

    /// <summary>
    /// Server receive queue limit.
    /// </summary>
    public int ReceiveQueueLimit { get; set; } = 10000;

    /// <summary>
    /// Disable Nagle's algorithm for lower latency.
    /// </summary>
    public bool NoDelay { get; set; } = true;

    /// <summary>
    /// Send timeout in milliseconds.
    /// </summary>
    public int SendTimeout { get; set; } = 5000;

    /// <summary>
    /// Receive timeout in milliseconds (0 = disabled).
    /// </summary>
    public int ReceiveTimeout { get; set; } = 0;

    public bool ServerActive => _server?.Active ?? false;
    public bool ClientConnected => _client?.Connected ?? false;

    // Events
    public event Action<int, string>? OnServerConnect;
    public event Action<int>? OnServerDisconnect;
    public event Action<int, ArraySegment<byte>>? OnServerData;
    public event Action? OnClientConnect;
    public event Action? OnClientDisconnect;
    public event Action<ArraySegment<byte>>? OnClientData;
    public event Action<int, string>? OnServerError;
    public event Action<string>? OnClientError;

    public void Initialize()
    {
        // Nothing to initialize upfront
    }

    public void Shutdown()
    {
        ServerStop();
        ClientDisconnect();
    }

    // Server methods

    public void ServerStart(int port)
    {
        if (_server != null)
        {
            global::Telepathy.Log.Warning("TelepathyTransport: Server already running");
            return;
        }

        _server = new global::Telepathy.Server(MaxMessageSize)
        {
            SendQueueLimit = SendQueueLimit,
            ReceiveQueueLimit = ReceiveQueueLimit,
            NoDelay = NoDelay,
            SendTimeout = SendTimeout,
            ReceiveTimeout = ReceiveTimeout
        };

        _server.OnConnected = (connectionId, address) =>
        {
            OnServerConnect?.Invoke(connectionId, address);
        };

        _server.OnData = (connectionId, data) =>
        {
            OnServerData?.Invoke(connectionId, data);
        };

        _server.OnDisconnected = (connectionId) =>
        {
            OnServerDisconnect?.Invoke(connectionId);
        };

        _server.Start(port);
    }

    public void ServerStop()
    {
        _server?.Stop();
        _server = null;
    }

    public bool ServerSend(int connectionId, ArraySegment<byte> data)
    {
        if (_server == null)
            return false;

        return _server.Send(connectionId, data);
    }

    public void ServerDisconnect(int connectionId)
    {
        _server?.Disconnect(connectionId);
    }

    public string GetClientAddress(int connectionId)
    {
        return _server?.GetClientAddress(connectionId) ?? string.Empty;
    }

    // Client methods

    public void ClientConnect(string address, int port)
    {
        if (_client != null)
        {
            global::Telepathy.Log.Warning("TelepathyTransport: Client already exists");
            return;
        }

        _client = new global::Telepathy.Client(MaxMessageSize)
        {
            SendQueueLimit = SendQueueLimit,
            ReceiveQueueLimit = ReceiveQueueLimit,
            NoDelay = NoDelay,
            SendTimeout = SendTimeout,
            ReceiveTimeout = ReceiveTimeout
        };

        _client.OnConnected = () =>
        {
            OnClientConnect?.Invoke();
        };

        _client.OnData = (data) =>
        {
            OnClientData?.Invoke(data);
        };

        _client.OnDisconnected = () =>
        {
            OnClientDisconnect?.Invoke();
        };

        _client.Connect(address, port);
    }

    public void ClientDisconnect()
    {
        _client?.Disconnect();
        _client = null;
    }

    public bool ClientSend(ArraySegment<byte> data)
    {
        if (_client == null)
            return false;

        return _client.Send(data);
    }

    // Tick

    public void Tick(int maxMessages = 100)
    {
        // Process server messages
        _server?.Tick(maxMessages);

        // Process client messages
        _client?.Tick(maxMessages);
    }
}
