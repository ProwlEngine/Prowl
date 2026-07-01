using System;
using System.Diagnostics;
using System.Threading;

namespace Prowl.Graphite;

/// <summary>
/// Interned identifier for a vertex attribute name (HLSL semantic on D3D, attribute name on GL).
/// Cheap value-type wrapper around a process-wide integer minted by an internal interner.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct VertexAttributeID : IEquatable<VertexAttributeID>, IFormattable
{
    internal readonly int Value;

    internal VertexAttributeID(int value) { Value = value; }

    /// <summary>
    /// True for any ID returned from <see cref="Intern(string)"/>. False for <c>default</c>.
    /// </summary>
    public bool IsValid => Value != 0;

    private static int _counter;
    private static readonly Interner<string, VertexAttributeID> s_interner =
        new(static _ => new VertexAttributeID(Interlocked.Increment(ref _counter)));

    /// <summary>
    /// Returns the ID for <paramref name="name"/>, minting one if this is the first time
    /// this string has been seen.
    /// </summary>
    public static VertexAttributeID Intern(string name) => s_interner.Intern(name);

    /// <summary>
    /// Slow reverse lookup. Returns the original string for <paramref name="id"/>, or
    /// null if no such ID has been interned in this process.
    /// </summary>
    public static string? ToString(VertexAttributeID id)
        => s_interner.TryGetKey(id, out string? key) ? key : null;

    /// <summary>
    /// Implicit string-to-ID conversion. Equivalent to <see cref="Intern(string)"/>.
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
    /// Hot-path safe. Does not touch the interner. Use the static <see cref="ToString(VertexAttributeID)"/>
    /// overload to retrieve the original interned string.
    /// </summary>
    public override string ToString()
        => $"VertexAttributeID({Value})";

    /// <summary>
    /// <see cref="IFormattable"/> conformance. Format and provider are ignored.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
        => ToString();
}
