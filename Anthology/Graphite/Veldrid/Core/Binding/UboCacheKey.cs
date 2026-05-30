using System;

namespace Prowl.Veldrid;

/// <summary>
/// Cache key for per-command-buffer implicit UBO deduplication, shared by graphics draws and
/// compute dispatches. Keyed by the program, set index, binding index, and the merged table's
/// uniform version so that UBO uploads are skipped when uniform state is unchanged.
/// </summary>
internal readonly struct UboCacheKey : IEquatable<UboCacheKey>
{
    public readonly ShaderProgram Shader;
    public readonly uint Set;
    public readonly int Binding;
    public readonly uint UniformVersion;

    public UboCacheKey(ShaderProgram shader, uint set, int binding, uint uniformVersion)
    {
        Shader = shader;
        Set = set;
        Binding = binding;
        UniformVersion = uniformVersion;
    }

    public bool Equals(UboCacheKey other)
        => ReferenceEquals(Shader, other.Shader)
        && Set == other.Set
        && Binding == other.Binding
        && UniformVersion == other.UniformVersion;

    public override bool Equals(object? obj) => obj is UboCacheKey k && Equals(k);

    public override int GetHashCode() => HashCode.Combine(
        System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Shader),
        Set,
        Binding,
        UniformVersion);
}
