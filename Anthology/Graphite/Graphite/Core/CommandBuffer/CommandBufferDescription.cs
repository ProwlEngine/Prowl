using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a command buffer, for creation via ResourceFactory.
/// </summary>
public struct CommandBufferDescription : IEquatable<CommandBufferDescription>
{
    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare.</param>
    /// <returns>True if equal.</returns>
    public readonly bool Equals(CommandBufferDescription other)
    {
        return true;
    }

    /// <summary>
    /// Hash code for this instance.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override readonly int GetHashCode()
    {
        return base.GetHashCode();
    }
}
