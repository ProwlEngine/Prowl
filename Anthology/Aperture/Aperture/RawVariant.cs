// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// Which camera raw flavour a <see cref="ImageFormat.Raw"/> file is. Most of these are TIFF
/// derivatives that differ in their maker notes and compression, so the container check alone
/// cannot tell them apart.
/// </summary>
public enum RawVariant
{
    /// <summary>Not a raw file, or the flavour could not be determined.</summary>
    None = 0,

    /// <summary>Adobe Digital Negative.</summary>
    Dng,

    /// <summary>Canon raw v2 (TIFF based).</summary>
    Cr2,

    /// <summary>Canon raw v3 (ISOBMFF based).</summary>
    Cr3,

    /// <summary>Nikon electronic format.</summary>
    Nef,

    /// <summary>Sony alpha raw.</summary>
    Arw,

    /// <summary>Olympus raw format.</summary>
    Orf,

    /// <summary>Panasonic raw v2.</summary>
    Rw2,

    /// <summary>Fujifilm raw, with an X-Trans colour filter array on most bodies.</summary>
    Raf,

    /// <summary>Pentax raw.</summary>
    Pef,

    /// <summary>Samsung raw.</summary>
    Srw,
}
