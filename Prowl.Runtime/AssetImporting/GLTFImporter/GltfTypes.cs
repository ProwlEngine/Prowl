using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Prowl.Runtime.AssetImporting.Gltf;

public class GltfRoot
{
    [JsonPropertyName("asset")] public GltfAsset Asset { get; set; } = new();
    [JsonPropertyName("scene")] public int? Scene { get; set; }
    [JsonPropertyName("scenes")] public List<GltfScene> Scenes { get; set; } = new();
    [JsonPropertyName("nodes")] public List<GltfNode> Nodes { get; set; } = new();
    [JsonPropertyName("meshes")] public List<GltfMesh> Meshes { get; set; } = new();
    [JsonPropertyName("materials")] public List<GltfMaterial> Materials { get; set; } = new();
    [JsonPropertyName("accessors")] public List<GltfAccessor> Accessors { get; set; } = new();
    [JsonPropertyName("bufferViews")] public List<GltfBufferView> BufferViews { get; set; } = new();
    [JsonPropertyName("buffers")] public List<GltfBuffer> Buffers { get; set; } = new();
    [JsonPropertyName("images")] public List<GltfImage> Images { get; set; } = new();
    [JsonPropertyName("textures")] public List<GltfTexture> Textures { get; set; } = new();
    [JsonPropertyName("samplers")] public List<GltfSampler> Samplers { get; set; } = new();
    [JsonPropertyName("skins")] public List<GltfSkin> Skins { get; set; } = new();
    [JsonPropertyName("animations")] public List<GltfAnimation> Animations { get; set; } = new();
}

public class GltfAsset
{
    [JsonPropertyName("version")] public string Version { get; set; } = "2.0";
    [JsonPropertyName("generator")] public string? Generator { get; set; }
    [JsonPropertyName("minVersion")] public string? MinVersion { get; set; }
}

public class GltfScene
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("nodes")] public List<int> Nodes { get; set; } = new();
}

public class GltfNode
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("children")] public List<int> Children { get; set; } = new();
    [JsonPropertyName("mesh")] public int? Mesh { get; set; }
    [JsonPropertyName("skin")] public int? Skin { get; set; }
    [JsonPropertyName("camera")] public int? Camera { get; set; }
    [JsonPropertyName("matrix")] public float[]? Matrix { get; set; }
    [JsonPropertyName("translation")] public float[]? Translation { get; set; }
    [JsonPropertyName("rotation")] public float[]? Rotation { get; set; }
    [JsonPropertyName("scale")] public float[]? Scale { get; set; }
    [JsonPropertyName("weights")] public float[]? Weights { get; set; }
}

public class GltfMesh
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("primitives")] public List<GltfPrimitive> Primitives { get; set; } = new();
    [JsonPropertyName("weights")] public float[]? Weights { get; set; }
}

public class GltfPrimitive
{
    [JsonPropertyName("attributes")] public Dictionary<string, int> Attributes { get; set; } = new();
    [JsonPropertyName("indices")] public int? Indices { get; set; }
    [JsonPropertyName("material")] public int? Material { get; set; }
    [JsonPropertyName("mode")] public int? Mode { get; set; } = 4;
    [JsonPropertyName("targets")] public List<Dictionary<string, int>>? Targets { get; set; }
}

public class GltfMaterial
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("pbrMetallicRoughness")] public GltfPbrMetallicRoughness? PbrMetallicRoughness { get; set; }
    [JsonPropertyName("normalTexture")] public GltfNormalTextureInfo? NormalTexture { get; set; }
    [JsonPropertyName("occlusionTexture")] public GltfOcclusionTextureInfo? OcclusionTexture { get; set; }
    [JsonPropertyName("emissiveTexture")] public GltfTextureInfo? EmissiveTexture { get; set; }
    [JsonPropertyName("emissiveFactor")] public float[]? EmissiveFactor { get; set; }
    [JsonPropertyName("alphaMode")] public string? AlphaMode { get; set; }
    [JsonPropertyName("alphaCutoff")] public float? AlphaCutoff { get; set; }
    [JsonPropertyName("doubleSided")] public bool? DoubleSided { get; set; }
}

public class GltfPbrMetallicRoughness
{
    [JsonPropertyName("baseColorFactor")] public float[]? BaseColorFactor { get; set; }
    [JsonPropertyName("baseColorTexture")] public GltfTextureInfo? BaseColorTexture { get; set; }
    [JsonPropertyName("metallicFactor")] public float? MetallicFactor { get; set; }
    [JsonPropertyName("roughnessFactor")] public float? RoughnessFactor { get; set; }
    [JsonPropertyName("metallicRoughnessTexture")] public GltfTextureInfo? MetallicRoughnessTexture { get; set; }
}

public class GltfTextureInfo
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("texCoord")] public int? TexCoord { get; set; } = 0;
}

public class GltfNormalTextureInfo : GltfTextureInfo
{
    [JsonPropertyName("scale")] public float? Scale { get; set; }
}

public class GltfOcclusionTextureInfo : GltfTextureInfo
{
    [JsonPropertyName("strength")] public float? Strength { get; set; }
}

public class GltfTexture
{
    [JsonPropertyName("source")] public int? Source { get; set; }
    [JsonPropertyName("sampler")] public int? Sampler { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class GltfImage
{
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("bufferView")] public int? BufferView { get; set; }
    [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class GltfSampler
{
    [JsonPropertyName("magFilter")] public int? MagFilter { get; set; }
    [JsonPropertyName("minFilter")] public int? MinFilter { get; set; }
    [JsonPropertyName("wrapS")] public int? WrapS { get; set; }
    [JsonPropertyName("wrapT")] public int? WrapT { get; set; }
}

public class GltfAccessor
{
    [JsonPropertyName("bufferView")] public int? BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; } = 0;
    [JsonPropertyName("componentType")] public int ComponentType { get; set; }
    [JsonPropertyName("normalized")] public bool? Normalized { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("min")] public float[]? Min { get; set; }
    [JsonPropertyName("max")] public float[]? Max { get; set; }
    [JsonPropertyName("sparse")] public GltfSparse? Sparse { get; set; }
}

public class GltfSparse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("indices")] public GltfSparseIndices Indices { get; set; } = new();
    [JsonPropertyName("values")] public GltfSparseValues Values { get; set; } = new();
}

public class GltfSparseIndices
{
    [JsonPropertyName("bufferView")] public int BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; } = 0;
    [JsonPropertyName("componentType")] public int ComponentType { get; set; }
}

public class GltfSparseValues
{
    [JsonPropertyName("bufferView")] public int BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; } = 0;
}

public class GltfBufferView
{
    [JsonPropertyName("buffer")] public int Buffer { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; } = 0;
    [JsonPropertyName("byteLength")] public int ByteLength { get; set; }
    [JsonPropertyName("byteStride")] public int? ByteStride { get; set; }
    [JsonPropertyName("target")] public int? Target { get; set; }
}

public class GltfBuffer
{
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("byteLength")] public int ByteLength { get; set; }
}

public class GltfSkin
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("inverseBindMatrices")] public int? InverseBindMatrices { get; set; }
    [JsonPropertyName("skeleton")] public int? Skeleton { get; set; }
    [JsonPropertyName("joints")] public List<int> Joints { get; set; } = new();
}

public class GltfAnimation
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("channels")] public List<GltfAnimChannel> Channels { get; set; } = new();
    [JsonPropertyName("samplers")] public List<GltfAnimSampler> Samplers { get; set; } = new();
}

public class GltfAnimChannel
{
    [JsonPropertyName("sampler")] public int Sampler { get; set; }
    [JsonPropertyName("target")] public GltfAnimTarget Target { get; set; } = new();
}

public class GltfAnimTarget
{
    [JsonPropertyName("node")] public int? Node { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

public class GltfAnimSampler
{
    [JsonPropertyName("input")] public int Input { get; set; }
    [JsonPropertyName("output")] public int Output { get; set; }
    [JsonPropertyName("interpolation")] public string? Interpolation { get; set; } = "LINEAR";
}
