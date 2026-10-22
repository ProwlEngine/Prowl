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

    public bool TrySend(Connection connection, ReadOnlySpan<byte> message, Delivery delivery, SendFlags flags)
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

        NetWriter writer = pending.Get(delivery);
        if (writer.Length != 0 && message.Length + 4 > threshold - writer.Length)
        {
            Flush(connection, delivery);
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
            // Match Connection.Flush: submit earlier sends on both channels before flushing I/O.
            Flush(connection, Delivery.Reliable);
            Flush(connection, Delivery.Unreliable);
            connection.Flush();
        }
        else if (writer.Length >= threshold)
        {
            Flush(connection, delivery);
        }

        return true;
    }

    public void Flush(Connection connection, Delivery delivery)
    {
        if (!_batches.TryGetValue(connection, out OutgoingBatch? pending))
        {
            return;
        }

        NetWriter? writer = delivery == Delivery.Reliable ? pending.Reliable : pending.Unreliable;
        if (writer == null || writer.Length == 0)
        {
            return;
        }

        try
        {
            if (connection.IsConnected)
            {
                connection.Send(writer.WrittenSpan, delivery);
            }
        }
        finally
        {
            writer.Reset();
        }
    }

    public void FlushAll()
    {
        foreach (Connection connection in _batches.Keys)
        {
            Flush(connection, Delivery.Reliable);
            Flush(connection, Delivery.Unreliable);
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
        public NetWriter? Reliable, Unreliable;

        public void Dispose()
        {
            Reliable?.Dispose();
            Unreliable?.Dispose();
        }

        public NetWriter Get(Delivery delivery) => delivery == Delivery.Reliable
            ? Reliable ??= new NetWriter()
            : Unreliable ??= new NetWriter();
    }
}
