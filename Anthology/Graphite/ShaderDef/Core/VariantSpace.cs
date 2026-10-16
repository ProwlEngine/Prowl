using System.Collections.Generic;

namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Who picks the value of a variant axis.
/// </summary>
public enum VariantScope : byte
{
    /// <summary>
    /// Set per material.
    /// </summary>
    Material,

    /// <summary>
    /// Set by the renderer, a world or a view, never by a material.
    /// </summary>
    Global,
}


/// <summary>
/// A variant axis declared by a shader.
/// </summary>
public readonly struct VariantSpace
{
    /// <summary>
    /// Toggle values, off first so key 0 has every toggle off.
    /// </summary>
    public static readonly IReadOnlyList<string> ToggleValues = ["false", "true"];

    /// <summary>
    /// Axis name in source.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Possible values, the first being the default.
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// True for an on or off axis with <see cref="ToggleValues"/>.
    /// </summary>
    public bool IsToggle { get; }

    /// <summary>
    /// Who picks the value.
    /// </summary>
    public VariantScope Scope { get; }

    /// <summary>
    /// Makes an axis with named values.
    /// </summary>
    public VariantSpace(string name, IReadOnlyList<string> values, VariantScope scope = VariantScope.Material)
    {
        Name = name;
        Values = values;
        Scope = scope;
    }

    private VariantSpace(string name, VariantScope scope, bool isToggle)
    {
        Name = name;
        Values = ToggleValues;
        Scope = scope;
        IsToggle = isToggle;
    }

    /// <summary>
    /// Makes an on or off axis.
    /// </summary>
    public static VariantSpace Toggle(string name, VariantScope scope = VariantScope.Material) => new(name, scope, true);
}
