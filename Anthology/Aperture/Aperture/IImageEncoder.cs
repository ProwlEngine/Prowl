// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// Writes one container format. Hand an implementation to
/// <see cref="ImageEncoderRegistry.Register"/> to add a format or replace a built in encoder.
/// Implementations must not throw for a layout they cannot write; they report
/// <see cref="ApertureError.UnsupportedFeature"/> and leave the stream as they found it.
/// </summary>
public interface IImageEncoder
{
    /// <summary>The container this encoder writes.</summary>
    ImageFormat Format { get; }

    /// <summary>Extension to give a file of this format, leading dot included, lowercase.</summary>
    string FileExtension { get; }

    /// <summary>
    /// The layouts this encoder writes without converting first. A frame in any other layout is
    /// converted to <see cref="PreferredPixelFormat"/> on the way in.
    /// </summary>
    IReadOnlyList<PixelFormat> SupportedPixelFormats { get; }

    /// <summary>What an unsupported layout is converted to before writing.</summary>
    PixelFormat PreferredPixelFormat { get; }

    /// <summary>
    /// Writes <paramref name="frame"/> to <paramref name="destination"/>. Nothing is written when
    /// this returns false.
    /// </summary>
    bool TryEncode(ImageFrame frame, EncodeOptions options, Stream destination, out ApertureError error);
}
