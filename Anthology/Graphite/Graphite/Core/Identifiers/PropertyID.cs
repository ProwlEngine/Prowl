using System;
using System.Diagnostics;
using System.Threading;

namespace Prowl.Graphite;

/// <summary>
/// Interned ID for a shader binding name or uniform field. Cheap wrapper around a process-wide int.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct PropertyID : IEquatable<PropertyID>, IFormattable
{
    internal readonly int Value;

    internal PropertyID(int value) { Value = value; }

    /// <summary>
    /// True if interned. False for default.
    /// </summary>
    public bool IsValid => Value != 0;

    private static int _counter;
    private static readonly Interner<string, PropertyID> s_interner =
        new(static _ => new PropertyID(Interlocked.Increment(ref _counter)));

    /// <summary>
    /// Gets or mints the ID for name.
    /// </summary>
    public static PropertyID Intern(string name) => s_interner.Intern(name);

    /// <summary>
    /// Slow reverse lookup. Original string for id, or null if never interned.
    /// </summary>
    public static string? ToString(PropertyID id)
        => s_interner.TryGetKey(id, out string? key) ? key : null;

    /// <summary>
    /// Implicit string-to-ID conversion. Same as Intern.
    /// </summary>
    public static implicit operator PropertyID(string name) => Intern(name);

    /// <inheritdoc/>
    public bool Equals(PropertyID other)
        => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is PropertyID o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode()
        => Value;

    /// <inheritdoc/>
    public static bool operator ==(PropertyID a, PropertyID b)
        => a.Value == b.Value;

    /// <inheritdoc/>
    public static bool operator !=(PropertyID a, PropertyID b)
        => a.Value != b.Value;

    /// <summary>
    /// Hot-path safe, doesn't touch the interner. Use static ToString(id) for the original string.
    /// </summary>
    public override string ToString()
        => $"ResourceID({Value})";

    /// <summary>
    /// IFormattable conformance. Format and provider ignored.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
        => ToString();
}
