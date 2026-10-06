using System;
using System.Threading;

namespace Prowl.Graphite.Debugging;

/// <summary>Stable identifier of a buffer or texture for its lifetime. Never reused.</summary>
public readonly struct ResourceId : IEquatable<ResourceId>, IComparable<ResourceId>
{
    private static long s_next;

    /// <summary>Raw identifier. Zero is never assigned.</summary>
    public ulong Value { get; }

    private ResourceId(ulong value)
    {
        Value = value;
    }

    internal static ResourceId Next() => new((ulong)Interlocked.Increment(ref s_next));

    public bool Equals(ResourceId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is ResourceId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public int CompareTo(ResourceId other) => Value.CompareTo(other.Value);

    public override string ToString() => $"ResourceId({Value})";

    public static bool operator ==(ResourceId a, ResourceId b) => a.Value == b.Value;

    public static bool operator !=(ResourceId a, ResourceId b) => a.Value != b.Value;
}

/// <summary>A resource at one point in its content history.</summary>
public readonly record struct ResourceVersion(ResourceId Resource, uint Version);
