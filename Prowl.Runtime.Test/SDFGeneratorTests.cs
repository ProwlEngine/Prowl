// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Runtime.MeshFeatures.Generation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;
using Xunit.Abstractions;

namespace Prowl.Runtime.Test;

public class SDFGeneratorTests(ITestOutputHelper output)
{
    private static Float3 Centre(Float3 origin, Float3 cell, int x, int y, int z)
        => new(origin.X + (x + 0.5f) * cell.X, origin.Y + (y + 0.5f) * cell.Y, origin.Z + (z + 0.5f) * cell.Z);

    // Every voxel against every triangle that has an area
    private static float BruteDistance(Mesh mesh, Float3 p)
    {
        float best = float.MaxValue;
        uint[] indices = mesh.Indices!;
        Float3[] vertices = mesh.Vertices!;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            Float3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
            if (Float3.LengthSquared(Float3.Cross(b - a, c - a)) < 1e-20f) continue;
            Float3 d = p - SDFGenerator.ClosestPoint(p, a, b, c);
            best = MathF.Min(best, Float3.Dot(d, d));
        }
        return MathF.Sqrt(best);
    }

    private void Check(Mesh mesh, Func<Float3, bool> inside, int res, float extent)
    {
        var surface = SDFGenerator.Surface.From(mesh)!;
        Float3 origin = new(-extent), cell = new(2f * extent / res);
        float maxDistance = 100f;
        float[] field = SDFGenerator.ComputeDistances(surface, origin, cell, res, maxDistance);

        // Away from the surface a voxel can miss its true closest triangle for one a little further, so a distance may
        // be slightly too long, never too short. Within a cell of the surface every distance is exact
        float worst = 0f, worstNear = 0f, under = 0f;
        int wrongSign = 0, off = 0;
        for (int z = 0; z < res; z++)
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    Float3 p = Centre(origin, cell, x, y, z);
                    float value = field[(z * res + y) * res + x];
                    float exact = BruteDistance(mesh, p);
                    float error = MathF.Abs(value) - exact;
                    worst = MathF.Max(worst, error);
                    under = MathF.Min(under, error);
                    if (exact < cell.X) worstNear = MathF.Max(worstNear, MathF.Abs(error));
                    if (error > 1e-4f) off++;
                    if (MathF.Abs(value) > 1e-3f && (value < 0f) != inside(p)) wrongSign++;
                }

        output.WriteLine($"{res}^3: worst error {worst / cell.X:F3} cells, near the surface {worstNear}, {off} voxels off, {wrongSign} wrong signs");
        Assert.Equal(0, wrongSign);
        Assert.True(worstNear < 1e-4f, $"a voxel within a cell of the surface is off by {worstNear}");
        Assert.True(under > -1e-4f, $"a distance is {-under} shorter than the true one");
        Assert.True(worst < cell.X * 0.25f, $"worst error {worst / cell.X} cells is more than a quarter of a cell");
    }

    [Fact]
    public void Cube_MatchesTheExactField_AndIsNegativeInside()
        => Check(Mesh.CreateCube(Float3.One), p => MathF.Abs(p.X) < 0.5f && MathF.Abs(p.Y) < 0.5f && MathF.Abs(p.Z) < 0.5f, 24, 1.2f);

    [Fact]
    public void Sphere_MatchesTheExactField_AndIsNegativeInside()
    {
        Mesh sphere = Mesh.CreateSphere(1f, 16, 24);
        Check(sphere, p => InsideMesh(sphere, p), 24, 1.5f);
    }

    [Fact]
    public void TwoSeparateBoxes_MatchTheExactField_AcrossTheGapBetweenThem()
    {
        Mesh left = Mesh.CreateCube(new Float3(1f, 2f, 1f));
        Mesh right = Mesh.CreateCube(new Float3(1f, 0.6f, 2f));
        var vertices = left.Vertices!.Select(v => v + new Float3(-1f, 0f, 0f))
            .Concat(right.Vertices!.Select(v => v + new Float3(1f, -0.5f, 0.3f))).ToArray();
        var indices = left.Indices!.Concat(right.Indices!.Select(i => i + (uint)left.Vertices!.Length)).ToArray();
        var both = new Mesh { Vertices = vertices, Indices = indices };

        bool Inside(Float3 p) =>
            (MathF.Abs(p.X + 1f) < 0.5f && MathF.Abs(p.Y) < 1f && MathF.Abs(p.Z) < 0.5f)
            || (MathF.Abs(p.X - 1f) < 0.5f && MathF.Abs(p.Y + 0.5f) < 0.3f && MathF.Abs(p.Z - 0.3f) < 1f);
        Check(both, Inside, 28, 2.2f);
    }

    private static bool InsideUnitCube(Float3 p) => MathF.Abs(p.X) < 0.5f && MathF.Abs(p.Y) < 0.5f && MathF.Abs(p.Z) < 0.5f;

    [Fact]
    public void FlippedTriangles_ChangeNothing()
    {
        Mesh cube = Mesh.CreateCube(Float3.One);
        uint[] indices = cube.Indices!.ToArray();
        for (int i = 0; i + 2 < indices.Length; i += 6)
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        Check(new Mesh { Vertices = cube.Vertices, Indices = indices }, InsideUnitCube, 24, 1.2f);
    }

    [Fact]
    public void AMissingFace_IsOutvotedByTheOtherAxes()
    {
        Mesh cube = Mesh.CreateCube(Float3.One);
        Float3[] v = cube.Vertices!;
        uint[] indices = cube.Indices!;
        var kept = new System.Collections.Generic.List<uint>();
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            bool top = v[indices[i]].Y > 0.49f && v[indices[i + 1]].Y > 0.49f && v[indices[i + 2]].Y > 0.49f;
            if (!top) kept.AddRange([indices[i], indices[i + 1], indices[i + 2]]);
        }
        var open = new Mesh { Vertices = v, Indices = kept.ToArray() };

        var surface = SDFGenerator.Surface.From(open)!;
        int res = 24;
        Float3 origin = new(-1.2f), cell = new(2.4f / res);
        float[] field = SDFGenerator.ComputeDistances(surface, origin, cell, res, 100f);
        int wrong = 0;
        for (int z = 0; z < res; z++)
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float value = field[(z * res + y) * res + x];
                    if (MathF.Abs(value) > 1e-3f && (value < 0f) != InsideUnitCube(Centre(origin, cell, x, y, z))) wrong++;
                }
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void ALoneOneSidedQuad_HasNoInside()
    {
        var quad = new Mesh
        {
            Vertices = [new(-1f, 0f, -1f), new(1f, 0f, -1f), new(1f, 0f, 1f), new(-1f, 0f, 1f)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
        var surface = SDFGenerator.Surface.From(quad)!;
        float[] field = SDFGenerator.ComputeDistances(surface, new Float3(-1.5f), new Float3(3f / 20), 20, 100f);
        Assert.DoesNotContain(field, value => value < 0f);
    }

    // Parity of crossings along +X, for the sphere whose inside has no simple formula at its facets
    private static bool InsideMesh(Mesh mesh, Float3 p)
    {
        uint[] indices = mesh.Indices!;
        Float3[] v = mesh.Vertices!;
        int crossings = 0;
        Float3 dir = Float3.Normalize(new Float3(1f, 0.0123f, 0.0071f));
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            Float3 a = v[indices[i]], b = v[indices[i + 1]], c = v[indices[i + 2]];
            Float3 e1 = b - a, e2 = c - a, h = Float3.Cross(dir, e2);
            float det = Float3.Dot(e1, h);
            if (MathF.Abs(det) < 1e-9f) continue;
            float f = 1f / det;
            Float3 s = p - a;
            float u = f * Float3.Dot(s, h);
            if (u < 0f || u > 1f) continue;
            Float3 q = Float3.Cross(s, e1);
            float w = f * Float3.Dot(dir, q);
            if (w < 0f || u + w > 1f) continue;
            if (f * Float3.Dot(e2, q) > 0f) crossings++;
        }
        return (crossings & 1) == 1;
    }
}
