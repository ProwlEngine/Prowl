using System;

namespace Prowl.Veldrid;

/// <summary>
/// Describes a compute <see cref="Pipeline"/>, for creation using a <see cref="ResourceFactory"/>.
/// All compute state lives on the wrapped <see cref="ComputeProgram"/>.
/// </summary>
public struct ComputePipelineDescription : IEquatable<ComputePipelineDescription>
{
    /// <summary>
    /// The <see cref="ComputeProgram"/> wrapped by this pipeline.
    /// </summary>
    public ComputeProgram Program;

    /// <summary>
    /// Constructs a new <see cref="ComputePipelineDescription"/>.
    /// </summary>
    /// <param name="program">The compute program to wrap.</param>
    public ComputePipelineDescription(ComputeProgram program)
    {
        Program = program;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    public bool Equals(ComputePipelineDescription other)
    {
        return ReferenceEquals(Program, other.Program);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        return Program?.GetHashCode() ?? 0;
    }
}
