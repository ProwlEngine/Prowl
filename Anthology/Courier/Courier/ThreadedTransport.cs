// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Prowl.Courier;

/// <summary>
///     Runs another transport on a worker thread: socket reads and writes, reliability timers, retransmits,
///     acknowledgements and encryption all happen there. Your thread keeps the usual single threaded API. Call
///     <see cref="Transport.Receive" />, <see cref="Transport.Poll" /> and <see cref="Connection.Send" /> from it as
///     before; sends are handed to the worker and events come back to <see cref="Transport.Poll" />.
/// </summary>
/// <remarks>
///     <para>
///         The worker wakes as soon as data arrives when the inner transport can signal it: a
///         <see cref="Transports.Local.LocalTransport" />, or a <see cref="Transports.Kcp.KcpTransport" /> with
///         <see cref="Transports.Kcp.KcpTransportOptions.UseIoThreads" />. Otherwise it checks every millisecond,
///         which on Windows means every timer tick, usually about 15 ms.
///     </para>
///     <para>
///         Use it when the game thread is too busy to pump the network often, or to spread a large server across
///         cores with one wrapped transport per port. Connections keep working, and acknowledge what arrives, even
///         while your thread is in a long frame.
///     </para>
///     <para>
///         The wrapper owns the inner transport: don't use the inner one directly once wrapped. Received data waits
///         for your thread within the queue budgets of this transport, as with any other.
///     </para>
/// </remarks>
public sealed class ThreadedTransport : Transport
{
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly Transport _inner;
    private readonly object _sendGate = new();
    private readonly ConcurrentQueue<Item> _toMain = new();
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _listening;
    private long _maxQueuedSendBytes = 64L * 1024 * 1024;
    private int _maxQueuedSendsPerConnection = 4096;
    private int _nextId;

    // Under _sendGate: bytes queued for the worker by connections that aren't closing.
    private long _queuedSendBytes;
    private ManualResetEventSlim? _shutdownDone;
    private volatile bool _stopping;
    private Thread? _worker;
    private ExceptionDispatchInfo? _workerError;

    // Worker thread only: each inner connection's proxy.
    private readonly Dictionary<Connection, ThreadedConnection> _proxies = new();
    private readonly List<ThreadedConnection> _snapshotScratch = new();

    /// <param name="inner">The transport to run on the worker. The wrapper takes ownership of it.</param>
    public ThreadedTransport(Transport inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _inner.SetActivitySignal(_wake);
    }

    public override bool IsListening => _listening;

    public override TransportCapabilities Capabilities => _inner.Capabilities;

    /// <summary>
    ///     The most sends one connection may have waiting for the worker. One more closes that connection with
    ///     <see cref="DisconnectReason.ResourceLimit" />.
    /// </summary>
    public int MaxQueuedSendsPerConnection
    {
        get => _maxQueuedSendsPerConnection;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxQueuedSendsPerConnection = value;
        }
    }

    /// <summary>
    ///     The most bytes all connections together may have waiting for the worker. A send past it closes whichever
    ///     connection has the most waiting with <see cref="DisconnectReason.ResourceLimit" />, so one slow peer can't
    ///     cost a healthy one its connection.
    /// </summary>
    public long MaxQueuedSendBytes
    {
        get => _maxQueuedSendBytes;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxQueuedSendBytes = value;
        }
    }

    public override int LocalPort => _inner.LocalPort;

    public override void Listen()
    {
        ThrowIfDisposed();
        if (_worker == null)
        {
            _inner.Listen();
        }
        else
        {
            RunOnWorker(new Command(CommandKind.Listen));
        }

        _listening = true;
        StartWorker();
    }

    public override Connection Connect(string address)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(address);
        StartWorker();
        ThreadedConnection proxy = new(this, Interlocked.Increment(ref _nextId), address, true);
        AddConnection(proxy);
        Post(new Command(CommandKind.Connect) { Proxy = proxy, Address = address });
        return proxy;
    }

    /// <summary>Queues the events the worker has produced since the last call. Never blocks on the network.</summary>
    public override void Receive()
    {
        ThrowIfDisposed();
        DrainWorkerEvents();
        if (Interlocked.Exchange(ref _workerError, null) is { } error)
        {
            error.Throw();
        }
    }

    /// <summary>The worker flushes on its own; this only wakes it so queued sends go out at once.</summary>
    public override void Flush()
    {
        ThrowIfDisposed();
        _wake.Set();
    }

    public override void Shutdown() => Shutdown(Timeout.InfiniteTimeSpan);

    /// <summary>
    ///     Shuts down as <see cref="Shutdown()" /> does, but gives up after <paramref name="timeout" /> with a
    ///     <see cref="TimeoutException" /> if the worker is stuck in the inner transport. The worker and the inner
    ///     transport are kept then, so a later call can finish the shutdown.
    /// </summary>
    public void Shutdown(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (_worker != null)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            if (_shutdownDone == null && !_worker.IsAlive)
            {
                Run(new Command(CommandKind.Shutdown));
                ForwardEvents();
            }
            else
            {
                _shutdownDone ??= PostShutdown();
                if (!_shutdownDone.Wait(timeout))
                {
                    throw new TimeoutException("The worker did not finish shutting down in time.");
                }
            }

            _stopping = true;
            _wake.Set();
            if (!_worker.Join(Remaining(timeout, elapsed)))
            {
                throw new TimeoutException("The worker did not stop in time.");
            }

            _worker = null;
            _stopping = false;
            _shutdownDone?.Dispose();
            _shutdownDone = null;
        }
        else
        {
            _inner.Shutdown();
        }

        DrainWorkerEvents();
        foreach (Connection connection in new List<Connection>(Connections))
        {
            RaiseDisconnected(connection, DisconnectReason.Requested);
        }

        while (_commands.TryDequeue(out Command command))
        {
            Discard(command);
        }

        _listening = false;
    }

    protected override void DisposeCore()
    {
        _inner.SetActivitySignal(null);
        _inner.Dispose();
        _wake.Dispose();
    }

    protected override void OnReceiveQueueOverflow(Connection connection)
    {
        RaiseDisconnected(connection, DisconnectReason.ResourceLimit);
        Post(new Command(CommandKind.Disconnect) { Proxy = (ThreadedConnection)connection });
    }

    internal void Post(Command command)
    {
        _commands.Enqueue(command);
        _wake.Set();
    }

    /// <summary>Counts a send against the queue budgets. Returns the connection to close for it, or null.</summary>
    internal ThreadedConnection? ReserveSend(ThreadedConnection proxy, int bytes)
    {
        lock (_sendGate)
        {
            ThreadedConnection? closing = null;
            if (proxy.QueuedSends >= _maxQueuedSendsPerConnection)
            {
                closing = proxy;
            }
            else if (bytes > _maxQueuedSendBytes - _queuedSendBytes)
            {
                closing = LargestBacklog(proxy, bytes);
            }

            // Once another connection's backlog is let go, this send fits.
            if (closing != proxy)
            {
                proxy.QueuedSends++;
                proxy.QueuedSendBytes += bytes;
                _queuedSendBytes += bytes;
            }

            if (closing != null)
            {
                closing.Closing = true;
                _queuedSendBytes -= closing.QueuedSendBytes;
            }

            return closing;
        }
    }

    internal void ReleaseSend(ThreadedConnection proxy, int bytes)
    {
        lock (_sendGate)
        {
            proxy.QueuedSends--;
            proxy.QueuedSendBytes -= bytes;
            if (!proxy.Closing)
            {
                _queuedSendBytes -= bytes;
            }
        }
    }

    // Under _sendGate. The open connection with the most queued, counting what proxy is about to add; proxy on a tie.
    private ThreadedConnection LargestBacklog(ThreadedConnection proxy, long adding)
    {
        ThreadedConnection largest = proxy;
        long most = proxy.QueuedSendBytes + adding;
        foreach (Connection connection in Connections)
        {
            if (connection is ThreadedConnection other && other != proxy && !other.Closing &&
                other.QueuedSendBytes > most)
            {
                (largest, most) = (other, other.QueuedSendBytes);
            }
        }

        return largest;
    }

    internal void CloseForBacklog(ThreadedConnection proxy)
    {
        if (proxy.State == ConnectionState.Disconnected)
        {
            return;
        }

        RaiseDisconnected(proxy, DisconnectReason.ResourceLimit);
        Post(new Command(CommandKind.Disconnect) { Proxy = proxy });
    }

    internal void CloseFromMain(ThreadedConnection proxy)
    {
        if (proxy.State == ConnectionState.Disconnected)
        {
            return;
        }

        RaiseDisconnected(proxy, DisconnectReason.Requested);
        Post(new Command(CommandKind.Disconnect) { Proxy = proxy });
    }

    private ManualResetEventSlim PostShutdown()
    {
        ManualResetEventSlim done = new();
        Post(new Command(CommandKind.Shutdown) { Done = done });
        return done;
    }

    private static TimeSpan Remaining(TimeSpan timeout, Stopwatch elapsed) =>
        timeout == Timeout.InfiniteTimeSpan
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromTicks(Math.Max(0, (timeout - elapsed.Elapsed).Ticks));

    private void StartWorker()
    {
        if (_worker != null)
        {
            return;
        }

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Courier worker" };
        _worker.Start();
    }

    /// <summary>Hands a command to the worker and waits until it has run.</summary>
    private void RunOnWorker(Command command)
    {
        // A worker that stopped on a failure can't run it, and nothing else touches the inner transport now.
        if (_worker is not { IsAlive: true })
        {
            Run(command);
            ForwardEvents();
            return;
        }

        using ManualResetEventSlim done = new();
        Post(command with { Done = done });
        done.Wait();
    }

    private void DrainWorkerEvents()
    {
        while (_toMain.TryDequeue(out Item item))
        {
            ThreadedConnection proxy = item.Proxy;
            switch (item.Type)
            {
                case TransportEventType.Connected:
                    if (!proxy.IsOutgoing)
                    {
                        AddConnection(proxy);
                    }

                    if (proxy.State == ConnectionState.Connecting)
                    {
                        RaiseConnected(proxy);
                    }

                    break;

                case TransportEventType.Disconnected:
                    // Over its budget while waiting for this thread: like the transport's own queue, its data goes too.
                    if (item.Reason == DisconnectReason.ResourceLimit)
                    {
                        DiscardQueuedData(proxy);
                    }

                    RaiseDisconnected(proxy, item.Reason);
                    break;

                case TransportEventType.Data:
                    proxy.ReleaseStaged(item.Buffer!.Length);
                    if (proxy.IsConnected)
                    {
                        RaiseDataPooled(proxy, item.Buffer, item.Length, item.Delivery, item.Channel);
                    }
                    else
                    {
                        ArrayPool<byte>.Shared.Return(item.Buffer);
                    }

                    break;
            }
        }
    }

    // --- Worker thread --------------------------------------------------------------------------

    /// <summary>Pumps the inner transport until shut down. A failure stops the worker and is rethrown by Receive.</summary>
    private void WorkerLoop()
    {
        try
        {
            while (!_stopping)
            {
                RunCommands();
                if (_stopping)
                {
                    break;
                }

                _inner.Receive();
                ForwardEvents();
                _inner.Flush();
                ForwardEvents();
                UpdateSnapshots();
                _wake.WaitOne(1);
            }
        }
        catch (Exception e)
        {
            _workerError = ExceptionDispatchInfo.Capture(e);
            while (_commands.TryDequeue(out Command command))
            {
                Discard(command);
                command.Done?.Set();
            }
        }
    }

    private void Discard(in Command command)
    {
        if (command is { Kind: CommandKind.Send, Proxy: { } proxy })
        {
            proxy.ReleasePending(command.Length);
            ReleaseSend(proxy, command.Length);
        }

        command.Release();
    }

    private void RunCommands()
    {
        while (_commands.TryDequeue(out Command command))
        {
            try
            {
                Run(command);
            }
            finally
            {
                command.Release();
                command.Done?.Set();
            }
        }
    }

    private void Run(Command command)
    {
        ThreadedConnection? proxy = command.Proxy;
        switch (command.Kind)
        {
            case CommandKind.Listen:
                _inner.Listen();
                break;

            case CommandKind.Connect:
                Connection inner = _inner.Connect(command.Address!);
                _proxies[inner] = proxy!;
                proxy!.Attach(inner);
                if (proxy.State == ConnectionState.Disconnected)
                {
                    inner.Disconnect();
                }

                break;

            case CommandKind.Send:
                proxy!.ReleasePending(command.Length);
                ReleaseSend(proxy, command.Length);
                // The inner connection can close between this check and the send, which is the normal race.
                if (!proxy.Closing && proxy.Inner is { IsConnected: true } target)
                {
                    try
                    {
                        target.Send(command.Buffer.AsSpan(0, command.Length), command.Delivery,
                            channel: command.Channel);
                    }
                    catch (InvalidOperationException) when (!target.IsConnected)
                    {
                    }
                }

                break;

            case CommandKind.Flush:
                proxy!.Inner?.Flush();
                break;

            case CommandKind.Disconnect:
                proxy!.ClosedByMain = true;
                proxy.Inner?.Disconnect();
                break;

            case CommandKind.Trust:
                proxy!.Inner?.MarkTrusted();
                break;

            case CommandKind.Shutdown:
                _inner.Shutdown();
                ForwardEvents();
                _stopping = true;
                break;
        }
    }

    private void ForwardEvents()
    {
        while (_inner.Poll(out TransportEvent ev))
        {
            if (!_proxies.TryGetValue(ev.Connection, out ThreadedConnection? proxy))
            {
                if (ev.Type != TransportEventType.Connected)
                {
                    continue;
                }

                proxy = new ThreadedConnection(this, Interlocked.Increment(ref _nextId), ev.Connection.Address, false);
                proxy.Attach(ev.Connection);
                _proxies[ev.Connection] = proxy;
            }

            switch (ev.Type)
            {
                case TransportEventType.Connected:
                    proxy.Snapshot();
                    _toMain.Enqueue(new Item(TransportEventType.Connected, proxy, null, 0, default, default, 0));
                    break;

                case TransportEventType.Disconnected:
                    _proxies.Remove(ev.Connection);
                    if (!proxy.ClosedByMain)
                    {
                        _toMain.Enqueue(new Item(TransportEventType.Disconnected, proxy, null, 0, default, ev.Reason, 0));
                    }

                    break;

                case TransportEventType.Data:
                    if (proxy.ClosedByMain)
                    {
                        break;
                    }

                    // Data waiting for the main thread counts against the same budget as the transport's own queue.
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(ev.Data.Length);
                    if (!proxy.TryStage(buffer.Length, MaxQueuedDataBytesPerConnection))
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        proxy.ClosedByMain = true;
                        ev.Connection.Disconnect();
                        _toMain.Enqueue(new Item(TransportEventType.Disconnected, proxy, null, 0, default,
                            DisconnectReason.ResourceLimit, 0));
                        break;
                    }

                    ev.Data.Span.CopyTo(buffer);
                    _toMain.Enqueue(new Item(TransportEventType.Data, proxy, buffer, ev.Data.Length, ev.Delivery, default,
                        ev.Channel));
                    break;
            }
        }
    }

    private void UpdateSnapshots()
    {
        _snapshotScratch.AddRange(_proxies.Values);
        foreach (ThreadedConnection proxy in _snapshotScratch)
        {
            proxy.Snapshot();
        }

        _snapshotScratch.Clear();
    }

    internal enum CommandKind : byte
    {
        Listen,
        Connect,
        Send,
        Flush,
        Disconnect,
        Trust,
        Shutdown
    }

    internal readonly record struct Command(CommandKind Kind)
    {
        public ThreadedConnection? Proxy { get; init; }
        public string? Address { get; init; }
        public byte[]? Buffer { get; init; }
        public int Length { get; init; }
        public Delivery Delivery { get; init; }
        public byte Channel { get; init; }
        public ManualResetEventSlim? Done { get; init; }

        public void Release()
        {
            if (Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(Buffer);
            }
        }
    }

    private readonly record struct Item(
        TransportEventType Type,
        ThreadedConnection Proxy,
        byte[]? Buffer,
        int Length,
        Delivery Delivery,
        DisconnectReason Reason,
        byte Channel);
}

/// <summary>
///     A connection of a <see cref="ThreadedTransport" />, standing in on your thread for the inner transport's
///     connection on the worker. Counters and round trip time are copies the worker refreshes as it runs.
/// </summary>
public sealed class ThreadedConnection : Connection
{
    private readonly ThreadedTransport _transport;
    private long _bytesReceived;
    private long _bytesSent;
    private volatile Connection? _inner;
    private long _innerPending;
    private long _maxPending = long.MaxValue;
    private long _packetsDropped;
    private long _packetsReceived;
    private long _packetsSent;
    private long _pending;
    private long _retransmits;
    private long _rttTicks;
    private long _staged;

    internal ThreadedConnection(ThreadedTransport transport, int id, string address, bool isOutgoing)
        : base(transport, id, address, isOutgoing)
    {
        _transport = transport;
    }

    internal Connection? Inner => _inner;

    /// <summary>Set on the worker once your thread has closed this connection, so the inner one's events are ignored.</summary>
    internal bool ClosedByMain { get; set; }

    /// <summary>Closed for its send backlog: what it still has queued is dropped, not sent.</summary>
    internal volatile bool Closing;

    // Under the transport's send gate: sends waiting for the worker.
    internal int QueuedSends;
    internal long QueuedSendBytes;

    public override TimeSpan RoundTripTime => TimeSpan.FromTicks(Interlocked.Read(ref _rttTicks));

    public override ConnectionStats Stats => new(Interlocked.Read(ref _packetsSent), Interlocked.Read(ref _packetsReceived),
        Interlocked.Read(ref _bytesSent), Interlocked.Read(ref _bytesReceived), Interlocked.Read(ref _retransmits),
        Interlocked.Read(ref _packetsDropped));

    /// <summary>What the inner connection has queued, plus sends still on their way to the worker.</summary>
    public override long PendingSendBytes => Interlocked.Read(ref _innerPending) + Interlocked.Read(ref _pending);

    public override long MaxPendingSendBytes => Interlocked.Read(ref _maxPending);

    public override int GetMaxMessageSize(Delivery delivery) => _inner?.GetMaxMessageSize(delivery) ?? 0;

    public override int GetBatchThreshold(Delivery delivery) => _inner?.GetBatchThreshold(delivery) ?? 0;

    protected override void SendCore(ReadOnlySpan<byte> data, Delivery delivery, byte channel)
    {
        ThreadedConnection? closing = _transport.ReserveSend(this, data.Length);
        if (closing != this)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, data.Length));
            data.CopyTo(buffer);
            Interlocked.Add(ref _pending, data.Length);
            _transport.Post(new ThreadedTransport.Command(ThreadedTransport.CommandKind.Send)
            {
                Proxy = this, Buffer = buffer, Length = data.Length, Delivery = delivery, Channel = channel
            });
        }

        if (closing != null)
        {
            _transport.CloseForBacklog(closing);
        }
    }

    /// <summary>Asks the worker to send this connection's queued messages now.</summary>
    public override void Flush() =>
        _transport.Post(new ThreadedTransport.Command(ThreadedTransport.CommandKind.Flush) { Proxy = this });

    public override void Disconnect() => _transport.CloseFromMain(this);

    protected override void OnTrusted() =>
        _transport.Post(new ThreadedTransport.Command(ThreadedTransport.CommandKind.Trust) { Proxy = this });

    internal void Attach(Connection inner) => _inner = inner;

    internal void ReleasePending(int bytes) => Interlocked.Add(ref _pending, -bytes);

    internal bool TryStage(int bytes, long limit)
    {
        if (Interlocked.Read(ref _staged) + bytes > limit)
        {
            return false;
        }

        Interlocked.Add(ref _staged, bytes);
        return true;
    }

    internal void ReleaseStaged(int bytes) => Interlocked.Add(ref _staged, -bytes);

    /// <summary>Worker: copies the inner connection's counters for your thread to read.</summary>
    internal void Snapshot()
    {
        Connection? inner = _inner;
        if (inner == null)
        {
            return;
        }

        ConnectionStats stats = inner.Stats;
        Interlocked.Exchange(ref _packetsSent, stats.PacketsSent);
        Interlocked.Exchange(ref _packetsReceived, stats.PacketsReceived);
        Interlocked.Exchange(ref _bytesSent, stats.BytesSent);
        Interlocked.Exchange(ref _bytesReceived, stats.BytesReceived);
        Interlocked.Exchange(ref _retransmits, stats.Retransmits);
        Interlocked.Exchange(ref _packetsDropped, stats.PacketsDropped);
        Interlocked.Exchange(ref _rttTicks, inner.RoundTripTime.Ticks);
        Interlocked.Exchange(ref _innerPending, inner.PendingSendBytes);
        Interlocked.Exchange(ref _maxPending, inner.MaxPendingSendBytes);
        if (inner.Address != Address)
        {
            Address = inner.Address;
        }
    }
}
