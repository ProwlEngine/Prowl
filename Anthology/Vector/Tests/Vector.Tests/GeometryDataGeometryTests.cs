// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Xunit;

namespace Prowl.Vector.Tests
{
    public class GeometryDataGeometryTests
    {
        [Fact]
        public void EdgeCenter_IsMidpoint()
        {
            var mesh = new GeometryData();
            var a = mesh.AddVertex(0, 0, 0);
            var b = mesh.AddVertex(2, 4, 6);
            var e = mesh.AddEdge(a, b);

            TestUtil.AssertClose(new Float3(1, 2, 3), e.Center());
        }

        [Fact]
        public void FaceCenter_IsCentroid()
        {
            var mesh = TestUtil.MakeQuad();
            TestUtil.AssertClose(new Float3(0.5f, 0.5f, 0), mesh.Faces[0].Center());
        }

        [Fact]
        public void FaceNormal_OfXYQuad_IsUnitZ()
        {
            var mesh = TestUtil.MakeQuad();
            var n = mesh.Faces[0].Normal();
            // CCW winding in the XY plane yields +Z (within sign of winding/order).
            TestUtil.AssertClose(1.0f, Maths.Abs(n.Z));
            TestUtil.AssertClose(0f, n.X);
            TestUtil.AssertClose(0f, n.Y);
        }

        [Fact]
        public void FaceNormal_IsNormalized()
        {
            var mesh = TestUtil.MakeQuad();
            var n = mesh.Faces[0].Normal();
            TestUtil.AssertClose(1.0f, Float3.Length(n));
        }

        [Fact]
        public void FaceGetAABB_BoundsTheFace()
        {
            var mesh = TestUtil.MakeQuad();
            var box = mesh.Faces[0].GetAABB();

            TestUtil.AssertClose(new Float3(0, 0, 0), box.Min);
            TestUtil.AssertClose(new Float3(1, 1, 0), box.Max);
        }

        [Fact]
        public void MeshGetAABB_BoundsAllVertices()
        {
            var mesh = new GeometryData();
            mesh.AddVertex(-1, -2, -3);
            mesh.AddVertex(4, 5, 6);
            mesh.AddVertex(0, 0, 0);

            var box = mesh.GetAABB();
            TestUtil.AssertClose(new Float3(-1, -2, -3), box.Min);
            TestUtil.AssertClose(new Float3(4, 5, 6), box.Max);
        }

        [Fact]
        public void MeshGetAABB_EmptyMesh_IsDefault()
        {
            var mesh = new GeometryData();
            var box = mesh.GetAABB();
            TestUtil.AssertClose(Float3.Zero, box.Min);
            TestUtil.AssertClose(Float3.Zero, box.Max);
        }
    }
}
