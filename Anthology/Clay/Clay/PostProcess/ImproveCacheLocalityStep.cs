// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector.Geometry;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Orders each material's triangles for the post transform vertex cache with <see cref="VertexCacheOptimizer"/>, the
/// materials in the order the baker draws them, then renumbers the vertices in the order those triangles first use
/// them so their fetches stay close. Lines and points follow the triangles in their own order.
/// </summary>
/// <remarks>Renumbers every per vertex stream, so it runs after the steps that keep vertex references.</remarks>
internal sealed class ImproveCacheLocalityStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.ImproveCacheLocality;
    public string Name => "ImproveCacheLocality";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        foreach (var mesh in scene.Meshes)
            OptimizeOne(mesh);
        _ = context;
    }

    private static void OptimizeOne(IntermediateMesh mesh)
    {
        FaceList faces = mesh.Faces;
        var materials = new List<int>();
        var triangles = new Dictionary<int, List<int>>();
        for (int f = 0; f < faces.Count; f++)
        {
            if (faces[f].Count != 3)
                continue;
            int material = mesh.MaterialForFace(faces[f]);
            if (!triangles.TryGetValue(material, out List<int>? group))
            {
                triangles[material] = group = new List<int>();
                materials.Add(material);
            }
            group.Add(f);
        }
        if (materials.Count == 0)
            return;

        uint vertexCount = (uint)mesh.Positions.Count;
        var ordered = new FaceList(faces.Count, faces.AllIndices.Length);
        foreach (int material in materials)
        {
            List<int> group = triangles[material];
            uint[] indices = new uint[group.Count * 3];
            for (int t = 0; t < group.Count; t++)
            {
                Span<int> face = faces.IndicesOf(group[t]);
                for (int k = 0; k < 3; k++)
                    indices[t * 3 + k] = (uint)face[k];
            }
            foreach (int t in VertexCacheOptimizer.TriangleOrder(indices, vertexCount))
                ordered.Add(faces.IndicesOf(group[t]), faces[group[t]]);
        }
        for (int f = 0; f < faces.Count; f++)
        {
            if (faces[f].Count != 3)
                ordered.Add(faces.IndicesOf(f), faces[f]);
        }
        mesh.Faces = ordered;

        uint[] firstUse = VertexCacheOptimizer.FirstUseOrder(MemoryMarshal.Cast<int, uint>(ordered.AllIndices), vertexCount);
        int[] oldToNew = new int[vertexCount], kept = new int[vertexCount];
        for (int v = 0; v < vertexCount; v++)
        {
            oldToNew[v] = (int)firstUse[v];
            kept[firstUse[v]] = v;
        }
        JoinIdenticalVerticesStep.Compact(mesh, oldToNew, kept, (int)vertexCount);
    }
}
