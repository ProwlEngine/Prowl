using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Free queries over a half-edge graph. Nothing here mutates the topology.
/// </summary>
internal static class MeshOps
{
    public static bool IsBorderEdge(HalfEdge edge) => edge.Face is null;

    /// <summary>True if any half-edge in this vertex's fan is a border.</summary>
    public static bool TouchesBorder(Vertex v, int cap = 1 << 20)
    {
        HalfEdge cur = v.IncidentEdge!;
        HalfEdge start = cur;
        int steps = 0;
        do
        {
            if (IsBorderEdge(cur)) return true;
            if (++steps >= cap) return true;   // Treat unbounded fans as a border for safety.
            cur = StepAroundVertex(cur);
        } while (cur != start);
        return false;
    }

    /// <summary>True if any of this face's three opposing twins is a border.</summary>
    public static bool TouchesBorder(MeshFace face, int cap = 1 << 20)
    {
        HalfEdge cur = face.FirstEdge!;
        HalfEdge start = cur;
        int steps = 0;
        do
        {
            if (IsBorderEdge(cur.Twin!)) return true;
            if (++steps >= cap) return true;
            cur = StepAroundVertex(cur);
        } while (cur != start);
        return false;
    }

    /// <summary>
    /// Number of half-edges in this vertex's fan. The walk is capped to keep non-manifold
    /// inputs from spinning indefinitely; a typical mesh vertex has degree well under 32.
    /// </summary>
    public static int FanSize(Vertex v, int cap = 1 << 20)
    {
        int degree = 0;
        HalfEdge cur = v.IncidentEdge!;
        HalfEdge start = cur;
        do
        {
            ++degree;
            if (degree >= cap) return degree;
            cur = StepAroundVertex(cur);
        } while (cur != start);
        return degree;
    }

    /// <summary>Vector from the half-edge's origin to its apex.</summary>
    public static Double3 EdgeVector(HalfEdge edge)
        => edge.Apex!.Position - edge.Previous!.Apex!.Position;

    /// <summary>Move to the next half-edge that starts at the same vertex.</summary>
    public static HalfEdge StepAroundVertex(HalfEdge edge) => edge.Twin!.Previous!;

    public static Double3 ComputeFaceNormal(MeshFace face)
    {
        // Normalising both edges before the cross keeps denormals at bay.
        Double3 e1 = Double3.Normalize(EdgeVector(face.FirstEdge!));
        Double3 e2 = Double3.Normalize(EdgeVector(face.FirstEdge!.Previous!.Twin!));
        return Double3.Normalize(Double3.Cross(e1, e2));
    }

    public static Double3 ComputeFaceCentroid(MeshFace face)
    {
        Double3 p0 = face.FirstEdge!.Apex!.Position;
        Double3 p1 = face.FirstEdge!.Next!.Apex!.Position;
        Double3 p2 = face.FirstEdge!.Next!.Next!.Apex!.Position;
        return (p0 + p1 + p2) / 3.0;
    }

    public static double ComputeFaceArea(MeshFace face)
    {
        Double3 p0 = face.FirstEdge!.Apex!.Position;
        Double3 p1 = face.FirstEdge!.Next!.Apex!.Position;
        Double3 p2 = face.FirstEdge!.Next!.Next!.Apex!.Position;
        return 0.5 * Double3.Length(Double3.Cross(p1 - p0, p2 - p0));
    }

    /// <summary>Interior angle of the corner at this half-edge's apex.</summary>
    public static double CornerAngleAt(HalfEdge edge)
    {
        Double3 a = EdgeVector(edge);
        Double3 b = EdgeVector(edge.Next!);
        double c = Double3.Dot(a, b) / (Double3.Length(a) * Double3.Length(b));
        c = System.Math.Clamp(c, -1.0, 1.0);
        return System.Math.PI - System.Math.Acos(c);
    }

    public static void StitchSequential(HalfEdge a, HalfEdge b)
    {
        a.Next = b;
        b.Previous = a;
    }

    public static void StitchTwins(HalfEdge a, HalfEdge b)
    {
        a.Twin = b;
        b.Twin = a;
    }

    /// <summary>
    /// Walk the half-edges in a vertex's star, invoking <paramref name="visit"/> on each.
    /// Bounded by <paramref name="cap"/> so a broken (non-manifold) topology can't spin forever.
    /// Returns the number of steps actually taken.
    /// </summary>
    public static int WalkVertexStar(Vertex v, System.Action<HalfEdge> visit, int cap = 1 << 20)
    {
        HalfEdge edge = v.IncidentEdge!;
        HalfEdge start = edge;
        int steps = 0;
        do
        {
            visit(edge);
            if (++steps >= cap) return steps;
            edge = StepAroundVertex(edge);
        } while (edge != start);
        return steps;
    }
}
