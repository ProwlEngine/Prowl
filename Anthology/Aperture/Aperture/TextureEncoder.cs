// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Encoders.Blocks;

namespace Prowl.Aperture;

/// <summary>How long a block encoder searches for the best block. The output size never changes.</summary>
public enum TextureEncodeQuality
{
    /// <summary>A quick search, for previews and iteration.</summary>
    Fast,

    /// <summary>Most of the quality of a thorough search at a fraction of the time.</summary>
    Balanced,

    /// <summary>Searches far more candidates per block, for final builds.</summary>
    Thorough,
}

/// <summary>Settings for <see cref="TextureEncoder"/>.</summary>
public sealed class TextureEncodeOptions
{
    /// <summary>The settings used when a caller passes none.</summary>
    public static TextureEncodeOptions Default { get; } = new();

    public TextureEncodeQuality Quality { get; set; } = TextureEncodeQuality.Balanced;

    /// <summary>Threads used across rows of blocks. Zero or less uses every core.</summary>
    public int MaxDegreeOfParallelism { get; set; }
}

/// <summary>
/// Compresses RGBA8 pixels into the block layouts a GPU samples directly. BC1 keeps alpha as a
/// one bit cutout below 128, BC3 keeps full alpha, BC4 stores red, BC5 stores red and green, and
/// ASTC stores all four channels at any of its block sizes.
/// </summary>
public static class TextureEncoder
{
    /// <summary>Whether <paramref name="format"/> can be written.</summary>
    public static bool CanEncode(TextureFormat format) => format switch
    {
        TextureFormat.Bc1Unorm or TextureFormat.Bc1UnormSrgb => true,
        TextureFormat.Bc3Unorm or TextureFormat.Bc3UnormSrgb => true,
        TextureFormat.Bc4Unorm or TextureFormat.Bc5Unorm => true,
        _ => AstcBlockSize(format, out _, out _),
    };

    /// <summary>Pixels a block of <paramref name="format"/> covers, or false for a layout that is not block compressed.</summary>
    public static bool TryGetBlockInfo(TextureFormat format, out int blockWidth, out int blockHeight, out int bytesPerBlock)
    {
        bytesPerBlock = format switch
        {
            TextureFormat.Bc1Unorm or TextureFormat.Bc1UnormSrgb or TextureFormat.Bc4Unorm or TextureFormat.Bc4Snorm => 8,
            TextureFormat.Bc2Unorm or TextureFormat.Bc2UnormSrgb or TextureFormat.Bc3Unorm or TextureFormat.Bc3UnormSrgb => 16,
            TextureFormat.Bc5Unorm or TextureFormat.Bc5Snorm or TextureFormat.Bc6hUf16 or TextureFormat.Bc6hSf16 => 16,
            TextureFormat.Bc7Unorm or TextureFormat.Bc7UnormSrgb => 16,
            _ => 0,
        };

        if (bytesPerBlock != 0)
        {
            blockWidth = 4;
            blockHeight = 4;
            return true;
        }

        if (AstcBlockSize(format, out blockWidth, out blockHeight))
        {
            bytesPerBlock = 16;
            return true;
        }

        blockWidth = 0;
        blockHeight = 0;
        return false;
    }

    /// <summary>Bytes one surface of <paramref name="format"/> takes, edge blocks included.</summary>
    public static int GetEncodedSize(TextureFormat format, int width, int height)
    {
        if (!TryGetBlockInfo(format, out int blockWidth, out int blockHeight, out int bytesPerBlock))
            throw new NotSupportedException($"{format} is not a block compressed layout.");

        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        long size = (((long)width + blockWidth - 1) / blockWidth) * (((long)height + blockHeight - 1) / blockHeight) * bytesPerBlock;
        if (size > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(width), $"A {width} by {height} surface needs {size} bytes, more than an array holds.");

        return (int)size;
    }

    /// <summary>Compresses tightly packed RGBA8 pixels into a new array.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height, TextureFormat format,
                                TextureEncodeOptions? options = null)
    {
        byte[] output = new byte[GetEncodedSize(format, width, height)];
        Encode(rgba, width, height, width * 4, format, output, options);
        return output;
    }

    /// <summary>Compresses one frame, converting it to RGBA8 first when it holds another layout.</summary>
    public static byte[] Encode(ImageFrame frame, TextureFormat format, TextureEncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.PixelFormat == PixelFormat.Rgba8)
        {
            byte[] direct = new byte[GetEncodedSize(format, frame.Width, frame.Height)];
            Encode(frame.Pixels, frame.Width, frame.Height, frame.Stride, format, direct, options);
            return direct;
        }

        long length = (long)frame.Width * frame.Height * 4;
        if (length > Array.MaxLength)
            throw new ArgumentException("The frame is too large to convert to RGBA8 in one array.", nameof(frame));

        byte[] rgba = new byte[length];
        for (int y = 0; y < frame.Height; y++)
            PixelConverter.ConvertRow(frame.GetRow(y), frame.PixelFormat, rgba.AsSpan((int)((long)y * frame.Width * 4)), PixelFormat.Rgba8, frame.Width);

        return Encode(rgba, frame.Width, frame.Height, format, options);
    }

    /// <summary>Compresses RGBA8 rows <paramref name="stride"/> bytes apart into <paramref name="destination"/>.</summary>
    public static unsafe void Encode(ReadOnlySpan<byte> rgba, int width, int height, int stride, TextureFormat format,
                                     Span<byte> destination, TextureEncodeOptions? options = null)
    {
        if (!CanEncode(format))
            throw new NotSupportedException($"Encoding to {format} is not supported.");

        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        long rowBytes = (long)width * 4;
        if (stride < rowBytes)
            throw new ArgumentOutOfRangeException(nameof(stride), stride, $"A row of {width} pixels needs {rowBytes} bytes.");

        if (rgba.Length < ((long)stride * (height - 1)) + rowBytes)
            throw new ArgumentException("The source is smaller than the stated size.", nameof(rgba));

        int size = GetEncodedSize(format, width, height);
        if (destination.Length < size)
            throw new ArgumentException($"The destination needs {size} bytes.", nameof(destination));

        options ??= TextureEncodeOptions.Default;
        TryGetBlockInfo(format, out int blockWidth, out int blockHeight, out int bytesPerBlock);

        IBlockEncoder encoder = format switch
        {
            TextureFormat.Bc1Unorm or TextureFormat.Bc1UnormSrgb => new Bc1BlockEncoder(options.Quality),
            TextureFormat.Bc3Unorm or TextureFormat.Bc3UnormSrgb => new Bc3BlockEncoder(options.Quality),
            TextureFormat.Bc4Unorm => new Bc4BlockEncoder(options.Quality, 1),
            TextureFormat.Bc5Unorm => new Bc4BlockEncoder(options.Quality, 2),
            _ => AstcEncoder.For(blockWidth, blockHeight, options.Quality),
        };

        int across = (width + blockWidth - 1) / blockWidth;
        int down = (height + blockHeight - 1) / blockHeight;

        fixed (byte* source = rgba)
        fixed (byte* target = destination)
        {
            BlockJob job = new(source, stride, width, height, target, across, blockWidth, blockHeight, bytesPerBlock, encoder);
            ParallelOptions parallel = new()
            {
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism > 0 ? options.MaxDegreeOfParallelism : -1,
            };

            Parallel.For(0, down, parallel, () => encoder.CreateScratch(), job.Row, _ => { });
        }
    }

    private static bool AstcBlockSize(TextureFormat format, out int width, out int height)
    {
        (width, height) = format switch
        {
            TextureFormat.Astc4x4Unorm or TextureFormat.Astc4x4UnormSrgb => (4, 4),
            TextureFormat.Astc5x4Unorm or TextureFormat.Astc5x4UnormSrgb => (5, 4),
            TextureFormat.Astc5x5Unorm or TextureFormat.Astc5x5UnormSrgb => (5, 5),
            TextureFormat.Astc6x5Unorm or TextureFormat.Astc6x5UnormSrgb => (6, 5),
            TextureFormat.Astc6x6Unorm or TextureFormat.Astc6x6UnormSrgb => (6, 6),
            TextureFormat.Astc8x5Unorm or TextureFormat.Astc8x5UnormSrgb => (8, 5),
            TextureFormat.Astc8x6Unorm or TextureFormat.Astc8x6UnormSrgb => (8, 6),
            TextureFormat.Astc8x8Unorm or TextureFormat.Astc8x8UnormSrgb => (8, 8),
            TextureFormat.Astc10x5Unorm or TextureFormat.Astc10x5UnormSrgb => (10, 5),
            TextureFormat.Astc10x6Unorm or TextureFormat.Astc10x6UnormSrgb => (10, 6),
            TextureFormat.Astc10x8Unorm or TextureFormat.Astc10x8UnormSrgb => (10, 8),
            TextureFormat.Astc10x10Unorm or TextureFormat.Astc10x10UnormSrgb => (10, 10),
            TextureFormat.Astc12x10Unorm or TextureFormat.Astc12x10UnormSrgb => (12, 10),
            TextureFormat.Astc12x12Unorm or TextureFormat.Astc12x12UnormSrgb => (12, 12),
            _ => (0, 0),
        };

        return width != 0;
    }

    /// <summary>One row of blocks per call, gathering each block with its edge pixels repeated.</summary>
    private sealed unsafe class BlockJob(byte* source, int stride, int width, int height, byte* target, int across,
                                         int blockWidth, int blockHeight, int bytesPerBlock, IBlockEncoder encoder)
    {
        public object Row(int blockY, ParallelLoopState state, object scratch)
        {
            Span<byte> pixels = stackalloc byte[blockWidth * blockHeight * 4];

            for (int blockX = 0; blockX < across; blockX++)
            {
                for (int y = 0; y < blockHeight; y++)
                {
                    int row = Math.Min((blockY * blockHeight) + y, height - 1);
                    byte* line = source + ((long)row * stride);

                    for (int x = 0; x < blockWidth; x++)
                    {
                        int column = Math.Min((blockX * blockWidth) + x, width - 1);
                        int to = ((y * blockWidth) + x) * 4;
                        int from = column * 4;
                        pixels[to] = line[from];
                        pixels[to + 1] = line[from + 1];
                        pixels[to + 2] = line[from + 2];
                        pixels[to + 3] = line[from + 3];
                    }
                }

                long at = (((long)blockY * across) + blockX) * bytesPerBlock;
                encoder.EncodeBlock(pixels, new Span<byte>(target + at, bytesPerBlock), scratch);
            }

            return scratch;
        }
    }
}
