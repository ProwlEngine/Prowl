using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Splits any mesh whose unique-vertex count exceeds
/// <see cref="ModelImporterSettings.MaxVerticesPerMesh"/> into multiple meshes.
/// </summary>
/// <remarks>
/// Modern engines accept 32-bit index buffers and rarely need this step, but it is provided
/// for engines whose index buffer addressing is restricted to 16 bits.
/// </remarks>
internal sealed class SplitLargeMeshesStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.SplitLargeMeshes;
    public string Name => "SplitLargeMeshes";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        int max = Math.Max(64, context.Settings.MaxVerticesPerMesh);
        int originalCount = scene.Meshes.Count;
        int producedSplits = 0;

        for (int mi = 0; mi < originalCount; mi++)
        {
            var src = scene.Meshes[mi];
            if (src.Positions.Count <= max) continue;
            // Skinned + morph meshes are harder to split without breaking weight/morph mapping;
            // skip them and warn.
            if (src.VertexJoints is not null || src.BlendShapes.Count > 0)
            {
                context.Log.Warning(
                    $"Mesh '{src.Name}': skipping SplitLargeMeshes because the mesh is skinned or has blend shapes.",
                    Name);
                continue;
            }

            var splits = SplitOne(src, max);
            if (splits.Count <= 1) continue;

            scene.Meshes[mi] = splits[0];
            for (int s = 1; s < splits.Count; s++)
            {
                scene.Meshes.Add(splits[s]);
                producedSplits++;
            }
        }

        if (producedSplits > 0)
            context.Log.Info($"Split oversized meshes; created {producedSplits} extra mesh(es).", Name);
    }

    private static List<IntermediateMesh> SplitOne(IntermediateMesh src, int maxVertices)
    {
        var result = new List<IntermediateMesh>();
        var currentFaces = new List<IntermediateFace>();
        var seen = new Dictionary<int, int>(); // old index -> new index in current chunk

        void Flush()
        {
            if (currentFaces.Count == 0) return;
            result.Add(BuildChunk(src, seen, currentFaces));
            currentFaces = new List<IntermediateFace>();
            seen = new Dictionary<int, int>();
        }

        foreach (var face in src.Faces)
        {
            int wouldAdd = 0;
            for (int k = 0; k < face.Indices.Length; k++)
                if (!seen.ContainsKey(face.Indices[k])) wouldAdd++;

            if (seen.Count + wouldAdd > maxVertices && currentFaces.Count > 0)
                Flush();

            int[] remapped = new int[face.Indices.Length];
            for (int k = 0; k < face.Indices.Length; k++)
            {
                int old = face.Indices[k];
                if (!seen.TryGetValue(old, out int nu))
                {
                    nu = seen.Count;
                    seen[old] = nu;
                }
                remapped[k] = nu;
            }
            currentFaces.Add(new IntermediateFace(remapped));
        }
        Flush();
        return result;
    }

    private static IntermediateMesh BuildChunk(IntermediateMesh src, Dictionary<int, int> oldToNew, List<IntermediateFace> faces)
    {
        var dst = new IntermediateMesh
        {
            Name = src.Name,
            MaterialIndex = src.MaterialIndex,
            MaxInfluencesPerVertex = src.MaxInfluencesPerVertex,
            PrimitiveKinds = src.PrimitiveKinds,
        };

        var ordered = oldToNew.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray();
        for (int i = 0; i < ordered.Length; i++)
            dst.Positions.Add(src.Positions[ordered[i]]);

        if (src.Normals is { } srcN)
        {
            dst.Normals = new List<Float3>(ordered.Length);
            for (int i = 0; i < ordered.Length; i++) dst.Normals.Add(srcN[ordered[i]]);
        }
        if (src.Tangents is { } srcT)
        {
            dst.Tangents = new List<Float4>(ordered.Length);
            for (int i = 0; i < ordered.Length; i++) dst.Tangents.Add(srcT[ordered[i]]);
        }
        if (src.Colors0 is { } srcC)
        {
            dst.Colors0 = new List<Color>(ordered.Length);
            for (int i = 0; i < ordered.Length; i++) dst.Colors0.Add(srcC[ordered[i]]);
        }
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
        {
            if (src.UVs[uv] is not { } srcU) continue;
            var list = new List<Float2>(ordered.Length);
            for (int i = 0; i < ordered.Length; i++) list.Add(srcU[ordered[i]]);
            dst.UVs[uv] = list;
        }

        dst.Faces.AddRange(faces);
        return dst;
    }
}
