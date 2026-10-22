using System;

namespace Prowl.Graphite;

/// <summary>What an instance stands for, such as a world or a camera. Interned, so its name resolves without the graph.</summary>
public readonly struct GraphKind : IEquatable<GraphKind>
{
    private static readonly Interner s_interner = new();

    internal readonly int Value;

    private GraphKind(int value) => Value = value;

    /// <summary>The kind of every graph's root instance.</summary>
    public static readonly GraphKind Root = Intern("Graphite.Root");

    /// <summary>Gets or mints the kind for a name.</summary>
    public static GraphKind Intern(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new GraphKind(s_interner.Intern(name));
    }

    /// <summary>The name the kind was interned with, empty for the default kind.</summary>
    public string Name => s_interner.TryGetKey(Value, out string? key) ? key : string.Empty;

    /// <summary>False for the default kind.</summary>
    public bool IsValid => Value != 0;

    public bool Equals(GraphKind other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is GraphKind other && Equals(other);

    public override int GetHashCode() => Value;

    public static bool operator ==(GraphKind a, GraphKind b) => a.Value == b.Value;

    public static bool operator !=(GraphKind a, GraphKind b) => a.Value != b.Value;

    public override string ToString() => Name;
}

/// <summary>Identity of an instance across executions. History and persistent resources are kept per key.</summary>
public readonly record struct GraphInstanceKey(GraphKind Kind, int Id)
{
    public override string ToString() => $"{Kind.Name} {Id}";
}

/// <summary>Pixel size an instance's relative textures are sized from. Zero for an instance with none.</summary>
public readonly record struct GraphExtent(uint Width, uint Height)
{
    /// <summary>False when the instance has no size.</summary>
    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>Declares that instances of a kind run under an instance of its parent kind.</summary>
public readonly record struct GraphScope(GraphKind Kind, GraphKind Parent);
