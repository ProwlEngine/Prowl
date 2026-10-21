// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>Owns a networking worker and its bounded send/event queues. Session processing stays on the caller's thread.</summary>
/// <remarks>The worker exclusively drives Transport while running. Shutdown joins it and permits reuse.</remarks>
internal sealed class NetworkingThread : IDisposable
{
    private readonly Transport _transport;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _listenDone = new(false);
    private readonly Queue<Command> _commands = new();
    private readonly Queue<Notice> _notices = new();
    private readonly Dictionary<Connection, QueuedConnection> _peers = new(); // Worker only.
    private readonly Dictionary<int, QueuedConnection> _connections = new(); // Caller only.
    private readonly Queue<TransportEvent> _closedEvents = new(); // Caller only.
    private readonly List<Connection> _closing = new(); // Caller only.
    private readonly int _interval, _maxMessages;
    private readonly long _maxBytes, _maxPeerBytes;
    private readonly int _maxPeerMessages;
    private readonly bool _encrypted;
    private Thread? _worker;
    private volatile bool _stop, _listening;
    private Exception? _failure;
    private int _nextId, _pendingData, _pendingControls;
    private int _localPort;
    private long _outgoingBytes, _incomingBytes;
    private byte[]? _polledBuffer;
    private bool _disposed;

    public NetworkingThread(Transport transport, int intervalMilliseconds = 2, int maxQueuedMessages = 4096,
        long maxQueuedBytes = 64 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (intervalMilliseconds < 1 || intervalMilliseconds > 1000) throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxQueuedMessages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxQueuedBytes);
        if (!transport.Capabilities.IsAvailable || transport.Capabilities.Execution == TransportExecution.CreatingThreadRequired)
            throw new NotSupportedException("This transport cannot run on a networking worker.");
        _transport = transport; _interval = intervalMilliseconds; _maxMessages = maxQueuedMessages; _maxBytes = maxQueuedBytes;
        _encrypted = transport.IsEncrypted;
        _maxPeerMessages = transport.MaxQueuedDataEventsPerConnection;
        _maxPeerBytes = transport.MaxQueuedDataBytesPerConnection;
    }

    public Transport Transport => _transport;
    public bool IsListening => _listening;
    public int LocalPort => Volatile.Read(ref _localPort);
    public bool IsEncrypted => _encrypted;
    public IReadOnlyCollection<Connection> Connections => _connections.Values;
    public long PendingSendBytes { get { lock (_gate) return _outgoingBytes; } }
    public long PendingReceiveBytes { get { lock (_gate) return _incomingBytes; } }
    public bool IsWorkerRunning => _worker?.IsAlive == true;

    private void Start()
    {
        ThrowIfDisposed();
        CheckFailure();
        if (_worker != null) return;
        _stop = false;
        _worker = new Thread(Run) { IsBackground = true, Name = "Prowl networking" };
        _worker.Start();
    }

    public void Listen()
    {
        ThrowIfDisposed();
        if (_listening) { CheckFailure(); return; }
        _listenDone.Reset();
        Start();
        Post(new(CommandKind.Listen));
        _listenDone.Wait();
        CheckFailure();
    }

    public Connection Connect(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Start();
        if (_connections.Count + _closedEvents.Count >= _maxMessages)
            throw new InvalidOperationException("Drain networking events before opening more connections.");
        QueuedConnection peer = new(this, Interlocked.Increment(ref _nextId), address, true) { Registered = true };
        Post(new(CommandKind.Connect, peer));
        _connections.Add(peer.Id, peer);
        return peer;
    }

    private void Post(Command command)
    {
        lock (_gate)
        {
            if (_commands.Count >= _maxMessages) throw new InvalidOperationException("Networking command queue is full.");
            _commands.Enqueue(command);
        }
        _wake.Set();
    }

    private void Send(QueuedConnection peer, ReadOnlySpan<byte> data, Delivery delivery, SendFlags flags)
    {
        CheckFailure();
        if (Volatile.Read(ref peer.CloseRequested) != 0) return;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(buffer);
        bool accepted;
        lock (_gate)
        {
            accepted = _commands.Count < _maxMessages && buffer.Length <= _maxBytes - _outgoingBytes;
            if (accepted)
            {
                _outgoingBytes += buffer.Length;
                _commands.Enqueue(new(CommandKind.Send, peer, buffer, data.Length, delivery, flags));
            }
        }
        if (!accepted)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            RequestClose(peer, DisconnectReason.ResourceLimit);
        }
        _wake.Set();
    }

    private void RequestClose(QueuedConnection peer, DisconnectReason reason)
    {
        Interlocked.CompareExchange(ref peer.CloseRequested, (int)reason + 1, 0);
        _wake.Set();
    }

    /// <summary>Reads a queued event. Its payload is valid until the next read or disposal.</summary>
    public bool TryReadEvent(out TransportEvent ev)
    {
        ThrowIfDisposed();
        ReleasePolledBuffer();
        if (_closedEvents.TryDequeue(out ev)) return true;
        while (TakeNotice(out Notice notice))
        {
            QueuedConnection peer = notice.Peer;
            switch (notice.Type)
            {
                case TransportEventType.Connected:
                    if (peer.State != ConnectionState.Connecting) continue;
                    if (!peer.Registered) { peer.Registered = true; _connections.Add(peer.Id, peer); }
                    peer.MarkConnected();
                    break;
                case TransportEventType.Disconnected:
                    if (peer.State == ConnectionState.Disconnected) continue;
                    peer.MarkDisconnected();
                    _connections.Remove(peer.Id);
                    break;
                case TransportEventType.Data:
                    if (!peer.IsConnected || Volatile.Read(ref peer.CloseRequested) != 0)
                    { ArrayPool<byte>.Shared.Return(notice.Buffer!); continue; }
                    _polledBuffer = notice.Buffer;
                    break;
            }
            ev = new(notice.Type, peer, notice.Buffer == null ? default : notice.Buffer.AsMemory(0, notice.Length),
                notice.Delivery, notice.Reason);
            return true;
        }
        CheckFailure();
        ev = default;
        return false;
    }

    private bool TakeNotice(out Notice notice)
    {
        lock (_gate)
        {
            if (!_notices.TryDequeue(out notice)) return false;
            if (notice.Buffer != null)
            {
                _incomingBytes -= notice.Buffer.Length;
                notice.Peer.IncomingBytes -= notice.Buffer.Length;
                notice.Peer.IncomingCount--;
                _pendingData--;
            }
            else _pendingControls--;
            return true;
        }
    }

    public void RequestFlush() { ThrowIfDisposed(); CheckFailure(); _wake.Set(); }

    private void Run()
    {
        try
        {
            while (!_stop)
            {
                for (int processed = 0; processed < _maxMessages && !_stop; processed++)
                {
                    Command command;
                    lock (_gate) { if (!_commands.TryDequeue(out command)) break; }
                    try
                    {
                        switch (command.Kind)
                        {
                            case CommandKind.Listen:
                                _transport.Listen(); Volatile.Write(ref _localPort, _transport.LocalPort);
                                _listening = true; _listenDone.Set(); break;
                            case CommandKind.Connect:
                                QueuedConnection peer = command.Peer!;
                                peer.Inner = _transport.Connect(peer.Address);
                                Volatile.Write(ref _localPort, _transport.LocalPort);
                                _peers.Add(peer.Inner, peer);
                                break;
                            case CommandKind.Send:
                                if (command.Peer!.Inner is { IsConnected: true } target && Volatile.Read(ref command.Peer.CloseRequested) == 0)
                                    target.Send(command.Buffer.AsSpan(0, command.Length), command.Delivery, command.Flags);
                                break;
                            case CommandKind.Stop:
                                _stop = true;
                                break;
                        }
                    }
                    finally
                    {
                        if (command.Buffer != null)
                        {
                            lock (_gate) _outgoingBytes -= command.Buffer.Length;
                            ArrayPool<byte>.Shared.Return(command.Buffer);
                        }
                    }
                }
                foreach (QueuedConnection peer in _peers.Values)
                    if (Volatile.Read(ref peer.CloseRequested) != 0) peer.Inner!.Disconnect();
                _transport.Receive();
                _transport.Flush();
                while (_transport.Poll(out TransportEvent ev)) Handle(ev);
                lock (_gate)
                {
                    foreach (QueuedConnection peer in _peers.Values) peer.Refresh();
                }
                _wake.WaitOne(_interval);
            }
        }
        catch (Exception error) { lock (_gate) _failure = error; }
        finally
        {
            try { _transport.Shutdown(); while (_transport.Poll(out _)) { } }
            catch (Exception error) { lock (_gate) _failure ??= error; }
            _peers.Clear();
            _listening = false;
            Volatile.Write(ref _localPort, 0);
            lock (_gate)
            {
                while (_commands.TryDequeue(out Command pending))
                    if (pending.Buffer != null) ArrayPool<byte>.Shared.Return(pending.Buffer);
                _outgoingBytes = 0;
            }
            _listenDone.Set();
        }
    }

    private void Handle(in TransportEvent ev)
    {
        if (!_peers.TryGetValue(ev.Connection, out QueuedConnection? peer))
        {
            peer = new(this, Interlocked.Increment(ref _nextId), ev.Connection.Address, ev.Connection.IsOutgoing) { Inner = ev.Connection };
            _peers.Add(ev.Connection, peer);
        }
        lock (_gate) peer.Refresh();
        if (ev.Type == TransportEventType.Data)
        {
            if (Volatile.Read(ref peer.CloseRequested) != 0) return;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(ev.Data.Length);
            ev.Data.Span.CopyTo(buffer);
            bool accepted;
            lock (_gate)
            {
                accepted = _pendingData < _maxMessages && buffer.Length <= _maxBytes - _incomingBytes &&
                    peer.IncomingCount < _maxPeerMessages && buffer.Length <= _maxPeerBytes - peer.IncomingBytes;
                if (accepted)
                {
                    _incomingBytes += buffer.Length; peer.IncomingBytes += buffer.Length; peer.IncomingCount++; _pendingData++;
                    _notices.Enqueue(new(ev.Type, peer, buffer, ev.Data.Length, ev.Delivery));
                }
            }
            if (!accepted) { ArrayPool<byte>.Shared.Return(buffer); RequestClose(peer, DisconnectReason.ResourceLimit); }
            return;
        }
        DisconnectReason reason = ev.Reason;
        if (ev.Type == TransportEventType.Disconnected)
        {
            int requested = Volatile.Read(ref peer.CloseRequested);
            if (requested != 0) reason = (DisconnectReason)(requested - 1);
            _peers.Remove(ev.Connection);
        }
        lock (_gate)
        {
            if (_pendingControls >= _maxMessages) throw new InvalidOperationException("Networking control event queue is full.");
            _pendingControls++;
            _notices.Enqueue(new(ev.Type, peer, Reason: reason));
        }
    }

    private void CheckFailure()
    {
        Exception? failure;
        lock (_gate)
        {
            failure = _failure;
            if (failure == null) return;
        }
        CloseAll(DisconnectReason.Unreachable);
        throw new InvalidOperationException("The networking worker failed.", failure);
    }

    private void CloseAll(DisconnectReason reason)
    {
        _closing.AddRange(Connections);
        foreach (QueuedConnection peer in _closing)
        {
            peer.MarkDisconnected();
            _closedEvents.Enqueue(new(TransportEventType.Disconnected, peer, reason: reason));
        }
        _connections.Clear();
        _closing.Clear();
    }

    public void Shutdown()
    {
        if (_worker != null)
        {
            // Drain prior sends, then flush them before the inner shutdown sends its disconnect.
            lock (_gate) _commands.Enqueue(new(CommandKind.Stop));
            _wake.Set(); _worker.Join(); _worker = null;
            lock (_gate)
            {
                while (_commands.TryDequeue(out Command pending))
                    if (pending.Buffer != null) ArrayPool<byte>.Shared.Return(pending.Buffer);
                _outgoingBytes = 0;
            }
        }
        else _transport.Shutdown();
        while (TakeNotice(out Notice notice))
            if (notice.Buffer != null) ArrayPool<byte>.Shared.Return(notice.Buffer);
        CloseAll(DisconnectReason.Requested);
        lock (_gate) _failure = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void ReleasePolledBuffer()
    {
        if (_polledBuffer == null) return;
        ArrayPool<byte>.Shared.Return(_polledBuffer);
        _polledBuffer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Shutdown(); }
        finally
        {
            ReleasePolledBuffer();
            _closedEvents.Clear();
            _transport.Dispose(); _wake.Dispose(); _listenDone.Dispose();
        }
    }

    private enum CommandKind : byte { Listen, Connect, Send, Stop }
    private readonly record struct Command(CommandKind Kind, QueuedConnection? Peer = null, byte[]? Buffer = null,
        int Length = 0, Delivery Delivery = default, SendFlags Flags = default);
    private readonly record struct Notice(TransportEventType Type, QueuedConnection Peer, byte[]? Buffer = null,
        int Length = 0, Delivery Delivery = default, DisconnectReason Reason = default);

    private sealed class QueuedConnection(NetworkingThread owner, int id, string address, bool outgoing)
        : Connection(owner._transport, id, address, outgoing)
    {
        public Connection? Inner; // Worker only.
        public bool Registered; // Caller only.
        public int CloseRequested;
        public long IncomingBytes;
        public int IncomingCount;
        private int _reliableMax, _unreliableMax;
        private long _rttTicks;
        private ConnectionStats _stats;
        public void MarkConnected() => State = ConnectionState.Connected;
        public void MarkDisconnected() => State = ConnectionState.Disconnected;
        public void Refresh()
        {
            _reliableMax = Inner!.GetMaxMessageSize(Delivery.Reliable);
            _unreliableMax = owner._transport.Capabilities.Supports(Delivery.Unreliable) ? Inner.GetMaxMessageSize(Delivery.Unreliable) : 0;
            _rttTicks = Inner.RoundTripTime.Ticks;
            _stats = Inner.Stats;
        }
        public override TimeSpan RoundTripTime { get { lock (owner._gate) return TimeSpan.FromTicks(_rttTicks); } }
        public override ConnectionStats Stats { get { lock (owner._gate) return _stats; } }
        public override int GetMaxMessageSize(Delivery delivery)
        { lock (owner._gate) return delivery == Delivery.Reliable ? _reliableMax : _unreliableMax; }
        public override void Send(ReadOnlySpan<byte> data, Delivery delivery) => Send(data, delivery, SendFlags.None);
        public override void Flush() => owner.RequestFlush();
        public override void Send(ReadOnlySpan<byte> data, Delivery delivery, SendFlags flags)
        {
            ValidateSend(data, delivery);
            if ((flags & ~SendFlags.Immediate) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
            owner.Send(this, data, delivery, flags);
        }
        public override void Disconnect()
        {
            if (State != ConnectionState.Disconnected) owner.RequestClose(this, DisconnectReason.Requested);
        }
    }
}
