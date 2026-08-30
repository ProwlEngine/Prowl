// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// Everything Aperture can report about an image without decoding its pixels. Produced by
/// <see cref="Image.Identify(string)"/>, which reads only as much of the file as the header
/// occupies, so it stays cheap on large files and on files that would fail to decode.
/// </summary>
public sealed class ImageInfo
{
    /// <summary>The detected container.</summary>
    public required ImageFormat Format { get; init; }

    /// <summary>Width of the primary image in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Height of the primary image in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Bits per colour channel as stored in the file, before any conversion.</summary>
    public int BitsPerChannel { get; init; }

    /// <summary>Number of stored channels, counting alpha.</summary>
    public int Channels { get; init; }

    /// <summary>Whether the file carries per pixel transparency.</summary>
    public bool HasAlpha { get; init; }

    /// <summary>Whether samples cover a range beyond the unit interval and want a float target.</summary>
    public bool IsHdr { get; init; }

    /// <summary>How stored samples relate to colour before conversion.</summary>
    public ColorModel ColorModel { get; init; }

    /// <summary>The layout a decode produces when <see cref="DecodeOptions.TargetPixelFormat"/> is null.</summary>
    public PixelFormat PreferredPixelFormat { get; init; }

    /// <summary>
    /// Number of frames, images or icon entries in the container. One for a plain still image,
    /// zero when the count cannot be known without a full parse.
    /// </summary>
    public int FrameCount { get; init; } = 1;

    /// <summary>Whether <see cref="FrameCount"/> frames form an animation rather than independent images.</summary>
    public bool IsAnimated { get; init; }

    /// <summary>Number of stored mipmap levels, one when the format has no mip chain.</summary>
    public int MipmapCount { get; init; } = 1;

    /// <summary>The transform needed to bring stored pixels into display order.</summary>
    public ExifOrientation Orientation { get; init; } = ExifOrientation.Unspecified;

    /// <summary>Horizontal resolution in dots per inch, zero when the file does not say.</summary>
    public double HorizontalDpi { get; init; }

    /// <summary>Vertical resolution in dots per inch, zero when the file does not say.</summary>
    public double VerticalDpi { get; init; }

    /// <summary>Which camera raw flavour this is, for <see cref="ImageFormat.Raw"/> files.</summary>
    public RawVariant RawVariant { get; init; }

    /// <summary>
    /// Width of the region the file asks to be displayed, or zero where it carries no clean
    /// aperture. <see cref="Width"/> stays the stored frame, since that is the buffer a decode
    /// produces, and cropping to this rectangle is the caller's choice.
    /// </summary>
    public int DisplayWidth { get; init; }

    /// <summary>Height of the region the file asks to be displayed, zero when it has no clean aperture.</summary>
    public int DisplayHeight { get; init; }

    /// <summary>
    /// Name of the compression the file uses, in the format's own vocabulary, for example
    /// "Progressive", "Deflate", "PIZ", "BC7" or "RLE". Null when there is nothing meaningful to say.
    /// </summary>
    public string? Compression { get; init; }

    /// <summary>Total pixels in the primary image, as a long so the multiply cannot overflow.</summary>
    public long PixelCount => (long)Width * Height;
}
