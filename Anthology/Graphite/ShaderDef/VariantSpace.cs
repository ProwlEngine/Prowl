using System.Collections.Generic;

namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Represents a variant space defined within a ShaderDef document.
/// </summary>
public readonly struct VariantSpace
{
    /// <summary>
    /// The name of the variant symbol in source.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The declared type of the variant symbol in source.
    /// </summary>
    public string DeclType { get; }

    /// <summary>
    /// The set of possible values defined for this variant space. These are the bare runtime keyword
    /// values (for example an enum case name such as "Realtime", or "true"/"false" for a bool axis).
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// Whether this axis is backed by an enum type. Enum values must be qualified with the enum type
    /// name when emitted into a specialization module (for example "Lighting.Realtime").
    /// </summary>
    public bool IsEnum { get; }

    /// <summary>
    /// The name of the module that declares <see cref="DeclType"/>, or <c>null</c> for builtin types
    /// such as bool. A specialization module must import this module to reference the type.
    /// </summary>
    public string? TypeModule { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VariantSpace"/> struct.
    /// </summary>
    public VariantSpace(string name, string declType, IReadOnlyList<string> values, bool isEnum = false, string? typeModule = null)
    {
        Name = name;
        DeclType = declType;
        Values = values;
        IsEnum = isEnum;
        TypeModule = typeModule;
    }
}
