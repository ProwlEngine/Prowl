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
    }
}
