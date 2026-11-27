// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Exr;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads OpenEXR: scanline, tiled and multi part images, and all eight compressions.</summary>
public sealed class ExrDecoder : DecoderBase
{
    /// <summary>Cap on attributes read, so a header of empty entries cannot stall the parse.</summary>
    private const int MaxAttributes = 4096;

    /// <summary>Cap on channels read from the channel list.</summary>
    private const int MaxChannels = 1024;

    private const int FlagTiled = 1 << 9;
    private const int FlagLongNames = 1 << 10;
    private const int FlagNonImage = 1 << 11;
    private const int FlagMultiPart = 1 << 12;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Exr;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".exr"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!ExrHeader.TryReadAll(data, out List<ExrHeader>? parts, out error))
            return false;

        ExrHeader header = parts![0];
        if (!ExrImageReader.IsSupported(header))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        PixelFormat natural = ExrImageReader.NaturalFormat(header);
        PixelFormat target = options.TargetPixelFormat ?? natural;
        if (!PixelConverter.CanConvert(natural, target))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        List<ImageFrame> frames = [];
        byte[]? scratch = null;

        try
        {
            if (!TryReadPart(data, header, options, info, natural, target, 0, ref scratch,
                             out ImageFrame? first, out error))
                return false;

            frames.Add(first!);

            // The smaller copies are the same picture rather than more of it.
            if (options.DecodeMipmaps && header.Tiled && header.LevelMode != 0)
            {
                int count = ExrLevels.Enumerate(header).Count;
                for (int level = 1; level < count && frames.Count < options.MaxFrames; level++)
                {
                    if (!TryReadPart(data, header, options, info, natural, target, level,
                                     ref scratch, out ImageFrame? small, out _))
                        break;

                    frames.Add(small!);
                }
            }

            // The other parts are read into the layout the first settled, and one that will
            // not fit its canvas is left out.
            if (options.DecodeAllFrames)
            {
                for (int i = 1; i < parts.Count && frames.Count < options.MaxFrames; i++)
                {
                    ExrHeader part = parts[i];
                    if (!ExrImageReader.IsSupported(part) ||
                        part.Width > info.Width || part.Height > info.Height)
                        continue;

                    if (ExrImageReader.NaturalFormat(part) != natural)
                        continue;

                    if (!TryReadPart(data, part, options, info, natural, target, 0, ref scratch,
                                     out ImageFrame? frame, out _))
                        continue;

                    frames.Add(frame!);
                }
            }

            image = new Image
            {
                Format = ImageFormat.Exr,
                Width = info.Width,
                Height = info.Height,
                PixelFormat = target,
                Frames = [.. frames],
                Info = info,
            };

            frames.Clear();
            error = ApertureError.None;
            return true;
        }
        finally
        {
            if (scratch is not null)
                BufferPool.Bytes.Return(scratch);

            foreach (ImageFrame frame in frames)
                frame.Release();
        }
    }

    /// <summary>Reads one part into a frame of its own.</summary>
    private static bool TryReadPart(ReadOnlySpan<byte> data, ExrHeader header, DecodeOptions options,
                                    ImageInfo info, PixelFormat natural, PixelFormat target,
                                    int level, ref byte[]? scratch, out ImageFrame? frame,
                                    out ApertureError error)
    {
        frame = null;

        int width = header.Width;
        int height = header.Height;

        if (level > 0)
        {
            List<ExrLevel> levels = ExrLevels.Enumerate(header);
            if (level >= levels.Count)
            {
                error = ApertureError.InvalidData;
                return false;
            }

            width = levels[level].Width;
            height = levels[level].Height;
        }
        int stride = options.GetStride(width, target);
        long total = (long)stride * height;

        if (total > options.MaxAllocationBytes || total > int.MaxValue || total <= 0)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        bool direct = target == natural;
        int naturalStride = width * natural.BytesPerPixel();

        byte[] buffer = options.UsePooledMemory ? BufferPool.Bytes.Rent((int)total) : new byte[(int)total];

        try
        {
            Span<byte> pixels = buffer.AsSpan(0, (int)total);
            pixels.Clear();

            Span<byte> surface = pixels;
            int surfaceStride = stride;

            if (!direct)
            {
                int needed = naturalStride * height;
                if (scratch is null || scratch.Length < needed)
                {
                    if (scratch is not null)
                        BufferPool.Bytes.Return(scratch);

                    scratch = BufferPool.Bytes.Rent(needed);
                }

                surface = scratch.AsSpan(0, needed);
                surface.Clear();
                surfaceStride = naturalStride;
            }

            bool read = level == 0
                ? ExrImageReader.TryDecode(data, header, surface, surfaceStride,
                                           options.FlipVertically, out error)
                : ExrImageReader.TryDecodeLevel(data, header, level, surface, surfaceStride,
                                                options.FlipVertically, out error);

            if (!read)
                return false;

            if (!direct)
            {
                for (int y = 0; y < height; y++)
                {
                    PixelConverter.ConvertRow(surface.Slice(y * surfaceStride, naturalStride), natural,
                                              pixels[(y * stride)..], target, width);
                }
            }

            frame = new ImageFrame(buffer, (int)total, width, height, stride, target,
                                   options.UsePooledMemory)
            {
                MipLevel = level,
            };
            buffer = [];
            error = ApertureError.None;
            return true;
        }
        finally
        {
            if (buffer.Length != 0 && options.UsePooledMemory)
                BufferPool.Bytes.Return(buffer);
        }
    }

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        if (data.Length < 8)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (data[0] != 0x76 || data[1] != 0x2F || data[2] != 0x31 || data[3] != 0x01)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        SpanReader reader = new(data);
        reader.Skip(4);
        reader.TryReadInt32(out int versionField);

        int version = versionField & 0xFF;
        if (version is not (1 or 2))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        bool tiled = (versionField & FlagTiled) != 0;
        bool multiPart = (versionField & FlagMultiPart) != 0;
        bool deep = (versionField & FlagNonImage) != 0;

        int xMin = 0, yMin = 0, xMax = -1, yMax = -1;
        bool sawDataWindow = false;
        int channelCount = 0;
        int widestType = 0;
        bool sawAlpha = false;
        byte compression = 0;
        bool sawCompression = false;

        for (int i = 0; i < MaxAttributes; i++)
        {
            if (!reader.TryReadNullTerminated(255, out string name))
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            // An empty name terminates the header.
            if (name.Length == 0)
                break;

            if (!reader.TryReadNullTerminated(255, out string type) ||
                !reader.TryReadInt32(out int size) ||
                size < 0 || size > reader.Remaining)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            int valueStart = reader.Position;

            switch (name)
            {
                case "dataWindow" when type == "box2i" && size >= 16:
                    reader.TryReadInt32(out xMin);
                    reader.TryReadInt32(out yMin);
                    reader.TryReadInt32(out xMax);
                    reader.TryReadInt32(out yMax);
                    sawDataWindow = true;
                    break;

                case "channels" when type == "chlist":
                    ReadChannelList(ref reader, valueStart + size, out channelCount, out widestType, out sawAlpha);
                    break;

                case "compression" when type == "compression" && size >= 1:
                    reader.TryReadByte(out compression);
                    sawCompression = true;
                    break;
            }

            if (!reader.Seek(valueStart + size))
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }
        }

        if (!sawDataWindow)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        long width = (long)xMax - xMin + 1;
        long height = (long)yMax - yMin + 1;
        if (!ValidateDimensions(width, height, out error))
            return false;

        if (channelCount == 0)
        {
            error = ApertureError.NoImageData;
            return false;
        }

        // Pixel type 0 is uint, 1 is half and 2 is float.
        bool floating = widestType != 0;
        int bitsPerChannel = widestType switch { 1 => 16, 2 => 32, _ => 32 };

        CountParts(data, tiled, multiPart, out int parts, out int levels);

        PixelFormat natural = ExrHeader.TryReadAll(data, out List<ExrHeader>? all, out _)
            ? ExrImageReader.NaturalFormat(all![0])
            : ChoosePixelFormat(Math.Min(channelCount, 3), bitsPerChannel, floating, sawAlpha);

        info = new ImageInfo
        {
            Format = ImageFormat.Exr,
            Width = (int)width,
            Height = (int)height,
            BitsPerChannel = bitsPerChannel,
            Channels = channelCount,
            HasAlpha = sawAlpha,
            IsHdr = true,
            ColorModel = channelCount == 1 ? ColorModel.Grayscale : ColorModel.Rgb,
            // Every channel comes back as a float whatever width the file stored it at, so this
            // is what the reader itself would choose rather than a guess from the channel list.
            PreferredPixelFormat = natural,
            // The first part's levels, then one for each part after it, which is both a tiled
            // file and a multi part one at once.
            FrameCount = levels + parts - 1,
            MipmapCount = levels,
            Compression = DescribeCompression(sawCompression ? compression : (byte)255, tiled, multiPart, deep),
        };
        _ = FlagLongNames;
        error = ApertureError.None;
        return true;
    }

    /// <summary>
    /// How many pictures the file holds and how many resolutions the first keeps. Both need the
    /// whole header, so a file that holds only one of each is not read twice.
    /// </summary>
    private static void CountParts(ReadOnlySpan<byte> data, bool tiled, bool multiPart,
                                   out int parts, out int levels)
    {
        parts = 1;
        levels = 1;

        if ((!tiled && !multiPart) || !ExrHeader.TryReadAll(data, out List<ExrHeader>? all, out _))
            return;

        parts = all!.Count;
        if (tiled && all[0].LevelMode != 0)
            levels = ExrLevels.Enumerate(all[0]).Count;
    }

    /// <summary>
    /// Walks the channel list. Each entry is a name, a pixel type, a linearity flag, three
    /// reserved bytes and the two sampling rates; an empty name ends the list.
    /// </summary>
    private static void ReadChannelList(ref SpanReader reader, int limit, out int count, out int widestType, out bool hasAlpha)
    {
        count = 0;
        widestType = 0;
        hasAlpha = false;

        for (int i = 0; i < MaxChannels && reader.Position < limit; i++)
        {
            if (!reader.TryReadNullTerminated(255, out string name) || name.Length == 0)
                return;

            if (!reader.TryReadInt32(out int pixelType) || !reader.Skip(12))
                return;

            count++;
            if (pixelType > widestType)
                widestType = pixelType;

            if (name is "A" or "a" || name.EndsWith(".A", StringComparison.Ordinal))
                hasAlpha = true;
        }
    }

    private static string DescribeCompression(byte compression, bool tiled, bool multiPart, bool deep)
    {
        string name = compression switch
        {
            0 => "None",
            1 => "RLE",
            2 => "ZIPS",
            3 => "ZIP",
            4 => "PIZ",
            5 => "PXR24",
            6 => "B44",
            7 => "B44A",
            8 => "DWAA",
            9 => "DWAB",
            255 => "Unspecified",
            _ => $"Unknown ({compression})",
        };

        if (tiled)
            name += ", tiled";
        if (multiPart)
            name += ", multi-part";
        if (deep)
            name += ", deep";
        return name;
    }
}
