using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// A global axis, one any shader may declare with "#pragma variant global", whose value the renderer, a world or a view
/// picks with <see cref="GlobalProperties.Set(GlobalAxis, string?)"/> or <see cref="RenderView.Keywords"/>. Defined once
/// per name: a shader declaring it must take only values of the axis and the same default, else it fails to load.
/// </summary>
public sealed class GlobalAxis
{
    private static readonly Dictionary<string, GlobalAxis> s_defined = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<(string Shader, IReadOnlyList<string> Values)>> s_declared = new(StringComparer.Ordinal);

    /// <summary>The debug view a view draws, see Graphite/Debug.glsl. Shaders may declare fewer of its values.</summary>
    public static readonly GlobalAxis Debug = Define("GRAPHITE_DEBUG", "UNLIT", "ALBEDO", "NORMALS", "ROUGHNESS", "METALLIC", "AO", "EMISSION", "LIGHTING",
        "CASCADES", "LIGHTMAP_DENSITY", "DIFFUSE", "SPECULAR", "SPECULAR_ALBEDO", "THROUGHPUT", "DIRECT", "INDIRECT", "FURNACE", "COAT", "FUZZ",
        "THIN_FILM", "DARKENING", "SUBSURFACE", "BACK", "TRANSMISSION", "THICKNESS");

    /// <summary>The standard surfaces' quality, FULL or LITE for mobile.</summary>
    public static readonly GlobalAxis PbrTier = Define("PBR_TIER", "FULL", "LITE");

    /// <summary>On while a light of the world has a mask, set by the renderer.</summary>
    public static readonly GlobalAxis LightMasks = Toggle("GRAPHITE_LIGHT_MASKS");

    /// <summary>On while a light of the world casts contact shadows, set by the renderer.</summary>
    public static readonly GlobalAxis ContactShadows = Toggle("GRAPHITE_CONTACT_SHADOWS");

    /// <summary>On while the world has decals, set by the renderer.</summary>
    public static readonly GlobalAxis Decals = Toggle("GRAPHITE_DECALS");

    private GlobalAxis(string name, IReadOnlyList<string> values, bool isToggle)
    {
        Name = name;
        Values = values;
        IsToggle = isToggle;
    }

    public string Name { get; }

    /// <summary>Every value the axis takes, the first being its default.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>True for an on or off axis, set with <see cref="GlobalProperties.Set(GlobalAxis, bool)"/>.</summary>
    public bool IsToggle { get; }

    /// <summary>The keyword picking a value, for <see cref="RenderView.Keywords"/>. Throws on a value the axis does not have.</summary>
    public GlobalKeyword this[string value] => new(this, Check(value));

    /// <summary>The keyword turning a toggle on or off. Throws on an axis that is not a toggle.</summary>
    public GlobalKeyword this[bool on] => new(this, ToggleValue(on));

    /// <summary>
    /// The axis of a name with its values, the first being the default. Defining a name again with the same values gives
    /// the same axis. Throws when it was defined with other values, or a loaded shader declares values it does not have.
    /// </summary>
    public static GlobalAxis Define(string name, params string[] values)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (values.Length < 2)
            throw new ArgumentException($"The global axis {name} needs two or more values, or use Toggle.", nameof(values));
        return Add(new GlobalAxis(name, [.. values], false));
    }

    /// <summary>An on or off axis, as "#pragma variant global NAME" declares it.</summary>
    public static GlobalAxis Toggle(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Add(new GlobalAxis(name, VariantSpace.ToggleValues, true));
    }

    /// <summary>The axis defined with a name, or null.</summary>
    public static GlobalAxis? Find(string name)
    {
        lock (s_defined)
            return s_defined.GetValueOrDefault(name);
    }

    internal string Check(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        foreach (string known in Values)
        {
            if (known == value)
                return known;
        }
        throw new ArgumentException($"The global axis {Name} has no value {value}, only {string.Join(" ", Values)}.", nameof(value));
    }

    internal string ToggleValue(bool on)
    {
        if (!IsToggle)
            throw new ArgumentException($"The global axis {Name} is not a toggle, its values are {string.Join(" ", Values)}.");
        return VariantSpace.ToggleValues[on ? 1 : 0];
    }

    /// <summary>Records a shader's global axis. Throws when an axis of its name is defined and the shader's values do not fit it.</summary>
    internal static void Declare(string shader, VariantSpace space)
    {
        lock (s_defined)
        {
            if (s_defined.TryGetValue(space.Name, out GlobalAxis? axis) && axis.Mismatch(space.Values) is { } problem)
                throw new InvalidOperationException($"The shader {shader} declares the global axis {space.Name} with {problem}.");
            if (!s_declared.TryGetValue(space.Name, out var declared))
                s_declared[space.Name] = declared = new();
            if (!declared.Exists(d => d.Shader == shader && Same(d.Values, space.Values)))
                declared.Add((shader, space.Values));
        }
    }

    private static GlobalAxis Add(GlobalAxis axis)
    {
        lock (s_defined)
        {
            if (s_defined.TryGetValue(axis.Name, out GlobalAxis? existing))
            {
                if (existing.IsToggle == axis.IsToggle && Same(existing.Values, axis.Values))
                    return existing;
                throw new ArgumentException($"The global axis {axis.Name} is already defined with values {string.Join(" ", existing.Values)}.");
            }
            foreach ((string shader, IReadOnlyList<string> values) in s_declared.GetValueOrDefault(axis.Name) ?? [])
            {
                if (axis.Mismatch(values) is { } problem)
                    throw new ArgumentException($"The shader {shader} declares the global axis {axis.Name} with {problem}.");
            }
            s_defined[axis.Name] = axis;
            return axis;
        }
    }

    // Why a shader's values do not fit the axis, or null when they do
    private string? Mismatch(IReadOnlyList<string> values)
    {
        if (values[0] != Values[0])
            return $"the default {values[0]}, the axis defaults to {Values[0]}";
        foreach (string value in values)
        {
            if (!Contains(Values, value))
                return $"the value {value}, the axis has only {string.Join(" ", Values)}";
        }
        return null;
    }

    private static bool Contains(IReadOnlyList<string> values, string value)
    {
        foreach (string known in values)
        {
            if (known == value)
                return true;
        }
        return false;
    }

    private static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    public override string ToString() => Name;
}

/// <summary>A value of a global axis, from the axis's indexer, such as GlobalAxis.Debug["ALBEDO"].</summary>
public readonly struct GlobalKeyword : IEquatable<GlobalKeyword>
{
    internal GlobalKeyword(GlobalAxis axis, string value)
    {
        Axis = axis;
        Value = value;
    }

    public GlobalAxis Axis { get; }

    public string Value { get; }

    public bool Equals(GlobalKeyword other) => Axis == other.Axis && Value == other.Value;

    public override bool Equals(object? obj) => obj is GlobalKeyword other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Axis, Value);

    public static bool operator ==(GlobalKeyword left, GlobalKeyword right) => left.Equals(right);

    public static bool operator !=(GlobalKeyword left, GlobalKeyword right) => !left.Equals(right);

    public override string ToString() => $"{Axis?.Name}={Value}";
}
