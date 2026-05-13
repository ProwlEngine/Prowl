using System;

namespace NeoVeldrid;

/// <summary>
/// Describes an individual resource element in a <see cref="ResourceLayout"/>.
/// </summary>
public struct ResourceLayoutElementDescription : IEquatable<ResourceLayoutElementDescription>
{
    /// <summary>
    /// The name of the element.
    /// </summary>
    public string Name;

    /// <summary>
    /// The kind of resource.
    /// </summary>
    public ResourceKind Kind;

    /// <summary>
    /// The <see cref="ShaderStages"/> in which this element is used.
    /// </summary>
    public ShaderStages Stages;

    /// <summary>
    /// The binding index for a resource.
    /// Corresponds to OpenGL/Vulkan binding.
    /// Corresponds to Metal index.
    /// Corresponds to DX11/DX12 register slot within its kind.
    /// On backends that require combined sampler/texture objects (OpenGL),
    /// a layout must include both a <see cref="ResourceKind.Sampler"/> and
    /// a <see cref="ResourceKind.TextureReadOnly"/> element at the same
    /// <see cref="BindingIndex"/> and <see cref="Name"/>.
    /// </summary>
    public int BindingIndex;

    public ResourceLayoutElementDescription(string name, ResourceKind kind, ShaderStages stages, int bindingIndex)
    {
        Name = name;
        Kind = kind;
        Stages = stages;
        BindingIndex = bindingIndex;
    }

    public bool Equals(ResourceLayoutElementDescription other)
    {
        return Name == other.Name && Kind == other.Kind && Stages == other.Stages && BindingIndex == other.BindingIndex;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name.GetHashCode(), (int)Kind, (int)Stages, BindingIndex);
    }
}
