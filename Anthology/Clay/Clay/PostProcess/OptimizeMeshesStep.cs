using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Merges sibling meshes that share a material and have compatible vertex layouts. Source nodes
/// for the merged-away meshes lose their <see cref="IntermediateNode.MeshIndex"/> reference; the
/// surviving node keeps the merged mesh.
/// </summary>
/// <remarks>
/// Per-node local transforms are baked into the merged-in vertices, so the result lives in the
/// surviving node's local space. Skinned meshes, meshes with morph targets, and meshes with
/// different primitive kinds are never merged.
/// </remarks>
internal sealed class OptimizeMeshesStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.OptimizeMeshes;
    public string Name => "OptimizeMeshes";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        int mergedCount = 0;
        foreach (var parent in scene.Nodes)
            mergedCount += MergeChildren(parent, scene);

        if (mergedCount > 0)
            context.Log.Info($"Merged {mergedCount} mesh(es) into siblings.", Name);
    }

    private static int MergeChildren(IntermediateNode parent, IntermediateScene scene)
    {
        int merged = 0;
        var byMaterial = new Dictionary<int, IntermediateNode>();

        for (int i = 0; i < parent.Children.Count; i++)
        {
            var child = parent.Children[i];
            if (!IsMergeCandidate(child, scene)) continue;

            var mesh = scene.Meshes[child.MeshIndex];
            if (!byMaterial.TryGetValue(mesh.MaterialIndex, out var primaryNode))
            {
                byMaterial[mesh.MaterialIndex] = child;
                continue;
            }

            var primary = scene.Meshes[primaryNode.MeshIndex];
            if (!AreLayoutCompatible(primary, mesh)) continue;

            AppendMeshIntoPrimary(primary, primaryNode, mesh, child);
            child.MeshIndex = -1;
            merged++;
        }
        return merged;
    }

    private static bool IsMergeCandidate(IntermediateNode node, IntermediateScene scene)
    {
        if (node.MeshIndex < 0) return false;
        if (node.SkinIndex >= 0) return false;
        if ((uint)node.MeshIndex >= (uint)scene.Meshes.Count) return false;
        var m = scene.Meshes[node.MeshIndex];
        if (m.BlendShapes.Count > 0) return false;
        if (m.VertexJoints is not null) return false;
        return true;
    }

    private static bool AreLayoutCompatible(IntermediateMesh a, IntermediateMesh b)
    {
        if (a.PrimitiveKinds != b.PrimitiveKinds) return false;
        if ((a.Normals is null) != (b.Normals is null)) return false;
        if ((a.Tangents is null) != (b.Tangents is null)) return false;
        if ((a.Colors0 is null) != (b.Colors0 is null)) return false;
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
            if ((a.UVs[uv] is null) != (b.UVs[uv] is null))
                return false;
        return true;
    }

    private static void AppendMeshIntoPrimary(
        IntermediateMesh primary, IntermediateNode primaryNode,
        IntermediateMesh source, IntermediateNode sourceNode)
    {
        // The two meshes live in different node-local spaces. Bake (source-local-to-primary-local)
        // into the appended vertices.
        Float4x4 primaryLocal = SceneBakerHelpers.ComposeTRS(primaryNode.LocalPosition, primaryNode.LocalRotation, primaryNode.LocalScale);
        Float4x4 sourceLocal = SceneBakerHelpers.ComposeTRS(sourceNode.LocalPosition, sourceNode.LocalRotation, sourceNode.LocalScale);
        Float4x4 sourceToPrimary = SceneBakerHelpers.Mul(Inverse(primaryLocal), sourceLocal);

        int vertexOffset = primary.Positions.Count;

        for (int i = 0; i < source.Positions.Count; i++)
            primary.Positions.Add(TransformPoint(sourceToPrimary, source.Positions[i]));

        if (primary.Normals is not null && source.Normals is not null)
        {
            for (int i = 0; i < source.Normals.Count; i++)
                primary.Normals.Add(TransformDirection(sourceToPrimary, source.Normals[i]));
        }

        if (primary.Tangents is not null && source.Tangents is not null)
        {
            for (int i = 0; i < source.Tangents.Count; i++)
            {
                var t = source.Tangents[i];
                var xformed = TransformDirection(sourceToPrimary, new Float3(t.X, t.Y, t.Z));
                primary.Tangents.Add(new Float4(xformed.X, xformed.Y, xformed.Z, t.W));
            }
        }

        if (primary.Colors0 is not null && source.Colors0 is not null)
            primary.Colors0.AddRange(source.Colors0);

        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
        {
            if (primary.UVs[uv] is { } pu && source.UVs[uv] is { } su)
                pu.AddRange(su);
        }

        foreach (var face in source.Faces)
        {
            int[] shifted = new int[face.Indices.Length];
            for (int k = 0; k < face.Indices.Length; k++)
                shifted[k] = face.Indices[k] + vertexOffset;
            primary.Faces.Add(new IntermediateFace(shifted));
        }
    }

    private static Float3 TransformPoint(Float4x4 m, Float3 p)
    {
        var v4 = SceneBakerHelpers.MulColumn(m, new Float4(p.X, p.Y, p.Z, 1f));
        return new Float3(v4.X, v4.Y, v4.Z);
    }

    private static Float3 TransformDirection(Float4x4 m, Float3 d)
    {
        // Ignore translation for direction vectors.
        var v4 = SceneBakerHelpers.MulColumn(m, new Float4(d.X, d.Y, d.Z, 0f));
        var result = new Float3(v4.X, v4.Y, v4.Z);
        float len = MathF.Sqrt(result.X * result.X + result.Y * result.Y + result.Z * result.Z);
        return len < 1e-12f ? result : new Float3(result.X / len, result.Y / len, result.Z / len);
    }

    private static Float4x4 Inverse(Float4x4 m)
    {
        // General 4x4 inverse via cofactor expansion. Good enough for occasional use during merge.
        float a00 = m.c0.X, a01 = m.c1.X, a02 = m.c2.X, a03 = m.c3.X;
        float a10 = m.c0.Y, a11 = m.c1.Y, a12 = m.c2.Y, a13 = m.c3.Y;
        float a20 = m.c0.Z, a21 = m.c1.Z, a22 = m.c2.Z, a23 = m.c3.Z;
        float a30 = m.c0.W, a31 = m.c1.W, a32 = m.c2.W, a33 = m.c3.W;

        float b00 = a00 * a11 - a01 * a10;
        float b01 = a00 * a12 - a02 * a10;
        float b02 = a00 * a13 - a03 * a10;
        float b03 = a01 * a12 - a02 * a11;
        float b04 = a01 * a13 - a03 * a11;
        float b05 = a02 * a13 - a03 * a12;
        float b06 = a20 * a31 - a21 * a30;
        float b07 = a20 * a32 - a22 * a30;
        float b08 = a20 * a33 - a23 * a30;
        float b09 = a21 * a32 - a22 * a31;
        float b10 = a21 * a33 - a23 * a31;
        float b11 = a22 * a33 - a23 * a32;

        float det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
        if (MathF.Abs(det) < 1e-12f) return Float4x4.Identity;

        float invDet = 1f / det;
        return new Float4x4(
            new Float4(
                (a11 * b11 - a12 * b10 + a13 * b09) * invDet,
                (-a10 * b11 + a12 * b08 - a13 * b07) * invDet,
                (a10 * b10 - a11 * b08 + a13 * b06) * invDet,
                (-a10 * b09 + a11 * b07 - a12 * b06) * invDet),
            new Float4(
                (-a01 * b11 + a02 * b10 - a03 * b09) * invDet,
                (a00 * b11 - a02 * b08 + a03 * b07) * invDet,
                (-a00 * b10 + a01 * b08 - a03 * b06) * invDet,
                (a00 * b09 - a01 * b07 + a02 * b06) * invDet),
            new Float4(
                (a31 * b05 - a32 * b04 + a33 * b03) * invDet,
                (-a30 * b05 + a32 * b02 - a33 * b01) * invDet,
                (a30 * b04 - a31 * b02 + a33 * b00) * invDet,
                (-a30 * b03 + a31 * b01 - a32 * b00) * invDet),
            new Float4(
                (-a21 * b05 + a22 * b04 - a23 * b03) * invDet,
                (a20 * b05 - a22 * b02 + a23 * b01) * invDet,
                (-a20 * b04 + a21 * b02 - a23 * b00) * invDet,
                (a20 * b03 - a21 * b01 + a22 * b00) * invDet));
    }
}
