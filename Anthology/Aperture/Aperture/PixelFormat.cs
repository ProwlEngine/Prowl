// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// Layout of a decoded pixel buffer. Every value is interleaved, tightly packed and stored in
/// the machine's native endianness, so a row is exactly <c>Width * <see cref="PixelFormatInfo.BytesPerPixel"/></c>
/// bytes with no padding. Names list channels in memory order.
/// </summary>
public enum PixelFormat
{
    /// <summary>Unset or not yet known.</summary>
    Unknown = 0,

    /// <summary>Single 8 bit luminance channel.</summary>
    L8,

    /// <summary>8 bit luminance followed by 8 bit alpha.</summary>
    La8,

    /// <summary>Three 8 bit channels, red first.</summary>
    Rgb8,

    /// <summary>Four 8 bit channels, red first, alpha last.</summary>
    Rgba8,

    /// <summary>Single 16 bit luminance channel.</summary>
    L16,

    /// <summary>16 bit luminance followed by 16 bit alpha.</summary>
    La16,

    /// <summary>Three 16 bit channels, red first.</summary>
    Rgb16,

    /// <summary>Four 16 bit channels, red first, alpha last.</summary>
    Rgba16,

    /// <summary>Three IEEE 754 half precision channels, red first.</summary>
    RgbF16,

    /// <summary>Four IEEE 754 half precision channels, red first, alpha last.</summary>
    RgbaF16,

    /// <summary>Single IEEE 754 single precision luminance channel.</summary>
    LF32,

    /// <summary>Three IEEE 754 single precision channels, red first.</summary>
    RgbF32,

    /// <summary>Four IEEE 754 single precision channels, red first, alpha last.</summary>
    RgbaF32,
}

/// <summary>Shape queries for a <see cref="PixelFormat"/>.</summary>
public static class PixelFormatInfo
{
    /// <summary>Number of colour channels, counting alpha.</summary>
    public static int ChannelCount(this PixelFormat format) => format switch
    {
        PixelFormat.L8 or PixelFormat.L16 or PixelFormat.LF32 => 1,
        PixelFormat.La8 or PixelFormat.La16 => 2,
        PixelFormat.Rgb8 or PixelFormat.Rgb16 or PixelFormat.RgbF16 or PixelFormat.RgbF32 => 3,
        PixelFormat.Rgba8 or PixelFormat.Rgba16 or PixelFormat.RgbaF16 or PixelFormat.RgbaF32 => 4,
        _ => 0,
    };

    /// <summary>Storage size of one channel in bytes.</summary>
    public static int BytesPerChannel(this PixelFormat format) => format switch
    {
        PixelFormat.L8 or PixelFormat.La8 or PixelFormat.Rgb8 or PixelFormat.Rgba8 => 1,
        PixelFormat.L16 or PixelFormat.La16 or PixelFormat.Rgb16 or PixelFormat.Rgba16 => 2,
        PixelFormat.RgbF16 or PixelFormat.RgbaF16 => 2,
        PixelFormat.LF32 or PixelFormat.RgbF32 or PixelFormat.RgbaF32 => 4,
        _ => 0,
    };

    /// <summary>Storage size of one pixel in bytes.</summary>
    public static int BytesPerPixel(this PixelFormat format) => format.ChannelCount() * format.BytesPerChannel();

    /// <summary>Whether the format carries an alpha channel.</summary>
    public static bool HasAlpha(this PixelFormat format) => format switch
    {
        PixelFormat.La8 or PixelFormat.La16 or PixelFormat.Rgba8 or PixelFormat.Rgba16
            or PixelFormat.RgbaF16 or PixelFormat.RgbaF32 => true,
        _ => false,
    };

    /// <summary>Whether channels are floating point rather than normalised integers.</summary>
    public static bool IsFloatingPoint(this PixelFormat format) => format switch
    {
        PixelFormat.RgbF16 or PixelFormat.RgbaF16 or PixelFormat.LF32
            or PixelFormat.RgbF32 or PixelFormat.RgbaF32 => true,
        _ => false,
    };
}
