using System;

namespace Prowl.Veldrid;

/// <summary>
/// Describes a <see cref="CommandBuffer"/>, for creation using a <see cref="ResourceFactory"/>.
/// </summary>
public struct CommandBufferDescription : IEquatable<CommandBufferDescription>
{
    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">The instance to compare to.</param>
    /// <returns>True if all elements are equal; false otherswise.</returns>
    public bool Equals(CommandBufferDescription other)
    {
        return true;
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
    public override int GetHashCode()
    {
        return base.GetHashCode();
    }
}
