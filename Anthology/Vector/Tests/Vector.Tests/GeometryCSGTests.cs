// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Xunit;

namespace Prowl.Vector.Tests
{
    public class GeometryCSGTests
    {
        // Box A occupies [-1,1]^3.
        private static GeometryData BoxA() => GeometryGenerator.Box(new Float3(2, 2, 2));

        // Box B occupies [0,2] x [-1,1] x [-1,1] — same Y/Z cross-section as A, shifted +1 in X.
        // The two boxes therefore union/intersect/subtract into exact axis-aligned boxes.
        private static GeometryData BoxB() => GeometryGenerator.Box(new Float3(2, 2, 2), new Float3(1, 0, 0));

        private static void AssertBounds(GeometryData mesh, Float3 min, Float3 max)
        {
            var box = mesh.GetAABB();
            TestUtil.AssertClose(min, box.Min);
            TestUtil.AssertClose(max, box.Max);
        }

        // A watertight result has every edge shared by exactly two faces.
        private static void AssertClosed(GeometryData mesh)
        {
            Assert.NotEmpty(mesh.Faces);
            foreach (var edge in mesh.Edges)
                Assert.Equal(2, edge.NeighborFaces().Count);
        }

        [Fact]
        public void Union_OfOverlappingBoxes_IsTheCombinedBox()
        {
            var result = GeometryCSG.Union(BoxA(), BoxB());
            AssertBounds(result, new Float3(-1, -1, -1), new Float3(2, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void Intersection_OfOverlappingBoxes_IsTheSharedBox()
        {
            var result = GeometryCSG.Intersect(BoxA(), BoxB());
            AssertBounds(result, new Float3(0, -1, -1), new Float3(1, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void Subtraction_RemovesTheOverlappingHalf()
        {
            var result = GeometryCSG.Subtraction(BoxA(), BoxB());
            // A minus B leaves the slab x in [-1, 0].
            AssertBounds(result, new Float3(-1, -1, -1), new Float3(0, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void Union_OfIdenticalBoxes_IsTheSameBox()
        {
            var result = GeometryCSG.Union(BoxA(), BoxA());
            AssertBounds(result, new Float3(-1, -1, -1), new Float3(1, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void Intersection_OfIdenticalBoxes_IsTheSameBox()
        {
            var result = GeometryCSG.Intersect(BoxA(), BoxA());
            AssertBounds(result, new Float3(-1, -1, -1), new Float3(1, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void Union_OfDisjointBoxes_KeepsBothShells()
        {
            var a = BoxA();
            var b = GeometryGenerator.Box(new Float3(2, 2, 2), new Float3(5, 0, 0)); // far away
            var result = GeometryCSG.Union(a, b);

            AssertBounds(result, new Float3(-1, -1, -1), new Float3(6, 1, 1));
            // Two separate closed boxes: 12 quad faces, 16 vertices.
            Assert.Equal(12, result.Faces.Count);
            Assert.Equal(16, result.Vertices.Count);
            AssertClosed(result);
        }

        [Fact]
        public void Intersection_OfDisjointBoxes_IsEmpty()
        {
            var a = BoxA();
            var b = GeometryGenerator.Box(new Float3(2, 2, 2), new Float3(5, 0, 0));
            var result = GeometryCSG.Intersect(a, b);

            Assert.Empty(result.Faces);
            Assert.Empty(result.Vertices);
        }

        [Fact]
        public void Subtraction_OfDisjointBox_LeavesOriginal()
        {
            var a = BoxA();
            var b = GeometryGenerator.Box(new Float3(2, 2, 2), new Float3(5, 0, 0));
            var result = GeometryCSG.Subtraction(a, b);

            AssertBounds(result, new Float3(-1, -1, -1), new Float3(1, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void Subtraction_WhereBContainsA_IsEmpty()
        {
            var a = BoxA();                                       // [-1,1]^3
            var b = GeometryGenerator.Box(new Float3(4, 4, 4));   // [-2,2]^3 contains A
            var result = GeometryCSG.Subtraction(a, b);

            Assert.Empty(result.Faces);
        }

        [Fact]
        public void Intersection_WhereBContainsA_IsA()
        {
            var a = BoxA();
            var b = GeometryGenerator.Box(new Float3(4, 4, 4));
            var result = GeometryCSG.Intersect(a, b);

            AssertBounds(result, new Float3(-1, -1, -1), new Float3(1, 1, 1));
            AssertClosed(result);
        }

        [Fact]
        public void Subtraction_BoxFromIcosahedron_IsClosedAndBounded()
        {
            // Mirrors the demo: a triangular-faced convex solid (icosahedron) minus a box.
            var ico = GeometryGenerator.Icosphere(0.6f, subdivisions: 0);
            var box = GeometryGenerator.Box(new Float3(0.6f));
            var result = GeometryCSG.Subtraction(ico, box);

            Assert.NotEmpty(result.Faces);
            AssertClosed(result);

            // Result cannot extend past the icosahedron's own bounds.
            var icoBox = ico.GetAABB();
            var box2 = result.GetAABB();
            Assert.True(box2.Min.X >= icoBox.Min.X - 1e-3f && box2.Max.X <= icoBox.Max.X + 1e-3f);
            Assert.True(box2.Min.Y >= icoBox.Min.Y - 1e-3f && box2.Max.Y <= icoBox.Max.Y + 1e-3f);
            Assert.True(box2.Min.Z >= icoBox.Min.Z - 1e-3f && box2.Max.Z <= icoBox.Max.Z + 1e-3f);
        }

        [Fact]
        public void NonCubeConvex_SubtractionStaysWithinOriginalBounds()
        {
            // A diagonal box poked into A; result must never exceed A's bounds.
            var a = BoxA();
            var b = GeometryGenerator.Box(new Float3(1.5f, 1.5f, 1.5f), new Float3(1, 1, 1));
            var result = GeometryCSG.Subtraction(a, b);

            var box = result.GetAABB();
            Assert.True(box.Min.X >= -1 - 1e-3f && box.Min.Y >= -1 - 1e-3f && box.Min.Z >= -1 - 1e-3f);
            Assert.True(box.Max.X <= 1 + 1e-3f && box.Max.Y <= 1 + 1e-3f && box.Max.Z <= 1 + 1e-3f);
            Assert.NotEmpty(result.Faces);
        }
    }
}
