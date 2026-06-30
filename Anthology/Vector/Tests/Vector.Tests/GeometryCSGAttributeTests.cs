// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Xunit;

namespace Prowl.Vector.Tests
{
    public class GeometryCSGAttributeTests
    {
        // Builds a box carrying attributes that are exact linear functions of position, so that any
        // correct interpolation must reproduce them exactly:
        //   loop "uv"      = (x, y)
        //   vertex "temp"  = x
        //   face "material"= material (constant)
        //   edge "crease"  = 1 on every source edge
        private static GeometryData AttributedBox(Float3 size, Float3 center, int material, Int3 segments = default)
        {
            var mesh = GeometryGenerator.Box(size, center, segments);

            mesh.AddVertexAttribute("temp", GeometryData.AttributeBaseType.Float, 1);
            mesh.AddLoopAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
            mesh.AddFaceAttribute("material", GeometryData.AttributeBaseType.Int, 1);
            mesh.AddEdgeAttribute("crease", GeometryData.AttributeBaseType.Float, 1);

            foreach (var v in mesh.Vertices)
                v.Attributes["temp"] = new GeometryData.FloatAttributeValue(v.Point.X);

            foreach (var f in mesh.Faces)
            {
                f.Attributes["material"] = new GeometryData.IntAttributeValue(material);

                var loop = f.Loop;
                do
                {
                    loop!.Attributes["uv"] = new GeometryData.FloatAttributeValue(loop.Vert.Point.X, loop.Vert.Point.Y);
                    loop = loop.Next;
                } while (loop != f.Loop);
            }

            foreach (var e in mesh.Edges)
                e.Attributes["crease"] = new GeometryData.FloatAttributeValue(1);

            return mesh;
        }

        private static GeometryData BoxA(int material = 7, Int3 segments = default)
            => AttributedBox(new Float3(2, 2, 2), Float3.Zero, material, segments);

        private static GeometryData BoxB(int material = 9)
            => AttributedBox(new Float3(2, 2, 2), new Float3(1, 0, 0), material);

        [Fact]
        public void Result_RegistersAttributeSchemaFromInputs()
        {
            var result = GeometryCSG.Subtraction(BoxA(), BoxB());

            Assert.True(result.HasVertexAttribute("temp"));
            Assert.True(result.HasLoopAttribute("uv"));
            Assert.True(result.HasFaceAttribute("material"));
            Assert.True(result.HasEdgeAttribute("crease"));
        }

        [Fact]
        public void LoopUVs_AreInterpolatedExactlyAcrossCutFaces()
        {
            // Both inputs carry uv = (x, y); interpolation must reproduce it at every output corner,
            // including the new vertices created along the cut.
            var result = GeometryCSG.Subtraction(BoxA(), BoxB());

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

        [Fact]
        public void VertexAttributes_AreInterpolatedExactly()
        {
            var result = GeometryCSG.Union(BoxA(), BoxB());

            foreach (var v in result.Vertices)
            {
                var temp = v.Attributes["temp"].AsFloat()!;
                TestUtil.AssertClose(v.Point.X, temp.Data[0], 1e-3f);
            }
        }

        [Fact]
        public void FaceAttributes_AreCarriedFromBothSources()
        {
            var result = GeometryCSG.Subtraction(BoxA(material: 7), BoxB(material: 9));

            var materials = result.Faces
                .Select(f => f.Attributes["material"].AsInt()!.Data[0])
                .ToHashSet();

            // A's outer walls keep 7; B's inside cap (flipped in) keeps 9. Never the default 0.
            Assert.Contains(7, materials);
            Assert.Contains(9, materials);
            Assert.DoesNotContain(0, materials);
        }

        [Fact]
        public void EdgeAttributes_AreCarriedOnSurvivingBoundaryEdges()
        {
            var result = GeometryCSG.Subtraction(BoxA(), BoxB());

            int creased = result.Edges.Count(e =>
                e.Attributes.TryGetValue("crease", out var value) &&
                value.AsFloat() is { } f && f.Data.Length > 0 && Maths.Abs(f.Data[0] - 1f) < 1e-3f);

            // Edges lying along original box edges keep crease = 1 (cut edges stay at the default 0).
            Assert.True(creased > 0);
        }

        [Fact]
        public void CoplanarSourceFaces_StillInterpolateCorrectly()
        {
            // Subdivided box: each side is several coplanar quads, so one plane maps to multiple source
            // faces. The per-corner containment search must still pick the right one.
            var a = BoxA(material: 7, segments: new Int3(2, 2, 2));
            var b = BoxB();
            var result = GeometryCSG.Subtraction(a, b);

            Assert.NotEmpty(result.Faces);
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

        [Fact]
        public void NoAttributes_ProducesPlainGeometry()
        {
            // Inputs without attributes keep the fast path: result has no attribute schema.
            var a = GeometryGenerator.Box(new Float3(2, 2, 2));
            var b = GeometryGenerator.Box(new Float3(2, 2, 2), new Float3(1, 0, 0));
            var result = GeometryCSG.Union(a, b);

            Assert.Empty(result.VertexAttributes);
            Assert.Empty(result.LoopAttributes);
            Assert.NotEmpty(result.Faces);
        }
    }
}
