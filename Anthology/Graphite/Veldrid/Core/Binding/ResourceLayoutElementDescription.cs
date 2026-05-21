using System;

namespace Prowl.Veldrid;

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

    /// <summary>
    /// Miscellaneous resource options for this element.
    /// </summary>
    public ResourceLayoutElementOptions Options;

    public ResourceLayoutElementDescription(string name, ResourceKind kind, ShaderStages stages, int bindingIndex)
    {
        Name = name;
        Kind = kind;
        Stages = stages;
        BindingIndex = bindingIndex;
        Options = ResourceLayoutElementOptions.None;
    }

    public ResourceLayoutElementDescription(string name, ResourceKind kind, ShaderStages stages, int bindingIndex, ResourceLayoutElementOptions options)
    {
        Name = name;
        Kind = kind;
        Stages = stages;
        BindingIndex = bindingIndex;
        Options = options;
    }

    public bool Equals(ResourceLayoutElementDescription other)
    {
        return Name == other.Name && Kind == other.Kind && Stages == other.Stages && BindingIndex == other.BindingIndex && Options == other.Options;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name.GetHashCode(), (int)Kind, (int)Stages, BindingIndex, (int)Options);
    }
}


/// <summary>
/// Miscellaneous options for an element in a <see cref="ResourceLayout"/>.
/// </summary>
[Flags]
public enum ResourceLayoutElementOptions
{
    /// <summary>
    /// No special options.
    /// </summary>
    None = 0,

    /// <summary>
    /// Can be applied to a buffer type resource (<see cref="ResourceKind.StructuredBufferReadOnly"/>,
    /// <see cref="ResourceKind.StructuredBufferReadWrite"/>, or <see cref="ResourceKind.UniformBuffer"/>), allowing it to be
    /// bound with a dynamic offset using
    /// <see cref="CommandBuffer.SetGraphicsResourceSet(uint, ResourceSet, uint[])"/>.
    /// Offsets specified this way must be a multiple of
    /// <see cref="GraphicsDevice.UniformBufferMinOffsetAlignment"/> or
    /// <see cref="GraphicsDevice.StructuredBufferMinOffsetAlignment"/>, depending on the kind of resource.
    /// </summary>
    DynamicBinding = 1 << 0,
}
