using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes an individual resource element in a <see cref="PropertySet"/>.
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
    /// </summary>
    public int BindingIndex;

    /// <summary>
    /// Miscellaneous resource options for this element. 
    /// At the moment, this can be used to specify if a buffer should read from dynamic offsets.
    /// </summary>
    public ResourceLayoutElementOptions Options;

    /// <summary>
    /// In-shader name used by the OpenGL backend when resolving this element via
    /// <c>glGetUniformLocation</c> / <c>glGetUniformBlockIndex</c>. 
    /// Required on OpenGL backends. Ignored by other backends. 
    /// If no name is specified, name will read from the name this description was created with.
    /// </summary>
    public string GLUniformName;

    /// <summary>
    /// Flat list of fields describing the layout of this uniform block. Must be null or empty
    /// unless <see cref="Kind"/> is <see cref="ResourceKind.UniformBuffer"/>. Order is not
    /// significant. Binder looks fields up by <see cref="UniformBlockField.Name"/> and binds by specified offset/size.
    /// </summary>
    public UniformBlockField[] UniformFields;


    /// <summary>
    /// Constructs an element from name, kind, stages used in, and platform binding index.
    /// </summary>
    public ResourceLayoutElementDescription(string name, ResourceKind kind, ShaderStages stages, int bindingIndex)
    {
        Name = name;
        Kind = kind;
        Stages = stages;
        BindingIndex = bindingIndex;
        Options = ResourceLayoutElementOptions.None;
        GLUniformName = name;
        UniformFields = [];
    }


    /// <summary>
    /// Constructs an element from name, kind, stages used in, platform binding index, and additional dynamic binding options.
    /// </summary>
    public ResourceLayoutElementDescription(string name, ResourceKind kind, ShaderStages stages, int bindingIndex, ResourceLayoutElementOptions options)
    {
        Name = name;
        Kind = kind;
        Stages = stages;
        BindingIndex = bindingIndex;
        Options = options;
        GLUniformName = name;
        UniformFields = [];
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


    /// <inheritdoc/>
    public readonly bool Equals(ResourceLayoutElementDescription other)
    {
        return Name == other.Name
            && Kind == other.Kind
            && Stages == other.Stages
            && BindingIndex == other.BindingIndex
            && Options == other.Options
            && string.Equals(GLUniformName, other.GLUniformName, StringComparison.Ordinal)
            && Util.ArrayEqualsEquatable(UniformFields, other.UniformFields);
    }


    /// <inheritdoc/>
    public override readonly int GetHashCode()
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
/// Miscellaneous options for an element in a <see cref="PropertySet"/>.
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
    /// bound with a dynamic offset via <see cref="PropertySet"/>.
    /// Offsets specified this way must be a multiple of
    /// <see cref="GraphicsDevice.UniformBufferMinOffsetAlignment"/> or
    /// <see cref="GraphicsDevice.StructuredBufferMinOffsetAlignment"/>, depending on the kind of resource.
    /// </summary>
    DynamicBinding = 1 << 0,

    /// <summary>
    /// Applies to a <see cref="ResourceKind.TextureReadOnly"/> element that originates from a combined
    /// texture-sampler type in the shader (e.g. Slang's <c>Sampler2D&lt;&gt;</c>). On Vulkan the element
    /// binds as a single combined image-sampler descriptor, sourcing its sampler from the paired
    /// <see cref="PropertySet.SetTexture(PropertyID,Texture,Sampler)"/> call. Ignored by backends that
    /// already merge texture and sampler (OpenGL).
    /// </summary>
    CombinedImageSampler = 1 << 1,
}
