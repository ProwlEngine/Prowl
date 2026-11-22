// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.CSG;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for <see cref="CSGMeshBuilder"/>: turning CSG output into engine meshes, split into one
/// submesh per material and separated from collision geometry.
/// </summary>
public class CSGMeshBuilderTests
{
    private static GeometryData Box(float size) => GeometryGenerator.Box(new Float3(size, size, size));
    private static Float4x4 At(float x) => Float4x4.CreateTranslation(new Float3(x, 0, 0));

    private static Material NewMaterial() => new(Shader.LoadDefault(DefaultShader.Invalid));

    /// <summary>Every surface renders and collides with one shared material.</summary>
    private static CSGMeshBuilder.SurfaceResolver Uniform(Material material) =>
        (_, _) => CSGSurfaceInfo.Default(material);

    private static GeometryData SingleBox()
    {
        var scene = new CSGScene();
        scene.Add(Box(2));
        return scene.Build();
    }

    [Fact]
    public void ABoxBecomesTwelveTriangles()
    {
        var meshes = CSGMeshBuilder.Build(SingleBox(), Uniform(NewMaterial()));

        Assert.NotNull(meshes.Visible);
        Assert.Equal(36, meshes.Visible!.Indices.Length);   // 6 quads, fanned
        Assert.Single(meshes.Materials);
        Assert.Equal(1, meshes.Visible.SubMeshCount);
    }

    /// <summary>
    /// The reason submeshes exist: a renderer draws one material per submesh, so faces have to be
    /// grouped by material and each group's indices kept contiguous.
    /// </summary>
    [Fact]
    public void FacesAreGroupedIntoOneSubmeshPerMaterial()
    {
        var a = NewMaterial();
        var b = NewMaterial();

        var meshes = CSGMeshBuilder.Build(SingleBox(),
            (_, faceIndex) => CSGSurfaceInfo.Default(faceIndex < 3 ? a : b));

        Assert.Equal(2, meshes.Visible!.SubMeshCount);
        Assert.Equal(2, meshes.Materials.Count);

        var first = meshes.Visible.GetSubMesh(0);
        var second = meshes.Visible.GetSubMesh(1);

        // Contiguous, in order, and together covering the whole index buffer.
        Assert.Equal(0, first.IndexStart);
        Assert.Equal(first.IndexStart + first.IndexCount, second.IndexStart);
        Assert.Equal(meshes.Visible.Indices.Length, second.IndexStart + second.IndexCount);
    }

    /// <summary>Materials are parallel to the submeshes, or the wrong faces get the wrong material.</summary>
    [Fact]
    public void MaterialsAreOrderedToMatchTheirSubmeshes()
    {
        var a = NewMaterial();
        var b = NewMaterial();

        var meshes = CSGMeshBuilder.Build(SingleBox(),
            (_, faceIndex) => CSGSurfaceInfo.Default(faceIndex == 0 ? a : b));

        // Face 0 is emitted first, so its material leads.
        Assert.Equal(a.InstanceID, meshes.Materials[0]!.InstanceID);
        Assert.Equal(b.InstanceID, meshes.Materials[1]!.InstanceID);

        // One face fanned into two triangles.
        Assert.Equal(6, meshes.Visible!.GetSubMesh(0).IndexCount);
    }

    [Fact]
    public void NonRenderingSurfacesAreExcludedFromTheVisibleMesh()
    {
        var material = NewMaterial();

        var meshes = CSGMeshBuilder.Build(SingleBox(), (_, faceIndex) => new CSGSurfaceInfo
        {
            Material = material,
            Render = faceIndex != 0,
            Collide = true,
        });

        Assert.Equal(30, meshes.Visible!.Indices.Length);    // five faces
        Assert.Equal(36, meshes.Collision!.Indices.Length);  // all six still collide
    }

    [Fact]
    public void NonCollidingSurfacesAreExcludedFromTheCollisionMesh()
    {
        var material = NewMaterial();

        var meshes = CSGMeshBuilder.Build(SingleBox(), (_, faceIndex) => new CSGSurfaceInfo
        {
            Material = material,
            Render = true,
            Collide = faceIndex != 0,
        });

        Assert.Equal(36, meshes.Visible!.Indices.Length);
        Assert.Equal(30, meshes.Collision!.Indices.Length);
    }

    /// <summary>
    /// Physics reads positions and indices only, so the collision mesh shares corners between faces
    /// instead of splitting them for normals and UVs it will never use.
    /// </summary>
    [Fact]
    public void CollisionMeshWeldsCornersAndCarriesNoShadingData()
    {
        var meshes = CSGMeshBuilder.Build(SingleBox(), Uniform(NewMaterial()));

        Assert.Equal(8, meshes.Collision!.Vertices.Length);      // a box has 8 corners, not 24
        Assert.Equal(36, meshes.Collision.Indices.Length);
        Assert.Equal(24, meshes.Visible!.Vertices.Length);       // visible still splits per face

        Assert.Empty(meshes.Collision.Normals);
        Assert.Empty(meshes.Collision.UV);

        foreach (uint index in meshes.Collision.Indices)
            Assert.InRange(index, 0u, (uint)(meshes.Collision.Vertices.Length - 1));
    }

    /// <summary>Normal-mapped level surfaces need tangents, and nothing else generates them.</summary>
    [Fact]
    public void VisibleMeshHasTangents()
    {
        var scene = new CSGScene
        {
            SurfaceUV = (int brush, int face, in Plane plane, out SurfaceTexSpace space) =>
            {
                space = SurfaceTexSpace.FromFace(plane.Normal, Float2.One);
                return true;
            }
        };
        scene.Add(Box(2));

        var meshes = CSGMeshBuilder.Build(scene.Build(), Uniform(NewMaterial()));

        Assert.Equal(meshes.Visible!.Vertices.Length, meshes.Visible.Tangents.Length);
        Assert.Contains(meshes.Visible.Tangents, t => Float3.LengthSquared(new Float3(t.X, t.Y, t.Z)) > 0.5f);
    }

    [Fact]
    public void SurfacesThatNeitherRenderNorCollide_ProduceNoMeshes()
    {
        var meshes = CSGMeshBuilder.Build(SingleBox(), (_, _) => new CSGSurfaceInfo
        {
            Material = NewMaterial(),
            Render = false,
            Collide = false,
        });

        Assert.Null(meshes.Visible);
        Assert.Null(meshes.Collision);
        Assert.Empty(meshes.Materials);
    }

    [Fact]
    public void EmptyGeometryProducesNoMeshes()
    {
        var meshes = CSGMeshBuilder.Build(new CSGScene().Build(), Uniform(NewMaterial()));

        Assert.Null(meshes.Visible);
        Assert.Null(meshes.Collision);
    }

    /// <summary>Indices must stay inside the vertex buffer, whatever the fan produced.</summary>
    [Fact]
    public void EveryIndexIsInRange()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));
        scene.Add(Box(2), CSGOperation.Subtractive, At(3));

        var meshes = CSGMeshBuilder.Build(scene.Build(), Uniform(NewMaterial()));

        int vertexCount = meshes.Visible!.Vertices.Length;
        foreach (uint index in meshes.Visible.Indices)
            Assert.InRange(index, 0u, (uint)(vertexCount - 1));

        Assert.Equal(vertexCount, meshes.Visible.Normals.Length);
        Assert.Equal(vertexCount, meshes.Visible.UV.Length);
    }

    /// <summary>
    /// T-junction welding inserts collinear corners, which a triangle fan turns into zero-area
    /// triangles. They must be dropped rather than shipped to the GPU.
    /// </summary>
    [Fact]
    public void CollinearCornersDoNotProduceZeroAreaTriangles()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));
        scene.Add(Box(2), CSGOperation.Subtractive, At(3));

        var meshes = CSGMeshBuilder.Build(scene.Build(), Uniform(NewMaterial()));
        var v = meshes.Visible!.Vertices;
        var idx = meshes.Visible.Indices;

        for (int i = 0; i < idx.Length; i += 3)
        {
            Float3 a = v[idx[i]], b = v[idx[i + 1]], c = v[idx[i + 2]];
            float area2 = Float3.LengthSquared(Float3.Cross(b - a, c - a));
            Assert.True(area2 > 1e-14f, $"degenerate triangle at index {i}");
        }
    }

    /// <summary>Flat shading gives every corner of a face the face's own normal.</summary>
    [Fact]
    public void FlatSurfacesUseTheFaceNormal()
    {
        var meshes = CSGMeshBuilder.Build(SingleBox(), Uniform(NewMaterial()));

        // A box has six axis-aligned faces, so every normal is a unit axis.
        foreach (Float3 n in meshes.Visible!.Normals)
        {
            float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
            Assert.True(ax > 0.99f || ay > 0.99f || az > 0.99f, $"unexpected normal {n}");
        }
    }

    /// <summary>Smoothed surfaces average the normals of faces meeting at a corner, so a curved
    /// run of surfaces shades continuously instead of faceting.</summary>
    [Fact]
    public void SmoothingGroupsAverageNormalsAtSharedCorners()
    {
        var flat = CSGMeshBuilder.Build(SingleBox(), Uniform(NewMaterial()));
        var smooth = CSGMeshBuilder.Build(SingleBox(), (_, _) => new CSGSurfaceInfo
        {
            Material = NewMaterial(),
            Render = true,
            Collide = true,
            SmoothingGroup = 1,
        });

        // Every corner of a box is shared by three faces, so averaging tilts every normal off-axis.
        bool anyOffAxis = smooth.Visible!.Normals.Any(n =>
            MathF.Abs(n.X) < 0.99f && MathF.Abs(n.Y) < 0.99f && MathF.Abs(n.Z) < 0.99f);

        Assert.True(anyOffAxis, "smoothing produced no averaged normals");
        Assert.Equal(24, flat.Visible!.Vertices.Length);
        Assert.Equal(8, smooth.Visible.Vertices.Length);
    }

    [Fact]
    public void DifferentSmoothingGroupsStaySharpWhereTheyMeet()
    {
        var material = NewMaterial();
        CSGMeshes Build(Func<int, int> group) => CSGMeshBuilder.Build(SingleBox(), (_, face) => new CSGSurfaceInfo
        {
            Material = material,
            Render = true,
            Collide = true,
            SmoothingGroup = group(face),
        });

        int oneGroup = Build(_ => 1).Visible!.Vertices.Length;
        int twoGroups = Build(face => face < 3 ? 1 : 2).Visible!.Vertices.Length;

        Assert.True(twoGroups > oneGroup, "a group boundary did not split the shading");
    }

    /// <summary>UVs generated by the CSG surface projection have to survive into the mesh.</summary>
    [Fact]
    public void ProjectedUVsReachTheMesh()
    {
        var scene = new CSGScene
        {
            SurfaceUV = (int brush, int face, in Plane plane, out SurfaceTexSpace space) =>
            {
                space = SurfaceTexSpace.FromFace(plane.Normal, new Float2(10f, 20f));
                return true;
            }
        };
        scene.Add(Box(2));

        var meshes = CSGMeshBuilder.Build(scene.Build(), Uniform(NewMaterial()));

        Assert.Contains(meshes.Visible!.UV, uv => MathF.Abs(uv.X) > 5f || MathF.Abs(uv.Y) > 5f);
    }

    /// <summary>Large models must not silently truncate their index buffer to 16 bits.</summary>
    [Fact]
    public void MeshesPastTheShortLimitUse32BitIndices()
    {
        var small = new CSGScene();
        small.Add(Box(2));
        Assert.Equal(IndexFormat.UInt16,
            CSGMeshBuilder.Build(small.Build(), Uniform(NewMaterial())).Visible!.IndexFormat);

        var large = new CSGScene();
        large.Add(GeometryGenerator.Sphere(4f, segments: 220, rings: 110));

        var meshes = CSGMeshBuilder.Build(large.Build(), Uniform(NewMaterial()));

        Assert.True(meshes.Visible!.Vertices.Length > ushort.MaxValue,
            $"expected a large mesh, got {meshes.Visible.Vertices.Length} vertices");
        Assert.Equal(IndexFormat.UInt32, meshes.Visible.IndexFormat);
    }
}
