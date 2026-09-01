// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Metadata;

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>What one image directory says about the picture it describes.</summary>
internal sealed class TiffImage
{
    /// <summary>Largest number of samples a pixel may carry before the file is refused.</summary>
    public const int MaxSamples = 8;

    /// <summary>The byte order the file header declared, which every wide sample follows.</summary>
    public bool LittleEndian = true;

    public int Width;
    public int Height;
    public int SamplesPerPixel = 1;
    public int BitsPerSample = 8;

    /// <summary>1 unsigned, 2 two's complement signed, 3 floating point.</summary>
    public int SampleFormat = 1;

    public int Compression = 1;
    public int Photometric = 1;

    /// <summary>1 interleaves the samples of a pixel, 2 stores each channel as its own plane.</summary>
    public int Planar = 1;

    public int Predictor = 1;
    public int FillOrder = 1;
    public int Orientation = 1;

    public int RowsPerStrip;
    public int TileWidth;
    public int TileLength;

    public long[] Offsets = [];
    public long[] Counts = [];

    public ushort[]? Palette;

    /// <summary>How much light each colour channel contributes to luma, for the chroma readings.</summary>
    public double LumaRed = 0.299;
    public double LumaGreen = 0.587;
    public double LumaBlue = 0.114;

    /// <summary>What each of the three channels reads as at its darkest and brightest.</summary>
    public double[] ReferenceBlackWhite = [0, 255, 128, 255, 128, 255];

    /// <summary>How many luma samples share one chroma pair, across and down.</summary>
    public int ChromaAcross = 2;
    public int ChromaDown = 2;

    /// <summary>Whether the chroma is shared, which changes how the samples are laid out.</summary>
    public bool Subsampled => Photometric == 6 && (ChromaAcross > 1 || ChromaDown > 1);

    /// <summary>Whether the samples come from one of the log forms and are already floats.</summary>
    public bool LogLuv;

    /// <summary>The filter grid a sensor image was measured through, and the levels it ran between.</summary>
    public byte[] CfaPattern = [];
    public int CfaAcross;
    public int CfaDown;
    public int BlackLevel;
    public int WhiteLevel;

    /// <summary>Where the pieces of a scattered JPEG stream lie, for the older compression.</summary>
    public long[] QuantisationTables = [];
    public long[] DcTables = [];
    public long[] AcTables = [];
    public int RestartInterval;

    /// <summary>Where a whole JPEG stream sits, for the files that kept one beside the tags.</summary>
    public long JpegStreamOffset;
    public long JpegStreamLength;

    /// <summary>What the fax compressions were told to do, from whichever options tag applies.</summary>
    public long FaxOptions;

    /// <summary>Where the shared quantisation and Huffman tables lie, for JPEG compression.</summary>
    public int JpegTablesOffset;
    public int JpegTablesLength;

    /// <summary>Index of the sample that carries alpha, or -1.</summary>
    public int AlphaSample = -1;

    /// <summary>True when the alpha is already multiplied into the colour it belongs to.</summary>
    public bool AlphaIsAssociated;

    public double HorizontalDpi;
    public double VerticalDpi;

    public bool IsTiled => TileWidth > 0 && TileLength > 0;

    /// <summary>Colour channels, which is every sample the extras do not claim.</summary>
    public int ColourSamples => AlphaSample >= 0 ? SamplesPerPixel - 1 : SamplesPerPixel;

    /// <summary>Whether the samples are sensor readings behind a filter grid rather than colour.</summary>
    public bool Cfa => Photometric == 32803 && CfaPattern.Length > 0;

    public static bool TryRead(ReadOnlySpan<byte> data, in TiffDirectory ifd, out TiffImage image,
                               out ApertureError error)
    {
        image = new TiffImage { LittleEndian = ifd.LittleEndian };
        error = ApertureError.None;

        if (!ifd.TryGetInteger(TiffTag.ImageWidth, out long width) ||
            !ifd.TryGetInteger(TiffTag.ImageLength, out long height))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        image.Width = (int)width;
        image.Height = (int)height;

        if (ifd.TryGetInteger(TiffTag.SamplesPerPixel, out long samples))
            image.SamplesPerPixel = (int)samples;

        if (image.SamplesPerPixel is < 1 or > MaxSamples)
        {
            error = ApertureError.InvalidColorType;
            return false;
        }

        Span<long> bits = stackalloc long[MaxSamples];
        int read = ifd.GetIntegers(TiffTag.BitsPerSample, bits);

        // A tag that is there but unreadable is a broken file, not a file that left it out.
        if (read == 0 && ifd.HasTag(TiffTag.BitsPerSample))
        {
            error = ApertureError.InvalidData;
            return false;
        }

        if (read > 0)
        {
            image.BitsPerSample = (int)bits[0];

            // Legal, almost never written, and a separate unpacking path, so it is refused.
            for (int i = 1; i < read; i++)
            {
                if (bits[i] != bits[0])
                {
                    error = ApertureError.UnsupportedFeature;
                    return false;
                }
            }
        }

        // Any whole number of bits is legal, and ten, twelve and fourteen are all in use.
        if (image.BitsPerSample is < 1 or > 64 || (image.BitsPerSample > 32 && image.BitsPerSample != 64))
        {
            error = ApertureError.InvalidBitDepth;
            return false;
        }

        if (ifd.TryGetInteger(TiffTag.SampleFormat, out long format))
            image.SampleFormat = (int)format;

        if (!ReadChroma(ifd, image))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (ifd.TryGetByteRange(TiffTag.JpegTables, out int tables, out int tablesLength))
        {
            image.JpegTablesOffset = tables;
            image.JpegTablesLength = tablesLength;
        }

        if (ifd.TryGetInteger(TiffTag.Compression, out long compression))
            image.Compression = (int)compression;

        image.Photometric = ifd.TryGetInteger(TiffTag.PhotometricInterpretation, out long photometric)
            ? (int)photometric
            : image.SamplesPerPixel >= 3 ? 2 : 1;

        // A JPEG stream carries its own colour transform, so colour comes back either way.
        if (image.Compression == 7 && image.Photometric == 6)
            image.Photometric = 2;

        // The log forms hand back floats whatever width the tags describe.
        if (image.Compression is 34676 or 34677 && image.Photometric is 32844 or 32845)
        {
            image.LogLuv = true;
            image.BitsPerSample = 32;
            image.SampleFormat = 3;
            image.Photometric = image.Photometric == 32844 ? 1 : 2;
        }

        // The rebuilt stream carries its own colour transform.
        if (image.Compression == 6)
        {
            image.QuantisationTables = ReadPointers(ifd, TiffTag.JpegQTables);
            image.DcTables = ReadPointers(ifd, TiffTag.JpegDcTables);
            image.AcTables = ReadPointers(ifd, TiffTag.JpegAcTables);

            if (ifd.TryGetInteger(TiffTag.JpegRestartInterval, out long interval))
                image.RestartInterval = (int)interval;

            if (ifd.TryGetInteger(TiffTag.JpegInterchangeFormat, out long stream) &&
                ifd.TryGetInteger(TiffTag.JpegInterchangeLength, out long streamLength))
            {
                image.JpegStreamOffset = stream;
                image.JpegStreamLength = streamLength;
            }

            if (image.Photometric == 6)
                image.Photometric = 2;
        }

        if (ifd.TryGetInteger(TiffTag.PlanarConfiguration, out long planar))
            image.Planar = (int)planar;

        if (ifd.TryGetInteger(TiffTag.Predictor, out long predictor))
            image.Predictor = (int)predictor;

        if (ifd.TryGetInteger(TiffTag.FillOrder, out long fill))
            image.FillOrder = (int)fill;

        if (ifd.TryGetInteger(TiffTag.Orientation, out long orientation))
            image.Orientation = (int)orientation;

        ReadResolution(ifd, image);
        ReadExtraSamples(ifd, image);

        if (image.Photometric == 32803)
            ReadCfa(ifd, image);

        // Each fax compression names its options in a tag of its own.
        int optionsTag = image.Compression == 3 ? TiffTag.T4Options
            : image.Compression == 4 ? TiffTag.T6Options
            : 0;

        if (optionsTag != 0 && ifd.TryGetInteger(optionsTag, out long faxOptions))
            image.FaxOptions = faxOptions;

        if (image.Planar is not (1 or 2))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (!ReadLayout(ifd, image, out error))
            return false;

        if (image.Photometric == 3 && !ReadPalette(ifd, image, out error))
            return false;

        return true;
    }

    private static void ReadResolution(in TiffDirectory ifd, TiffImage image)
    {
        if (!ifd.TryGetInteger(TiffTag.ResolutionUnit, out long unit))
            unit = 2;

        if (!ifd.TryGetRational(TiffTag.XResolution, out double x) ||
            !ifd.TryGetRational(TiffTag.YResolution, out double y))
            return;

        // Unit three is centimetres; anything else is already per inch or has no unit at all.
        double scale = unit == 3 ? 2.54 : 1.0;
        image.HorizontalDpi = x * scale;
        image.VerticalDpi = y * scale;
    }

    private static void ReadExtraSamples(in TiffDirectory ifd, TiffImage image)
    {
        Span<long> extras = stackalloc long[MaxSamples];
        int count = ifd.GetIntegers(TiffTag.ExtraSamples, extras);

        for (int i = 0; i < count; i++)
        {
            // One is premultiplied, two is not, zero is something the format does not name.
            if (extras[i] is not (1 or 2))
                continue;

            image.AlphaSample = image.SamplesPerPixel - count + i;
            image.AlphaIsAssociated = extras[i] == 1;
            break;
        }
    }

    /// <summary>
    /// Reads which colour each site of the sensor grid measured, and the two levels the readings
    /// run between. A site holding nothing but darkness still reads above zero, and one that is
    /// full still reads below the width of its samples, so both ends have to be named.
    /// </summary>
    private static void ReadCfa(in TiffDirectory ifd, TiffImage image)
    {
        Span<long> dimensions = stackalloc long[2];
        if (ifd.GetIntegers(TiffTag.CfaRepeatPatternDim, dimensions) != 2)
            return;

        int across = (int)dimensions[1];
        int down = (int)dimensions[0];
        if (across is < 1 or > 8 || down is < 1 or > 8)
            return;

        long[] pattern = new long[across * down];
        if (ifd.GetIntegers(TiffTag.CfaPattern, pattern) != pattern.Length)
            return;

        image.CfaPattern = new byte[pattern.Length];
        for (int i = 0; i < pattern.Length; i++)
            image.CfaPattern[i] = (byte)pattern[i];

        image.CfaAcross = across;
        image.CfaDown = down;

        image.BlackLevel = ifd.TryGetInteger(TiffTag.BlackLevel, out long black) ? (int)black : 0;
        image.WhiteLevel = ifd.TryGetInteger(TiffTag.WhiteLevel, out long white)
            ? (int)white
            : (1 << image.BitsPerSample) - 1;
    }

    /// <summary>Reads a tag holding a list of places in the file rather than values.</summary>
    private static long[] ReadPointers(in TiffDirectory ifd, int tag)
    {
        long count = ifd.CountOf(tag);
        if (count is <= 0 or > 8)
            return [];

        long[] result = new long[count];
        return ifd.GetIntegers(tag, result) == count ? result : [];
    }

    /// <summary>
    /// The three tags that say how a chroma pair turns back into colour: how much each channel
    /// weighs in the luma, how far apart the values are stretched, and how many pixels share one
    /// pair. Each has a default the format names, so a file may state none of them.
    /// </summary>
    private static bool ReadChroma(in TiffDirectory ifd, TiffImage image)
    {
        Span<double> luma = stackalloc double[3];
        int weights = ifd.GetRationals(TiffTag.YCbCrCoefficients, luma);

        // Named unreadably is refused rather than converted with weights it did not ask for.
        if (weights != 3 && ifd.HasTag(TiffTag.YCbCrCoefficients))
            return false;

        if (weights == 3)
        {
            if (luma[0] + luma[1] + luma[2] <= 0 || luma[1] <= 0)
                return false;

            image.LumaRed = luma[0];
            image.LumaGreen = luma[1];
            image.LumaBlue = luma[2];
        }

        Span<double> range = stackalloc double[6];
        if (ifd.GetRationals(TiffTag.ReferenceBlackWhite, range) == 6)
        {
            double[] read = new double[6];
            for (int i = 0; i < 6; i++)
                read[i] = range[i];

            // The same value twice would divide by nothing.
            if (read[0] != read[1] && read[2] != read[3] && read[4] != read[5])
                image.ReferenceBlackWhite = read;
        }

        Span<long> sampling = stackalloc long[2];
        if (ifd.GetIntegers(TiffTag.YCbCrSubSampling, sampling) == 2 &&
            sampling[0] is 1 or 2 or 4 && sampling[1] is 1 or 2 or 4)
        {
            image.ChromaAcross = (int)sampling[0];
            image.ChromaDown = (int)sampling[1];
        }

        return true;
    }

    private static bool ReadLayout(in TiffDirectory ifd, TiffImage image, out ApertureError error)
    {
        error = ApertureError.None;

        if (ifd.TryGetInteger(TiffTag.TileWidth, out long tileWidth) &&
            ifd.TryGetInteger(TiffTag.TileLength, out long tileLength))
        {
            image.TileWidth = (int)tileWidth;
            image.TileLength = (int)tileLength;

            if (image.TileWidth <= 0 || image.TileLength <= 0)
            {
                error = ApertureError.InvalidHeader;
                return false;
            }

            // Files predating the tile tags describe their tiles with the strip ones.
            return ifd.CountOf(TiffTag.TileOffsets) > 0
                ? ReadOffsets(ifd, image, TiffTag.TileOffsets, TiffTag.TileByteCounts, out error)
                : ReadOffsets(ifd, image, TiffTag.StripOffsets, TiffTag.StripByteCounts, out error);
        }

        image.RowsPerStrip = ifd.TryGetInteger(TiffTag.RowsPerStrip, out long rows) && rows > 0
            ? (int)Math.Min(rows, image.Height)
            : image.Height;

        return ReadOffsets(ifd, image, TiffTag.StripOffsets, TiffTag.StripByteCounts, out error);
    }

    private static bool ReadOffsets(in TiffDirectory ifd, TiffImage image, int offsetTag, int countTag,
                                    out ApertureError error)
    {
        error = ApertureError.None;

        long count = ifd.CountOf(offsetTag);
        if (count <= 0 || count > int.MaxValue / 8)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        image.Offsets = new long[count];
        image.Counts = new long[count];

        if (ifd.GetIntegers(offsetTag, image.Offsets) != count)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        // The counts may be left out where a strip runs to the next offset, but rarely enough
        // that the tag is required rather than guessed at.
        if (ifd.GetIntegers(countTag, image.Counts) != count)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        return true;
    }

    private static bool ReadPalette(in TiffDirectory ifd, TiffImage image, out ApertureError error)
    {
        error = ApertureError.None;

        long entries = ifd.CountOf(TiffTag.ColorMap);
        int expected = 3 << Math.Min(image.BitsPerSample, 16);

        if (entries != expected)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        long[] values = new long[entries];
        if (ifd.GetIntegers(TiffTag.ColorMap, values) != entries)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        // The map is three runs of one channel each, not one run of triples.
        image.Palette = new ushort[entries];
        for (int i = 0; i < entries; i++)
            image.Palette[i] = (ushort)values[i];

        return true;
    }
}
