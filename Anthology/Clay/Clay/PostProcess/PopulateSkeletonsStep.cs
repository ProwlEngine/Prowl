using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Finalises <see cref="IntermediateSkin"/>s: when the source did not declare an explicit skeleton
/// root, this step infers the closest common ancestor of all bone nodes and assigns it.
/// </summary>
/// <remarks>
/// glTF skins may omit <c>skeleton</c>, in which case the engine usually wants a single node
/// whose subtree contains every joint, so transforms above it can move the rig as a unit.
/// </remarks>
internal sealed class PopulateSkeletonsStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.PopulateSkeletons;
    public string Name => "PopulateSkeletons";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        foreach (var skin in scene.Skins)
        {
            if (skin.RootNode is not null || skin.BoneNodes.Count == 0)
                continue;

            skin.RootNode = FindClosestCommonAncestor(skin.BoneNodes);
            if (skin.RootNode is null)
                context.Log.Warning(
                    $"Could not determine a skeleton root for skin '{skin.Name ?? "(unnamed)"}'; using first joint.",
                    Name);

            skin.RootNode ??= skin.BoneNodes[0];
        }
    }

    private static IntermediateNode? FindClosestCommonAncestor(IReadOnlyList<IntermediateNode> nodes)
    {
        if (nodes.Count == 0) return null;
        if (nodes.Count == 1) return nodes[0];

        // Collect the ancestor chain (including self) of the first node, then walk each other
        // node's ancestor chain and find the deepest one shared by all.
        var chainOfFirst = new HashSet<IntermediateNode>();
        for (IntermediateNode? cur = nodes[0]; cur is not null; cur = cur.Parent)
            chainOfFirst.Add(cur);

        IntermediateNode? candidate = nodes[0];
        for (int i = 1; i < nodes.Count; i++)
        {
            IntermediateNode? cur = nodes[i];
            while (cur is not null && !chainOfFirst.Contains(cur))
                cur = cur.Parent;
            if (cur is null) return null;
            candidate = cur;
            // Restrict subsequent searches: rebuild chainOfFirst to candidate's chain.
            chainOfFirst.Clear();
            for (IntermediateNode? n = candidate; n is not null; n = n.Parent)
                chainOfFirst.Add(n);
        }
        return candidate;
    }
}
