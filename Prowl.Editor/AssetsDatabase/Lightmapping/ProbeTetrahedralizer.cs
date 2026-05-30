// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Editor.Lightmapping;

/// <summary>
/// 3D Delaunay tetrahedralization (Bowyer-Watson) of light-probe positions. Produces the tetra
/// vertex indices + per-face neighbour links the runtime <c>LightProbeVolume</c> walks. Probe sets
/// are normally well-conditioned (grids / scattered points); a tiny deterministic jitter guards
/// against exact coplanar/cospherical degeneracies.
/// </summary>
public static class ProbeTetrahedralizer
{
    public struct Result
    {
        /// <summary>4 probe indices per tetrahedron.</summary>
        public int[] Tetrahedra;
        /// <summary>4 neighbour-tet indices per tetra (across the face opposite vertex i); -1 = hull.</summary>
        public int[] Neighbours;
    }

    private struct Tet
    {
        public int A, B, C, D;
        public bool Dead;
        public Tet(int a, int b, int c, int d) { A = a; B = b; C = c; D = d; Dead = false; }
        public int Get(int i) => i == 0 ? A : i == 1 ? B : i == 2 ? C : D;
    }

    /// <summary>Tetrahedralize <paramref name="points"/>. Returns empty arrays when fewer than 4 points.</summary>
    public static Result Build(IReadOnlyList<Float3> points)
    {
        int n = points.Count;
        if (n < 4) return new Result { Tetrahedra = [], Neighbours = [] };

        // Working vertex list: real points + 4 super-tetra corners appended at the end.
        var verts = new Double3[n + 4];
        Double3 min = new(double.MaxValue), max = new(double.MinValue);
        for (int i = 0; i < n; i++)
        {
            // Deterministic sub-mm jitter to avoid cospherical/coplanar degeneracies.
            double jx = ((i * 73856093) & 1023) / 1023.0 - 0.5;
            double jy = ((i * 19349663) & 1023) / 1023.0 - 0.5;
            double jz = ((i * 83492791) & 1023) / 1023.0 - 0.5;
            var p = new Double3(points[i].X + jx * 1e-4, points[i].Y + jy * 1e-4, points[i].Z + jz * 1e-4);
            verts[i] = p;
            min = new Double3(System.Math.Min(min.X, p.X), System.Math.Min(min.Y, p.Y), System.Math.Min(min.Z, p.Z));
            max = new Double3(System.Math.Max(max.X, p.X), System.Math.Max(max.Y, p.Y), System.Math.Max(max.Z, p.Z));
        }

        Double3 c = (min + max) * 0.5;
        Double3 size = max - min;
        double d = System.Math.Max(size.X, System.Math.Max(size.Y, size.Z));
        if (d <= 0) d = 1;
        double big = d * 1000.0;
        // A large tetra enclosing all points.
        verts[n + 0] = new Double3(c.X - big, c.Y - big, c.Z - big);
        verts[n + 1] = new Double3(c.X + big * 3, c.Y - big, c.Z - big);
        verts[n + 2] = new Double3(c.X - big, c.Y + big * 3, c.Z - big);
        verts[n + 3] = new Double3(c.X - big, c.Y - big, c.Z + big * 3);

        var tets = new List<Tet>(n * 6) { new Tet(n, n + 1, n + 2, n + 3) };

        var badFaces = new List<(int v0, int v1, int v2)>();
        for (int ip = 0; ip < n; ip++)
        {
            Double3 p = verts[ip];
            badFaces.Clear();

            // Collect faces of all tetra whose circumsphere contains p, killing those tetra.
            for (int t = 0; t < tets.Count; t++)
            {
                var tet = tets[t];
                if (tet.Dead) continue;
                if (!InCircumsphere(verts[tet.A], verts[tet.B], verts[tet.C], verts[tet.D], p)) continue;

                tets[t] = tet with { Dead = true };
                AddFace(badFaces, tet.A, tet.B, tet.C);
                AddFace(badFaces, tet.A, tet.B, tet.D);
                AddFace(badFaces, tet.A, tet.C, tet.D);
                AddFace(badFaces, tet.B, tet.C, tet.D);
            }

            // Faces that appear once form the cavity boundary; connect each to p.
            for (int f = 0; f < badFaces.Count; f++)
            {
                if (badFaces[f].v0 < 0) continue; // marked as shared (interior)
                var face = badFaces[f];
                tets.Add(MakeOriented(verts, face.v0, face.v1, face.v2, ip));
            }
        }

        // Drop tetra touching any super-tetra vertex, compact, and index.
        var finalTets = new List<Tet>(tets.Count);
        foreach (var t in tets)
        {
            if (t.Dead) continue;
            if (t.A >= n || t.B >= n || t.C >= n || t.D >= n) continue;
            finalTets.Add(t);
        }

        int m = finalTets.Count;
        var tetra = new int[m * 4];
        for (int i = 0; i < m; i++)
        {
            tetra[i * 4 + 0] = finalTets[i].A;
            tetra[i * 4 + 1] = finalTets[i].B;
            tetra[i * 4 + 2] = finalTets[i].C;
            tetra[i * 4 + 3] = finalTets[i].D;
        }

        return new Result { Tetrahedra = tetra, Neighbours = BuildNeighbours(finalTets, m) };
    }

    // Face de-dup: if a face is seen twice it's interior (shared by two killed tetra) -> mark both gone.
    // We store the SORTED triple as the key so the comparison is order-independent; the cavity face's
    // winding doesn't need preserving because MakeOriented re-derives it from the apex.
    private static void AddFace(List<(int, int, int)> faces, int a, int b, int c)
    {
        int x = a, y = b, z = c;
        Sort3(ref x, ref y, ref z);
        for (int i = 0; i < faces.Count; i++)
        {
            if (faces[i].Item1 == x && faces[i].Item2 == y && faces[i].Item3 == z)
            {
                faces[i] = (-1, -1, -1); // shared -> remove from boundary
                return;
            }
        }
        faces.Add((x, y, z));
    }

    private static void Sort3(ref int a, ref int b, ref int c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
    }

    // Orient (f0,f1,f2,apex) so the signed volume is positive (consistent winding for predicates).
    private static Tet MakeOriented(Double3[] v, int f0, int f1, int f2, int apex)
    {
        double vol = Orient3D(v[f0], v[f1], v[f2], v[apex]);
        return vol < 0 ? new Tet(f0, f1, f2, apex) : new Tet(f0, f2, f1, apex);
    }

    private static int[] BuildNeighbours(List<Tet> tets, int m)
    {
        var neighbours = new int[m * 4];
        for (int i = 0; i < neighbours.Length; i++) neighbours[i] = -1;

        // Map a sorted face -> (tet, faceIndex). Face opposite vertex i is the other three.
        var faceMap = new Dictionary<(int, int, int), (int tet, int face)>();
        for (int t = 0; t < m; t++)
        {
            for (int fi = 0; fi < 4; fi++)
            {
                int a = tets[t].Get((fi + 1) & 3);
                int b = tets[t].Get((fi + 2) & 3);
                int cc = tets[t].Get((fi + 3) & 3);
                Sort3(ref a, ref b, ref cc);
                var key = (a, b, cc);
                if (faceMap.TryGetValue(key, out var other))
                {
                    neighbours[t * 4 + fi] = other.tet;
                    neighbours[other.tet * 4 + other.face] = t;
                }
                else faceMap[key] = (t, fi);
            }
        }
        return neighbours;
    }

    // Orient3D: positive when d is below the plane of (a,b,c) in right-handed terms.
    private static double Orient3D(Double3 a, Double3 b, Double3 c, Double3 dp)
    {
        Double3 ad = a - dp, bd = b - dp, cd = c - dp;
        return ad.X * (bd.Y * cd.Z - bd.Z * cd.Y)
             - ad.Y * (bd.X * cd.Z - bd.Z * cd.X)
             + ad.Z * (bd.X * cd.Y - bd.Y * cd.X);
    }

    // InSphere: true if p lies strictly inside the circumsphere of (a,b,c,d). Uses the standard
    // 4x4 lifted determinant, sign-corrected by the tetra orientation.
    private static bool InCircumsphere(Double3 a, Double3 b, Double3 c, Double3 d, Double3 p)
    {
        double orient = Orient3D(a, b, c, d);
        if (System.Math.Abs(orient) < 1e-18) return false;

        Double3 ap = a - p, bp = b - p, cp = c - p, dp = d - p;
        double a2 = Dot(ap, ap), b2 = Dot(bp, bp), c2 = Dot(cp, cp), d2 = Dot(dp, dp);

        // 4x4 determinant of [ap.x ap.y ap.z a2; ...] expanded.
        double det =
              a2 * Det3(bp, cp, dp)
            - b2 * Det3(ap, cp, dp)
            + c2 * Det3(ap, bp, dp)
            - d2 * Det3(ap, bp, cp);

        // Sign convention: with positive orientation, det > 0 means inside.
        return orient > 0 ? det > 0 : det < 0;
    }

    private static double Det3(Double3 a, Double3 b, Double3 c)
        => a.X * (b.Y * c.Z - b.Z * c.Y)
         - a.Y * (b.X * c.Z - b.Z * c.X)
         + a.Z * (b.X * c.Y - b.Y * c.X);

    private static double Dot(Double3 a, Double3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
