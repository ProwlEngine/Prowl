// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders;

/// <summary>
/// Shared plumbing for the built in decoders. Header parsing lives in
/// <see cref="ParseHeader"/>; pixel decoding is opt in through <see cref="CanDecodePixels"/> so a
/// decoder can ship identification support before its codec is written.
/// </summary>
public abstract class DecoderBase : IImageDecoder
{
    /// <inheritdoc />
    public abstract ImageFormat Format { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<string> FileExtensions { get; }

    /// <inheritdoc />
    public virtual bool CanDecodePixels => false;

    /// <summary>
    /// Reads the container header. Implementations must treat every field as hostile: bounds
    /// check before indexing, reject impossible geometry, and never loop on a value the file
    /// controls without a hard cap.
    /// </summary>
    protected abstract bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error);

    /// <summary>
    /// Decodes pixels. The base implementation reports <see cref="ApertureError.NotImplemented"/>;
    /// override it together with <see cref="CanDecodePixels"/> once the codec exists.
    /// </summary>
    protected virtual bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                        ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;
        error = ApertureError.NotImplemented;
        return false;
    }

    /// <inheritdoc />
    public bool TryIdentify(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        error = ApertureError.None;
        if (data.IsEmpty)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (!ParseHeader(data, out info, out error))
        {
            info = null;
            if (error == ApertureError.None)
                error = ApertureError.InvalidHeader;
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> data, DecodeOptions options, out Image? image, out ApertureError error)
    {
        ArgumentNullException.ThrowIfNull(options);
        image = null;

        if (!TryIdentify(data, out ImageInfo? info, out error))
            return false;

        if (!options.IsWithinLimits(info!.Width, info.Height, Math.Max(1, info.PreferredPixelFormat.BytesPerPixel()), out error))
            return false;

        if (!DecodePixels(data, options, info, out image, out error))
            return false;

        // A format that hands back the rows as the file stored them leaves any recorded
        // orientation outstanding, and a caller may ask for it to be settled here.
        if (options.ApplyExifOrientation && OrientationIsPending && image is not null &&
            OrientationPass.Turns(info.Orientation))
        {
            if (!OrientationPass.TryApply(image, options, info.Orientation, out Image? turned, out error))
            {
                // The turn failed, so the untouched image still owns its buffers and nobody is
                // going to be handed them.
                image.Dispose();
                image = null;
                return false;
            }

            image = turned;
        }

        // Reading the ancillary blocks is the same work whatever came out of the pixels, so it
        // happens here rather than in each decoder, where one of them would eventually forget.
        if (options.ReadMetadata && image is not null)
            image.Metadata = ReadMetadata(data);

        return true;
    }

    /// <summary>
    /// Whether the pixels this decoder returns are still in the order the file stored them, so a
    /// recorded orientation is work outstanding rather than a fact already dealt with. A format
    /// that turns its own rows the right way round, as Radiance and the bitmap both do, says no
    /// and reports its orientation as a description of the file.
    /// </summary>
    protected virtual bool OrientationIsPending => false;

    /// <summary>
    /// Lifts whatever ancillary blocks the container carries. The default finds none, which is
    /// the right answer for a format that has nowhere to put them.
    /// </summary>
    protected virtual ImageMetadata ReadMetadata(ReadOnlySpan<byte> data) => ImageMetadata.Empty;

    /// <summary>Rejects geometry that no decoder should ever try to allocate for.</summary>
    protected static bool ValidateDimensions(long width, long height, out ApertureError error)
    {
        if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        error = ApertureError.None;
        return true;
    }

    /// <summary>
    /// Picks the smallest lossless pixel layout that can hold the described samples. Alpha can
    /// arrive from outside the channel count, as a PNG transparency chunk does, so it is passed
    /// separately and promotes the result to a layout that has somewhere to put it.
    /// </summary>
    protected static PixelFormat ChoosePixelFormat(int channels, int bitsPerChannel, bool floatingPoint,
                                                   bool hasAlpha = false)
    {
        if (hasAlpha)
            channels = channels <= 2 ? 2 : 4;

        if (floatingPoint)
        {
            return bitsPerChannel <= 16
                ? channels >= 4 ? PixelFormat.RgbaF16 : PixelFormat.RgbF16
                : channels switch
                {
                    1 => PixelFormat.LF32,
                    >= 4 => PixelFormat.RgbaF32,
                    _ => PixelFormat.RgbF32,
                };
        }

        bool wide = bitsPerChannel > 8;
        return channels switch
        {
            <= 1 => wide ? PixelFormat.L16 : PixelFormat.L8,
            2 => wide ? PixelFormat.La16 : PixelFormat.La8,
            3 => wide ? PixelFormat.Rgb16 : PixelFormat.Rgb8,
            _ => wide ? PixelFormat.Rgba16 : PixelFormat.Rgba8,
        };
    }
}
