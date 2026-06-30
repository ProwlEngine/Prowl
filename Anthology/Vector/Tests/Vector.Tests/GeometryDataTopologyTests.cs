// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Xunit;

namespace Prowl.Vector.Tests
{
    public class GeometryDataTopologyTests
    {
        [Fact]
        public void AddVertex_StoresPosition()
        {
            var mesh = new GeometryData();
            var v = mesh.AddVertex(1, 2, 3);

            Assert.Single(mesh.Vertices);
            TestUtil.AssertClose(new Float3(1, 2, 3), v.Point);
            Assert.Null(v.Edge);
        }

        [Fact]
        public void AddEdge_CreatesEdgeAndWiresVertices()
        {
            var mesh = new GeometryData();
            var a = mesh.AddVertex(0, 0, 0);
            var b = mesh.AddVertex(1, 0, 0);

            var e = mesh.AddEdge(a, b);

            Assert.Single(mesh.Edges);
            Assert.True(e.ContainsVertex(a));
            Assert.True(e.ContainsVertex(b));
            Assert.Same(b, e.OtherVertex(a));
            Assert.Same(a, e.OtherVertex(b));
            Assert.NotNull(a.Edge);
            Assert.NotNull(b.Edge);
        }

        [Fact]
        public void AddEdge_IsDeduplicated()
        {
            var mesh = new GeometryData();
            var a = mesh.AddVertex(0, 0, 0);
            var b = mesh.AddVertex(1, 0, 0);

            var e1 = mesh.AddEdge(a, b);
            var e2 = mesh.AddEdge(a, b);
            var e3 = mesh.AddEdge(b, a); // reversed order, still same edge

            Assert.Same(e1, e2);
            Assert.Same(e1, e3);
            Assert.Single(mesh.Edges);
        }

        [Fact]
        public void FindEdge_ReturnsExistingEdge_OrNull()
        {
            var mesh = new GeometryData();
            var a = mesh.AddVertex(0, 0, 0);
            var b = mesh.AddVertex(1, 0, 0);
            var c = mesh.AddVertex(2, 0, 0);
            var e = mesh.AddEdge(a, b);

            Assert.Same(e, mesh.FindEdge(a, b));
            Assert.Same(e, mesh.FindEdge(b, a));
            Assert.Null(mesh.FindEdge(a, c));
        }

        [Fact]
        public void AddFace_CreatesLoopsEdgesAndVertCount()
        {
            var mesh = TestUtil.MakeQuad();

            Assert.Single(mesh.Faces);
            Assert.Equal(4, mesh.Edges.Count);
            Assert.Equal(4, mesh.Loops.Count);

            var face = mesh.Faces[0];
            Assert.Equal(4, face.VertCount);
            Assert.Equal(4, face.NeighborVertices().Count);
            Assert.Equal(4, face.NeighborEdges().Count);
        }

        [Fact]
        public void AddFace_SharedEdgeBetweenTwoFaces_IsReused()
        {
            var mesh = new GeometryData();
            var v0 = mesh.AddVertex(0, 0, 0);
            var v1 = mesh.AddVertex(1, 0, 0);
            var v2 = mesh.AddVertex(1, 1, 0);
            var v3 = mesh.AddVertex(0, 1, 0);

            mesh.AddFace(v0, v1, v2);
            mesh.AddFace(v0, v2, v3);

            // 5 edges total: the v0-v2 diagonal is shared, not duplicated.
            Assert.Equal(5, mesh.Edges.Count);
            Assert.Equal(2, mesh.Faces.Count);

            var shared = mesh.FindEdge(v0, v2);
            Assert.NotNull(shared);
            // Shared edge is used by exactly two faces.
            Assert.Equal(2, shared!.NeighborFaces().Count);
        }

        [Fact]
        public void NeighborVertices_AreFaceMembers()
        {
            var mesh = TestUtil.MakeQuad();
            var face = mesh.Faces[0];

            var verts = face.NeighborVertices();
            foreach (var v in mesh.Vertices)
                Assert.Contains(v, verts);
        }

        [Fact]
        public void VertexNeighborEdges_ListsAllConnected()
        {
            var mesh = TestUtil.MakeQuad();
            // Each corner of a quad has exactly two incident edges.
            foreach (var v in mesh.Vertices)
                Assert.Equal(2, v.NeighborEdges().Count);
        }

        [Fact]
        public void VertexNeighborFaces_ListsIncidentFaces()
        {
            var mesh = TestUtil.MakeQuad();
            foreach (var v in mesh.Vertices)
                Assert.Single(v.NeighborFaces());
        }

        [Fact]
        public void RemoveFace_RemovesLoopsButKeepsVerticesAndEdges()
        {
            var mesh = TestUtil.MakeQuad();
            var face = mesh.Faces[0];

            mesh.RemoveFace(face);

            Assert.Empty(mesh.Faces);
            Assert.Empty(mesh.Loops);
            // Edges and vertices remain.
            Assert.Equal(4, mesh.Edges.Count);
            Assert.Equal(4, mesh.Vertices.Count);
        }

        [Fact]
        public void RemoveEdge_RemovesAdjacentFace()
        {
            var mesh = TestUtil.MakeQuad();
            var edge = mesh.Edges[0];

            mesh.RemoveEdge(edge);

            Assert.Equal(3, mesh.Edges.Count);
            // The face touched the removed edge, so it is gone.
            Assert.Empty(mesh.Faces);
            Assert.Empty(mesh.Loops);
        }

        [Fact]
        public void RemoveVertex_CascadesEdgesAndFaces()
        {
            var mesh = TestUtil.MakeQuad();
            var v = mesh.Vertices[0];

            mesh.RemoveVertex(v);

            Assert.Equal(3, mesh.Vertices.Count);
            // The two edges touching v are removed.
            Assert.Equal(2, mesh.Edges.Count);
            Assert.Empty(mesh.Faces);
        }

        [Fact]
        public void RemoveVertex_UnwiresRemainingVertices()
        {
            var mesh = new GeometryData();
            var a = mesh.AddVertex(0, 0, 0);
            var b = mesh.AddVertex(1, 0, 0);
            mesh.AddEdge(a, b);

            mesh.RemoveVertex(a);

            Assert.Single(mesh.Vertices);
            Assert.Empty(mesh.Edges);
            // b should no longer reference the removed edge.
            Assert.Null(b.Edge);
        }
    }
}
