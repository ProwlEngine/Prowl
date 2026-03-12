using Prowl.Wicked;

namespace Prowl.Wicked.Tests;

/// <summary>
/// In-memory server transport for testing. Paired with InMemoryClientTransport.
/// Messages are queued and delivered during Tick().
/// </summary>
public class InMemoryServerTransport : IServerTransport
{
    private readonly Dictionary<uint, InMemoryClientTransport> _clients = new();
    private readonly Queue<(uint connectionId, ArraySegment<byte> data)> _incomingData = new();
    private readonly Queue<uint> _pendingConnections = new();
    private readonly Queue<(uint connectionId, string? reason)> _pendingDisconnections = new();
    private uint _nextConnectionId = 1;

    public void Listen(int port) { }

    public void Stop()
    {
        // Directly notify clients instead of queuing — there are no more Tick() calls
        // after Stop(), so queued work would never be processed.
        foreach (var (id, client) in _clients)
        {
            client.EnqueueDisconnected("Server stopped");
            OnClientDisconnected?.Invoke(id, "Server stopped");
        }
        _clients.Clear();
        _pendingConnections.Clear();
        _pendingDisconnections.Clear();
        _incomingData.Clear();
    }

    public void Send(uint connectionId, ArraySegment<byte> data)
    {
        if (_clients.TryGetValue(connectionId, out var client))
        {
            // Copy data — the original buffer may be reused
            var copy = new byte[data.Count];
            Array.Copy(data.Array!, data.Offset, copy, 0, data.Count);
            client.EnqueueIncoming(new ArraySegment<byte>(copy));
        }
    }

    public void Disconnect(uint connectionId, string? reason = null)
    {
        _pendingDisconnections.Enqueue((connectionId, reason));
    }

    public void Tick()
    {
        // Order: connect → data → disconnect
        // This matches real TCP where buffered data arrives before the connection-close signal.

        // Process pending connections
        while (_pendingConnections.Count > 0)
        {
            var id = _pendingConnections.Dequeue();
            OnClientConnected?.Invoke(id);
        }

        // Deliver incoming data (before disconnections, so data sent before disconnect arrives first)
        while (_incomingData.Count > 0)
        {
            var (id, data) = _incomingData.Dequeue();
            OnDataReceived?.Invoke(id, data);
        }

        // Process pending disconnections
        while (_pendingDisconnections.Count > 0)
        {
            var (id, reason) = _pendingDisconnections.Dequeue();
            if (_clients.Remove(id, out var client))
            {
                client.EnqueueDisconnected(reason);
            }
            OnClientDisconnected?.Invoke(id, reason);
        }
    }

    /// <summary>
    /// Called by InMemoryClientTransport to simulate a client connection.
    /// </summary>
    internal uint AcceptClient(InMemoryClientTransport client)
    {
        var id = _nextConnectionId++;
        _clients[id] = client;
        _pendingConnections.Enqueue(id);
        return id;
    }

    /// <summary>
    /// Called by InMemoryClientTransport to send data to the server.
    /// </summary>
    internal void EnqueueFromClient(uint connectionId, ArraySegment<byte> data)
    {
        // Copy data — the original buffer may be reused
        var copy = new byte[data.Count];
        Array.Copy(data.Array!, data.Offset, copy, 0, data.Count);
        _incomingData.Enqueue((connectionId, new ArraySegment<byte>(copy)));
    }

    /// <summary>
    /// Called by InMemoryClientTransport to simulate a client disconnect.
    /// </summary>
    internal void ClientDisconnected(uint connectionId)
    {
        _pendingDisconnections.Enqueue((connectionId, null));
    }

    public event Action<uint>? OnClientConnected;
    public event Action<uint, string?>? OnClientDisconnected;
    public event Action<uint, ArraySegment<byte>>? OnDataReceived;
}

/// <summary>
/// In-memory client transport for testing. Takes an InMemoryServerTransport
/// in its constructor to create a direct link. Messages are queued and delivered during Tick().
/// </summary>
public class InMemoryClientTransport : IClientTransport
{
    private readonly InMemoryServerTransport _server;
    private uint _connectionId;
    private bool _connected;
    private bool _connectPending;
    private readonly Queue<ArraySegment<byte>> _incomingData = new();
    private bool _disconnectedPending;
    private string? _disconnectReason;

    public InMemoryClientTransport(InMemoryServerTransport server)
    {
        _server = server;
    }

    public void Connect(string host, int port)
    {
        // Clear stale state from a previous session to prevent a pending
        // disconnect from the old connection killing the new one.
        _disconnectedPending = false;
        _disconnectReason = null;
        _incomingData.Clear();
        _connectionId = _server.AcceptClient(this);
        _connectPending = true;
    }

    public void Disconnect()
    {
        if (_connected)
        {
            _connected = false;
            // Don't set _disconnectedPending here — let the server round-trip handle it
            // via EnqueueDisconnected, consistent with real TCP behavior where the
            // disconnect acknowledgment comes back from the server.
            _server.ClientDisconnected(_connectionId);
        }
        else if (_connectPending)
        {
            _connectPending = false;
            _server.ClientDisconnected(_connectionId);
            _disconnectedPending = true;
            _disconnectReason = null;
        }
    }

    public void Send(ArraySegment<byte> data)
    {
        if (_connected)
        {
            _server.EnqueueFromClient(_connectionId, data);
        }
    }

    public void Tick()
    {
        // Order: connect → data → disconnect
        // This matches real TCP where buffered data arrives before the connection-close signal.

        // Process pending connection
        if (_connectPending)
        {
            _connectPending = false;
            _connected = true;
            OnConnected?.Invoke();
        }

        // Deliver incoming data (before disconnection, so data sent before disconnect arrives first)
        while (_incomingData.Count > 0)
        {
            var data = _incomingData.Dequeue();
            OnDataReceived?.Invoke(data);
        }

        // Process pending disconnection
        if (_disconnectedPending)
        {
            _disconnectedPending = false;
            OnDisconnected?.Invoke(_disconnectReason);
        }
    }

    /// <summary>
    /// Called by InMemoryServerTransport to deliver data to this client.
    /// </summary>
    internal void EnqueueIncoming(ArraySegment<byte> data)
    {
        _incomingData.Enqueue(data);
    }

    /// <summary>
    /// Called by InMemoryServerTransport to notify this client of disconnection.
    /// </summary>
    internal void EnqueueDisconnected(string? reason)
    {
        _disconnectedPending = true;
        _disconnectReason = reason;
        _connected = false;
    }

    public event Action? OnConnected;
    public event Action<string?>? OnDisconnected;
    public event Action<ArraySegment<byte>>? OnDataReceived;
}
