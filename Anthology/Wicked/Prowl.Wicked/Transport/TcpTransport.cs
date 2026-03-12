using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Prowl.Wicked.Transport;

/// <summary>
/// TCP server transport. Manages multiple client connections with length-prefixed framing.
/// Non-blocking I/O driven by Tick(). Disables Nagle's algorithm for low-latency messaging.
/// </summary>
public class TcpServerTransport : IServerTransport
{
    private TcpListener? _listener;
    private readonly Dictionary<uint, TcpConnection> _connections = new();
    private readonly Queue<(uint connectionId, string? reason)> _pendingDisconnections = new();
    private uint _nextConnectionId = 1;
    private readonly int _maxMessageSize;

    /// <summary>
    /// Creates a new TCP server transport.
    /// </summary>
    /// <param name="maxMessageSize">
    /// Maximum allowed payload size in bytes per message. Messages exceeding this limit
    /// cause the offending client to be disconnected. Default: 10 MB.
    /// </param>
    public TcpServerTransport(int maxMessageSize = 10_485_760)
    {
        _maxMessageSize = maxMessageSize;
    }

    public void Listen(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
    }

    public void Stop()
    {
        _listener?.Stop();
        _listener = null;

        // Fire OnClientDisconnected for each connection before clearing
        foreach (var (id, conn) in _connections)
        {
            conn.TcpClient.Close();
            OnClientDisconnected?.Invoke(id, "server stopped");
        }
        _connections.Clear();
        _pendingDisconnections.Clear();
    }

    public void Send(uint connectionId, ArraySegment<byte> data)
    {
        if (!_connections.TryGetValue(connectionId, out var conn))
            return;

        try
        {
            Span<byte> header = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, data.Count);
            conn.Stream.Write(header);
            conn.Stream.Write(data);
        }
        catch
        {
            // Write failed — connection is broken. Close immediately;
            // Tick() will skip the closed socket and fire OnClientDisconnected.
            conn.TcpClient.Close();
        }
    }

    public void Disconnect(uint connectionId, string? reason = null)
    {
        if (_connections.Remove(connectionId, out var conn))
        {
            conn.TcpClient.Close();
            _pendingDisconnections.Enqueue((connectionId, reason));
        }
    }

    public void Tick()
    {
        // Order: connect → data → disconnect (matches real TCP behavior)

        // 1. Accept new connections
        if (_listener != null)
        {
            while (_listener.Pending())
            {
                var tcpClient = _listener.AcceptTcpClient();
                tcpClient.NoDelay = true;
                var id = _nextConnectionId++;
                _connections[id] = new TcpConnection(tcpClient);
                OnClientConnected?.Invoke(id);
            }
        }

        // 2. Read data from each connection (snapshot — callbacks may disconnect clients)
        foreach (var (id, conn) in _connections.ToArray())
        {
            if (!_connections.ContainsKey(id)) continue;

            try
            {
                if (!ReadMessages(id, conn))
                {
                    // Clean close (read returned 0)
                    if (_connections.Remove(id))
                    {
                        conn.TcpClient.Close();
                        _pendingDisconnections.Enqueue((id, null));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                if (_connections.Remove(id))
                {
                    conn.TcpClient.Close();
                    _pendingDisconnections.Enqueue((id, ex.Message));
                }
            }
        }

        // 3. Fire queued disconnect events
        while (_pendingDisconnections.Count > 0)
        {
            var (id, reason) = _pendingDisconnections.Dequeue();
            OnClientDisconnected?.Invoke(id, reason);
        }
    }

    /// <summary>
    /// Reads available data and processes complete length-prefixed messages.
    /// Returns false if the remote end closed the connection cleanly.
    /// </summary>
    private bool ReadMessages(uint id, TcpConnection conn)
    {
        int available = conn.TcpClient.Available;
        if (available == 0) return true;

        conn.EnsureCapacity(conn.BufferCount + available);

        int read = conn.Stream.Read(conn.Buffer, conn.BufferCount, available);
        if (read == 0) return false;

        conn.BufferCount += read;

        // Parse complete length-prefixed messages
        int offset = 0;
        while (offset + 4 <= conn.BufferCount)
        {
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(
                conn.Buffer.AsSpan(offset, 4));

            if (payloadLength < 0 || payloadLength > _maxMessageSize)
            {
                // Invalid or oversized — disconnect
                if (_connections.Remove(id))
                {
                    conn.TcpClient.Close();
                    _pendingDisconnections.Enqueue((id, payloadLength < 0
                        ? "invalid message length"
                        : "message too large"));
                }
                return true;
            }

            if (payloadLength > conn.BufferCount - offset - 4)
                break; // Incomplete message — wait for more data

            OnDataReceived?.Invoke(id,
                new ArraySegment<byte>(conn.Buffer, offset + 4, payloadLength));

            // Callback may have disconnected this client
            if (!_connections.ContainsKey(id))
                return true;

            offset += 4 + payloadLength;
        }

        conn.Compact(offset);
        return true;
    }

    public event Action<uint>? OnClientConnected;
    public event Action<uint, string?>? OnClientDisconnected;
    public event Action<uint, ArraySegment<byte>>? OnDataReceived;

    private class TcpConnection
    {
        public readonly TcpClient TcpClient;
        public readonly NetworkStream Stream;
        public byte[] Buffer = new byte[4096];
        public int BufferCount;

        public TcpConnection(TcpClient tcpClient)
        {
            TcpClient = tcpClient;
            Stream = tcpClient.GetStream();
        }

        public void EnsureCapacity(int required)
        {
            if (required <= Buffer.Length) return;
            int newSize = Buffer.Length;
            while (newSize < required) newSize *= 2;
            Array.Resize(ref Buffer, newSize);
        }

        public void Compact(int consumed)
        {
            if (consumed <= 0) return;
            int remaining = BufferCount - consumed;
            if (remaining > 0)
                System.Buffer.BlockCopy(Buffer, consumed, Buffer, 0, remaining);
            BufferCount = remaining;
        }
    }
}

/// <summary>
/// TCP client transport. Maintains a single connection to a server with length-prefixed framing.
/// Non-blocking I/O driven by Tick(). Connect is asynchronous — the connection is established
/// in the background, and OnConnected fires during the first Tick() after success.
/// Disables Nagle's algorithm for low-latency messaging.
/// </summary>
public class TcpClientTransport : IClientTransport
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private Task? _connectTask;
    private bool _connected;
    private byte[] _buffer = new byte[4096];
    private int _bufferCount;
    private bool _disconnectPending;
    private string? _disconnectReason;
    private readonly int _maxMessageSize;

    /// <summary>
    /// Creates a new TCP client transport.
    /// </summary>
    /// <param name="maxMessageSize">
    /// Maximum allowed payload size in bytes per message. Messages exceeding this limit
    /// cause a disconnect. Default: 10 MB.
    /// </param>
    public TcpClientTransport(int maxMessageSize = 10_485_760)
    {
        _maxMessageSize = maxMessageSize;
    }

    public void Connect(string host, int port)
    {
        // Clear stale state from a previous session
        _connectTask = null;
        _connected = false;
        _stream = null;
        _client?.Close();
        _bufferCount = 0;
        _disconnectPending = false;
        _disconnectReason = null;

        _client = new TcpClient { NoDelay = true };
        _connectTask = _client.ConnectAsync(host, port);
    }

    public void Disconnect()
    {
        if (_connected || _connectTask != null)
        {
            _connectTask = null;
            _connected = false;
            _stream = null;
            _client?.Close();
            _client = null;
            _disconnectPending = true;
            _disconnectReason = null;
        }
    }

    public void Send(ArraySegment<byte> data)
    {
        if (!_connected || _stream == null) return;

        try
        {
            Span<byte> header = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, data.Count);
            _stream.Write(header);
            _stream.Write(data);
        }
        catch
        {
            // Write failed — connection is broken. Tick() will fire OnDisconnected.
            _connected = false;
            _stream = null;
            _client?.Close();
            _client = null;
            _disconnectPending = true;
        }
    }

    public void Tick()
    {
        // Order: connect → data → disconnect (matches real TCP behavior)

        // 1. Check async connect completion
        if (_connectTask != null)
        {
            if (_connectTask.IsCompletedSuccessfully)
            {
                _connectTask = null;
                _stream = _client!.GetStream();
                _connected = true;
                OnConnected?.Invoke();
            }
            else if (_connectTask.IsFaulted)
            {
                var reason = _connectTask.Exception?.InnerException?.Message ?? "Connection failed";
                _connectTask = null;
                _client?.Close();
                _client = null;
                _disconnectPending = true;
                _disconnectReason = reason;
            }
            else if (_connectTask.IsCanceled)
            {
                _connectTask = null;
                _client?.Close();
                _client = null;
                _disconnectPending = true;
                _disconnectReason = "Connection canceled";
            }
            // else: still connecting, wait
        }

        // 2. Read data
        if (_connected && _stream != null)
        {
            try
            {
                if (!ReadMessages())
                {
                    _connected = false;
                    _stream = null;
                    _client?.Close();
                    _client = null;
                    _disconnectPending = true;
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                _connected = false;
                _stream = null;
                _client?.Close();
                _client = null;
                _disconnectPending = true;
                _disconnectReason = ex.Message;
            }
        }

        // 3. Fire pending disconnect event
        if (_disconnectPending)
        {
            _disconnectPending = false;
            var reason = _disconnectReason;
            _disconnectReason = null;
            OnDisconnected?.Invoke(reason);
        }
    }

    /// <summary>
    /// Reads available data and processes complete length-prefixed messages.
    /// Returns false if the remote end closed the connection cleanly.
    /// </summary>
    private bool ReadMessages()
    {
        int available = _client!.Available;
        if (available == 0) return true;

        // Grow buffer if needed
        if (_bufferCount + available > _buffer.Length)
        {
            int newSize = _buffer.Length;
            while (newSize < _bufferCount + available) newSize *= 2;
            Array.Resize(ref _buffer, newSize);
        }

        int read = _stream!.Read(_buffer, _bufferCount, available);
        if (read == 0) return false;

        _bufferCount += read;

        // Parse complete length-prefixed messages
        int offset = 0;
        while (offset + 4 <= _bufferCount)
        {
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(
                _buffer.AsSpan(offset, 4));

            if (payloadLength < 0 || payloadLength > _maxMessageSize)
            {
                _connected = false;
                _stream = null;
                _client?.Close();
                _client = null;
                _disconnectPending = true;
                _disconnectReason = payloadLength < 0
                    ? "invalid message length"
                    : "message too large";
                return true;
            }

            if (payloadLength > _bufferCount - offset - 4)
                break; // Incomplete message — wait for more data

            OnDataReceived?.Invoke(
                new ArraySegment<byte>(_buffer, offset + 4, payloadLength));

            // Callback may have disconnected us
            if (!_connected)
                return true;

            offset += 4 + payloadLength;
        }

        // Compact: shift remaining bytes to front
        if (offset > 0)
        {
            int remaining = _bufferCount - offset;
            if (remaining > 0)
                System.Buffer.BlockCopy(_buffer, offset, _buffer, 0, remaining);
            _bufferCount = remaining;
        }

        return true;
    }

    public event Action? OnConnected;
    public event Action<string?>? OnDisconnected;
    public event Action<ArraySegment<byte>>? OnDataReceived;
}
