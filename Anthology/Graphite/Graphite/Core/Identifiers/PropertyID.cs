using System;
using System.Diagnostics;
using System.Threading;

namespace Prowl.Graphite;

/// <summary>
/// Interned shader binding or uniform field ID; cheap int wrapper.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct PropertyID : IEquatable<PropertyID>, IFormattable
{
    internal readonly int Value;

    internal PropertyID(int value) { Value = value; }

    /// <summary>
    /// True if interned, false if default.
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
    /// Reverse lookup; null if not interned.
    /// </summary>
    public static string? ToString(PropertyID id)
        => s_interner.TryGetKey(id, out string? key) ? key : null;

    /// <summary>
    /// String-to-ID conversion via Intern.
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
    /// Hot-path safe; use static ToString for the name.
    /// </summary>
    public override string ToString()
        => $"ResourceID({Value})";

    /// <summary>
    /// Implements IFormattable; ignores format/provider.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
        => ToString();
}
