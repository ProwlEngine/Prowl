using System;
using System.Diagnostics;

namespace Prowl.Graphite;

/// <summary>
/// Interned vertex attribute name; cheap int wrapper.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct VertexAttributeID : IEquatable<VertexAttributeID>, IFormattable
{
    internal readonly int Value;

    internal VertexAttributeID(int value) { Value = value; }

    /// <summary>
    /// True if interned, false if default.
    /// </summary>
    public bool IsValid => Value != 0;

    private static readonly Interner s_interner = new();

    /// <summary>
    /// Gets or mints the ID for a name.
    /// </summary>
    public static VertexAttributeID Intern(string name) => new VertexAttributeID(s_interner.Intern(name));

    /// <summary>
    /// Slow reverse lookup. Null if never interned.
    /// </summary>
    public static string? ToString(VertexAttributeID id)
        => s_interner.TryGetKey(id.Value, out string? key) ? key : null;

    /// <summary>
    /// String-to-ID conversion via Intern.
    /// </summary>
    public static implicit operator VertexAttributeID(string name)
        => Intern(name);

    /// <inheritdoc/>
    public bool Equals(VertexAttributeID other)
        => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is VertexAttributeID o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode()
        => Value;

    /// <inheritdoc/>
    public static bool operator ==(VertexAttributeID a, VertexAttributeID b)
        => a.Value == b.Value;

    /// <inheritdoc/>
    public static bool operator !=(VertexAttributeID a, VertexAttributeID b)
        => a.Value != b.Value;

    /// <summary>
    /// Hot-path safe; use static ToString for the name.
    /// </summary>
    public override string ToString()
        => $"VertexAttributeID({Value})";

    /// <summary>
    /// Implements IFormattable; ignores format/provider.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
        => ToString();
}
