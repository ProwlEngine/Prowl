using System;
using System.Collections.Generic;
using System.IO;

using ImageMagick;

using Prowl.Clay.Importer;
using Prowl.Graphite;
using Prowl.Graphite.Samples;
using Prowl.Vector;
using Prowl.Unwrapper;

namespace Prowl.Graphite.Samples.PBRRenderer;

public struct TextureBlob
{
    public int Width;
    public int Height;
    public byte[] RGBA;
}

public struct MaterialInfo
{
    public Float4 BaseColor;
    public float Metallic;
    public float Roughness;
    public Float3 EmissiveFactor;
    public Texture? AlbedoTexture;
    public Texture? NormalTexture;
    public Texture? MetallicRoughnessTexture;
    public Texture? EmissiveTexture;

    /// <summary>CPU-side RGBA bytes for <see cref="AlbedoTexture"/>, kept for lightmap baking. Null when there is no albedo texture.</summary>
    public TextureBlob? AlbedoBlob;
}

public struct SubMeshRange
{
    public int IndexStart;
    public int IndexCount;
    public int MaterialIndex;
}

public sealed class ModelAsset : IDisposable
{
    public Mesh Mesh { get; private set; } = null!;

    public SubMeshRange[] SubMeshes { get; private set; } = Array.Empty<SubMeshRange>();
    public MaterialInfo[] Materials { get; private set; } = Array.Empty<MaterialInfo>();

    public Prowl.Clay.Bounds Bounds { get; private set; }

    public Float3[] Positions { get; private set; } = Array.Empty<Float3>();
    public Float3[] Normals { get; private set; } = Array.Empty<Float3>();
    public Float4[] Tangents { get; private set; } = Array.Empty<Float4>();
    public Float2[] UV0 { get; private set; } = Array.Empty<Float2>();
    public Float2[] UV1 { get; private set; } = Array.Empty<Float2>();
    public uint[] Indices { get; private set; } = Array.Empty<uint>();

    private readonly List<Texture> _ownedTextures = new();
    private Texture? _defaultWhite;
    private Texture? _defaultNormal;
    private Texture? _defaultBlack;

    private GraphicsDevice _device = null!;

    private ModelAsset() { }

    public static ModelAsset Load(GraphicsDevice device, string path, bool unwrapLightmapUVs = true)
    {
        var model = ModelImporter.Load(path);

        var asset = new ModelAsset { _device = device };

        var positions = new List<Float3>(1 << 16);
        var normals = new List<Float3>(1 << 16);
        var tangents = new List<Float4>(1 << 16);
        var uv0 = new List<Float2>(1 << 16);
        var indices = new List<uint>(1 << 16);
        var ranges = new List<SubMeshRange>();

        var bounds = Prowl.Clay.Bounds.Empty;

        foreach (var node in model.Nodes)
        {
            if (node.MeshIndex < 0)
                continue;

            var mesh = model.Meshes[node.MeshIndex];
            var w = node.WorldMatrix;
            uint baseV = (uint)positions.Count;

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Float3 pos = TransformPoint(w, mesh.Vertices[i]);
                positions.Add(pos);
                bounds.Encapsulate(pos);

                Float3 nrm = mesh.Normals is null
                    ? new Float3(0, 1, 0)
                    : Float3.Normalize(TransformDir(w, mesh.Normals[i]));
                normals.Add(nrm);

                if (mesh.Tangents is not null)
                {
                    Float4 t = mesh.Tangents[i];
                    Float3 tdir = Float3.Normalize(TransformDir(w, new Float3(t.X, t.Y, t.Z)));
                    tangents.Add(new Float4(tdir.X, tdir.Y, tdir.Z, t.W));
                }
                else
                {
                    tangents.Add(new Float4(1, 0, 0, 1));
                }

                uv0.Add(mesh.UVs.Length > 0 && mesh.UVs[0] is { } a ? a[i] : Float2.Zero);
            }

            foreach (var sub in mesh.SubMeshes)
            {
                int indexStart = indices.Count;
                for (int k = 0; k < sub.IndexCount; k++)
                {
                    uint v = mesh.Indices[sub.IndexStart + k];
                    indices.Add(baseV + v);
                }

                ranges.Add(new SubMeshRange
                {
                    IndexStart = indexStart,
                    IndexCount = sub.IndexCount,
                    MaterialIndex = sub.MaterialIndex,
                });
            }
        }

        asset.Bounds = bounds;

        Float3[] posArr = positions.ToArray();
        Float3[] nrmArr = normals.ToArray();
        Float4[] tanArr = tangents.ToArray();
        Float2[] uv0Arr = uv0.ToArray();
        uint[] idxArr = indices.ToArray();
        Float2[] uv1Arr;

        if (unwrapLightmapUVs && posArr.Length > 0 && idxArr.Length > 0)
        {
            var doublePos = new Double3[posArr.Length];
            for (int i = 0; i < posArr.Length; i++)
                doublePos[i] = new Double3(posArr[i].X, posArr[i].Y, posArr[i].Z);

            var triangles = new int[idxArr.Length];
            for (int i = 0; i < idxArr.Length; i++)
                triangles[i] = (int)idxArr[i];

            var unwrap = UnwrapMesh.Unwrap(doublePos, triangles, new UnwrapOptions
            {
                PackMargin = 2.0 / 512.0,
            });

            SplitPerCornerUVs(
                posArr, nrmArr, tanArr, uv0Arr, idxArr, unwrap.PerCornerUVs, ranges,
                out posArr, out nrmArr, out tanArr, out uv0Arr, out uv1Arr, out idxArr, out ranges);
        }
        else
        {
            uv1Arr = uv0Arr;
        }

        asset.Positions = posArr;
        asset.Normals = nrmArr;
        asset.Tangents = tanArr;
        asset.UV0 = uv0Arr;
        asset.UV1 = uv1Arr;
        asset.Indices = idxArr;
        asset.SubMeshes = ranges.ToArray();

        var meshCreateInfo = MeshCreateInfo.Default;
        meshCreateInfo.Topology = PrimitiveTopology.TriangleList;
        var gpuMesh = new Mesh(device, meshCreateInfo) { IsReadable = true };

        gpuMesh.SetVertexInput<Float3>(posArr, 0);
        gpuMesh.SetVertexInput<Float3>(nrmArr, 1);
        gpuMesh.SetVertexInput<Float3>(TangentsAsFloat3(tanArr), 2);
        gpuMesh.SetVertexInput<Float4>(ToFloat4(uv0Arr), 3);
        gpuMesh.SetVertexInput<Float4>(ToFloat4(uv1Arr), 4);

        if (idxArr.Length > 0)
            gpuMesh.SetIndexInput32(idxArr);

        asset.Mesh = gpuMesh;

        asset.Materials = BuildMaterials(device, model, asset);

        return asset;
    }

    private static Float3[] TangentsAsFloat3(Float4[] tangents)
    {
        var result = new Float3[tangents.Length];
        for (int i = 0; i < tangents.Length; i++)
            result[i] = new Float3(tangents[i].X, tangents[i].Y, tangents[i].Z);
        return result;
    }

    private static Float4[] ToFloat4(Float2[] uvs)
    {
        var result = new Float4[uvs.Length];
        for (int i = 0; i < uvs.Length; i++)
            result[i] = new Float4(uvs[i].X, uvs[i].Y, 0f, 0f);
        return result;
    }

    private static void SplitPerCornerUVs(
        Float3[] positions, Float3[] normals, Float4[] tangents, Float2[] uv0, uint[] indices,
        Double2[] cornerUVs, List<SubMeshRange> ranges,
        out Float3[] outPositions, out Float3[] outNormals, out Float4[] outTangents,
        out Float2[] outUV0, out Float2[] outUV1, out uint[] outIndices, out List<SubMeshRange> outRanges)
    {
        int triCount = indices.Length / 3;

        var newPositions = new List<Float3>(positions.Length);
        var newNormals = new List<Float3>(positions.Length);
        var newTangents = new List<Float4>(positions.Length);
        var newUV0 = new List<Float2>(positions.Length);
        var newUV1 = new List<Float2>(positions.Length);
        var newIndices = new uint[indices.Length];

        var dedup = new Dictionary<(uint v, int qu, int qv), uint>(positions.Length);
        const float quant = 1f / 32768f;

        for (int t = 0; t < triCount; t++)
        {
            for (int c = 0; c < 3; c++)
            {
                uint origIndex = indices[t * 3 + c];
                var uv = cornerUVs[t * 3 + c];
                int qu = (int)Math.Round(uv.X / quant);
                int qv = (int)Math.Round(uv.Y / quant);
                var key = (origIndex, qu, qv);
                if (!dedup.TryGetValue(key, out uint ni))
                {
                    ni = (uint)newPositions.Count;
                    newPositions.Add(positions[origIndex]);
                    newNormals.Add(normals[origIndex]);
                    newTangents.Add(tangents[origIndex]);
                    newUV0.Add(uv0[origIndex]);
                    newUV1.Add(new Float2((float)uv.X, (float)uv.Y));
                    dedup.Add(key, ni);
                }
                newIndices[t * 3 + c] = ni;
            }
        }

        outPositions = newPositions.ToArray();
        outNormals = newNormals.ToArray();
        outTangents = newTangents.ToArray();
        outUV0 = newUV0.ToArray();
        outUV1 = newUV1.ToArray();
        outIndices = newIndices;
        outRanges = ranges;
    }

    private static MaterialInfo[] BuildMaterials(GraphicsDevice device, Prowl.Clay.Model model, ModelAsset asset)
    {
        var textureCache = new Dictionary<int, Texture>();
        var blobCache = new Dictionary<int, TextureBlob>();

        Texture GetOrDecode(int textureIndex)
        {
            if (textureCache.TryGetValue(textureIndex, out var existing))
                return existing;

            (Texture decoded, TextureBlob blob) = DecodeTexture(device, model.Textures[textureIndex]);
            textureCache[textureIndex] = decoded;
            blobCache[textureIndex] = blob;
            asset._ownedTextures.Add(decoded);
            return decoded;
        }

        var result = new MaterialInfo[model.Materials.Count];
        for (int i = 0; i < model.Materials.Count; i++)
        {
            var src = model.Materials[i];

            int albedoIndex = src.BaseColorTexture?.TextureIndex ?? -1;
            Texture? albedo = albedoIndex >= 0 ? GetOrDecode(albedoIndex) : null;
            Texture? normal = src.NormalTexture is { } nt ? GetOrDecode(nt.TextureIndex) : null;
            Texture? metallicRoughness = src.MetallicRoughnessTexture is { } mr ? GetOrDecode(mr.TextureIndex) : null;
            Texture? emissive = src.EmissiveTexture is { } et ? GetOrDecode(et.TextureIndex) : null;

            result[i] = new MaterialInfo
            {
                BaseColor = new Float4(src.BaseColor.R, src.BaseColor.G, src.BaseColor.B, src.BaseColor.A),
                Metallic = src.Metallic,
                Roughness = src.Roughness,
                EmissiveFactor = new Float3(src.EmissiveFactor.R, src.EmissiveFactor.G, src.EmissiveFactor.B),
                AlbedoTexture = albedo,
                NormalTexture = normal,
                MetallicRoughnessTexture = metallicRoughness,
                EmissiveTexture = emissive,
                AlbedoBlob = albedoIndex >= 0 ? blobCache[albedoIndex] : null,
            };
        }

        return result;
    }

    private static (Texture, TextureBlob) DecodeTexture(GraphicsDevice device, Prowl.Clay.Texture tex)
    {
        byte[] bytes;
        if (tex.EncodedBytes is not null)
        {
            bytes = tex.EncodedBytes;
        }
        else if (tex.SourcePath is not null && File.Exists(tex.SourcePath))
        {
            bytes = File.ReadAllBytes(tex.SourcePath);
        }
        else
        {
            throw new FileNotFoundException($"Texture has no decodable source: {tex.SourcePath}");
        }

        using var image = new MagickImage(bytes);
        image.Alpha(AlphaOption.Set);
        image.Depth = 8;
        image.Flip();

        using IUnsafePixelCollection<ushort> pixels = image.GetPixelsUnsafe();
        byte[] color = pixels.ToByteArray(PixelMapping.RGBA) ?? throw new Exception("Failed to load pixel data");

        var desc = TextureDescription.Texture2D((uint)image.Width, (uint)image.Height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled);
        Texture texture = device.ResourceFactory.CreateTexture(desc);
        device.UpdateTexture(texture, color, 0, 0, 0, (uint)image.Width, (uint)image.Height, 1, 0, 0);

        var blob = new TextureBlob { Width = (int)image.Width, Height = (int)image.Height, RGBA = color };
        return (texture, blob);
    }

    public Texture GetDefaultWhite()
    {
        _defaultWhite ??= CreateSolidTexture(255, 255, 255, 255);
        return _defaultWhite;
    }

    public Texture GetDefaultNormal()
    {
        _defaultNormal ??= CreateSolidTexture(128, 128, 255, 255);
        return _defaultNormal;
    }

    public Texture GetDefaultBlack()
    {
        _defaultBlack ??= CreateSolidTexture(0, 0, 0, 255);
        return _defaultBlack;
    }

    private Texture CreateSolidTexture(byte r, byte g, byte b, byte a)
    {
        var desc = TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled);
        Texture texture = _device.ResourceFactory.CreateTexture(desc);
        byte[] pixel = { r, g, b, a };
        _device.UpdateTexture(texture, pixel, 0, 0, 0, 1, 1, 1, 0, 0);
        _ownedTextures.Add(texture);
        return texture;
    }

    private static Float3 TransformPoint(Float4x4 m, Float3 v) => Transform(m, v, 1f);
    private static Float3 TransformDir(Float4x4 m, Float3 v) => Transform(m, v, 0f);

    private static Float3 Transform(Float4x4 m, Float3 v, float w)
    {
        float x = m.c0.X * v.X + m.c1.X * v.Y + m.c2.X * v.Z + m.c3.X * w;
        float y = m.c0.Y * v.X + m.c1.Y * v.Y + m.c2.Y * v.Z + m.c3.Y * w;
        float z = m.c0.Z * v.X + m.c1.Z * v.Y + m.c2.Z * v.Z + m.c3.Z * w;
        return new Float3(x, y, z);
    }

    public void Dispose()
    {
        Mesh?.Dispose();
        foreach (var tex in _ownedTextures)
            tex.Dispose();
        _ownedTextures.Clear();
    }
}
