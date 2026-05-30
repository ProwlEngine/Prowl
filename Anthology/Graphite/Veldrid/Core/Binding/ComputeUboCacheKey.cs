using System;
using System.Runtime.CompilerServices;

namespace Prowl.Veldrid;

/// <summary>
/// Cache key for per-command-buffer implicit UBO deduplication for compute dispatches.
/// Mirrors <see cref="UboCacheKey"/> but keyed by <see cref="ComputeProgram"/> instead of
/// <see cref="ShaderProgram"/> since they are separate type hierarchies.
/// </summary>
internal readonly struct ComputeUboCacheKey : IEquatable<ComputeUboCacheKey>
{
    public readonly ComputeProgram Program;
    public readonly uint Set;
    public readonly int Binding;
    public readonly uint UniformVersion;

    public ComputeUboCacheKey(ComputeProgram program, uint set, int binding, uint uniformVersion)
    {
        Program = program;
        Set = set;
        Binding = binding;
        UniformVersion = uniformVersion;
    }

    public bool Equals(ComputeUboCacheKey other)
        => Program.Equals(other.Program) && Set == other.Set && Binding == other.Binding && UniformVersion == other.UniformVersion;

    public override bool Equals(object? obj)
        => obj is ComputeUboCacheKey k && Equals(k);

    public override int GetHashCode() => HashCode.Combine(
        RuntimeHelpers.GetHashCode(Program),
        Set,
        Binding,
        UniformVersion);
}
