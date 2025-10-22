// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Represents a 3D triangle defined by three vertices.
    /// </summary>
    public struct Triangle : IEquatable<Triangle>, IFormattable, IBoundingShape
    {
        /// <summary>The first vertex of the triangle.</summary>
        public Double3 V0;

        /// <summary>The second vertex of the triangle.</summary>
        public Double3 V1;

        /// <summary>The third vertex of the triangle.</summary>
        public Double3 V2;

        /// <summary>
        /// Initializes a new triangle from three vertices.
        /// </summary>
        /// <param name="v0">First vertex.</param>
        /// <param name="v1">Second vertex.</param>
        /// <param name="v2">Third vertex.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Triangle(Double3 v0, Double3 v1, Double3 v2)
        {
            V0 = v0;
            V1 = v1;
            V2 = v2;
        }

        /// <summary>
        /// Gets the normal vector of the triangle (not normalized).
        /// Direction follows right-hand rule based on vertex order.
        /// </summary>
        public Double3 Normal
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Double3.Cross(V1 - V0, V2 - V0);
        }

        /// <summary>
        /// Gets the normalized normal vector of the triangle.
        /// </summary>
        public Double3 NormalizedNormal
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Double3.Normalize(Normal);
        }

        /// <summary>
        /// Gets the area of the triangle.
        /// </summary>
        public double Area
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Double3.Length(Normal) / 2.0;
        }

        /// <summary>
        /// Gets the centroid (center point) of the triangle.
        /// </summary>
        public Double3 Centroid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (V0 + V1 + V2) / new Double3(3, 3, 3);
        }

        /// <summary>
        /// Gets the perimeter of the triangle.
        /// </summary>
        public double Perimeter
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Double3.Length(V1 - V0) + Double3.Length(V2 - V1) + Double3.Length(V0 - V2);
        }

        /// <summary>
        /// Gets a vertex by index (0, 1, or 2).
        /// </summary>
        public Double3 this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                switch (index)
                {
                    case 0: return V0;
                    case 1: return V1;
                    case 2: return V2;
                    default: throw new IndexOutOfRangeException("Triangle vertex index must be 0, 1, or 2.");
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                switch (index)
                {
                    case 0: V0 = value; break;
                    case 1: V1 = value; break;
                    case 2: V2 = value; break;
                    default: throw new IndexOutOfRangeException("Triangle vertex index must be 0, 1, or 2.");
                }
            }
        }

        /// <summary>
        /// Calculates the barycentric coordinates of a point with respect to this triangle.
        /// </summary>
        /// <param name="point">The point to calculate coordinates for.</param>
        /// <param name="u">Barycentric coordinate u (weight for V1).</param>
        /// <param name="v">Barycentric coordinate v (weight for V2).</param>
        /// <remarks>w (weight for V0) = 1 - u - v.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetBarycentricCoordinates(Double3 point, out double u, out double v)
        {
            Double3 v0v1 = V1 - V0;
            Double3 v0v2 = V2 - V0;
            Double3 v0p = point - V0;

            double dot00 = Double3.Dot(v0v2, v0v2);
            double dot01 = Double3.Dot(v0v2, v0v1);
            double dot02 = Double3.Dot(v0v2, v0p);
            double dot11 = Double3.Dot(v0v1, v0v1);
            double dot12 = Double3.Dot(v0v1, v0p);

            double invDenom = 1.0 / (dot00 * dot11 - dot01 * dot01);
            u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            v = (dot00 * dot12 - dot01 * dot02) * invDenom;
        }

        /// <summary>
        /// Checks if a point (defined by barycentric coordinates) is inside this triangle.
        /// </summary>
        /// <param name="u">Barycentric coordinate u.</param>
        /// <param name="v">Barycentric coordinate v.</param>
        /// <returns>True if the point is inside or on the edge of the triangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPointInTriangle(double u, double v)
        {
            return u >= 0.0 && v >= 0.0 && (u + v) <= 1.0;
        }

        /// <summary>
        /// Gets a point on the triangle using barycentric coordinates.
        /// </summary>
        /// <param name="u">Barycentric coordinate u (weight for V1).</param>
        /// <param name="v">Barycentric coordinate v (weight for V2).</param>
        /// <returns>The interpolated point on the triangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 GetPointFromBarycentric(double u, double v)
        {
            double w = 1.0 - u - v;
            return V0 * w + V1 * u + V2 * v;
        }

        /// <summary>
        /// Finds the closest point on this triangle to a given point.
        /// </summary>
        /// <param name="point">The point to find the closest point to.</param>
        /// <returns>The closest point on the triangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 ClosestPointTo(Double3 point)
        {
            GetBarycentricCoordinates(point, out double u, out double v);
            
            // Clamp barycentric coordinates to triangle bounds
            if (u < 0.0) u = 0.0;
            if (v < 0.0) v = 0.0;
            if (u + v > 1.0)
            {
                double sum = u + v;
                u /= sum;
                v /= sum;
            }
            
            return GetPointFromBarycentric(u, v);
        }

        /// <summary>
        /// Gets the plane containing this triangle.
        /// </summary>
        /// <returns>The plane containing the triangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Plane GetPlane()
        {
            return Plane.FromNormalAndPoint(NormalizedNormal, V0);
        }

        /// <summary>
        /// Checks if this triangle is degenerate (has zero area).
        /// </summary>
        /// <returns>True if the triangle is degenerate.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDegenerate()
        {
            return Double3.LengthSquared(Normal) < double.Epsilon * double.Epsilon;
        }

        /// <summary>
        /// Transforms this triangle by a 4x4 matrix.
        /// </summary>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>The transformed triangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Triangle Transform(Double4x4 matrix)
        {
            return new Triangle(
                Double4x4.TransformPoint(V0, matrix),
                Double4x4.TransformPoint(V1, matrix),
                Double4x4.TransformPoint(V2, matrix)
            );
        }

        /// <summary>
        /// Calculates the signed volume between this triangle and a point.
        /// Used for determining orientation in 3D space.
        /// </summary>
        /// <param name="point">The point to test against.</param>
        /// <returns>The signed volume (positive if point is on normal side).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double SignedVolumeToPoint(Double3 point)
        {
            return Double3.Dot(Normal, point - V0) / 6;
        }

        /// <summary>
        /// Gets the edge vector for the specified edge index.
        /// </summary>
        /// <param name="edgeIndex">Edge index (0 = V0->V1, 1 = V1->V2, 2 = V2->V0).</param>
        /// <returns>The edge vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 GetEdge(int edgeIndex)
        {
            switch (edgeIndex)
            {
                case 0: return V1 - V0;
                case 1: return V2 - V1;
                case 2: return V0 - V2;
                default: throw new IndexOutOfRangeException("Edge index must be 0, 1, or 2.");
            }
        }

        /// <summary>
        /// Gets the length of the specified edge.
        /// </summary>
        /// <param name="edgeIndex">Edge index (0 = V0->V1, 1 = V1->V2, 2 = V2->V0).</param>
        /// <returns>The edge length.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetEdgeLength(int edgeIndex)
        {
            return Double3.Length(GetEdge(edgeIndex));
        }

        /// <summary>
        /// Checks if a point is coplanar with this triangle within a tolerance.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <param name="tolerance">The tolerance for the test.</param>
        /// <returns>True if the point is coplanar.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsCoplanar(Double3 point, double tolerance = double.Epsilon)
        {
            Double3 normal = NormalizedNormal;
            double distance = Maths.Abs(Double3.Dot(normal, point - V0));
            return distance <= tolerance;
        }

        /// <summary>
        /// Reverses the winding order of the triangle (flips the normal).
        /// </summary>
        /// <returns>A triangle with reversed winding order.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Triangle Reversed()
        {
            return new Triangle(V0, V2, V1);
        }

        /// <summary>
        /// Calculates the circumcenter of the triangle.
        /// </summary>
        /// <returns>The circumcenter point.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 GetCircumcenter()
        {
            Double3 a = V1 - V0;
            Double3 b = V2 - V0;
            Double3 cross = Double3.Cross(a, b);
            double denom = 2.0 * Double3.Dot(cross, cross);
            
            if (Maths.Abs(denom) < double.Epsilon)
                return Centroid; // Fallback for degenerate triangle
                
            Double3 result = Double3.Cross(Double3.Cross(cross, a) * Double3.LengthSquared(b) +
                                                 Double3.Cross(b, cross) * Double3.LengthSquared(a), cross) / denom;
            return V0 + result;
        }

        /// <summary>
        /// Calculates the circumradius of the triangle.
        /// </summary>
        /// <returns>The circumradius.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetCircumradius()
        {
            double a = Double3.Length(V1 - V2);
            double b = Double3.Length(V2 - V0);
            double c = Double3.Length(V0 - V1);
            double area = Area;
            
            if (area < double.Epsilon)
                return 0.0; // Degenerate triangle
                
            return (a * b * c) / (4 * area);
        }

        /// <summary>
        /// Calculates the incenter of the triangle.
        /// </summary>
        /// <returns>The incenter point.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 GetIncenter()
        {
            double a = Double3.Length(V1 - V2);
            double b = Double3.Length(V2 - V0);
            double c = Double3.Length(V0 - V1);
            double perimeter = a + b + c;
            
            if (perimeter < double.Epsilon)
                return Centroid; // Fallback for degenerate triangle
                
            return (a * V0 + b * V1 + c * V2) / perimeter;
        }

        /// <summary>
        /// Calculates the inradius of the triangle.
        /// </summary>
        /// <returns>The inradius.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetInradius()
        {
            double area = Area;
            double semiperimeter = Perimeter / 2.0;
            
            if (semiperimeter < double.Epsilon)
                return 0.0; // Degenerate triangle
                
            return area / semiperimeter;
        }

        /// <summary>
        /// Interpolates between triangle vertices using barycentric coordinates.
        /// </summary>
        /// <param name="u">Barycentric coordinate u.</param>
        /// <param name="v">Barycentric coordinate v.</param>
        /// <param name="attribute0">Attribute value at V0.</param>
        /// <param name="attribute1">Attribute value at V1.</param>
        /// <param name="attribute2">Attribute value at V2.</param>
        /// <returns>The interpolated attribute value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double InterpolateAttribute(double u, double v, double attribute0, double attribute1, double attribute2)
        {
            double w = 1.0 - u - v;
            return w * attribute0 + u * attribute1 + v * attribute2;
        }

        /// <summary>
        /// Interpolates between triangle vertices using barycentric coordinates.
        /// </summary>
        /// <param name="u">Barycentric coordinate u.</param>
        /// <param name="v">Barycentric coordinate v.</param>
        /// <param name="attribute0">Attribute value at V0.</param>
        /// <param name="attribute1">Attribute value at V1.</param>
        /// <param name="attribute2">Attribute value at V2.</param>
        /// <returns>The interpolated attribute value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 InterpolateAttribute(double u, double v, Double3 attribute0, Double3 attribute1, Double3 attribute2)
        {
            double w = 1.0 - u - v;
            return w * attribute0 + u * attribute1 + v * attribute2;
        }

        /// <summary>
        /// Determines if this triangle intersects with another triangle.
        /// </summary>
        /// <param name="other">The other triangle.</param>
        /// <returns>True if the triangles intersect.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IntersectsWith(Triangle other)
        {
            return Intersection.TriangleTriangle(V0, V1, V2, other.V0, other.V1, other.V2);
        }

        /// <summary>
        /// Samples a random point uniformly distributed on the triangle surface.
        /// </summary>
        /// <param name="u">Random value between 0 and 1.</param>
        /// <param name="v">Random value between 0 and 1.</param>
        /// <returns>A uniformly distributed point on the triangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 SampleUniform(double u, double v)
        {
            // Transform uniform random variables to barycentric coordinates
            double sqrtU = Maths.Sqrt(u);
            double baryU = 1.0 - sqrtU;
            double baryV = v * sqrtU;
            return GetPointFromBarycentric(baryU, baryV);
        }

        // --- IBoundingShape Implementation ---

        /// <summary>
        /// Returns the vertex on the triangle that is farthest in the given direction.
        /// </summary>
        /// <param name="direction">The direction to search in.</param>
        /// <returns>The farthest vertex in the given direction.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 SupportMap(Double3 direction)
        {
            double dot0 = Double3.Dot(V0, direction);
            double dot1 = Double3.Dot(V1, direction);
            double dot2 = Double3.Dot(V2, direction);

            if (dot0 >= dot1 && dot0 >= dot2)
                return V0;
            else if (dot1 >= dot2)
                return V1;
            else
                return V2;
        }

        /// <summary>
        /// Generates mesh data for rendering this triangle.
        /// </summary>
        /// <param name="mode">Wireframe for edges, Solid for filled triangle.</param>
        /// <param name="resolution">Unused for Triangle (topology is fixed).</param>
        /// <returns>Mesh data for rendering.</returns>
        public GeometryData GetMeshData(MeshMode mode, int resolution = 16)
        {
            if (mode == MeshMode.Wireframe)
            {
                // 3 edges, each edge is 2 vertices (LineList)
                var vertices = new Double3[]
                {
                    V0, V1,
                    V1, V2,
                    V2, V0
                };
                return new GeometryData(vertices, MeshTopology.LineList);
            }
            else
            {
                // Solid triangle (double-sided: front and back faces)
                var vertices = new Double3[] { V0, V1, V2 };
                var indices = new uint[]
                {
                    0, 1, 2,  // Front face
                    0, 2, 1   // Back face
                };
                return new GeometryData(vertices, indices, MeshTopology.TriangleList);
            }
        }

        // --- IEquatable & IFormattable Implementation ---
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Triangle other) => V0.Equals(other.V0) && V1.Equals(other.V1) && V2.Equals(other.V2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is Triangle other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(V0, V1, V2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format, IFormatProvider? formatProvider = null)
        {
            return string.Format(formatProvider, "TriangleD(V0: {0}, V1: {1}, V2: {2})", 
                V0.ToString(format, formatProvider), V1.ToString(format, formatProvider), V2.ToString(format, formatProvider));
        }

        public static bool operator ==(Triangle left, Triangle right) => left.Equals(right);
        public static bool operator !=(Triangle left, Triangle right) => !left.Equals(right);
    }
}
