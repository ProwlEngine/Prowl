// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Vector.Tests;

/// <summary>
/// Covers <see cref="CSGScene"/>, <see cref="GeometryGenerator.ConvexFromPlanes"/> and
/// <see cref="CSGBrushValidity"/>.
/// </summary>
public class CSGTests
{
    private static GeometryData Box(float size) => GeometryGenerator.Box(new Float3(size, size, size));
    private static Plane[] CubePlanes() => GeometryGenerator.BoxPlanes(new Float3(2, 2, 2));
    private static Float4x4 At(float x) => Float4x4.CreateTranslation(new Float3(x, 0, 0));

    private static int BrushOf(GeometryData.Face f) => CSGScene.GetBrushId(f);
    private static int SourceOf(GeometryData.Face f) => CSGScene.GetSourceFaceIndex(f);

    private static Float3 FaceNormal(GeometryData.Face face)
    {
        var verts = face.NeighborVertices();
        Float3 n = Float3.Zero;
        for (int i = 0; i < verts.Count; i++)
        {
            Float3 a = verts[i].Point, b = verts[(i + 1) % verts.Count].Point;
            n.X += (a.Y - b.Y) * (a.Z + b.Z);
            n.Y += (a.Z - b.Z) * (a.X + b.X);
            n.Z += (a.X - b.X) * (a.Y + b.Y);
        }
        return Float3.Normalize(n);
    }

    private static void AssertManifold(GeometryData mesh)
    {
        foreach (var edge in mesh.Edges)
            Assert.Equal(2, edge.NeighborFaces().Count);
    }

    // ================================================================
    //  Convex solids from half-spaces
    // ================================================================

    [Fact]
    public void ConvexFromPlanes_BuildsAClosedSolid()
    {
        var result = GeometryGenerator.ConvexFromPlanes(CubePlanes());

        Assert.True(result.IsValid);
        Assert.Equal(6, result.Solid!.Faces.Count);
        Assert.Equal(8, result.Solid.Vertices.Count);
        Assert.Empty(result.RedundantPlanes);
        AssertManifold(result.Solid);
    }

    /// <summary>Catches a flipped half-space convention, which still yields six faces.</summary>
    [Fact]
    public void ConvexFromPlanes_PutsCornersWhereExpected()
    {
        var result = GeometryGenerator.ConvexFromPlanes(CubePlanes());

        foreach (var v in result.Solid!.Vertices)
        {
            Assert.Equal(1f, MathF.Abs(v.Point.X), 3);
            Assert.Equal(1f, MathF.Abs(v.Point.Y), 3);
            Assert.Equal(1f, MathF.Abs(v.Point.Z), 3);
        }
    }

    /// <summary>Windings must follow their plane normals or the solid is inside-out.</summary>
    [Fact]
    public void ConvexFromPlanes_WindsFacesToMatchTheirPlanes()
    {
        var planes = CubePlanes();
        var result = GeometryGenerator.ConvexFromPlanes(planes);

        foreach (var face in result.Solid!.Faces)
        {
            Float3 normal = FaceNormal(face);
            Assert.Contains(planes, p => Float3.Dot(normal, p.Normal) > 0.99f);
        }
    }

    [Fact]
    public void ConvexFromPlanes_CuttingPlaneAddsAFace()
    {
        var planes = new List<Plane>(CubePlanes())
        {
            new Plane(Float3.Normalize(new Float3(1, 1, 1)), 1.2f)
        };

        var result = GeometryGenerator.ConvexFromPlanes(planes);

        Assert.True(result.IsValid);
        Assert.Equal(7, result.Solid!.Faces.Count);
        Assert.Empty(result.RedundantPlanes);
    }

    /// <summary>A plane that clips nothing is reported so callers know it stopped contributing.</summary>
    [Fact]
    public void ConvexFromPlanes_ReportsPlanesThatClipNothing()
    {
        var planes = new List<Plane>(CubePlanes()) { new Plane(Float3.UnitX, 50f) };

        var result = GeometryGenerator.ConvexFromPlanes(planes);

        Assert.Equal(6, result.Solid!.Faces.Count);
        Assert.Equal(new[] { 6 }, result.RedundantPlanes);
    }

    /// <summary>
    /// A duplicated plane must be dropped, not built into a second face on top of the first - that
    /// would share every edge and leave the mesh non-manifold.
    /// </summary>
    [Fact]
    public void ConvexFromPlanes_DropsDuplicatePlanes()
    {
        var planes = new List<Plane>(CubePlanes()) { CubePlanes()[0] };

        var result = GeometryGenerator.ConvexFromPlanes(planes);

        Assert.Equal(6, result.Solid!.Faces.Count);
        Assert.Equal(new[] { 6 }, result.RedundantPlanes);
        AssertManifold(result.Solid);
    }

    /// <summary>The <see cref="Plane"/> constructor substitutes UnitZ for a zero normal, so real
    /// degeneracy only arrives via <c>default</c> or field assignment.</summary>
    [Fact]
    public void ConvexFromPlanes_ReportsDegeneratePlanes()
    {
        var planes = new List<Plane>(CubePlanes()) { default };

        var result = GeometryGenerator.ConvexFromPlanes(planes);

        Assert.Equal(6, result.Solid!.Faces.Count);
        Assert.Contains(6, result.RedundantPlanes);
    }

    [Fact]
    public void ConvexFromPlanes_ContradictoryPlanesEncloseNothing()
    {
        var result = GeometryGenerator.ConvexFromPlanes(
            new Plane(Float3.UnitX, -5f), new Plane(-Float3.UnitX, -5f),
            new Plane(Float3.UnitY, 1f), new Plane(-Float3.UnitY, 1f),
            new Plane(Float3.UnitZ, 1f), new Plane(-Float3.UnitZ, 1f));

        Assert.Equal(ConvexSolidStatus.Empty, result.Status);
    }

    /// <summary>An open set runs to infinity, which callers need distinguished from empty.</summary>
    [Fact]
    public void ConvexFromPlanes_OpenSetIsUnbounded()
    {
        var result = GeometryGenerator.ConvexFromPlanes(
            new Plane(Float3.UnitX, 1f), new Plane(-Float3.UnitX, 1f),
            new Plane(Float3.UnitY, 1f), new Plane(-Float3.UnitY, 1f),
            new Plane(Float3.UnitZ, 1f));   // no -Z cap

        Assert.Equal(ConvexSolidStatus.Unbounded, result.Status);
    }

    [Fact]
    public void ConvexFromPlanes_SolidWorksAsABrush()
    {
        var solid = GeometryGenerator.ConvexFromPlanes(CubePlanes());
        Assert.True(CSGBrushValidity.IsValid(solid.Solid!));

        var scene = new CSGScene();
        scene.Add(solid.Solid!);
        scene.Add(Box(1), CSGOperation.Subtractive, At(1));

        Assert.NotEmpty(scene.Build().Faces);
    }

    // ================================================================
    //  Brush validity
    // ================================================================

    [Fact]
    public void Validity_AcceptsGeneratedPrimitives()
    {
        Assert.True(CSGBrushValidity.IsValid(Box(2)));
        Assert.True(CSGBrushValidity.IsValid(GeometryGenerator.Tetrahedron(2f)));
        Assert.True(CSGBrushValidity.IsValid(GeometryGenerator.Octahedron(2f)));
    }

    [Fact]
    public void Validity_RejectsAnOpenSurface()
    {
        var validity = CSGBrushValidity.Check(GeometryGenerator.Plane(new Float2(2, 2)));

        Assert.True(validity.HasFlag(BrushValidity.NotClosed) || validity.HasFlag(BrushValidity.TooFewFaces));
    }

    /// <summary>Concavity breaks the convexity CSG assumes, giving wrong output rather than ugly.</summary>
    [Fact]
    public void Validity_RejectsConcavity()
    {
        var box = Box(2);
        var corner = box.Vertices[0];
        corner.Point = Float3.Zero - corner.Point * 0.2f;

        Assert.True(CSGBrushValidity.Check(box).HasFlag(BrushValidity.NotConvex));
    }

    [Fact]
    public void Validity_RejectsAFlattenedBox()
    {
        var validity = CSGBrushValidity.Check(GeometryGenerator.Box(new Float3(2, 0, 2)));

        Assert.True(validity.HasFlag(BrushValidity.ZeroVolume) || validity.HasFlag(BrushValidity.DegenerateFace));
    }

    // ================================================================
    //  Source identity on output
    // ================================================================

    [Fact]
    public void Identity_EveryFaceNamesItsBrushAndSourceFace()
    {
        var scene = new CSGScene();
        var a = scene.Add(Box(2));
        var b = scene.Add(Box(2), CSGOperation.Additive, At(5));

        var built = scene.Build();

        Assert.NotEmpty(built.Faces);
        foreach (var face in built.Faces)
            Assert.InRange(SourceOf(face), 0, 5);

        var ids = built.Faces.Select(BrushOf).ToHashSet();
        Assert.Contains(a.Id, ids);
        Assert.Contains(b.Id, ids);
    }

    /// <summary>Ids are identity, not position: a held reference must not start naming another brush.</summary>
    [Fact]
    public void Identity_BrushIdSurvivesReordering()
    {
        var scene = new CSGScene();
        var a = scene.Add(Box(2));
        var b = scene.Add(Box(2), CSGOperation.Additive, At(5));

        int before = a.Id;
        scene.SetOrder(a, 1);

        Assert.Equal(before, a.Id);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Contains(a.Id, scene.Build().Faces.Select(BrushOf));
    }

    /// <summary>Surfaces revealed by a cut belong to the cutting brush, which decides their material.</summary>
    [Fact]
    public void Identity_CutSurfacesBelongToTheCuttingBrush()
    {
        var scene = new CSGScene();
        var solid = scene.Add(Box(4));
        var cutter = scene.Add(Box(2), CSGOperation.Subtractive, At(2));

        var ids = scene.Build().Faces.Select(BrushOf).ToHashSet();

        Assert.Contains(solid.Id, ids);
        Assert.Contains(cutter.Id, ids);
    }

    /// <summary>Fragments of one clipped face keep its source index, so they remain one surface.</summary>
    [Fact]
    public void Identity_FragmentsShareTheirSourceIndex()
    {
        var scene = new CSGScene();
        var solid = scene.Add(Box(4));
        scene.Add(Box(1), CSGOperation.Subtractive, At(2));

        var perSource = new Dictionary<int, int>();
        foreach (var face in scene.Build().Faces.Where(f => BrushOf(f) == solid.Id))
            perSource[SourceOf(face)] = perSource.GetValueOrDefault(SourceOf(face)) + 1;

        Assert.Contains(perSource, kv => kv.Value > 1);
    }

    // ================================================================
    //  Tree: groups, nesting and intersection
    // ================================================================

    /// <summary>
    /// A group resolves internally before folding into its parent, so subtracting (A minus B) leaves
    /// B behind as an island. The flat equivalent below cannot express that.
    /// </summary>
    [Fact]
    public void Tree_SubtractingAGroupResolvesItFirst()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));

        var carve = scene.AddGroup(operation: CSGOperation.Subtractive);
        scene.AddTo(carve, Box(3), CSGOperation.Additive, At(3));
        var keep = scene.AddTo(carve, Box(1), CSGOperation.Subtractive, At(3));

        Assert.NotEmpty(CSGScene.FacesOf(scene.Build(), keep));
    }

    [Fact]
    public void Tree_FlatEquivalentLeavesNoIsland()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));
        scene.Add(Box(3), CSGOperation.Subtractive, At(3));
        var inner = scene.Add(Box(1), CSGOperation.Subtractive, At(3));

        Assert.Empty(CSGScene.FacesOf(scene.Build(), inner));
    }

    [Fact]
    public void Tree_IntersectKeepsOnlyTheSharedVolume()
    {
        var scene = new CSGScene();
        scene.Add(Box(2));
        scene.Add(Box(2), CSGOperation.Intersect, At(1));

        var built = scene.Build();

        // Two unit-offset 2-cubes share x in [0,1].
        Assert.NotEmpty(built.Faces);
        Assert.Equal(0f, built.Vertices.Min(v => v.Point.X), 3);
        Assert.Equal(1f, built.Vertices.Max(v => v.Point.X), 3);
        AssertManifold(built);
    }

    [Fact]
    public void Tree_IntersectingDisjointVolumesLeavesNothing()
    {
        var scene = new CSGScene();
        scene.Add(Box(2));
        scene.Add(Box(2), CSGOperation.Intersect, At(50));

        Assert.Empty(scene.Build().Faces);
    }

    [Fact]
    public void Tree_NestedGroupsResolveInsideOut()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));

        var outer = scene.AddGroup(operation: CSGOperation.Subtractive);
        scene.AddTo(outer, Box(3), CSGOperation.Additive, At(3));
        var inner = scene.AddGroup(outer, CSGOperation.Subtractive);
        var island = scene.AddTo(inner, Box(1), CSGOperation.Additive, At(3));

        Assert.NotEmpty(CSGScene.FacesOf(scene.Build(), island));
    }

    [Fact]
    public void Tree_ReparentingChangesTheResult()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));
        var carve = scene.AddGroup(operation: CSGOperation.Subtractive);
        scene.AddTo(carve, Box(3), CSGOperation.Additive, At(3));

        var brush = scene.Add(Box(1), CSGOperation.Subtractive, At(3));
        Assert.Empty(CSGScene.FacesOf(scene.Build(), brush));

        scene.SetParent(brush, carve);
        Assert.NotEmpty(CSGScene.FacesOf(scene.Build(), brush));
    }

    [Fact]
    public void Tree_RemovingAGroupRemovesItsContents()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));
        var carve = scene.AddGroup(operation: CSGOperation.Subtractive);
        scene.AddTo(carve, Box(3), CSGOperation.Additive, At(3));
        scene.Build();

        scene.Remove(carve);

        Assert.Equal(1, scene.Count);
        Assert.Equal(6, scene.Build().Faces.Count);
    }

    /// <summary>Guards against a cycle, which would recurse forever during evaluation.</summary>
    [Fact]
    public void Tree_GroupCannotBeMovedInsideItself()
    {
        var scene = new CSGScene();
        var outer = scene.AddGroup();
        var inner = scene.AddGroup(outer);

        Assert.Throws<InvalidOperationException>(() => scene.SetParent(outer, inner));
    }

    // ================================================================
    //  Surface texture projection
    // ================================================================

    private static IEnumerable<(int surface, Float3 pos, Float2 uv)> UVs(GeometryData built)
    {
        foreach (var face in built.Faces)
        {
            int surface = BrushOf(face) * 1000 + SourceOf(face);
            var loop = face.Loop;
            if (loop == null) continue;
            do
            {
                if (loop.Attributes.TryGetValue("uv", out var v) && v is GeometryData.FloatAttributeValue f)
                    yield return (surface, loop.Vert.Point, new Float2(f.Data[0], f.Data[1]));
                loop = loop.Next;
            } while (loop != null && loop != face.Loop);
        }
    }

    private static CSGScene SceneWithProjection(Float2 offset = default, Float2 scale = default)
    {
        var scene = new CSGScene
        {
            SurfaceUV = (int brush, int face, in Plane plane, out SurfaceTexSpace space) =>
            {
                space = SurfaceTexSpace.FromFace(plane.Normal, offset, scale);
                return true;
            }
        };
        scene.Add(Box(4));
        return scene;
    }

    private static Dictionary<(int, int, int), Float2> UVByPosition(CSGScene scene)
    {
        var map = new Dictionary<(int, int, int), Float2>();
        foreach (var (_, pos, uv) in UVs(scene.Build()))
            map[((int)MathF.Round(pos.X * 100), (int)MathF.Round(pos.Y * 100), (int)MathF.Round(pos.Z * 100))] = uv;
        return map;
    }

    /// <summary>
    /// Within one surface, UV is a function of world position, so re-cutting a surface into different
    /// fragments does not shift the texture. Scoped per surface because faces meeting at a corner use
    /// different projection bases and are expected to disagree there.
    /// </summary>
    [Fact]
    public void Projection_IsStableAcrossFragmentsOfOneSurface()
    {
        var scene = new CSGScene
        {
            SurfaceUV = (int brush, int face, in Plane plane, out SurfaceTexSpace space) =>
            {
                space = SurfaceTexSpace.FromFace(plane.Normal);
                return true;
            }
        };
        scene.Add(Box(6));
        scene.Add(Box(2), CSGOperation.Subtractive, At(3));

        var seen = new Dictionary<(int, int, int, int), Float2>();
        int shared = 0;

        foreach (var (surface, pos, uv) in UVs(scene.Build()))
        {
            var key = (surface, (int)MathF.Round(pos.X * 100), (int)MathF.Round(pos.Y * 100), (int)MathF.Round(pos.Z * 100));
            if (seen.TryGetValue(key, out var existing))
            {
                shared++;
                Assert.Equal(existing.X, uv.X, 3);
                Assert.Equal(existing.Y, uv.Y, 3);
            }
            else seen[key] = uv;
        }

        Assert.NotEmpty(seen);
        Assert.True(shared > 0, "expected fragments of a cut surface to share corner positions");
    }

    [Fact]
    public void Projection_AppliesOffsetAndScale()
    {
        Float2 shift = new(0.25f, -0.5f);
        var plain = UVByPosition(SceneWithProjection());
        var moved = UVByPosition(SceneWithProjection(offset: shift));
        var scaled = UVByPosition(SceneWithProjection(scale: new Float2(2, 2)));

        foreach (var key in plain.Keys)
        {
            Assert.Equal(plain[key].X + shift.X, moved[key].X, 3);
            Assert.Equal(plain[key].Y + shift.Y, moved[key].Y, 3);
            Assert.Equal(plain[key].X * 0.5f, scaled[key].X, 3);
            Assert.Equal(plain[key].Y * 0.5f, scaled[key].Y, 3);
        }
    }

    [Fact]
    public void Projection_AbsentByDefault()
    {
        var scene = new CSGScene();
        scene.Add(Box(2));

        Assert.Empty(UVs(scene.Build()));
    }

    /// <summary>Registering the attribute gives every loop a default entry, so this checks values.</summary>
    [Fact]
    public void Projection_CanBeDeclinedPerSurface()
    {
        var scene = new CSGScene
        {
            SurfaceUV = (int brush, int face, in Plane plane, out SurfaceTexSpace space) =>
            {
                space = SurfaceTexSpace.FromFace(plane.Normal, new Float2(10f, 20f));
                return face == 0;
            }
        };
        scene.Add(Box(2));

        foreach (var face in scene.Build().Faces)
        {
            var uv = (GeometryData.FloatAttributeValue)face.Loop!.Attributes["uv"];
            if (SourceOf(face) == 0) Assert.True(uv.Data[0] > 5f);
            else Assert.Equal(0f, uv.Data[0], 3);
        }
    }

    // ================================================================
    //  Incremental rebuilding
    // ================================================================

    [Fact]
    public void Incremental_RebuildWithNoChangesRecomputesNothing()
    {
        var scene = new CSGScene();
        scene.Add(Box(2));
        scene.Build();

        scene.Build();

        Assert.Equal(0, scene.LastRebuildCount);
    }

    [Fact]
    public void Incremental_MovingOneBrushSkipsDistantOnes()
    {
        var scene = new CSGScene();
        var a = scene.Add(Box(2));
        scene.Add(Box(2), CSGOperation.Additive, At(20));
        scene.Add(Box(2), CSGOperation.Additive, At(100));
        scene.Build();

        scene.SetTransform(a, At(0.5f));
        scene.Build();

        Assert.True(scene.LastRebuildCount < 3, $"recomputed {scene.LastRebuildCount} of 3");
    }

    /// <summary>An incremental build must match one from scratch exactly.</summary>
    [Fact]
    public void Incremental_MatchesAFullRebuild()
    {
        var incremental = new CSGScene();
        incremental.Add(Box(6));
        var cutter = incremental.Add(Box(2), CSGOperation.Subtractive, At(3));
        incremental.Build();
        incremental.SetTransform(cutter, At(2.5f));
        var afterEdit = incremental.Build();

        var scratch = new CSGScene();
        scratch.Add(Box(6));
        scratch.Add(Box(2), CSGOperation.Subtractive, At(2.5f));
        var fresh = scratch.Build();

        Assert.Equal(fresh.Faces.Count, afterEdit.Faces.Count);
        Assert.Equal(fresh.Vertices.Count, afterEdit.Vertices.Count);
        Assert.Equal(fresh.Edges.Count, afterEdit.Edges.Count);
    }

    [Fact]
    public void Incremental_MovingABrushChangesTheMesh()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));
        var cutter = scene.Add(Box(2), CSGOperation.Subtractive, At(3));

        int carved = scene.Build().Faces.Count;
        scene.SetTransform(cutter, At(50));

        Assert.NotEqual(carved, scene.Build().Faces.Count);
        Assert.Equal(6, scene.Build().Faces.Count);
    }

    /// <summary>Nothing is left to recompute, so the structural change itself must drop the cache.</summary>
    [Fact]
    public void Incremental_RemovingTheLastBrushEmptiesTheOutput()
    {
        var scene = new CSGScene();
        var only = scene.Add(Box(2));
        Assert.Equal(6, scene.Build().Faces.Count);

        scene.Remove(only);

        Assert.Empty(scene.Build().Faces);
    }

    [Fact]
    public void Incremental_RemovingABrushChangesTheMesh()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));
        var cutter = scene.Add(Box(2), CSGOperation.Subtractive, At(3));

        int carved = scene.Build().Faces.Count;
        scene.Remove(cutter);

        Assert.NotEqual(carved, scene.Build().Faces.Count);
        Assert.Equal(6, scene.Build().Faces.Count);
    }

    /// <summary>
    /// Intersect removes volume arbitrarily far from the brush carrying it, so the dirty-region
    /// scheme - which assumes a brush only affects what it overlaps - has to escalate.
    /// </summary>
    [Fact]
    public void Incremental_IntersectWithADistantBrushReEvaluatesEverything()
    {
        var scene = new CSGScene();
        scene.Add(Box(2));
        var far = scene.Add(Box(2), CSGOperation.Additive, At(50));
        Assert.Equal(12, scene.Build().Faces.Count);

        scene.SetOperation(far, CSGOperation.Intersect);

        Assert.Empty(scene.Build().Faces);
    }

    [Fact]
    public void Incremental_MovingABrushWhileAnIntersectExists()
    {
        var scene = new CSGScene();
        scene.Add(Box(2));
        var gate = scene.Add(Box(2), CSGOperation.Intersect, At(50));
        Assert.Empty(scene.Build().Faces);

        scene.SetTransform(gate, Float4x4.Identity);

        Assert.NotEmpty(scene.Build().Faces);
    }

    [Fact]
    public void Incremental_GroupOperationChangeReachesTheOutput()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));
        var group = scene.AddGroup();
        scene.AddTo(group, Box(2), CSGOperation.Additive, At(3));

        Assert.Equal(4f, scene.Build().Vertices.Max(v => v.Point.X), 3);
        scene.SetOperation(group, CSGOperation.Subtractive);
        Assert.Equal(3f, scene.Build().Vertices.Max(v => v.Point.X), 3);
    }

    /// <summary>Changing the provider alters output without moving anything.</summary>
    [Fact]
    public void Incremental_ChangingTheUVProviderDropsTheCache()
    {
        var scene = new CSGScene();
        scene.Add(Box(2));
        scene.Build();

        scene.SurfaceUV = (int brush, int face, in Plane plane, out SurfaceTexSpace space) =>
        {
            space = SurfaceTexSpace.FromFace(plane.Normal, new Float2(5f, 5f));
            return true;
        };

        var uv = (GeometryData.FloatAttributeValue)scene.Build().Faces[0].Loop!.Attributes["uv"];
        Assert.NotEqual(0f, uv.Data[0]);
    }

    /// <summary>Reading Count flattens the tree; that must not swallow a pending re-evaluation.</summary>
    [Fact]
    public void Incremental_ReadingCountDoesNotSwallowReEvaluation()
    {
        var scene = new CSGScene();
        scene.Add(Box(6));
        var group = scene.AddGroup();
        scene.AddTo(group, Box(2), CSGOperation.Additive, At(3));
        scene.Build();

        scene.SetOperation(group, CSGOperation.Subtractive);
        _ = scene.Count;

        Assert.Equal(3f, scene.Build().Vertices.Max(v => v.Point.X), 3);
    }

    // ================================================================
    //  Whole-scene behaviour
    // ================================================================

    [Theory]
    [InlineData(CSGOperation.Additive, 6)]      // duplicates share one surface
    [InlineData(CSGOperation.Subtractive, 0)]   // a brush subtracting itself annihilates
    public void CoincidentBrushes_ResolveTheirSharedSurface(CSGOperation op, int expectedFaces)
    {
        var scene = new CSGScene();
        scene.Add(Box(2));
        scene.Add(Box(2), op);

        Assert.Equal(expectedFaces, scene.Build().Faces.Count);
    }

    [Fact]
    public void SubtractingALargerBrush_LeavesNothing()
    {
        var scene = new CSGScene();
        scene.Add(Box(2));
        scene.Add(Box(6), CSGOperation.Subtractive);

        Assert.Empty(scene.Build().Faces);
    }

    /// <summary>Brushes meeting face to face merge rather than keeping both touching surfaces.</summary>
    [Fact]
    public void BrushesMeetingFaceToFace_MergeTheirTouchingSurfaces()
    {
        var scene = new CSGScene();
        scene.Add(Box(2));
        scene.Add(Box(2), CSGOperation.Additive, At(2));

        var built = scene.Build();

        Assert.Equal(10, built.Faces.Count);
        AssertManifold(built);
    }

    [Fact]
    public void EmptyScene_BuildsNothing()
    {
        Assert.Empty(new CSGScene().Build().Faces);
    }

    [Fact]
    public void SubtractiveBrushAlone_BuildsNothing()
    {
        var scene = new CSGScene();
        scene.Add(Box(2), CSGOperation.Subtractive);

        Assert.Empty(scene.Build().Faces);
    }

    [Fact]
    public void RemovedBrushHandle_BecomesInvalid()
    {
        var scene = new CSGScene();
        var group = scene.AddGroup();
        var brush = scene.AddTo(group, Box(2));

        scene.Remove(group);

        Assert.False(brush.IsValid);
        Assert.Empty(scene.Build().Faces);
    }
}
