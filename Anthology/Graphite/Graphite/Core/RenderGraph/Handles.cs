using System;

namespace Prowl.Graphite;

/// <summary>A version of a texture in the frame. Every write returns a new one.</summary>
public readonly struct TextureHandle : IEquatable<TextureHandle>
{
    internal readonly int Index;
    internal readonly int Version;

    internal TextureHandle(RenderResourceID id, int index, int version)
    {
        Id = id;
        Index = index;
        Version = version;
    }

    public RenderResourceID Id { get; }

    /// <summary>False for a default handle.</summary>
    public bool IsValid => Id.IsValid;

    public bool Equals(TextureHandle other) => Index == other.Index && Version == other.Version && Id == other.Id;

    public override bool Equals(object? obj) => obj is TextureHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Index, Version);

    public static bool operator ==(TextureHandle a, TextureHandle b) => a.Equals(b);

    public static bool operator !=(TextureHandle a, TextureHandle b) => !a.Equals(b);
}

/// <summary>A version of a buffer in the frame. Every write returns a new one.</summary>
public readonly struct BufferHandle : IEquatable<BufferHandle>
{
    internal readonly int Index;
    internal readonly int Version;

    internal BufferHandle(RenderResourceID id, int index, int version)
    {
        Id = id;
        Index = index;
        Version = version;
    }

    public RenderResourceID Id { get; }

    /// <summary>False for a default handle.</summary>
    public bool IsValid => Id.IsValid;

    public bool Equals(BufferHandle other) => Index == other.Index && Version == other.Version && Id == other.Id;

    public override bool Equals(object? obj) => obj is BufferHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Index, Version);

    public static bool operator ==(BufferHandle a, BufferHandle b) => a.Equals(b);

    public static bool operator !=(BufferHandle a, BufferHandle b) => !a.Equals(b);
}
