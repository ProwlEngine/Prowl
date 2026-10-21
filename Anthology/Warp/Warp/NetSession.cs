// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Prowl.Warp;

/// <summary>Where a <see cref="NetSession"/> is in its life.</summary>
public enum SessionState : byte
{
    /// <summary>A client that has not been let in yet.</summary>
    Joining,

    /// <summary>Hosting, or joined. Messages can be sent.</summary>
    Active,

    /// <summary>Over. See <see cref="NetSession.Ended"/> for why.</summary>
    Ended,
}

/// <summary>
/// A host and the clients that joined it, over any <see cref="Transport"/>. The session runs the join
/// handshake (version check, capacity, optional approval), keeps every member's peer list in step,
/// and routes typed <see cref="INetMessage"/>s to registered handlers.
/// </summary>
/// <remarks>
/// <para>
/// Star topology: clients only ever talk to the host, and the host relays whatever others need to see.
/// The host is a peer too (<see cref="NetPeer.HostId"/>), so a listen server and its local player use the
/// same code as remote ones; messages a peer sends to itself are delivered on the next <see cref="Receive"/>.
/// There is no host migration: when the host leaves, the session ends for everyone.
/// </para>
/// <para>
/// Like transports, a session does nothing in the background. Pump it with <see cref="Receive"/> and
/// <see cref="Flush"/> (or <see cref="Update"/>); events and message handlers run inside
/// <see cref="Receive"/>, on the calling thread. The session drives its transport and shuts it down when it ends.
/// </para>
/// </remarks>
public sealed class NetSession : IDisposable
{
    /// <summary>Message ids from here up belong to Warp. Your <see cref="INetMessage.Id"/>s must be below it.</summary>
    public const ushort ReservedMessageIds = 0xFF00;

    private enum InternalMessage : ushort
    {
        JoinRequest = ReservedMessageIds,
        JoinAccepted,
        PeerJoined,
        PeerLeft,
        Goodbye,

        /// <summary>
        /// A piece of a reliable message too big for the transport: <c>[bool first][varuint total, if first][bytes]</c>.
        /// The pieces of one message are sent back to back, and reliable delivery keeps them in order.
        /// </summary>
        Fragment,
    }

    /// <summary>A fragmented message being put back together.</summary>
    private sealed class Reassembly
    {
        public required byte[] Buffer;
        public required int Total;
        public int Filled;
        public long DeadlineMs;
    }

    private enum RemoteStage : byte
    {
        AwaitingRequest,
        Pending,
        Joined,
        Lingering,
    }

    /// <summary>What the host knows about one connection.</summary>
    private sealed class Remote
    {
        public RemoteStage Stage;
        public long DeadlineMs;
        public NetPeer? Peer;
        public PendingJoin? Pending;
        public Reassembly? Incoming;
        public Reassembly? BulkIncoming;
    }

    private readonly record struct Loopback(byte[] Buffer, int Length, Delivery Delivery);

    private readonly Dictionary<ushort, RawMessageHandler> _handlers = new();
    private readonly Dictionary<int, NetPeer> _peers = new();
    private readonly Dictionary<Connection, Remote> _remotes = new();
    private readonly List<KeyValuePair<Connection, Remote>> _scratch = new();
    private readonly Queue<Loopback> _loopback = new();
    private readonly NetWriter _writer = new();
    private readonly NetWriter _fragmentWriter = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _reassemblyBytes;
    private sealed class ResourceLimitException(string message) : Exception(message);
    private sealed class ReassemblyTimeoutException() : Exception("Fragmented message timed out.");

    /// <summary>Pooled bytes currently retained by incomplete incoming messages.</summary>
    public long PendingReassemblyBytes => _reassemblyBytes;

    // Client only.
    private readonly Connection? _hostConnection;
    private readonly string _joinName = string.Empty;
    private readonly byte[] _joinToken = Array.Empty<byte>();
    private long _joinDeadlineMs = long.MaxValue;
    private SessionEndReason? _goodbyeReason;
    private string? _goodbyeMessage;
    private Reassembly? _incomingFromHost;
    private Reassembly? _bulkIncomingFromHost;

    // Host only.
    private int _nextPeerId = NetPeer.HostId + 1;

    private NetSession(Transport transport, NetSessionOptions? options, bool isHost)
    {
        ArgumentNullException.ThrowIfNull(transport);
        Transport = transport;
        _options = (options ?? new NetSessionOptions()).CopyValidated();
        IsHost = isHost;
    }

    private NetSession(Transport transport, NetSessionOptions? options, string address, string name, byte[] token)
        : this(transport, options, isHost: false)
    {
        _joinName = name;
        _joinToken = token;
        State = SessionState.Joining;
        _hostConnection = transport.Connect(address);
    }

    /// <summary>Starts listening on <paramref name="transport"/> and hosts a session, with this process as the host peer.</summary>
    /// <param name="transport">The transport to host on. The session drives it from now on.</param>
    /// <param name="name">The host peer's name, as other peers see it.</param>
    /// <param name="options">Session settings; defaults if null.</param>
    public static NetSession Host(Transport transport, string name = "Host", NetSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        NetSession session = new(transport, options, isHost: true);
        transport.Listen();

        NetPeer host = new(NetPeer.HostId, name, isLocal: true, connection: null);
        session._peers.Add(host.Id, host);
        session.LocalPeer = host;
        session.HostPeer = host;
        session.State = SessionState.Active;
        return session;
    }

    /// <summary>
    /// Connects to a host and asks to join. The session starts <see cref="SessionState.Joining"/>;
    /// <see cref="Joined"/> or <see cref="Ended"/> reports how it went.
    /// </summary>
    /// <param name="transport">The transport to connect with. The session drives it from now on.</param>
    /// <param name="address">The host's address, in <paramref name="transport"/>'s format.</param>
    /// <param name="name">The name to join with.</param>
    /// <param name="token">Optional opaque data for the host's <see cref="JoinRequested"/>, such as an auth ticket.</param>
    /// <param name="options">Session settings; defaults if null. <see cref="NetSessionOptions.AppVersion"/> must match the host's.</param>
    public static NetSession Join(Transport transport, string address, string name, ReadOnlySpan<byte> token = default, NetSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(name);
        return new NetSession(transport, options, address, name, token.ToArray());
    }

    public Transport Transport { get; }

    private readonly NetSessionOptions _options;

    /// <summary>A detached copy of the settings captured when this session was created.</summary>
    public NetSessionOptions Options => _options.CopyValidated();

    public bool IsHost { get; }

    public SessionState State { get; private set; }

    /// <summary>This process's peer. Null on a client until it has joined.</summary>
    public NetPeer? LocalPeer { get; private set; }

    /// <summary>The host's peer. Null on a client until it has joined.</summary>
    public NetPeer? HostPeer { get; private set; }

    /// <summary>Everyone in the session, including the host and this process.</summary>
    public IReadOnlyCollection<NetPeer> Peers => _peers.Values;

    /// <summary>Client: the host let us in. <see cref="LocalPeer"/>, <see cref="HostPeer"/> and <see cref="Peers"/> are now set.</summary>
    public event Action? Joined;

    /// <summary>Someone else joined. Not raised for this process's own peer.</summary>
    public event Action<NetPeer>? PeerJoined;

    /// <summary>Someone else left. Not raised when the session itself ends.</summary>
    public event Action<NetPeer, SessionEndReason>? PeerLeft;

    /// <summary>
    /// Host: a client asks to join, having passed the version and capacity checks. Resolve the
    /// <see cref="PendingJoin"/> now or later. With no handler, every request is accepted.
    /// </summary>
    public event Action<PendingJoin>? JoinRequested;

    /// <summary>The session is over, with a reason and, when the other side gave one, a message. Raised once.</summary>
    public event Action<SessionEndReason, string?>? Ended;

    /// <summary>A message handler threw after decoding. The sender remains connected.</summary>
    public event Action<NetPeer, ushort, Exception>? HandlerFailed;

    public bool TryGetPeer(int id, [MaybeNullWhen(false)] out NetPeer peer) => _peers.TryGetValue(id, out peer);

    // --- Handlers ------------------------------------------------------------------------------

    /// <summary>Calls <paramref name="handler"/> for every <typeparamref name="T"/> received.</summary>
    /// <exception cref="InvalidOperationException">A handler is already registered for this id.</exception>
    public void On<T>(MessageHandler<T> handler) where T : INetMessage, new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        OnRaw(T.Id, (NetPeer sender, ref NetReader reader, Delivery _) =>
        {
            T message = new();
            try { message.Read(ref reader); }
            catch (Exception e) when (e is OverflowException or FormatException or ArgumentException)
            { throw new InvalidDataException("Typed message contains an invalid value.", e); }
            try { handler(sender, message); }
            catch (Exception e) { ReportHandlerFailure(sender, T.Id, e); }
        });
    }

    /// <summary>Calls <paramref name="handler"/> for every message with this id, to read its payload directly.</summary>
    /// <exception cref="InvalidOperationException">A handler is already registered for this id.</exception>
    public void OnRaw(ushort id, RawMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ValidateUserId(id);
        if (!_handlers.TryAdd(id, handler))
            throw new InvalidOperationException($"A handler is already registered for message id {id}.");
    }

    public bool Off<T>() where T : INetMessage => _handlers.Remove(T.Id);

    public bool OffRaw(ushort id) => _handlers.Remove(id);

    // --- Sending -------------------------------------------------------------------------------

    /// <summary>
    /// Sends a message to one peer. Clients can only send to the host (or themselves). Sending to a
    /// peer that has just left does nothing.
    /// </summary>
    /// <remarks>
    /// Reliable messages can be any size up to the receiver's <see cref="NetSessionOptions.MaxMessageSize"/>:
    /// ones the transport can't carry whole are split and reassembled. Unreliable ones must fit the transport.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The session is not <see cref="SessionState.Active"/>, or a client targets another client.</exception>
    public void Send<T>(NetPeer to, T message, Delivery delivery = Delivery.Reliable) where T : INetMessage
    {
        ArgumentNullException.ThrowIfNull(to);
        BeginUserMessage(T.Id);
        message.Write(_writer);
        SendWritten(to, delivery);
    }

    /// <summary>Sends a message to the host. On the host, delivers it to itself.</summary>
    public void SendToHost<T>(T message, Delivery delivery = Delivery.Reliable) where T : INetMessage
    {
        EnsureActive();
        Send(HostPeer!, message, delivery);
    }

    /// <summary>Host: sends a message to every client, except <paramref name="except"/>, and to itself if <paramref name="includeLocal"/>.</summary>
    public void Broadcast<T>(T message, Delivery delivery = Delivery.Reliable, NetPeer? except = null, bool includeLocal = false) where T : INetMessage
    {
        EnsureHost();
        BeginUserMessage(T.Id);
        message.Write(_writer);
        BroadcastWritten(delivery, except, includeLocal);
    }

    /// <summary>Like <see cref="Send{T}"/>, with a payload you have already serialized.</summary>
    public void SendRaw(NetPeer to, ushort id, ReadOnlySpan<byte> payload, Delivery delivery = Delivery.Reliable)
    {
        ArgumentNullException.ThrowIfNull(to);
        ValidateUserId(id);
        BeginUserMessage(id);
        _writer.WriteBytes(payload);
        SendWritten(to, delivery);
    }

    /// <summary>Like <see cref="Broadcast{T}"/>, with a payload you have already serialized.</summary>
    public void BroadcastRaw(ushort id, ReadOnlySpan<byte> payload, Delivery delivery = Delivery.Reliable, NetPeer? except = null, bool includeLocal = false)
    {
        EnsureHost();
        ValidateUserId(id);
        BeginUserMessage(id);
        _writer.WriteBytes(payload);
        BroadcastWritten(delivery, except, includeLocal);
    }

    // --- Membership ------------------------------------------------------------------------------

    /// <summary>
    /// Host: removes a client. It ends with <see cref="SessionEndReason.Kicked"/> and <paramref name="message"/>,
    /// and everyone else sees it leave with the same reason.
    /// </summary>
    public void Kick(NetPeer peer, string message)
    {
        ArgumentNullException.ThrowIfNull(peer);
        EnsureHost();
        if (peer.IsLocal)
            throw new InvalidOperationException("The host cannot kick itself. Use Shutdown.");

        if (peer.Connection != null && _remotes.TryGetValue(peer.Connection, out Remote? remote) && remote.Peer == peer)
            Goodbye(peer.Connection, remote, SessionEndReason.Kicked, message);
    }

    /// <summary>
    /// Leaves the session, or on the host, ends it for everyone with <paramref name="message"/>. Raises
    /// <see cref="Ended"/> with <see cref="SessionEndReason.Left"/>. Does nothing if already ended.
    /// </summary>
    public void Shutdown(string? message = null)
    {
        if (State == SessionState.Ended)
            return;

        if (IsHost)
        {
            WriteGoodbye(SessionEndReason.HostShutdown, message ?? "The host closed the session.");
            foreach (Connection connection in _remotes.Keys)
            {
                if (connection.IsConnected)
                    connection.Send(_writer.WrittenSpan, Delivery.Reliable);
            }
            Transport.Flush();
        }

        End(SessionEndReason.Left, message);
    }

    public void Dispose()
    {
        Shutdown();
        _writer.Dispose();
        _fragmentWriter.Dispose();
    }

    // --- Pumping -------------------------------------------------------------------------------

    /// <summary>Reads from the transport and runs everything that arrived: membership changes, events and message handlers.</summary>
    public void Receive()
    {
        if (State == SessionState.Ended)
            return;

        Transport.Receive();
        while (State != SessionState.Ended && Transport.Poll(out TransportEvent ev))
        {
            if (IsHost)
                HostEvent(ev);
            else
                ClientEvent(ev);
        }

        DispatchLoopback();
    }

    /// <summary>Runs the session's timers, then flushes the transport.</summary>
    public void Flush()
    {
        if (State == SessionState.Ended)
            return;

        long now = NowMs;
        if (IsHost)
        {
            _scratch.AddRange(_remotes);
            foreach ((Connection connection, Remote remote) in _scratch)
            {
                if ((remote.Incoming is { } incoming && now >= incoming.DeadlineMs) ||
                    (remote.BulkIncoming is { } bulk && now >= bulk.DeadlineMs))
                {
                    Goodbye(connection, remote, SessionEndReason.Timeout, "Fragmented message timed out.");
                    continue;
                }
                if (now < remote.DeadlineMs)
                    continue;

                switch (remote.Stage)
                {
                    case RemoteStage.AwaitingRequest:
                    case RemoteStage.Lingering:
                        connection.Disconnect();
                        break;

                    case RemoteStage.Pending:
                        Goodbye(connection, remote, SessionEndReason.Timeout, "The host took too long to let you in.");
                        break;
                }
            }
            _scratch.Clear();
        }
        else if ((_incomingFromHost is { } incoming && now >= incoming.DeadlineMs) ||
                 (_bulkIncomingFromHost is { } bulk && now >= bulk.DeadlineMs))
        {
            ReleaseReassembly(ref _incomingFromHost);
            ReleaseReassembly(ref _bulkIncomingFromHost);
            _goodbyeReason = SessionEndReason.Timeout;
            _goodbyeMessage = "Fragmented message timed out.";
            _hostConnection!.Disconnect();
        }
        else if (State == SessionState.Joining && now >= _joinDeadlineMs)
        {
            _goodbyeReason ??= SessionEndReason.Timeout;
            _goodbyeMessage ??= "The host did not let us in in time.";
            _hostConnection!.Disconnect();
        }

        Transport.Flush();
    }

    /// <summary><see cref="Receive"/> followed by <see cref="Flush"/>.</summary>
    public void Update()
    {
        Receive();
        Flush();
    }

    private long NowMs => _clock.ElapsedMilliseconds;

    // --- Host side -------------------------------------------------------------------------------

    private void HostEvent(in TransportEvent ev)
    {
        switch (ev.Type)
        {
            case TransportEventType.Connected:
                _remotes[ev.Connection] = new Remote
                {
                    Stage = RemoteStage.AwaitingRequest,
                    DeadlineMs = NowMs + (long)_options.JoinTimeout.TotalMilliseconds,
                };
                break;

            case TransportEventType.Data:
                HostReceive(ev.Connection, ev.Data.Span, ev.Delivery);
                break;

            case TransportEventType.Disconnected:
                if (_remotes.Remove(ev.Connection, out Remote? remote))
                {
                    ReleaseReassembly(ref remote.Incoming);
                    ReleaseReassembly(ref remote.BulkIncoming);
                    if (remote.Pending != null)
                        remote.Pending.IsResolved = true;
                    if (remote.Peer != null)
                        RemovePeer(remote.Peer, ev.Reason switch
                        {
                            DisconnectReason.Timeout => SessionEndReason.Timeout,
                            DisconnectReason.ResourceLimit => SessionEndReason.ResourceLimit,
                            DisconnectReason.AuthenticationFailed => SessionEndReason.AuthenticationFailed,
                            _ => SessionEndReason.Left,
                        });
                }
                break;
        }
    }

    private void HostReceive(Connection connection, ReadOnlySpan<byte> data, Delivery delivery, bool reassembled = false)
    {
        if (!_remotes.TryGetValue(connection, out Remote? remote))
            return;

        NetReader reader = new(data);
        try
        {
            if (data.Length > _options.MaxMessageSize)
                throw new InvalidDataException($"A {data.Length} byte message exceeds the {_options.MaxMessageSize} byte limit.");

            ushort id = reader.ReadUInt16();
            if (id == (ushort)InternalMessage.Fragment)
            {
                if (reassembled)
                    throw new InvalidDataException("Nested message fragments are not allowed.");
                if ((delivery == Delivery.Reliable && remote.Stage is RemoteStage.AwaitingRequest or RemoteStage.Joined ||
                     delivery == Delivery.Bulk && remote.Stage == RemoteStage.Joined)
                    && Reassemble(ref (delivery == Delivery.Bulk ? ref remote.BulkIncoming : ref remote.Incoming), ref reader, out int length,
                        remote.Stage == RemoteStage.Joined ? _options.MaxMessageSize : 8192) is { } whole)
                {
                    try
                    {
                        HostReceive(connection, whole.AsSpan(0, length), delivery, reassembled: true);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(whole);
                    }
                }
                return;
            }

            switch (remote.Stage)
            {
                case RemoteStage.AwaitingRequest:
                    if (id == (ushort)InternalMessage.JoinRequest)
                        HandleJoinRequest(connection, remote, ref reader);
                    break;

                case RemoteStage.Joined:
                    // Clients have no business sending Warp's own messages once joined.
                    if (id < ReservedMessageIds)
                        Dispatch(remote.Peer!, id, ref reader, delivery);
                    break;

                // Pending and lingering clients aren't in the session; drop what they send.
            }
        }
        catch (Exception e) when (e is ResourceLimitException or ReassemblyTimeoutException)
        {
            Goodbye(connection, remote, e is ResourceLimitException ? SessionEndReason.ResourceLimit : SessionEndReason.Timeout, e.Message);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
        {
            if (remote.Stage != RemoteStage.Lingering)
                Goodbye(connection, remote, SessionEndReason.ProtocolError, "Sent a malformed message.");
        }
    }

    private void HandleJoinRequest(Connection connection, Remote remote, ref NetReader reader)
    {
        uint version = reader.ReadUInt32();
        string name = reader.ReadString();
        byte[] token = reader.ReadBytesWithLength().ToArray();

        if (version != _options.AppVersion)
        {
            Goodbye(connection, remote, SessionEndReason.VersionMismatch, $"The host runs version {_options.AppVersion}, but you have {version}.");
            return;
        }

        if (JoinedClientCount >= _options.MaxPeers)
        {
            Goodbye(connection, remote, SessionEndReason.ServerFull, "The session is full.");
            return;
        }

        PendingJoin pending = new(this, connection, new JoinRequest(version, name, token));
        remote.Stage = RemoteStage.Pending;
        remote.Pending = pending;
        remote.DeadlineMs = NowMs + (long)_options.JoinTimeout.TotalMilliseconds;

        if (JoinRequested == null)
            pending.Accept();
        else
            JoinRequested(pending);
    }

    internal void ResolveJoin(PendingJoin pending, bool accept, string? message)
    {
        if (pending.IsResolved)
            return;
        pending.IsResolved = true;

        Connection connection = pending.Connection;
        if (State != SessionState.Active || !_remotes.TryGetValue(connection, out Remote? remote) || remote.Pending != pending)
            return;
        remote.Pending = null;

        if (!accept)
        {
            Goodbye(connection, remote, SessionEndReason.Rejected, message ?? "The host turned you away.");
            return;
        }

        // Checked again: several joins may have been pending at once.
        if (JoinedClientCount >= _options.MaxPeers)
        {
            Goodbye(connection, remote, SessionEndReason.ServerFull, "The session is full.");
            return;
        }

        NetPeer peer = new(_nextPeerId++, pending.Request.Name, isLocal: false, connection);
        remote.Stage = RemoteStage.Joined;
        remote.Peer = peer;
        _peers.Add(peer.Id, peer);

        // The newcomer learns its id and everyone present, itself included.
        _writer.Reset();
        _writer.WriteUInt16((ushort)InternalMessage.JoinAccepted);
        _writer.WriteVarUInt32((uint)peer.Id);
        _writer.WriteVarUInt32((uint)_peers.Count);
        foreach (NetPeer present in _peers.Values)
        {
            _writer.WriteVarUInt32((uint)present.Id);
            _writer.WriteString(present.Name);
        }
        SendToConnection(connection, _writer.WrittenSpan, Delivery.Reliable);

        _writer.Reset();
        _writer.WriteUInt16((ushort)InternalMessage.PeerJoined);
        _writer.WriteVarUInt32((uint)peer.Id);
        _writer.WriteString(peer.Name);
        BroadcastWritten(Delivery.Reliable, except: peer, includeLocal: false);

        PeerJoined?.Invoke(peer);
    }

    /// <summary>
    /// Tells a client why it is being let go, takes it out of the session, and gives it
    /// <see cref="NetSessionOptions.GoodbyeLinger"/> to hang up before the host does.
    /// </summary>
    private void Goodbye(Connection connection, Remote remote, SessionEndReason reason, string message)
    {
        WriteGoodbye(reason, message);
        if (connection.IsConnected)
            connection.Send(_writer.WrittenSpan, Delivery.Reliable);

        if (remote.Pending != null)
        {
            remote.Pending.IsResolved = true;
            remote.Pending = null;
        }

        ReleaseReassembly(ref remote.Incoming);
        ReleaseReassembly(ref remote.BulkIncoming);
        NetPeer? peer = remote.Peer;
        remote.Peer = null;
        remote.Stage = RemoteStage.Lingering;
        remote.DeadlineMs = NowMs + (long)_options.GoodbyeLinger.TotalMilliseconds;

        if (peer != null)
            RemovePeer(peer, reason);
    }

    private void RemovePeer(NetPeer peer, SessionEndReason reason)
    {
        if (!_peers.Remove(peer.Id))
            return;

        _writer.Reset();
        _writer.WriteUInt16((ushort)InternalMessage.PeerLeft);
        _writer.WriteVarUInt32((uint)peer.Id);
        _writer.WriteByte((byte)reason);
        BroadcastWritten(Delivery.Reliable, except: null, includeLocal: false);

        PeerLeft?.Invoke(peer, reason);
    }

    private int JoinedClientCount => _peers.Count - 1;

    // --- Client side -----------------------------------------------------------------------------

    private void ClientEvent(in TransportEvent ev)
    {
        if (ev.Connection != _hostConnection)
            return;

        switch (ev.Type)
        {
            case TransportEventType.Connected:
                _writer.Reset();
                _writer.WriteUInt16((ushort)InternalMessage.JoinRequest);
                _writer.WriteUInt32(_options.AppVersion);
                _writer.WriteString(_joinName);
                _writer.WriteBytesWithLength(_joinToken);
                _hostConnection.Send(_writer.WrittenSpan, Delivery.Reliable);
                _joinDeadlineMs = NowMs + (long)_options.JoinTimeout.TotalMilliseconds;
                break;

            case TransportEventType.Data:
                ClientReceive(ev.Data.Span, ev.Delivery);
                break;

            case TransportEventType.Disconnected:
                End(_goodbyeReason ?? ev.Reason switch
                {
                    DisconnectReason.Requested => SessionEndReason.Left,
                    DisconnectReason.RemoteClosed => SessionEndReason.HostShutdown,
                    DisconnectReason.Timeout => SessionEndReason.Timeout,
                    DisconnectReason.Unreachable => SessionEndReason.Unreachable,
                    DisconnectReason.ServerFull => SessionEndReason.ServerFull,
                    DisconnectReason.ResourceLimit => SessionEndReason.ResourceLimit,
                            DisconnectReason.AuthenticationFailed => SessionEndReason.AuthenticationFailed,
                    _ => SessionEndReason.Rejected,
                }, _goodbyeMessage);
                break;
        }
    }

    private void ClientReceive(ReadOnlySpan<byte> data, Delivery delivery, bool reassembled = false)
    {
        NetReader reader = new(data);
        try
        {
            if (data.Length > _options.MaxMessageSize)
                throw new InvalidDataException($"A {data.Length} byte message exceeds the {_options.MaxMessageSize} byte limit.");

            ushort id = reader.ReadUInt16();
            switch (id)
            {
                case (ushort)InternalMessage.Fragment:
                    if (reassembled)
                        throw new InvalidDataException("Nested message fragments are not allowed.");
                    if ((delivery == Delivery.Reliable || delivery == Delivery.Bulk && State == SessionState.Active)
                        && Reassemble(ref (delivery == Delivery.Bulk ? ref _bulkIncomingFromHost : ref _incomingFromHost), ref reader, out int length) is { } whole)
                    {
                        try
                        {
                            ClientReceive(whole.AsSpan(0, length), delivery, reassembled: true);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(whole);
                        }
                    }
                    break;

                case (ushort)InternalMessage.JoinAccepted:
                    if (State == SessionState.Joining)
                        HandleJoinAccepted(ref reader);
                    break;

                case (ushort)InternalMessage.PeerJoined:
                    if (State == SessionState.Active)
                    {
                        NetPeer peer = new((int)reader.ReadVarUInt32(), reader.ReadString(), isLocal: false, connection: null);
                        if (_peers.TryAdd(peer.Id, peer))
                            PeerJoined?.Invoke(peer);
                    }
                    break;

                case (ushort)InternalMessage.PeerLeft:
                    if (State == SessionState.Active)
                    {
                        int peerId = (int)reader.ReadVarUInt32();
                        SessionEndReason reason = ReadReason(ref reader);
                        if (peerId != LocalPeer!.Id && _peers.Remove(peerId, out NetPeer? peer))
                            PeerLeft?.Invoke(peer, reason);
                    }
                    break;

                case (ushort)InternalMessage.Goodbye:
                    _goodbyeReason = ReadReason(ref reader);
                    _goodbyeMessage = reader.ReadString();
                    _hostConnection!.Disconnect();
                    break;

                default:
                    if (State == SessionState.Active && id < ReservedMessageIds)
                        Dispatch(HostPeer!, id, ref reader, delivery);
                    break;
            }
        }
        catch (Exception e) when (e is ResourceLimitException or ReassemblyTimeoutException)
        {
            ReleaseReassembly(ref _incomingFromHost);
            ReleaseReassembly(ref _bulkIncomingFromHost);
            _goodbyeReason = e is ResourceLimitException ? SessionEndReason.ResourceLimit : SessionEndReason.Timeout;
            _goodbyeMessage = e.Message;
            _hostConnection!.Disconnect();
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
        {
            _goodbyeReason ??= SessionEndReason.ProtocolError;
            _goodbyeMessage ??= "The host sent a malformed message.";
            _hostConnection!.Disconnect();
        }
    }

    private void HandleJoinAccepted(ref NetReader reader)
    {
        int localId = (int)reader.ReadVarUInt32();
        uint count = reader.ReadVarUInt32();
        if (count > (uint)reader.Remaining)
            throw new InvalidDataException("Peer count exceeds the message size.");

        for (uint i = 0; i < count; i++)
        {
            int id = (int)reader.ReadVarUInt32();
            string name = reader.ReadString();
            Connection? connection = id == NetPeer.HostId ? _hostConnection : null;
            if (!_peers.TryAdd(id, new NetPeer(id, name, isLocal: id == localId, connection)))
                throw new InvalidDataException($"Peer id {id} listed twice.");
        }

        if (!_peers.TryGetValue(localId, out NetPeer? local) || !_peers.TryGetValue(NetPeer.HostId, out NetPeer? host))
            throw new InvalidDataException("The peer list is missing the host or this client.");

        LocalPeer = local;
        HostPeer = host;
        State = SessionState.Active;
        _joinDeadlineMs = long.MaxValue;
        Joined?.Invoke();
    }

    private static SessionEndReason ReadReason(ref NetReader reader)
    {
        byte value = reader.ReadByte();
        return value <= (byte)SessionEndReason.AuthenticationFailed ? (SessionEndReason)value : SessionEndReason.Left;
    }

    // --- Shared --------------------------------------------------------------------------------

    private void Dispatch(NetPeer sender, ushort id, ref NetReader reader, Delivery delivery)
    {
        // Messages nobody registered for are dropped; a peer on a newer build may know more messages.
        if (_handlers.TryGetValue(id, out RawMessageHandler? handler))
        {
            try { handler(sender, ref reader, delivery); }
            catch (Exception e) when (e is not (EndOfStreamException or InvalidDataException))
            { ReportHandlerFailure(sender, id, e); }
        }
    }

    private void ReportHandlerFailure(NetPeer sender, ushort id, Exception error)
    {
        Trace.TraceError($"Warp message {id} handler failed: {error}");
        if (HandlerFailed == null) return;
        foreach (Action<NetPeer, ushort, Exception> listener in HandlerFailed.GetInvocationList())
        {
            try { listener(sender, id, error); }
            catch (Exception e) { Trace.TraceError($"Warp HandlerFailed listener failed: {e}"); }
        }
    }

    private void BeginUserMessage(ushort id)
    {
        ValidateUserId(id);
        EnsureActive();
        _writer.Reset();
        _writer.WriteUInt16(id);
    }

    private void SendWritten(NetPeer to, Delivery delivery)
    {
        if (!Enum.IsDefined(delivery)) throw new ArgumentOutOfRangeException(nameof(delivery));
        if (to.IsLocal)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(_writer.Length);
            _writer.WrittenSpan.CopyTo(buffer);
            _loopback.Enqueue(new Loopback(buffer, _writer.Length, delivery));
            return;
        }

        if (!IsHost && !to.IsHost)
            throw new InvalidOperationException($"Clients can only send to the host, not to {to}. Relay through the host instead.");

        // A peer that already left (its NetPeer can outlive it) is skipped: that race is normal, not an error.
        if (to.Connection is { IsConnected: true } connection && _peers.TryGetValue(to.Id, out NetPeer? current) && current == to)
            SendToConnection(connection, _writer.WrittenSpan, delivery);
    }

    /// <summary>Sends a message, splitting a reliable one into fragments if the transport can't take it whole.</summary>
    private void SendToConnection(Connection connection, ReadOnlySpan<byte> message, Delivery delivery)
    {
        if (!connection.IsConnected)
            return;
        int max = connection.GetMaxMessageSize(delivery);
        if (delivery == Delivery.Unreliable || message.Length <= max)
        {
            connection.Send(message, delivery);
            return;
        }

        // Fragment id, first flag and a total length of up to 5 bytes.
        int chunkSize = max - 8;
        for (int offset = 0; offset < message.Length && connection.IsConnected; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, message.Length - offset);
            _fragmentWriter.Reset();
            _fragmentWriter.WriteUInt16((ushort)InternalMessage.Fragment);
            _fragmentWriter.WriteBool(offset == 0);
            if (offset == 0)
                _fragmentWriter.WriteVarUInt32((uint)message.Length);
            _fragmentWriter.WriteBytes(message.Slice(offset, length));
            connection.Send(_fragmentWriter.WrittenSpan, delivery);
        }
    }

    /// <summary>
    /// Adds a fragment to <paramref name="state"/>. Returns the whole message, rented from
    /// <see cref="ArrayPool{T}.Shared"/>, once its last fragment arrives; null until then.
    /// </summary>
    private byte[]? Reassemble(ref Reassembly? state, ref NetReader reader, out int length, int? maximum = null)
    {
        length = 0;
        if (reader.ReadBool())
        {
            if (state != null)
                throw new InvalidDataException("A fragmented message started before the previous one finished.");

            uint total = reader.ReadVarUInt32();
            if (total < 2 || total > (uint)(maximum ?? _options.MaxMessageSize))
                throw new InvalidDataException($"A {total} byte message exceeds the {_options.MaxMessageSize} byte limit.");

            if (total > _options.MaxReassemblyBytes - _reassemblyBytes)
                throw new ResourceLimitException("Session reassembly budget exceeded.");
            byte[] buffer = ArrayPool<byte>.Shared.Rent((int)total);
            if (buffer.Length > _options.MaxReassemblyBytes - _reassemblyBytes)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw new ResourceLimitException("Session reassembly budget exceeded.");
            }
            _reassemblyBytes += buffer.Length;
            state = new Reassembly { Buffer = buffer, Total = (int)total,
                DeadlineMs = NowMs + (long)_options.ReassemblyTimeout.TotalMilliseconds };
        }
        else if (state == null)
        {
            throw new InvalidDataException("A message fragment arrived without its start.");
        }

        if (NowMs >= state.DeadlineMs)
            throw new ReassemblyTimeoutException();
        ReadOnlySpan<byte> chunk = reader.ReadBytes(reader.Remaining);
        if (chunk.Length > state.Total - state.Filled)
            throw new InvalidDataException("Message fragments add up to more than their announced length.");

        chunk.CopyTo(state.Buffer.AsSpan(state.Filled));
        state.Filled += chunk.Length;
        if (state.Filled < state.Total)
            return null;

        byte[] whole = state.Buffer;
        _reassemblyBytes -= whole.Length;
        length = state.Total;
        state = null;
        return whole;
    }

    private void ReleaseReassembly(ref Reassembly? state)
    {
        if (state == null)
            return;
        _reassemblyBytes -= state.Buffer.Length;
        ArrayPool<byte>.Shared.Return(state.Buffer);
        state.Buffer = Array.Empty<byte>();
        state = null;
    }

    private void BroadcastWritten(Delivery delivery, NetPeer? except, bool includeLocal)
    {
        foreach (NetPeer peer in _peers.Values)
        {
            if (peer == except || (peer.IsLocal && !includeLocal))
                continue;
            SendWritten(peer, delivery);
        }
    }

    private void DispatchLoopback()
    {
        // Only what was queued before this call; messages handlers send to themselves wait for the next Receive.
        int count = _loopback.Count;
        for (int i = 0; i < count && State == SessionState.Active; i++)
        {
            Loopback message = _loopback.Dequeue();
            try
            {
                NetReader reader = new(message.Buffer.AsSpan(0, message.Length));
                ushort id = reader.ReadUInt16();
                Dispatch(LocalPeer!, id, ref reader, message.Delivery);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(message.Buffer);
            }
        }
    }

    private void WriteGoodbye(SessionEndReason reason, string message)
    {
        _writer.Reset();
        _writer.WriteUInt16((ushort)InternalMessage.Goodbye);
        _writer.WriteByte((byte)reason);
        _writer.WriteString(message);
    }

    private void End(SessionEndReason reason, string? message)
    {
        if (State == SessionState.Ended)
            return;
        State = SessionState.Ended;

        ReleaseReassembly(ref _incomingFromHost);
        ReleaseReassembly(ref _bulkIncomingFromHost);
        foreach (Remote remote in _remotes.Values)
        {
            ReleaseReassembly(ref remote.Incoming);
            ReleaseReassembly(ref remote.BulkIncoming);
            if (remote.Pending != null)
                remote.Pending.IsResolved = true;
        }
        _remotes.Clear();
        _peers.Clear();
        while (_loopback.TryDequeue(out Loopback pending))
            ArrayPool<byte>.Shared.Return(pending.Buffer);

        Transport.Shutdown();
        while (Transport.Poll(out _)) { }

        Ended?.Invoke(reason, message);
    }

    private void EnsureActive()
    {
        if (State != SessionState.Active)
            throw new InvalidOperationException($"The session is {State}; messages can only be sent while it is Active.");
    }

    private void EnsureHost()
    {
        if (!IsHost)
            throw new InvalidOperationException("Only the host can do this.");
        EnsureActive();
    }

    private static void ValidateUserId(ushort id)
    {
        if (id >= ReservedMessageIds)
            throw new ArgumentOutOfRangeException(nameof(id), id, $"Message ids from {ReservedMessageIds} up are reserved by Warp.");
    }
}
