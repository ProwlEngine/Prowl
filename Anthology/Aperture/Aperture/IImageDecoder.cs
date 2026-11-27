// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// Reads one container format. Hand an implementation to
/// <see cref="ImageDecoderRegistry.Register"/> to add a format or replace a built in decoder.
/// Implementations must not throw for malformed input, and must honour the
/// <see cref="DecodeOptions"/> limits before allocating rather than after.
/// </summary>
public interface IImageDecoder
{
    /// <summary>The container this decoder reads.</summary>
    ImageFormat Format { get; }

    /// <summary>
    /// Extensions conventionally used for this format, leading dot included, lowercase. Purely
    /// informational; detection is driven by content.
    /// </summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Whether pixel decoding is implemented. When false the decoder still reports header facts
    /// through <see cref="TryIdentify"/> and <see cref="TryDecode"/> fails with
    /// <see cref="ApertureError.NotImplemented"/>.
    /// </summary>
    bool CanDecodePixels { get; }

    /// <summary>Reads header facts without decoding pixels.</summary>
    bool TryIdentify(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error);

    /// <summary>Decodes the image, honouring the limits and conversions in <paramref name="options"/>.</summary>
    bool TryDecode(ReadOnlySpan<byte> data, DecodeOptions options, out Image? image, out ApertureError error);
}
