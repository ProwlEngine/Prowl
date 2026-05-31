using System;
using System.Runtime.CompilerServices;

namespace Prowl.Veldrid;

/// <summary>
/// A read-only, array-backed set of <see cref="ProfileCounter"/> values indexed by an enum.
/// The backing array is sized to <c>Enum.GetValues&lt;TBin&gt;().Length</c>; indexing uses a
/// reinterpret cast of the enum to its <see cref="int"/> backing, so there are no dictionaries
/// and no boxing.
/// </summary>
public readonly struct ProfileBinGroup<TBin> where TBin : unmanaged, Enum
{
    private readonly ProfileCounter[] _counters;

    internal ProfileBinGroup(ProfileCounter[] counters)
    {
        _counters = counters;
    }

    /// <summary>
    /// The counter for the given bin. Returns a zeroed counter when profiling is disabled
    /// (the snapshot carries no backing storage).
    /// </summary>
    public ProfileCounter this[TBin bin]
        => _counters is null ? default : _counters[Unsafe.As<TBin, int>(ref bin)];
}
