using System;
using System.IO;
using System.IO.Compression;

using Prowl.Echo;
using Prowl.Warp;

namespace Prowl.Runtime.Networking.Serialization;

/// <summary>Bounds compressed scene/spawn decoding before Echo allocates or recursively reads the tree.</summary>
internal static class SnapshotDecoder
{
    internal static EchoObject Decode(ReadOnlySpan<byte> compressed, NetworkConfiguration limits)
    {
        using MemoryStream input = new(compressed.ToArray());
        using ZLibStream zlib = new(input, CompressionMode.Decompress);
        using MemoryStream decoded = new(Math.Min(limits.MaxSnapshotDecompressedBytes, 8192));
        Span<byte> buffer = stackalloc byte[8192];
        int read;
        // Read at most one byte beyond the budget, so even a compression bomb stops promptly.
        while ((read = zlib.Read(
                   buffer[..(int)Math.Min(buffer.Length, limits.MaxSnapshotDecompressedBytes - decoded.Length + 1)])) !=
               0)
        {
            if (read > limits.MaxSnapshotDecompressedBytes - decoded.Length)
            {
                throw new InvalidDataException("Snapshot decompressed byte limit exceeded.");
            }

            decoded.Write(buffer[..read]);
        }

        NetReader reader = new(decoded.GetBuffer().AsSpan(0, (int)decoded.Length));
        int remainingNodes = limits.MaxSnapshotNodes;
        ValidateValue(ref reader, 1, limits.MaxSnapshotDepth, ref remainingNodes);
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Snapshot contains trailing Echo bytes.");
        }

        // Echo's general-purpose reader trusts lengths and container counts. Only give it a
        // completely validated tree, with fixed encoding and bounded recursion/allocation.
        decoded.Position = 0;
        using BinaryReader binary = new(decoded);
        return EchoObject.ReadFromBinary(binary,
            new BinarySerializationOptions { EncodingMode = BinaryEncodingMode.Performance });
    }

    private static void ValidateValue(ref NetReader reader, int depth, int maxDepth, ref int remainingNodes)
    {
        if (depth > maxDepth)
        {
            throw new InvalidDataException("Snapshot nesting depth limit exceeded.");
        }

        if (remainingNodes-- <= 0)
        {
            throw new InvalidDataException("Snapshot node limit exceeded.");
        }

        EchoType type = (EchoType)reader.ReadByte();
        switch (type)
        {
            case EchoType.Null: return;
            case EchoType.Byte:
            case EchoType.sByte:
            case EchoType.Bool:
                reader.ReadBytes(1);
                return;
            case EchoType.Short:
            case EchoType.UShort:
                reader.ReadBytes(2);
                return;
            case EchoType.Int:
            case EchoType.UInt:
            case EchoType.Float:
                reader.ReadBytes(4);
                return;
            case EchoType.Long:
            case EchoType.ULong:
            case EchoType.Double:
                reader.ReadBytes(8);
                return;
            case EchoType.Decimal:
                reader.ReadBytes(16);
                return;
            case EchoType.String:
            case EchoType.ByteArray:
                ValidateBytes(ref reader);
                return;
            case EchoType.List:
            case EchoType.Compound:
                int count = reader.ReadInt32();
                if (count < 0 || count > remainingNodes)
                {
                    throw new InvalidDataException("Snapshot container exceeds the node limit.");
                }

                // Each list value needs a type byte; each compound value also needs a key length.
                int minimumBytes = type == EchoType.Compound ? 5 : 1;
                if (count > reader.Remaining / minimumBytes)
                {
                    throw new InvalidDataException("Snapshot container count exceeds the remaining data.");
                }

                for (int i = 0; i < count; i++)
                {
                    if (type == EchoType.Compound)
                    {
                        ValidateBytes(ref reader);
                    }

                    ValidateValue(ref reader, depth + 1, maxDepth, ref remainingNodes);
                }

                return;
            default: throw new InvalidDataException($"Unknown snapshot Echo type {(byte)type}.");
        }
    }

    private static void ValidateBytes(ref NetReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > reader.Remaining)
        {
            throw new InvalidDataException("Snapshot byte/string length exceeds the remaining data.");
        }

        reader.ReadBytes(length);
    }
}
