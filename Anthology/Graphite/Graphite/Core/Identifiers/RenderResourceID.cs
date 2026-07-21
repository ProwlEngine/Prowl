using System;
using System.Diagnostics;
using System.Threading;

namespace Prowl.Graphite;

/// <summary>
/// Interned render graph resource name (declared texture handle). Cheap int wrapper.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct RenderResourceID : IEquatable<RenderResourceID>, IFormattable
{
    internal readonly int Value;

    internal RenderResourceID(int value) { Value = value; }

    /// <summary>
    /// True if interned, false if default.
    /// </summary>
    public bool IsValid => Value != 0;

    private static int _counter;
    private static readonly Interner<string, RenderResourceID> s_interner =
        new(static _ => new RenderResourceID(Interlocked.Increment(ref _counter)));

    /// <summary>
    /// Gets or mints the ID for a name.
    /// </summary>
    public static RenderResourceID Intern(string name)
        => s_interner.Intern(name);

    /// <summary>
    /// Slow reverse lookup. Null if never interned.
    /// </summary>
    public static string? ToString(RenderResourceID id)
        => s_interner.TryGetKey(id, out string? key) ? key : null;

    /// <summary>
    /// Implicit string-to-ID conversion. Same as Intern.
    /// </summary>
    public static implicit operator RenderResourceID(string name)
        => Intern(name);

    /// <inheritdoc/>
    public bool Equals(RenderResourceID other)
        => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is RenderResourceID o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode()
        => Value;

    /// <inheritdoc/>
    public static bool operator ==(RenderResourceID a, RenderResourceID b)
        => a.Value == b.Value;

    /// <inheritdoc/>
    public static bool operator !=(RenderResourceID a, RenderResourceID b)
        => a.Value != b.Value;

    /// <summary>
    /// Hot-path safe, doesn't touch interner. Use the static ToString overload for the real string.
    /// </summary>
    public override string ToString()
        => $"RenderResourceID({Value})";

    /// <summary>
    /// IFormattable conformance, ignores format and provider.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
        => ToString();
}
