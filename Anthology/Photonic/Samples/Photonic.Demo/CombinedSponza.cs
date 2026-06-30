using Prowl.Clay;
using Prowl.Clay.Importer;
using Prowl.Vector;
using Prowl.Unwrapper;
using StbImageSharp;

namespace Photonic.Demo;

/// <summary>
/// Loads a Clay model and concatenates it into one big vertex stream while keeping enough
/// per-submesh / per-material info to drive both the GL renderer (textured) and the Photonic
/// path tracer (sampling the same textures during bounce evaluation).
/// </summary>
internal sealed class CombinedSponza
{
    public Float3[] Vertices = System.Array.Empty<Float3>();
    public Float3[] Normals = System.Array.Empty<Float3>();
    public Float2[] UV0     = System.Array.Empty<Float2>();
    public Float2[] UV1     = System.Array.Empty<Float2>();
    public int[]    Indices = System.Array.Empty<int>();

    /// <summary>A contiguous index range within <see cref="Indices"/> that uses one material.</summary>
    public struct SubMeshSlice
    {
        public int IndexStart;
        public int IndexCount;
        public int MaterialIndex;
    }

    public SubMeshSlice[] SubMeshes = System.Array.Empty<SubMeshSlice>();

    public sealed class MaterialInfo
    {
        public required string Name;
        public Float3 BaseColor;
        public int DiffuseTextureIndex = -1; // index into Textures; -1 if no diffuse texture
    }

    /// <summary>Decoded textures referenced by materials. Indexed by <see cref="MaterialInfo.DiffuseTextureIndex"/>.</summary>
    public sealed class TextureBlob
    {
        public required string Name;
        public required int Width;
        public required int Height;
        public required byte[] RGBA;   // 4 bytes per pixel, sRGB encoded
    }

    public MaterialInfo[] Materials = System.Array.Empty<MaterialInfo>();
    public TextureBlob?[] Textures  = System.Array.Empty<TextureBlob?>();

    public enum UV1Mode
    {
        /// <summary>Run Prowl.Unwrapper. Best quality, slowest.</summary>
        AutoUnwrap,
        /// <summary>Per-triangle shelf pack. Every triangle gets a unique atlas region, no chart overlap. Use for diagnostics or when artifacts plague the auto-unwrap.</summary>
        PerTrianglePack,
        /// <summary>Copy the model's UV0 as UV1. Only useful if the model already has proper non-overlapping UVs.</summary>
        UseUV0,
    }

    public static CombinedSponza Load(string path, UV1Mode uv1Mode, int atlasWidth, int atlasHeight, System.Action<string>? progress)
    {
        progress?.Invoke("Importing model via Prowl.Clay...");
        var model = ModelImporter.Load(path);
        progress?.Invoke($"Imported: {model.Meshes.Count} meshes, {model.Materials.Count} materials, {model.Textures.Count} textures, {model.Nodes.Count} nodes.");

        // ---- decode textures (only those actually used as BaseColorTexture) ---------------------
        var diffuseTextureIndices = new System.Collections.Generic.HashSet<int>();
        foreach (var mat in model.Materials)
            if (mat.BaseColorTexture is not null) diffuseTextureIndices.Add(mat.BaseColorTexture.TextureIndex);

        var blobs = new TextureBlob?[model.Textures.Count];
        int decoded = 0;
        foreach (int ti in diffuseTextureIndices)
        {
            var tex = model.Textures[ti];
            try
            {
                blobs[ti] = DecodeTexture(tex);
                decoded++;
                progress?.Invoke($"Decoded texture {decoded}/{diffuseTextureIndices.Count}: {tex.Name ?? tex.SourcePath ?? "(unnamed)"}");
            }
            catch (System.Exception ex)
            {
                progress?.Invoke($"  Skipped texture {ti} ({tex.SourcePath}): {ex.Message}");
            }
        }

        // ---- build material table ---------------------------------------------------------------
        var matInfos = new MaterialInfo[model.Materials.Count];
        for (int i = 0; i < model.Materials.Count; i++)
        {
            var m = model.Materials[i];
            matInfos[i] = new MaterialInfo
            {
                Name = string.IsNullOrEmpty(m.Name) ? $"Material_{i}" : m.Name,
                BaseColor = new Float3(m.BaseColor.R, m.BaseColor.G, m.BaseColor.B),
                DiffuseTextureIndex = m.BaseColorTexture?.TextureIndex ?? -1,
            };
        }

        // ---- walk nodes and concatenate geometry ------------------------------------------------
        var positions = new System.Collections.Generic.List<Float3>(1 << 18);
        var normals   = new System.Collections.Generic.List<Float3>(1 << 18);
        var uv0       = new System.Collections.Generic.List<Float2>(1 << 18);
        var indices   = new System.Collections.Generic.List<int>(1 << 18);
        var slices    = new System.Collections.Generic.List<SubMeshSlice>();

        foreach (var node in model.Nodes)
        {
            if (node.MeshIndex < 0) continue;
            var mesh = model.Meshes[node.MeshIndex];
            var w = node.WorldMatrix;
            int baseV = positions.Count;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                positions.Add(Transform(w, mesh.Vertices[i], 1f));
                normals.Add(mesh.Normals is null ? new Float3(0, 1, 0) : Float3.Normalize(Transform(w, mesh.Normals[i], 0f)));
                uv0.Add(mesh.UVs[0] is null ? Float2.Zero : mesh.UVs[0]![i]);
            }
            foreach (var sub in mesh.SubMeshes)
            {
                int indexStart = indices.Count;
                for (int k = 0; k < sub.IndexCount; k++)
                {
                    uint v = mesh.Indices[sub.IndexStart + k];
                    indices.Add(baseV + (int)v);
                }
                slices.Add(new SubMeshSlice
                {
                    IndexStart = indexStart,
                    IndexCount = sub.IndexCount,
                    MaterialIndex = sub.MaterialIndex,
                });
            }
        }
        progress?.Invoke($"Concatenated -> {positions.Count} verts, {indices.Count / 3} tris, {slices.Count} submeshes.");

        var result = new CombinedSponza
        {
            Vertices = positions.ToArray(),
            Normals = normals.ToArray(),
            UV0 = uv0.ToArray(),
            Indices = indices.ToArray(),
            SubMeshes = slices.ToArray(),
            Materials = matInfos,
            Textures = blobs,
        };

        switch (uv1Mode)
        {
            case UV1Mode.AutoUnwrap:
            {
                progress?.Invoke("Unwrapping UV1 via Prowl.Unwrapper (this can take 30s+ on Sponza)...");
                var doublePos = new Double3[result.Vertices.Length];
                for (int i = 0; i < result.Vertices.Length; i++)
                    doublePos[i] = new Double3(result.Vertices[i].X, result.Vertices[i].Y, result.Vertices[i].Z);
                var unwrap = UnwrapMesh.Unwrap(doublePos, result.Indices, new UnwrapOptions
                {
                    PackMargin = 2.0 / 512.0,
                });
                result = SplitPerCornerUVs(result, unwrap.PerCornerUVs);
                progress?.Invoke($"Unwrap done: {result.Vertices.Length} verts after seam splits.");
                break;
            }
            case UV1Mode.PerTrianglePack:
            {
                result = TrianglePacker.Repack(result, atlasWidth, atlasHeight, progress);
                break;
            }
            case UV1Mode.UseUV0:
            default:
            {
                result.UV1 = (Float2[])result.UV0.Clone();
                break;
            }
        }

        return result;
    }

    /// <summary>Decode a Clay texture (external file or embedded bytes) into RGBA8.</summary>
    private static TextureBlob DecodeTexture(Texture tex)
    {
        StbImage.stbi_set_flip_vertically_on_load(0); // glTF expects (0,0) at top-left
        ImageResult? img = null;
        if (tex.EncodedBytes is not null)
        {
            img = ImageResult.FromMemory(tex.EncodedBytes, ColorComponents.RedGreenBlueAlpha);
        }
        else if (tex.SourcePath is not null && System.IO.File.Exists(tex.SourcePath))
        {
            using var fs = System.IO.File.OpenRead(tex.SourcePath);
            img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
        }
        if (img is null) throw new System.IO.FileNotFoundException($"Texture has no decodable source: {tex.SourcePath}");
        return new TextureBlob
        {
            Name = tex.Name ?? System.IO.Path.GetFileName(tex.SourcePath) ?? "(unnamed)",
            Width = img.Width,
            Height = img.Height,
            RGBA = img.Data,
        };
    }

    /// <summary>
    /// Per-corner UVs disagree at chart boundaries; this duplicates vertices with conflicting UV1s
    /// and rewires <see cref="Indices"/> while preserving submesh boundaries.
    /// </summary>
    private static CombinedSponza SplitPerCornerUVs(CombinedSponza src, Double2[] cornerUVs)
    {
        int triCount = src.Indices.Length / 3;
        var newPositions = new System.Collections.Generic.List<Float3>(src.Vertices.Length);
        var newNormals   = new System.Collections.Generic.List<Float3>(src.Vertices.Length);
        var newUV0       = new System.Collections.Generic.List<Float2>(src.Vertices.Length);
        var newUV1       = new System.Collections.Generic.List<Float2>(src.Vertices.Length);
        var newIndices   = new int[src.Indices.Length];

        var dedup = new System.Collections.Generic.Dictionary<(int v, int qu, int qv), int>(src.Vertices.Length);
        const float quant = 1f / 32768f;

        for (int t = 0; t < triCount; t++)
        for (int c = 0; c < 3; c++)
        {
            int origIndex = src.Indices[t * 3 + c];
            var uv = cornerUVs[t * 3 + c];
            int qu = (int)System.Math.Round(uv.X / quant);
            int qv = (int)System.Math.Round(uv.Y / quant);
            var key = (origIndex, qu, qv);
            if (!dedup.TryGetValue(key, out int ni))
            {
                ni = newPositions.Count;
                newPositions.Add(src.Vertices[origIndex]);
                newNormals.Add(src.Normals[origIndex]);
                newUV0.Add(src.UV0[origIndex]);
                newUV1.Add(new Float2((float)uv.X, (float)uv.Y));
                dedup.Add(key, ni);
            }
            newIndices[t * 3 + c] = ni;
        }

        return new CombinedSponza
        {
            Vertices = newPositions.ToArray(),
            Normals = newNormals.ToArray(),
            UV0 = newUV0.ToArray(),
            UV1 = newUV1.ToArray(),
            Indices = newIndices,
            SubMeshes = src.SubMeshes,
            Materials = src.Materials,
            Textures = src.Textures,
        };
    }

    private static Float3 Transform(Float4x4 m, Float3 v, float w)
    {
        float x = m.c0.X * v.X + m.c1.X * v.Y + m.c2.X * v.Z + m.c3.X * w;
        float y = m.c0.Y * v.X + m.c1.Y * v.Y + m.c2.Y * v.Z + m.c3.Y * w;
        float z = m.c0.Z * v.X + m.c1.Z * v.Y + m.c2.Z * v.Z + m.c3.Z * w;
        return new Float3(x, y, z);
    }
}
