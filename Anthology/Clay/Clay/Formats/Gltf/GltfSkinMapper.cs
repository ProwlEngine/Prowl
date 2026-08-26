// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Maps glTF skins into <see cref="IntermediateSkin"/>s.
/// </summary>
/// <remarks>
/// Bone-node references are kept as <see cref="IntermediateNode"/> pointers until bake; the
/// inverseBindMatrices accessor is materialized eagerly. When the source omitted the matrices,
/// glTF says they default to identity per joint.
/// </remarks>
internal static class GltfSkinMapper
{
    public static void MapAll(
        GltfDom dom,
        IntermediateNode[] nodes,
        GltfAccessorReader reader,
        IntermediateScene scene,
        ImportContext ctx)
    {
        if (dom.Skins is null)
            return;

        for (int s = 0; s < dom.Skins.Length; s++)
        {
            var src = dom.Skins[s];
            var skin = new IntermediateSkin
            {
                Name = src.Name,
            };

            Float4x4[]? ibms = null;
            if (src.InverseBindMatrices is { } ibmAcc)
            {
                ibms = reader.ReadMat4(ibmAcc);
                if (ibms.Length != src.Joints.Length)
                {
                    ctx.Log.Warning(
                        $"Skin {s}: inverseBindMatrices has {ibms.Length} entries, joints has {src.Joints.Length}. Filling missing with identity.",
                        "GltfSkinMapper");
                }
            }

            for (int j = 0; j < src.Joints.Length; j++)
            {
                int jointNodeIdx = src.Joints[j];
                if ((uint)jointNodeIdx >= (uint)nodes.Length)
                {
                    ctx.Log.Warning(
                        $"Skin {s} references missing joint node {jointNodeIdx}; substituting identity.",
                        "GltfSkinMapper");
                    skin.BoneNodes.Add(new IntermediateNode { Name = $"<missing_joint_{jointNodeIdx}>" });
                    skin.InverseBindPoses.Add(Float4x4.Identity);
                    continue;
                }
                skin.BoneNodes.Add(nodes[jointNodeIdx]);
                skin.InverseBindPoses.Add(ibms is not null && j < ibms.Length ? ibms[j] : Float4x4.Identity);
            }

            if (src.Skeleton is { } skeletonIdx && (uint)skeletonIdx < (uint)nodes.Length)
                skin.RootNode = nodes[skeletonIdx];

            scene.Skins.Add(skin);
        }

        AttachOrphanJoints(scene, ctx);
    }

    /// <summary>
    /// Brings any joint that is not part of the scene graph into it, under the root.
    /// </summary>
    /// <remarks>
    /// A joint index the file got wrong, and a joint belonging to a scene other than the one being
    /// imported, both leave a bone node that never reaches <c>scene.Nodes</c>. Its BakeIndex stays
    /// -1, that -1 lands in <see cref="Skin.BoneNodeIndices"/>, and the consumer indexes its node
    /// array with it and throws. Attaching the node instead is what "substituting identity" was
    /// supposed to mean: the bone exists, it just sits at the root.
    /// </remarks>
    private static void AttachOrphanJoints(IntermediateScene scene, ImportContext ctx)
    {
        var inScene = new HashSet<IntermediateNode>(scene.Nodes);

        foreach (var skin in scene.Skins)
        {
            foreach (var bone in skin.BoneNodes)
                Attach(bone);
            if (skin.RootNode is { } root)
                Attach(root);
        }

        void Attach(IntermediateNode node)
        {
            if (inScene.Contains(node)) return;

            // Attach the whole detached branch rather than the joint alone, so the joint keeps the
            // transforms its ancestors gave it.
            var top = node;
            while (top.Parent is { } parent && !inScene.Contains(parent))
                top = parent;

            if (top.Parent is null)
            {
                top.Parent = scene.Root;
                scene.Root.Children.Add(top);
            }

            var subtree = new List<IntermediateNode>();
            NodeGraph.Flatten(top, subtree);
            foreach (var n in subtree)
                if (inScene.Add(n))
                    scene.Nodes.Add(n);

            ctx.Log.Warning(
                $"Skin joint '{node.Name}' was not part of the scene graph; attached it under the root.",
                "GltfSkinMapper");
        }
    }
}
