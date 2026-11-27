// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// Why an identify or decode attempt failed. Every failure path returns one of these rather than
/// throwing, so a caller walking a directory of untrusted files never pays for exception unwinding.
/// </summary>
public enum ApertureError
{
    /// <summary>The operation succeeded.</summary>
    None = 0,

    /// <summary>The data matched no known container signature.</summary>
    UnknownFormat,

    /// <summary>The container was recognised but no decoder is registered for it.</summary>
    NotSupported,

    /// <summary>
    /// A decoder is registered and parsed the header, but pixel decoding for this container is
    /// not written yet. Distinct from <see cref="NotSupported"/> so callers can tell "Aperture
    /// has never heard of this" from "Aperture knows this but cannot decode it yet".
    /// </summary>
    NotImplemented,

    /// <summary>
    /// The file is well formed but uses a feature this decoder does not implement, such as an
    /// arithmetic coded JPEG or an unhandled TIFF compression scheme.
    /// </summary>
    UnsupportedFeature,

    /// <summary>A header field is structurally invalid, out of range or self contradictory.</summary>
    InvalidHeader,

    /// <summary>A bit depth the format does not allow, or that this decoder rejects for the given colour type.</summary>
    InvalidBitDepth,

    /// <summary>A colour type or sample layout the format does not define.</summary>
    InvalidColorType,

    /// <summary>Width or height is zero, negative or otherwise unusable.</summary>
    InvalidDimensions,

    /// <summary>Pixel or metadata payload is corrupt beyond what the decoder can recover from.</summary>
    InvalidData,

    /// <summary>The stream ended before a structure that the header promised was complete.</summary>
    UnexpectedEndOfData,

    /// <summary>A CRC, checksum or digest embedded in the file did not match the data it covers.</summary>
    ChecksumMismatch,

    /// <summary>An entropy or dictionary coded stream could not be unpacked.</summary>
    DecompressionFailed,

    /// <summary>
    /// The file asks for more than <see cref="DecodeOptions"/> permits: too many pixels, frames or
    /// bytes. Raised before any large allocation happens, so a decompression bomb costs nothing.
    /// </summary>
    LimitExceeded,

    /// <summary>An allocation inside the permitted limits still failed.</summary>
    OutOfMemory,

    /// <summary>The underlying stream or file could not be read.</summary>
    IoError,

    /// <summary>The container is valid but holds no image, for example an ICO with zero entries.</summary>
    NoImageData,
}
