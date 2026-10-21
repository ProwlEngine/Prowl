// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Prowl.Warp.Transports.Batching;

/// <summary>Combines small messages per connection and delivery mode. Both ends must use this wrapper.</summary>
/// <remarks>Owns the inner transport. Flush sends partial batches; full batches are submitted immediately.</remarks>
public sealed class BatchingTransport : Transport
{
    private readonly Transport _inner;
    private readonly int _batchSize;
    private readonly Dictionary<Connection, BatchedConnection> _peers = new();

    public BatchingTransport(Transport inner, int batchSize = 1200)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 8);
        _inner = inner;
        _batchSize = batchSize;
    }

    public override bool IsListening => _inner.IsListening;
    public override int LocalPort => _inner.LocalPort;
    public override bool IsEncrypted => _inner.IsEncrypted;
    public override void Listen() { ThrowIfDisposed(); _inner.Listen(); }
    public override Connection Connect(string address)
    {
        ThrowIfDisposed();
        return Add(_inner.Connect(address));
    }

    private BatchedConnection Add(Connection inner)
    {
        if (_peers.TryGetValue(inner, out BatchedConnection? existing)) return existing;
        BatchedConnection peer = new(this, inner, NextConnectionId());
        _peers.Add(inner, peer);
        AddConnection(peer);
        return peer;
    }

    public override void Receive()
    {
        ThrowIfDisposed();
        _inner.Receive();
        PollInner(deliverData: true);
    }

    private void PollInner(bool deliverData)
    {
        while (_inner.Poll(out TransportEvent ev))
        {
            if (!_peers.TryGetValue(ev.Connection, out BatchedConnection? peer))
            {
                if (ev.Type != TransportEventType.Connected) continue;
                peer = Add(ev.Connection);
            }
            switch (ev.Type)
            {
                case TransportEventType.Connected:
                    RaiseConnected(peer);
                    break;
                case TransportEventType.Disconnected:
                    RaiseDisconnected(peer, peer.Malformed ? DisconnectReason.ProtocolError : ev.Reason);
                    peer.Release();
                    _peers.Remove(ev.Connection);
                    break;
                case TransportEventType.Data when deliverData && peer.IsConnected && !peer.Malformed:
                    ReadBatch(peer, ev.Data.Span, ev.Delivery);
                    break;
            }
        }
    }

    private void ReadBatch(BatchedConnection peer, ReadOnlySpan<byte> batch, Delivery delivery)
    {
        // Validate the entire frame before exposing any messages to the application.
        int offset = 0;
        bool valid = batch.Length >= 4;
        while (valid && offset < batch.Length)
        {
            if (batch.Length - offset < 4) { valid = false; break; }
            int length = BinaryPrimitives.ReadInt32LittleEndian(batch[offset..]);
            offset += 4;
            if (length < 0 || length > batch.Length - offset) { valid = false; break; }
            offset += length;
        }
        if (!valid)
        {
            peer.Malformed = true;
            peer.Inner.Disconnect();
            RaiseDisconnected(peer, DisconnectReason.ProtocolError);
            return;
        }
        offset = 0;
        while (offset < batch.Length && peer.IsConnected)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(batch[offset..]);
            offset += 4;
            RaiseData(peer, batch.Slice(offset, length), delivery);
            offset += length;
        }
    }

    protected override void OnReceiveQueueOverflow(Connection connection)
    {
        ((BatchedConnection)connection).Inner.Disconnect();
        RaiseDisconnected(connection, DisconnectReason.ResourceLimit);
    }

    public override void Flush()
    {
        ThrowIfDisposed();
        foreach (BatchedConnection peer in _peers.Values)
        {
            peer.Flush(Delivery.Reliable);
            peer.Flush(Delivery.Unreliable);
        }
        _inner.Flush();
        PollInner(deliverData: true);
    }

    public override void Shutdown()
    {
        _inner.Shutdown();
        PollInner(deliverData: false);
        foreach (BatchedConnection peer in _peers.Values)
        {
            peer.Release();
            RaiseDisconnected(peer, DisconnectReason.Requested);
        }
        _peers.Clear();
    }

    protected override void DisposeCore() => _inner.Dispose();

    private sealed class BatchedConnection : Connection
    {
        private readonly BatchingTransport _owner;
        private readonly NetWriter _reliable = new(), _unreliable = new();
        public readonly Connection Inner;
        public bool Malformed;

        public BatchedConnection(BatchingTransport owner, Connection inner, int id)
            : base(owner, id, inner.Address, inner.IsOutgoing) { _owner = owner; Inner = inner; }
        public override TimeSpan RoundTripTime => Inner.RoundTripTime;
        public override ConnectionStats Stats => Inner.Stats;
        public override int GetMaxMessageSize(Delivery delivery) => Math.Max(0, Inner.GetMaxMessageSize(delivery) - 4);
        public override int GetBatchThreshold(Delivery delivery) => Math.Max(0, Math.Min(_owner._batchSize, Inner.GetBatchThreshold(delivery)) - 4);
        public override void Disconnect() => Inner.Disconnect();
        public override void Send(ReadOnlySpan<byte> data, Delivery delivery)
        {
            ValidateSend(data, delivery);
            NetWriter batch = delivery == Delivery.Reliable ? _reliable : _unreliable;
            // Stay near a single datagram even when reliable messages may be much larger.
            int threshold = Math.Min(_owner._batchSize, Inner.GetBatchThreshold(delivery));
            threshold = Math.Min(threshold, Inner.GetMaxMessageSize(delivery));
            if (batch.Length != 0 && data.Length + 4 > threshold - batch.Length) Flush(delivery);
            if (!Inner.IsConnected) return;
            batch.WriteInt32(data.Length);
            batch.WriteBytes(data);
            if (batch.Length >= threshold) Flush(delivery);
        }

        public void Flush(Delivery delivery)
        {
            NetWriter batch = delivery == Delivery.Reliable ? _reliable : _unreliable;
            if (batch.Length == 0) return;
            try { if (Inner.IsConnected) Inner.Send(batch.WrittenSpan, delivery); }
            finally { batch.Reset(); }
        }
        public void Release() { _reliable.Dispose(); _unreliable.Dispose(); }
    }
}
