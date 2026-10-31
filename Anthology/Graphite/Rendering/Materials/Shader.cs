using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

public static class PropertyIDText
{
    /// <summary>The interned name.</summary>
    public static string Text(this PropertyID id) => PropertyID.ToString(id) ?? id.ToString();
}

/// <summary>A kind of pass a shader can provide, found by a ShaderDef pass's LightMode tag.</summary>
public readonly record struct PassType(string Name)
{
    public const string Tag = "LightMode";

    public static readonly PassType Prepass = new("Prepass");
    public static readonly PassType Forward = new("Forward");
    public static readonly PassType ShadowCaster = new("ShadowCaster");
    public static readonly PassType ObjectId = new("ObjectId");

    /// <summary>A surface's debug view, drawn in place of its Forward pass while the view sets <see cref="Shader.DebugAxis"/>. See Graphite/Debug.glsl.</summary>
    public static readonly PassType Debug = new("Debug");

    /// <summary>A world's sky, a fullscreen triangle drawn after the opaque surfaces where nothing was drawn. See Graphite/Sky.glsl.</summary>
    public static readonly PassType Sky = new("Sky");

    /// <summary>
    /// A transmissive surface's pass, drawn in place of its Forward pass while the shader's <see cref="Shader.TransmissiveAxis"/>
    /// is on, in the transparent list.
    /// </summary>
    public static readonly PassType Transmission = new("Transmission");

    /// <summary>
    /// The back faces of a transmissive volume, their distance along the view ray into the view's back depth before the
    /// transparent surfaces draw. Drawn with front faces culled, depth tested and written, unblended.
    /// </summary>
    public static readonly PassType BackDepth = new("BackDepth");

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
    Int2,
    Int3,
    Int4,
    Matrix,
    Texture2D,
    Texture2DArray,
    Texture3D,
    TextureCube,
    TextureCubeArray,
}

/// <summary>
/// A material property value. Numbers live in <see cref="Vector"/>, ints also in <see cref="Int"/>, and a texture keeps
/// its scale offset in <see cref="Vector"/> and the channel and UV set a map reads in <see cref="Int"/>.
/// </summary>
public readonly struct MaterialValue
{
    private sealed class MatrixBox(Float4x4 value)
    {
        public readonly Float4x4 Value = value;
    }

    private sealed class TextureBox(Texture? texture, SamplerDescription? sampler, float rotation)
    {
        public readonly Texture? Texture = texture;
        public readonly SamplerDescription? Sampler = sampler;
        public readonly float Rotation = rotation;
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

    /// <summary>The radians a map property turns its UVs counter clockwise by, after its scale and before its offset.</summary>
    public float Rotation => (_data as TextureBox)?.Rotation ?? 0;

    public bool IsTexture => Type >= MaterialPropertyType.Texture2D;

    /// <summary>The channel a map property reads for a scalar input, 0 to 3 for R to A.</summary>
    public int MapChannel => Int & 3;

    /// <summary>The UV set a map property samples, 0 or 1.</summary>
    public int MapUVSet => (Int >> 2) & 1;

    public static readonly Float4 IdentityScaleOffset = new(1, 1, 0, 0);

    public static MaterialValue FromFloat(float value) => new(MaterialPropertyType.Float, new Float4(value, 0, 0, 0));

    public static MaterialValue FromFloat2(Float2 value) => new(MaterialPropertyType.Float2, new Float4(value.X, value.Y, 0, 0));

    public static MaterialValue FromFloat3(Float3 value) => new(MaterialPropertyType.Float3, new Float4(value.X, value.Y, value.Z, 0));

    public static MaterialValue FromFloat4(Float4 value) => new(MaterialPropertyType.Float4, value);

    public static MaterialValue FromColor(Float4 value) => new(MaterialPropertyType.Color, value);

    public static MaterialValue FromInt(int value) => new(MaterialPropertyType.Int, new Float4(value, 0, 0, 0), value);

    // Integer vectors keep their exact bits in the vector's words.
    public static MaterialValue FromInt2(Int2 value) => new(MaterialPropertyType.Int2, IntBits(value.X, value.Y, 0, 0));

    public static MaterialValue FromInt3(Int3 value) => new(MaterialPropertyType.Int3, IntBits(value.X, value.Y, value.Z, 0));

    public static MaterialValue FromInt4(Int4 value) => new(MaterialPropertyType.Int4, IntBits(value.X, value.Y, value.Z, value.W));

    private static Float4 IntBits(int x, int y, int z, int w) => new(BitConverter.Int32BitsToSingle(x), BitConverter.Int32BitsToSingle(y), BitConverter.Int32BitsToSingle(z), BitConverter.Int32BitsToSingle(w));

    public static MaterialValue FromMatrix(Float4x4 value) => new(MaterialPropertyType.Matrix, Float4.Zero, 0, new MatrixBox(value));

    /// <summary>A texture value. A null texture means the schema default. Channel and UV set only matter to map properties.</summary>
    public static MaterialValue FromTexture(MaterialPropertyType type, Texture? texture, SamplerDescription? sampler, Float4 scaleOffset, int channel = 0, int uvSet = 0, float rotation = 0)
    {
        if (type < MaterialPropertyType.Texture2D)
            throw new ArgumentException($"{type} is not a texture type.", nameof(type));
        return new(type, scaleOffset, Mapping(channel, uvSet), new TextureBox(texture, sampler, rotation));
    }

    internal MaterialValue WithScaleOffset(Float4 scaleOffset) => new(Type, scaleOffset, Int, _data);

    internal MaterialValue WithMapping(int channel, int uvSet) => new(Type, Vector, Mapping(channel, uvSet), _data);

    internal MaterialValue WithRotation(float rotation) => new(Type, Vector, Int, new TextureBox(Texture, Sampler, rotation));

    private static int Mapping(int channel, int uvSet) => (channel & 3) | ((uvSet & 1) << 2);
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

    /// <summary>
    /// Byte offset in the material record. A texture holds its texture index here, its sampler index 4 bytes on and its
    /// scale offset at <see cref="ScaleOffsetOffset"/>. A map holds <see cref="MaterialSchema.MapSize"/> bytes here: its texture
    /// slot in bits 0 to 15, its sampler slot in 16 to 21, its channel in 22 and 23 and its UV set in 24, then as floats its
    /// scale, its offset and its rotation's cosine and sine.
    /// </summary>
    public uint Offset { get; init; }

    public uint ScaleOffsetOffset { get; init; }

    /// <summary>The bit a [Map] texture sets in the record's map mask while it holds a texture, -1 for any other property.</summary>
    public int MapBit { get; init; }

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
/// variant, then the map mask when it has [Map] textures. Records are padded to 16 bytes.
/// </summary>
public sealed class MaterialSchema
{
    private readonly Dictionary<PropertyID, int> _indices = new();
    private readonly MaterialProperty[] _properties;

    public MaterialSchema(IReadOnlyList<ShaderProperty> properties)
    {
        MaterialProperty[] result = new MaterialProperty[properties.Count];
        uint offset = 0;
        int maps = 0;
        for (int i = 0; i < properties.Count; i++)
        {
            ShaderProperty source = properties[i];
            MaterialPropertyType type = TypeOf(source.PropertyType);
            string[] attributes = source.Attributes ?? [];
            bool map = Array.IndexOf(attributes, "Map") >= 0;
            if (map && type != MaterialPropertyType.Texture2D)
                throw new ArgumentException($"The property {source.Name} is a [Map] but not a Texture2D.");
            if (map && maps == 32)
                throw new ArgumentException($"The property {source.Name} is a [Map] past the 32 a mask holds.");
            (uint size, uint alignment) = map ? (MapSize, 4u) : Std430(type);

            offset = AlignUp(offset, alignment);
            uint propertyOffset = offset;
            offset += size;
            uint scaleOffsetOffset = 0;
            if (type >= MaterialPropertyType.Texture2D && !map)
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
                MapBit = map ? maps++ : -1,
                Gamma = type == MaterialPropertyType.Color && Array.IndexOf(attributes, "Gamma") >= 0,
            };
        }

        _properties = result;
        if (maps > 0)
        {
            MapMaskOffset = (int)offset;
            offset += 4;
        }
        StrideWords = Math.Max(AlignUp(offset, 16), 16u) / 4;
    }

    public IReadOnlyList<MaterialProperty> Properties => _properties;

    /// <summary>
    /// Byte offset of the map mask after the properties, a bit per [Map] texture in declaration order set while it holds
    /// a texture, -1 for a schema without maps.
    /// </summary>
    public int MapMaskOffset { get; } = -1;

    /// <summary>Bytes a [Map] property takes in the record.</summary>
    public const uint MapSize = 28;

    /// <summary>Words per record, a multiple of 4.</summary>
    public uint StrideWords { get; }

    public int Find(PropertyID name) => _indices.TryGetValue(name, out int index) ? index : -1;

    /// <summary>Writes a record from the resolved value of each property.</summary>
    internal void Pack(ReadOnlySpan<MaterialValue> values, Span<uint> record, IMaterialResources resources)
    {
        record.Clear();
        uint present = 0;
        for (int i = 0; i < _properties.Length; i++)
        {
            ref readonly MaterialProperty property = ref _properties[i];
            MaterialValue value = values[i];
            int word = (int)(property.Offset / 4);
            if (property.MapBit >= 0)
            {
                uint slot = resources.TextureSlot(value.Texture, property.Type, property.DefaultTexture);
                uint sampler = resources.SamplerSlot(value.Sampler);
                record[word] = (slot & 0xFFFFu) | ((sampler & 63u) << 16) | ((uint)value.MapChannel << 22) | ((uint)value.MapUVSet << 24);
                WriteFloats(record, word + 1, value.ScaleOffset, 4);
                record[word + 5] = BitConverter.SingleToUInt32Bits(MathF.Cos(value.Rotation));
                record[word + 6] = BitConverter.SingleToUInt32Bits(MathF.Sin(value.Rotation));
                if (value.Texture != null)
                    present |= 1u << property.MapBit;
            }
            else if (property.Type >= MaterialPropertyType.Texture2D)
            {
                record[word] = resources.TextureSlot(value.Texture, property.Type, property.DefaultTexture);
                record[word + 1] = resources.SamplerSlot(value.Sampler);
                WriteFloats(record, (int)(property.ScaleOffsetOffset / 4), value.ScaleOffset, 4);
            }
            else
            {
                WriteNumber(property.Type, value, record, word, property.Gamma);
            }
        }
        if (MapMaskOffset >= 0)
            record[MapMaskOffset / 4] = present;
    }

    /// <summary>Writes a numeric value as std430 lays out a property of <paramref name="type"/>.</summary>
    internal static void WriteNumber(MaterialPropertyType type, in MaterialValue value, Span<uint> record, int word, bool gamma)
    {
        switch (type)
        {
            case MaterialPropertyType.Float:
                record[word] = BitConverter.SingleToUInt32Bits(value.Vector.X);
                break;
            case MaterialPropertyType.Int:
                record[word] = (uint)(value.Type == MaterialPropertyType.Int ? value.Int : (int)value.Vector.X);
                break;
            case MaterialPropertyType.Float2:
            case MaterialPropertyType.Int2:
                WriteFloats(record, word, value.Vector, 2);
                break;
            case MaterialPropertyType.Int3:
                WriteFloats(record, word, value.Vector, 3);
                break;
            case MaterialPropertyType.Int4:
                WriteFloats(record, word, value.Vector, 4);
                break;
            case MaterialPropertyType.Float3:
                WriteFloats(record, word, value.Vector, 3);
                break;
            case MaterialPropertyType.Float4:
                WriteFloats(record, word, value.Vector, 4);
                break;
            case MaterialPropertyType.Color:
                WriteFloats(record, word, gamma ? GammaToLinear(value.Vector) : value.Vector, 4);
                break;
            case MaterialPropertyType.Matrix:
                Float4x4 matrix = value.Matrix;
                WriteFloats(record, word, matrix.c0, 4);
                WriteFloats(record, word + 4, matrix.c1, 4);
                WriteFloats(record, word + 8, matrix.c2, 4);
                WriteFloats(record, word + 12, matrix.c3, 4);
                break;
        }
    }

    /// <summary>Bytes and std430 alignment of a property. A texture is its texture and sampler index, without the scale offset.</summary>
    internal static (uint Size, uint Alignment) Std430(MaterialPropertyType type) => type switch
    {
        MaterialPropertyType.Float or MaterialPropertyType.Int => (4u, 4u),
        MaterialPropertyType.Float2 or MaterialPropertyType.Int2 => (8u, 8u),
        MaterialPropertyType.Float3 or MaterialPropertyType.Int3 => (12u, 16u),
        MaterialPropertyType.Float4 or MaterialPropertyType.Color or MaterialPropertyType.Int4 => (16u, 16u),
        MaterialPropertyType.Matrix => (64u, 16u),
        _ => (8u, 4u),
    };

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

    internal static uint AlignUp(uint value, uint alignment) => (value + alignment - 1) & ~(alignment - 1);

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

public enum KeywordBindingKind : byte
{
    /// <summary>On while the texture property holds a texture.</summary>
    Texture,

    /// <summary>On while the number is not zero.</summary>
    Toggle,

    /// <summary>The axis value at the integer's index.</summary>
    Enum,
}

/// <summary>A material axis a property drives, see <see cref="Shader.KeywordBindings"/>.</summary>
public readonly record struct KeywordBinding(int Property, string Axis, KeywordBindingKind Kind);

/// <summary>A material axis of one shader, from <see cref="Shader.Axis"/>, whose value its materials pick with <see cref="Material.Set(MaterialAxis, string?)"/>.</summary>
public sealed class MaterialAxis
{
    internal MaterialAxis(Shader shader, VariantSpace space)
    {
        Shader = shader;
        Name = space.Name;
        Values = space.Values;
        IsToggle = space.IsToggle;
    }

    public Shader Shader { get; }

    public string Name { get; }

    /// <summary>Every value the axis takes, the first being its default.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>True for an on or off axis, set with <see cref="Material.Set(MaterialAxis, bool)"/>.</summary>
    public bool IsToggle { get; }

    public override string ToString() => Name;
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

    /// <summary>The pass tag of a pass reading the view's opaque colour, "SceneColor" = "Read". See Graphite/SceneColor.glsl.</summary>
    public const string SceneColorTag = "SceneColor";

    /// <summary>The pass tag of a pass reading the view's opaque depth, "SceneDepth" = "Read". See Graphite/SceneDepth.glsl.</summary>
    public const string SceneDepthTag = "SceneDepth";

    /// <summary>The pass tag of a pass reading the view's back depth, "BackDepth" = "Read". See Graphite/PBR/Transmission.glsl.</summary>
    public const string BackDepthTag = "BackDepth";

    /// <summary>
    /// The pass tag limiting a pass's SceneColor, SceneDepth and BackDepth reads to views whose global axis has a value,
    /// "ReadsWhen" = "AXIS VALUE", so views at other values pay for no copy. A view without the axis set takes its first value.
    /// </summary>
    public const string ReadsWhenTag = "ReadsWhen";

    /// <summary>
    /// The pass tag of a shader whose colour leaves already multiplied by its coverage, "Output" = "Premultiplied", so
    /// <see cref="SurfaceSettings"/> blends it with premultiplied factors. Graphite/PBR declares it.
    /// </summary>
    public const string OutputTag = "Output";

    /// <summary>
    /// The pass tag of a Forward pass whose fragments discard, "Discards" = "On", so a material without a ShadowCaster
    /// pass casts no shadow in place of one shaped by its whole depth.
    /// </summary>
    public const string DiscardsTag = "Discards";

    /// <summary>The global axis a view sets to show a debug view, which its draws take from their shaders' Debug passes.</summary>
    public const string DebugAxis = "GRAPHITE_DEBUG";

    private readonly bool[] _readsSceneColor;
    private readonly bool[] _readsSceneDepth;
    private readonly bool[] _readsBackDepth;
    private readonly (string Axis, string Value)?[] _readsWhen;
    private readonly HashSet<string>[] _passAxes;

    /// <summary>The shader drawing in this one's place while its variants compile or after they failed, from its Fallback declaration.</summary>
    public Shader? Fallback { get; set; }

    /// <summary>A shader drawing a copy of the definition, which is left as it is.</summary>
    public Shader(ShaderDefinition definition, IShaderCompiler? compiler)
    {
        definition = definition.Copy();
        ExpandSurface(definition);
        Definition = definition;
        Name = definition.Name ?? "";
        Schema = new MaterialSchema(definition.Properties ?? []);
        ShaderPass[] passes = definition.Passes ?? [];
        MaterialAbi abi = MaterialLayout.Abi(Schema);
        foreach (ShaderPass pass in passes)
            pass.Material = abi;
        _readsSceneColor = new bool[passes.Length];
        _readsSceneDepth = new bool[passes.Length];
        _readsBackDepth = new bool[passes.Length];
        _readsWhen = new (string, string)?[passes.Length];
        for (int i = 0; i < passes.Length; i++)
        {
            _readsSceneColor[i] = ShaderDefinition.PassHasTag(passes[i], SceneColorTag, "Read");
            _readsSceneDepth[i] = ShaderDefinition.PassHasTag(passes[i], SceneDepthTag, "Read");
            _readsBackDepth[i] = ShaderDefinition.PassHasTag(passes[i], BackDepthTag, "Read");
            if (passes[i].Tags != null && passes[i].Tags!.TryGetValue(ReadsWhenTag, out string? when) && when.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [string axis, string value])
                _readsWhen[i] = (axis, value);
            PremultipliedOutput |= ShaderDefinition.PassHasTag(passes[i], OutputTag, "Premultiplied");
        }

        Dictionary<string, VariantSpace> axes = new(StringComparer.Ordinal);
        List<VariantSpace> material = new();
        List<VariantSpace> global = new();
        int queue = DefaultRenderQueue;
        _passAxes = new HashSet<string>[passes.Length];
        for (int p = 0; p < passes.Length; p++)
        {
            ShaderPass pass = passes[p];
            _passAxes[p] = new HashSet<string>(StringComparer.Ordinal);
            foreach (VariantSpace axis in pass.AxesWith(compiler))
            {
                _passAxes[p].Add(axis.Name);
                if (axes.TryGetValue(axis.Name, out VariantSpace existing))
                {
                    if (existing.Scope != axis.Scope)
                        throw new InvalidOperationException($"The shader {Name} declares the axis {axis.Name} as both material and global.");
                    if (!System.Linq.Enumerable.SequenceEqual(existing.Values, axis.Values))
                        throw new InvalidOperationException($"The shader {Name} declares the axis {axis.Name} with values {string.Join(" ", existing.Values)} in one pass and {string.Join(" ", axis.Values)} in another.");
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
        foreach (VariantSpace axis in material)
            _materialAxes[axis.Name] = new MaterialAxis(this, axis);
        RenderQueue = queue;
        KeywordBindings = BindKeywords();
        TwoSidedAxis = FindAttributeAxis("TwoSided");
        TransmissiveAxis = FindAttributeAxis("Transmissive");
    }

    private static readonly System.Text.RegularExpressions.Regex s_surfaceMarker = new(@"^[ \t]*#[ \t]*pragma[ \t]+surface[ \t]*\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);
    private static readonly System.Text.RegularExpressions.Regex s_vertexMarker = new(@"^[ \t]*#[ \t]*pragma[ \t]+surface_vertex[ \t]*\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);
    private static readonly System.Text.RegularExpressions.Regex s_brdfDefinition = new(@"\bvec3\s+Graphite_BRDF\s*\(");
    private static readonly System.Text.RegularExpressions.Regex s_ambientDefinition = new(@"\bvec3\s+Graphite_AmbientBRDF\s*\(");
    private static readonly System.Text.RegularExpressions.Regex s_lightingMarker = new(@"^[ \t]*#[ \t]*pragma[ \t]+surface_lighting[ \t]*\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);

    /// <summary>
    /// Expands a Surface block into the passes of its model, the library shader Graphite/Standard/{Model}.shader: each
    /// "#pragma surface" line of the model's passes becomes the block's code, which defines the model's Surface function,
    /// void Surface(inout PBR_Params p, Graphite_SurfaceData i) for PBR, before the surface is lit, with
    /// GRAPHITE_SURFACE_FUNCTION defined. A Lighting block's code defines Graphite_BRDF, Graphite_AmbientBRDF or both
    /// for the passes with a "#pragma surface_lighting" line, which becomes GRAPHITE_CUSTOM_BRDF and
    /// GRAPHITE_CUSTOM_AMBIENT_BRDF for the ones it defines, the code following the block's at their "#pragma surface". A Vertex block's code, defining void SurfaceVertex(inout Graphite_SurfaceVertex v), goes
    /// in place of each "#pragma surface_vertex" line with GRAPHITE_SURFACE_VERTEX_FUNCTION defined. The model's
    /// properties come before the shader's own, which replace any of the same name. A shader naming no Fallback takes the
    /// model's.
    /// </summary>
    private static void ExpandSurface(ShaderDefinition definition)
    {
        if (definition.Surface is not { } surface)
            return;
        string name = definition.Name ?? "";
        string path = $"{ShaderLibrary.Prefix}Standard/{surface.Model}.shader";
        InternalShaders.Register();
        string source = ShaderLibrary.Get(path)
            ?? throw new ShaderLoadException($"{name}:{surface.Line}: there is no surface model \"{surface.Model}\", a model is a shader at {path}.");
        ShaderDefinition model = ShaderParser.Parse(source);

        string code = surface.Code.Length > 0 ? "#define GRAPHITE_SURFACE_FUNCTION\n" + surface.Code : "";
        string lighting = surface.Lighting;
        string lightingDefines = (s_brdfDefinition.IsMatch(lighting) ? "#define GRAPHITE_CUSTOM_BRDF\n" : "")
            + (s_ambientDefinition.IsMatch(lighting) ? "#define GRAPHITE_CUSTOM_AMBIENT_BRDF\n" : "");
        if (lighting.Length > 0 && lightingDefines.Length == 0)
            throw new ShaderLoadException($"{name}:{surface.Line}: the Lighting block defines neither Graphite_BRDF nor Graphite_AmbientBRDF.");
        string vertex = surface.Vertex.Length > 0 ? "#define GRAPHITE_SURFACE_VERTEX_FUNCTION\n" + surface.Vertex : "";
        ShaderPass[] standard = model.Passes ?? [];
        bool spliced = false;
        bool lit = false;
        bool moved = false;
        foreach (ShaderPass pass in standard)
        {
            moved |= s_vertexMarker.IsMatch(pass.Source);
            pass.Source = s_vertexMarker.Replace(pass.Source, _ => vertex);

            string passCode = code;
            if (s_lightingMarker.IsMatch(pass.Source))
            {
                lit = true;
                pass.Source = s_lightingMarker.Replace(pass.Source, _ => lightingDefines);
                if (lighting.Length > 0)
                    passCode = code + "\n" + lighting;
            }
            spliced |= s_surfaceMarker.IsMatch(pass.Source);
            pass.Source = s_surfaceMarker.Replace(pass.Source, _ => passCode);
            pass.SourceName = name;
        }
        if (code.Length > 0 && !spliced)
            throw new ShaderLoadException($"{name}:{surface.Line}: the surface model {path} has no \"#pragma surface\" line for the Surface block's code.");
        if (vertex.Length > 0 && !moved)
            throw new ShaderLoadException($"{name}:{surface.Line}: the surface model {path} has no \"#pragma surface_vertex\" line for the Vertex block's code.");
        if (lighting.Length > 0 && !lit)
            throw new ShaderLoadException($"{name}:{surface.Line}: the surface model {path} has no \"#pragma surface_lighting\" line for the Lighting block's code.");

        foreach (ShaderPass own in definition.Passes ?? [])
        {
            if (Array.Exists(standard, p => p.Name == own.Name))
                throw new ShaderLoadException($"{name}: the pass name {own.Name} is taken by the Surface block's passes.");
        }
        definition.Passes = [.. standard, .. definition.Passes ?? []];
        if (string.IsNullOrEmpty(definition.Fallback))
            definition.Fallback = model.Fallback;

        List<ShaderProperty> properties = new(model.Properties ?? []);
        foreach (ShaderProperty property in definition.Properties ?? [])
        {
            int index = properties.FindIndex(p => p.Name == property.Name);
            if (index >= 0)
                properties[index] = property;
            else
                properties.Add(property);
        }
        definition.Properties = [.. properties];
        definition.Surface = null;
    }

    /// <summary>
    /// Material axes that properties drive: a texture with [Keyword(AXIS)] turns its toggle on while a texture is set, a
    /// number with [Toggle(AXIS)] while it is not zero, and an integer with [KeywordEnum(AXIS)] picks the value at its index.
    /// A toggle several properties drive is on while any of them is.
    /// </summary>
    public IReadOnlyList<KeywordBinding> KeywordBindings { get; }

    private KeywordBinding[] BindKeywords()
    {
        List<KeywordBinding> bindings = new();
        for (int i = 0; i < Properties.Count; i++)
        {
            foreach (string attribute in Properties[i].Attributes)
            {
                KeywordBindingKind? kind = attribute.StartsWith("Keyword(", StringComparison.Ordinal) ? KeywordBindingKind.Texture
                    : attribute.StartsWith("Toggle(", StringComparison.Ordinal) ? KeywordBindingKind.Toggle
                    : attribute.StartsWith("KeywordEnum(", StringComparison.Ordinal) ? KeywordBindingKind.Enum
                    : null;
                if (kind == null || !attribute.EndsWith(')'))
                    continue;
                string axis = attribute[(attribute.IndexOf('(') + 1)..^1].Trim();
                if (FindAxis(axis, out VariantScope scope) >= 0 && scope == VariantScope.Material)
                    bindings.Add(new KeywordBinding(i, axis, kind.Value));
            }
        }
        return [.. bindings];
    }

    /// <summary>
    /// The toggle axis of the property marked [TwoSided], or null: while it is on, a material draws both sides whatever its
    /// <see cref="SurfaceCull"/>, so back faces shade with a flipped normal.
    /// </summary>
    public string? TwoSidedAxis { get; }

    /// <summary>
    /// The toggle axis of the property marked [Transmissive], or null: while it is on, a material draws as transparent
    /// whatever its <see cref="SurfaceSettings"/>, through its <see cref="PassType.Transmission"/> pass in place of Forward,
    /// and volumes, those not two sided, draw their <see cref="PassType.BackDepth"/> pass.
    /// </summary>
    public string? TransmissiveAxis { get; }

    private string? FindAttributeAxis(string attribute)
    {
        foreach (KeywordBinding binding in KeywordBindings)
        {
            if (binding.Kind == KeywordBindingKind.Toggle && Properties[binding.Property].HasAttribute(attribute))
                return binding.Axis;
        }
        return null;
    }

    public string Name { get; }

    public ShaderDefinition Definition { get; }

    public MaterialSchema Schema { get; }

    public IReadOnlyList<MaterialProperty> Properties => Schema.Properties;

    public IReadOnlyList<VariantSpace> MaterialAxes { get; }

    public IReadOnlyList<VariantSpace> GlobalAxes { get; }

    private readonly Dictionary<string, MaterialAxis> _materialAxes = new(StringComparer.Ordinal);

    /// <summary>The material axis of a name, for <see cref="Material.Set(MaterialAxis, string?)"/>. Throws when the shader has no such axis or it is global.</summary>
    public MaterialAxis Axis(string name)
    {
        if (_materialAxes.TryGetValue(name, out MaterialAxis? axis))
            return axis;
        if (FindAxis(name, out _) >= 0)
            throw new ArgumentException($"The axis {name} is global in {Name}, the renderer, a world or a view sets it, not a material.", nameof(name));
        throw new ArgumentException($"The shader {Name} has no axis {name}.", nameof(name));
    }

    /// <summary>Whether a pass declares an axis, so its selection can tell that pass's programs apart.</summary>
    internal bool PassHasAxis(int passIndex, string axis) => _passAxes[passIndex].Contains(axis);

    /// <summary>The material variant as one pass sees it, axes the pass does not declare left out. Keywords in material axis order.</summary>
    internal ulong PassVariant(int passIndex, Keyword[] keywords)
    {
        ulong variant = 0, stride = 1;
        for (int i = 0; i < MaterialAxes.Count; i++)
        {
            VariantSpace axis = MaterialAxes[i];
            if (!_passAxes[passIndex].Contains(axis.Name))
                continue;
            for (int v = 0; v < axis.Values.Count; v++)
            {
                if (axis.Values[v] == keywords[i].Value)
                    variant += (ulong)v * stride;
            }
            stride *= (ulong)axis.Values.Count;
        }
        return variant;
    }

    /// <summary>Default render queue of the shader's materials, from a RenderQueue pass tag.</summary>
    public int RenderQueue { get; }

    /// <summary>Whether a pass declares "Output" = "Premultiplied", see <see cref="OutputTag"/>.</summary>
    public bool PremultipliedOutput { get; }

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

    /// <summary>Whether any pass of the shader reads the view's opaque colour through its SceneColor tag.</summary>
    public bool ReadsSceneColor => Array.IndexOf(_readsSceneColor, true) >= 0;

    internal bool PassReadsSceneColor(int passIndex) => passIndex >= 0 && passIndex < _readsSceneColor.Length && _readsSceneColor[passIndex];

    /// <summary>Whether any pass of the shader reads the view's opaque depth through its SceneDepth tag.</summary>
    public bool ReadsSceneDepth => Array.IndexOf(_readsSceneDepth, true) >= 0;

    internal bool PassReadsSceneDepth(int passIndex) => passIndex >= 0 && passIndex < _readsSceneDepth.Length && _readsSceneDepth[passIndex];

    /// <summary>Whether any pass of the shader reads the view's back depth through its BackDepth tag.</summary>
    public bool ReadsBackDepth => Array.IndexOf(_readsBackDepth, true) >= 0;

    internal bool PassReadsBackDepth(int passIndex) => passIndex >= 0 && passIndex < _readsBackDepth.Length && _readsBackDepth[passIndex];

    /// <summary>Whether a pass's screen reads count in a view with these global keywords, see <see cref="ReadsWhenTag"/>.</summary>
    internal bool PassReadsIn(int passIndex, IReadOnlyDictionary<string, string> keywords)
    {
        if (passIndex < 0 || passIndex >= _readsWhen.Length || _readsWhen[passIndex] is not (string axis, string value))
            return true;
        if (keywords.TryGetValue(axis, out string? set))
            return set == value;
        return GlobalAxisDefault(axis) is not { } first || first == value;
    }

    private string? GlobalAxisDefault(string axis)
    {
        foreach (VariantSpace space in GlobalAxes)
        {
            if (space.Name == axis)
                return space.Values[0];
        }
        return null;
    }

    private readonly Dictionary<string, int> _passIndices = new(StringComparer.Ordinal);

    /// <summary>Index of the definition's pass for a pass type, or -1 when the shader has none.</summary>
    internal int PassIndex(PassType type)
    {
        lock (_passIndices)
        {
            if (!_passIndices.TryGetValue(type.Name, out int index))
            {
                index = Definition.GetPassWithTag(PassType.Tag, type.Name) ?? -1;
                _passIndices[type.Name] = index;
            }
            return index;
        }
    }

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
