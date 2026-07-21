using System;

namespace Prowl.Graphite;

/// <summary>
/// One resource element in a PropertySet.
/// </summary>
public struct ResourceLayoutElementDescription : IEquatable<ResourceLayoutElementDescription>
{
    /// <summary>
    /// Interned element name. Implicitly converts from string.
    /// </summary>
    public PropertyID Name;

    /// <summary>
    /// Resource kind.
    /// </summary>
    public ResourceKind Kind;

    /// <summary>
    /// Shader stages this element is used in.
    /// </summary>
    public ShaderStages Stages;

    /// <summary>
    /// Binding index. Vulkan binding, Metal index, or DX11/DX12 register slot within its kind.
    /// </summary>
    public int BindingIndex;

    /// <summary>
    /// Misc options. Currently just controls dynamic offset support.
    /// </summary>
    public ResourceLayoutElementOptions Options;

    /// <summary>
    /// In-shader uniform name, for the old OpenGL backend. Unused on Vulkan. Defaults to Name.
    /// </summary>
    public string GLUniformName;

    /// <summary>
    /// Uniform block fields, matched by name, bound by offset/size. Order doesn't matter. Empty unless Kind is UniformBuffer.
    /// </summary>
    public UniformBlockField[] UniformFields;


    /// <summary>
    /// Name, kind, stages, binding index.
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
    /// Name, kind, stages, binding index, plus options.
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
    /// Full ctor: also sets GL uniform name and per-field UBO metadata.
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
/// Misc options for a PropertySet element.
/// </summary>
[Flags]
public enum ResourceLayoutElementOptions
{
    /// <summary>
    /// Nothing special.
    /// </summary>
    None = 0,

    /// <summary>
    /// Lets a buffer resource (structured RO/RW or uniform) bind with a dynamic offset. Offset must be a multiple of the device's min offset alignment for that kind.
    /// </summary>
    DynamicBinding = 1 << 0,

    /// <summary>
    /// Marks a read-only texture element from a combined texture-sampler type (e.g. Slang Sampler2D). Binds as one combined image-sampler on Vulkan, sampler comes from the paired SetTexture call.
    /// </summary>
    CombinedImageSampler = 1 << 1,
}
