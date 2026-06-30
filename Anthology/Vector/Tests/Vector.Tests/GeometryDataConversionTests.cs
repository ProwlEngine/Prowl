// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Xunit;

namespace Prowl.Vector.Tests
{
    public class GeometryDataConversionTests
    {
        [Fact]
        public void ToTriangleMesh_Triangle_ProducesOneTriangle()
        {
            var mesh = TestUtil.MakeTriangle();
            var tri = mesh.ToTriangleMesh();

            Assert.Equal(3, tri.Indices.Length);
            Assert.Equal(3, tri.Vertices.Length);
        }

        [Fact]
        public void ToTriangleMesh_Quad_FanTriangulatesToTwoTriangles()
        {
            var mesh = TestUtil.MakeQuad();
            var tri = mesh.ToTriangleMesh();

            // A quad fan-triangulates into 2 triangles = 6 indices.
            Assert.Equal(6, tri.Indices.Length);
            // All emitted indices must be in range.
            Assert.All(tri.Indices, i => Assert.True(i < (uint)tri.Vertices.Length));
        }

        [Fact]
        public void ToLineMesh_EmitsTwoIndicesPerEdge()
        {
            var mesh = TestUtil.MakeQuad();
            var line = mesh.ToLineMesh();

            Assert.Equal(mesh.Edges.Count * 2, line.Indices.Length);
            Assert.Equal(mesh.Edges.Count * 2, line.Vertices.Length);
        }

        [Fact]
        public void ToTriangleMesh_EmptyMesh_IsEmpty()
        {
            var mesh = new GeometryData();
            var tri = mesh.ToTriangleMesh();

            Assert.Empty(tri.Vertices);
            Assert.Empty(tri.Indices);
        }
    }
}
