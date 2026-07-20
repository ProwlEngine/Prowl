using System;
using System.Diagnostics;
using System.Threading;

namespace Prowl.Graphite;

/// <summary>
/// ID for a shader/program name. Cheap wrapper around a process-wide int.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct RenderResourceID : IEquatable<RenderResourceID>, IFormattable
{
    internal readonly int Value;

    internal RenderResourceID(int value) { Value = value; }

    /// <summary>
    /// True for any interned ID. False for default.
    /// </summary>
    public bool IsValid => Value != 0;

    private static int _counter;
    private static readonly Interner<string, RenderResourceID> s_interner =
        new(static _ => new RenderResourceID(Interlocked.Increment(ref _counter)));

    /// <summary>
    /// Gets the ID for name, minting one if it's new.
    /// </summary>
    public static RenderResourceID Intern(string name)
        => s_interner.Intern(name);

    /// <summary>
    /// Slow reverse lookup. Returns the original string for id, or null if not interned.
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
    /// Hot-path safe, doesn't touch the interner. Use the static ToString overload to get the original string.
    /// </summary>
    public override string ToString()
        => $"RenderResourceID({Value})";

    /// <summary>
    /// IFormattable support. Format and provider are ignored.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
        => ToString();
}
