using System.Collections.Generic;

namespace Prowl.Unwrapper;

/// <summary>
/// A subset of triangles within a <see cref="HalfEdgeMesh"/>. The mesh is shared; multiple
/// regions can coexist with overlapping or disjoint triangle sets.
/// </summary>
internal sealed class MeshRegion
{
    public HalfEdgeMesh Mesh;
    public int[] Triangles;

    public MeshRegion(HalfEdgeMesh mesh, int triangleCount)
    {
        Mesh = mesh;
        Triangles = new int[triangleCount];
    }

    /// <summary>
    /// Walk every corner of every triangle and fill <paramref name="vertexList"/> with the dense
    /// list of source-vertex indices used here. <paramref name="vertexLookup"/> is its inverse
    /// (original vertex index → dense slot).
    /// </summary>
    public void CollectVertices(List<int> vertexList, Dictionary<int, int> vertexLookup)
    {
        vertexLookup.Clear();
        for (int i = 0; i < Triangles.Length; ++i)
        {
            HalfEdge h0 = Mesh.Triangles[Triangles[i]].FirstEdge!;
            HalfEdge h1 = h0.Next!;
            HalfEdge h2 = h1.Next!;

            vertexLookup.TryAdd(Mesh.IndexOf(h0.Apex!), 1);
            vertexLookup.TryAdd(Mesh.IndexOf(h1.Apex!), 1);
            vertexLookup.TryAdd(Mesh.IndexOf(h2.Apex!), 1);
        }

        vertexList.Clear();
        // Walk in vertex order so the resulting indexing is stable across runs.
        for (int v = 0; v < Mesh.Vertices.Count; ++v)
        {
            if (vertexLookup.ContainsKey(v))
            {
                vertexLookup[v] = vertexList.Count;
                vertexList.Add(v);
            }
        }
    }

    /// <summary>Map each triangle's source-mesh index to its slot within this region.</summary>
    public void BuildTriangleLookup(Dictionary<int, int> triangleLookup)
    {
        triangleLookup.Clear();
        for (int i = 0; i < Triangles.Length; ++i)
            triangleLookup[Triangles[i]] = i;
    }

    /// <summary>For every triangle of this region, look up its position in the supplied table.</summary>
    public void RemapTriangles(Dictionary<int, int> triangleLookup, List<int> output)
    {
        output.Clear();
        for (int i = 0; i < Triangles.Length; ++i)
            output.Add(triangleLookup[Triangles[i]]);
    }
}
