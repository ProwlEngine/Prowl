using System;

namespace Prowl.Graphite;

/// <summary>
/// Resource layout for a GraphicsProgram.
/// </summary>
public struct ResourceLayoutDescription
{
    /// <summary>
    /// Hard cap on <see cref="Elements"/> per set.
    /// </summary>
    public const int MaxElementsPerSet = 64;

    /// <summary>
    /// Descriptor set index. Ignored on backends without sets.
    /// </summary>
    public uint Set;

    /// <summary>
    /// Per-element layout descriptions.
    /// </summary>
    public ResourceLayoutElementDescription[] Elements;

    /// <summary>
    /// New ResourceLayoutDescription with set index 0.
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
}
