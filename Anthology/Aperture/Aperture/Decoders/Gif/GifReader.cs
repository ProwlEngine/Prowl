// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Gif;

/// <summary>One frame as the file describes it, before anything is composited.</summary>
internal sealed class GifFrame
{
    public int Left;
    public int Top;
    public int Width;
    public int Height;
    public bool Interlaced;

    /// <summary>Palette entries packed as RGBA, with the transparent one already cleared.</summary>
    public uint[] Palette = [];

    /// <summary>One index a pixel, in the frame's own rectangle.</summary>
    public byte[] Indices = [];

    public int TransparentIndex = -1;
    public FrameDisposal Disposal;
    public TimeSpan Delay;
}

/// <summary>
/// Walks the block stream and pulls out the frames. A frame is a rectangle placed on the canvas
/// rather than a whole picture, so what the viewer sees is the running result of drawing each over
/// the last, and the disposal a frame carries belongs to the frame before it.
/// </summary>
internal static class GifReader
{
    private const byte Extension = 0x21;
    private const byte Comment = 0xFE;
    private const byte ImageDescriptor = 0x2C;
    private const byte Trailer = 0x3B;
    private const byte GraphicControl = 0xF9;

    /// <summary>Cap on blocks walked, so a file of empty extensions cannot spin.</summary>
    private const int MaxBlocks = 1 << 20;

    /// <summary>Cap on one frame's rectangle, which the two sixteen bit fields can overflow.</summary>
    private const long MaxFramePixels = 1L << 30;

    /// <summary>
    /// Finds the comment a file may carry, which is the one piece of text the format defines.
    /// </summary>
    public static bool TryReadComment(ReadOnlySpan<byte> data, out string comment)
    {
        comment = string.Empty;
        if (data.Length < 13 || !data[..3].SequenceEqual("GIF"u8))
            return false;

        byte flags = data[10];
        int at = 13;

        if ((flags & 0x80) != 0 && !TryReadPalette(data, ref at, 2 << (flags & 7), out _))
            return false;

        for (int block = 0; block < MaxBlocks && at < data.Length; block++)
        {
            byte marker = data[at++];
            if (marker == Trailer)
                break;

            if (marker != Extension)
            {
                if (marker != ImageDescriptor)
                    continue;

                // Walking past a frame means decoding it, which is more than a comment is worth.
                break;
            }

            if (at >= data.Length)
                break;

            byte label = data[at++];
            if (label != Comment)
            {
                SkipSubBlocks(data, ref at);
                continue;
            }

            System.Text.StringBuilder text = new();
            while (at < data.Length && data[at] != 0)
            {
                int length = data[at++];
                if (at + length > data.Length)
                    break;

                text.Append(System.Text.Encoding.Latin1.GetString(data.Slice(at, length)));
                at += length;
            }

            comment = text.ToString();
            return comment.Length > 0;
        }

        return false;
    }

    public static bool TryRead(ReadOnlySpan<byte> data, int maxFrames, out int width, out int height,
                               out uint background, out List<GifFrame> frames, out ApertureError error)
    {
        frames = [];
        width = height = 0;
        background = 0;

        if (data.Length < 13 || !data[..3].SequenceEqual("GIF"u8))
        {
            error = data.Length < 13 ? ApertureError.UnexpectedEndOfData : ApertureError.InvalidHeader;
            return false;
        }

        width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);

        if (width <= 0 || height <= 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        byte flags = data[10];
        int at = 13;
        uint[] global = [];

        if ((flags & 0x80) != 0)
        {
            int entries = 2 << (flags & 7);
            if (!TryReadPalette(data, ref at, entries, out global))
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            // The screen descriptor names the colour to show wherever no frame has drawn, which
            // matters because a frame need not cover the canvas and often does not.
            if (data[11] < entries)
                background = global[data[11]];
        }

        int transparent = -1;
        FrameDisposal disposal = FrameDisposal.None;
        TimeSpan delay = TimeSpan.Zero;

        for (int block = 0; block < MaxBlocks && at < data.Length; block++)
        {
            byte marker = data[at++];

            if (marker == Trailer)
                break;

            if (marker == Extension)
            {
                if (at >= data.Length)
                    break;

                byte label = data[at++];
                if (label == GraphicControl && at + 6 <= data.Length && data[at] == 4)
                {
                    byte control = data[at + 1];
                    delay = TimeSpan.FromMilliseconds(
                        BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 2)..]) * 10);
                    transparent = (control & 1) != 0 ? data[at + 4] : -1;
                    disposal = ((control >> 2) & 7) switch
                    {
                        2 => FrameDisposal.RestoreBackground,
                        3 => FrameDisposal.RestorePrevious,
                        _ => FrameDisposal.None,
                    };
                }

                SkipSubBlocks(data, ref at);
                continue;
            }

            if (marker != ImageDescriptor)
                continue;

            if (!TryReadFrame(data, ref at, global, transparent, disposal, delay, out GifFrame? frame))
            {
                // A frame that will not parse ends the file rather than condemning the frames
                // ahead of it, which were whole and are a picture on their own.
                break;
            }

            frames.Add(frame!);
            transparent = -1;
            disposal = FrameDisposal.None;
            delay = TimeSpan.Zero;

            if (frames.Count >= maxFrames)
                break;
        }

        if (frames.Count == 0)
        {
            error = ApertureError.NoImageData;
            return false;
        }

        // The background is drawn as the first frame's transparency sees it, since that is the
        // frame it sits behind.
        if (frames[0].TransparentIndex >= 0)
            background = data[11] == frames[0].TransparentIndex ? 0 : background & 0x00FFFFFF;

        error = ApertureError.None;
        return true;
    }

    private static bool TryReadFrame(ReadOnlySpan<byte> data, ref int at, uint[] global, int transparent,
                                     FrameDisposal disposal, TimeSpan delay, out GifFrame? frame)
    {
        frame = null;
        if (at + 9 > data.Length)
            return false;

        GifFrame result = new()
        {
            Left = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]),
            Top = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 2)..]),
            Width = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]),
            Height = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 6)..]),
            TransparentIndex = transparent,
            Disposal = disposal,
            Delay = delay,
        };

        byte flags = data[at + 8];
        result.Interlaced = (flags & 0x40) != 0;
        at += 9;

        if ((flags & 0x80) != 0)
        {
            if (!TryReadPalette(data, ref at, 2 << (flags & 7), out uint[] local))
                return false;

            result.Palette = local;
        }
        else
        {
            result.Palette = global;
        }

        if (result.Width <= 0 || result.Height <= 0 || result.Palette.Length == 0)
            return false;

        if ((long)result.Width * result.Height > MaxFramePixels)
            return false;

        // The transparent index names a palette entry that is not drawn at all, which is the only
        // transparency the format has.
        if (transparent >= 0 && transparent < result.Palette.Length)
        {
            result.Palette = (uint[])result.Palette.Clone();
            result.Palette[transparent] = 0;
        }

        if (at >= data.Length)
            return false;

        int minimumCodeSize = data[at++];
        if (minimumCodeSize is < 2 or > 8)
            return false;

        result.Indices = new byte[result.Width * result.Height];
        if (!GifLzw.TryDecode(data, ref at, minimumCodeSize, result.Indices))
            return false;

        if (result.Interlaced)
            Deinterlace(result);

        frame = result;
        return true;
    }

    /// <summary>
    /// Puts the four passes back in order. An interlaced file stores every eighth row first, so a
    /// viewer reading as it arrives sees the whole picture coarsely before it sees any of it well.
    /// </summary>
    private static void Deinterlace(GifFrame frame)
    {
        ReadOnlySpan<int> starts = [0, 4, 2, 1];
        ReadOnlySpan<int> steps = [8, 8, 4, 2];

        byte[] ordered = new byte[frame.Indices.Length];
        int from = 0;

        for (int pass = 0; pass < 4; pass++)
        {
            for (int y = starts[pass]; y < frame.Height; y += steps[pass], from++)
            {
                Array.Copy(frame.Indices, from * frame.Width, ordered, y * frame.Width, frame.Width);
            }
        }

        frame.Indices = ordered;
    }

    private static bool TryReadPalette(ReadOnlySpan<byte> data, ref int at, int entries, out uint[] palette)
    {
        palette = [];
        if (at + (entries * 3) > data.Length)
            return false;

        palette = new uint[entries];
        for (int i = 0; i < entries; i++)
        {
            int from = at + (i * 3);
            palette[i] = 0xFF000000u | ((uint)data[from] << 16) | ((uint)data[from + 1] << 8) | data[from + 2];
        }

        at += entries * 3;
        return true;
    }

    private static void SkipSubBlocks(ReadOnlySpan<byte> data, ref int at)
    {
        while (at < data.Length)
        {
            int length = data[at++];
            if (length == 0)
                return;

            at += length;
        }
    }
}
