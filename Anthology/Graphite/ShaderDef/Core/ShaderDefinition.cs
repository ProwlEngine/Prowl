using System;
using System.Collections.Generic;

using Prowl.Vector;


namespace Prowl.Graphite.ShaderDef;


/// <summary>A Surface block: the shading model it names, its code, the document line it starts on, and its Lighting and Vertex code.</summary>
public sealed record ShaderSurface(string Model, string Code, int Line, string Lighting = "", string Vertex = "");


/// <summary>
/// Parsed shaderdef shader, plus runtime binding to a device/compiler once Create is called.
/// </summary>
public sealed class ShaderDefinition
{
    /// <summary>
    /// Shader name. Required, never null.
    /// </summary>
    public string? Name;

    /// <summary>
    /// Fallback shader name for renderers. Empty if none.
    /// </summary>
    public string? Fallback;

    /// <summary>
    /// Default properties requested by markup for resources/uniforms.
    /// </summary>
    public ShaderProperty[]? Properties;

    /// <summary>
    /// Render passes in the shader.
    /// </summary>
    public ShaderPass[]? Passes;

    /// <summary>
    /// The shader's Surface block, a shading model and the code run over its surface, which the renderer that loads the
    /// shader expands into passes and properties. Null without one, or once expanded.
    /// </summary>
    public ShaderSurface? Surface;


    private bool _created;

    /// <summary>
    /// True once created for a device.
    /// </summary>
    public bool IsCreated => _created;


    /// <summary>A definition with the same fields and copies of the passes, not created for a device.</summary>
    internal ShaderDefinition Copy()
    {
        ShaderPass[]? passes = null;
        if (Passes != null)
        {
            passes = new ShaderPass[Passes.Length];
            for (int i = 0; i < passes.Length; i++)
                passes[i] = Passes[i].Copy();
        }
        return new ShaderDefinition
        {
            Name = Name,
            Fallback = Fallback,
            Properties = Properties is null ? null : [.. Properties],
            Passes = passes,
            Surface = Surface,
        };
    }


    /// <summary>
    /// Binds to a device, each pass's variant axes from the compiler. Variants compile on the calling thread when first
    /// asked for, and one that failed throws. Without a compiler a pass resolves only variants that come from elsewhere.
    /// </summary>
    public void Create(GraphicsDevice device, IShaderCompiler? compiler = null)
    {
        if (Passes == null)
            throw new InvalidOperationException("Shader has no passes.");

        for (int i = 0; i < Passes.Length; i++)
        {
            ShaderPass pass = Passes[i];
            VariantSpace[] axes = [.. pass.AxesWith(compiler)];
            pass.Bind(device, Name ?? "", i, axes, [], compiler);
        }

        _created = true;
    }


    /// <summary>
    /// Binds to a device from a captured snapshot. Variants missing from it, or not compiled for the device backend,
    /// compile through the compiler when there is one and throw when there is none.
    /// </summary>
    public void Create(GraphicsDevice device, ShaderSnapshot snapshot, IShaderCompiler? compiler = null)
    {
        if (Passes == null)
            throw new InvalidOperationException("Shader has no passes.");

        if (snapshot.Passes == null || snapshot.Passes.Length != Passes.Length)
            throw new ArgumentException("Snapshot pass count does not match the shader's pass count.", nameof(snapshot));

        for (int i = 0; i < Passes.Length; i++)
        {
            PassSnapshot pass = snapshot.Passes[i];
            Passes[i].Bind(device, Name ?? "", i, pass.Axes ?? [], pass.Variants ?? [], compiler);
        }

        _created = true;
    }


    /// <summary>
    /// Captures every pass's axes and compiled variants into a serializable snapshot. Only
    /// populated variants are captured.
    /// </summary>
    public ShaderSnapshot Snapshot()
    {
        if (Passes == null)
            return new ShaderSnapshot { Passes = [] };

        PassSnapshot[] passes = new PassSnapshot[Passes.Length];
        for (int i = 0; i < Passes.Length; i++)
            passes[i] = Passes[i].Snapshot();

        return new ShaderSnapshot { Passes = passes };
    }


    /// <summary>
    /// Builds a set from properties that have a default and a target in the pass variant.
    /// </summary>
    public PropertySet CreateDefaultProperties(ShaderPass pass, VariantKey variantKey = default, DefaultTextureResolver? resolveTexture = null)
    {
        ArgumentNullException.ThrowIfNull(pass);

        PropertySet set = new();
        if (Properties == null)
            return set;

        HashSet<PropertyID> targets = new();
        foreach (ResourceLayoutDescription layout in pass.GetDescription(variantKey.Index).ResourceLayouts)
        {
            foreach (ResourceLayoutElementDescription element in layout.Elements)
            {
                targets.Add(element.Name);
                if (element.UniformFields is { } fields)
                {
                    foreach (UniformBlockField field in fields)
                        targets.Add(field.Name);
                }
            }
        }

        for (int i = 0; i < Properties.Length; i++)
        {
            ShaderProperty property = Properties[i];
            PropertyID id = property.Name;

            if (!property.HasDefault || !targets.Contains(id))
                continue;

            switch (property.PropertyType)
            {
                case ShaderPropertyType.Float:
                    set.SetFloat(id, property.Value.X);
                    break;
                case ShaderPropertyType.Integer:
                    set.SetInt(id, (int)property.Value.X);
                    break;
                case ShaderPropertyType.Color:
                case ShaderPropertyType.Vector:
                    set.SetFloat4(id, property.Value);
                    break;
                case ShaderPropertyType.Vector2:
                    set.SetFloat2(id, new Float2(property.Value.X, property.Value.Y));
                    break;
                case ShaderPropertyType.Vector3:
                    set.SetFloat3(id, new Float3(property.Value.X, property.Value.Y, property.Value.Z));
                    break;
                case ShaderPropertyType.Matrix:
                    set.SetMatrix(id, property.MatrixValue);
                    break;
                default:
                    if (resolveTexture?.Invoke(property.TextureValue, property.PropertyType) is { } view)
                        set.SetTexture(id, view);
                    break;
            }
        }

        return set;
    }


    /// <summary>True if pass carries tag, optionally matching a specific value.</summary>
    public static bool PassHasTag(ShaderPass pass, string tag, string? tagValue = null)
    {
        return pass.Tags is { } tags && tags.TryGetValue(tag, out string value)
            && (tagValue == null || value == tagValue);
    }

    /// <summary>
    /// Pass index by name, or -1 if not found.
    /// </summary>
    public int GetPassIndex(string passName)
    {
        ShaderPass[] passes = Passes ?? [];
        for (int i = 0; i < passes.Length; i++)
        {
            if (passes[i].Name == passName)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Pass by name.
    /// </summary>
    public ShaderPass GetPass(string passName)
        => Passes![GetPassIndex(passName)];

    /// <summary>
    /// Index of first pass carrying tag, optionally matching a specific value. Null if none.
    /// </summary>
    public int? GetPassWithTag(string tag, string? tagValue = null)
    {
        ShaderPass[] passes = Passes ?? [];
        for (int i = 0; i < passes.Length; i++)
        {
            if (PassHasTag(passes[i], tag, tagValue))
                return i;
        }

        return null;
    }

    /// <summary>
    /// Indices of every pass carrying tag, optionally matching a specific value.
    /// </summary>
    public List<int> GetPassesWithTag(string tag, string? tagValue = null)
    {
        ShaderPass[] passes = Passes ?? [];
        List<int> result = [];

        for (int i = 0; i < passes.Length; i++)
        {
            if (PassHasTag(passes[i], tag, tagValue))
                result.Add(i);
        }

        return result;
    }
}
