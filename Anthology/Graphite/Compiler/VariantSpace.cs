using System.Collections.Generic;

namespace Prowl.Graphite.Compiler;


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
    /// The set of possible values defined for this variant space.
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VariantSpace"/> struct.
    /// </summary>
    public VariantSpace(string name, string declType, IReadOnlyList<string> values)
    {
        Name = name;
        DeclType = declType;
        Values = values;
    }
}
