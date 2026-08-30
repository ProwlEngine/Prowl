// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>How the stored samples in a file relate to colour, before any conversion to RGB.</summary>
public enum ColorModel
{
    /// <summary>Not determined.</summary>
    Unknown = 0,

    /// <summary>Single luminance channel.</summary>
    Grayscale,

    /// <summary>Red, green and blue components.</summary>
    Rgb,

    /// <summary>Palette indices into a colour table.</summary>
    Indexed,

    /// <summary>Luma plus two chroma difference channels, usually subsampled.</summary>
    YCbCr,

    /// <summary>Subtractive four channel process colour.</summary>
    Cmyk,

    /// <summary>Perceptual lightness with two opponent channels.</summary>
    Lab,

    /// <summary>Undemosaiced sensor samples behind a colour filter array.</summary>
    ColorFilterArray,

    /// <summary>Two channel luminance and subsampled chroma, as used by EXR luminance/chroma images.</summary>
    LuminanceChroma,
}

/// <summary>
/// The Exif orientation tag. Values describe the transform needed to bring stored pixels into
/// display order, matching Exif tag 0x0112.
/// </summary>
public enum ExifOrientation
{
    /// <summary>No orientation recorded; treat as <see cref="TopLeft"/>.</summary>
    Unspecified = 0,

    /// <summary>Row 0 is the visual top, column 0 the visual left. No transform needed.</summary>
    TopLeft = 1,

    /// <summary>Mirrored horizontally.</summary>
    TopRight = 2,

    /// <summary>Rotated 180 degrees.</summary>
    BottomRight = 3,

    /// <summary>Mirrored vertically.</summary>
    BottomLeft = 4,

    /// <summary>Mirrored horizontally then rotated 270 degrees clockwise.</summary>
    LeftTop = 5,

    /// <summary>Rotated 90 degrees clockwise.</summary>
    RightTop = 6,

    /// <summary>Mirrored horizontally then rotated 90 degrees clockwise.</summary>
    RightBottom = 7,

    /// <summary>Rotated 270 degrees clockwise.</summary>
    LeftBottom = 8,
}
