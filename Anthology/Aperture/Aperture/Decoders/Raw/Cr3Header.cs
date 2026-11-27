// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Raw;

/// <summary>
/// Finds the sensor frame in Canon's newer raw container, which is an ISO base media file whose
/// sensor readings are a track. The frame is read from the sample description as a player would.
/// A file holds the sensor image, a preview and a thumbnail; the largest is the one wanted.
/// </summary>
internal static class Cr3Header
{
    /// <summary>Cap on boxes walked, so a file of empty boxes cannot spin.</summary>
    private const int MaxBoxes = 4096;

    /// <summary>How deep the boxes that matter are nested: moov, trak, mdia, minf, stbl, stsd.</summary>
    private const int MaxDepth = 6;

    public static bool TryRead(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length < 12 || !data[4..8].SequenceEqual("ftyp"u8))
            return false;

        int budget = MaxBoxes;
        Walk(data, 0, ref width, ref height, ref budget);
        return width > 0 && height > 0;
    }

    /// <summary>
    /// Descends the boxes that lead to a sample description, and reads the frame out of every
    /// one it finds. Only the containers on that path are entered; the rest are stepped over.
    /// </summary>
    private static void Walk(ReadOnlySpan<byte> data, int depth, ref int width, ref int height,
                             ref int budget)
    {
        int at = 0;

        while (at + 8 <= data.Length && budget-- > 0)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
            ReadOnlySpan<byte> kind = data.Slice(at + 4, 4);

            // A size of zero means the box runs to the end of the file.
            if (size == 0)
                size = data.Length - at;

            if (size < 8 || at + size > data.Length)
                return;

            ReadOnlySpan<byte> body = data.Slice(at + 8, (int)size - 8);

            if (kind.SequenceEqual("stsd"u8))
                ReadSampleDescriptions(body, ref width, ref height);
            else if (depth < MaxDepth && IsOnThePath(kind))
                Walk(body, depth + 1, ref width, ref height, ref budget);

            at += (int)size;
        }
    }

    private static bool IsOnThePath(ReadOnlySpan<byte> kind) =>
        kind.SequenceEqual("moov"u8) || kind.SequenceEqual("trak"u8) ||
        kind.SequenceEqual("mdia"u8) || kind.SequenceEqual("minf"u8) ||
        kind.SequenceEqual("stbl"u8);

    /// <summary>
    /// Reads the frame each sample description names, keeping the largest. The entries are laid
    /// out as a visual sample is: six reserved bytes, the data reference, sixteen more the format
    /// does not use here, and then the width and the height.
    /// </summary>
    private static void ReadSampleDescriptions(ReadOnlySpan<byte> body, ref int width, ref int height)
    {
        // A version byte, three of flags, then the number of entries.
        if (body.Length < 8)
            return;

        uint entries = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
        int at = 8;

        for (uint i = 0; i < entries && at + 8 <= body.Length; i++)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(body[at..]);
            if (size < 36 || at + size > body.Length)
                return;

            int entryWidth = BinaryPrimitives.ReadUInt16BigEndian(body[(at + 32)..]);
            int entryHeight = BinaryPrimitives.ReadUInt16BigEndian(body[(at + 34)..]);

            if ((long)entryWidth * entryHeight > (long)width * height)
            {
                width = entryWidth;
                height = entryHeight;
            }

            at += (int)size;
        }
    }
}
