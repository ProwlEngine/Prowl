// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using Prowl.Aperture.Decoders.Hdr;
using Prowl.Aperture.Metadata;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads Radiance RGBE and XYZE high dynamic range images.</summary>
public sealed class HdrDecoder : DecoderBase
{
    /// <summary>Cap on header bytes scanned before the file is called malformed.</summary>
    private const int MaxHeaderBytes = 64 * 1024;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Hdr;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".hdr", ".pic", ".rgbe"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!TryParse(data, out ImageInfo? parsed, out int offset, out bool bottomUp, out error))
            return false;

        if (!HdrImageReader.CanDescribe(data.Length - offset, parsed!.Width, parsed.Height))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        PixelFormat natural = PixelFormat.RgbF32;
        PixelFormat target = options.TargetPixelFormat ?? natural;
        if (!PixelConverter.CanConvert(natural, target))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        bool direct = target == natural;
        int naturalStride = info.Width * 12;
        int stride = options.GetStride(info.Width, target);
        long total = (long)stride * info.Height;

        if (total > options.MaxAllocationBytes || total > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        byte[] buffer = options.UsePooledMemory ? BufferPool.Bytes.Rent((int)total) : new byte[(int)total];
        byte[]? scratch = null;

        try
        {
            Span<byte> pixels = buffer.AsSpan(0, (int)total);
            pixels.Clear();

            Span<byte> surface = pixels;
            int surfaceStride = stride;

            if (!direct)
            {
                scratch = BufferPool.Bytes.Rent(naturalStride * info.Height);
                surface = scratch.AsSpan(0, naturalStride * info.Height);
                surface.Clear();
                surfaceStride = naturalStride;
            }

            if (!HdrImageReader.TryDecode(data, offset, info.Width, info.Height, bottomUp,
                                          surface, surfaceStride, options.FlipVertically, out error))
                return false;

            if (!direct)
            {
                for (int y = 0; y < info.Height; y++)
                {
                    PixelConverter.ConvertRow(surface.Slice(y * surfaceStride, naturalStride), natural,
                                              pixels[(y * stride)..], target, info.Width);
                }
            }

            ImageFrame frame = new(buffer, (int)total, info.Width, info.Height, stride, target,
                                   options.UsePooledMemory);

            image = new Image
            {
                Format = ImageFormat.Hdr,
                Width = info.Width,
                Height = info.Height,
                PixelFormat = target,
                Frames = new[] { frame },
                Info = info,
            };
            buffer = [];
            error = ApertureError.None;
            return true;
        }
        finally
        {
            if (scratch is not null)
                BufferPool.Bytes.Return(scratch);
            if (buffer.Length != 0 && options.UsePooledMemory)
                BufferPool.Bytes.Return(buffer);
        }
    }

    /// <inheritdoc />
    protected override ImageMetadata ReadMetadata(ReadOnlySpan<byte> data)
    {
        MetadataBuilder builder = new();
        int offset = 0;

        // The header is lines of text up to a blank one, each either a comment or a setting.
        for (int line = 0; line < MaxHeaderLines; line++)
        {
            int end = data[offset..].IndexOf((byte)'\n');
            if (end < 0)
                break;

            ReadOnlySpan<byte> text = data.Slice(offset, end);
            offset += end + 1;

            if (text.Length == 0)
                break;

            if (text[0] == '#')
                continue;

            int split = text.IndexOf((byte)'=');
            if (split <= 0)
                continue;

            builder.AddText(Encoding.ASCII.GetString(text[..split]).Trim(),
                            Encoding.ASCII.GetString(text[(split + 1)..]).Trim());
        }

        return builder.Build();
    }

    /// <summary>Cap on header lines read, well past what any file writes.</summary>
    private const int MaxHeaderLines = 256;

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error) =>
        TryParse(data, out info, out _, out _, out error);

    private static bool TryParse(ReadOnlySpan<byte> data, out ImageInfo? info, out int dataOffset,
                                 out bool bottomUp, out ApertureError error)
    {
        info = null;
        dataOffset = 0;
        bottomUp = false;
        if (data.Length < 4)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (data[0] != (byte)'#' || data[1] != (byte)'?')
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        int offset = 0;
        int limit = Math.Min(data.Length, MaxHeaderBytes);
        string format = string.Empty;
        double exposure = 1.0;
        bool sawBlankLine = false;

        while (offset < limit)
        {
            if (!TryReadLine(data, limit, ref offset, out string line))
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            if (line.Length == 0)
            {
                sawBlankLine = true;
                break;
            }

            if (line.StartsWith("FORMAT=", StringComparison.Ordinal))
                format = line["FORMAT=".Length..].Trim();
            else if (line.StartsWith("EXPOSURE=", StringComparison.Ordinal))
                double.TryParse(line["EXPOSURE=".Length..].Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out exposure);
        }

        if (!sawBlankLine)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (format.Length != 0 && format is not ("32-bit_rle_rgbe" or "32-bit_rle_xyze"))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        if (!TryReadLine(data, limit, ref offset, out string resolution))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (!TryParseResolution(resolution, out int width, out int height, out bool flipped))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (!ValidateDimensions(width, height, out error))
            return false;

        dataOffset = offset;
        bottomUp = flipped;
        info = new ImageInfo
        {
            Format = ImageFormat.Hdr,
            Width = width,
            Height = height,
            BitsPerChannel = 32,
            Channels = 3,
            HasAlpha = false,
            IsHdr = true,
            ColorModel = ColorModel.Rgb,
            PreferredPixelFormat = PixelFormat.RgbF32,
            FrameCount = 1,
            Orientation = flipped ? ExifOrientation.BottomLeft : ExifOrientation.TopLeft,
            Compression = format.Length == 0 ? "RGBE" : format,
        };
        _ = exposure;
        error = ApertureError.None;
        return true;
    }

    /// <summary>
    /// Parses the resolution line. Radiance allows all eight axis orderings; the common one is
    /// "-Y height +X width", which stores rows top to bottom.
    /// </summary>
    private static bool TryParseResolution(string line, out int width, out int height, out bool flipped)
    {
        width = height = 0;
        flipped = false;

        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return false;

        for (int i = 0; i < 4; i += 2)
        {
            string axis = parts[i];
            if (axis.Length != 2 || (axis[0] != '+' && axis[0] != '-'))
                return false;

            if (!int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
                return false;

            switch (axis[1])
            {
                case 'Y':
                    height = value;
                    flipped = axis[0] == '+';
                    break;
                case 'X':
                    width = value;
                    break;
                default:
                    return false;
            }
        }

        return width > 0 && height > 0;
    }

    private static bool TryReadLine(ReadOnlySpan<byte> data, int limit, ref int offset, out string line)
    {
        int start = offset;
        while (offset < limit && data[offset] != (byte)'\n')
            offset++;

        if (offset >= limit)
        {
            line = string.Empty;
            return false;
        }

        int end = offset;
        if (end > start && data[end - 1] == (byte)'\r')
            end--;

        line = Encoding.ASCII.GetString(data[start..end]);
        offset++;
        return true;
    }
}
