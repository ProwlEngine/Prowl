using System;

namespace Prowl.Veldrid;

/// <summary>
/// Describes an individual resource element in a <see cref="ResourceLayout"/>.
/// </summary>
public struct ResourceLayoutElementDescription : IEquatable<ResourceLayoutElementDescription>
{
    /// <summary>
    /// The interned name of the element. Implicit conversion from <see cref="string"/> is supported.
    /// </summary>
    public PropertyID Name;

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

    // OpenGL-only: overrides the name passed to glGetUniformLocation / glGetUniformBlockIndex.
    /// <summary>
    /// Optional in-shader name used by the OpenGL backend when resolving this element via
    /// <c>glGetUniformLocation</c> / <c>glGetUniformBlockIndex</c>. Ignored by other backends.
    /// When null or empty, the OpenGL backend falls back to <c>ResourceID.ToString(Name)</c>.
    /// </summary>
    public string GLUniformName;

    /// <summary>
    /// Flat list of fields describing the layout of this uniform block. Must be null or empty
    /// unless <see cref="Kind"/> is <see cref="ResourceKind.UniformBuffer"/>. Order is not
    /// significant; Stage 7 looks fields up by <see cref="UniformBlockField.Name"/>.
    /// </summary>
    public UniformBlockField[] UniformFields;

    public ResourceLayoutElementDescription(string name, ResourceKind kind, ShaderStages stages, int bindingIndex)
    {
        Name = name;
        Kind = kind;
        Stages = stages;
        BindingIndex = bindingIndex;
        Options = ResourceLayoutElementOptions.None;
        GLUniformName = null;
        UniformFields = null;
    }

    public ResourceLayoutElementDescription(string name, ResourceKind kind, ShaderStages stages, int bindingIndex, ResourceLayoutElementOptions options)
    {
        Name = name;
        Kind = kind;
        Stages = stages;
        BindingIndex = bindingIndex;
        Options = options;
        GLUniformName = null;
        UniformFields = null;
    }

    /// <summary>
    /// Constructs a fully-specified element including the OpenGL resolve name and per-field UBO metadata.
    /// </summary>
    public ResourceLayoutElementDescription(
        PropertyID name,
        ResourceKind kind,
        ShaderStages stages,
        int bindingIndex,
        ResourceLayoutElementOptions options,
        string glUniformName,
        UniformBlockField[] uniformFields)
    {
        Name = name;
        Kind = kind;
        Stages = stages;
        BindingIndex = bindingIndex;
        Options = options;
        GLUniformName = glUniformName;
        UniformFields = uniformFields;
    }

    /// <summary>
    /// Convenience overload that interns <paramref name="name"/> implicitly.
    /// </summary>
    public ResourceLayoutElementDescription(
        string name,
        ResourceKind kind,
        ShaderStages stages,
        int bindingIndex,
        ResourceLayoutElementOptions options,
        string glUniformName,
        UniformBlockField[] uniformFields)
        : this((PropertyID)name, kind, stages, bindingIndex, options, glUniformName, uniformFields)
    {
    }

    public bool Equals(ResourceLayoutElementDescription other)
    {
        return Name == other.Name
            && Kind == other.Kind
            && Stages == other.Stages
            && BindingIndex == other.BindingIndex
            && Options == other.Options
            && string.Equals(GLUniformName, other.GLUniformName, StringComparison.Ordinal)
            && Util.ArrayEqualsEquatable(UniformFields, other.UniformFields);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Name,
            (int)Kind,
            (int)Stages,
            BindingIndex,
            (int)Options,
            GLUniformName != null ? StringComparer.Ordinal.GetHashCode(GLUniformName) : 0,
            UniformFields != null ? UniformFields.ArrayHash() : 0);
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
