using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes the layout of <see cref="BindableResource"/> objects for a <see cref="GraphicsProgram"/>.
/// </summary>
public struct ResourceLayoutDescription : IEquatable<ResourceLayoutDescription>
{
    /// <summary>
    /// The descriptor set index for this layout. Maps to the Vulkan descriptor set number
    /// (or the equivalent DX12 register space). Ignored on backends that do not use sets.
    /// </summary>
    public uint Set;

    /// <summary>
    /// An array of <see cref="ResourceLayoutElementDescription"/> objects, describing the properties of each resource
    /// element in the <see cref="PropertySet"/>.
    /// </summary>
    public ResourceLayoutElementDescription[] Elements;

    /// <summary>
    /// Constructs a new ResourceLayoutDescription with a default set index of 0.
    /// </summary>
    /// <param name="elements">An array of <see cref="ResourceLayoutElementDescription"/> objects, describing the properties
    /// of each resource element in the <see cref="PropertySet"/>.</param>
    public ResourceLayoutDescription(params ResourceLayoutElementDescription[] elements)
    {
        Set = 0;
        Elements = elements;
    }

    /// <summary>
    /// Constructs a new ResourceLayoutDescription with an explicit set index.
    /// </summary>
    /// <param name="set">The descriptor set index (Vulkan set / DX12 register space).</param>
    /// <param name="elements">An array of <see cref="ResourceLayoutElementDescription"/> objects, describing the properties
    /// of each resource element in the <see cref="PropertySet"/>.</param>
    public ResourceLayoutDescription(uint set, params ResourceLayoutElementDescription[] elements)
    {
        Set = set;
        Elements = elements;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">The instance to compare to.</param>
    /// <returns>True if all array elements are equal; false otherswise.</returns>
    public readonly bool Equals(ResourceLayoutDescription other)
        => Set == other.Set && Util.ArrayEqualsEquatable(Elements, other.Elements);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
    public override readonly int GetHashCode()
        => HashCode.Combine(Set, Elements.ArrayHash());

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj)
        => obj is ResourceLayoutDescription description && Equals(description);
}
