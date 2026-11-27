using System.Collections.Generic;
using Prowl.Clay;
using Prowl.Clay.Importer;
using Prowl.Vector;
using StbImageSharp;

namespace Photonic.Demo;

/// <summary>
/// Loads any glTF / GLB / OBJ / FBX via Prowl.Clay into a flat <see cref="LoadedModel"/>: a single
/// concatenated vertex stream + submeshes + decoded diffuse textures. Node transforms are baked
/// in here so callers can apply a single per-instance transform on top.
/// </summary>
internal static class ModelLoader
{
    public static LoadedModel Load(string path, System.Action<string>? progress = null)
    {
        progress?.Invoke($"Importing {System.IO.Path.GetFileName(path)} via Prowl.Clay...");
        var model = ModelImporter.Load(path);
        progress?.Invoke($"Imported: {model.Meshes.Count} meshes, {model.Materials.Count} materials, {model.Textures.Count} textures, {model.Nodes.Count} nodes.");

        // Decode the diffuse textures actually used by materials.
        var wantedTex = new HashSet<int>();
        foreach (var m in model.Materials)
            if (m.BaseColorTexture is not null) wantedTex.Add(m.BaseColorTexture.TextureIndex);

        var blobs = new LoadedModel.TextureBlob?[model.Textures.Count];
        int decoded = 0;
        foreach (int ti in wantedTex)
        {
            try
            {
                blobs[ti] = DecodeTexture(model.Textures[ti]);
                decoded++;
            }
            catch (System.Exception ex)
            {
                progress?.Invoke($"  Skipped texture {ti}: {ex.Message}");
            }
        }
        progress?.Invoke($"Decoded {decoded}/{wantedTex.Count} diffuse textures.");

        var matInfos = new LoadedModel.MaterialInfo[model.Materials.Count];
        for (int i = 0; i < model.Materials.Count; i++)
        {
            var src = model.Materials[i];
            matInfos[i] = new LoadedModel.MaterialInfo
            {
                Name = string.IsNullOrEmpty(src.Name) ? $"Material_{i}" : src.Name,
                BaseColor = new Float3(src.BaseColor.R, src.BaseColor.G, src.BaseColor.B),
                DiffuseTextureIndex = src.BaseColorTexture?.TextureIndex ?? -1,
            };
        }

        // Concatenate every node's mesh into one vertex stream + indices, applying node world
        // transforms. Track which UV layer to use as "best existing" -- prefer UV1+ over UV0
        // since UV0 is typically tiled-material space.
        var positions = new List<Float3>(1 << 18);
        var normals   = new List<Float3>(1 << 18);
        var uv0       = new List<Float2>(1 << 18);
        var uv1       = new List<Float2>(1 << 18);
        var uv2       = new List<Float2>(1 << 18);
        var indices   = new List<int>(1 << 18);
        var slices    = new List<LoadedModel.SubMeshSlice>();

        // Track whether higher-numbered UV layers actually had data anywhere (a model with a UV1
        // slot full of zeros shouldn't count as "has UV1").
        bool anyUV1 = false;
        bool anyUV2 = false;

        foreach (var node in model.Nodes)
        {
            if (node.MeshIndex < 0) continue;
            var mesh = model.Meshes[node.MeshIndex];
            var w = node.WorldMatrix;
            int baseV = positions.Count;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                positions.Add(TransformPoint(w, mesh.Vertices[i]));
                normals.Add(mesh.Normals is null ? new Float3(0, 1, 0) : Float3.Normalize(TransformDir(w, mesh.Normals[i])));
                uv0.Add(mesh.UVs.Length > 0 && mesh.UVs[0] is { } a ? a[i] : Float2.Zero);
                if (mesh.UVs.Length > 1 && mesh.UVs[1] is { } b) { uv1.Add(b[i]); anyUV1 = true; } else uv1.Add(Float2.Zero);
                if (mesh.UVs.Length > 2 && mesh.UVs[2] is { } c) { uv2.Add(c[i]); anyUV2 = true; } else uv2.Add(Float2.Zero);
            }
            foreach (var sub in mesh.SubMeshes)
            {
                int idxStart = indices.Count;
                for (int k = 0; k < sub.IndexCount; k++)
                {
                    uint v = mesh.Indices[sub.IndexStart + k];
                    indices.Add(baseV + (int)v);
                }
                slices.Add(new LoadedModel.SubMeshSlice
                {
                    IndexStart = idxStart,
                    IndexCount = sub.IndexCount,
                    MaterialIndex = sub.MaterialIndex,
                });
            }
        }

        progress?.Invoke($"Concatenated -> {positions.Count} verts, {indices.Count / 3} tris, {slices.Count} submeshes.");

        // Pick the best-existing UV layer for the UseExisting strategy.
        Float2[] best;
        bool dedicated;
        if (anyUV2) { best = uv2.ToArray(); dedicated = true; }
        else if (anyUV1) { best = uv1.ToArray(); dedicated = true; }
        else { best = uv0.ToArray(); dedicated = false; }

        return new LoadedModel
        {
            SourcePath = path,
            DisplayName = System.IO.Path.GetFileNameWithoutExtension(path),
            Positions = positions.ToArray(),
            Normals = normals.ToArray(),
            UV0 = uv0.ToArray(),
            Indices = indices.ToArray(),
            SubMeshes = slices.ToArray(),
            Materials = matInfos,
            Textures = blobs,
            BestExistingUV = best,
            HasDedicatedUV = dedicated,
        };
    }

    private static LoadedModel.TextureBlob DecodeTexture(Texture tex)
    {
        StbImage.stbi_set_flip_vertically_on_load(0); // glTF expects (0,0) at top-left
        ImageResult? img = null;
        if (tex.EncodedBytes is not null)
            img = ImageResult.FromMemory(tex.EncodedBytes, ColorComponents.RedGreenBlueAlpha);
        else if (tex.SourcePath is not null && System.IO.File.Exists(tex.SourcePath))
        {
            using var fs = System.IO.File.OpenRead(tex.SourcePath);
            img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
        }
        if (img is null) throw new System.IO.FileNotFoundException($"Texture has no decodable source: {tex.SourcePath}");
        return new LoadedModel.TextureBlob
        {
            Name = tex.Name ?? System.IO.Path.GetFileName(tex.SourcePath) ?? "(unnamed)",
            Width = img.Width,
            Height = img.Height,
            RGBA = img.Data,
        };
    }

    private static Float3 TransformPoint(Float4x4 m, Float3 v) => Transform(m, v, 1f);
    private static Float3 TransformDir(Float4x4 m, Float3 v)   => Transform(m, v, 0f);

    private static Float3 Transform(Float4x4 m, Float3 v, float w)
    {
        float x = m.c0.X * v.X + m.c1.X * v.Y + m.c2.X * v.Z + m.c3.X * w;
        float y = m.c0.Y * v.X + m.c1.Y * v.Y + m.c2.Y * v.Z + m.c3.Y * w;
        float z = m.c0.Z * v.X + m.c1.Z * v.Y + m.c2.Z * v.Z + m.c3.Z * w;
        return new Float3(x, y, z);
    }
}
