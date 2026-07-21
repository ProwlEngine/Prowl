using System;

namespace Prowl.Graphite;

/// <summary>Bitmask of writable color components.</summary>
[Flags]
public enum ColorWriteMask
{
    /// <summary>Write nothing.</summary>
    None,

    /// <summary>Write red.</summary>
    Red = 1 << 0,

    /// <summary>Write green.</summary>
    Green = 1 << 1,

    /// <summary>Write blue.</summary>
    Blue = 1 << 2,

    /// <summary>Write alpha.</summary>
    Alpha = 1 << 3,

    /// <summary>Write everything.</summary>
    All = Red | Green | Blue | Alpha,
}
