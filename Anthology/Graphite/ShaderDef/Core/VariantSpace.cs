using System.Collections.Generic;

namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// A variant space declared in a ShaderDef document.
/// </summary>
public readonly struct VariantSpace
{
    /// <summary>
    /// Variant symbol name in source.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Declared type of the variant symbol.
    /// </summary>
    public string DeclType { get; }

    /// <summary>
    /// Possible values for this axis. Bare runtime keywords, e.g. an enum case like "Realtime" or "true"/"false" for bool.
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// True if enum-backed. Enum values need the enum type prefix when emitted, e.g. "Lighting.Realtime".
    /// </summary>
    public bool IsEnum { get; }

    /// <summary>
    /// Module declaring DeclType, or null for builtins like bool. Must be imported to reference the type.
    /// </summary>
    public string? TypeModule { get; }

    /// <summary>
    /// Makes a VariantSpace.
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
