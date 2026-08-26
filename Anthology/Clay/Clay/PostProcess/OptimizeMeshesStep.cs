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
        var mergedAway = new HashSet<int>();
        foreach (var parent in scene.Nodes)
            MergeChildren(parent, scene, mergedAway);

        if (mergedAway.Count == 0) return;

        RemoveOrphanedMeshes(scene, mergedAway);
        context.Log.Info($"Merged {mergedAway.Count} mesh(es) into siblings.", Name);
    }

    /// <summary>
    /// Drops the merged-away meshes and renumbers what is left.
    /// </summary>
    /// <remarks>
    /// Clearing the node's MeshIndex is not enough on its own: the mesh stays in the scene, reaches
    /// <see cref="Model.Meshes"/>, and the editor registers every entry there as a sub-asset, so the
    /// project gains dead mesh assets that nothing references.
    /// </remarks>
    private static void RemoveOrphanedMeshes(IntermediateScene scene, HashSet<int> mergedAway)
    {
        // A merged-away mesh can still be referenced by another node, since one mesh may be
        // instanced under several. Those are not orphans and have to stay.
        foreach (var node in scene.Nodes)
            mergedAway.Remove(node.MeshIndex);

        if (mergedAway.Count == 0) return;

        var remap = new int[scene.Meshes.Count];
        var kept = new List<IntermediateMesh>(scene.Meshes.Count - mergedAway.Count);
        for (int i = 0; i < scene.Meshes.Count; i++)
        {
            if (mergedAway.Contains(i))
            {
                remap[i] = -1;
                continue;
            }
            remap[i] = kept.Count;
            kept.Add(scene.Meshes[i]);
        }

        scene.Meshes.Clear();
        scene.Meshes.AddRange(kept);

        foreach (var node in scene.Nodes)
            if (node.MeshIndex >= 0)
                node.MeshIndex = remap[node.MeshIndex];
    }

    private static void MergeChildren(IntermediateNode parent, IntermediateScene scene, HashSet<int> mergedAway)
    {
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

            // Two nodes instancing one mesh have nothing to merge, and appending a mesh into itself
            // walks a list that grows with every append.
            if (primaryNode.MeshIndex == child.MeshIndex) continue;

            var primary = scene.Meshes[primaryNode.MeshIndex];
            if (!AreLayoutCompatible(primary, mesh)) continue;

            AppendMeshIntoPrimary(primary, primaryNode, mesh, child);
            mergedAway.Add(child.MeshIndex);
            child.MeshIndex = -1;
        }
    }

    private static bool IsMergeCandidate(IntermediateNode node, IntermediateScene scene)
    {
        if (node.MeshIndex < 0) return false;
        if (node.SkinIndex >= 0) return false;
        if ((uint)node.MeshIndex >= (uint)scene.Meshes.Count) return false;
        var m = scene.Meshes[node.MeshIndex];
        if (m.BlendShapes.Count > 0) return false;
        if (m.VertexJoints is not null) return false;
        // Grouping below keys on the mesh's single material, which says nothing about a mesh whose
        // faces carry their own. Merging those would need the key to be the whole material set.
        if (m.HasMixedMaterials()) return false;
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

        // A negative determinant means the transform mirrors. Handedness is baked into two places the
        // vertex streams cannot express on their own, so both have to be flipped with it: the
        // bitangent sign in tangent.W, and the triangle winding.
        bool mirrored = Float4x4.Determinant(sourceToPrimary) < 0f;

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
                primary.Tangents.Add(new Float4(xformed.X, xformed.Y, xformed.Z, mirrored ? -t.W : t.W));
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
            {
                int src = mirrored ? face.Indices.Length - 1 - k : k;
                shifted[k] = face.Indices[src] + vertexOffset;
            }
            // Resolved rather than copied: the source may have been inheriting its own mesh material,
            // which would silently become the primary's after the move.
            primary.Faces.Add(new IntermediateFace(shifted, source.MaterialForFace(face), face.SmoothingGroup));
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
