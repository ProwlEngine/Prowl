using System;
using System.Diagnostics;
using System.Threading;

namespace Prowl.Veldrid;

/// <summary>
/// Interned identifier for a uniform-block field name. Cheap value-type wrapper around
/// a process-wide integer minted by an internal interner.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct UniformID : IEquatable<UniformID>, IFormattable
{
    internal readonly int Value;

    internal UniformID(int value) { Value = value; }

    /// <summary>
    /// True for any ID returned from <see cref="Intern(string)"/>. False for <c>default</c>.
    /// </summary>
    public bool IsValid => Value != 0;

    private static int _counter;
    private static readonly Interner<string, UniformID> s_interner =
        new(static _ => new UniformID(Interlocked.Increment(ref _counter)));

    /// <summary>
    /// Returns the ID for <paramref name="name"/>, minting one if this is the first time
    /// this string has been seen.
    /// </summary>
    public static UniformID Intern(string name) => s_interner.Intern(name);

    /// <summary>
    /// Slow reverse lookup. Returns the original string for <paramref name="id"/>, or
    /// null if no such ID has been interned in this process.
    /// </summary>
    public static string? ToString(UniformID id)
        => s_interner.TryGetKey(id, out string? key) ? key : null;

    /// <summary>
    /// Implicit string-to-ID conversion. Equivalent to <see cref="Intern(string)"/>.
    /// </summary>
    public static implicit operator UniformID(string name) => Intern(name);

    public bool Equals(UniformID other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is UniformID o && Equals(o);
    public override int GetHashCode() => Value;
    public static bool operator ==(UniformID a, UniformID b) => a.Value == b.Value;
    public static bool operator !=(UniformID a, UniformID b) => a.Value != b.Value;

    /// <summary>
    /// Hot-path safe. Does not touch the interner. Use the static <see cref="ToString(UniformID)"/>
    /// overload to retrieve the original interned string.
    /// </summary>
    public override string ToString() => $"UniformID({Value})";

    /// <summary>
    /// <see cref="IFormattable"/> conformance. Format and provider are ignored.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();
}
