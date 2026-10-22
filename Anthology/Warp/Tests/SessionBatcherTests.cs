using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Xunit;

namespace Prowl.Warp.Tests;

public class SessionBatcherTests
{
    [Theory]
    [InlineData(Delivery.Reliable)]
    [InlineData(Delivery.Unreliable)]
    public void BuffersCopiesAndPreservesMessageBoundaries(Delivery delivery)
    {
        using Backend backend = new();
        using SessionBatcher batcher = new(4096);
        byte[] message = { 42, 0, 1 };
        Assert.True(batcher.TrySend(backend.Peer, message, delivery, SendFlags.None));
        message[2] = 2;
        Assert.True(batcher.TrySend(backend.Peer, message, delivery, SendFlags.None));
        message[2] = 99;
        Assert.Empty(backend.Sends);
        batcher.FlushAll();
        (byte[] Data, Delivery Delivery) packet = Assert.Single(backend.Sends);
        Assert.Equal(delivery, packet.Delivery);
        batcher.Validate(packet.Data.AsSpan(2));
        Assert.Equal(new[] { new byte[] { 42, 0, 1 }, new byte[] { 42, 0, 2 } }, Decode(packet.Data));
        batcher.FlushAll();
        Assert.Single(backend.Sends);
    }

    [Theory]
    [InlineData(24, 100, 100)]
    [InlineData(100, 24, 100)]
    [InlineData(100, 100, 24)]
    public void FullBatchesRespectBackendAndSessionLimits(int threshold, int maximum, int sessionMaximum)
    {
        using Backend backend = new() { Threshold = threshold, Maximum = maximum };
        using SessionBatcher batcher = new(sessionMaximum);
        byte[] message = { 42, 0, 1, 2, 3, 4, 5 }; // 11 bytes with framing, two fit exactly in 24.
        for (int i = 0; i < 5; i++)
        {
            Assert.True(batcher.TrySend(backend.Peer, message, Delivery.Reliable, SendFlags.None));
        }

        Assert.Equal(2, backend.Sends.Count);
        batcher.FlushAll();
        Assert.Equal(new[] { 24, 24, 13 }, backend.Sends.Select(s => s.Data.Length));
        Assert.Equal(5, backend.Sends.Sum(s => Decode(s.Data).Length));
    }

    [Fact]
    public void FlushesBeforeNextMessageWouldOverflowAndDeclinesOversizedMessages()
    {
        using Backend backend = new() { Threshold = 16 };
        using SessionBatcher batcher = new(4096);
        byte[] message = { 42, 0, 1, 2 }; // Ten bytes for the first, eight for the next.
        batcher.TrySend(backend.Peer, message, Delivery.Reliable, SendFlags.None);
        batcher.TrySend(backend.Peer, message, Delivery.Reliable, SendFlags.None);
        Assert.Single(backend.Sends);
        Assert.False(batcher.TrySend(backend.Peer, new byte[11], Delivery.Reliable, SendFlags.None));
        batcher.FlushAll();
        Assert.Equal(2, backend.Sends.Count);
        Assert.All(backend.Sends, s => Assert.Equal(message, Assert.Single(Decode(s.Data))));
    }

    [Fact]
    public void ImmediateSubmitsBothChannelsBeforeFlushingConnection()
    {
        using Backend backend = new();
        using SessionBatcher batcher = new(4096);
        batcher.TrySend(backend.Peer, new byte[] { 42, 0, 1 }, Delivery.Reliable, SendFlags.None);
        batcher.TrySend(backend.Peer, new byte[] { 42, 0, 2 }, Delivery.Unreliable, SendFlags.None);
        batcher.TrySend(backend.Peer, new byte[] { 42, 0, 3 }, Delivery.Reliable, SendFlags.Immediate);
        Assert.Equal(new[] { Delivery.Reliable, Delivery.Unreliable }, backend.Sends.Select(s => s.Delivery));
        Assert.Equal(new byte[] { 1, 3 }, Decode(backend.Sends[0].Data).Select(m => m[2]));
        Assert.Equal(1, backend.ConnectionFlushes);
        Assert.Equal(2, backend.SendsAtFlush);
        Assert.Equal(0, backend.TransportFlushes);
    }

    [Fact]
    public void ConnectionsHaveIndependentBuffersAndRemovalDiscardsPendingMessages()
    {
        using Backend first = new(), second = new();
        using SessionBatcher batcher = new(4096);
        batcher.TrySend(first.Peer, new byte[] { 42, 0 }, Delivery.Reliable, SendFlags.None);
        batcher.TrySend(second.Peer, new byte[] { 43, 0 }, Delivery.Reliable, SendFlags.None);
        batcher.Remove(first.Peer);
        batcher.Remove(first.Peer);
        batcher.FlushAll();
        Assert.Empty(first.Sends);
        Assert.Single(second.Sends);
        batcher.TrySend(second.Peer, new byte[] { 44, 0 }, Delivery.Reliable, SendFlags.None);
        batcher.Dispose();
        batcher.FlushAll();
        Assert.Single(second.Sends);
    }

    [Fact]
    public void DisconnectedPeersDropPendingMessages()
    {
        using Backend backend = new();
        using SessionBatcher batcher = new(4096);
        batcher.TrySend(backend.Peer, new byte[] { 42, 0 }, Delivery.Reliable, SendFlags.None);
        backend.Peer.Disconnect();
        batcher.FlushAll();
        Assert.Empty(backend.Sends);
    }

    [Fact]
    public void FailedSendClearsItsBufferAndLaterMessagesCanBeSent()
    {
        using Backend backend = new();
        using SessionBatcher batcher = new(4096);
        batcher.TrySend(backend.Peer, new byte[] { 42, 0 }, Delivery.Reliable, SendFlags.None);
        backend.ThrowOnSend = true;
        Assert.Throws<IOException>(batcher.FlushAll);
        backend.ThrowOnSend = false;
        batcher.TrySend(backend.Peer, new byte[] { 43, 0 }, Delivery.Reliable, SendFlags.None);
        batcher.FlushAll();
        Assert.Equal(new byte[] { 43, 0 }, Assert.Single(Decode(Assert.Single(backend.Sends).Data)));
    }

    [Fact]
    public void ReliableOnlyBackendNeverQueriesUnreliableLimits()
    {
        using Backend backend = new() { ReliableOnly = true };
        using SessionBatcher batcher = new(4096);
        batcher.TrySend(backend.Peer, new byte[] { 42, 0 }, Delivery.Reliable, SendFlags.None);
        batcher.FlushAll();
        Assert.Single(backend.Sends);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void RejectsInvalidLengths(int length)
    {
        using SessionBatcher batcher = new(4096);
        using NetWriter malformed = new();
        malformed.WriteInt32(length);
        malformed.WriteUInt16(42);
        Assert.Throws<InvalidDataException>(() => batcher.Validate(malformed.WrittenSpan));
    }

    [Theory]
    [InlineData(NetSession.ReservedMessageIds)]
    [InlineData(SessionBatcher.MessageId)]
    [InlineData(NetSession.ReservedMessageIds + 5)]
    public void RejectsControlMessagesNestedBatchesAndFragments(int id)
    {
        using SessionBatcher batcher = new(4096);
        using NetWriter malformed = new();
        malformed.WriteInt32(2);
        malformed.WriteUInt16((ushort)id);
        Assert.Throws<InvalidDataException>(() => batcher.Validate(malformed.WrittenSpan));
    }

    [Fact]
    public void RejectsEmptyTruncatedAndOversizedBatches()
    {
        using SessionBatcher batcher = new(64);
        Assert.Throws<InvalidDataException>(() => batcher.Validate(Array.Empty<byte>()));
        Assert.Throws<InvalidDataException>(() => batcher.Validate(new byte[3]));
        Assert.Throws<InvalidDataException>(() => batcher.Validate(new byte[65535]));
        using NetWriter malformed = new();
        malformed.WriteInt32(65);
        malformed.WriteBytes(new byte[65]);
        Assert.Throws<InvalidDataException>(() => batcher.Validate(malformed.WrittenSpan));
        malformed.Reset();
        malformed.WriteInt32(2);
        malformed.WriteUInt16(42);
        malformed.WriteByte(1); // Complete first record followed by truncated framing.
        Assert.Throws<InvalidDataException>(() => batcher.Validate(malformed.WrittenSpan));
    }

    private static byte[][] Decode(byte[] data)
    {
        NetReader reader = new(data);
        Assert.Equal(SessionBatcher.MessageId, reader.ReadUInt16());
        List<byte[]> messages = new();
        while (reader.Remaining > 0)
        {
            int length = reader.ReadInt32();
            messages.Add(reader.ReadBytes(length).ToArray());
        }

        return messages.ToArray();
    }

    private sealed class Backend : Transport
    {
        public readonly Connection Peer;
        public readonly List<(byte[] Data, Delivery Delivery)> Sends = new();
        public int ConnectionFlushes, TransportFlushes, SendsAtFlush;
        public bool ReliableOnly, ThrowOnSend;
        public int Threshold = 128, Maximum = 4096;
        public Backend() => Peer = new RecordingConnection(this);
        public override bool IsListening => false;

        public override void Listen()
        {
        }

        public override Connection Connect(string address) => Peer;

        public override void Receive()
        {
        }

        public override void Flush() => TransportFlushes++;

        public override void Shutdown()
        {
        }

        private sealed class RecordingConnection : Connection
        {
            private readonly Backend _owner;

            public RecordingConnection(Backend owner) : base(owner, 1, "peer", true)
            {
                _owner = owner;
                State = ConnectionState.Connected;
            }

            public override TimeSpan RoundTripTime => default;

            public override int GetMaxMessageSize(Delivery delivery) =>
                delivery == Delivery.Unreliable && _owner.ReliableOnly
                    ? throw new NotSupportedException()
                    : _owner.Maximum;

            public override int GetBatchThreshold(Delivery delivery)
            {
                GetMaxMessageSize(delivery);
                return _owner.Threshold;
            }

            public override void Send(ReadOnlySpan<byte> data, Delivery delivery)
            {
                ValidateSend(data, delivery);
                if (_owner.ThrowOnSend)
                {
                    throw new IOException("Send failed");
                }

                _owner.Sends.Add((data.ToArray(), delivery));
            }

            public override void Flush()
            {
                _owner.ConnectionFlushes++;
                _owner.SendsAtFlush = _owner.Sends.Count;
            }

            public override void Disconnect() => State = ConnectionState.Disconnected;
        }
    }
}
