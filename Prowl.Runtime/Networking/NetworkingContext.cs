using System;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>Owns one session and its transport execution. Use separate contexts for concurrent lobby and game connections.</summary>
/// <remarks>
/// Call from the creating thread. Host and Join take ownership of the transport once startup begins.
/// Session handlers run during Receive on this thread. Scene replication belongs to the default Networking session.
/// Custom transports must return promptly from I/O and shutdown; a stalled worker throws after five seconds
/// and retains ownership so disposal can be retried after the backend recovers.
/// </remarks>
public sealed class NetworkingContext : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly NetworkConfiguration _configuration;
    private NetworkingThread? _thread;
    private Transport? _transport;
    private bool _disposed, _disposing;

    public NetworkingContext(NetworkConfiguration? configuration = null) => _configuration = (configuration ?? new()).ValidatedCopy();

    public NetSession? Session { get; private set; }
    public int LocalPort => _thread?.LocalPort ?? _transport?.LocalPort ?? 0;

    public NetSession Host(Transport transport, string name = "Host", NetSessionOptions? options = null)
    {
        Prepare(transport);
        try
        {
            NetSession session;
            if (_thread == null) session = NetSession.Host(transport, name, options ?? _configuration.CreateSessionOptions());
            else
            {
                _thread.Listen();
                session = NetSession.CreateHost(transport, name, options ?? _configuration.CreateSessionOptions());
            }
            return Attach(session);
        }
        catch { ReleaseTransport(); throw; }
    }

    public NetSession Join(Transport transport, string address, string name, ReadOnlySpan<byte> token = default,
        NetSessionOptions? options = null)
    {
        Prepare(transport);
        try
        {
            return Attach(_thread == null
                ? NetSession.Join(transport, address, name, token, options ?? _configuration.CreateSessionOptions())
                : NetSession.CreateClient(_thread.Connect(address), name, token, options ?? _configuration.CreateSessionOptions()));
        }
        catch { ReleaseTransport(); throw; }
    }

    private void Prepare(Transport transport)
    {
        CheckOwner();
        ObjectDisposedException.ThrowIf(_disposing, this);
        ArgumentNullException.ThrowIfNull(transport);
        if (Session != null || _transport != null) throw new InvalidOperationException("This context already owns a session. Shut it down first.");
        bool worker = _configuration.UsesWorker(transport);
        _thread = worker ? new NetworkingThread(transport) : null;
        _transport = transport;
    }

    private NetSession Attach(NetSession session)
    {
        Session = session;
        session.Ended += (_, _) =>
        {
            try
            {
                ReleaseTransport();
                Session = null;
            }
            finally { session.Dispose(); }
        };
        return session;
    }

    public void Receive()
    {
        CheckOwner();
        NetSession? session = Session;
        if (session == null) return;
        NetworkingThread? thread = _thread;
        if (thread != null)
            while (session.State != SessionState.Ended && thread.TryReadEvent(out TransportEvent ev)) session.ProcessEvent(ev);
        session.Receive();
    }

    public void Flush()
    {
        CheckOwner();
        NetSession? session = Session;
        if (session == null) return;
        session.Flush();
        if (Session == session) _thread?.RequestFlush();
    }

    public void Update() { Receive(); if (!_disposed) Flush(); }

    public void Shutdown(string? message = null)
    {
        CheckOwner();
        NetSession? session = Session;
        session?.Shutdown(message);
        if (Session == session)
        {
            ReleaseTransport();
            Session = null;
        }
        session?.Dispose();
    }

    private void ReleaseTransport()
    {
        if (_thread != null) _thread.Dispose();
        else _transport?.Dispose();
        _thread = null;
        _transport = null;
    }

    private void CheckOwner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Drive this networking context from its creating thread.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        CheckOwner();
        _disposing = true;
        try { Shutdown(); _disposed = true; }
        finally { _disposing = false; }
    }
}
