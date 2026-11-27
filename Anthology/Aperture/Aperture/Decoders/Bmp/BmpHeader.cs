// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Bmp;

/// <summary>
/// Everything the file says about itself before the pixels start. The format grew by appending
/// to one header seven times over, so the size of that header is what decides which fields are
/// present, and a reader has to treat the size as the only reliable thing in it.
/// </summary>
internal struct BmpHeader
{
    public const int FileHeaderSize = 14;

    public int Width;
    public int Height;

    /// <summary>Rows run down the image rather than up, which a negative height declares.</summary>
    public bool TopDown;

    public int BitsPerPixel;
    public uint Compression;
    public uint DibSize;

    /// <summary>Where the pixel data starts, as the file header gives it.</summary>
    public int PixelOffset;

    public int PaletteOffset;
    public int PaletteEntries;

    /// <summary>Three bytes on the oldest header, four on every later one.</summary>
    public int PaletteEntrySize;

    public uint RedMask;
    public uint GreenMask;
    public uint BlueMask;
    public uint AlphaMask;

    public double HorizontalDpi;
    public double VerticalDpi;

    public readonly bool IsIndexed => BitsPerPixel <= 8;

    public readonly bool HasAlpha => AlphaMask != 0;

    /// <summary>Bytes a row occupies, rounded up to the four byte boundary rows sit on.</summary>
    public readonly int Stride => ((((Width * BitsPerPixel) + 7) / 8) + 3) & ~3;

    public static bool TryRead(ReadOnlySpan<byte> data, out BmpHeader header, out ApertureError error)
    {
        header = default;

        if (data.Length < 2)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        // The other five signatures wrap a bitmap rather than being one.
        if (data[0] != (byte)'B' || data[1] != (byte)'M')
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (data.Length < FileHeaderSize + 4)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        int pixelOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        return TryReadDib(data, FileHeaderSize, pixelOffset, out header, out error);
    }

    /// <summary>
    /// Reads the header alone, for the containers that carry one without the file header in front
    /// of it. An icon directory entry is such a container, and it has no field to say where its
    /// pixels begin, so a negative offset asks for that to be worked out from the header instead.
    /// </summary>
    public static bool TryReadDib(ReadOnlySpan<byte> data, int start, int pixelOffset,
                                  out BmpHeader header, out ApertureError error)
    {
        header = default;
        error = ApertureError.None;
        header.PixelOffset = pixelOffset;

        if (data.Length < start + 4)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        header.DibSize = BinaryPrimitives.ReadUInt32LittleEndian(data[start..]);

        if (header.DibSize is not (12 or 16 or 40 or 52 or 56 or 64 or 108 or 124))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (data.Length < start + header.DibSize)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        ReadOnlySpan<byte> dib = data.Slice(start, (int)header.DibSize);
        ushort planes;
        uint colorsUsed = 0;

        if (header.DibSize == 12)
        {
            header.Width = BinaryPrimitives.ReadUInt16LittleEndian(dib[4..]);
            header.Height = BinaryPrimitives.ReadUInt16LittleEndian(dib[6..]);
            planes = BinaryPrimitives.ReadUInt16LittleEndian(dib[8..]);
            header.BitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(dib[10..]);
            header.PaletteEntrySize = 3;
        }
        else
        {
            header.Width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
            header.Height = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
            planes = BinaryPrimitives.ReadUInt16LittleEndian(dib[12..]);
            header.BitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
            header.PaletteEntrySize = 4;

            if (header.DibSize >= 40)
            {
                header.Compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
                header.HorizontalDpi = BinaryPrimitives.ReadInt32LittleEndian(dib[24..]) * 0.0254;
                header.VerticalDpi = BinaryPrimitives.ReadInt32LittleEndian(dib[28..]) * 0.0254;
                colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
            }
        }

        if (header.Height < 0)
        {
            header.TopDown = true;
            header.Height = header.Height == int.MinValue ? 0 : -header.Height;
        }

        if (header.Width <= 0 || header.Height <= 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        if (planes != 1)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (header.BitsPerPixel is not (1 or 2 or 4 or 8 or 16 or 24 or 32))
        {
            error = ApertureError.InvalidBitDepth;
            return false;
        }

        if (header.Compression > 6)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        // A run length encoded bitmap is defined bottom up, so a negative height means nothing.
        if (header.Compression is 1 or 2 && header.TopDown)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (header.IsIndexed && colorsUsed > 1u << header.BitsPerPixel)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        // The later headers carry the masks; the 1995 one leaves them before the palette.
        int extra = 0;
        if (header.DibSize >= 52)
        {
            header.RedMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[40..]);
            header.GreenMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[44..]);
            header.BlueMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[48..]);
            if (header.DibSize >= 56)
                header.AlphaMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[52..]);
        }
        else if (header.Compression is 3 or 6)
        {
            int wanted = header.Compression == 3 ? 12 : 16;
            int at = start + (int)header.DibSize;

            // Pixels starting before there is room for the masks means they are not there. The
            // file still names its geometry, so the defaults below stand in rather than refusing.
            bool room = header.PixelOffset <= 0 || header.PixelOffset >= at + wanted;
            if (room && data.Length >= at + wanted)
            {
                extra = wanted;
                header.RedMask = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
                header.GreenMask = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
                header.BlueMask = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 8)..]);
                if (wanted == 16)
                    header.AlphaMask = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 12)..]);
            }
        }

        // Uncompressed colour has implied positions, so a later header's mask fields do not
        // describe it. Sixteen bits is five each with one spare; the rest is the byte order.
        bool implied = header.Compression is 0 or 1 or 2 ||
                       (header.RedMask | header.GreenMask | header.BlueMask) == 0;

        if (implied && !header.IsIndexed)
        {
            if (header.BitsPerPixel == 16)
            {
                header.RedMask = 0x7C00;
                header.GreenMask = 0x03E0;
                header.BlueMask = 0x001F;
            }
            else
            {
                header.RedMask = 0x00FF0000;
                header.GreenMask = 0x0000FF00;
                header.BlueMask = 0x000000FF;
            }
        }

        // Writers leave a full set of masks whatever the depth, so a twenty four bit file
        // routinely claims an alpha channel it has no room for.
        if (!header.IsIndexed && header.BitsPerPixel < 32)
        {
            uint reach = (1u << header.BitsPerPixel) - 1;
            if ((header.RedMask & ~reach) != 0) header.RedMask = 0;
            if ((header.GreenMask & ~reach) != 0) header.GreenMask = 0;
            if ((header.BlueMask & ~reach) != 0) header.BlueMask = 0;
            if ((header.AlphaMask & ~reach) != 0) header.AlphaMask = 0;
        }

        header.PaletteOffset = start + (int)header.DibSize + extra;
        if (header.IsIndexed)
        {
            header.PaletteEntries = colorsUsed != 0 ? (int)colorsUsed : 1 << header.BitsPerPixel;

            // A palette may be declared without room for it, so the pixel offset bounds it.
            int available = header.PixelOffset > header.PaletteOffset
                ? header.PixelOffset - header.PaletteOffset
                : data.Length - header.PaletteOffset;

            // A container with no stated offset gives the palette the room its own count asks for.
            if (header.PixelOffset < 0)
                available = data.Length - header.PaletteOffset;

            header.PaletteEntries = Math.Clamp(
                Math.Min(header.PaletteEntries, Math.Max(0, available) / header.PaletteEntrySize),
                0, 256);
        }

        // A container that gives no offset puts the pixels straight after the palette.
        if (header.PixelOffset < 0)
            header.PixelOffset = header.PaletteOffset + (header.PaletteEntries * header.PaletteEntrySize);

        if (header.PixelOffset < header.PaletteOffset && header.Compression is not (4 or 5))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        return true;
    }
}
