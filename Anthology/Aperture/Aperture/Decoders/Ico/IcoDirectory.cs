// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Ico;

/// <summary>One entry in the directory, naming a size and where its image lives.</summary>
internal readonly struct IcoEntry
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Bits { get; init; }
    public required int Offset { get; init; }
    public required int Length { get; init; }
    public required bool IsPng { get; init; }
}

/// <summary>
/// The directory at the front of an icon or cursor file. Every entry is either a complete PNG or
/// a bitmap header with no file header in front of it, so the format is a container rather than a
/// codec, and the interesting part is choosing which entry to hand back.
/// </summary>
internal static class IcoDirectory
{
    public const int HeaderSize = 6;
    public const int EntrySize = 16;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads the directory, dropping entries whose image is not inside the file.</summary>
    public static bool TryRead(ReadOnlySpan<byte> data, out List<IcoEntry> entries, out ApertureError error)
    {
        entries = [];

        if (data.Length < HeaderSize)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(data);
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);

        if (reserved != 0 || type is not (1 or 2))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (count == 0)
        {
            error = ApertureError.NoImageData;
            return false;
        }

        if (HeaderSize + ((long)count * EntrySize) > data.Length)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = data.Slice(HeaderSize + (i * EntrySize), EntrySize);

            // A stored dimension of zero means 256, which does not fit in the byte.
            int width = entry[0] == 0 ? 256 : entry[0];
            int height = entry[1] == 0 ? 256 : entry[1];
            ushort bitsOrHotspot = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);

            if (length == 0 || offset < HeaderSize || offset + (long)length > data.Length)
                continue;

            bool isPng = length >= 8 && data.Slice((int)offset, 8).SequenceEqual(PngSignature);

            // For a cursor those two fields hold the hotspot rather than the plane and bit counts.
            int bits = type == 1 ? bitsOrHotspot : 0;
            if (isPng && bits == 0)
                bits = 32;

            entries.Add(new IcoEntry
            {
                Width = width,
                Height = height,
                Bits = bits,
                Offset = (int)offset,
                Length = (int)length,
                IsPng = isPng,
            });
        }

        if (entries.Count == 0)
        {
            error = ApertureError.NoImageData;
            return false;
        }

        error = ApertureError.None;
        return true;
    }

    /// <summary>
    /// The entry a caller asking for one image wants: the largest, and among equals the one with
    /// the most colour.
    /// </summary>
    public static IcoEntry Best(List<IcoEntry> entries)
    {
        IcoEntry best = entries[0];
        foreach (IcoEntry entry in entries)
        {
            long area = (long)entry.Width * entry.Height;
            long bestArea = (long)best.Width * best.Height;
            if (area > bestArea || (area == bestArea && entry.Bits > best.Bits))
                best = entry;
        }

        return best;
    }
}
