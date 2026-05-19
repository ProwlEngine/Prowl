using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// glTF 2.0 root document. Mirrors <see href="https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html"/>.
/// </summary>
/// <remarks>
/// Optional arrays are typed as nullable so the deserializer can leave them as <c>null</c> rather
/// than empty; consumers should check with <c>?? Array.Empty&lt;...&gt;()</c>. Extensions and extras
/// are kept as raw <see cref="JsonElement"/> so unknown extensions passthrough to the public
/// <see cref="Material.RawExtensions"/> / <see cref="ModelMetadata.RawExtensions"/>.
/// </remarks>
internal sealed class GltfDom
{
    [JsonPropertyName("asset")] public GltfAsset Asset { get; set; } = new();
    [JsonPropertyName("extensionsUsed")] public string[]? ExtensionsUsed { get; set; }
    [JsonPropertyName("extensionsRequired")] public string[]? ExtensionsRequired { get; set; }

    [JsonPropertyName("scene")] public int? DefaultScene { get; set; }
    [JsonPropertyName("scenes")] public GltfScene[]? Scenes { get; set; }
    [JsonPropertyName("nodes")] public GltfNode[]? Nodes { get; set; }

    [JsonPropertyName("buffers")] public GltfBuffer[]? Buffers { get; set; }
    [JsonPropertyName("bufferViews")] public GltfBufferView[]? BufferViews { get; set; }
    [JsonPropertyName("accessors")] public GltfAccessorJson[]? Accessors { get; set; }

    [JsonPropertyName("meshes")] public GltfMesh[]? Meshes { get; set; }
    [JsonPropertyName("materials")] public GltfMaterial[]? Materials { get; set; }
    [JsonPropertyName("textures")] public GltfTexture[]? Textures { get; set; }
    [JsonPropertyName("images")] public GltfImage[]? Images { get; set; }
    [JsonPropertyName("samplers")] public GltfSampler[]? Samplers { get; set; }

    [JsonPropertyName("skins")] public GltfSkin[]? Skins { get; set; }
    [JsonPropertyName("animations")] public GltfAnimation[]? Animations { get; set; }

    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement>? Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

internal sealed class GltfAsset
{
    [JsonPropertyName("version")] public string Version { get; set; } = "2.0";
    [JsonPropertyName("minVersion")] public string? MinVersion { get; set; }
    [JsonPropertyName("generator")] public string? Generator { get; set; }
    [JsonPropertyName("copyright")] public string? Copyright { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement>? Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

internal sealed class GltfScene
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("nodes")] public int[]? Nodes { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement>? Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

internal sealed class GltfNode
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("children")] public int[]? Children { get; set; }
    [JsonPropertyName("matrix")] public float[]? Matrix { get; set; }
    [JsonPropertyName("translation")] public float[]? Translation { get; set; }
    [JsonPropertyName("rotation")] public float[]? Rotation { get; set; }
    [JsonPropertyName("scale")] public float[]? Scale { get; set; }
    [JsonPropertyName("mesh")] public int? Mesh { get; set; }
    [JsonPropertyName("skin")] public int? Skin { get; set; }
    [JsonPropertyName("camera")] public int? Camera { get; set; }
    [JsonPropertyName("weights")] public float[]? Weights { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement>? Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

internal sealed class GltfBuffer
{
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("byteLength")] public int ByteLength { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class GltfBufferView
{
    [JsonPropertyName("buffer")] public int Buffer { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
    [JsonPropertyName("byteLength")] public int ByteLength { get; set; }
    [JsonPropertyName("byteStride")] public int? ByteStride { get; set; }
    [JsonPropertyName("target")] public int? Target { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class GltfAccessorJson
{
    [JsonPropertyName("bufferView")] public int? BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
    [JsonPropertyName("componentType")] public int ComponentType { get; set; }
    [JsonPropertyName("normalized")] public bool Normalized { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "SCALAR";
    [JsonPropertyName("min")] public float[]? Min { get; set; }
    [JsonPropertyName("max")] public float[]? Max { get; set; }
    [JsonPropertyName("sparse")] public GltfAccessorSparse? Sparse { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class GltfAccessorSparse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("indices")] public GltfSparseIndices Indices { get; set; } = new();
    [JsonPropertyName("values")] public GltfSparseValues Values { get; set; } = new();
}

internal sealed class GltfSparseIndices
{
    [JsonPropertyName("bufferView")] public int BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
    [JsonPropertyName("componentType")] public int ComponentType { get; set; }
}

internal sealed class GltfSparseValues
{
    [JsonPropertyName("bufferView")] public int BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
}

internal sealed class GltfMesh
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("primitives")] public GltfPrimitive[] Primitives { get; set; } = Array.Empty<GltfPrimitive>();
    [JsonPropertyName("weights")] public float[]? Weights { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

internal sealed class GltfPrimitive
{
    [JsonPropertyName("attributes")] public Dictionary<string, int> Attributes { get; set; } = new();
    [JsonPropertyName("indices")] public int? Indices { get; set; }
    [JsonPropertyName("material")] public int? Material { get; set; }
    [JsonPropertyName("mode")] public int Mode { get; set; } = 4; // TRIANGLES
    [JsonPropertyName("targets")] public Dictionary<string, int>[]? Targets { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement>? Extensions { get; set; }
}

internal sealed class GltfMaterial
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("pbrMetallicRoughness")] public GltfPbrMr? PbrMetallicRoughness { get; set; }
    [JsonPropertyName("normalTexture")] public GltfNormalTextureInfo? NormalTexture { get; set; }
    [JsonPropertyName("occlusionTexture")] public GltfOcclusionTextureInfo? OcclusionTexture { get; set; }
    [JsonPropertyName("emissiveFactor")] public float[]? EmissiveFactor { get; set; }
    [JsonPropertyName("emissiveTexture")] public GltfTextureInfo? EmissiveTexture { get; set; }
    [JsonPropertyName("alphaMode")] public string? AlphaMode { get; set; }
    [JsonPropertyName("alphaCutoff")] public float? AlphaCutoff { get; set; }
    [JsonPropertyName("doubleSided")] public bool DoubleSided { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement>? Extensions { get; set; }
}

internal sealed class GltfPbrMr
{
    [JsonPropertyName("baseColorFactor")] public float[]? BaseColorFactor { get; set; }
    [JsonPropertyName("baseColorTexture")] public GltfTextureInfo? BaseColorTexture { get; set; }
    [JsonPropertyName("metallicFactor")] public float? MetallicFactor { get; set; }
    [JsonPropertyName("roughnessFactor")] public float? RoughnessFactor { get; set; }
    [JsonPropertyName("metallicRoughnessTexture")] public GltfTextureInfo? MetallicRoughnessTexture { get; set; }
}

internal class GltfTextureInfo
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("texCoord")] public int TexCoord { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement>? Extensions { get; set; }
}

internal sealed class GltfNormalTextureInfo : GltfTextureInfo
{
    [JsonPropertyName("scale")] public float? Scale { get; set; }
}

internal sealed class GltfOcclusionTextureInfo : GltfTextureInfo
{
    [JsonPropertyName("strength")] public float? Strength { get; set; }
}

internal sealed class GltfTexture
{
    [JsonPropertyName("source")] public int? Source { get; set; }
    [JsonPropertyName("sampler")] public int? Sampler { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class GltfImage
{
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
    [JsonPropertyName("bufferView")] public int? BufferView { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class GltfSampler
{
    [JsonPropertyName("magFilter")] public int? MagFilter { get; set; }
    [JsonPropertyName("minFilter")] public int? MinFilter { get; set; }
    [JsonPropertyName("wrapS")] public int? WrapS { get; set; }
    [JsonPropertyName("wrapT")] public int? WrapT { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class GltfSkin
{
    [JsonPropertyName("inverseBindMatrices")] public int? InverseBindMatrices { get; set; }
    [JsonPropertyName("skeleton")] public int? Skeleton { get; set; }
    [JsonPropertyName("joints")] public int[] Joints { get; set; } = Array.Empty<int>();
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class GltfAnimation
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("samplers")] public GltfAnimationSampler[] Samplers { get; set; } = Array.Empty<GltfAnimationSampler>();
    [JsonPropertyName("channels")] public GltfAnimationChannel[] Channels { get; set; } = Array.Empty<GltfAnimationChannel>();
}

internal sealed class GltfAnimationSampler
{
    [JsonPropertyName("input")] public int Input { get; set; }
    [JsonPropertyName("output")] public int Output { get; set; }
    [JsonPropertyName("interpolation")] public string? Interpolation { get; set; }
}

internal sealed class GltfAnimationChannel
{
    [JsonPropertyName("sampler")] public int Sampler { get; set; }
    [JsonPropertyName("target")] public GltfAnimationTarget Target { get; set; } = new();
}

internal sealed class GltfAnimationTarget
{
    [JsonPropertyName("node")] public int? Node { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

/// <summary>glTF accessor component types (OpenGL constants).</summary>
internal static class GltfComponentType
{
    public const int Byte = 5120;
    public const int UnsignedByte = 5121;
    public const int Short = 5122;
    public const int UnsignedShort = 5123;
    public const int UnsignedInt = 5125;
    public const int Float = 5126;
}

/// <summary>glTF sampler filter constants (OpenGL).</summary>
internal static class GltfSamplerFilter
{
    public const int Nearest = 9728;
    public const int Linear = 9729;
    public const int NearestMipmapNearest = 9984;
    public const int LinearMipmapNearest = 9985;
    public const int NearestMipmapLinear = 9986;
    public const int LinearMipmapLinear = 9987;
}

/// <summary>glTF sampler wrap constants (OpenGL).</summary>
internal static class GltfSamplerWrap
{
    public const int ClampToEdge = 33071;
    public const int MirroredRepeat = 33648;
    public const int Repeat = 10497;
}

/// <summary>glTF mesh primitive modes.</summary>
internal static class GltfPrimitiveMode
{
    public const int Points = 0;
    public const int Lines = 1;
    public const int LineLoop = 2;
    public const int LineStrip = 3;
    public const int Triangles = 4;
    public const int TriangleStrip = 5;
    public const int TriangleFan = 6;
}
