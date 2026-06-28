// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Xunit;

namespace Prowl.Vector.Tests
{
    public class GeometryDataAttributeTests
    {
        [Fact]
        public void AddVertexAttribute_AppliesDefaultToExistingVertices()
        {
            var mesh = new GeometryData();
            var v = mesh.AddVertex(0, 0, 0);

            mesh.AddVertexAttribute("uv", GeometryData.AttributeBaseType.Float, 2);

            Assert.True(mesh.HasVertexAttribute("uv"));
            Assert.True(v.Attributes.ContainsKey("uv"));
            var uv = v.Attributes["uv"].AsFloat();
            Assert.NotNull(uv);
            Assert.Equal(2, uv!.Data.Length);
            Assert.Equal(new float[] { 0, 0 }, uv.Data);
        }

        [Fact]
        public void AddVertexAttribute_AppliesDefaultToVerticesAddedLater()
        {
            var mesh = new GeometryData();
            mesh.AddVertexAttribute("id", GeometryData.AttributeBaseType.Int, 1);

            var v = mesh.AddVertex(0, 0, 0);

            Assert.True(v.Attributes.ContainsKey("id"));
            var id = v.Attributes["id"].AsInt();
            Assert.NotNull(id);
            Assert.Single(id!.Data);
            Assert.Equal(0, id.Data[0]);
        }

        [Fact]
        public void AddVertexAttribute_Duplicate_DoesNotRegisterTwice()
        {
            var mesh = new GeometryData();
            mesh.AddVertexAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
            mesh.AddVertexAttribute("uv", GeometryData.AttributeBaseType.Float, 2);

            Assert.Single(mesh.VertexAttributes);
        }

        [Fact]
        public void EdgeLoopFaceAttributes_AreEnsuredOnNewElements()
        {
            var mesh = new GeometryData();
            mesh.AddEdgeAttribute("crease", GeometryData.AttributeBaseType.Float, 1);
            mesh.AddLoopAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
            mesh.AddFaceAttribute("material", GeometryData.AttributeBaseType.Int, 1);

            var meshQuad = TestUtil.MakeQuad();
            // Re-add the attributes onto the populated mesh and verify they propagate.
            meshQuad.AddEdgeAttribute("crease", GeometryData.AttributeBaseType.Float, 1);
            meshQuad.AddLoopAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
            meshQuad.AddFaceAttribute("material", GeometryData.AttributeBaseType.Int, 1);

            Assert.All(meshQuad.Edges, e => Assert.True(e.Attributes.ContainsKey("crease")));
            Assert.All(meshQuad.Loops, l => Assert.True(l.Attributes.ContainsKey("uv")));
            Assert.All(meshQuad.Faces, f => Assert.True(f.Attributes.ContainsKey("material")));
        }

        [Fact]
        public void AttributeValueCopy_IsDeepCopy()
        {
            var original = new GeometryData.FloatAttributeValue(1, 2, 3);
            var copy = GeometryData.AttributeValue.Copy(original).AsFloat();

            Assert.NotNull(copy);
            Assert.NotSame(original.Data, copy!.Data);
            copy.Data[0] = 99;
            Assert.Equal(1, original.Data[0]); // original unaffected
        }

        [Fact]
        public void DefaultValuesAreIndependentPerElement()
        {
            var mesh = new GeometryData();
            mesh.AddVertexAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
            var a = mesh.AddVertex(0, 0, 0);
            var b = mesh.AddVertex(1, 0, 0);

            // Mutating one vertex's attribute must not bleed into another.
            a.Attributes["uv"].AsFloat()!.Data[0] = 5;
            Assert.Equal(0, b.Attributes["uv"].AsFloat()!.Data[0]);
        }

        [Fact]
        public void FloatAttributeValue_Vector3RoundTrips()
        {
            var val = new GeometryData.FloatAttributeValue(0, 0, 0);
            val.FromVector3(new Float3(7, 8, 9));
            TestUtil.AssertClose(new Float3(7, 8, 9), val.AsVector3());
        }

        [Fact]
        public void AttributeType_CheckValue_ValidatesDimensionAndBaseType()
        {
            var floatType = new GeometryData.AttributeType { BaseType = GeometryData.AttributeBaseType.Float, Dimensions = 3 };

            Assert.True(floatType.CheckValue(new GeometryData.FloatAttributeValue(1, 2, 3)));
            Assert.False(floatType.CheckValue(new GeometryData.FloatAttributeValue(1, 2)));      // wrong dimension
            Assert.False(floatType.CheckValue(new GeometryData.IntAttributeValue(1, 2, 3)));     // wrong base type
        }
    }
}
