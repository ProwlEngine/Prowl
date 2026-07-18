// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Interned identifier for a render-graph texture resource (e.g. "SceneColor", "Depth").
/// A cheap value-type wrapper around a process-wide integer minted the first time a name is seen.
/// Two passes that reference the same name share the same physical resource, which is how the
/// dependency graph links a pass that writes a resource to the passes that read it.
/// </summary>
public readonly struct RenderResourceID : IEquatable<RenderResourceID>
{
    private readonly int _value;

    private RenderResourceID(int value) => _value = value;

    /// <summary>True for any interned id. False for <c>default</c>.</summary>
    public bool IsValid => _value != 0;

    private static int s_counter;
    private static readonly ConcurrentDictionary<string, RenderResourceID> s_ids = new();
    private static readonly ConcurrentDictionary<int, string> s_names = new();

    /// <summary>Returns the id for <paramref name="name"/>, minting one on first use.</summary>
    public static RenderResourceID Intern(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Resource name cannot be null or empty.", nameof(name));

        return s_ids.GetOrAdd(name, static n =>
        {
            int id = Interlocked.Increment(ref s_counter);
            s_names[id] = n;
            return new RenderResourceID(id);
        });
    }

    /// <summary>Slow reverse lookup of the original name, or null if never interned.</summary>
    public static string? NameOf(RenderResourceID id)
        => s_names.TryGetValue(id._value, out string? name) ? name : null;

    /// <summary>Implicit string-to-id conversion. Equivalent to <see cref="Intern(string)"/>.</summary>
    public static implicit operator RenderResourceID(string name) => Intern(name);

    /// <inheritdoc/>
    public bool Equals(RenderResourceID other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RenderResourceID o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => _value;

    public static bool operator ==(RenderResourceID a, RenderResourceID b) => a._value == b._value;

    public static bool operator !=(RenderResourceID a, RenderResourceID b) => a._value != b._value;

    /// <inheritdoc/>
    public override string ToString() => NameOf(this) ?? $"RenderResourceID({_value})";
}
