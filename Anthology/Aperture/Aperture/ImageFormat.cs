// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// The container format a byte stream is stored in. This is the format as detected from the
/// file's signature, not the codec used inside it: a <see cref="Dds"/> names a container whose
/// payload may be any of a dozen block compressions.
/// </summary>
public enum ImageFormat
{
    /// <summary>No known signature matched.</summary>
    Unknown = 0,

    /// <summary>Windows/OS2 bitmap.</summary>
    Bmp,

    /// <summary>DirectDraw Surface, a GPU texture container for block-compressed and raw formats.</summary>
    Dds,

    /// <summary>OpenEXR high dynamic range image.</summary>
    Exr,

    /// <summary>Graphics Interchange Format, including animated files.</summary>
    Gif,

    /// <summary>Radiance RGBE high dynamic range image (.hdr / .pic).</summary>
    Hdr,

    /// <summary>Windows icon or cursor, a directory of embedded BMP or PNG images.</summary>
    Ico,

    /// <summary>JPEG, in either JFIF or Exif framing.</summary>
    Jpeg,

    /// <summary>Portable Network Graphics, including the APNG animation extension.</summary>
    Png,

    /// <summary>Netpbm family: PBM, PGM, PPM and PAM, in both ASCII and binary encodings.</summary>
    Pnm,

    /// <summary>Truevision TGA. Has no leading signature, so detection relies on the file footer or the extension.</summary>
    Tga,

    /// <summary>Tagged Image File Format, little or big endian, classic or BigTIFF.</summary>
    Tiff,

    /// <summary>WebP, a RIFF container holding VP8, VP8L or VP8X payloads.</summary>
    Webp,

    /// <summary>Adobe Photoshop document (.psd) or large document (.psb).</summary>
    Psd,

    /// <summary>
    /// Camera raw sensor data. Covers DNG and the vendor formats built on TIFF (CR2, NEF, ARW,
    /// ORF, RW2, RAF) as well as the ISOBMFF-based CR3. Use <see cref="ImageInfo.RawVariant"/>
    /// for the specific flavour.
    /// </summary>
    Raw,
}
