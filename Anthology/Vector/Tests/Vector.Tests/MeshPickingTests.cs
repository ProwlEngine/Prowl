// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

public class MeshPickingTests
{
    [Fact]
    public void ARayFromAboveHitsTheTopOfAMovedBox()
    {
        GeometryData box = Prowl.Vector.Geometry.GeometryGenerator.Box(new Float3(2f));
        Float4x4 toWorld = Float4x4.CreateTranslation(new Float3(0, 5, 0));

        Assert.True(MeshPicking.Raycast(box, toWorld, new Ray(new Float3(0.2f, 20, 0.3f), -Float3.UnitY), out GeometryData.Face? face, out float distance));

        Assert.True(face!.Normal().Y > 0.99f);
        Assert.Equal(14f, distance, 3);
        Assert.False(MeshPicking.Raycast(box, toWorld, new Ray(new Float3(5, 20, 0), -Float3.UnitY), out _, out _));
    }

    /// <summary>A bumpy grid, folded so rays meet it more than once.</summary>
    private static GeometryData Bumpy(int n)
    {
        var mesh = new GeometryData();
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
                mesh.AddVertex(x, MathF.Sin(x * 0.7f) * MathF.Cos(z * 0.5f) * 2f, z);
        int stride = n + 1;
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                mesh.AddFace(z * stride + x, (z + 1) * stride + x, (z + 1) * stride + x + 1, z * stride + x + 1);
        return mesh;
    }

    [Fact]
    public void TheBvhFindsWhatTestingEveryFaceFinds()
    {
        GeometryData mesh = Bumpy(30);
        Float4x4 toWorld = Float4x4.CreateTRS(new Float3(1, 2, 3), Quaternion.FromEuler(new Float3(0.3f, 0.5f, 0.1f)), new Float3(1.5f));
        MeshBvh bvh = MeshBvh.For(mesh);
        var random = new System.Random(5);
        int hits = 0;
        for (int i = 0; i < 300; i++)
        {
            var from = new Float3(random.NextSingle() * 60f - 15f, 30f, random.NextSingle() * 60f - 15f);
            var to = new Float3(random.NextSingle() * 50f, -20f, random.NextSingle() * 50f);
            var ray = new Ray(from, Float3.Normalize(to - from));
            bool brute = MeshPicking.Raycast(mesh, toWorld, ray, out GeometryData.Face? face, out float distance);
            bool fast = bvh.Raycast(toWorld, ray, out GeometryData.Face? bvhFace, out float bvhDistance);
            Assert.Equal(brute, fast);
            if (!brute) continue;
            hits++;
            Assert.Equal(distance, bvhDistance, 3);
            Assert.Same(face, bvhFace);
        }
        Assert.True(hits > 50);
    }

    [Fact]
    public void TheBvhFollowsMovedPointsAndChangedFaces()
    {
        GeometryData mesh = Bumpy(6);
        MeshBvh tree = MeshBvh.For(mesh);
        var ray = new Ray(new Float3(3.1f, 40f, 3.2f), -Float3.UnitY);
        Assert.True(tree.Raycast(Float4x4.Identity, ray, out _, out _));

        foreach (GeometryData.Vertex vertex in mesh.Vertices) vertex.Point += new Float3(0, 10, 0);
        Assert.Same(tree, MeshBvh.For(mesh));
        Assert.True(MeshBvh.For(mesh).Raycast(Float4x4.Identity, ray, out _, out float moved));
        Assert.True(MeshPicking.Raycast(mesh, Float4x4.Identity, ray, out _, out float brute));
        Assert.Equal(brute, moved, 4);

        foreach (GeometryData.Face face in mesh.Faces.ToList()) mesh.RemoveFace(face);
        Assert.False(MeshBvh.For(mesh).Raycast(Float4x4.Identity, ray, out _, out _));
    }
}
