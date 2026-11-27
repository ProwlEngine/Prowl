// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// Caps and conversions applied to a decode. The limits exist because image headers are
/// attacker controlled: a four byte width field can claim two billion pixels. Every decoder
/// checks the declared geometry against these before allocating anything, so a malicious file
/// fails with <see cref="ApertureError.LimitExceeded"/> instead of exhausting the process.
/// </summary>
public sealed class DecodeOptions
{
    /// <summary>Shared instance with the default limits. Treat as immutable.</summary>
    public static DecodeOptions Default { get; } = new();

    /// <summary>
    /// Convert the decoded pixels to this layout. Null keeps whatever the decoder produces
    /// naturally, reported by <see cref="ImageInfo.PreferredPixelFormat"/>.
    /// </summary>
    public PixelFormat? TargetPixelFormat { get; set; }

    /// <summary>Largest accepted width in pixels.</summary>
    public int MaxWidth { get; set; } = 65535;

    /// <summary>Largest accepted height in pixels.</summary>
    public int MaxHeight { get; set; } = 65535;

    /// <summary>
    /// Largest accepted pixel count for a single frame. Bounds the aspect ratio abuse that
    /// slips past <see cref="MaxWidth"/> and <see cref="MaxHeight"/> individually.
    /// </summary>
    public long MaxPixels { get; set; } = 256L * 1024 * 1024;

    /// <summary>Largest total buffer a single decode may allocate for pixel data.</summary>
    public long MaxAllocationBytes { get; set; } = 1L << 30;

    /// <summary>Largest number of frames read from an animation or a multi-image container.</summary>
    public int MaxFrames { get; set; } = 1024;

    /// <summary>
    /// Decode every frame of an animated or multi-image file. When false only the primary image
    /// is decoded, which is what a texture loader normally wants.
    /// </summary>
    public bool DecodeAllFrames { get; set; }

    /// <summary>
    /// Decode the mipmap chain of container formats that store one, currently DDS and KTX style
    /// textures. When false only the largest level is read.
    /// </summary>
    public bool DecodeMipmaps { get; set; }

    /// <summary>
    /// Rotate and flip decoded pixels to match the Exif orientation tag. Off by default, because
    /// a quarter turn trades the width for the height and would put a decode at odds with
    /// <see cref="Image.Identify(string)"/>. The tag is on <see cref="ImageInfo.Orientation"/>
    /// either way.
    /// </summary>
    public bool ApplyExifOrientation { get; set; }

    /// <summary>
    /// Return the rows that were recovered when a stream ends early, instead of failing with
    /// <see cref="ApertureError.UnexpectedEndOfData"/>. Useful for showing a partial download;
    /// a build pipeline usually wants the failure.
    /// </summary>
    public bool AllowTruncated { get; set; }

    /// <summary>
    /// Reject a file whose embedded checksums do not match. When false a bad CRC is ignored as
    /// long as the data still decodes, which is how most viewers behave.
    /// </summary>
    public bool ValidateChecksums { get; set; } = true;

    /// <summary>Keep Exif, XMP and ICC blocks on the decoded <see cref="Image"/>.</summary>
    public bool ReadMetadata { get; set; } = true;

    /// <summary>
    /// Pads every row out to a multiple of this many bytes. One packs the rows with no gap;
    /// setting the row pitch a graphics API reported makes the decoder write straight into that
    /// layout rather than the caller re-striding afterwards.
    /// </summary>
    public int RowAlignment { get; set; } = 1;

    /// <summary>
    /// Writes the bottom row of the image first, which is the order a lower left texture origin
    /// wants. It costs nothing here, where flipping afterwards is a second pass over the image.
    /// </summary>
    public bool FlipVertically { get; set; }

    /// <summary>
    /// Rent pixel memory from the shared array pool rather than allocating it. This is what
    /// makes repeated loads cheap, and it is why <see cref="Image"/> is disposable. Turn it off
    /// when the pixel buffer has to outlive the image it came from.
    /// </summary>
    public bool UsePooledMemory { get; set; } = true;

    /// <summary>Creates a copy so a caller can tweak one knob without mutating a shared instance.</summary>
    public DecodeOptions Clone() => (DecodeOptions)MemberwiseClone();

    /// <summary>
    /// Row length in bytes for a frame of this width, rounded up to <see cref="RowAlignment"/>.
    /// </summary>
    public int GetStride(int width, PixelFormat format)
    {
        int packed = width * format.BytesPerPixel();
        int alignment = RowAlignment;
        if (alignment <= 1)
            return packed;

        // Round up to the next multiple. Powers of two take the cheap path, which is every
        // alignment a graphics API actually asks for.
        if ((alignment & (alignment - 1)) == 0)
            return (packed + alignment - 1) & ~(alignment - 1);

        int remainder = packed % alignment;
        return remainder == 0 ? packed : packed + (alignment - remainder);
    }

    /// <summary>
    /// Checks a declared frame geometry against the limits. Decoders call this before allocating.
    /// </summary>
    public bool IsWithinLimits(int width, int height, int bytesPerPixel, out ApertureError error)
    {
        if (width <= 0 || height <= 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        if (width > MaxWidth || height > MaxHeight || (long)width * height > MaxPixels)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        if ((long)width * height * bytesPerPixel > MaxAllocationBytes)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        error = ApertureError.None;
        return true;
    }
}
