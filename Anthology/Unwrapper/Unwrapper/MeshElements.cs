using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// A vertex inside the half-edge graph. Keeps one incident half-edge so we can walk
/// the vertex star, plus the source index it came from (preserved across cleanup).
/// </summary>
/// <remarks>
/// <see cref="Index"/> mirrors the slot this vertex occupies in its owning <see cref="HalfEdgeMesh"/>.
/// It's stamped in at insert time to keep <c>HalfEdgeMesh.IndexOf</c> O(1) (vs O(N) on a List).
/// </remarks>
internal sealed class Vertex
{
    public Double3 Position;
    public HalfEdge? IncidentEdge;
    public int SourceIndex;
    public int Index;

    public Vertex(Double3 position, int sourceIndex)
    {
        Position = position;
        SourceIndex = sourceIndex;
    }
}

/// <summary>A triangular face — owns one of its three half-edges.</summary>
internal sealed class MeshFace
{
    public HalfEdge? FirstEdge;
    public int Index;
}

/// <summary>
/// One side of an edge in the half-edge data structure. Linked into a ring around its face;
/// every interior edge has a twin going the other way. A half-edge with no <see cref="Face"/>
/// is a border edge bordering a hole.
/// </summary>
internal sealed class HalfEdge
{
    public HalfEdge? Twin;
    public HalfEdge? Next;
    public HalfEdge? Previous;
    public MeshFace? Face;
    public Vertex? Apex;
    public int Index;
}

/// <summary>Cached per-face properties precomputed during topology build.</summary>
internal struct FaceAttribute
{
    public Double3 Centroid;
    public Double3 Normal;
    public double Area;
    public int SourceIndex;
    public int VertexA;
    public int VertexB;
    public int VertexC;
}

/// <summary>Cached per-half-edge flags driving segmentation.</summary>
internal struct EdgeAttribute
{
    public double Length;
    public bool IsUvSeam;
    public bool IsCrease;
}
