// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Xunit;

namespace Prowl.Vector.Tests
{
    public class CSGSceneTests
    {
        private static GeometryData Box(Float3 size, Float3 center = default) => GeometryGenerator.Box(size, center);

        private static void AssertBounds(GeometryData mesh, Float3 min, Float3 max)
        {
            var box = mesh.GetAABB();
            TestUtil.AssertClose(min, box.Min);
            TestUtil.AssertClose(max, box.Max);
        }

        // Strictly watertight: every edge shared by exactly two faces. Holds when faces are split by a
        // single shared plane.
        private static void AssertClosed(GeometryData mesh)
        {
            Assert.NotEmpty(mesh.Faces);
            foreach (var edge in mesh.Edges)
                Assert.Equal(2, edge.NeighborFaces().Count);
        }

        // No edge shared by three or more faces. T-junctions (1-face edges) are allowed; a 3+ edge would
        // indicate overlapping/duplicated geometry, which must never happen.
        private static void AssertNoExcessEdges(GeometryData mesh)
        {
            Assert.NotEmpty(mesh.Faces);
            foreach (var edge in mesh.Edges)
                Assert.True(edge.NeighborFaces().Count <= 2);
        }

        [Fact]
        public void SingleAdditiveBrush_IsItself()
        {
            var scene = new CSGScene();
            scene.Add(Box(new Float3(2, 2, 2)));
            var result = scene.Build();

            AssertBounds(result, new Float3(-1, -1, -1), new Float3(1, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void TwoAdditiveBrushes_MatchBinaryUnion()
        {
            var scene = new CSGScene();
            scene.Add(Box(new Float3(2, 2, 2)));
            scene.Add(Box(new Float3(2, 2, 2), new Float3(1, 0, 0)));
            var result = scene.Build();

            // Same cross-section boxes shifted in X -> a single [-1,2] box, same as GeometryCSG.Union.
            AssertBounds(result, new Float3(-1, -1, -1), new Float3(2, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void AdditiveThenSubtractive_CarvesACavity()
        {
            var scene = new CSGScene();
            scene.Add(Box(new Float3(2, 2, 2)));                                   // solid [-1,1]^3
            scene.Add(Box(new Float3(2, 2, 2), new Float3(1, 0, 0)), CSGOperation.Subtractive); // remove x in [0,1]
            var result = scene.Build();

            // Same result as GeometryCSG.Subtraction: the slab x in [-1,0].
            AssertBounds(result, new Float3(-1, -1, -1), new Float3(0, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void LoneSubtractiveBrush_ProducesNothing()
        {
            var scene = new CSGScene();
            scene.Add(Box(new Float3(2, 2, 2)), CSGOperation.Subtractive);
            var result = scene.Build();

            Assert.Empty(result.Faces);
        }

        [Fact]
        public void ThreeBrushes_SlabWithTwoThroughTunnels_StaysClosed()
        {
            // Base slab with two square tunnels punched fully through it in Z (genus-2 but watertight).
            var scene = new CSGScene();
            scene.Add(Box(new Float3(4, 2, 2)));                                                  // [-2,2]x[-1,1]x[-1,1]
            scene.Add(Box(new Float3(1, 1, 3), new Float3(-1, 0, 0)), CSGOperation.Subtractive);  // interior tunnel
            scene.Add(Box(new Float3(1, 1, 3), new Float3(1, 0, 0)), CSGOperation.Subtractive);   // interior tunnel
            var result = scene.Build();

            // Two independent cuts subdivide the entry/exit faces separately; the default T-junction weld
            // pass stitches the result back into a watertight mesh.
            AssertClosed(result);
            AssertBounds(result, new Float3(-2, -1, -1), new Float3(2, 1, 1));
        }

        [Fact]
        public void DisablingTJunctionWeld_KeepsGeometryButMayLeaveTJunctions()
        {
            var scene = new CSGScene { WeldTJunctions = false };
            scene.Add(Box(new Float3(4, 2, 2)));
            scene.Add(Box(new Float3(1, 1, 3), new Float3(-1, 0, 0)), CSGOperation.Subtractive);
            scene.Add(Box(new Float3(1, 1, 3), new Float3(1, 0, 0)), CSGOperation.Subtractive);
            var result = scene.Build();

            // Still geometrically correct (right bounds, no overlapping faces), just not stitched.
            AssertNoExcessEdges(result);
            AssertBounds(result, new Float3(-2, -1, -1), new Float3(2, 1, 1));
        }

        [Fact]
        public void FourBrushes_AllAdditiveDisjoint_KeepsEachShell()
        {
            var scene = new CSGScene();
            for (int i = 0; i < 4; i++)
                scene.Add(Box(new Float3(1, 1, 1), new Float3(i * 5, 0, 0)));
            var result = scene.Build();

            Assert.Equal(4 * 6, result.Faces.Count);
            Assert.Equal(4 * 8, result.Vertices.Count);
            AssertClosed(result);
        }

        [Fact]
        public void Build_IsIdempotent_WhenNothingChanges()
        {
            var scene = new CSGScene();
            scene.Add(Box(new Float3(2, 2, 2)));
            scene.Add(Box(new Float3(2, 2, 2), new Float3(1, 0, 0)), CSGOperation.Subtractive);

            scene.Build();
            Assert.Equal(2, scene.LastRebuildCount); // both dirty initially

            scene.Build();
            Assert.Equal(0, scene.LastRebuildCount); // nothing changed -> nothing recomputed
        }

        [Fact]
        public void MovingBrush_OnlyRecomputesTouchingBrushes()
        {
            var scene = new CSGScene();
            scene.Add(Box(new Float3(2, 2, 2)));                          // A at origin
            scene.Add(Box(new Float3(2, 2, 2), new Float3(1, 0, 0)));     // B overlaps A
            var far = scene.Add(Box(new Float3(1, 1, 1), new Float3(50, 0, 0))); // C far away
            scene.Build();

            // Nudge C within its empty region: only C recomputes (it touches nothing).
            scene.SetTransform(far, Float4x4.CreateTranslation(new Float3(50, 0, 1)));
            scene.Build();
            Assert.Equal(1, scene.LastRebuildCount);
        }

        [Fact]
        public void IncrementalResult_MatchesFullRebuild()
        {
            // Build a scene, mutate it, and compare an incremental Build to a freshly constructed scene.
            var incremental = new CSGScene();
            var a = incremental.Add(Box(new Float3(2, 2, 2)));
            incremental.Add(Box(new Float3(2, 2, 2), new Float3(1, 0, 0)), CSGOperation.Subtractive);
            incremental.Build();
            incremental.SetTransform(a, Float4x4.CreateTranslation(new Float3(0, 0, 0.5f)));
            var incrementalResult = incremental.Build();

            var fresh = new CSGScene();
            fresh.Add(Box(new Float3(2, 2, 2)), CSGOperation.Additive, Float4x4.CreateTranslation(new Float3(0, 0, 0.5f)));
            fresh.Add(Box(new Float3(2, 2, 2), new Float3(1, 0, 0)), CSGOperation.Subtractive);
            var freshResult = fresh.Build();

            TestUtil.AssertClose(freshResult.GetAABB().Min, incrementalResult.GetAABB().Min);
            TestUtil.AssertClose(freshResult.GetAABB().Max, incrementalResult.GetAABB().Max);
            Assert.Equal(freshResult.Faces.Count, incrementalResult.Faces.Count);
        }

        [Fact]
        public void RemovingBrush_RevertsItsEffect()
        {
            var scene = new CSGScene();
            scene.Add(Box(new Float3(2, 2, 2)));
            var cut = scene.Add(Box(new Float3(2, 2, 2), new Float3(1, 0, 0)), CSGOperation.Subtractive);

            scene.Build();
            scene.Remove(cut);
            var result = scene.Build();

            Assert.False(cut.IsValid);
            // Back to the full box.
            AssertBounds(result, new Float3(-1, -1, -1), new Float3(1, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void Reordering_SubtractiveBeforeAdditive_ChangesResult()
        {
            // Subtractive then additive (additive last) -> the additive fills back in: full box.
            var scene = new CSGScene();
            var solid = scene.Add(Box(new Float3(2, 2, 2)));
            var cut = scene.Add(Box(new Float3(2, 2, 2), new Float3(1, 0, 0)), CSGOperation.Subtractive);
            scene.SetOrder(solid, 1); // move the additive box after the cut -> it wins
            var result = scene.Build();

            AssertBounds(result, new Float3(-1, -1, -1), new Float3(1, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void Attributes_AreCarriedThroughTheScene()
        {
            var a = Box(new Float3(2, 2, 2));
            a.AddLoopAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
            foreach (var f in a.Faces)
            {
                var loop = f.Loop;
                do
                {
                    loop!.Attributes["uv"] = new GeometryData.FloatAttributeValue(loop.Vert.Point.X, loop.Vert.Point.Y);
                    loop = loop.Next;
                } while (loop != f.Loop);
            }

            var b = Box(new Float3(2, 2, 2), new Float3(1, 0, 0));
            b.AddLoopAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
            foreach (var f in b.Faces)
            {
                var loop = f.Loop;
                do
                {
                    loop!.Attributes["uv"] = new GeometryData.FloatAttributeValue(loop.Vert.Point.X, loop.Vert.Point.Y);
                    loop = loop.Next;
                } while (loop != f.Loop);
            }

            var scene = new CSGScene();
            scene.Add(a);
            scene.Add(b, CSGOperation.Subtractive);
            var result = scene.Build();

            Assert.True(result.HasLoopAttribute("uv"));
            foreach (var face in result.Faces)
            {
                var loop = face.Loop;
                do
                {
                    var uv = loop!.Attributes["uv"].AsFloat()!;
                    TestUtil.AssertClose(loop.Vert.Point.X, uv.Data[0], 1e-3f);
                    TestUtil.AssertClose(loop.Vert.Point.Y, uv.Data[1], 1e-3f);
                    loop = loop.Next;
                } while (loop != face.Loop);
            }
        }
    }
}
