// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
        Float4x4 primaryLocal = Float4x4.CreateTRS(primaryNode.LocalPosition, primaryNode.LocalRotation, primaryNode.LocalScale);
        Float4x4 sourceLocal = Float4x4.CreateTRS(sourceNode.LocalPosition, sourceNode.LocalRotation, sourceNode.LocalScale);
        Float4x4 sourceToPrimary = Inverse(primaryLocal) * sourceLocal;
        // Normals need the inverse-transpose so they stay perpendicular under non-uniform scale/shear.
        Float4x4 normalMatrix = Float4x4.Transpose(Inverse(sourceToPrimary));

        int vertexOffset = primary.Positions.Count;

        for (int i = 0; i < source.Positions.Count; i++)
            primary.Positions.Add(TransformPoint(sourceToPrimary, source.Positions[i]));

        if (primary.Normals is not null && source.Normals is not null)
        {
            for (int i = 0; i < source.Normals.Count; i++)
                primary.Normals.Add(TransformDirection(normalMatrix, source.Normals[i]));
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
        var v4 = m * new Float4(p.X, p.Y, p.Z, 1f);
        return new Float3(v4.X, v4.Y, v4.Z);
    }

    private static Float3 TransformDirection(Float4x4 m, Float3 d)
    {
        // Ignore translation for direction vectors.
        var v4 = m * new Float4(d.X, d.Y, d.Z, 0f);
        var result = new Float3(v4.X, v4.Y, v4.Z);
        float len = MathF.Sqrt(result.X * result.X + result.Y * result.Y + result.Z * result.Z);
        return len < 1e-12f ? result : new Float3(result.X / len, result.Y / len, result.Z / len);
    }

    // Delegates to Prowl.Vector, keeping Clay's Identity fallback for a singular matrix (Prowl.Vector.Invert
    // yields a NaN matrix there).
    private static Float4x4 Inverse(Float4x4 m) => Float4x4.Invert(m, out Float4x4 inv) ? inv : Float4x4.Identity;
}
