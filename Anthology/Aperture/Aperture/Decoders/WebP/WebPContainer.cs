// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>One frame of an animation, or the single frame a still file holds.</summary>
internal sealed class WebPFrame
{
    public int Left;
    public int Top;
    public int Width;
    public int Height;
    public TimeSpan Delay;

    /// <summary>Whether the canvas is cleared to the background before this frame is drawn.</summary>
    public bool DisposeToBackground;

    /// <summary>Whether this frame is blended over what is beneath rather than replacing it.</summary>
    public bool Blend = true;

    /// <summary>Where the coded picture lies, and whether it is the lossless form.</summary>
    public int Offset;
    public int Length;
    public bool Lossless;

    /// <summary>Where a separately coded alpha plane lies, if there is one.</summary>
    public int AlphaOffset;
    public int AlphaLength;
    public byte AlphaFlags;
}

/// <summary>
/// The chunk container a file is wrapped in. A still picture is one coded chunk; anything more
/// arrives as further chunks after a header naming which to expect, so reading it is a walk over a
/// list rather than a fixed layout.
/// </summary>
internal static class WebPContainer
{
    /// <summary>Cap on chunks walked, so a file of empty ones cannot stall the parse.</summary>
    private const int MaxChunks = 1 << 16;

    public static bool TryRead(ReadOnlySpan<byte> data, int maxFrames, out int width, out int height,
                              out bool hasAlpha, out List<WebPFrame> frames, out ApertureError error)
    {
        frames = [];
        width = height = 0;
        hasAlpha = false;

        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WEBP"u8))
        {
            error = data.Length < 12 ? ApertureError.UnexpectedEndOfData : ApertureError.InvalidHeader;
            return false;
        }

        long declared = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        int limit = (int)Math.Min(data.Length, 8 + declared);

        int at = 12;
        WebPFrame? still = null;
        int alphaOffset = 0;
        int alphaLength = 0;
        byte alphaFlags = 0;
        bool sawCanvas = false;

        for (int chunk = 0; chunk < MaxChunks && at + 8 <= limit; chunk++)
        {
            ReadOnlySpan<byte> name = data.Slice(at, 4);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);

            if (size < 0 || at + 8 + size > limit)
                break;

            int body = at + 8;
            int length = (int)size;

            if (name.SequenceEqual("VP8X"u8) && length >= 10)
            {
                hasAlpha = (data[body] & 0x10) != 0;
                width = ReadUInt24(data[(body + 4)..]) + 1;
                height = ReadUInt24(data[(body + 7)..]) + 1;
                sawCanvas = true;
            }
            else if (name.SequenceEqual("ALPH"u8) && length >= 1)
            {
                alphaFlags = data[body];
                alphaOffset = body + 1;
                alphaLength = length - 1;
                hasAlpha = true;
            }
            else if (name.SequenceEqual("VP8 "u8) || name.SequenceEqual("VP8L"u8))
            {
                bool lossless = name[3] == (byte)'L';
                still ??= new WebPFrame
                {
                    Offset = body,
                    Length = length,
                    Lossless = lossless,
                    AlphaOffset = alphaOffset,
                    AlphaLength = alphaLength,
                    AlphaFlags = alphaFlags,
                };

                alphaOffset = alphaLength = 0;
                alphaFlags = 0;
            }
            else if (name.SequenceEqual("ANMF"u8) && length >= 16)
            {
                if (TryReadFrame(data, body, length, out WebPFrame? frame) && frames.Count < maxFrames)
                {
                    frames.Add(frame!);
                    if (frame!.AlphaLength > 0)
                        hasAlpha = true;
                }
            }

            at = body + length + (length & 1);
        }

        if (frames.Count == 0)
        {
            if (still is null)
            {
                error = ApertureError.NoImageData;
                return false;
            }

            frames.Add(still);
        }

        // A still file states its size inside the coded picture rather than in the container, and
        // an extended one states it in both, where the container is what a viewer trusts.
        if (!sawCanvas)
        {
            WebPFrame first = frames[0];
            if (!TryReadSize(data, first, out width, out height, out bool codedAlpha))
            {
                error = ApertureError.InvalidHeader;
                return false;
            }

            hasAlpha |= codedAlpha;
            first.Width = width;
            first.Height = height;
        }

        if (width <= 0 || height <= 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        error = ApertureError.None;
        return true;
    }

    /// <summary>Reads one animation frame's rectangle, timing and the chunks inside it.</summary>
    private static bool TryReadFrame(ReadOnlySpan<byte> data, int body, int length, out WebPFrame? frame)
    {
        frame = null;

        WebPFrame result = new()
        {
            Left = ReadUInt24(data[body..]) * 2,
            Top = ReadUInt24(data[(body + 3)..]) * 2,
            Width = ReadUInt24(data[(body + 6)..]) + 1,
            Height = ReadUInt24(data[(body + 9)..]) + 1,
            Delay = TimeSpan.FromMilliseconds(ReadUInt24(data[(body + 12)..])),
            DisposeToBackground = (data[body + 15] & 1) != 0,
            Blend = (data[body + 15] & 2) == 0,
        };

        int at = body + 16;
        int end = body + length;
        bool sawPicture = false;

        for (int chunk = 0; chunk < 8 && at + 8 <= end; chunk++)
        {
            ReadOnlySpan<byte> name = data.Slice(at, 4);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);

            if (size < 0 || at + 8 + size > end)
                break;

            int inner = at + 8;
            int innerLength = (int)size;

            if (name.SequenceEqual("ALPH"u8) && innerLength >= 1)
            {
                result.AlphaFlags = data[inner];
                result.AlphaOffset = inner + 1;
                result.AlphaLength = innerLength - 1;
            }
            else if (name.SequenceEqual("VP8 "u8) || name.SequenceEqual("VP8L"u8))
            {
                result.Offset = inner;
                result.Length = innerLength;
                result.Lossless = name[3] == (byte)'L';
                sawPicture = true;
            }

            at = inner + innerLength + (innerLength & 1);
        }

        if (!sawPicture || result.Width <= 0 || result.Height <= 0)
            return false;

        frame = result;
        return true;
    }

    /// <summary>Takes the picture's own size out of whichever coded form it is in.</summary>
    public static bool TryReadSize(ReadOnlySpan<byte> data, WebPFrame frame, out int width,
                                   out int height, out bool alpha)
    {
        width = height = 0;
        alpha = false;

        if (frame.Offset < 0 || frame.Length < 5 || frame.Offset + frame.Length > data.Length)
            return false;

        ReadOnlySpan<byte> body = data.Slice(frame.Offset, frame.Length);

        if (frame.Lossless)
        {
            if (body[0] != 0x2F || body.Length < 5)
                return false;

            uint packed = BinaryPrimitives.ReadUInt32LittleEndian(body[1..]);
            width = (int)(packed & 0x3FFF) + 1;
            height = (int)((packed >> 14) & 0x3FFF) + 1;
            alpha = ((packed >> 28) & 1) != 0;
            return (packed >> 29) == 0;
        }

        // A lossy frame starts with a three byte tag, then a start code, then the size.
        if (body.Length < 10 || (body[0] & 1) != 0)
            return false;

        if (body[3] != 0x9D || body[4] != 0x01 || body[5] != 0x2A)
            return false;

        width = BinaryPrimitives.ReadUInt16LittleEndian(body[6..]) & 0x3FFF;
        height = BinaryPrimitives.ReadUInt16LittleEndian(body[8..]) & 0x3FFF;
        return width > 0 && height > 0;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data) =>
        data[0] | (data[1] << 8) | (data[2] << 16);
}
