// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Runtime.MeshFeatures;
using Prowl.Runtime.MeshFeatures.Generation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;
using Xunit.Abstractions;

namespace Prowl.Runtime.Test;

public class SDFGeneratorTests(ITestOutputHelper output)
{
    private static Float3 Centre(Float3 origin, float cell, int x, int y, int z)
        => origin + new Float3(x + 0.5f, y + 0.5f, z + 0.5f) * cell;

    // Closest point on a triangle by its Voronoi regions, independent of how the generator measures
    private static Float3 ClosestPoint(Float3 p, Float3 a, Float3 b, Float3 c)
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
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

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
            Float3 d = p - ClosestPoint(p, a, b, c);
            best = MathF.Min(best, Float3.Dot(d, d));
        }
        return MathF.Sqrt(best);
    }

    private void Check(Mesh mesh, Func<Float3, bool> inside, int res, float extent)
        => Check(mesh, inside, new Int3(res, res, res), new Float3(-extent), 2f * extent / res);

    private void Check(Mesh mesh, Func<Float3, bool> inside, Int3 size, Float3 origin, float cell)
    {
        var surface = SDFGenerator.Surface.From(mesh)!;
        float[] field = SDFGenerator.ComputeDistances(surface, origin, cell, size);

        // Away from the surface a voxel can miss its true closest triangle for one a little further, so a distance may
        // be slightly too long, never too short. Within a cell of the surface every distance is exact
        float worst = 0f, worstNear = 0f, under = 0f;
        int wrongSign = 0, off = 0;
        for (int z = 0; z < size.Z; z++)
            for (int y = 0; y < size.Y; y++)
                for (int x = 0; x < size.X; x++)
                {
                    Float3 p = Centre(origin, cell, x, y, z);
                    float value = field[(z * size.Y + y) * size.X + x];
                    float exact = BruteDistance(mesh, p);
                    float error = MathF.Abs(value) - exact;
                    worst = MathF.Max(worst, error);
                    under = MathF.Min(under, error);
                    if (exact < cell) worstNear = MathF.Max(worstNear, MathF.Abs(error));
                    if (error > 1e-4f) off++;
                    if (MathF.Abs(value) > 1e-3f && (value < 0f) != inside(p)) wrongSign++;
                }

        output.WriteLine($"{size.X}x{size.Y}x{size.Z}: worst error {worst / cell:F3} cells, near the surface {worstNear}, {off} voxels off, {wrongSign} wrong signs");
        Assert.Equal(0, wrongSign);
        Assert.True(worstNear < 1e-4f, $"a voxel within a cell of the surface is off by {worstNear}");
        Assert.True(under > -1e-4f, $"a distance is {-under} shorter than the true one");
        Assert.True(worst < cell * 0.25f, $"worst error {worst / cell} cells is more than a quarter of a cell");
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
        Check(both, Inside, new Int3(36, 25, 26), new Float3(-1.83f, -1.27f, -1.02f), 0.1f);
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
        Float3 origin = new(-1.2f);
        float cell = 2.4f / res;
        float[] field = SDFGenerator.ComputeDistances(surface, origin, cell, new Int3(res, res, res));
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
        float[] field = SDFGenerator.ComputeDistances(surface, new Float3(-1.5f), 3f / 20, new Int3(20, 20, 20));
        Assert.DoesNotContain(field, value => value < 0f);
    }

    private static SDFGenerator.Options Voxels(float size, int max = 128) => new() { VoxelSize = size, MaxResolution = max };

    [Fact]
    public void Layout_UsesTheVoxelSize_PerAxis_PaddedAndCentredOnTheMesh()
    {
        Assert.True(SDFGenerator.Layout(new AABB(new Float3(-1f, -0.5f, 0f), new Float3(1f, 0.5f, 0.5f)), Voxels(0.1f),
            out AABB bounds, out float cell, out Int3 resolution));
        Assert.Equal(0.1f, cell, 5);
        int pad = 2 * SDFGenerator.PaddingVoxels;
        Assert.Equal(new Int3(20 + pad, 10 + pad, 5 + pad), resolution);
        Float3 centre = (bounds.Min + bounds.Max) * 0.5f;
        Assert.Equal(0.25f, centre.Z, 4);
        Assert.Equal(resolution.X * cell, bounds.Max.X - bounds.Min.X, 4);
    }

    [Fact]
    public void Layout_GrowsVoxels_ToStayWithinTheMaxResolution()
    {
        SDFGenerator.Layout(new AABB(Float3.Zero, new Float3(100f, 10f, 1f)), Voxels(0.1f, 64), out _, out float cell, out Int3 resolution);
        Assert.Equal(64, resolution.X);
        Assert.Equal(100f / (64 - 2 * SDFGenerator.PaddingVoxels), cell, 4);
    }

    [Fact]
    public void Layout_ShrinksVoxels_ToKeepTheMinResolution_OnASmallMesh()
    {
        SDFGenerator.Layout(new AABB(Float3.Zero, new Float3(0.05f, 0.01f, 0.02f)), Voxels(0.1f), out _, out _, out Int3 resolution);
        Assert.Equal(SDFGenerator.MinResolution, resolution.X);
    }

    [Fact]
    public void Layout_RefusesAMeshWithNoExtent()
        => Assert.False(SDFGenerator.Layout(new AABB(Float3.One, Float3.One), Voxels(0.1f), out _, out _, out _));

    [Fact]
    public void ChangingTheProjectSettings_ChangesTheImporterVersion_SoEveryMeshReimports()
    {
        bool enabled = SDFFeatureSpec.Enabled;
        SDFGenerator.Options options = SDFFeatureSpec.Options;
        try
        {
            SDFFeatureSpec.Enabled = true;
            SDFFeatureSpec.Options = SDFGenerator.Options.Default;
            int baseline = MeshFeatureRegistry.AggregateVersion;

            SDFFeatureSpec.Options = Voxels(0.05f);
            Assert.NotEqual(baseline, MeshFeatureRegistry.AggregateVersion);
            SDFFeatureSpec.Options = Voxels(0.1f, 64);
            Assert.NotEqual(baseline, MeshFeatureRegistry.AggregateVersion);
            SDFFeatureSpec.Options = SDFGenerator.Options.Default;
            SDFFeatureSpec.Enabled = false;
            Assert.NotEqual(baseline, MeshFeatureRegistry.AggregateVersion);

            SDFFeatureSpec.Enabled = true;
            Assert.Equal(baseline, MeshFeatureRegistry.AggregateVersion);
        }
        finally
        {
            SDFFeatureSpec.Enabled = enabled;
            SDFFeatureSpec.Options = options;
        }
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
