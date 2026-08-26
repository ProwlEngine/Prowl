// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Collapses pass-through nodes (no mesh, no skin, not a joint, not animated, no metadata, uniform
/// scale, has children, not on the preserve list). Each collapsed node's local transform is folded
/// into its children's transforms.
/// </summary>
/// <remarks>
/// Significantly reduces hierarchy depth for content authored in DCC tools that use lots of
/// grouping nodes (Blender empties, Maya transform groups, etc.). Nodes are only ever lifted out of
/// the hierarchy, never deleted: a node with nothing under it contributes no depth to remove, and
/// deleting one throws away a socket or marker that the file exists to carry.
/// </remarks>
internal sealed class OptimizeGraphStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.OptimizeGraph;
    public string Name => "OptimizeGraph";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        var keep = ComputeKeepSet(scene, context.Settings.OptimizeGraphPreserveNodeNames);

        // Walk root-first. For each node whose parent we are about to collapse, fold the parent's
        // local matrix into ours.
        if (!keep.Contains(scene.Root))
        {
            // The root is always implicitly kept by index but we should never collapse it.
            keep.Add(scene.Root);
        }

        int collapsed = CollapseRecursive(scene.Root, keep);

        if (collapsed > 0)
        {
            // Rebuild scene.Nodes from the surviving hierarchy.
            scene.Nodes.Clear();
            NodeGraph.Flatten(scene.Root, scene.Nodes);
            context.Log.Info($"Collapsed {collapsed} pass-through node(s).", Name);
        }
    }

    private static HashSet<IntermediateNode> ComputeKeepSet(IntermediateScene scene, IReadOnlyList<string> preserveNames)
    {
        var keep = new HashSet<IntermediateNode>();

        foreach (var node in scene.Nodes)
        {
            // A camera or light node carries no geometry, so without this it reads as a pass-through
            // and gets folded away along with the only transform that positioned it.
            if (node.MeshIndex >= 0 || node.SkinIndex >= 0 || node.CameraIndex >= 0 || node.LightIndex >= 0)
                keep.Add(node);

            // FBX user properties and glTF extras live here, and folding the node away takes them
            // with it.
            if (node.Metadata.Count > 0)
                keep.Add(node);

            // Folding a non-uniform scale into a rotated child produces shear, which TRS cannot
            // represent and DecomposeMatrix silently drops, deforming the subtree with no warning.
            // Uniform scale commutes with rotation, so those collapse exactly.
            if (!IsUniformScale(node.LocalScale))
                keep.Add(node);
        }

        // Animation targets must be kept; we'd lose the curve mapping otherwise.
        foreach (var anim in scene.Animations)
            foreach (var binding in anim.Bindings)
                if (binding.TargetNode is { } n)
                    keep.Add(n);

        // Skin joints + skeleton roots must be kept.
        foreach (var skin in scene.Skins)
        {
            foreach (var bone in skin.BoneNodes)
                keep.Add(bone);
            if (skin.RootNode is { } sr)
                keep.Add(sr);
        }

        // Named-preserve list.
        if (preserveNames.Count > 0)
        {
            var nameSet = new HashSet<string>(preserveNames, StringComparer.Ordinal);
            foreach (var node in scene.Nodes)
                if (nameSet.Contains(node.Name))
                    keep.Add(node);
        }

        return keep;
    }

    private static bool IsUniformScale(Float3 scale)
    {
        const float Epsilon = 1e-5f;
        return MathF.Abs(scale.X - scale.Y) <= Epsilon && MathF.Abs(scale.Y - scale.Z) <= Epsilon;
    }

    // Recursion depth is one frame per hierarchy level, which the reader already capped at
    // NodeGraph.MaxDepth when it flattened the scene.
    private static int CollapseRecursive(IntermediateNode node, HashSet<IntermediateNode> keep)
    {
        int collapsed = 0;

        // Replace child list iteratively, walking by index because we mutate.
        for (int i = 0; i < node.Children.Count;)
        {
            var child = node.Children[i];

            // First recurse so grand-children stabilize before we look at child.
            collapsed += CollapseRecursive(child, keep);

            // A childless empty is an attachment point or a marker, which is most of why an artist
            // puts an empty in a file at all, and collapsing one saves no depth because it has no
            // subtree to lift. Only pass-through nodes with children are worth removing.
            if (!keep.Contains(child) && child.Children.Count > 0)
            {
                // Fold child's local transform into each grand-child's local transform, then
                // promote grand-children to siblings.
                FoldChildIntoGrandchildren(child);

                node.Children.RemoveAt(i);
                foreach (var gc in child.Children)
                {
                    gc.Parent = node;
                    node.Children.Insert(i, gc);
                    i++;
                }
                collapsed++;
                continue;
            }

            i++;
        }
        return collapsed;
    }

    private static void FoldChildIntoGrandchildren(IntermediateNode child)
    {
        // grandchild_new = child_local * grandchild_old
        Float4x4 m = Float4x4.CreateTRS(child.LocalPosition, child.LocalRotation, child.LocalScale);

        foreach (var gc in child.Children)
        {
            Float4x4 g = Float4x4.CreateTRS(gc.LocalPosition, gc.LocalRotation, gc.LocalScale);
            Float4x4 combined = m * g;
            SceneBakerHelpers.DecomposeMatrix(combined, out var t, out var r, out var s);
            gc.LocalPosition = t;
            gc.LocalRotation = r;
            gc.LocalScale = s;
        }
    }
}
