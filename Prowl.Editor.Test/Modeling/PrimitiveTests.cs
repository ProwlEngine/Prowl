// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class PrimitiveTests
{
    private static IEnumerable<(GeometryData.Face Face, Float2 UV)> Corners(GeometryData mesh)
    {
        foreach (GeometryData.Face face in mesh.Faces)
        {
            GeometryData.Loop loop = face.Loop!;
            do
            {
                float[] uv = ((GeometryData.FloatAttributeValue)loop.Attributes[MeshGeometry.UV]).Data;
                yield return (face, new Float2(uv[0], uv[1]));
                loop = loop.Next!;
            } while (loop != face.Loop);
        }
    }

    [Theory]
    [InlineData(PrimitiveKind.Cube, true)]
    [InlineData(PrimitiveKind.Cylinder, true)]
    [InlineData(PrimitiveKind.Sphere, true)]
    [InlineData(PrimitiveKind.Torus, true)]
    [InlineData(PrimitiveKind.Plane, false)]
    [InlineData(PrimitiveKind.Grid, false)]
    public void EveryPrimitiveHasUVsOnEveryCorner(PrimitiveKind kind, bool closed)
    {
        GeometryData mesh = Primitives.Make(kind);

        Assert.True(mesh.HasLoopAttribute(MeshGeometry.UV));
        Assert.Equal(closed, mesh.IsClosed());
        Assert.All(Corners(mesh), c => Assert.True(float.IsFinite(c.UV.X) && float.IsFinite(c.UV.Y)));
    }

    /// <summary>Round a cylinder the U of one face never jumps the whole way round, so no face is stretched across the seam.</summary>
    [Fact]
    public void ACylinderProjectionHasNoFaceAcrossTheSeam()
    {
        GeometryData mesh = Primitives.Make(PrimitiveKind.Cylinder);

        foreach (IGrouping<GeometryData.Face, (GeometryData.Face Face, Float2 UV)> face in Corners(mesh).Where(c => MathF.Abs(c.Face.Normal().Y) < 0.7f).GroupBy(c => c.Face))
        {
            float min = face.Min(c => c.UV.X), max = face.Max(c => c.UV.X);
            Assert.True(max - min < 0.5f);
        }
    }

    [Fact]
    public void ACubeProjectionMapsEachFaceAcrossItsOwnPlane()
    {
        GeometryData mesh = Primitives.Make(PrimitiveKind.Cube);

        foreach (IGrouping<GeometryData.Face, (GeometryData.Face Face, Float2 UV)> face in Corners(mesh).GroupBy(c => c.Face))
        {
            Assert.Equal(1f, face.Max(c => c.UV.X) - face.Min(c => c.UV.X), 3);
            Assert.Equal(1f, face.Max(c => c.UV.Y) - face.Min(c => c.UV.Y), 3);
        }
    }
}
