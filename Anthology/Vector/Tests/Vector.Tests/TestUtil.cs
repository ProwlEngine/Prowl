// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;
using Xunit;

namespace Prowl.Vector.Tests
{
    internal static class TestUtil
    {
        public const float Eps = 1e-4f;

        public static void AssertClose(Float3 expected, Float3 actual, float eps = Eps)
        {
            Assert.True(
                Maths.Abs(expected.X - actual.X) <= eps &&
                Maths.Abs(expected.Y - actual.Y) <= eps &&
                Maths.Abs(expected.Z - actual.Z) <= eps,
                $"Expected {expected} but got {actual}");
        }

        public static void AssertClose(float expected, float actual, float eps = Eps)
        {
            Assert.True(Maths.Abs(expected - actual) <= eps, $"Expected {expected} but got {actual}");
        }

        /// <summary>
        /// Build a single unit quad in the XY plane: (0,0)-(1,0)-(1,1)-(0,1).
        /// </summary>
        public static GeometryData MakeQuad()
        {
            var mesh = new GeometryData();
            var v0 = mesh.AddVertex(0, 0, 0);
            var v1 = mesh.AddVertex(1, 0, 0);
            var v2 = mesh.AddVertex(1, 1, 0);
            var v3 = mesh.AddVertex(0, 1, 0);
            mesh.AddFace(v0, v1, v2, v3);
            return mesh;
        }

        /// <summary>
        /// Build a single triangle in the XY plane: (0,0)-(1,0)-(0,1).
        /// </summary>
        public static GeometryData MakeTriangle()
        {
            var mesh = new GeometryData();
            var v0 = mesh.AddVertex(0, 0, 0);
            var v1 = mesh.AddVertex(1, 0, 0);
            var v2 = mesh.AddVertex(0, 1, 0);
            mesh.AddFace(v0, v1, v2);
            return mesh;
        }
    }
}
