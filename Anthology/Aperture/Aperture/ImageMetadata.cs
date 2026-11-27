// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// Ancillary blocks lifted from a container and handed back untouched. Aperture does not parse
/// Exif or XMP beyond the orientation tag it needs, so callers that care can pass these to a
/// dedicated library.
/// </summary>
public sealed class ImageMetadata
{
    /// <summary>Raw Exif segment, starting at the TIFF header, or null if the file had none.</summary>
    public byte[]? Exif { get; init; }

    /// <summary>Raw XMP packet as UTF-8 XML, or null if the file had none.</summary>
    public byte[]? Xmp { get; init; }

    /// <summary>Embedded ICC colour profile, or null if the file had none.</summary>
    public byte[]? IccProfile { get; init; }

    /// <summary>
    /// Textual key/value pairs the container defines, such as PNG tEXt chunks or Radiance HDR
    /// header lines. Empty when there are none.
    /// </summary>
    public IReadOnlyDictionary<string, string> TextEntries { get; init; } = EmptyText;

    private static readonly Dictionary<string, string> EmptyText = [];

    /// <summary>An instance with nothing in it, shared to avoid allocating per decode.</summary>
    public static ImageMetadata Empty { get; } = new();
}
