// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Xunit;

namespace Prowl.Vector.Tests
{
    /// <summary>
    /// Regression tests covering bugs fixed during review.
    /// </summary>
    public class GeometryDataFixTests
    {
        [Fact]
        public void FindEdge_FindsEdge_WhenVertexRingsDifferInLength()
        {
            // Build a fan so the two endpoints of a target edge have very different edge-ring sizes.
            var mesh = new GeometryData();
            var hub = mesh.AddVertex(0, 0, 0);
            var spokes = Enumerable.Range(0, 6)
                .Select(i => mesh.AddVertex(i + 1, 0, 0))
                .ToArray();

            // hub gets a large edge ring; the last spoke gets a ring of size 1.
            foreach (var s in spokes)
                mesh.AddEdge(hub, s);

            var target = spokes[5];
            var found = mesh.FindEdge(hub, target);

            Assert.NotNull(found);
            Assert.True(found!.ContainsVertex(hub));
            Assert.True(found.ContainsVertex(target));
            // And symmetric lookup agrees.
            Assert.Same(found, mesh.FindEdge(target, hub));
        }

        [Fact]
        public void AddFace_WithFewerThanThreeVertices_ReturnsNullAndAddsNothing()
        {
            var mesh = new GeometryData();
            var a = mesh.AddVertex(0, 0, 0);
            var b = mesh.AddVertex(1, 0, 0);

            Assert.Null(mesh.AddFace(a));        // single vertex
            Assert.Null(mesh.AddFace(a, b));     // two vertices

            Assert.Empty(mesh.Faces);
            Assert.Empty(mesh.Edges);
            Assert.Empty(mesh.Loops);
        }

        [Fact]
        public void AddVertexAttribute_Duplicate_ReturnsRegisteredDefinition()
        {
            var mesh = new GeometryData();
            var first = mesh.AddVertexAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
            var second = mesh.AddVertexAttribute("uv", GeometryData.AttributeBaseType.Float, 2);

            // The duplicate call must return the definition actually stored in the mesh.
            Assert.Same(first, second);
            Assert.Same(first, mesh.VertexAttributes.Single());
        }

        [Fact]
        public void AddEdgeFaceLoopAttribute_Duplicate_ReturnsRegisteredDefinition()
        {
            var mesh = new GeometryData();
            var e1 = mesh.AddEdgeAttribute("crease", GeometryData.AttributeBaseType.Float, 1);
            var e2 = mesh.AddEdgeAttribute("crease", GeometryData.AttributeBaseType.Float, 1);
            var l1 = mesh.AddLoopAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
            var l2 = mesh.AddLoopAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
            var f1 = mesh.AddFaceAttribute("material", GeometryData.AttributeBaseType.Int, 1);
            var f2 = mesh.AddFaceAttribute("material", GeometryData.AttributeBaseType.Int, 1);

            Assert.Same(e1, e2);
            Assert.Same(l1, l2);
            Assert.Same(f1, f2);
            Assert.Same(e1, mesh.EdgeAttributes.Single());
            Assert.Same(l1, mesh.LoopAttributes.Single());
            Assert.Same(f1, mesh.FaceAttributes.Single());
        }

        [Fact]
        public void AttributeLerp_ExtrapolatesBeyondUnitRange()
        {
            var mesh = new GeometryData();
            mesh.AddVertexAttribute("value", GeometryData.AttributeBaseType.Float, 1);
            var a = mesh.AddVertex(0, 0, 0);
            var b = mesh.AddVertex(1, 0, 0);
            var dst = mesh.AddVertex(2, 0, 0);

            a.Attributes["value"] = new GeometryData.FloatAttributeValue(0);
            b.Attributes["value"] = new GeometryData.FloatAttributeValue(10);

            // t = 2 must extrapolate to 20 (clamped lerp would yield 10).
            GeometryOperators.AttributeLerp(mesh, dst, a, b, 2.0f);
            TestUtil.AssertClose(20f, dst.Attributes["value"].AsFloat()!.Data[0]);

            // t = -1 must extrapolate to -10.
            GeometryOperators.AttributeLerp(mesh, dst, a, b, -1.0f);
            TestUtil.AssertClose(-10f, dst.Attributes["value"].AsFloat()!.Data[0]);
        }
    }
}
