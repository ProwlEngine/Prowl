namespace Prowl.Motion;

/// <summary>
/// A hashed string identifier used for bone names, event IDs, and parameter names.
/// Cheap value type with fast equality; retains the original string (used by avatar auto-mapping
/// and debugging).
/// </summary>
public readonly struct StringID : IEquatable<StringID>
{
    /// <summary>The invalid / unset id (hash 0).</summary>
    public static readonly StringID Invalid = default;

    private readonly uint _id;
    private readonly string? _name;

    /// <summary>Creates an id by hashing the given string (FNV-1a 32-bit).</summary>
    public StringID(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _name = value;
        _id = Hash(value);
    }

    /// <summary>Creates an id directly from a precomputed hash (no retained name).</summary>
    public StringID(uint id)
    {
        _id = id;
        _name = null;
    }

    /// <summary>The 32-bit hash value.</summary>
    public uint ID => _id;

    /// <summary>True if this id is not the invalid value.</summary>
    public bool IsValid => _id != 0;

    /// <summary>The original string, if this id was created from one; otherwise null.</summary>
    public string? DebugName => _name;

    public bool Equals(StringID other) => _id == other._id;
    public override bool Equals(object? obj) => obj is StringID other && _id == other._id;
    public override int GetHashCode() => (int)_id;
    public override string ToString() => _name ?? _id.ToString("X8");

    public static bool operator ==(StringID left, StringID right) => left._id == right._id;
    public static bool operator !=(StringID left, StringID right) => left._id != right._id;

    private static uint Hash(string value)
    {
        // FNV-1a 32-bit.
        const uint offset = 2166136261u;
        const uint prime = 16777619u;
        uint hash = offset;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= prime;
        }
        return hash;
    }
}
