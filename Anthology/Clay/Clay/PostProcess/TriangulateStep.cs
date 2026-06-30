using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Splits faces with 4 or more indices into triangles. Faces of size 1 (points) and 2 (lines)
/// pass through unchanged. Strips/fans were already unrolled by the format reader.
/// </summary>
/// <remarks>
/// Convex quads use the fan triangulation (v0,v1,v2)+(v0,v2,v3). For polygons of higher arity,
/// or for concave quads, an ear-clipping pass on a 2D projection of the polygon is applied.
/// Most game-art content arrives pre-triangulated, so the slow path is rarely exercised.
/// </remarks>
internal sealed class TriangulateStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.Triangulate;
    public string Name => "Triangulate";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        foreach (var mesh in scene.Meshes)
        {
            if ((mesh.PrimitiveKinds & PrimitiveKind.Polygon) == 0)
            {
                // Even meshes the reader marked as "Triangle only" may carry polygons if a later
                // step inserted some; we still walk the face list to be safe.
            }

            var oldFaces = mesh.Faces;
            var newFaces = new List<IntermediateFace>(oldFaces.Count);

            foreach (var face in oldFaces)
            {
                int n = face.Indices.Length;
                if (n <= 3)
                {
                    newFaces.Add(face);
                    continue;
                }

                if (n == 4 && IsConvexQuad(mesh, face.Indices))
                {
                    int a = face.Indices[0], b = face.Indices[1], c = face.Indices[2], d = face.Indices[3];
                    newFaces.Add(new IntermediateFace(new[] { a, b, c }));
                    newFaces.Add(new IntermediateFace(new[] { a, c, d }));
                    continue;
                }

                EarClip(mesh, face.Indices, newFaces, context);
            }

            mesh.Faces.Clear();
            mesh.Faces.AddRange(newFaces);
            mesh.PrimitiveKinds &= ~PrimitiveKind.Polygon;
            mesh.PrimitiveKinds |= PrimitiveKind.Triangle;
        }
    }

    private static bool IsConvexQuad(IntermediateMesh mesh, int[] q)
    {
        Float3 a = mesh.Positions[q[0]];
        Float3 b = mesh.Positions[q[1]];
        Float3 c = mesh.Positions[q[2]];
        Float3 d = mesh.Positions[q[3]];

        // Compute cross products of consecutive edges; sign-consistency around the loop means convex.
        var n0 = Cross(b - a, c - b);
        var n1 = Cross(c - b, d - c);
        var n2 = Cross(d - c, a - d);
        var n3 = Cross(a - d, b - a);
        return Dot(n0, n1) > 0f && Dot(n1, n2) > 0f && Dot(n2, n3) > 0f;
    }

    private static void EarClip(IntermediateMesh mesh, int[] poly, List<IntermediateFace> output, ImportContext ctx)
    {
        int n = poly.Length;
        Float3 normal = PolygonNormal(mesh, poly);
        (int axisU, int axisV) = PickProjectionAxes(normal);

        // Pre-project once.
        var pts2 = new Float2[n];
        for (int i = 0; i < n; i++)
            pts2[i] = ProjectPoint(mesh.Positions[poly[i]], axisU, axisV);

        // Compute the signed area of the projected polygon. If it's negative, the projected
        // polygon is clockwise in our 2D frame, so reverse iteration so the rest of the algorithm
        // always works on a CCW polygon. Without this, every triangle fails the convex test and
        // ear-clipping spins on the guard counter.
        float signedArea = 0f;
        for (int i = 0; i < n; i++)
        {
            Float2 a = pts2[i];
            Float2 b = pts2[(i + 1) % n];
            signedArea += a.X * b.Y - b.X * a.Y;
        }
        bool reversed = signedArea < 0f;

        // Build the working linked list (prev/next), respecting the desired winding direction.
        var prev = new int[n];
        var next = new int[n];
        if (!reversed)
        {
            for (int i = 0; i < n; i++) { prev[i] = (i + n - 1) % n; next[i] = (i + 1) % n; }
        }
        else
        {
            for (int i = 0; i < n; i++) { prev[i] = (i + 1) % n; next[i] = (i + n - 1) % n; }
        }

        int remaining = n;
        int guard = n * n; // generous bound so concave polygons get a fair number of skips
        int cur = 0;

        while (remaining > 3 && guard-- > 0)
        {
            int p = prev[cur];
            int q = cur;
            int r = next[cur];

            if (IsEar(pts2[p], pts2[q], pts2[r], pts2, prev, next, p, q, r))
            {
                output.Add(new IntermediateFace(new[] { poly[p], poly[q], poly[r] }));
                next[p] = r;
                prev[r] = p;
                remaining--;
                cur = r;
            }
            else
            {
                cur = next[cur];
            }
        }

        if (remaining == 3)
        {
            int p = prev[cur], q = cur, r = next[cur];
            output.Add(new IntermediateFace(new[] { poly[p], poly[q], poly[r] }));
            return;
        }

        // Last resort: if ear-clip didn't terminate cleanly, fan-triangulate the remaining
        // un-clipped polygon. We'd rather emit something potentially overlapping than drop the
        // face entirely - downstream RemoveDegenerates + JoinIdenticalVertices clean up.
        if (remaining > 3)
        {
            int[] survivors = new int[remaining];
            int s = 0;
            int walk = cur;
            for (int i = 0; i < remaining; i++) { survivors[i] = walk; walk = next[walk]; }
            for (int i = 1; i + 1 < remaining; i++)
                output.Add(new IntermediateFace(new[] { poly[survivors[0]], poly[survivors[i]], poly[survivors[i + 1]] }));
            ctx.Log.Info(
                $"Triangulate: fan-triangulated a {n}-gon after ear-clipping stalled on {remaining} remaining vertices.",
                "Triangulate");
            _ = s;
        }
    }

    private static bool IsEar(Float2 a, Float2 b, Float2 c,
        Float2[] pts2, int[] prev, int[] next, int skipA, int skipB, int skipC)
    {
        // Triangle must be convex (counter-clockwise in 2D) - we ensured CCW winding before
        // entering the loop, so the cross product is positive for convex ears.
        if (Cross2(b - a, c - a) <= 0f) return false;

        // Walk the active vertex ring (not the original ring) - already-clipped vertices are
        // skipped because the linked list omits them.
        int cursor = next[skipC];
        while (cursor != skipA)
        {
            if (cursor != skipB) // belt-and-braces: skip the three forming vertices
            {
                if (PointInTriangle(pts2[cursor], a, b, c))
                    return false;
            }
            cursor = next[cursor];
        }
        return true;
    }

    private static Float3 PolygonNormal(IntermediateMesh mesh, int[] poly)
    {
        // Newell's method - robust normal for arbitrary planar polygons.
        Float3 n = Float3.Zero;
        for (int i = 0; i < poly.Length; i++)
        {
            Float3 a = mesh.Positions[poly[i]];
            Float3 b = mesh.Positions[poly[(i + 1) % poly.Length]];
            n.X += (a.Y - b.Y) * (a.Z + b.Z);
            n.Y += (a.Z - b.Z) * (a.X + b.X);
            n.Z += (a.X - b.X) * (a.Y + b.Y);
        }
        return n;
    }

    private static (int u, int v) PickProjectionAxes(Float3 n)
    {
        float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
        if (ax >= ay && ax >= az) return (1, 2); // drop X
        if (ay >= ax && ay >= az) return (0, 2); // drop Y
        return (0, 1);                            // drop Z
    }

    private static Float2 ProjectPoint(Float3 p, int axisU, int axisV)
    {
        float u = axisU switch { 0 => p.X, 1 => p.Y, _ => p.Z };
        float v = axisV switch { 0 => p.X, 1 => p.Y, _ => p.Z };
        return new Float2(u, v);
    }

    private static bool PointInTriangle(Float2 p, Float2 a, Float2 b, Float2 c)
    {
        float d1 = Cross2(p - a, b - a);
        float d2 = Cross2(p - b, c - b);
        float d3 = Cross2(p - c, a - c);
        bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(negative && positive);
    }

    private static Float3 Cross(Float3 a, Float3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static float Dot(Float3 a, Float3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static float Cross2(Float2 a, Float2 b) => a.X * b.Y - a.Y * b.X;
}
