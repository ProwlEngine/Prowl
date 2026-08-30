// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Png;

/// <summary>
/// One frame of an animated file: where it sits, how long it lasts, and what to do with the canvas
/// before and after it is drawn. The frame itself is a whole image compressed the ordinary way.
/// </summary>
internal sealed class PngAnimationFrame
{
    public int Width;
    public int Height;
    public int Left;
    public int Top;
    public int DelayNumerator;
    public int DelayDenominator;
    public byte Dispose;
    public byte Blend;

    /// <summary>Where this frame's compressed data lies, one span for each chunk carrying it.</summary>
    public List<(int Offset, int Length)> Data = [];

    public int CompressedLength
    {
        get
        {
            int total = 0;
            foreach ((int _, int length) in Data)
                total += length;

            return total;
        }
    }

    /// <summary>How long the frame is shown. A denominator of zero means hundredths of a second.</summary>
    public TimeSpan Delay
    {
        get
        {
            int denominator = DelayDenominator == 0 ? 100 : DelayDenominator;
            return TimeSpan.FromMilliseconds((DelayNumerator * 1000.0) / denominator);
        }
    }
}
