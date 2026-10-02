// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

public class GeometrySimplifyTests
{
    private const float GridSize = 16f;

    /// <summary>
    /// Flat grid of n by n cells in XZ, two triangles per cell. With a seam column, loops left of it get
    /// UVs from the position and loops right of it get the same UVs shifted by one, so the column is a
    /// UV seam. With regions, the same column instead splits two face regions.
    /// </summary>
    private static GeometryData Grid(int n, int seamColumn = -1, bool regions = false)
    {
        var mesh = new GeometryData();
        mesh.AddLoopAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
        if (regions) mesh.AddFaceAttribute("region", GeometryData.AttributeBaseType.Int, 1);

        var verts = new GeometryData.Vertex[n + 1, n + 1];
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
                verts[z, x] = mesh.AddVertex(x * GridSize / n, 0, z * GridSize / n);

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                bool right = seamColumn >= 0 && x >= seamColumn;
                AddTriangle(mesh, right, regions, verts[z, x], verts[z + 1, x], verts[z + 1, x + 1]);
                AddTriangle(mesh, right, regions, verts[z, x], verts[z + 1, x + 1], verts[z, x + 1]);
            }
        }
        return mesh;
    }

    private static void AddTriangle(GeometryData mesh, bool right, bool regions, params GeometryData.Vertex[] corners)
    {
        var face = mesh.AddFace(corners)!;
        if (regions)
            face.Attributes["region"] = new GeometryData.IntAttributeValue(right ? 1 : 0);

        foreach (var v in corners)
        {
            Float2 uv = UvOf(v.Point) + new Float2(right && !regions ? 1f : 0f, 0f);
            face.GetLoop(v)!.Attributes["uv"] = new GeometryData.FloatAttributeValue(uv.X, uv.Y);
        }
    }

    private static Float2 UvOf(Float3 p) => new(p.X / GridSize, p.Z / GridSize);

    private static Float2 UvOf(GeometryData.Loop loop)
    {
        var data = ((GeometryData.FloatAttributeValue)loop.Attributes["uv"]).Data;
        return new Float2(data[0], data[1]);
    }

    private static IEnumerable<GeometryData.Loop> LoopsOf(GeometryData.Face face)
    {
        var loop = face.Loop!;
        do
        {
            yield return loop;
            loop = loop.Next!;
        } while (loop != face.Loop);
    }

    private static Float3 CornerNormal(GeometryData.Face face)
    {
        var v = face.NeighborVertices();
        return Float3.Cross(v[1].Point - v[0].Point, v[2].Point - v[0].Point);
    }

    [Fact]
    public void FlatGridCollapsesToItsCorners()
    {
        var mesh = Grid(10);
        var result = GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0f, MaxError = 1e-3f });

        Assert.Equal(2, mesh.Faces.Count);
        Assert.Equal(2, result.TrianglesAfter);
        Assert.Equal(4, mesh.Vertices.Count);
        Assert.All(mesh.Vertices, v => Assert.True(
            (v.Point.X == 0 || v.Point.X == GridSize) && (v.Point.Z == 0 || v.Point.Z == GridSize)));
    }

    [Fact]
    public void ReachesTheTargetRatio()
    {
        var mesh = GeometryGenerator.Icosphere(1f, subdivisions: 4);
        int before = mesh.Faces.Count;

        var result = GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0.25f });

        Assert.Equal(before, result.TrianglesBefore);
        Assert.Equal(mesh.Faces.Count, result.TrianglesAfter);
        Assert.InRange(result.TrianglesAfter, before / 4 - 2, before / 4);
        Assert.True(result.Error > 0);
    }

    [Fact]
    public void NoTriangleFlipsOver()
    {
        var mesh = GeometryGenerator.Icosphere(1f, subdivisions: 4);
        float orientation = MathF.Sign(Float3.Dot(CornerNormal(mesh.Faces[0]), mesh.Faces[0].Center()));

        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0.05f });

        Assert.All(mesh.Faces, f => Assert.True(Float3.Dot(CornerNormal(f), f.Center()) * orientation > 0));
    }

    [Fact]
    public void KeepsOnlyOriginalVerticesWithTheirAttributes()
    {
        var mesh = GeometryGenerator.Icosphere(1f, subdivisions: 3);
        mesh.AddVertexAttribute("bone", GeometryData.AttributeBaseType.Float, 4);
        var original = new Dictionary<GeometryData.Vertex, float[]>();
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var value = new float[] { i, i * 2, i * 3, 1 };
            mesh.Vertices[i].Attributes["bone"] = new GeometryData.FloatAttributeValue(value);
            original[mesh.Vertices[i]] = value;
        }

        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0.25f });

        foreach (var v in mesh.Vertices)
        {
            Assert.True(original.ContainsKey(v));
            Assert.Equal(original[v], ((GeometryData.FloatAttributeValue)v.Attributes["bone"]).Data);
        }
        foreach (var face in mesh.Faces)
            Assert.All(face.NeighborVertices(), v => Assert.Contains(v, mesh.Vertices));
    }

    [Fact]
    public void LoopAttributesFollowTheirVertex()
    {
        var mesh = Grid(12);
        var options = new SimplifyOptions { TargetRatio = 0.25f };
        options.AttributeWeights["uv"] = 1f;

        GeometryOperators.Simplify(mesh, options);

        foreach (var face in mesh.Faces)
            foreach (var loop in LoopsOf(face))
                Assert.Equal(UvOf(loop.Vert.Point), UvOf(loop));
    }

    [Fact]
    public void UvSeamsSurvive()
    {
        var mesh = Grid(16, seamColumn: 8);
        float seamX = GridSize / 2;

        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0f, MaxError = 1e-3f });

        // Each side collapses to its own rectangle, and no triangle reaches across the seam
        Assert.Equal(4, mesh.Faces.Count);
        foreach (var face in mesh.Faces)
        {
            bool right = LoopsOf(face).Any(l => l.Vert.Point.X > seamX);
            foreach (var loop in LoopsOf(face))
            {
                Assert.True(right ? loop.Vert.Point.X >= seamX : loop.Vert.Point.X <= seamX);
                Assert.Equal(UvOf(loop.Vert.Point) + new Float2(right ? 1f : 0f, 0f), UvOf(loop));
            }
        }
    }

    [Fact]
    public void RegionOutlinesSurvive()
    {
        var mesh = Grid(16, seamColumn: 8, regions: true);
        float seamX = GridSize / 2;

        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0f, MaxError = 1e-3f });

        Assert.Equal(4, mesh.Faces.Count);
        foreach (var face in mesh.Faces)
        {
            int region = ((GeometryData.IntAttributeValue)face.Attributes["region"]).Data[0];
            Assert.All(face.NeighborVertices(), v => Assert.True(region == 1 ? v.Point.X >= seamX : v.Point.X <= seamX));
        }
    }

    [Fact]
    public void LockedBordersKeepEveryBorderVertex()
    {
        const int n = 10;
        var mesh = Grid(n);

        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0f, LockBorders = true });

        int border = mesh.Vertices.Count(v => v.Point.X == 0 || v.Point.X == GridSize || v.Point.Z == 0 || v.Point.Z == GridSize);
        Assert.Equal(n * 4, border);
    }

    /// <summary>Distance from p to the closest point of any face in the mesh.</summary>
    private static float DistanceToSurface(GeometryData mesh, Float3 p)
    {
        float best = float.MaxValue;
        foreach (var face in mesh.Faces)
        {
            var v = face.NeighborVertices();
            for (int i = 1; i + 1 < v.Count; i++)
                best = MathF.Min(best, Float3.Distance(p, ClosestOnTriangle(p, v[0].Point, v[i].Point, v[i + 1].Point)));
        }
        return best;
    }

    private static Float3 ClosestOnTriangle(Float3 p, Float3 a, Float3 b, Float3 c)
    {
        Float3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Float3.Dot(ab, ap), d2 = Float3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        Float3 bp = p - b;
        float d3 = Float3.Dot(ab, bp), d4 = Float3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        Float3 cp = p - c;
        float d5 = Float3.Dot(ab, cp), d6 = Float3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denominator = 1f / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }

    [Fact]
    public void RealDeviationStaysNearTheLimit()
    {
        var mesh = GeometryGenerator.Icosphere(1f, subdivisions: 4);
        var original = mesh.Vertices.Select(v => v.Point).ToList();

        var result = GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0f, MaxError = 0.01f });

        Assert.True(result.Error <= 0.01f);
        Assert.True(mesh.Faces.Count < 1000);
        float worst = original.Max(p => DistanceToSurface(mesh, p));
        Assert.True(worst <= 0.02f, $"Source vertices end up {worst} from the surface");
    }

    [Fact]
    public void ASpikeNextToHugeFlatAreasOutlivesATightLimit()
    {
        // A spike on a small ring whose vertices also carry enormous flat triangles. Judging the spike's
        // collapse by both vertices' planes together would let that flat weight dilute how far the spike
        // moves to almost nothing, and the spike would go well past the limit.
        var mesh = new GeometryData();
        var tip = mesh.AddVertex(0, 1, 0);
        const int count = 8;
        var ring = new GeometryData.Vertex[count];
        var outer = new GeometryData.Vertex[count];
        for (int i = 0; i < count; i++)
        {
            float angle = i * MathF.PI * 2 / count;
            ring[i] = mesh.AddVertex(MathF.Cos(angle), 0, MathF.Sin(angle));
            outer[i] = mesh.AddVertex(MathF.Cos(angle) * 100, 0, MathF.Sin(angle) * 100);
        }
        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count;
            mesh.AddFace(tip, ring[j], ring[i]);
            mesh.AddFace(ring[i], ring[j], outer[j]);
            mesh.AddFace(ring[i], outer[j], outer[i]);
        }

        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0f, MaxError = 0.25f });

        Assert.Contains(tip, mesh.Vertices);
    }

    [Fact]
    public void NegativeMaxErrorOnlyAllowsCollapsesThatKeepTheSurface()
    {
        var sphere = GeometryGenerator.Icosphere(1f, subdivisions: 2);
        int before = sphere.Faces.Count;
        GeometryOperators.Simplify(sphere, new SimplifyOptions { TargetRatio = 0f, MaxError = -1f });
        Assert.Equal(before, sphere.Faces.Count);

        var flat = Grid(6);
        GeometryOperators.Simplify(flat, new SimplifyOptions { TargetRatio = 0f, MaxError = -1f });
        Assert.Equal(2, flat.Faces.Count);
    }

    [Fact]
    public void TargetTriangleCountIsExact()
    {
        var mesh = GeometryGenerator.Icosphere(1f, subdivisions: 3);

        var result = GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetTriangleCount = 500 });

        Assert.Equal(500, result.TrianglesAfter);
        Assert.Equal(500, mesh.Faces.Count);
    }

    [Fact]
    public void FineDetailInALargeMeshStillSimplifies()
    {
        // A tiny sphere far from a lone point, so its triangles are minute relative to the whole extent
        var mesh = GeometryGenerator.Icosphere(0.01f, subdivisions: 3);
        mesh.AddVertex(100f, 0f, 0f);
        int before = mesh.Faces.Count;

        var result = GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0.25f });

        Assert.Equal(before / 4, result.TrianglesAfter);
    }

    [Fact]
    public void OtherAttributesKeepTheirValuesWhereNothingCollapsed()
    {
        // Normals are not a seam attribute here, so their hard edge at column 2 is not protected. The
        // vertices around it are locked, and their corners must still come back exactly as authored.
        const int n = 8;
        var mesh = Grid(n);
        mesh.AddLoopAttribute("normal", GeometryData.AttributeBaseType.Float, 3);
        mesh.AddVertexAttribute("lock", GeometryData.AttributeBaseType.Int, 1);
        float column = 2 * GridSize / n;
        foreach (var v in mesh.Vertices)
            v.Attributes["lock"] = new GeometryData.IntAttributeValue(v.Point.X <= 3 * GridSize / n ? 1 : 0);
        foreach (var face in mesh.Faces)
        {
            bool right = face.Center().X > column;
            foreach (var loop in LoopsOf(face))
                loop.Attributes["normal"] = new GeometryData.FloatAttributeValue(right ? 1f : 0f, right ? 0f : 1f, 0f);
        }

        GeometryOperators.Simplify(mesh, new SimplifyOptions
        {
            TargetRatio = 0.3f,
            SeamAttributes = new HashSet<string> { "uv" },
            LockAttribute = "lock",
        });

        foreach (var face in mesh.Faces)
        {
            if (face.NeighborVertices().Any(v => v.Point.X > 3 * GridSize / n)) continue;
            bool right = face.Center().X > column;
            foreach (var loop in LoopsOf(face))
                Assert.Equal(right ? 1f : 0f, ((GeometryData.FloatAttributeValue)loop.Attributes["normal"]).Data[0]);
        }
    }

    [Fact]
    public void WireEdgesKeepBothEnds()
    {
        var mesh = Grid(6);
        var interior = mesh.Vertices.First(v => v.Point.X == GridSize / 2 && v.Point.Z == GridSize / 2);
        var outside = mesh.AddVertex(GridSize * 2, 0, 0);
        mesh.AddEdge(interior, outside);

        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0f, MaxError = 1e-3f });

        Assert.Contains(interior, mesh.Vertices);
        Assert.Contains(outside, mesh.Vertices);
        Assert.NotNull(mesh.FindEdge(interior, outside));
        Assert.All(mesh.Edges, e => Assert.True(mesh.Vertices.Contains(e.Vert1) && mesh.Vertices.Contains(e.Vert2)));
    }

    [Fact]
    public void SmallClosedPartsSurvive()
    {
        var tetrahedron = GeometryGenerator.Tetrahedron(1f);
        GeometryOperators.Simplify(tetrahedron, new SimplifyOptions { TargetRatio = 0f });
        Assert.Equal(4, tetrahedron.Faces.Count);

        var triangle = TestUtil.MakeTriangle();
        GeometryOperators.Simplify(triangle, new SimplifyOptions { TargetRatio = 0f });
        Assert.Single(triangle.Faces);
    }

    [Fact]
    public void PinchedVertexStaysPut()
    {
        // Two octahedra touching at one shared vertex, a point where two closed fans meet
        var mesh = new GeometryData();
        var pinch = mesh.AddVertex(0, 0, 0);
        AddOctahedron(mesh, pinch, 1f);
        AddOctahedron(mesh, pinch, -1f);

        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0f });

        Assert.Contains(pinch, mesh.Vertices);
        Assert.Contains(mesh.Faces, f => f.Center().Y > 0);
        Assert.Contains(mesh.Faces, f => f.Center().Y < 0);
    }

    /// <summary>An octahedron above or below <paramref name="tip"/>, using it as one of its points.</summary>
    private static void AddOctahedron(GeometryData mesh, GeometryData.Vertex tip, float side)
    {
        var far = mesh.AddVertex(0, 2 * side, 0);
        var ring = new[]
        {
            mesh.AddVertex(1, side, 0), mesh.AddVertex(0, side, 1),
            mesh.AddVertex(-1, side, 0), mesh.AddVertex(0, side, -1),
        };
        for (int i = 0; i < 4; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % 4];
            if (side > 0)
            {
                mesh.AddFace(tip, a, b);
                mesh.AddFace(far, b, a);
            }
            else
            {
                mesh.AddFace(tip, b, a);
                mesh.AddFace(far, a, b);
            }
        }
    }

    [Fact]
    public void SubdividedBoxCollapsesToItsCorners()
    {
        var mesh = GeometryGenerator.Box(new Float3(2f, 2f, 2f), segments: new Int3(4, 4, 4));

        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0f, MaxError = 1e-4f });

        Assert.Equal(12, mesh.Faces.Count);
        Assert.Equal(8, mesh.Vertices.Count);
    }

    [Fact]
    public void QuadsStayQuadsWhenNothingCollapses()
    {
        var mesh = GeometryGenerator.Plane(4f, 4);

        var result = GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 1f });

        Assert.Equal(16, result.TrianglesAfter);
        Assert.All(mesh.Faces, f => Assert.Equal(4, f.VertCount));
    }

    [Fact]
    public void QuadsComeBackAsTrianglesWhenSimplified()
    {
        var mesh = GeometryGenerator.Plane(4f, 4);

        var result = GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0.5f });

        Assert.Equal(32, result.TrianglesBefore);
        Assert.InRange(result.TrianglesAfter, 15, 16);
        Assert.All(mesh.Faces, f => Assert.Equal(3, f.VertCount));
    }

    [Fact]
    public void LargeQuadMeshesSimplifyQuickly()
    {
        var mesh = GeometryGenerator.Plane(100f, 300);
        var timer = Stopwatch.StartNew();

        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0.5f });

        Assert.True(timer.Elapsed.TotalSeconds < 10, $"Took {timer.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public void SameInputGivesTheSameResult()
    {
        static List<Float3> Run()
        {
            var mesh = GeometryGenerator.Icosphere(1f, subdivisions: 3);
            GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = 0.3f });
            return mesh.Faces.SelectMany(f => f.NeighborVertices()).Select(v => v.Point).ToList();
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void PlaneSpreadsItsVerticesAcrossTheGrid()
    {
        var mesh = GeometryGenerator.Plane(4f, 4);

        Assert.Equal(25, mesh.Vertices.Select(v => v.Point).Distinct().Count());
    }
}
