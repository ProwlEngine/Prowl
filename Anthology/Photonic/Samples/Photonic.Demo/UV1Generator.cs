using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Unwrapper;

namespace Photonic.Demo;

/// <summary>
/// Bakes a <see cref="LoadedModel"/> + <see cref="UV1Strategy"/> into a
/// <see cref="SceneModel"/>'s Baked* arrays. After this runs the model has a UV1 layer in
/// <c>[0,1]^2</c> that can be passed to <see cref="Prowl.Photonic.AutoAtlasPacker"/>.
/// </summary>
internal static class UV1Generator
{
    public static void Bake(SceneModel sm, System.Action<string>? progress = null)
    {
        var src = sm.Source;
        switch (sm.UV1Mode)
        {
            case UV1Strategy.AutoUnwrap:
            {
                progress?.Invoke($"Unwrapping {sm.Name} via Prowl.Unwrapper...");
                var doublePos = new Double3[src.Positions.Length];
                for (int i = 0; i < src.Positions.Length; i++)
                    doublePos[i] = new Double3(src.Positions[i].X, src.Positions[i].Y, src.Positions[i].Z);
                var unwrap = UnwrapMesh.Unwrap(doublePos, src.Indices, new UnwrapOptions
                {
                    PackMargin = 2.0 / 512.0,
                });
                SplitPerCornerUVs(sm, unwrap.PerCornerUVs);
                progress?.Invoke($"Unwrap done: {sm.BakedPositions.Length} verts after seam splits.");
                break;
            }
            case UV1Strategy.TrianglePack:
            {
                progress?.Invoke($"Per-triangle packing UV1 for {sm.Name}...");
                TrianglePackUV1(sm);
                break;
            }
            case UV1Strategy.UseExisting:
            default:
            {
                progress?.Invoke($"Using model's best-existing UV layer for {sm.Name} ({(src.HasDedicatedUV ? "dedicated layer" : "fallback to UV0")})...");
                CopyExistingUV1(sm);
                break;
            }
        }
    }

    /// <summary>Pass-through: keep vertex layout, just copy <see cref="LoadedModel.BestExistingUV"/> into UV1.</summary>
    private static void CopyExistingUV1(SceneModel sm)
    {
        var src = sm.Source;
        sm.BakedPositions = src.Positions;
        sm.BakedNormals   = src.Normals;
        sm.BakedUV0       = src.UV0;
        sm.BakedUV1       = src.BestExistingUV;
        sm.BakedIndices   = src.Indices;
        sm.BakedSubMeshes = src.SubMeshes;
    }

    /// <summary>
    /// Per-corner unwrap can disagree at chart boundaries; this duplicates vertices wherever
    /// triangles meet with different UV1 values and rewires indices, preserving submesh order.
    /// </summary>
    private static void SplitPerCornerUVs(SceneModel sm, Double2[] cornerUVs)
    {
        var src = sm.Source;
        int triCount = src.Indices.Length / 3;

        var newPositions = new List<Float3>(src.Positions.Length);
        var newNormals   = new List<Float3>(src.Positions.Length);
        var newUV0       = new List<Float2>(src.Positions.Length);
        var newUV1       = new List<Float2>(src.Positions.Length);
        var newIndices   = new int[src.Indices.Length];

        var dedup = new Dictionary<(int v, int qu, int qv), int>(src.Positions.Length);
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
                newPositions.Add(src.Positions[origIndex]);
                newNormals.Add(src.Normals[origIndex]);
                newUV0.Add(src.UV0[origIndex]);
                newUV1.Add(new Float2((float)uv.X, (float)uv.Y));
                dedup.Add(key, ni);
            }
            newIndices[t * 3 + c] = ni;
        }

        sm.BakedPositions = newPositions.ToArray();
        sm.BakedNormals   = newNormals.ToArray();
        sm.BakedUV0       = newUV0.ToArray();
        sm.BakedUV1       = newUV1.ToArray();
        sm.BakedIndices   = newIndices;
        sm.BakedSubMeshes = src.SubMeshes;
    }

    /// <summary>
    /// Per-triangle shelf packer: lay every triangle out as a same-orientation pair onto a grid
    /// inside <c>[0,1]^2</c>. Splits every triangle to have a unique vertex set so per-triangle
    /// UV1s can disagree freely. Wraps the existing <see cref="TrianglePacker"/> implementation
    /// already shipped with the demo.
    /// </summary>
    private static void TrianglePackUV1(SceneModel sm)
    {
        var src = sm.Source;
        int triCount = src.Indices.Length / 3;

        // Build a fresh combined that TrianglePacker can consume.
        var packerInput = new CombinedSponza
        {
            Vertices = src.Positions,
            Normals = src.Normals,
            UV0 = src.UV0,
            UV1 = System.Array.Empty<Float2>(),
            Indices = src.Indices,
            SubMeshes = new CombinedSponza.SubMeshSlice[src.SubMeshes.Length],
            Materials = System.Array.Empty<CombinedSponza.MaterialInfo>(),
            Textures = System.Array.Empty<CombinedSponza.TextureBlob?>(),
        };
        for (int i = 0; i < src.SubMeshes.Length; i++)
            packerInput.SubMeshes[i] = new CombinedSponza.SubMeshSlice
            {
                IndexStart = src.SubMeshes[i].IndexStart,
                IndexCount = src.SubMeshes[i].IndexCount,
                MaterialIndex = src.SubMeshes[i].MaterialIndex,
            };

        var packed = TrianglePacker.Repack(packerInput, atlasWidth: 512, atlasHeight: 512, progress: null);

        sm.BakedPositions = packed.Vertices;
        sm.BakedNormals   = packed.Normals;
        sm.BakedUV0       = packed.UV0;
        sm.BakedUV1       = packed.UV1;
        sm.BakedIndices   = packed.Indices;
        sm.BakedSubMeshes = new LoadedModel.SubMeshSlice[packed.SubMeshes.Length];
        for (int i = 0; i < packed.SubMeshes.Length; i++)
            sm.BakedSubMeshes[i] = new LoadedModel.SubMeshSlice
            {
                IndexStart = packed.SubMeshes[i].IndexStart,
                IndexCount = packed.SubMeshes[i].IndexCount,
                MaterialIndex = packed.SubMeshes[i].MaterialIndex,
            };
    }
}
