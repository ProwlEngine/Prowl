using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>A kind of pass a shader can provide, found by a ShaderDef pass's LightMode tag.</summary>
public readonly record struct PassType(string Name)
{
    public const string Tag = "LightMode";

    public static readonly PassType Prepass = new("Prepass");
    public static readonly PassType Forward = new("Forward");
    public static readonly PassType ShadowCaster = new("ShadowCaster");
    public static readonly PassType ObjectId = new("ObjectId");

    public override string ToString() => Name;
}

public enum MaterialPropertyType : byte
{
    None,
    Float,
    Float2,
    Float3,
    Float4,
    Color,
    Int,
    Matrix,
    Texture2D,
    Texture2DArray,
    Texture3D,
    TextureCube,
    TextureCubeArray,
}

/// <summary>
/// A material property value. Numbers live in <see cref="Vector"/>, ints also in <see cref="Int"/>, and a texture keeps
/// its scale offset in <see cref="Vector"/>.
/// </summary>
public readonly struct MaterialValue
{
    private sealed class MatrixBox(Float4x4 value)
    {
        public readonly Float4x4 Value = value;
    }

    private sealed class TextureBox(Texture? texture, SamplerDescription? sampler)
    {
        public readonly Texture? Texture = texture;
        public readonly SamplerDescription? Sampler = sampler;
    }

    private readonly object? _data;

    private MaterialValue(MaterialPropertyType type, Float4 vector, int integer = 0, object? data = null)
    {
        Type = type;
        Vector = vector;
        Int = integer;
        _data = data;
    }

    /// <summary>The type the value was set as, None when nothing was set.</summary>
    public MaterialPropertyType Type { get; }

    public Float4 Vector { get; }

    public int Int { get; }

    public bool IsEmpty => Type == MaterialPropertyType.None;

    public float Float => Vector.X;

    public Float4x4 Matrix => _data is MatrixBox box ? box.Value : Float4x4.Identity;

    public Texture? Texture => (_data as TextureBox)?.Texture;

    public SamplerDescription? Sampler => (_data as TextureBox)?.Sampler;

    public Float4 ScaleOffset => Vector;

    public bool IsTexture => Type >= MaterialPropertyType.Texture2D;

    public static readonly Float4 IdentityScaleOffset = new(1, 1, 0, 0);

    public static MaterialValue FromFloat(float value) => new(MaterialPropertyType.Float, new Float4(value, 0, 0, 0));

    public static MaterialValue FromFloat2(Float2 value) => new(MaterialPropertyType.Float2, new Float4(value.X, value.Y, 0, 0));

    public static MaterialValue FromFloat3(Float3 value) => new(MaterialPropertyType.Float3, new Float4(value.X, value.Y, value.Z, 0));

    public static MaterialValue FromFloat4(Float4 value) => new(MaterialPropertyType.Float4, value);

    public static MaterialValue FromColor(Float4 value) => new(MaterialPropertyType.Color, value);

    public static MaterialValue FromInt(int value) => new(MaterialPropertyType.Int, new Float4(value, 0, 0, 0), value);

    public static MaterialValue FromMatrix(Float4x4 value) => new(MaterialPropertyType.Matrix, Float4.Zero, 0, new MatrixBox(value));

    /// <summary>A texture value. A null texture means the schema default.</summary>
    public static MaterialValue FromTexture(MaterialPropertyType type, Texture? texture, SamplerDescription? sampler, Float4 scaleOffset)
    {
        if (type < MaterialPropertyType.Texture2D)
            throw new ArgumentException($"{type} is not a texture type.", nameof(type));
        return new(type, scaleOffset, 0, new TextureBox(texture, sampler));
    }

    internal MaterialValue WithScaleOffset(Float4 scaleOffset) => new(Type, scaleOffset, Int, _data);
}

/// <summary>One property of a shader's schema, with where it sits in the material record.</summary>
public readonly struct MaterialProperty
{
    public PropertyID Name { get; init; }
    public string DisplayName { get; init; }
    public MaterialPropertyType Type { get; init; }
    public MaterialValue Default { get; init; }

    /// <summary>Default texture name for texture properties, like white or normal.</summary>
    public string DefaultTexture { get; init; }

    public IReadOnlyList<string> Attributes { get; init; }

    /// <summary>Byte offset in the material record. A texture holds its texture index here, its sampler index 4 bytes on and its scale offset at <see cref="ScaleOffsetOffset"/>.</summary>
    public uint Offset { get; init; }

    public uint ScaleOffsetOffset { get; init; }

    /// <summary>True when the colour is entered in gamma space and converted to linear in the record.</summary>
    public bool Gamma { get; init; }

    public bool HasAttribute(string name)
    {
        foreach (string attribute in Attributes)
        {
            if (attribute == name || (attribute.StartsWith(name, StringComparison.Ordinal) && attribute.Length > name.Length && attribute[name.Length] == '('))
                return true;
        }
        return false;
    }
}

/// <summary>Resolves the bindless slots a material record stores for its textures and samplers.</summary>
internal interface IMaterialResources
{
    /// <summary>Slot of a texture in the table of its type, or of the named default when the texture is null.</summary>
    uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName);

    /// <summary>Slot of a sampler in the sampler table, null meaning the default sampler.</summary>
    uint SamplerSlot(SamplerDescription? sampler);
}

/// <summary>
/// The material record layout of a shader: its properties in declaration order, std430, shared by every pass and
/// variant. Records are padded to 16 bytes.
/// </summary>
public sealed class MaterialSchema
{
    private readonly Dictionary<PropertyID, int> _indices = new();
    private readonly MaterialProperty[] _properties;

    public MaterialSchema(IReadOnlyList<ShaderProperty> properties)
    {
        MaterialProperty[] result = new MaterialProperty[properties.Count];
        uint offset = 0;
        for (int i = 0; i < properties.Count; i++)
        {
            ShaderProperty source = properties[i];
            MaterialPropertyType type = TypeOf(source.PropertyType);
            string[] attributes = source.Attributes ?? [];
            (uint size, uint alignment) = type switch
            {
                MaterialPropertyType.Float or MaterialPropertyType.Int => (4u, 4u),
                MaterialPropertyType.Float2 => (8u, 8u),
                MaterialPropertyType.Float3 => (12u, 16u),
                MaterialPropertyType.Float4 or MaterialPropertyType.Color => (16u, 16u),
                MaterialPropertyType.Matrix => (64u, 16u),
                _ => (8u, 4u),
            };

            offset = AlignUp(offset, alignment);
            uint propertyOffset = offset;
            offset += size;
            uint scaleOffsetOffset = 0;
            if (type >= MaterialPropertyType.Texture2D)
            {
                scaleOffsetOffset = AlignUp(offset, 16);
                offset = scaleOffsetOffset + 16;
            }

            if (!_indices.TryAdd(source.Name, i))
                throw new ArgumentException($"The property {source.Name} is declared twice.");

            result[i] = new MaterialProperty
            {
                Name = source.Name,
                DisplayName = source.DisplayName ?? source.Name,
                Type = type,
                Default = DefaultOf(type, source),
                DefaultTexture = source.TextureValue ?? "",
                Attributes = attributes,
                Offset = propertyOffset,
                ScaleOffsetOffset = scaleOffsetOffset,
                Gamma = type == MaterialPropertyType.Color && Array.IndexOf(attributes, "Gamma") >= 0,
            };
        }

        _properties = result;
        StrideWords = Math.Max(AlignUp(offset, 16), 16u) / 4;
    }

    public IReadOnlyList<MaterialProperty> Properties => _properties;

    /// <summary>Words per record, a multiple of 4.</summary>
    public uint StrideWords { get; }

    public int Find(PropertyID name) => _indices.TryGetValue(name, out int index) ? index : -1;

    /// <summary>Writes a record from the resolved value of each property.</summary>
    internal void Pack(ReadOnlySpan<MaterialValue> values, Span<uint> record, IMaterialResources resources)
    {
        record.Clear();
        for (int i = 0; i < _properties.Length; i++)
        {
            ref readonly MaterialProperty property = ref _properties[i];
            MaterialValue value = values[i];
            int word = (int)(property.Offset / 4);
            switch (property.Type)
            {
                case MaterialPropertyType.Float:
                    record[word] = BitConverter.SingleToUInt32Bits(value.Vector.X);
                    break;
                case MaterialPropertyType.Int:
                    record[word] = (uint)(value.Type == MaterialPropertyType.Int ? value.Int : (int)value.Vector.X);
                    break;
                case MaterialPropertyType.Float2:
                    WriteFloats(record, word, value.Vector, 2);
                    break;
                case MaterialPropertyType.Float3:
                    WriteFloats(record, word, value.Vector, 3);
                    break;
                case MaterialPropertyType.Float4:
                    WriteFloats(record, word, value.Vector, 4);
                    break;
                case MaterialPropertyType.Color:
                    WriteFloats(record, word, property.Gamma ? GammaToLinear(value.Vector) : value.Vector, 4);
                    break;
                case MaterialPropertyType.Matrix:
                    Float4x4 matrix = value.Matrix;
                    WriteFloats(record, word, matrix.c0, 4);
                    WriteFloats(record, word + 4, matrix.c1, 4);
                    WriteFloats(record, word + 8, matrix.c2, 4);
                    WriteFloats(record, word + 12, matrix.c3, 4);
                    break;
                default:
                    record[word] = resources.TextureSlot(value.Texture, property.Type, property.DefaultTexture);
                    record[word + 1] = resources.SamplerSlot(value.Sampler);
                    WriteFloats(record, (int)(property.ScaleOffsetOffset / 4), value.ScaleOffset, 4);
                    break;
            }
        }
    }

    /// <summary>True when a value set as <paramref name="set"/> can fill a property of type <paramref name="property"/>.</summary>
    internal static bool Compatible(MaterialPropertyType property, MaterialPropertyType set)
    {
        bool propertyTexture = property >= MaterialPropertyType.Texture2D;
        bool setTexture = set >= MaterialPropertyType.Texture2D;
        if (propertyTexture || setTexture)
            return property == set;
        return (property == MaterialPropertyType.Matrix) == (set == MaterialPropertyType.Matrix);
    }

    private static void WriteFloats(Span<uint> record, int word, Float4 value, int count)
    {
        for (int i = 0; i < count; i++)
            record[word + i] = BitConverter.SingleToUInt32Bits(value[i]);
    }

    private static Float4 GammaToLinear(Float4 color)
    {
        static float Channel(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        return new Float4(Channel(color.X), Channel(color.Y), Channel(color.Z), color.W);
    }

    private static uint AlignUp(uint value, uint alignment) => (value + alignment - 1) & ~(alignment - 1);

    private static MaterialPropertyType TypeOf(ShaderPropertyType type) => type switch
    {
        ShaderPropertyType.Float => MaterialPropertyType.Float,
        ShaderPropertyType.Integer => MaterialPropertyType.Int,
        ShaderPropertyType.Color => MaterialPropertyType.Color,
        ShaderPropertyType.Vector => MaterialPropertyType.Float4,
        ShaderPropertyType.Vector2 => MaterialPropertyType.Float2,
        ShaderPropertyType.Vector3 => MaterialPropertyType.Float3,
        ShaderPropertyType.Matrix => MaterialPropertyType.Matrix,
        ShaderPropertyType.Texture2D => MaterialPropertyType.Texture2D,
        ShaderPropertyType.Texture2DArray => MaterialPropertyType.Texture2DArray,
        ShaderPropertyType.Texture3D => MaterialPropertyType.Texture3D,
        ShaderPropertyType.TextureCubemap => MaterialPropertyType.TextureCube,
        ShaderPropertyType.TextureCubemapArray => MaterialPropertyType.TextureCubeArray,
        _ => throw new NotSupportedException($"Unsupported property type {type}."),
    };

    private static MaterialValue DefaultOf(MaterialPropertyType type, ShaderProperty source) => type switch
    {
        MaterialPropertyType.Float => MaterialValue.FromFloat(source.Value.X),
        MaterialPropertyType.Int => MaterialValue.FromInt((int)source.Value.X),
        MaterialPropertyType.Float2 => MaterialValue.FromFloat2(new Float2(source.Value.X, source.Value.Y)),
        MaterialPropertyType.Float3 => MaterialValue.FromFloat3(new Float3(source.Value.X, source.Value.Y, source.Value.Z)),
        MaterialPropertyType.Float4 => MaterialValue.FromFloat4(source.Value),
        MaterialPropertyType.Color => MaterialValue.FromColor(source.HasDefault ? source.Value : new Float4(1, 1, 1, 1)),
        MaterialPropertyType.Matrix => MaterialValue.FromMatrix(source.HasDefault ? source.MatrixValue : Float4x4.Identity),
        _ => MaterialValue.FromTexture(type, null, null, MaterialValue.IdentityScaleOffset),
    };
}

/// <summary>
/// A shader the renderer can draw with: its definition, the material schema from its Properties block, and its
/// variant axes split by scope.
/// </summary>
public sealed class Shader
{
    /// <summary>Render queue of a shader whose passes give none. Queues from 2500 up are transparent.</summary>
    public const int DefaultRenderQueue = 2000;

    public const int TransparentQueue = 2500;

    internal Shader(ShaderDefinition definition, IShaderCompiler compiler)
    {
        Definition = definition;
        Name = definition.Name ?? "";
        Schema = new MaterialSchema(definition.Properties ?? []);

        Dictionary<string, VariantSpace> axes = new(StringComparer.Ordinal);
        List<VariantSpace> material = new();
        List<VariantSpace> global = new();
        int queue = DefaultRenderQueue;
        foreach (ShaderPass pass in definition.Passes ?? [])
        {
            foreach (VariantSpace axis in compiler.GetAxes(pass))
            {
                if (axes.TryGetValue(axis.Name, out VariantSpace existing))
                {
                    if (existing.Scope != axis.Scope)
                        throw new InvalidOperationException($"The shader {Name} declares the axis {axis.Name} as both material and global.");
                    continue;
                }

                axes[axis.Name] = axis;
                (axis.Scope == VariantScope.Global ? global : material).Add(axis);
            }

            if (pass.Tags != null && pass.Tags.TryGetValue("RenderQueue", out string? value) && int.TryParse(value, out int parsed))
                queue = parsed;
        }

        MaterialAxes = material;
        GlobalAxes = global;
        RenderQueue = queue;
    }

    public string Name { get; }

    public ShaderDefinition Definition { get; }

    public MaterialSchema Schema { get; }

    public IReadOnlyList<MaterialProperty> Properties => Schema.Properties;

    public IReadOnlyList<VariantSpace> MaterialAxes { get; }

    public IReadOnlyList<VariantSpace> GlobalAxes { get; }

    /// <summary>Default render queue of the shader's materials, from a RenderQueue pass tag.</summary>
    public int RenderQueue { get; }

    public bool HasPass(PassType type)
    {
        foreach (ShaderPass pass in Definition.Passes ?? [])
        {
            if (ShaderDefinition.PassHasTag(pass, PassType.Tag, type.Name))
                return true;
        }
        return false;
    }

    public int FindProperty(PropertyID name) => Schema.Find(name);

    internal int FindAxis(string name, out VariantScope scope)
    {
        for (int i = 0; i < MaterialAxes.Count; i++)
        {
            if (MaterialAxes[i].Name == name)
            {
                scope = VariantScope.Material;
                return i;
            }
        }

        for (int i = 0; i < GlobalAxes.Count; i++)
        {
            if (GlobalAxes[i].Name == name)
            {
                scope = VariantScope.Global;
                return i;
            }
        }

        scope = default;
        return -1;
    }
}
