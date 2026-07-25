// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Builds the <see cref="IntermediateNode"/> hierarchy from glTF <c>nodes</c> + <c>scenes</c>.
/// </summary>
/// <remarks>
/// Because one glTF mesh expands to N <see cref="IntermediateMesh"/>es (one per primitive), a glTF
/// node with a <c>mesh</c> reference expands into the node itself plus N-1 sibling sub-nodes
/// (one per extra primitive). Both the primary node and every sub-node inherit the source node's
/// <c>skin</c> reference so they all render with the same skeleton.
/// </remarks>
internal static class GltfNodeMapper
{
    public sealed class Result
    {
        public required IntermediateNode Root { get; init; }
        /// <summary>Length = dom.Nodes.Length. Element i is the IntermediateNode produced from
        /// glTF node i (the primary node for mesh-bearing nodes).</summary>
        public required IntermediateNode[] SourceNodeToIntermediate { get; init; }
    }

    public static Result Map(
        GltfDom dom,
        GltfMeshMapper.Result meshMapping,
        IntermediateScene scene,
        ImportContext ctx)
    {
        var sourceNodes = dom.Nodes ?? Array.Empty<GltfNode>();
        var built = new IntermediateNode[sourceNodes.Length];

        for (int i = 0; i < sourceNodes.Length; i++)
            built[i] = BuildSingle(sourceNodes[i], i);

        for (int i = 0; i < sourceNodes.Length; i++)
        {
            var children = sourceNodes[i].Children;
            if (children is null) continue;
            foreach (int childIdx in children)
            {
                if ((uint)childIdx >= (uint)built.Length)
                    throw new ImportException($"Node {i} references missing child {childIdx}.");
                var childNode = built[childIdx];
                childNode.Parent = built[i];
                built[i].Children.Add(childNode);
            }
        }

        var root = new IntermediateNode { Name = "<RootNode>" };
        int[]? sceneRoots = ResolveSceneRoots(dom);
        if (sceneRoots is not null)
        {
            foreach (int idx in sceneRoots)
            {
                if ((uint)idx >= (uint)built.Length) continue;
                root.Children.Add(built[idx]);
                built[idx].Parent = root;
            }
        }
        else
        {
            for (int i = 0; i < built.Length; i++)
            {
                if (built[i].Parent is null)
                {
                    root.Children.Add(built[i]);
                    built[i].Parent = root;
                }
            }
        }

        ExpandMultiPrimitiveMeshNodes(sourceNodes, built, meshMapping, ctx);

        scene.Nodes.Clear();
        AppendDepthFirst(root, scene.Nodes);

        return new Result
        {
            Root = root,
            SourceNodeToIntermediate = built,
        };
    }

    private static int[]? ResolveSceneRoots(GltfDom dom)
    {
        if (dom.Scenes is null || dom.Scenes.Length == 0)
            return null;
        int sceneIdx = dom.DefaultScene ?? 0;
        if ((uint)sceneIdx >= (uint)dom.Scenes.Length)
            sceneIdx = 0;
        return dom.Scenes[sceneIdx].Nodes;
    }

    private static IntermediateNode BuildSingle(GltfNode src, int sourceIndex)
    {
        var node = new IntermediateNode
        {
            Name = src.Name ?? $"Node_{sourceIndex}",
            SkinIndex = src.Skin ?? -1,
        };

        if (src.Matrix is { Length: 16 } m)
        {
            var matrix = new Float4x4(
                new Float4(m[0], m[1], m[2], m[3]),
                new Float4(m[4], m[5], m[6], m[7]),
                new Float4(m[8], m[9], m[10], m[11]),
                new Float4(m[12], m[13], m[14], m[15]));
            Prowl.Clay.PostProcess.SceneBakerHelpers.DecomposeMatrix(matrix, out Float3 t, out Quaternion r, out Float3 s);
            node.LocalPosition = t;
            node.LocalRotation = r;
            node.LocalScale = s;
        }
        else
        {
            if (src.Translation is { Length: 3 } tr)
                node.LocalPosition = new Float3(tr[0], tr[1], tr[2]);
            if (src.Rotation is { Length: 4 } rt)
                node.LocalRotation = new Quaternion(rt[0], rt[1], rt[2], rt[3]);
            if (src.Scale is { Length: 3 } sc)
                node.LocalScale = new Float3(sc[0], sc[1], sc[2]);
        }

        return node;
    }

    private static void ExpandMultiPrimitiveMeshNodes(
        GltfNode[] sourceNodes,
        IntermediateNode[] built,
        GltfMeshMapper.Result meshMapping,
        ImportContext ctx)
    {
        for (int i = 0; i < sourceNodes.Length; i++)
        {
            int? mi = sourceNodes[i].Mesh;
            if (mi is null) continue;

            if ((uint)mi.Value >= (uint)meshMapping.MeshIndexRanges.Count)
            {
                ctx.Log.Warning($"Node references missing mesh {mi.Value}.", "GltfNodeMapper");
                continue;
            }

            var range = meshMapping.MeshIndexRanges[mi.Value];
            if (range.Count == 0)
                continue;

            built[i].MeshIndex = range.First;

            for (int p = 1; p < range.Count; p++)
            {
                var sub = new IntermediateNode
                {
                    Name = $"{built[i].Name}_prim{p}",
                    Parent = built[i],
                    MeshIndex = range.First + p,
                    SkinIndex = built[i].SkinIndex,
                };
                built[i].Children.Add(sub);
            }
        }
    }

    private static void AppendDepthFirst(IntermediateNode node, List<IntermediateNode> list)
    {
        list.Add(node);
        foreach (var c in node.Children)
            AppendDepthFirst(c, list);
    }

}
