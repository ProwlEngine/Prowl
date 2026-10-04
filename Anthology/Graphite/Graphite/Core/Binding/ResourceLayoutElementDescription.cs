using System;

namespace Prowl.Graphite;

/// <summary>
/// Resource element in a PropertySet.
/// </summary>
public record struct ResourceLayoutElementDescription
{
    /// <summary>
    /// Element name (interned).
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
    /// Binding slot (Vulkan/Metal/DX11/DX12).
    /// </summary>
    public int BindingIndex;

    /// <summary>
    /// Dynamic offset control.
    /// </summary>
    public ResourceLayoutElementOptions Options;

    /// <summary>
    /// Uniform block fields (order-independent).
    /// </summary>
    public UniformBlockField[] UniformFields;

    /// <summary>
    /// Name, kind, stages, binding index, plus optional GL uniform name and UBO metadata.
    /// </summary>
    public ResourceLayoutElementDescription(
        PropertyID name,
        ResourceKind kind,
        ShaderStages stages,
        int bindingIndex,
        ResourceLayoutElementOptions options = ResourceLayoutElementOptions.None,
        UniformBlockField[]? uniformFields = null)
    {
        Name = name;
        Kind = kind;
        Stages = stages;
        BindingIndex = bindingIndex;
        Options = options;
        UniformFields = uniformFields ?? [];
    }

    /// <inheritdoc/>
    public readonly bool Equals(ResourceLayoutElementDescription other)
        => Name == other.Name
        && Kind == other.Kind
        && Stages == other.Stages
        && BindingIndex == other.BindingIndex
        && Options == other.Options
        && Util.ArrayEqualsEquatable(UniformFields, other.UniformFields);

    /// <inheritdoc/>
    public override readonly int GetHashCode()
        => HashCode.Combine(Name, Kind, Stages, BindingIndex, Options, UniformFields?.ArrayHash() ?? 0);
}


/// <summary>
/// PropertySet element options.
/// </summary>
[Flags]
public enum ResourceLayoutElementOptions
{
    /// <summary>
    /// Nothing special.
    /// </summary>
    None = 0,

    /// <summary>
    /// Combined texture-sampler element.
    /// </summary>
    CombinedImageSampler = 1 << 1,
}
