// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;

namespace Prowl.Aperture;

/// <summary>
/// Recognises a container from its leading bytes. Detection never trusts a file extension when
/// the content says otherwise, because the extension is the one part of an untrusted file that
/// costs an attacker nothing to lie about.
/// </summary>
public static class FormatDetector
{
    /// <summary>
    /// Bytes needed to place every format that has a leading signature. The ISO base media
    /// brands sit furthest in, at offsets 4 through 12.
    /// </summary>
    public const int MaxSignatureLength = 32;

    /// <summary>
    /// Identifies the container from its content alone. TGA has no leading signature, so it is
    /// only recognised here when the Truevision footer is present; use the overload taking a
    /// file name to allow the extension to break that tie.
    /// </summary>
    public static ImageFormat Detect(ReadOnlySpan<byte> data)
    {
        ImageFormat signature = DetectSignature(data);
        if (signature != ImageFormat.Unknown)
            return signature;

        return HasTgaFooter(data) ? ImageFormat.Tga : ImageFormat.Unknown;
    }

    /// <summary>
    /// Identifies the container, falling back to a TGA header check when the content carries no
    /// signature and <paramref name="fileName"/> claims a TGA extension.
    /// </summary>
    public static ImageFormat Detect(ReadOnlySpan<byte> data, string? fileName)
    {
        ImageFormat detected = Detect(data);
        if (detected != ImageFormat.Unknown)
            return detected;

        if (fileName is not null && FromExtension(fileName) == ImageFormat.Tga && LooksLikeTgaHeader(data))
            return ImageFormat.Tga;

        return ImageFormat.Unknown;
    }

    /// <summary>
    /// Maps a file name or bare extension to the format it claims to be. Only a hint: nothing in
    /// the library decodes on the strength of an extension alone.
    /// </summary>
    public static ImageFormat FromExtension(string pathOrExtension)
    {
        ArgumentNullException.ThrowIfNull(pathOrExtension);
        string ext = Path.GetExtension(pathOrExtension);
        if (ext.Length == 0)
            ext = pathOrExtension.StartsWith('.') ? pathOrExtension : "." + pathOrExtension;

        return ext.ToLowerInvariant() switch
        {
            ".bmp" or ".dib" => ImageFormat.Bmp,
            ".dds" => ImageFormat.Dds,
            ".exr" => ImageFormat.Exr,
            ".gif" => ImageFormat.Gif,
            ".hdr" or ".pic" or ".rgbe" => ImageFormat.Hdr,
            ".ico" or ".cur" => ImageFormat.Ico,
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" or ".jfi" => ImageFormat.Jpeg,
            ".png" or ".apng" => ImageFormat.Png,
            ".pnm" or ".pbm" or ".pgm" or ".ppm" or ".pam" or ".pfm" => ImageFormat.Pnm,
            ".tga" or ".icb" or ".vda" or ".vst" or ".tpic" => ImageFormat.Tga,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            ".webp" => ImageFormat.Webp,
            ".psd" or ".psb" => ImageFormat.Psd,
            ".dng" or ".cr2" or ".cr3" or ".nef" or ".nrw" or ".arw" or ".srf" or ".sr2"
                or ".orf" or ".rw2" or ".raf" or ".pef" or ".srw" or ".raw" or ".3fr"
                or ".erf" or ".mrw" or ".x3f" or ".iiq" => ImageFormat.Raw,
            _ => ImageFormat.Unknown,
        };
    }

    /// <summary>Whether the data ends with the Truevision TGA 2.0 footer.</summary>
    public static bool HasTgaFooter(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> signature = "TRUEVISION-XFILE."u8;
        return data.Length >= 18 + signature.Length &&
               data[^(signature.Length + 1)..^1].SequenceEqual(signature) &&
               data[^1] == 0;
    }

    // ---- Signature table -------------------------------------------------------------

    private static ImageFormat DetectSignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return ImageFormat.Unknown;

        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return ImageFormat.Png;

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ImageFormat.Jpeg;

        if (data.Length >= 6 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
            return ImageFormat.Gif;

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return ImageFormat.Webp;

        if (data.Length >= 4 && data[..4].SequenceEqual("8BPS"u8))
            return ImageFormat.Psd;

        if (data.Length >= 4 && data[..4].SequenceEqual("DDS "u8))
            return ImageFormat.Dds;

        if (data.Length >= 4 && data[0] == 0x76 && data[1] == 0x2F && data[2] == 0x31 && data[3] == 0x01)
            return ImageFormat.Exr;

        if (data.Length >= 15 && data[..15].SequenceEqual("FUJIFILMCCD-RAW"u8))
            return ImageFormat.Raw;

        if (IsRadianceHdr(data))
            return ImageFormat.Hdr;

        if (IsIco(data))
            return ImageFormat.Ico;

        if (IsPnm(data))
            return ImageFormat.Pnm;

        if (IsBmp(data))
            return ImageFormat.Bmp;

        ImageFormat isoBmff = DetectIsoBmff(data);
        if (isoBmff != ImageFormat.Unknown)
            return isoBmff;

        return DetectTiffFamily(data);
    }

    private static bool IsRadianceHdr(ReadOnlySpan<byte> data)
    {
        // "#?RADIANCE" is usual but any program name is legal, and requiring the FORMAT= line
        // would mean scanning an unbounded header.
        return data.Length >= 2 && data[0] == (byte)'#' && data[1] == (byte)'?';
    }

    private static bool IsIco(ReadOnlySpan<byte> data)
    {
        // Reserved zero, type 1 for an icon and 2 for a cursor, and a non-zero entry count,
        // without which this matches far too much.
        if (data.Length < 6 || data[0] != 0 || data[1] != 0 || data[3] != 0)
            return false;
        if (data[2] is not (1 or 2))
            return false;
        return BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 0;
    }

    private static bool IsPnm(ReadOnlySpan<byte> data)
    {
        // "P1".."P7" and whitespace, which keeps text files starting with a P out.
        if (data.Length < 3 || data[0] != (byte)'P')
            return false;
        if (data[1] is < (byte)'1' or > (byte)'7')
            return false;
        return data[2] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'#';
    }

    private static bool IsBmp(ReadOnlySpan<byte> data)
    {
        // A two byte signature is weak, so the declared file size is checked too.
        if (data.Length < 14 || data[0] != (byte)'B' || data[1] != (byte)'M')
            return false;
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(data[2..]);
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        return declared >= 14 && offset >= 14;
    }

    private static ImageFormat DetectIsoBmff(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[4..8].SequenceEqual("ftyp"u8))
            return ImageFormat.Unknown;

        // The one container here built on the ISO base media format.
        ReadOnlySpan<byte> brand = data[8..12];
        if (brand.SequenceEqual("crx "u8))
            return ImageFormat.Raw;

        return ImageFormat.Unknown;
    }

    private static ImageFormat DetectTiffFamily(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            return ImageFormat.Unknown;

        // Two makers replace the TIFF version word, so they come before the generic check.
        if (data[..4].SequenceEqual("IIRO"u8) || data[..4].SequenceEqual("IIRS"u8) ||
            data[..4].SequenceEqual("MMOR"u8))
            return ImageFormat.Raw;

        if (data[0] == 'I' && data[1] == 'I' && data[2] == 0x55 && data[3] == 0x00)
            return ImageFormat.Raw;

        bool little = data[0] == 'I' && data[1] == 'I';
        bool big = data[0] == 'M' && data[1] == 'M';
        if (!little && !big)
            return ImageFormat.Unknown;

        ushort version = little
            ? BinaryPrimitives.ReadUInt16LittleEndian(data[2..])
            : BinaryPrimitives.ReadUInt16BigEndian(data[2..]);

        // 42 is classic TIFF, 43 is BigTIFF.
        if (version is not (42 or 43))
            return ImageFormat.Unknown;

        // Canon stamps "CR" and a version at offset 8 of an otherwise ordinary TIFF.
        if (version == 42 && data.Length >= 10 && data[8] == (byte)'C' && data[9] == (byte)'R')
            return ImageFormat.Raw;

        return version == 42 && TiffLooksLikeRaw(data, little) ? ImageFormat.Raw : ImageFormat.Tiff;
    }

    /// <summary>
    /// Walks IFD0 looking for the tags that mark a file as camera raw rather than an ordinary
    /// TIFF: the DNG version, a colour filter array pattern, or a camera maker string. Bounded
    /// to the buffer it is given, so a truncated or hostile file just falls through to TIFF.
    /// </summary>
    private static bool TiffLooksLikeRaw(ReadOnlySpan<byte> data, bool little)
    {
        const int TagMake = 0x010F;
        const int TagCfaPattern = 0x828E;
        const int TagDngVersion = 0xC612;
        const int TagDngBackwardVersion = 0xC613;

        uint ifdOffset = ReadU32(data, 4, little);
        // Widened deliberately: 0xFFFFFFFF plus two wraps to one in 32 bit arithmetic.
        if (ifdOffset < 8 || (long)ifdOffset + 2 > data.Length)
            return false;

        int entryCount = ReadU16(data, (int)ifdOffset, little);
        int entriesStart = (int)ifdOffset + 2;
        if (entryCount <= 0 || entriesStart + entryCount * 12 > data.Length)
            entryCount = Math.Max(0, (data.Length - entriesStart) / 12);

        for (int i = 0; i < entryCount; i++)
        {
            int entry = entriesStart + i * 12;
            int tag = ReadU16(data, entry, little);
            if (tag is TagDngVersion or TagDngBackwardVersion or TagCfaPattern)
                return true;

            if (tag == TagMake)
            {
                uint count = ReadU32(data, entry + 4, little);
                uint valueOffset = ReadU32(data, entry + 8, little);
                if (count is > 4 and < 64 && (long)valueOffset + count <= data.Length)
                {
                    string make = Encoding.ASCII.GetString(data.Slice((int)valueOffset, (int)count)).TrimEnd('\0');
                    if (IsRawCameraMake(make))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool IsRawCameraMake(string make) =>
        make.StartsWith("NIKON", StringComparison.OrdinalIgnoreCase) ||
        make.StartsWith("SONY", StringComparison.OrdinalIgnoreCase) ||
        make.StartsWith("PENTAX", StringComparison.OrdinalIgnoreCase) ||
        make.StartsWith("SAMSUNG", StringComparison.OrdinalIgnoreCase) ||
        make.StartsWith("Hasselblad", StringComparison.OrdinalIgnoreCase) ||
        make.StartsWith("Leaf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Validates the fixed 18 byte TGA header. Used only as a last resort, because every field
    /// is small enough that unrelated data passes now and then.
    /// </summary>
    internal static bool LooksLikeTgaHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 18)
            return false;

        byte colorMapType = data[1];
        if (colorMapType > 1)
            return false;

        byte imageType = data[2];
        if (imageType is not (0 or 1 or 2 or 3 or 9 or 10 or 11 or 32 or 33))
            return false;

        byte colorMapEntrySize = data[7];
        if (colorMapEntrySize is not (0 or 15 or 16 or 24 or 32))
            return false;

        byte depth = data[16];
        if (depth is not (1 or 8 or 15 or 16 or 24 or 32))
            return false;

        // Bits 6 and 7 of the descriptor are reserved and zero in TGA 2.0.
        if ((data[17] & 0xC0) != 0)
            return false;

        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        return width > 0 && height > 0;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data[offset..])
               : BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..])
               : BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
}
