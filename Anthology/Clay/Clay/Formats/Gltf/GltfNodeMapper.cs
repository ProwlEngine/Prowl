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
/// One glTF mesh is one <see cref="IntermediateMesh"/> with its primitives merged, so the hierarchy
/// here is exactly the one the file describes: a node with a <c>mesh</c> reference points at that
/// mesh and nothing is synthesised alongside it.
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
            built[i] = BuildSingle(sourceNodes[i], i, scene, ctx);

        for (int i = 0; i < sourceNodes.Length; i++)
        {
            var children = sourceNodes[i].Children;
            if (children is null) continue;
            foreach (int childIdx in children)
            {
                if ((uint)childIdx >= (uint)built.Length)
                    throw new ImportException($"Node {i} references missing child {childIdx}.");
                if (childIdx == i)
                    throw new ImportException($"Node {i} lists itself as a child.");

                var childNode = built[childIdx];
                // glTF nodes form a tree, so a second parent means the file is malformed. Linking it
                // anyway leaves the node in two children lists with one Parent pointer, which the
                // depth-first walk then visits twice under conflicting transforms.
                if (childNode.Parent is not null)
                    throw new ImportException($"Node {childIdx} is a child of more than one node.");

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
                // The scene lists roots, so a node that already has a parent is reachable through it
                // and adding it here too would make the walk visit it twice.
                if (built[idx].Parent is not null)
                {
                    ctx.Log.Warning($"Scene lists node {idx}, which is already a child of another node.", "GltfNodeMapper");
                    continue;
                }
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

        AttachMeshes(sourceNodes, built, meshMapping, ctx);

        NodeGraph.ValidateNoCycles(built);

        scene.Nodes.Clear();
        NodeGraph.Flatten(root, scene.Nodes);

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

    private static IntermediateNode BuildSingle(GltfNode src, int sourceIndex, IntermediateScene scene, ImportContext ctx)
    {
        int camera = src.Camera ?? -1;
        if (camera >= scene.Cameras.Count)
        {
            ctx.Log.Warning(
                $"Node '{src.Name ?? "(unnamed)"}' references camera {camera}, but the file declares {scene.Cameras.Count}.",
                "GltfNodeMapper");
            camera = -1;
        }

        var node = new IntermediateNode
        {
            Name = src.Name ?? $"Node_{sourceIndex}",
            SkinIndex = src.Skin ?? -1,
            CameraIndex = camera,
            LightIndex = GltfCameraLightMapper.ReadNodeLight(src, scene.Lights.Count, ctx),
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

    /// <summary>
    /// Attaches each mesh-bearing node to its mesh. One glTF mesh is one
    /// <see cref="IntermediateMesh"/>, so this is a straight assignment: the sibling nodes that used
    /// to be synthesised per primitive are gone, and a node keeps the single object the file
    /// described.
    /// </summary>
    private static void AttachMeshes(
        GltfNode[] sourceNodes,
        IntermediateNode[] built,
        GltfMeshMapper.Result meshMapping,
        ImportContext ctx)
    {
        for (int i = 0; i < sourceNodes.Length; i++)
        {
            int? mi = sourceNodes[i].Mesh;
            if (mi is null) continue;

            if ((uint)mi.Value >= (uint)meshMapping.MeshIndex.Count)
            {
                ctx.Log.Warning($"Node references missing mesh {mi.Value}.", "GltfNodeMapper");
                continue;
            }

            built[i].MeshIndex = meshMapping.MeshIndex[mi.Value];
        }
    }
}
