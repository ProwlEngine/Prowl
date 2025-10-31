// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

using Prowl.Vector.Geometry;

namespace Prowl.Vector
{
    /// <summary>
    /// Represents a 3D triangle defined by three vertices.
    /// </summary>
    public struct Triangle : IEquatable<Triangle>, IFormattable, IBoundingShape
    {
        /// <summary>The first vertex of the triangle.</summary>
        public Float3 V0;

        /// <summary>The second vertex of the triangle.</summary>
        public Float3 V1;

        /// <summary>The third vertex of the triangle.</summary>
        public Float3 V2;

        /// <summary>
        /// Initializes a new triangle from three vertices.
        /// </summary>
        /// <param name="v0">First vertex.</param>
        /// <param name="v1">Second vertex.</param>
        /// <param name="v2">Third vertex.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Triangle(Float3 v0, Float3 v1, Float3 v2)
        {
            V0 = v0;
            V1 = v1;
            V2 = v2;
        }

        /// <summary>
        /// Gets the normal vector of the triangle (not normalized).
        /// Direction follows right-hand rule based on vertex order.
        /// </summary>
        public Float3 Normal
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Float3.Cross(V1 - V0, V2 - V0);
        }

        /// <summary>
        /// Gets the normalized normal vector of the triangle.
        /// </summary>
        public Float3 NormalizedNormal
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Float3.Normalize(Normal);
        }

        /// <summary>
        /// Gets the area of the triangle.
        /// </summary>
        public float Area
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Float3.Length(Normal) / 2.0f;
        }

        /// <summary>
        /// Gets the centroid (center point) of the triangle.
        /// </summary>
        public Float3 Centroid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (V0 + V1 + V2) / new Float3(3, 3, 3);
        }

        /// <summary>
        /// Gets the perimeter of the triangle.
        /// </summary>
        public float Perimeter
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Float3.Length(V1 - V0) + Float3.Length(V2 - V1) + Float3.Length(V0 - V2);
        }

        /// <summary>
        /// Gets a vertex by index (0, 1, or 2).
        /// </summary>
        public Float3 this[int index]
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
        public void GetBarycentricCoordinates(Float3 point, out float u, out float v)
        {
            Float3 v0v1 = V1 - V0;
            Float3 v0v2 = V2 - V0;
            Float3 v0p = point - V0;

            float dot00 = Float3.Dot(v0v2, v0v2);
            float dot01 = Float3.Dot(v0v2, v0v1);
            float dot02 = Float3.Dot(v0v2, v0p);
            float dot11 = Float3.Dot(v0v1, v0v1);
            float dot12 = Float3.Dot(v0v1, v0p);

            float invDenom = 1.0f / (dot00 * dot11 - dot01 * dot01);
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
        public static bool IsPointInTriangle(float u, float v)
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
        public Float3 GetPointFromBarycentric(float u, float v)
        {
            float w = 1.0f - u - v;
            return V0 * w + V1 * u + V2 * v;
        }

        /// <summary>
        /// Finds the closest point on this triangle to a given point.
        /// </summary>
        /// <param name="point">The point to find the closest point to.</param>
        /// <returns>The closest point on the triangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 ClosestPointTo(Float3 point)
        {
            GetBarycentricCoordinates(point, out float u, out float v);
            
            // Clamp barycentric coordinates to triangle bounds
            if (u < 0.0) u = 0.0f;
            if (v < 0.0) v = 0.0f;
            if (u + v > 1.0)
            {
                float sum = u + v;
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
            return Float3.LengthSquared(Normal) < float.Epsilon * float.Epsilon;
        }

        /// <summary>
        /// Transforms this triangle by a 4x4 matrix.
        /// </summary>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>The transformed triangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Triangle Transform(Float4x4 matrix)
        {
            return new Triangle(
                Float4x4.TransformPoint(V0, matrix),
                Float4x4.TransformPoint(V1, matrix),
                Float4x4.TransformPoint(V2, matrix)
            );
        }

        /// <summary>
        /// Calculates the signed volume between this triangle and a point.
        /// Used for determining orientation in 3D space.
        /// </summary>
        /// <param name="point">The point to test against.</param>
        /// <returns>The signed volume (positive if point is on normal side).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float SignedVolumeToPoint(Float3 point)
        {
            return Float3.Dot(Normal, point - V0) / 6;
        }

        /// <summary>
        /// Gets the edge vector for the specified edge index.
        /// </summary>
        /// <param name="edgeIndex">Edge index (0 = V0->V1, 1 = V1->V2, 2 = V2->V0).</param>
        /// <returns>The edge vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 GetEdge(int edgeIndex)
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
        public float GetEdgeLength(int edgeIndex)
        {
            return Float3.Length(GetEdge(edgeIndex));
        }

        /// <summary>
        /// Checks if a point is coplanar with this triangle within a tolerance.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <param name="tolerance">The tolerance for the test.</param>
        /// <returns>True if the point is coplanar.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsCoplanar(Float3 point, float tolerance = float.Epsilon)
        {
            Float3 normal = NormalizedNormal;
            float distance = Maths.Abs(Float3.Dot(normal, point - V0));
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
        public Float3 GetCircumcenter()
        {
            Float3 a = V1 - V0;
            Float3 b = V2 - V0;
            Float3 cross = Float3.Cross(a, b);
            float denom = 2.0f * Float3.Dot(cross, cross);
            
            if (Maths.Abs(denom) < float.Epsilon)
                return Centroid; // Fallback for degenerate triangle
                
            Float3 result = Float3.Cross(Float3.Cross(cross, a) * Float3.LengthSquared(b) +
                                                 Float3.Cross(b, cross) * Float3.LengthSquared(a), cross) / denom;
            return V0 + result;
        }

        /// <summary>
        /// Calculates the circumradius of the triangle.
        /// </summary>
        /// <returns>The circumradius.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCircumradius()
        {
            float a = Float3.Length(V1 - V2);
            float b = Float3.Length(V2 - V0);
            float c = Float3.Length(V0 - V1);
            float area = Area;
            
            if (area < float.Epsilon)
                return 0.0f; // Degenerate triangle
                
            return (a * b * c) / (4 * area);
        }

        /// <summary>
        /// Calculates the incenter of the triangle.
        /// </summary>
        /// <returns>The incenter point.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 GetIncenter()
        {
            float a = Float3.Length(V1 - V2);
            float b = Float3.Length(V2 - V0);
            float c = Float3.Length(V0 - V1);
            float perimeter = a + b + c;
            
            if (perimeter < float.Epsilon)
                return Centroid; // Fallback for degenerate triangle
                
            return (a * V0 + b * V1 + c * V2) / perimeter;
        }

        /// <summary>
        /// Calculates the inradius of the triangle.
        /// </summary>
        /// <returns>The inradius.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetInradius()
        {
            float area = Area;
            float semiperimeter = Perimeter / 2.0f;
            
            if (semiperimeter < float.Epsilon)
                return 0.0f; // Degenerate triangle
                
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
        public static float InterpolateAttribute(float u, float v, float attribute0, float attribute1, float attribute2)
        {
            float w = 1.0f - u - v;
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
        public static Float3 InterpolateAttribute(float u, float v, Float3 attribute0, Float3 attribute1, Float3 attribute2)
        {
            float w = 1.0f - u - v;
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
        public Float3 SampleUniform(float u, float v)
        {
            // Transform uniform random variables to barycentric coordinates
            float sqrtU = Maths.Sqrt(u);
            float baryU = 1.0f - sqrtU;
            float baryV = v * sqrtU;
            return GetPointFromBarycentric(baryU, baryV);
        }

        public AABB GetAABB() 
        {
            Float3 min = Maths.Min(Maths.Min(V0, V1), V2);
            Float3 max = Maths.Max(Maths.Max(V0, V1), V2);
            return new AABB(min, max);
        }

        // --- IBoundingShape Implementation ---

        /// <summary>
        /// Returns the vertex on the triangle that is farthest in the given direction.
        /// </summary>
        /// <param name="direction">The direction to search in.</param>
        /// <returns>The farthest vertex in the given direction.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 SupportMap(Float3 direction)
        {
            float dot0 = Float3.Dot(V0, direction);
            float dot1 = Float3.Dot(V1, direction);
            float dot2 = Float3.Dot(V2, direction);

            if (dot0 >= dot1 && dot0 >= dot2)
                return V0;
            else if (dot1 >= dot2)
                return V1;
            else
                return V2;
        }

        /// <summary>
        /// Generates geometry data for this triangle as a BMesh-like structure.
        /// </summary>
        /// <param name="resolution">Unused for Triangle (topology is fixed).</param>
        /// <returns>GeometryData containing vertices, edges, and face information.</returns>
        public GeometryData GetGeometryData(int resolution = 16)
        {
            var geometryData = new GeometryData();

            // Add vertices
            var v0 = geometryData.AddVertex(V0);
            var v1 = geometryData.AddVertex(V1);
            var v2 = geometryData.AddVertex(V2);

            // Add face (automatically creates edges)
            geometryData.AddFace(v0, v1, v2);

            return geometryData;
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
