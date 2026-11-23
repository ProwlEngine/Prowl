// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class UVTests
{
    /// <summary>A flat grid of quads in the XZ plane, one unit each.</summary>
    private static GeometryData Grid(int columns, int rows)
    {
        var mesh = new GeometryData();
        for (int z = 0; z <= rows; z++)
            for (int x = 0; x <= columns; x++)
                mesh.AddVertex(x, 0, z);
        int stride = columns + 1;
        for (int z = 0; z < rows; z++)
            for (int x = 0; x < columns; x++)
                mesh.AddFace(z * stride + x, (z + 1) * stride + x, (z + 1) * stride + x + 1, z * stride + x + 1);
        return mesh;
    }

    private static Float2 UVOf(GeometryData.Loop loop)
        => loop.TryGetAttribute(MeshGeometry.UV, out GeometryData.AttributeValue? value) && value is GeometryData.FloatAttributeValue uv ? new Float2(uv.Data[0], uv.Data[1]) : Float2.Zero;

    private static IEnumerable<GeometryData.Loop> Corners(GeometryData.Face face)
    {
        GeometryData.Loop loop = face.Loop!;
        do
        {
            yield return loop;
            loop = loop.Next!;
        } while (loop != face.Loop);
    }

    private static float UVArea(GeometryData.Face face)
    {
        List<Float2> uvs = [.. Corners(face).Select(UVOf)];
        float area = 0f;
        for (int i = 0; i < uvs.Count; i++)
        {
            Float2 a = uvs[i], b = uvs[(i + 1) % uvs.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area * 0.5f;
    }

    [Fact]
    public void UnwrappingAFlatGridLaysItOutWholeWithoutStretch()
    {
        GeometryData mesh = Grid(3, 2);

        UVs.Unwrap(mesh, [.. mesh.Faces]);

        foreach (GeometryData.Vertex vertex in mesh.Vertices)
        {
            List<Float2> at = [.. mesh.Faces.SelectMany(Corners).Where(l => l.Vert == vertex).Select(UVOf)];
            Assert.All(at, uv => Assert.True(Float2.Distance(uv, at[0]) < 1e-4f));
        }
        Assert.All(mesh.Faces.SelectMany(Corners).Select(UVOf), uv => Assert.True(uv.X >= -1e-4f && uv.X <= 1.0001f && uv.Y >= -1e-4f && uv.Y <= 1.0001f));

        float first = MathF.Abs(UVArea(mesh.Faces[0]));
        Assert.True(first > 1e-3f);
        Assert.All(mesh.Faces, f => Assert.True(MathF.Abs(MathF.Abs(UVArea(f)) - first) < first * 0.01f));
        Assert.All(mesh.Faces, f => Assert.True(UVArea(f) * UVArea(mesh.Faces[0]) > 0f));
    }

    [Fact]
    public void SeamsSplitTheLayoutIntoIslandsThatDoNotOverlap()
    {
        GeometryData cube = Primitives.Make(PrimitiveKind.Cube);
        UVs.SetSeams(cube, [.. cube.Edges], true);

        UVs.Unwrap(cube, [.. cube.Faces]);

        Assert.Equal(6, UVs.Islands(cube, [.. cube.Faces]).Count);
        List<(Float2 Min, Float2 Max)> bounds = [.. cube.Faces.Select(f =>
        {
            List<Float2> uvs = [.. Corners(f).Select(UVOf)];
            return (new Float2(uvs.Min(u => u.X), uvs.Min(u => u.Y)), new Float2(uvs.Max(u => u.X), uvs.Max(u => u.Y)));
        })];
        for (int i = 0; i < bounds.Count; i++)
        {
            Assert.True(bounds[i].Max.X - bounds[i].Min.X > 1e-3f);
            for (int j = i + 1; j < bounds.Count; j++)
            {
                float overlapX = MathF.Min(bounds[i].Max.X, bounds[j].Max.X) - MathF.Max(bounds[i].Min.X, bounds[j].Min.X);
                float overlapY = MathF.Min(bounds[i].Max.Y, bounds[j].Max.Y) - MathF.Max(bounds[i].Min.Y, bounds[j].Min.Y);
                Assert.False(overlapX > 1e-4f && overlapY > 1e-4f, $"faces {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void AClosedMeshWithoutSeamsStillUnwrapsEveryFace()
    {
        GeometryData cube = Primitives.Make(PrimitiveKind.Cube);

        UVs.Unwrap(cube, [.. cube.Faces]);

        Assert.All(cube.Faces, f => Assert.True(MathF.Abs(UVArea(f)) > 1e-4f));
    }

    [Fact]
    public void PinnedPointsKeepTheirPlace()
    {
        GeometryData mesh = Grid(2, 1);
        UVs.Unwrap(mesh, [.. mesh.Faces]);
        GeometryData.Vertex a = mesh.Vertices[0], b = mesh.Vertices[2];
        UVs.MoveUVs(mesh, [.. mesh.Faces.SelectMany(Corners).Where(l => l.Vert == a)], new Float2(0.1f, 0.1f), absolute: true);
        UVs.MoveUVs(mesh, [.. mesh.Faces.SelectMany(Corners).Where(l => l.Vert == b)], new Float2(0.9f, 0.1f), absolute: true);
        UVs.SetPinned([.. mesh.Faces.SelectMany(Corners).Where(l => l.Vert == a || l.Vert == b)], true);

        UVs.Unwrap(mesh, [.. mesh.Faces]);

        Assert.All(mesh.Faces.SelectMany(Corners).Where(l => l.Vert == a), l => Assert.True(Float2.Distance(UVOf(l), new Float2(0.1f, 0.1f)) < 1e-4f));
        Assert.All(mesh.Faces.SelectMany(Corners).Where(l => l.Vert == b), l => Assert.True(Float2.Distance(UVOf(l), new Float2(0.9f, 0.1f)) < 1e-4f));
    }

    [Fact]
    public void SeamsComeFromMaterialBordersAndFromTheCurrentIslands()
    {
        GeometryData mesh = Grid(2, 1);
        mesh.Faces[1].SetAttribute(MeshGeometry.SubMesh, new GeometryData.IntAttributeValue(1));

        UVs.SeamsFromMaterials(mesh);

        GeometryData.Edge middle = mesh.FindEdge(mesh.Vertices[1], mesh.Vertices[4])!;
        Assert.True(UVs.IsSeam(middle));
        Assert.Single(mesh.Edges, UVs.IsSeam);

        UVs.SetSeams(mesh, [.. mesh.Edges], false);
        UVs.Reset(mesh, [.. mesh.Faces]);
        UVs.SeamsFromIslands(mesh);

        Assert.True(UVs.IsSeam(middle));
    }

    [Fact]
    public void ResetGivesEachQuadTheWholeSquare()
    {
        GeometryData mesh = Grid(1, 1);

        UVs.Reset(mesh, [.. mesh.Faces]);

        List<Float2> uvs = [.. Corners(mesh.Faces[0]).Select(UVOf)];
        Assert.Contains(uvs, u => Float2.Distance(u, new Float2(0, 0)) < 1e-5f);
        Assert.Contains(uvs, u => Float2.Distance(u, new Float2(1, 0)) < 1e-5f);
        Assert.Contains(uvs, u => Float2.Distance(u, new Float2(1, 1)) < 1e-5f);
        Assert.Contains(uvs, u => Float2.Distance(u, new Float2(0, 1)) < 1e-5f);
        Assert.True(UVArea(mesh.Faces[0]) > 0f);
    }

    [Fact]
    public void PackingFitsIslandsIntoTheSquareWithoutOverlap()
    {
        GeometryData cube = Primitives.Make(PrimitiveKind.Cube);
        UVs.SetSeams(cube, [.. cube.Edges], true);
        UVs.Unwrap(cube, [.. cube.Faces]);
        UVs.Transform(cube, [.. cube.Faces.SelectMany(Corners)], uv => uv * 5f + new Float2(3f, -2f));

        UVs.Pack(cube, [.. cube.Faces]);

        Assert.All(cube.Faces.SelectMany(Corners).Select(UVOf), uv => Assert.True(uv.X >= -1e-4f && uv.X <= 1.0001f && uv.Y >= -1e-4f && uv.Y <= 1.0001f));
        Assert.Equal(6, UVs.Islands(cube, [.. cube.Faces]).Count);
        float area = cube.Faces.Sum(f => MathF.Abs(UVArea(f)));
        Assert.True(area > 0.2f, $"packed area {area}");
    }

    [Fact]
    public void AveragingIslandScaleGivesEveryIslandTheSameTexelDensity()
    {
        GeometryData mesh = Grid(2, 1);
        UVs.SetSeams(mesh, [mesh.FindEdge(mesh.Vertices[1], mesh.Vertices[4])!], true);
        UVs.Unwrap(mesh, [.. mesh.Faces]);
        UVs.Transform(mesh, [.. Corners(mesh.Faces[1])], uv => uv * 3f);

        UVs.AverageIslandScale(mesh, [.. mesh.Faces]);

        float a = MathF.Abs(UVArea(mesh.Faces[0])), b = MathF.Abs(UVArea(mesh.Faces[1]));
        Assert.True(MathF.Abs(a - b) < a * 0.01f, $"{a} and {b}");
    }

    [Fact]
    public void AligningFlippingAndWeldingMoveOnlyTheGivenPoints()
    {
        GeometryData mesh = Grid(1, 1);
        UVs.Reset(mesh, [.. mesh.Faces]);
        List<GeometryData.Loop> corners = [.. Corners(mesh.Faces[0])];

        UVs.Align(mesh, [corners[0], corners[1]], 1);
        Assert.True(MathF.Abs(UVOf(corners[0]).Y - UVOf(corners[1]).Y) < 1e-6f);

        UVs.Flip(mesh, corners, 0);
        Assert.Equal(1f, UVOf(corners[0]).X, 4);

        UVs.Weld(mesh, [corners[2], corners[3]]);
        Assert.True(Float2.Distance(UVOf(corners[2]), UVOf(corners[3])) < 1e-6f);
        Assert.True(Float2.Distance(UVOf(corners[2]), new Float2(0.5f, 1f)) < 1e-4f);
    }

    [Fact]
    public void SnappingToPixelsRoundsToTheTexelGrid()
    {
        GeometryData mesh = Grid(1, 1);
        UVs.Reset(mesh, [.. mesh.Faces]);
        List<GeometryData.Loop> corners = [.. Corners(mesh.Faces[0])];
        UVs.MoveUVs(mesh, [corners[2]], new Float2(0.123f, 0.456f), absolute: true);

        UVs.SnapToPixels(mesh, [corners[2]], 10, 4);

        Assert.True(Float2.Distance(UVOf(corners[2]), new Float2(0.1f, 0.5f)) < 1e-5f);
    }

    [Fact]
    public void TheLayoutImageDrawsFaceEdgesOnAClearBackground()
    {
        GeometryData mesh = Grid(1, 1);
        UVs.Reset(mesh, [.. mesh.Faces]);
        UVs.Transform(mesh, [.. Corners(mesh.Faces[0])], uv => uv * 0.5f + new Float2(0.25f));

        byte[] image = UVs.LayoutImage([.. mesh.Faces], 64, 64);

        byte Alpha(int x, int y) => image[(y * 64 + x) * 4 + 3];
        Assert.Equal(64 * 64 * 4, image.Length);
        Assert.Equal(255, Alpha(16, 32));
        Assert.Equal(255, Alpha(32, 16));
        Assert.Equal(0, Alpha(32, 32));
        Assert.Equal(0, Alpha(4, 4));
    }

    [Fact]
    public void StitchingJoinsTheUVsOfTheFacesAcrossAnEdge()
    {
        GeometryData mesh = Grid(2, 1);
        UVs.Reset(mesh, [.. mesh.Faces]);
        GeometryData.Edge middle = mesh.FindEdge(mesh.Vertices[1], mesh.Vertices[4])!;
        Assert.Equal(2, UVs.Islands(mesh, [.. mesh.Faces]).Count);

        UVs.Stitch(mesh, [middle], [.. mesh.Faces]);

        Assert.Single(UVs.Islands(mesh, [.. mesh.Faces]));
    }

    [Fact]
    public void StraighteningAndDistributingLineThePointsUpEvenly()
    {
        GeometryData mesh = Grid(3, 1);
        UVs.Unwrap(mesh, [.. mesh.Faces]);
        List<GeometryData.Vertex> row = [mesh.Vertices[0], mesh.Vertices[1], mesh.Vertices[2], mesh.Vertices[3]];
        List<GeometryData.Loop> corners = [.. mesh.Faces.SelectMany(Corners).Where(l => row.Contains(l.Vert))];
        UVs.Transform(mesh, corners.Where(l => l.Vert == row[1]), uv => uv + new Float2(0.01f, 0.05f));

        UVs.Straighten(mesh, corners);
        UVs.Distribute(mesh, corners);

        Float2 a = UVOf(corners.First(l => l.Vert == row[0])), d = UVOf(corners.First(l => l.Vert == row[3]));
        for (int i = 0; i < 4; i++)
        {
            Float2 expected = a + (d - a) * (i / 3f);
            Assert.All(corners.Where(l => l.Vert == row[i]), l => Assert.True(Float2.Distance(UVOf(l), expected) < 1e-4f, $"point {i}"));
        }
    }

    [Fact]
    public void RelaxingPullsAPointTowardItsNeighbours()
    {
        GeometryData mesh = Grid(2, 2);
        UVs.Unwrap(mesh, [.. mesh.Faces]);
        GeometryData.Vertex middle = mesh.Vertices[4];
        List<GeometryData.Loop> at = [.. mesh.Faces.SelectMany(Corners).Where(l => l.Vert == middle)];
        Float2 home = UVOf(at[0]);
        UVs.Transform(mesh, at, uv => uv + new Float2(0.1f, 0f));

        UVs.Relax(mesh, at, [.. mesh.Faces], 1f);

        Assert.All(at, l => Assert.True(Float2.Distance(UVOf(l), home) < 0.02f));
    }

    [Fact]
    public void OverlappingFacesAreFound()
    {
        GeometryData mesh = Grid(2, 1);
        UVs.Reset(mesh, [.. mesh.Faces]);
        Assert.Equal(2, UVs.OverlappingFaces([.. mesh.Faces]).Count);

        UVs.Transform(mesh, [.. Corners(mesh.Faces[1])], uv => uv + new Float2(2f, 0f));
        Assert.Empty(UVs.OverlappingFaces([.. mesh.Faces]));
    }

    [Fact]
    public void ProjectingByAngleGivesACubeAnIslandPerSideWithoutOverlap()
    {
        GeometryData cube = Primitives.Make(PrimitiveKind.Cube);

        UVs.ByAngle(cube, [.. cube.Faces], 60f);

        Assert.Equal(6, UVs.Islands(cube, [.. cube.Faces]).Count);
        Assert.Empty(UVs.OverlappingFaces([.. cube.Faces]));
        Assert.All(cube.Faces, f => Assert.True(MathF.Abs(UVArea(f)) > 1e-3f));
    }

    [Fact]
    public void SeamsAndPinsAreSavedWithTheModel()
    {
        GeometryData mesh = Grid(2, 1);
        UVs.Reset(mesh, [.. mesh.Faces]);
        UVs.SetSeams(mesh, [mesh.FindEdge(mesh.Vertices[1], mesh.Vertices[4])!], true);
        UVs.SetPinned([mesh.Faces[0].Loop!], true);
        var document = new PMeshDocument { Objects = { new PMeshObject { Name = "Body", Mesh = mesh } } };

        GeometryData read = PMeshDocument.Read(document.Write()).Objects[0].Mesh;

        Assert.Single(read.Edges, UVs.IsSeam);
        Assert.True(UVs.IsSeam(read.FindEdge(read.Vertices[1], read.Vertices[4])!));
        Assert.Single(read.Faces.SelectMany(Corners), UVs.IsPinned);
    }

    [Fact]
    public void FollowingAQuadLaysJoinedQuadsOutInAGridLikeIt()
    {
        GeometryData mesh = Grid(3, 2);
        UVs.Reset(mesh, [mesh.Faces[0]]);
        UVs.Transform(mesh, [.. Corners(mesh.Faces[0])], uv => uv * 0.1f);

        UVs.FollowQuad(mesh, mesh.Faces[0], [.. mesh.Faces]);

        Assert.Single(UVs.Islands(mesh, [.. mesh.Faces]));
        float first = UVArea(mesh.Faces[0]);
        Assert.All(mesh.Faces, f => Assert.True(MathF.Abs(UVArea(f) - first) < first * 0.01f, $"{UVArea(f)} against {first}"));
        foreach (GeometryData.Vertex vertex in mesh.Vertices)
        {
            Float2 expected = UVOf(mesh.Faces.SelectMany(Corners).First(l => l.Vert == vertex));
            Assert.All(mesh.Faces.SelectMany(Corners).Where(l => l.Vert == vertex), l => Assert.True(Float2.Distance(UVOf(l), expected) < 1e-4f));
        }
    }

    [Theory]
    [InlineData(60f, 12)]
    [InlineData(100f, 0)]
    public void SeamsFromSharpMarkEdgesWhoseFacesTurnPastTheAngle(float degrees, int seams)
    {
        GeometryData mesh = Primitives.Make(PrimitiveKind.Cube);

        UVs.SeamsFromSharp(mesh, degrees);

        Assert.Equal(seams, mesh.Edges.Count(UVs.IsSeam));
    }

    [Fact]
    public void ClearingTakesTheUVsAndPinsAwayAndKeepsSeams()
    {
        GeometryData mesh = Primitives.Make(PrimitiveKind.Cube);
        UVs.SetPinned(mesh.Faces[0].Corners(), true);
        UVs.SeamsFromSharp(mesh, 60f);

        UVs.Clear(mesh);

        Assert.False(mesh.HasLoopAttribute(MeshGeometry.UV));
        Assert.False(mesh.HasLoopAttribute(UVs.Pin));
        Assert.Equal(12, mesh.Edges.Count(UVs.IsSeam));
    }
}
