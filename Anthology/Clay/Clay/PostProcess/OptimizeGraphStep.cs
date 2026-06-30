using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Collapses pass-through nodes (no mesh, no skin, not a joint, not animated, not on the
/// preserve list). Each collapsed node's local transform is folded into its parent's children's
/// transforms.
/// </summary>
/// <remarks>
/// Significantly reduces hierarchy depth for content authored in DCC tools that use lots of
/// grouping nodes (Blender empties, Maya transform groups, etc.).
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
            AppendDepthFirst(scene.Root, scene.Nodes);
            context.Log.Info($"Collapsed {collapsed} pass-through node(s).", Name);
        }
    }

    private static HashSet<IntermediateNode> ComputeKeepSet(IntermediateScene scene, IReadOnlyList<string> preserveNames)
    {
        var keep = new HashSet<IntermediateNode>();

        foreach (var node in scene.Nodes)
        {
            if (node.MeshIndex >= 0 || node.SkinIndex >= 0)
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

    private static int CollapseRecursive(IntermediateNode node, HashSet<IntermediateNode> keep)
    {
        int collapsed = 0;

        // Replace child list iteratively, walking by index because we mutate.
        for (int i = 0; i < node.Children.Count; )
        {
            var child = node.Children[i];

            // First recurse so grand-children stabilize before we look at child.
            collapsed += CollapseRecursive(child, keep);

            if (!keep.Contains(child) && child.Children.Count == 0)
            {
                node.Children.RemoveAt(i);
                collapsed++;
                continue;
            }

            if (!keep.Contains(child))
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
        Float4x4 m = SceneBakerHelpers.ComposeTRS(child.LocalPosition, child.LocalRotation, child.LocalScale);

        foreach (var gc in child.Children)
        {
            Float4x4 g = SceneBakerHelpers.ComposeTRS(gc.LocalPosition, gc.LocalRotation, gc.LocalScale);
            Float4x4 combined = SceneBakerHelpers.Mul(m, g);
            SceneBakerHelpers.DecomposeMatrix(combined, out var t, out var r, out var s);
            gc.LocalPosition = t;
            gc.LocalRotation = r;
            gc.LocalScale = s;
        }
    }

    private static void AppendDepthFirst(IntermediateNode node, List<IntermediateNode> list)
    {
        list.Add(node);
        foreach (var c in node.Children)
            AppendDepthFirst(c, list);
    }
}
