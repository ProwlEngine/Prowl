// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Tga;

/// <summary>The eighteen byte header, plus the two answers only the rest of the file can give.</summary>
internal struct TgaHeader
{
    public const int HeaderSize = 18;

    /// <summary>The marker a version two file ends with, pointing back at its extension area.</summary>
    private static ReadOnlySpan<byte> Footer => "TRUEVISION-XFILE.\0"u8;

    public int Width;
    public int Height;
    public int Kind;
    public int Depth;

    public bool IsPaletted;
    public bool IsGrayscale;
    public bool IsRunLength;

    /// <summary>The first row stored is the top row.</summary>
    public bool TopDown;

    /// <summary>Columns run right to left.</summary>
    public bool RightToLeft;

    /// <summary>Whether the attribute bits carry a channel rather than something undefined.</summary>
    public bool AlphaUsed;

    public int ColorMapFirst;
    public int ColorMapLength;
    public int ColorMapDepth;

    public int ColorMapOffset;
    public int PixelOffset;

    public readonly int BytesPerPixel => (Depth + 7) / 8;

    public static bool TryRead(ReadOnlySpan<byte> data, out TgaHeader header, out ApertureError error)
    {
        header = default;

        if (data.Length < HeaderSize)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        int idLength = data[0];
        int colorMapType = data[1];
        header.Kind = data[2];
        header.ColorMapFirst = BinaryPrimitives.ReadUInt16LittleEndian(data[3..]);
        header.ColorMapLength = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
        header.ColorMapDepth = data[7];
        header.Width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        header.Height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        header.Depth = data[16];

        byte descriptor = data[17];
        header.TopDown = (descriptor & 0x20) != 0;
        header.RightToLeft = (descriptor & 0x10) != 0;
        int attributeBits = descriptor & 0x0F;

        if (colorMapType > 1)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        // The top two bits of the descriptor are reserved and defined to be zero.
        if ((descriptor & 0xC0) != 0)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (header.Kind is not (0 or 1 or 2 or 3 or 9 or 10 or 11 or 32 or 33))
        {
            error = ApertureError.InvalidColorType;
            return false;
        }

        if (header.Kind == 0)
        {
            error = ApertureError.NoImageData;
            return false;
        }

        if (header.Width == 0 || header.Height == 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        if (header.Depth is not (8 or 15 or 16 or 24 or 32))
        {
            error = ApertureError.InvalidBitDepth;
            return false;
        }

        header.IsPaletted = header.Kind is 1 or 9;
        header.IsGrayscale = header.Kind is 3 or 11;
        header.IsRunLength = header.Kind is 9 or 10 or 11 or 32 or 33;

        if (header.IsPaletted)
        {
            if (colorMapType != 1 || header.ColorMapLength == 0 ||
                header.ColorMapDepth is not (15 or 16 or 24 or 32))
            {
                error = ApertureError.InvalidHeader;
                return false;
            }
        }

        header.ColorMapOffset = HeaderSize + idLength;
        long mapBytes = colorMapType == 1
            ? (long)header.ColorMapLength * ((header.ColorMapDepth + 7) / 8)
            : 0;

        long start = header.ColorMapOffset + mapBytes;
        if (start > data.Length)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        header.PixelOffset = (int)start;
        header.AlphaUsed = DecideAlpha(data, header.Depth, attributeBits);

        error = ApertureError.None;
        return true;
    }

    /// <summary>
    /// Whether the attribute bits mean anything. A version two file says so outright in its
    /// extension area: three is useful alpha and four premultiplied, zero none, one and two
    /// undefined. Older files have only the descriptor, which writers routinely leave at zero, so
    /// a thirty two bit pixel is read as carrying alpha whatever it claims.
    /// </summary>
    private static bool DecideAlpha(ReadOnlySpan<byte> data, int depth, int attributeBits)
    {
        if (data.Length >= 26 && data[^18..].SequenceEqual(Footer))
        {
            uint extension = BinaryPrimitives.ReadUInt32LittleEndian(data[^26..]);
            if (extension != 0 && extension + 495 <= (uint)data.Length)
            {
                byte kind = data[(int)extension + 494];
                return kind is 3 or 4;
            }
        }

        return depth == 32 || attributeBits > 0;
    }
}
