using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Prowl.Warp;

/// <summary>Session message framing and bounded per-connection send buffers. Used only on the session thread.</summary>
internal sealed class SessionBatcher(int maxMessageSize) : IDisposable
{
    internal const ushort MessageId = NetSession.ReservedMessageIds + 6;
    private const int MaxBatchSize = 65536;
    private readonly Dictionary<Connection, OutgoingBatch> _batches = new();
    private readonly int _maxMessageSize = maxMessageSize;

    public void Dispose()
    {
        foreach (OutgoingBatch batch in _batches.Values)
        {
            batch.Dispose();
        }

        _batches.Clear();
    }

    public bool TrySend(Connection connection, ReadOnlySpan<byte> message, Delivery delivery, SendFlags flags,
        byte channel = 0)
    {
        int threshold = Math.Min(MaxBatchSize, Math.Min(_maxMessageSize,
            Math.Min(connection.GetBatchThreshold(delivery), connection.GetMaxMessageSize(delivery))));
        // Two bytes identify the batch, then each message has a four byte length.
        if (message.Length > threshold - 6)
        {
            return false;
        }

        if (!_batches.TryGetValue(connection, out OutgoingBatch? pending))
        {
            pending = new OutgoingBatch();
            _batches.Add(connection, pending);
        }

        NetWriter writer = pending.Get(delivery, channel);
        if (writer.Length != 0 && message.Length + 4 > threshold - writer.Length)
        {
            Flush(connection, delivery, channel);
        }

        if (!connection.IsConnected)
        {
            return true;
        }

        if (writer.Length == 0)
        {
            writer.WriteUInt16(MessageId);
        }

        writer.WriteInt32(message.Length);
        writer.WriteBytes(message);
        if ((flags & SendFlags.Immediate) != 0)
        {
            // Match Connection.Flush: submit earlier sends on every channel before flushing I/O.
            FlushConnection(connection);
            connection.Flush();
        }
        else if (writer.Length >= threshold)
        {
            Flush(connection, delivery, channel);
        }

        return true;
    }

    public void Flush(Connection connection, Delivery delivery, byte channel = 0)
    {
        if (_batches.TryGetValue(connection, out OutgoingBatch? pending) &&
            pending.Writers.TryGetValue((delivery, channel), out NetWriter? writer))
        {
            Submit(connection, writer, delivery, channel);
        }
    }

    /// <summary>Submits every pending batch for one connection, reliable ones first.</summary>
    public void FlushConnection(Connection connection)
    {
        if (!_batches.TryGetValue(connection, out OutgoingBatch? pending))
        {
            return;
        }

        foreach (((Delivery delivery, byte channel), NetWriter writer) in pending.Writers)
        {
            if (delivery == Delivery.Reliable)
            {
                Submit(connection, writer, delivery, channel);
            }
        }

        foreach (((Delivery delivery, byte channel), NetWriter writer) in pending.Writers)
        {
            if (delivery != Delivery.Reliable)
            {
                Submit(connection, writer, delivery, channel);
            }
        }
    }

    public void FlushAll()
    {
        foreach (Connection connection in _batches.Keys)
        {
            FlushConnection(connection);
        }
    }

    private static void Submit(Connection connection, NetWriter writer, Delivery delivery, byte channel)
    {
        if (writer.Length == 0)
        {
            return;
        }

        try
        {
            if (connection.IsConnected)
            {
                connection.Send(writer.WrittenSpan, delivery, channel: channel);
            }
        }
        finally
        {
            writer.Reset();
        }
    }

    public void Remove(Connection connection)
    {
        if (_batches.Remove(connection, out OutgoingBatch? batch))
        {
            batch.Dispose();
        }
    }

    public void Validate(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || data.Length > MaxBatchSize - 2)
        {
            throw new InvalidDataException("Invalid message batch size.");
        }

        // Validate all framing before invoking any handlers. Only user messages may appear in a batch.
        int offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 4)
            {
                throw new InvalidDataException("Truncated batch length.");
            }

            int length = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            offset += 4;
            if (length < 2 || length > _maxMessageSize || length > data.Length - offset ||
                BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]) >= NetSession.ReservedMessageIds)
            {
                throw new InvalidDataException("Invalid batched message.");
            }

            offset += length;
        }
    }

    private sealed class OutgoingBatch : IDisposable
    {
        /// <summary>One pending batch per delivery mode and channel, each its own stream on the transport.</summary>
        public readonly Dictionary<(Delivery Delivery, byte Channel), NetWriter> Writers = new();

        public void Dispose()
        {
            foreach (NetWriter writer in Writers.Values)
            {
                writer.Dispose();
            }

            Writers.Clear();
        }

        public NetWriter Get(Delivery delivery, byte channel)
        {
            if (!Writers.TryGetValue((delivery, channel), out NetWriter? writer))
            {
                writer = new NetWriter();
                Writers.Add((delivery, channel), writer);
            }

            return writer;
        }
    }
}
