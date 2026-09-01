// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Psd;

/// <summary>
/// The flattened picture a document carries after its layers, and where it starts. The colour mode
/// data, image resources and layer information in front of it only have to be measured, not read,
/// which is what makes the composite far cheaper to reach than the layers behind it.
/// </summary>
internal struct PsdComposite
{
    public int Width;
    public int Height;
    public int Channels;
    public int Depth;
    public int ColorMode;

    /// <summary>Two is the large document form, which widens two of the length fields to eight bytes.</summary>
    public bool Large;

    /// <summary>Zero is raw, one is the run length form. Two and three are deflate.</summary>
    public int Compression;

    public int DataOffset;

    /// <summary>The palette an indexed or duotone document stores before anything else.</summary>
    public byte[]? Palette;

    public readonly bool IsRunLength => Compression == 1;

    /// <summary>Bytes one row of one channel occupies once unpacked.</summary>
    public readonly int RowBytes => Depth == 1 ? (Width + 7) / 8 : Width * (Depth / 8);

    public static bool TryRead(ReadOnlySpan<byte> data, out PsdComposite composite, out ApertureError error)
    {
        composite = default;

        if (data.Length < 26 || !data[..4].SequenceEqual("8BPS"u8))
        {
            error = data.Length < 26 ? ApertureError.UnexpectedEndOfData : ApertureError.InvalidHeader;
            return false;
        }

        int version = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        if (version is not (1 or 2))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        composite.Large = version == 2;
        composite.Channels = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
        composite.Height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[14..]);
        composite.Width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[18..]);
        composite.Depth = BinaryPrimitives.ReadUInt16BigEndian(data[22..]);
        composite.ColorMode = BinaryPrimitives.ReadUInt16BigEndian(data[24..]);

        if (composite.Width <= 0 || composite.Height <= 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        if (composite.Channels is < 1 or > 56 || composite.Depth is not (1 or 8 or 16 or 32))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        int at = 26;
        if (!TrySkipSection(data, ref at, 4, out ReadOnlySpan<byte> colourData) ||
            !TrySkipSection(data, ref at, 4, out _) ||
            !TrySkipSection(data, ref at, composite.Large ? 8 : 4, out _))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (composite.ColorMode is 2 or 8 && colourData.Length >= 768)
            composite.Palette = colourData[..768].ToArray();

        if (at + 2 > data.Length)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        composite.Compression = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
        composite.DataOffset = at + 2;

        error = ApertureError.None;
        return true;
    }

    /// <summary>Steps over a length prefixed section, handing back what it held.</summary>
    private static bool TrySkipSection(ReadOnlySpan<byte> data, ref int at, int lengthBytes,
                                       out ReadOnlySpan<byte> content)
    {
        content = default;
        if (at + lengthBytes > data.Length)
            return false;

        long length = lengthBytes == 8
            ? (long)BinaryPrimitives.ReadUInt64BigEndian(data[at..])
            : BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

        at += lengthBytes;
        if (length < 0 || at + length > data.Length)
            return false;

        content = data.Slice(at, (int)length);
        at += (int)length;
        return true;
    }
}
