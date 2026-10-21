// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Generic;

namespace Prowl.Warp;

/// <summary>
/// Moves messages between this process and remote peers. A transport can listen for incoming
/// connections, open outgoing ones, or both.
/// </summary>
/// <remarks>
/// <para>
/// Transports do no work in the background. Everything happens when you pump them, on the thread
/// that pumps them: <see cref="Receive"/> reads incoming packets and queues events,
/// <see cref="Flush"/> sends queued messages and runs timers (retransmits, keepalives, timeouts),
/// and <see cref="Poll"/> hands the queued events to you. A game loop typically calls
/// <see cref="Receive"/> before its simulation step and <see cref="Flush"/> after it, or just
/// <see cref="Update"/> once per frame.
/// </para>
/// <para>A transport is not thread safe. Use each one from a single thread.</para>
/// </remarks>
public abstract class Transport : IDisposable
{
    private readonly Queue<PendingEvent> _events = new();
    private readonly Dictionary<int, Connection> _connections = new();
    private byte[]? _polledBuffer;
    private int _nextConnectionId = 1;
    private bool _disposed;
    private readonly Dictionary<Connection, (int Count, long Bytes)> _queuedData = new();
    private long _queuedDataBytes;

    /// <summary>Maximum unpolled data events from one connection; overflow disconnects that peer.</summary>
    public int MaxQueuedDataEventsPerConnection { get; set; } = 4096;

    /// <summary>Maximum pooled data buffer bytes queued by one connection.</summary>
    public long MaxQueuedDataBytesPerConnection { get; set; } = 32 * 1024 * 1024;

    /// <summary>Maximum pooled data buffer bytes queued by the whole transport.</summary>
    public long MaxQueuedDataBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>Every connection that is not yet <see cref="ConnectionState.Disconnected"/>, incoming and outgoing.</summary>
    public IReadOnlyCollection<Connection> Connections => _connections.Values;

    /// <summary>True between <see cref="Listen"/> and <see cref="Shutdown"/>.</summary>
    public abstract bool IsListening { get; }

    /// <summary>Starts accepting incoming connections, at the address the transport was configured with.</summary>
    public abstract void Listen();

    /// <summary>
    /// Starts connecting to a remote peer. The returned connection is <see cref="ConnectionState.Connecting"/>;
    /// a <see cref="TransportEventType.Connected"/> or <see cref="TransportEventType.Disconnected"/>
    /// event reports how it went. Connecting never fails synchronously: even an address that can't be
    /// resolved is reported by a later <see cref="Receive"/>.
    /// </summary>
    /// <param name="address">Where to connect, in the transport's own format (for UDP, <c>host:port</c>).</param>
    public abstract Connection Connect(string address);

    /// <summary>Reads everything that has arrived and queues the resulting events.</summary>
    public abstract void Receive();

    /// <summary>Sends queued messages and advances timers: retransmits, keepalives, handshakes and timeouts.</summary>
    public abstract void Flush();

    /// <summary><see cref="Receive"/> followed by <see cref="Flush"/>.</summary>
    public void Update()
    {
        Receive();
        Flush();
    }

    /// <summary>
    /// Takes the next queued event. Returns false when there are none left.
    /// </summary>
    /// <remarks>The <see cref="TransportEvent.Data"/> of the returned event is only valid until the next call.</remarks>
    public bool Poll(out TransportEvent ev)
    {
        if (_polledBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_polledBuffer);
            _polledBuffer = null;
        }

        if (!_events.TryDequeue(out PendingEvent pending))
        {
            ev = default;
            return false;
        }

        ReadOnlyMemory<byte> data = ReadOnlyMemory<byte>.Empty;
        if (pending.Buffer != null)
        {
            RemoveQueuedData(pending);
            _polledBuffer = pending.Buffer;
            data = new ReadOnlyMemory<byte>(pending.Buffer, 0, pending.Length);
        }

        ev = new TransportEvent(pending.Type, pending.Connection, data, pending.Delivery, pending.Reason);
        return true;
    }

    /// <summary>
    /// Closes every connection (telling the remote ends where the transport can) and stops listening.
    /// The <see cref="TransportEventType.Disconnected"/> events stay queued, so <see cref="Poll"/>
    /// still reports them. The transport can be used again afterwards.
    /// </summary>
    public abstract void Shutdown();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Shutdown();
        while (Poll(out _)) { }
        GC.SuppressFinalize(this);
    }

    // --- Helpers for transport implementations ---------------------------------------------

    /// <summary>A fresh id for a new <see cref="Connection"/>.</summary>
    protected int NextConnectionId() => _nextConnectionId++;

    /// <summary>Starts tracking a new connection in <see cref="Connections"/>.</summary>
    protected void AddConnection(Connection connection) => _connections.Add(connection.Id, connection);

    /// <summary>Marks the connection connected and queues its <see cref="TransportEventType.Connected"/> event.</summary>
    protected void RaiseConnected(Connection connection)
    {
        if (connection.State != ConnectionState.Connecting)
            throw new InvalidOperationException($"{connection} cannot become connected.");

        connection.State = ConnectionState.Connected;
        _events.Enqueue(new PendingEvent(TransportEventType.Connected, connection, null, 0, default, default));
    }

    /// <summary>
    /// Marks the connection disconnected, stops tracking it and queues its
    /// <see cref="TransportEventType.Disconnected"/> event. Does nothing if it is already disconnected.
    /// </summary>
    protected void RaiseDisconnected(Connection connection, DisconnectReason reason)
    {
        if (connection.State == ConnectionState.Disconnected)
            return;

        connection.State = ConnectionState.Disconnected;
        _connections.Remove(connection.Id);
        _events.Enqueue(new PendingEvent(TransportEventType.Disconnected, connection, null, 0, default, reason));
    }

    /// <summary>Queues a <see cref="TransportEventType.Data"/> event with a copy of <paramref name="data"/>.</summary>
    protected void RaiseData(Connection connection, ReadOnlySpan<byte> data, Delivery delivery)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(buffer);
        RaiseDataPooled(connection, buffer, data.Length, delivery);
    }

    /// <summary>
    /// Queues a <see cref="TransportEventType.Data"/> event for a buffer rented from
    /// <see cref="ArrayPool{T}.Shared"/>. The transport takes ownership and returns it once polled.
    /// </summary>
    protected void RaiseDataPooled(Connection connection, byte[] rentedBuffer, int length, Delivery delivery)
    {
        (int count, long bytes) = _queuedData.GetValueOrDefault(connection);
        if (!connection.IsConnected || count >= MaxQueuedDataEventsPerConnection ||
            rentedBuffer.Length > MaxQueuedDataBytesPerConnection - bytes ||
            rentedBuffer.Length > MaxQueuedDataBytes - _queuedDataBytes)
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
            if (connection.IsConnected)
            {
                OnReceiveQueueOverflow(connection);
                // Release the offending peer's queued buffers immediately, preserving other peers' events.
                int pendingCount = _events.Count;
                for (int i = 0; i < pendingCount; i++)
                {
                    PendingEvent pending = _events.Dequeue();
                    if (pending.Connection == connection && pending.Buffer != null)
                    {
                        RemoveQueuedData(pending);
                        ArrayPool<byte>.Shared.Return(pending.Buffer);
                    }
                    else
                        _events.Enqueue(pending);
                }
            }
            return;
        }
        _queuedData[connection] = (count + 1, bytes + rentedBuffer.Length);
        _queuedDataBytes += rentedBuffer.Length;
        _events.Enqueue(new PendingEvent(TransportEventType.Data, connection, rentedBuffer, length, delivery, default));
    }

    /// <summary>Closes a peer that exhausted the receive queue budget.</summary>
    protected virtual void OnReceiveQueueOverflow(Connection connection) => connection.Disconnect();

    private void RemoveQueuedData(PendingEvent pending)
    {
        (int count, long bytes) = _queuedData[pending.Connection];
        if (count == 1)
            _queuedData.Remove(pending.Connection);
        else
            _queuedData[pending.Connection] = (count - 1, bytes - pending.Buffer!.Length);
        _queuedDataBytes -= pending.Buffer!.Length;
    }

    private readonly record struct PendingEvent(
        TransportEventType Type,
        Connection Connection,
        byte[]? Buffer,
        int Length,
        Delivery Delivery,
        DisconnectReason Reason);
}
