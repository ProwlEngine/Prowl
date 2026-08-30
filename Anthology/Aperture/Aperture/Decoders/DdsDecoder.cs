// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Dds;

namespace Prowl.Aperture.Decoders;

/// <summary>
/// Reads DirectDraw Surface textures: the legacy FourCC and mask based pixel formats as well as
/// the DX10 extension header that carries a DXGI format.
/// </summary>
public sealed class DdsDecoder : DecoderBase
{
    private const uint HeaderSize = 124;
    private const uint PixelFormatSize = 32;

    private const uint PixelFormatFourCc = 0x4;
    private const uint PixelFormatRgb = 0x40;
    private const uint PixelFormatLuminance = 0x20000;
    private const uint PixelFormatAlpha = 0x2;
    private const uint PixelFormatAlphaPixels = 0x1;

    private const uint Caps2CubeMap = 0x200;
    private const uint Caps2Volume = 0x200000;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Dds;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".dds"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!DdsSurface.TryRead(data, out DdsSurface surface, out error))
            return false;

        if (!DdsImageReader.CanDescribe(data.Length - surface.DataOffset, surface))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        // For the newer headers this is the component count, not a mask they do not set.
        PixelFormat natural = surface.HasAlphaChannel ? PixelFormat.Rgba8 : PixelFormat.Rgb8;
        PixelFormat target = options.TargetPixelFormat ?? natural;
        if (!PixelConverter.CanConvert(natural, target))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        int channels = target switch
        {
            PixelFormat.Rgb8 => 3,
            PixelFormat.Rgba8 => 4,
            _ => 0,
        };

        bool direct = channels != 0;
        int naturalChannels = natural.BytesPerPixel();

        // A stack rather than a picture: faces or array slices, each with its own chain of
        // smaller copies. The two options decide which of them the caller wants.
        bool everything = options.DecodeAllFrames;
        int levels = options.DecodeMipmaps ? surface.MipLevels : 1;

        List<DdsPlane> planes = DdsPlanes.Enumerate(surface, data.Length);
        if (planes.Count == 0)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        List<ImageFrame> frames = [];
        byte[]? scratch = null;

        try
        {
            foreach (DdsPlane plane in planes)
            {
                if (plane.MipLevel >= levels || (!everything && plane.Slice > 0))
                    continue;

                DdsSurface one = surface;
                one.DataOffset = plane.Offset;
                one.Width = plane.Width;
                one.Height = plane.Height;

                if (!TryReadPlane(data, one, options, natural, target, channels, naturalChannels,
                                  direct, plane.MipLevel, plane.Slice, ref scratch,
                                  out ImageFrame? frame, out error))
                    return false;

                frames.Add(frame!);
            }

            if (frames.Count == 0)
            {
                error = ApertureError.InvalidData;
                return false;
            }

            image = new Image
            {
                Format = ImageFormat.Dds,
                Width = surface.Width,
                Height = surface.Height,
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

    /// <summary>Decodes one level of one slice into a frame of its own.</summary>
    private static bool TryReadPlane(ReadOnlySpan<byte> data, in DdsSurface plane, DecodeOptions options,
                                     PixelFormat natural, PixelFormat target, int channels,
                                     int naturalChannels, bool direct, int level, int slice,
                                     ref byte[]? scratch, out ImageFrame? frame, out ApertureError error)
    {
        frame = null;

        int stride = options.GetStride(plane.Width, target);
        long total = (long)stride * plane.Height;

        if (total > options.MaxAllocationBytes || total > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        byte[] buffer = options.UsePooledMemory ? BufferPool.Bytes.Rent((int)total) : new byte[(int)total];

        try
        {
            Span<byte> pixels = buffer.AsSpan(0, (int)total);
            pixels.Clear();

            Span<byte> surfacePixels = pixels;
            int surfaceStride = stride;
            int surfaceChannels = channels;

            if (!direct)
            {
                int naturalStride = plane.Width * naturalChannels;
                int needed = naturalStride * plane.Height;

                if (scratch is null || scratch.Length < needed)
                {
                    if (scratch is not null)
                        BufferPool.Bytes.Return(scratch);

                    scratch = BufferPool.Bytes.Rent(needed);
                }

                surfacePixels = scratch.AsSpan(0, needed);
                surfacePixels.Clear();
                surfaceStride = naturalStride;
                surfaceChannels = naturalChannels;
            }

            if (!DdsImageReader.TryDecode(data, plane, surfaceChannels, surfacePixels, surfaceStride,
                                          options.FlipVertically, out error))
            {
                if (error == ApertureError.None)
                    error = ApertureError.InvalidData;
                return false;
            }

            if (!direct)
            {
                for (int y = 0; y < plane.Height; y++)
                {
                    PixelConverter.ConvertRow(surfacePixels.Slice(y * surfaceStride, surfaceStride), natural,
                                              pixels[(y * stride)..], target, plane.Width);
                }
            }

            frame = new ImageFrame(buffer, (int)total, plane.Width, plane.Height, stride, target,
                                   options.UsePooledMemory)
            {
                MipLevel = level,
                ArraySlice = slice,
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
        if (data.Length < 4 + HeaderSize)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        SpanReader reader = new(data);
        reader.Skip(4);
        reader.TryReadUInt32(out uint size);
        reader.TryReadUInt32(out uint flags);
        reader.TryReadUInt32(out uint height);
        reader.TryReadUInt32(out uint width);
        reader.TryReadUInt32(out uint pitchOrLinearSize);
        reader.TryReadUInt32(out uint depth);
        reader.TryReadUInt32(out uint mipMapCount);
        reader.Skip(44);

        reader.TryReadUInt32(out uint pfSize);
        reader.TryReadUInt32(out uint pfFlags);
        reader.TryReadBytes(4, out ReadOnlySpan<byte> fourCc);
        reader.TryReadUInt32(out uint rgbBitCount);
        reader.TryReadUInt32(out uint rMask);
        reader.Skip(8); // green and blue masks
        reader.TryReadUInt32(out uint aMask);
        reader.Skip(4); // caps
        reader.TryReadUInt32(out uint caps2);
        reader.Skip(12); // caps3, caps4 and the trailing reserved word

        if (size != HeaderSize)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (!ValidateDimensions(width, height, out error))
            return false;

        string fourCcText = System.Text.Encoding.ASCII.GetString(fourCc);
        bool hasDx10 = (pfFlags & PixelFormatFourCc) != 0 && fourCcText == "DX10";

        uint dxgiFormat = 0;
        uint arraySize = 1;
        if (hasDx10)
        {
            if (!reader.TryReadUInt32(out dxgiFormat) || !reader.Skip(8) || !reader.TryReadUInt32(out arraySize))
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }
            if (arraySize == 0)
                arraySize = 1;
        }

        // A cube map stores six faces; the caps2 face bits are advisory and often incomplete.
        int slices = (caps2 & Caps2CubeMap) != 0 ? 6 : 1;
        slices *= (int)Math.Min(arraySize, 4096);

        int mipLevels = (int)Math.Clamp(mipMapCount == 0 ? 1 : mipMapCount, 1, 32);

        // A volume holds a stack of slices at every level, each level half as deep as the last.
        int planes = 0;
        for (int level = 0; level < mipLevels; level++)
            planes += (int)Math.Max(1, Math.Min(depth, 65536) >> level);

        SurfaceDescription surface = hasDx10
            ? DescribeDxgi(dxgiFormat)
            : DescribeLegacy(pfFlags, fourCcText, rgbBitCount, rMask, aMask);

        if (surface.Name is null)
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        // The reader decides this, so it is asked rather than guessed at a second time.
        bool alpha = DdsSurface.TryRead(data, out DdsSurface full, out _)
            ? full.HasAlphaChannel
            : surface.HasAlpha;

        info = new ImageInfo
        {
            Format = ImageFormat.Dds,
            Width = (int)width,
            Height = (int)height,
            BitsPerChannel = surface.BitsPerChannel,
            Channels = surface.Channels,
            HasAlpha = alpha,
            IsHdr = surface.IsFloat,
            ColorModel = surface.Channels == 1 ? ColorModel.Grayscale : ColorModel.Rgb,
            // What a decode hands back, which here is eight bit colour whatever is stored.
            PreferredPixelFormat = alpha ? PixelFormat.Rgba8 : PixelFormat.Rgb8,
            FrameCount = planes * slices,
            MipmapCount = mipLevels,
            Compression = surface.Name,
        };
        _ = flags;
        _ = pitchOrLinearSize;
        _ = pfSize;
        _ = Caps2Volume;
        error = ApertureError.None;
        return true;
    }

    private readonly record struct SurfaceDescription(string? Name, int Channels, int BitsPerChannel, bool HasAlpha, bool IsFloat);

    private static SurfaceDescription DescribeLegacy(uint pfFlags, string fourCc, uint rgbBitCount, uint rMask, uint aMask)
    {
        if ((pfFlags & PixelFormatFourCc) != 0)
        {
            return fourCc switch
            {
                "DXT1" => new("BC1", 4, 8, true, false),
                "DXT2" or "DXT3" => new("BC2", 4, 8, true, false),
                "DXT4" or "DXT5" => new("BC3", 4, 8, true, false),
                "ATI1" or "BC4U" => new("BC4 unsigned", 1, 8, false, false),
                "BC4S" => new("BC4 signed", 1, 8, false, false),
                "ATI2" or "BC5U" => new("BC5 unsigned", 2, 8, false, false),
                "BC5S" => new("BC5 signed", 2, 8, false, false),
                "RXGB" => new("BC3 with swizzled channels", 3, 8, false, false),
                "RGBG" or "GRGB" => new("Sub-sampled RGBG", 3, 8, false, false),
                "UYVY" or "YUY2" => new("Sub-sampled YUV", 3, 8, false, false),
                _ => DescribeD3dFormat(fourCc),
            };
        }

        bool hasAlpha = (pfFlags & (PixelFormatAlphaPixels | PixelFormatAlpha)) != 0 && aMask != 0;
        if ((pfFlags & PixelFormatRgb) != 0)
        {
            int channels = hasAlpha ? 4 : 3;
            int bits = rgbBitCount >= 32 && hasAlpha ? 8 : 8;
            return new($"Uncompressed {rgbBitCount} bit", channels, bits, hasAlpha, false);
        }

        if ((pfFlags & PixelFormatLuminance) != 0)
            return new($"Uncompressed luminance {rgbBitCount} bit", hasAlpha ? 2 : 1, 8, hasAlpha, false);

        if ((pfFlags & PixelFormatAlpha) != 0)
            return new("Alpha only", 1, 8, true, false);

        // Some writers leave the flags empty but still fill in the masks.
        if (rgbBitCount > 0 && rMask != 0)
            return new($"Uncompressed {rgbBitCount} bit", hasAlpha ? 4 : 3, 8, hasAlpha, false);

        return new(null, 0, 0, false, false);
    }

    /// <summary>Legacy files may store a numeric D3DFORMAT in the FourCC slot instead of four characters.</summary>
    private static SurfaceDescription DescribeD3dFormat(string fourCc)
    {
        if (fourCc.Length != 4)
            return new(null, 0, 0, false, false);

        uint code = (uint)(fourCc[0] | (fourCc[1] << 8) | (fourCc[2] << 16) | (fourCc[3] << 24));
        return code switch
        {
            36 => new("A16B16G16R16", 4, 16, true, false),
            110 => new("Q16W16V16U16", 4, 16, true, false),
            111 => new("R16F", 1, 16, false, true),
            112 => new("G16R16F", 2, 16, false, true),
            113 => new("A16B16G16R16F", 4, 16, true, true),
            114 => new("R32F", 1, 32, false, true),
            115 => new("G32R32F", 2, 32, false, true),
            116 => new("A32B32G32R32F", 4, 32, true, true),
            _ => new(null, 0, 0, false, false),
        };
    }

    /// <summary>
    /// Maps the DXGI format enumeration. The groups are contiguous runs of typeless, float,
    /// unorm, uint, snorm and sint variants, which is why the ranges look arbitrary.
    /// </summary>
    private static SurfaceDescription DescribeDxgi(uint format) => format switch
    {
        >= 1 and <= 4 => new("R32G32B32A32", 4, 32, true, format == 2),
        >= 5 and <= 8 => new("R32G32B32", 3, 32, false, format == 6),
        >= 9 and <= 14 => new("R16G16B16A16", 4, 16, true, format == 10),
        >= 15 and <= 18 => new("R32G32", 2, 32, false, format == 16),
        >= 23 and <= 25 => new("R10G10B10A2", 4, 10, true, false),
        26 => new("R11G11B10 float", 3, 11, false, true),
        >= 27 and <= 32 => new("R8G8B8A8", 4, 8, true, false),
        >= 33 and <= 38 => new("R16G16", 2, 16, false, format == 34),
        >= 39 and <= 43 => new("R32", 1, 32, false, format is 40 or 41),
        >= 48 and <= 52 => new("R8G8", 2, 8, false, false),
        >= 53 and <= 59 => new("R16", 1, 16, false, format == 54),
        >= 60 and <= 64 => new("R8", 1, 8, false, false),
        65 => new("A8", 1, 8, true, false),
        66 => new("R1", 1, 1, false, false),
        67 => new("R9G9B9E5 shared exponent", 3, 9, false, true),
        68 => new("R8G8_B8G8 sub-sampled", 3, 8, false, false),
        69 => new("G8R8_G8B8 sub-sampled", 3, 8, false, false),
        >= 70 and <= 72 => new("BC1", 4, 8, true, false),
        >= 73 and <= 75 => new("BC2", 4, 8, true, false),
        >= 76 and <= 78 => new("BC3", 4, 8, true, false),
        >= 79 and <= 81 => new("BC4", 1, 8, false, false),
        >= 82 and <= 84 => new("BC5", 2, 8, false, false),
        85 => new("B5G6R5", 3, 5, false, false),
        86 => new("B5G5R5A1", 4, 5, true, false),
        87 or 90 or 91 => new("B8G8R8A8", 4, 8, true, false),
        88 or 92 or 93 => new("B8G8R8X8", 3, 8, false, false),
        89 => new("R10G10B10 XR bias A2", 4, 10, true, false),
        >= 94 and <= 96 => new("BC6H", 3, 16, false, true),
        >= 97 and <= 99 => new("BC7", 4, 8, true, false),
        100 => new("AYUV", 4, 8, true, false),
        101 => new("Y410", 4, 10, true, false),
        102 => new("Y416", 4, 16, true, false),
        103 => new("NV12", 3, 8, false, false),
        104 => new("P010", 3, 10, false, false),
        105 => new("P016", 3, 16, false, false),
        106 => new("420 opaque", 3, 8, false, false),
        107 => new("YUY2", 3, 8, false, false),
        108 => new("Y210", 3, 10, false, false),
        109 => new("Y216", 3, 16, false, false),
        110 => new("NV11", 3, 8, false, false),
        113 => new("P8", 1, 8, false, false),
        114 => new("A8P8", 2, 8, true, false),
        115 => new("B4G4R4A4", 4, 4, true, false),
        // Four slots a block size, from 4x4 at 133 through 12x12 at 188.
        >= 133 and <= 188 => new(DescribeAstc(format), 4, 8, true, false),
        191 => new("A4B4G4R4", 4, 4, true, false),
        _ => new(null, 0, 0, false, false),
    };

    private static string DescribeAstc(uint format)
    {
        (int Width, int Height)[] blocks =
        [
            (4, 4), (5, 4), (5, 5), (6, 5), (6, 6), (8, 5), (8, 6), (8, 8),
            (10, 5), (10, 6), (10, 8), (10, 10), (12, 10), (12, 12),
        ];
        int index = (int)((format - 133) / 4);
        return index >= 0 && index < blocks.Length
            ? $"ASTC {blocks[index].Width}x{blocks[index].Height}"
            : "ASTC";
    }
}
