using Prowl.Prism.Glsl;
using Prowl.Prism.SpirV;

namespace Prowl.Prism.Compiler;

/// <summary>The base scalar of a GLSL type.</summary>
public enum Scalar { Void, Bool, Int, UInt, Float, Half, Double, Int64, UInt64, Int8, UInt8, Int16, UInt16 }

/// <summary>The ruleset an interface block lays its members out under.</summary>
public enum BlockLayout { Std140, Std430, Scalar }

/// <summary>A resolved GLSL type. The semantic model annotates expressions with these.</summary>
public abstract record GlslType;

public sealed record ScalarType(Scalar Kind) : GlslType;
public sealed record VectorType(Scalar Component, int Size) : GlslType;
public sealed record MatrixType(int Columns, int Rows, Scalar Component = Scalar.Float) : GlslType;   // columns x rows
public sealed record SamplerType(Dim Dim, bool Arrayed, bool Shadow, Scalar Sampled = Scalar.Float, bool Multisampled = false) : GlslType;   // combined sampled image
public sealed record TextureType(Dim Dim, bool Arrayed, bool Shadow, Scalar Sampled = Scalar.Float, bool Multisampled = false) : GlslType;   // bare image (separate texture)
public sealed record ImageType(Dim Dim, bool Arrayed, ImageFormat Format, Scalar Sampled = Scalar.Float, bool Multisampled = false) : GlslType;   // storage image (image2D/uimage2D/iimage2D)
public sealed record PureSamplerType(bool Shadow = false) : GlslType;                // bare sampler state (sampler / samplerShadow)
public sealed record AccelStructType : GlslType;                                   // accelerationStructureEXT
public sealed record RayQueryType : GlslType;                                      // rayQueryEXT (a Function-local)
// SpecLength is the specialization-constant length expression (a bare spec-const identifier or an arithmetic
// expression over them), when the size is only fixed at pipeline creation; Length is then unused.
public sealed record ArrayType(GlslType Element, int Length, Expr? SpecLength = null) : GlslType;
public sealed record RuntimeArrayType(GlslType Element) : GlslType;               // "[]" storage-buffer array
public sealed record StructType(string Name, IReadOnlyList<(string Name, GlslType Type)> Fields) : GlslType;
public sealed record CoopMatType(Scalar Component, uint Scope, uint Rows, uint Cols, uint Use) : GlslType;
// A GL_EXT_buffer_reference type: a 64-bit pointer to a Block-decorated struct in PhysicalStorageBuffer.
public sealed record BufferReferenceType(string Name) : GlslType;

/// <summary>Resolves built-in GLSL type names to <see cref="GlslType"/>.</summary>
public static class Types
{
    public static readonly ScalarType Void = new(Scalar.Void);
    public static readonly ScalarType Bool = new(Scalar.Bool);
    public static readonly ScalarType Int = new(Scalar.Int);
    public static readonly ScalarType UInt = new(Scalar.UInt);
    public static readonly ScalarType Float = new(Scalar.Float);
    public static readonly ScalarType Half = new(Scalar.Half);
    public static readonly ScalarType Double = new(Scalar.Double);
    public static readonly ScalarType Int64 = new(Scalar.Int64);
    public static readonly ScalarType UInt64 = new(Scalar.UInt64);
    public static readonly ScalarType Int8 = new(Scalar.Int8);
    public static readonly ScalarType UInt8 = new(Scalar.UInt8);
    public static readonly ScalarType Int16 = new(Scalar.Int16);
    public static readonly ScalarType UInt16 = new(Scalar.UInt16);

    public static GlslType? Resolve(string name) => name switch
    {
        "void" => Void,
        "bool" => Bool,
        "int" => Int,
        "uint" => UInt,
        "float" => Float,
        "float16_t" => Half,
        "double" => Double,
        "int64_t" => Int64,
        "uint64_t" => UInt64,
        "int8_t" => Int8,
        "uint8_t" => UInt8,
        "int16_t" => Int16,
        "uint16_t" => UInt16,
        "i8vec2" => new VectorType(Scalar.Int8, 2),
        "i8vec3" => new VectorType(Scalar.Int8, 3),
        "i8vec4" => new VectorType(Scalar.Int8, 4),
        "u8vec2" => new VectorType(Scalar.UInt8, 2),
        "u8vec3" => new VectorType(Scalar.UInt8, 3),
        "u8vec4" => new VectorType(Scalar.UInt8, 4),
        "i16vec2" => new VectorType(Scalar.Int16, 2),
        "i16vec3" => new VectorType(Scalar.Int16, 3),
        "i16vec4" => new VectorType(Scalar.Int16, 4),
        "u16vec2" => new VectorType(Scalar.UInt16, 2),
        "u16vec3" => new VectorType(Scalar.UInt16, 3),
        "u16vec4" => new VectorType(Scalar.UInt16, 4),
        "i64vec2" => new VectorType(Scalar.Int64, 2),
        "i64vec3" => new VectorType(Scalar.Int64, 3),
        "i64vec4" => new VectorType(Scalar.Int64, 4),
        "u64vec2" => new VectorType(Scalar.UInt64, 2),
        "u64vec3" => new VectorType(Scalar.UInt64, 3),
        "u64vec4" => new VectorType(Scalar.UInt64, 4),
        "vec2" => new VectorType(Scalar.Float, 2),
        "vec3" => new VectorType(Scalar.Float, 3),
        "vec4" => new VectorType(Scalar.Float, 4),
        "dvec2" => new VectorType(Scalar.Double, 2),
        "dvec3" => new VectorType(Scalar.Double, 3),
        "dvec4" => new VectorType(Scalar.Double, 4),
        "f16vec2" => new VectorType(Scalar.Half, 2),
        "f16vec3" => new VectorType(Scalar.Half, 3),
        "f16vec4" => new VectorType(Scalar.Half, 4),
        "ivec2" => new VectorType(Scalar.Int, 2),
        "ivec3" => new VectorType(Scalar.Int, 3),
        "ivec4" => new VectorType(Scalar.Int, 4),
        "uvec2" => new VectorType(Scalar.UInt, 2),
        "uvec3" => new VectorType(Scalar.UInt, 3),
        "uvec4" => new VectorType(Scalar.UInt, 4),
        "bvec2" => new VectorType(Scalar.Bool, 2),
        "bvec3" => new VectorType(Scalar.Bool, 3),
        "bvec4" => new VectorType(Scalar.Bool, 4),
        "mat2" or "mat2x2" => new MatrixType(2, 2),
        "mat3" or "mat3x3" => new MatrixType(3, 3),
        "mat4" or "mat4x4" => new MatrixType(4, 4),
        "mat2x3" => new MatrixType(2, 3),   // matCxR: C columns, R rows
        "mat2x4" => new MatrixType(2, 4),
        "mat3x2" => new MatrixType(3, 2),
        "mat3x4" => new MatrixType(3, 4),
        "mat4x2" => new MatrixType(4, 2),
        "mat4x3" => new MatrixType(4, 3),
        "dmat2" or "dmat2x2" => new MatrixType(2, 2, Scalar.Double),
        "dmat3" or "dmat3x3" => new MatrixType(3, 3, Scalar.Double),
        "dmat4" or "dmat4x4" => new MatrixType(4, 4, Scalar.Double),
        "dmat2x3" => new MatrixType(2, 3, Scalar.Double),
        "dmat2x4" => new MatrixType(2, 4, Scalar.Double),
        "dmat3x2" => new MatrixType(3, 2, Scalar.Double),
        "dmat3x4" => new MatrixType(3, 4, Scalar.Double),
        "dmat4x2" => new MatrixType(4, 2, Scalar.Double),
        "dmat4x3" => new MatrixType(4, 3, Scalar.Double),
        "f16mat2" or "f16mat2x2" => new MatrixType(2, 2, Scalar.Half),
        "f16mat3" or "f16mat3x3" => new MatrixType(3, 3, Scalar.Half),
        "f16mat4" or "f16mat4x4" => new MatrixType(4, 4, Scalar.Half),
        "f16mat2x3" => new MatrixType(2, 3, Scalar.Half),
        "f16mat2x4" => new MatrixType(2, 4, Scalar.Half),
        "f16mat3x2" => new MatrixType(3, 2, Scalar.Half),
        "f16mat3x4" => new MatrixType(3, 4, Scalar.Half),
        "f16mat4x2" => new MatrixType(4, 2, Scalar.Half),
        "f16mat4x3" => new MatrixType(4, 3, Scalar.Half),
        "sampler2D" => new SamplerType(Dim.D2, Arrayed: false, Shadow: false),
        "sampler3D" => new SamplerType(Dim.D3, Arrayed: false, Shadow: false),
        "samplerCube" => new SamplerType(Dim.Cube, Arrayed: false, Shadow: false),
        "sampler2DArray" => new SamplerType(Dim.D2, Arrayed: true, Shadow: false),
        "samplerCubeArray" => new SamplerType(Dim.Cube, Arrayed: true, Shadow: false),
        "sampler2DShadow" => new SamplerType(Dim.D2, Arrayed: false, Shadow: true),
        "sampler2DArrayShadow" => new SamplerType(Dim.D2, Arrayed: true, Shadow: true),
        "samplerCubeShadow" => new SamplerType(Dim.Cube, Arrayed: false, Shadow: true),
        "sampler2DMS" => new SamplerType(Dim.D2, Arrayed: false, Shadow: false, Multisampled: true),
        "isampler2D" => new SamplerType(Dim.D2, Arrayed: false, Shadow: false, Scalar.Int),
        "isampler3D" => new SamplerType(Dim.D3, Arrayed: false, Shadow: false, Scalar.Int),
        "isampler2DArray" => new SamplerType(Dim.D2, Arrayed: true, Shadow: false, Scalar.Int),
        "usampler2D" => new SamplerType(Dim.D2, Arrayed: false, Shadow: false, Scalar.UInt),
        "usampler3D" => new SamplerType(Dim.D3, Arrayed: false, Shadow: false, Scalar.UInt),
        "usampler2DArray" => new SamplerType(Dim.D2, Arrayed: true, Shadow: false, Scalar.UInt),
        "sampler1D" => new SamplerType(Dim.D1, Arrayed: false, Shadow: false),
        "sampler1DArray" => new SamplerType(Dim.D1, Arrayed: true, Shadow: false),
        "sampler1DShadow" => new SamplerType(Dim.D1, Arrayed: false, Shadow: true),
        "sampler1DArrayShadow" => new SamplerType(Dim.D1, Arrayed: true, Shadow: true),
        "isampler1D" => new SamplerType(Dim.D1, Arrayed: false, Shadow: false, Scalar.Int),
        "usampler1D" => new SamplerType(Dim.D1, Arrayed: false, Shadow: false, Scalar.UInt),
        "samplerBuffer" => new SamplerType(Dim.Buffer, Arrayed: false, Shadow: false),
        "isamplerBuffer" => new SamplerType(Dim.Buffer, Arrayed: false, Shadow: false, Scalar.Int),
        "usamplerBuffer" => new SamplerType(Dim.Buffer, Arrayed: false, Shadow: false, Scalar.UInt),
        "sampler2DRect" => new SamplerType(Dim.Rect, Arrayed: false, Shadow: false),
        "sampler2DRectShadow" => new SamplerType(Dim.Rect, Arrayed: false, Shadow: true),
        "isampler2DRect" => new SamplerType(Dim.Rect, Arrayed: false, Shadow: false, Scalar.Int),
        "usampler2DRect" => new SamplerType(Dim.Rect, Arrayed: false, Shadow: false, Scalar.UInt),
        "sampler2DMSArray" => new SamplerType(Dim.D2, Arrayed: true, Shadow: false, Multisampled: true),
        "isampler2DMSArray" => new SamplerType(Dim.D2, Arrayed: true, Shadow: false, Scalar.Int, Multisampled: true),
        "usampler2DMSArray" => new SamplerType(Dim.D2, Arrayed: true, Shadow: false, Scalar.UInt, Multisampled: true),
        "isampler2DMS" => new SamplerType(Dim.D2, Arrayed: false, Shadow: false, Scalar.Int, Multisampled: true),
        "usampler2DMS" => new SamplerType(Dim.D2, Arrayed: false, Shadow: false, Scalar.UInt, Multisampled: true),
        "samplerCubeArrayShadow" => new SamplerType(Dim.Cube, Arrayed: true, Shadow: true),
        "isamplerCube" => new SamplerType(Dim.Cube, Arrayed: false, Shadow: false, Scalar.Int),
        "usamplerCube" => new SamplerType(Dim.Cube, Arrayed: false, Shadow: false, Scalar.UInt),
        "isamplerCubeArray" => new SamplerType(Dim.Cube, Arrayed: true, Shadow: false, Scalar.Int),
        "usamplerCubeArray" => new SamplerType(Dim.Cube, Arrayed: true, Shadow: false, Scalar.UInt),
        "texture2D" => new TextureType(Dim.D2, Arrayed: false, Shadow: false),
        "texture3D" => new TextureType(Dim.D3, Arrayed: false, Shadow: false),
        "textureCube" => new TextureType(Dim.Cube, Arrayed: false, Shadow: false),
        "texture2DArray" => new TextureType(Dim.D2, Arrayed: true, Shadow: false),
        "sampler" => new PureSamplerType(),
        "samplerShadow" => new PureSamplerType(Shadow: true),
        "accelerationStructureEXT" => new AccelStructType(),
        "accelerationStructureNV" => new AccelStructType(),
        "rayQueryEXT" => new RayQueryType(),
        "subpassInput" => new ImageType(Dim.SubpassData, Arrayed: false, ImageFormat.Unknown),
        "subpassInputMS" => new ImageType(Dim.SubpassData, Arrayed: false, ImageFormat.Unknown, Multisampled: true),
        "isubpassInputMS" => new ImageType(Dim.SubpassData, Arrayed: false, ImageFormat.Unknown, Scalar.Int, Multisampled: true),
        "usubpassInputMS" => new ImageType(Dim.SubpassData, Arrayed: false, ImageFormat.Unknown, Scalar.UInt, Multisampled: true),
        "isubpassInput" => new ImageType(Dim.SubpassData, Arrayed: false, ImageFormat.Unknown, Scalar.Int),
        "usubpassInput" => new ImageType(Dim.SubpassData, Arrayed: false, ImageFormat.Unknown, Scalar.UInt),
        "image2D" => new ImageType(Dim.D2, Arrayed: false, ImageFormat.Unknown),
        "image3D" => new ImageType(Dim.D3, Arrayed: false, ImageFormat.Unknown),
        "imageCube" => new ImageType(Dim.Cube, Arrayed: false, ImageFormat.Unknown),
        "image2DArray" => new ImageType(Dim.D2, Arrayed: true, ImageFormat.Unknown),
        "uimage2D" => new ImageType(Dim.D2, Arrayed: false, ImageFormat.Unknown, Scalar.UInt),
        "uimage3D" => new ImageType(Dim.D3, Arrayed: false, ImageFormat.Unknown, Scalar.UInt),
        "uimage2DArray" => new ImageType(Dim.D2, Arrayed: true, ImageFormat.Unknown, Scalar.UInt),
        "image1D" => new ImageType(Dim.D1, Arrayed: false, ImageFormat.Unknown),
        "iimage1D" => new ImageType(Dim.D1, Arrayed: false, ImageFormat.Unknown, Scalar.Int),
        "uimage1D" => new ImageType(Dim.D1, Arrayed: false, ImageFormat.Unknown, Scalar.UInt),
        "imageBuffer" => new ImageType(Dim.Buffer, Arrayed: false, ImageFormat.Unknown),
        "iimageBuffer" => new ImageType(Dim.Buffer, Arrayed: false, ImageFormat.Unknown, Scalar.Int),
        "uimageBuffer" => new ImageType(Dim.Buffer, Arrayed: false, ImageFormat.Unknown, Scalar.UInt),
        "image2DMS" => new ImageType(Dim.D2, Arrayed: false, ImageFormat.Unknown, Multisampled: true),
        "image2DMSArray" => new ImageType(Dim.D2, Arrayed: true, ImageFormat.Unknown, Multisampled: true),
        "iimageCube" => new ImageType(Dim.Cube, Arrayed: false, ImageFormat.Unknown, Scalar.Int),
        "uimageCube" => new ImageType(Dim.Cube, Arrayed: false, ImageFormat.Unknown, Scalar.UInt),
        "imageCubeArray" => new ImageType(Dim.Cube, Arrayed: true, ImageFormat.Unknown),
        "iimageCubeArray" => new ImageType(Dim.Cube, Arrayed: true, ImageFormat.Unknown, Scalar.Int),
        "uimageCubeArray" => new ImageType(Dim.Cube, Arrayed: true, ImageFormat.Unknown, Scalar.UInt),
        "iimage2D" => new ImageType(Dim.D2, Arrayed: false, ImageFormat.Unknown, Scalar.Int),
        "iimage3D" => new ImageType(Dim.D3, Arrayed: false, ImageFormat.Unknown, Scalar.Int),
        "iimage2DArray" => new ImageType(Dim.D2, Arrayed: true, ImageFormat.Unknown, Scalar.Int),
        _ => ResolveOpaque(name),
    };

    // The full opaque-type family glslang declares: [i|u|i64|u64|f16] + sampler|texture|image + 1D|2D|3D|Cube|
    // 2DRect|Buffer|2DMS + [Array] + [Shadow], and [i|u]subpassInput[MS].
    static GlslType? ResolveOpaque(string name)
    {
        string rest = name;
        Scalar sampled = Scalar.Float;
        foreach (var (prefix, kind) in new[] { ("i64", Scalar.Int64), ("u64", Scalar.UInt64), ("f16", Scalar.Half), ("i", Scalar.Int), ("u", Scalar.UInt) })
            if (rest.StartsWith(prefix, StringComparison.Ordinal) && !rest.StartsWith("image", StringComparison.Ordinal))
            {
                sampled = kind; rest = rest[prefix.Length..]; break;
            }
        string? cls = rest.StartsWith("sampler", StringComparison.Ordinal) ? "sampler"
            : rest.StartsWith("texture", StringComparison.Ordinal) ? "texture"
            : rest.StartsWith("image", StringComparison.Ordinal) ? "image" : null;
        if (cls is null) return null;
        rest = rest[cls.Length..];
        Dim dim; bool ms = false;
        if (rest.StartsWith("2DRect", StringComparison.Ordinal)) { dim = Dim.Rect; rest = rest[6..]; }
        else if (rest.StartsWith("2DMS", StringComparison.Ordinal)) { dim = Dim.D2; ms = true; rest = rest[4..]; }
        else if (rest.StartsWith("1D", StringComparison.Ordinal)) { dim = Dim.D1; rest = rest[2..]; }
        else if (rest.StartsWith("2D", StringComparison.Ordinal)) { dim = Dim.D2; rest = rest[2..]; }
        else if (rest.StartsWith("3D", StringComparison.Ordinal)) { dim = Dim.D3; rest = rest[2..]; }
        else if (rest.StartsWith("Cube", StringComparison.Ordinal)) { dim = Dim.Cube; rest = rest[4..]; }
        else if (rest.StartsWith("Buffer", StringComparison.Ordinal)) { dim = Dim.Buffer; rest = rest[6..]; }
        else return null;
        bool arrayed = rest.StartsWith("Array", StringComparison.Ordinal);
        if (arrayed) rest = rest[5..];
        bool shadow = rest == "Shadow";
        if (rest.Length > 0 && !shadow) return null;
        if (arrayed && dim is Dim.D3 or Dim.Rect or Dim.Buffer) return null;
        if (shadow && (cls == "image" || sampled != Scalar.Float || dim is Dim.D3 or Dim.Buffer || ms)) return null;
        return cls switch
        {
            "sampler" => new SamplerType(dim, arrayed, shadow, sampled, ms),
            "texture" => new TextureType(dim, arrayed, shadow, sampled, ms),
            _ => new ImageType(dim, arrayed, ImageFormat.Unknown, sampled, ms),
        };
    }

    public static bool IsTypeName(string name) => Resolve(name) is not null;

    /// <summary>The canonical GLSL spelling of a type, for user-facing diagnostics.</summary>
    public static string Name(GlslType t) => t switch
    {
        ScalarType s => ScalarName(s.Kind),
        VectorType v => VectorPrefix(v.Component) + v.Size,
        MatrixType m => MatrixName(m),
        ArrayType a => $"{Name(a.Element)}[{a.Length}]",
        RuntimeArrayType r => $"{Name(r.Element)}[]",
        StructType st => $"struct {st.Name}",
        BufferReferenceType b => b.Name,
        CoopMatType => "coopmat",
        SamplerType or TextureType or ImageType or PureSamplerType => "an opaque type",
        _ => "a value",
    };

    public static string ScalarName(Scalar s) => s switch
    {
        Scalar.Void => "void", Scalar.Bool => "bool", Scalar.Int => "int", Scalar.UInt => "uint",
        Scalar.Float => "float", Scalar.Half => "float16_t", Scalar.Double => "double",
        Scalar.Int64 => "int64_t", Scalar.UInt64 => "uint64_t", Scalar.Int8 => "int8_t",
        Scalar.UInt8 => "uint8_t", Scalar.Int16 => "int16_t", Scalar.UInt16 => "uint16_t", _ => "?",
    };

    static string VectorPrefix(Scalar c) => c switch
    {
        Scalar.Float => "vec", Scalar.Double => "dvec", Scalar.Int => "ivec", Scalar.UInt => "uvec",
        Scalar.Bool => "bvec", Scalar.Half => "f16vec", Scalar.Int64 => "i64vec", Scalar.UInt64 => "u64vec",
        Scalar.Int8 => "i8vec", Scalar.UInt8 => "u8vec", Scalar.Int16 => "i16vec", Scalar.UInt16 => "u16vec", _ => "vec",
    };

    static string MatrixName(MatrixType m)
    {
        string prefix = m.Component == Scalar.Double ? "dmat" : "mat";
        return m.Columns == m.Rows ? $"{prefix}{m.Columns}" : $"{prefix}{m.Columns}x{m.Rows}";
    }
}
