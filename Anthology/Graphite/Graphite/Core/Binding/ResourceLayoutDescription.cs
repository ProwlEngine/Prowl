using System;

namespace Prowl.Graphite;

/// <summary>
/// Layout of bindable resources for a GraphicsProgram.
/// </summary>
public struct ResourceLayoutDescription : IEquatable<ResourceLayoutDescription>
{
    /// <summary>
    /// Descriptor set index (Vulkan set / DX12 register space). Ignored on backends without sets.
    /// </summary>
    public uint Set;

    /// <summary>
    /// Per-element layout descriptions for the resources in this set.
    /// </summary>
    public ResourceLayoutElementDescription[] Elements;

    /// <summary>
    /// New ResourceLayoutDescription, set index 0.
    /// </summary>
    /// <param name="elements">Per-element layout descriptions.</param>
    public ResourceLayoutDescription(params ResourceLayoutElementDescription[] elements)
    {
        Set = 0;
        Elements = elements;
    }

    /// <summary>
    /// New ResourceLayoutDescription with explicit set index.
    /// </summary>
    /// <param name="set">Descriptor set index (Vulkan set / DX12 register space).</param>
    /// <param name="elements">Per-element layout descriptions.</param>
    public ResourceLayoutDescription(uint set, params ResourceLayoutElementDescription[] elements)
    {
        Set = set;
        Elements = elements;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare against.</param>
    /// <returns>True if everything matches.</returns>
    public readonly bool Equals(ResourceLayoutDescription other)
        => Set == other.Set && Util.ArrayEqualsEquatable(Elements, other.Elements);

    /// <summary>
    /// Hash code.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override readonly int GetHashCode()
        => HashCode.Combine(Set, Elements.ArrayHash());

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj)
        => obj is ResourceLayoutDescription description && Equals(description);
}
