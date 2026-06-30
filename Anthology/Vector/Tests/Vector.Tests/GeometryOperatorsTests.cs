// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Xunit;

namespace Prowl.Vector.Tests
{
    public class GeometryOperatorsTests
    {
        [Fact]
        public void Scale_ScalesAllVerticesFromOrigin()
        {
            var mesh = new GeometryData();
            var v = mesh.AddVertex(1, 2, 3);

            GeometryOperators.Scale(mesh, 2.0f);

            TestUtil.AssertClose(new Float3(2, 4, 6), v.Point);
        }

        [Fact]
        public void Translate_OffsetsAllVertices()
        {
            var mesh = new GeometryData();
            var v = mesh.AddVertex(1, 1, 1);

            GeometryOperators.Translate(mesh, new Float3(1, 2, 3));

            TestUtil.AssertClose(new Float3(2, 3, 4), v.Point);
        }

        [Fact]
        public void ScaleFace_ScalesAboutFaceCenter()
        {
            var mesh = TestUtil.MakeQuad();
            var center = mesh.Faces[0].Center();

            GeometryOperators.ScaleFace(mesh, mesh.Faces[0], 2.0f);

            // Center is invariant under a uniform scale about the center.
            TestUtil.AssertClose(center, mesh.Faces[0].Center());
            // Corner (0,0) should move to twice its distance from the (0.5,0.5) center.
            var corner = mesh.Vertices.First(v => Maths.Abs(v.Point.X - (-0.5f)) < 1e-3f);
            TestUtil.AssertClose(new Float3(-0.5f, -0.5f, 0), corner.Point);
        }

        [Fact]
        public void TranslateFaces_MovesSharedVerticesOnce()
        {
            var mesh = new GeometryData();
            var v0 = mesh.AddVertex(0, 0, 0);
            var v1 = mesh.AddVertex(1, 0, 0);
            var v2 = mesh.AddVertex(1, 1, 0);
            var v3 = mesh.AddVertex(0, 1, 0);
            var f0 = mesh.AddFace(v0, v1, v2)!;
            var f1 = mesh.AddFace(v0, v2, v3)!;

            // v0 and v2 are shared by both faces; they must only move once.
            GeometryOperators.TranslateFaces(mesh, new[] { f0, f1 }, new Float3(0, 0, 1));

            TestUtil.AssertClose(new Float3(0, 0, 1), v0.Point);
            TestUtil.AssertClose(new Float3(1, 1, 1), v2.Point);
        }

        [Fact]
        public void Translate_ThenScale_GetAABBReflectsChange()
        {
            var mesh = TestUtil.MakeQuad();
            GeometryOperators.Scale(mesh, 2.0f);

            var box = mesh.GetAABB();
            TestUtil.AssertClose(new Float3(0, 0, 0), box.Min);
            TestUtil.AssertClose(new Float3(2, 2, 0), box.Max);
        }

        [Fact]
        public void RecalculateNormals_ProducesUnitNormals()
        {
            var mesh = TestUtil.MakeQuad();

            GeometryOperators.RecalculateNormals(mesh);

            Assert.True(mesh.HasVertexAttribute("normal"));
            foreach (var v in mesh.Vertices)
            {
                var n = v.Attributes["normal"].AsFloat()!.AsVector3();
                TestUtil.AssertClose(1.0f, Float3.Length(n));
                TestUtil.AssertClose(1.0f, Maths.Abs(n.Z)); // XY quad -> normal along Z
            }
        }

        [Fact]
        public void Triangulate_ConvertsQuadIntoTriangles()
        {
            var mesh = TestUtil.MakeQuad();

            GeometryOperators.Triangulate(mesh);

            Assert.Equal(2, mesh.Faces.Count);
            Assert.All(mesh.Faces, f => Assert.Equal(3, f.VertCount));
        }

        [Fact]
        public void Triangulate_LeavesTrianglesUnchanged()
        {
            var mesh = TestUtil.MakeTriangle();

            GeometryOperators.Triangulate(mesh);

            Assert.Single(mesh.Faces);
            Assert.Equal(3, mesh.Faces[0].VertCount);
        }

        [Fact]
        public void AttributeLerp_InterpolatesFloatAttributes()
        {
            var mesh = new GeometryData();
            mesh.AddVertexAttribute("value", GeometryData.AttributeBaseType.Float, 1);
            var a = mesh.AddVertex(0, 0, 0);
            var b = mesh.AddVertex(1, 0, 0);
            var dst = mesh.AddVertex(0.5f, 0, 0);

            a.Attributes["value"] = new GeometryData.FloatAttributeValue(0);
            b.Attributes["value"] = new GeometryData.FloatAttributeValue(10);

            GeometryOperators.AttributeLerp(mesh, dst, a, b, 0.25f);

            TestUtil.AssertClose(2.5f, dst.Attributes["value"].AsFloat()!.Data[0]);
        }

        [Fact]
        public void WeldVertices_MergesCoincidentVertices()
        {
            var mesh = new GeometryData();
            mesh.AddVertex(0, 0, 0);
            mesh.AddVertex(0.00001f, 0, 0); // within default threshold

            int welded = GeometryOperators.WeldVertices(mesh);

            Assert.Equal(1, welded);
            Assert.Single(mesh.Vertices);
        }
    }
}
