using System;
using System.Diagnostics;
using System.Threading;

namespace Prowl.Graphite;

/// <summary>
/// Interned vertex attribute name (HLSL semantic on D3D, attribute name on GL). Cheap int wrapper.
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

    private static int _counter;
    private static readonly Interner<string, VertexAttributeID> s_interner =
        new(static _ => new VertexAttributeID(Interlocked.Increment(ref _counter)));

    /// <summary>
    /// Gets or mints the ID for a name.
    /// </summary>
    public static VertexAttributeID Intern(string name) => s_interner.Intern(name);

    /// <summary>
    /// Slow reverse lookup. Null if never interned.
    /// </summary>
    public static string? ToString(VertexAttributeID id)
        => s_interner.TryGetKey(id, out string? key) ? key : null;

    /// <summary>
    /// Implicit string-to-ID conversion. Same as Intern.
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
    /// Hot-path safe, doesn't touch interner. Use the static ToString overload for the real string.
    /// </summary>
    public override string ToString()
        => $"VertexAttributeID({Value})";

    /// <summary>
    /// IFormattable conformance, ignores format and provider.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
        => ToString();
}
