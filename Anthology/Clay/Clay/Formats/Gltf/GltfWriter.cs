// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Prowl.Vector;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Writes an <see cref="ExportModel"/> as binary glTF. The model's left handed space goes back to glTF's right handed
/// one by negating X, which also turns winding, rotations and inverse bind matrices over, the reverse of what the
/// importer does.
/// </summary>
public static class GltfWriter
{
    private const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;
    private const int ArrayBuffer = 34962, ElementArrayBuffer = 34963;

    private static readonly JsonSerializerOptions s_json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static void WriteGlb(ExportModel model, Stream stream)
    {
        var writer = new Writer();
        GltfDom dom = writer.Build(model);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(dom, s_json);
        byte[] bin = writer.Binary.ToArray();

        int jsonLength = Pad(json.Length), binLength = Pad(bin.Length);
        int total = 12 + 8 + jsonLength + (bin.Length > 0 ? 8 + binLength : 0);
        using var output = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        output.Write(Magic);
        output.Write(2u);
        output.Write((uint)total);
        output.Write((uint)jsonLength);
        output.Write(JsonChunk);
        output.Write(json);
        for (int i = json.Length; i < jsonLength; i++) output.Write((byte)0x20);
        if (bin.Length == 0) return;
        output.Write((uint)binLength);
        output.Write(BinChunk);
        output.Write(bin);
        for (int i = bin.Length; i < binLength; i++) output.Write((byte)0);
    }

    private static int Pad(int length) => (length + 3) & ~3;

    private static Float3 Flip(Float3 v) => new(-v.X, v.Y, v.Z);

    private sealed class Writer
    {
        public readonly MemoryStream Binary = new();
        private readonly List<GltfBufferView> _views = [];
        private readonly List<GltfAccessorJson> _accessors = [];

        public GltfDom Build(ExportModel model)
        {
            var dom = new GltfDom { Asset = new GltfAsset { Generator = "Prowl Clay" } };
            var extensions = new SortedSet<string>();

            dom.Nodes = [.. model.Nodes.Select(Node)];
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                int[] children = [.. Enumerable.Range(0, model.Nodes.Count).Where(c => model.Nodes[c].Parent == i)];
                if (children.Length > 0) dom.Nodes[i].Children = children;
            }
            dom.Scenes = [new GltfScene { Name = model.Name, Nodes = [.. Enumerable.Range(0, model.Nodes.Count).Where(i => model.Nodes[i].Parent < 0)] }];
            dom.DefaultScene = 0;

            if (model.Meshes.Count > 0) dom.Meshes = [.. model.Meshes.Select(Mesh)];
            if (model.Materials.Count > 0) dom.Materials = [.. model.Materials.Select(m => Material(m, extensions))];
            if (model.Textures.Count > 0)
            {
                dom.Images = [.. model.Textures.Select(t => new GltfImage { Name = t.Name, MimeType = "image/png", BufferView = View(t.Png, null) })];
                dom.Samplers = [new GltfSampler { MagFilter = 9729, MinFilter = 9987, WrapS = 10497, WrapT = 10497 }];
                dom.Textures = [.. model.Textures.Select((t, i) => new GltfTexture { Name = t.Name, Source = i, Sampler = 0 })];
            }
            if (model.Skins.Count > 0) dom.Skins = [.. model.Skins.Select(Skin)];

            if (Binary.Length > 0) dom.Buffers = [new GltfBuffer { ByteLength = (int)Binary.Length }];
            if (_views.Count > 0) dom.BufferViews = [.. _views];
            if (_accessors.Count > 0) dom.Accessors = [.. _accessors];
            if (extensions.Count > 0) dom.ExtensionsUsed = [.. extensions];
            return dom;
        }

        private static GltfNode Node(ExportNode node)
        {
            Float3 position = Flip(node.Position);
            Quaternion rotation = node.Rotation;
            var gltf = new GltfNode { Name = node.Name.Length > 0 ? node.Name : null };
            if (position != Float3.Zero) gltf.Translation = [position.X, position.Y, position.Z];
            if (rotation != Quaternion.Identity) gltf.Rotation = [rotation.X, -rotation.Y, -rotation.Z, rotation.W];
            if (node.Scale != Float3.One) gltf.Scale = [node.Scale.X, node.Scale.Y, node.Scale.Z];
            if (node.Mesh >= 0) gltf.Mesh = node.Mesh;
            if (node.Skin >= 0) gltf.Skin = node.Skin;
            return gltf;
        }

        private GltfMesh Mesh(ExportMesh mesh)
        {
            var attributes = new Dictionary<string, int>
            {
                ["POSITION"] = Vec3(mesh.Positions.Select(Flip).ToArray(), bounds: true),
            };
            if (mesh.Normals is { } normals) attributes["NORMAL"] = Vec3([.. normals.Select(Flip)], bounds: false);
            if (mesh.Tangents is { } tangents) attributes["TANGENT"] = Floats([.. tangents.SelectMany(t => new[] { -t.X, t.Y, t.Z, t.W })], tangents.Length, "VEC4");
            if (mesh.UV0 is { } uv0) attributes["TEXCOORD_0"] = Floats([.. uv0.SelectMany(u => new[] { u.X, u.Y })], uv0.Length, "VEC2");
            if (mesh.UV1 is { } uv1) attributes["TEXCOORD_1"] = Floats([.. uv1.SelectMany(u => new[] { u.X, u.Y })], uv1.Length, "VEC2");
            if (mesh.Colors is { } colors) attributes["COLOR_0"] = Floats([.. colors.SelectMany(c => new[] { c.X, c.Y, c.Z, c.W })], colors.Length, "VEC4");
            if (mesh.Joints is { } joints && mesh.Weights is { } weights)
            {
                byte[] shorts = new byte[joints.Length * 8];
                for (int i = 0; i < joints.Length; i++)
                    for (int k = 0; k < 4; k++) BitConverter.TryWriteBytes(shorts.AsSpan(i * 8 + k * 2), (ushort)Math.Max(0, joints[i][k]));
                attributes["JOINTS_0"] = Accessor(View(shorts, ArrayBuffer), GltfComponentType.UnsignedShort, joints.Length, "VEC4");
                attributes["WEIGHTS_0"] = Floats([.. weights.SelectMany(Normalized)], weights.Length, "VEC4");
            }

            Dictionary<string, int>[]? targets = null;
            if (mesh.MorphTargets.Count > 0)
                targets = [.. mesh.MorphTargets.Select(t =>
                {
                    var target = new Dictionary<string, int> { ["POSITION"] = Vec3([.. t.DeltaPositions.Select(Flip)], bounds: true) };
                    if (t.DeltaNormals is { } dn) target["NORMAL"] = Vec3([.. dn.Select(Flip)], bounds: false);
                    if (t.DeltaTangents is { } dt) target["TANGENT"] = Vec3([.. dt.Select(Flip)], bounds: false);
                    return target;
                })];

            var gltf = new GltfMesh
            {
                Name = mesh.Name.Length > 0 ? mesh.Name : null,
                Primitives = [.. mesh.Primitives.Select(p => new GltfPrimitive
                {
                    Attributes = attributes,
                    Indices = Indices(p.Indices),
                    Material = p.Material >= 0 ? p.Material : null,
                    Targets = targets,
                })],
            };
            if (mesh.MorphTargets.Count > 0)
            {
                gltf.Weights = [.. mesh.MorphTargets.Select(t => t.Weight)];
                gltf.Extras = JsonSerializer.SerializeToElement(new Dictionary<string, string[]> { ["targetNames"] = [.. mesh.MorphTargets.Select(t => t.Name)] });
            }
            return gltf;
        }

        private static float[] Normalized(Float4 w)
        {
            float sum = w.X + w.Y + w.Z + w.W;
            return sum > 0f ? [w.X / sum, w.Y / sum, w.Z / sum, w.W / sum] : [1f, 0f, 0f, 0f];
        }

        // Triangles wound the other way, since negating X mirrors them
        private int Indices(uint[] indices)
        {
            byte[] flipped = new byte[indices.Length * 4];
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                BitConverter.TryWriteBytes(flipped.AsSpan(i * 4), indices[i]);
                BitConverter.TryWriteBytes(flipped.AsSpan((i + 1) * 4), indices[i + 2]);
                BitConverter.TryWriteBytes(flipped.AsSpan((i + 2) * 4), indices[i + 1]);
            }
            return Accessor(View(flipped, ElementArrayBuffer), GltfComponentType.UnsignedInt, indices.Length, "SCALAR");
        }

        private static GltfMaterial Material(ExportMaterial material, SortedSet<string> extensions)
        {
            Dictionary<string, JsonElement>? Transform()
            {
                if (material.TextureOffset == Float2.Zero && material.TextureScale == Float2.One) return null;
                extensions.Add("KHR_texture_transform");
                return new Dictionary<string, JsonElement>
                {
                    ["KHR_texture_transform"] = JsonSerializer.SerializeToElement(new Dictionary<string, float[]>
                    {
                        ["offset"] = [material.TextureOffset.X, material.TextureOffset.Y],
                        ["scale"] = [material.TextureScale.X, material.TextureScale.Y],
                    }),
                };
            }
            GltfTextureInfo? Info(int texture) => texture >= 0 ? new GltfTextureInfo { Index = texture, Extensions = Transform() } : null;

            var gltf = new GltfMaterial
            {
                Name = material.Name.Length > 0 ? material.Name : null,
                PbrMetallicRoughness = new GltfPbrMr
                {
                    BaseColorFactor = [material.BaseColor.X, material.BaseColor.Y, material.BaseColor.Z, material.BaseColor.W],
                    MetallicFactor = material.Metallic,
                    RoughnessFactor = material.Roughness,
                    BaseColorTexture = Info(material.BaseColorTexture),
                    MetallicRoughnessTexture = Info(material.MetallicRoughnessTexture),
                },
                NormalTexture = material.NormalTexture >= 0
                    ? new GltfNormalTextureInfo { Index = material.NormalTexture, Scale = material.NormalScale, Extensions = Transform() }
                    : null,
                EmissiveTexture = Info(material.EmissionTexture),
                AlphaMode = material.AlphaMode switch
                {
                    MaterialAlphaMode.Mask => "MASK",
                    MaterialAlphaMode.Blend => "BLEND",
                    _ => null,
                },
                AlphaCutoff = material.AlphaMode == MaterialAlphaMode.Mask ? material.AlphaCutoff : null,
                DoubleSided = material.DoubleSided,
            };
            if (material.Emission != Float3.Zero || material.EmissionTexture >= 0)
            {
                gltf.EmissiveFactor = [material.Emission.X, material.Emission.Y, material.Emission.Z];
                if (material.EmissionStrength != 1f)
                {
                    extensions.Add("KHR_materials_emissive_strength");
                    gltf.Extensions = new Dictionary<string, JsonElement>
                    {
                        ["KHR_materials_emissive_strength"] = JsonSerializer.SerializeToElement(new Dictionary<string, float> { ["emissiveStrength"] = material.EmissionStrength }),
                    };
                }
            }
            return gltf;
        }

        private GltfSkin Skin(ExportSkin skin)
        {
            var matrices = new List<float>(skin.InverseBindMatrices.Count * 16);
            foreach (Float4x4 m in skin.InverseBindMatrices)
            {
                // Conjugating by the X flip negates the entries where exactly one of row and column is X
                Float4x4 f = m;
                f.c0 = new Float4(f.c0.X, -f.c0.Y, -f.c0.Z, -f.c0.W);
                f.c1 = new Float4(-f.c1.X, f.c1.Y, f.c1.Z, f.c1.W);
                f.c2 = new Float4(-f.c2.X, f.c2.Y, f.c2.Z, f.c2.W);
                f.c3 = new Float4(-f.c3.X, f.c3.Y, f.c3.Z, f.c3.W);
                matrices.AddRange([f.c0.X, f.c0.Y, f.c0.Z, f.c0.W, f.c1.X, f.c1.Y, f.c1.Z, f.c1.W, f.c2.X, f.c2.Y, f.c2.Z, f.c2.W, f.c3.X, f.c3.Y, f.c3.Z, f.c3.W]);
            }
            return new GltfSkin
            {
                Name = skin.Name.Length > 0 ? skin.Name : null,
                Joints = [.. skin.Joints],
                InverseBindMatrices = Floats([.. matrices], skin.InverseBindMatrices.Count, "MAT4", target: null),
            };
        }

        private int Vec3(Float3[] values, bool bounds)
        {
            int accessor = Floats([.. values.SelectMany(v => new[] { v.X, v.Y, v.Z })], values.Length, "VEC3");
            if (bounds && values.Length > 0)
            {
                Float3 min = values[0], max = values[0];
                foreach (Float3 v in values)
                {
                    min = new Float3(MathF.Min(min.X, v.X), MathF.Min(min.Y, v.Y), MathF.Min(min.Z, v.Z));
                    max = new Float3(MathF.Max(max.X, v.X), MathF.Max(max.Y, v.Y), MathF.Max(max.Z, v.Z));
                }
                _accessors[accessor].Min = [min.X, min.Y, min.Z];
                _accessors[accessor].Max = [max.X, max.Y, max.Z];
            }
            return accessor;
        }

        private int Floats(float[] values, int count, string type, int? target = ArrayBuffer)
        {
            byte[] bytes = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return Accessor(View(bytes, target), GltfComponentType.Float, count, type);
        }

        private int View(byte[] bytes, int? target)
        {
            while (Binary.Length % 4 != 0) Binary.WriteByte(0);
            _views.Add(new GltfBufferView { Buffer = 0, ByteOffset = (int)Binary.Length, ByteLength = bytes.Length, Target = target });
            Binary.Write(bytes);
            return _views.Count - 1;
        }

        private int Accessor(int view, int componentType, int count, string type)
        {
            _accessors.Add(new GltfAccessorJson { BufferView = view, ComponentType = componentType, Count = count, Type = type });
            return _accessors.Count - 1;
        }
    }
}
