// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Prowl.Vector.Geometry;

namespace Prowl.Vector
{
    /// <summary>
    /// Represents an Axis-Aligned Bounding Box (AABB) in 3D space.
    /// </summary>
    public struct AABB : IEquatable<AABB>, IFormattable, IBoundingShape
    {
        /// <summary>The minimum corner of the AABB.</summary>
        public Float3 Min;

        /// <summary>The maximum corner of the AABB.</summary>
        public Float3 Max;

        /// <summary>
        /// Initializes a new AABB with the specified min and max corners.
        /// </summary>
        /// <param name="min">The minimum corner.</param>
        /// <param name="max">The maximum corner.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AABB(Float3 min, Float3 max)
        {
            Min = new Float3(
                Maths.Min(min.X, max.X),
                Maths.Min(min.Y, max.Y),
                Maths.Min(min.Z, max.Z)
            );
            Max = new Float3(
                Maths.Max(min.X, max.X),
                Maths.Max(min.Y, max.Y),
                Maths.Max(min.Z, max.Z)
            );
        }

        /// <summary>
        /// Initializes a new AABB with individual min/max components.
        /// </summary>
        /// <param name="minX">Minimum X coordinate.</param>
        /// <param name="minY">Minimum Y coordinate.</param>
        /// <param name="minZ">Minimum Z coordinate.</param>
        /// <param name="maxX">Maximum X coordinate.</param>
        /// <param name="maxY">Maximum Y coordinate.</param>
        /// <param name="maxZ">Maximum Z coordinate.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AABB(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
            : this(new Float3(minX, minY, minZ), new Float3(maxX, maxY, maxZ))
        {
        }

        /// <summary>
        /// Initializes a new AABB centered at a point with the specified size.
        /// </summary>
        /// <param name="center">The center point.</param>
        /// <param name="size">The size (width, height, depth).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AABB FromCenterAndSize(Float3 center, Float3 size)
        {
            Float3 halfSize = size / 2.0f;
            return new AABB(center - halfSize, center + halfSize);
        }

        /// <summary>
        /// Gets the center point of the AABB.
        /// </summary>
        public Float3 Center
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Min + Max) / 2.0f;
        }

        /// <summary>
        /// Gets the size (width, height, depth) of the AABB.
        /// </summary>
        public Float3 Size
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Max - Min;
        }

        /// <summary>
        /// Gets the extents (half-size) of the AABB.
        /// </summary>
        public Float3 Extents
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Size / 2.0f;
        }

        /// <summary>
        /// Gets the volume of the AABB.
        /// </summary>
        public float Volume
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Float3 size = Size;
                return size.X * size.Y * size.Z;
            }
        }

        /// <summary>
        /// Gets the surface area of the AABB.
        /// </summary>
        public float SurfaceArea
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Float3 size = Size;
                return 2.0f * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
            }
        }

        /// <summary>
        /// Gets a corner of the AABB by index (0-7).
        /// </summary>
        /// <param name="index">Corner index (0-7).</param>
        /// <returns>The corner position.</returns>
        public Float3 GetCorner(int index)
        {
            switch (index)
            {
                case 0: return new Float3(Min.X, Min.Y, Min.Z);
                case 1: return new Float3(Max.X, Min.Y, Min.Z);
                case 2: return new Float3(Min.X, Max.Y, Min.Z);
                case 3: return new Float3(Max.X, Max.Y, Min.Z);
                case 4: return new Float3(Min.X, Min.Y, Max.Z);
                case 5: return new Float3(Max.X, Min.Y, Max.Z);
                case 6: return new Float3(Min.X, Max.Y, Max.Z);
                case 7: return new Float3(Max.X, Max.Y, Max.Z);
                default: throw new IndexOutOfRangeException("AABB corner index must be between 0 and 7.");
            }
        }

        /// <summary>
        /// Gets all 8 corners of the AABB.
        /// </summary>
        /// <returns>Array of 8 corner positions.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3[] GetCorners()
        {
            return new Float3[]
            {
                new Float3(Min.X, Min.Y, Min.Z),
                new Float3(Max.X, Min.Y, Min.Z),
                new Float3(Min.X, Max.Y, Min.Z),
                new Float3(Max.X, Max.Y, Min.Z),
                new Float3(Min.X, Min.Y, Max.Z),
                new Float3(Max.X, Min.Y, Max.Z),
                new Float3(Min.X, Max.Y, Max.Z),
                new Float3(Max.X, Max.Y, Max.Z)
            };
        }

        /// <summary>
        /// Checks if this AABB contains a point.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside or on the AABB boundary.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Float3 point)
        {
            return point.X >= Min.X - float.Epsilon && point.X <= Max.X + float.Epsilon &&
                   point.Y >= Min.Y - float.Epsilon && point.Y <= Max.Y + float.Epsilon &&
                   point.Z >= Min.Z - float.Epsilon && point.Z <= Max.Z + float.Epsilon;
        }

        /// <summary>
        /// Checks if this AABB completely contains another AABB.
        /// </summary>
        /// <param name="other">The other AABB to test.</param>
        /// <returns>True if the other AABB is completely inside this AABB.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(AABB other)
        {
            return other.Min.X >= Min.X - float.Epsilon && other.Max.X <= Max.X + float.Epsilon &&
                   other.Min.Y >= Min.Y - float.Epsilon && other.Max.Y <= Max.Y + float.Epsilon &&
                   other.Min.Z >= Min.Z - float.Epsilon && other.Max.Z <= Max.Z + float.Epsilon;
        }

        /// <summary>
        /// Checks if this AABB intersects with another AABB.
        /// </summary>
        /// <param name="other">The other AABB to test.</param>
        /// <returns>True if the AABBs intersect or touch.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(AABB other)
        {
            return Intersection.AABBAABBOverlap(Min, Max, other.Min, other.Max);
        }

        /// <summary>
        /// Checks if this AABB intersects with a sphere.
        /// </summary>
        /// <param name="sphere">The sphere to test.</param>
        /// <returns>True if the AABB intersects the sphere.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(Sphere sphere)
        {
            return Intersection.SphereAABBOverlap(sphere.Center, sphere.Radius, Min, Max);
        }

        /// <summary>
        /// Gets the closest point on the AABB to a given point.
        /// </summary>
        /// <param name="point">The point to find the closest point to.</param>
        /// <returns>The closest point on the AABB.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 ClosestPointTo(Float3 point)
        {
            Float3 closestPoint;
            Intersection.ClosestPointOnAABBToPoint(point, Min, Max, out closestPoint);
            return closestPoint;
        }

        /// <summary>
        /// Gets the squared distance from a point to the AABB.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>The squared distance (0 if point is inside).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetSqrDistanceToPoint(Float3 point)
        {
            Float3 closest = ClosestPointTo(point);
            return Float3.LengthSquared(point - closest);
        }

        /// <summary>
        /// Gets the distance from a point to the AABB.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>The distance (0 if point is inside).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetDistanceToPoint(Float3 point)
        {
            return Maths.Sqrt(GetSqrDistanceToPoint(point));
        }

        /// <summary>
        /// Expands the AABB to include a point.
        /// </summary>
        /// <param name="point">The point to include.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Encapsulate(Float3 point)
        {
            Min = new Float3(
                Maths.Min(Min.X, point.X),
                Maths.Min(Min.Y, point.Y),
                Maths.Min(Min.Z, point.Z)
            );
            Max = new Float3(
                Maths.Max(Max.X, point.X),
                Maths.Max(Max.Y, point.Y),
                Maths.Max(Max.Z, point.Z)
            );
        }

        /// <summary>
        /// Expands the AABB to include another AABB.
        /// </summary>
        /// <param name="other">The AABB to include.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Encapsulate(AABB other)
        {
            Min = new Float3(
                Maths.Min(Min.X, other.Min.X),
                Maths.Min(Min.Y, other.Min.Y),
                Maths.Min(Min.Z, other.Min.Z)
            );
            Max = new Float3(
                Maths.Max(Max.X, other.Max.X),
                Maths.Max(Max.Y, other.Max.Y),
                Maths.Max(Max.Z, other.Max.Z)
            );
        }

        /// <summary>
        /// Returns an AABB that encapsulates both this AABB and a point.
        /// </summary>
        /// <param name="point">The point to include.</param>
        /// <returns>The encapsulating AABB.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AABB Encapsulating(Float3 point)
        {
            var result = this;
            result.Encapsulate(point);
            return result;
        }

        /// <summary>
        /// Returns an AABB that encapsulates both this AABB and another AABB.
        /// </summary>
        /// <param name="other">The AABB to include.</param>
        /// <returns>The encapsulating AABB.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AABB Encapsulating(AABB other)
        {
            var result = this;
            result.Encapsulate(other);
            return result;
        }

        /// <summary>
        /// Expands the AABB by a given amount in all directions.
        /// </summary>
        /// <param name="amount">The amount to expand by.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Expand(float amount)
        {
            Float3 expansion = new Float3(amount, amount, amount);
            Min -= expansion;
            Max += expansion;
        }

        /// <summary>
        /// Expands the AABB by different amounts in each direction.
        /// </summary>
        /// <param name="expansion">The amount to expand by in each direction.</param>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public void Expand(Float3 expansion)
       {
           Min -= expansion;
           Max += expansion;
       }

       /// <summary>
       /// Returns an expanded version of this AABB.
       /// </summary>
       /// <param name="amount">The amount to expand by.</param>
       /// <returns>The expanded AABB.</returns>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public AABB Expanded(float amount)
       {
           var result = this;
           result.Expand(amount);
           return result;
       }

       /// <summary>
       /// Returns an expanded version of this AABB.
       /// </summary>
       /// <param name="expansion">The amount to expand by in each direction.</param>
       /// <returns>The expanded AABB.</returns>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public AABB Expanded(Float3 expansion)
       {
           var result = this;
           result.Expand(expansion);
           return result;
       }

       /// <summary>
       /// Transforms the AABB by a 4x4 matrix.
       /// Results in an AABB that contains the transformed oriented bounding box.
       /// </summary>
       /// <param name="matrix">The transformation matrix.</param>
       /// <returns>The transformed AABB.</returns>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public AABB TransformBy(Float4x4 matrix)
       {
           // Transform all 8 corners and find the new AABB
           Float3[] corners = GetCorners();
           Float3 newMin = Float4x4.TransformPoint(corners[0], matrix);
           Float3 newMax = newMin;

           for (int i = 1; i < 8; i++)
           {
               Float3 transformedCorner = Float4x4.TransformPoint(corners[i], matrix);
               newMin = new Float3(
                   Maths.Min(newMin.X, transformedCorner.X),
                   Maths.Min(newMin.Y, transformedCorner.Y),
                   Maths.Min(newMin.Z, transformedCorner.Z)
               );
               newMax = new Float3(
                   Maths.Max(newMax.X, transformedCorner.X),
                   Maths.Max(newMax.Y, transformedCorner.Y),
                   Maths.Max(newMax.Z, transformedCorner.Z)
               );
           }

           return new AABB(newMin, newMax);
       }

       /// <summary>
       /// Clips this AABB against another AABB, returning the intersection.
       /// </summary>
       /// <param name="other">The AABB to clip against.</param>
       /// <returns>The clipped AABB, or an invalid AABB if no intersection.</returns>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public AABB ClippedBy(AABB other)
       {
           Float3 newMin = new Float3(
               Maths.Max(Min.X, other.Min.X),
               Maths.Max(Min.Y, other.Min.Y),
               Maths.Max(Min.Z, other.Min.Z)
           );
           Float3 newMax = new Float3(
               Maths.Min(Max.X, other.Max.X),
               Maths.Min(Max.Y, other.Max.Y),
               Maths.Min(Max.Z, other.Max.Z)
           );

           // If any dimension is invalid, return an empty AABB
           if (newMin.X > newMax.X || newMin.Y > newMax.Y || newMin.Z > newMax.Z)
           {
               return new AABB(Float3.Zero, Float3.Zero);
           }

           return new AABB(newMin, newMax);
       }

       /// <summary>
       /// Checks if this AABB is valid (min <= max in all dimensions).
       /// </summary>
       /// <returns>True if the AABB is valid.</returns>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public bool IsValid()
       {
           return Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;
       }

       /// <summary>
       /// Checks if this AABB has zero volume.
       /// </summary>
       /// <returns>True if the AABB has zero volume.</returns>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public bool IsEmpty()
       {
           return Maths.Abs(Volume) < float.Epsilon;
       }

       /// <summary>
       /// Samples a random point uniformly distributed inside the AABB.
       /// </summary>
       /// <returns>A uniformly distributed point inside the AABB.</returns>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public Float3 SampleVolume()
       {
            float u = RNG.Shared.NextFloat();
            float v = RNG.Shared.NextFloat();
            float w = RNG.Shared.NextFloat();
           return new Float3(
               Maths.Lerp(Min.X, Max.X, u),
               Maths.Lerp(Min.Y, Max.Y, v),
               Maths.Lerp(Min.Z, Max.Z, w)
           );
       }

       /// <summary>
       /// Gets the face normal for the specified face index.
       /// </summary>
       /// <param name="faceIndex">Face index (0-5): -X, +X, -Y, +Y, -Z, +Z.</param>
       /// <returns>The face normal vector.</returns>
       public Float3 GetFaceNormal(int faceIndex)
       {
           switch (faceIndex)
           {
               case 0: return new Float3(-1.0f, 0.0f, 0.0f);  // -X face
               case 1: return new Float3(1.0f, 0.0f, 0.0f);   // +X face
               case 2: return new Float3(0.0f, -1.0f, 0.0f);  // -Y face
               case 3: return new Float3(0.0f, 1.0f, 0.0f);   // +Y face
               case 4: return new Float3(0.0f, 0.0f, -1.0f);  // -Z face
               case 5: return new Float3(0.0f, 0.0f, 1.0f);   // +Z face
               default: throw new IndexOutOfRangeException("AABB face index must be between 0 and 5.");
           }
       }

       /// <summary>
       /// Gets the center point of the specified face.
       /// </summary>
       /// <param name="faceIndex">Face index (0-5): -X, +X, -Y, +Y, -Z, +Z.</param>
       /// <returns>The face center point.</returns>
       public Float3 GetFaceCenter(int faceIndex)
       {
           Float3 center = Center;
           switch (faceIndex)
           {
               case 0: return new Float3(Min.X, center.Y, center.Z);  // -X face
               case 1: return new Float3(Max.X, center.Y, center.Z);  // +X face
               case 2: return new Float3(center.X, Min.Y, center.Z);  // -Y face
               case 3: return new Float3(center.X, Max.Y, center.Z);  // +Y face
               case 4: return new Float3(center.X, center.Y, Min.Z);  // -Z face
               case 5: return new Float3(center.X, center.Y, Max.Z);  // +Z face
               default: throw new IndexOutOfRangeException("AABB face index must be between 0 and 5.");
           }
       }

       /// <summary>
       /// Creates an AABB from a collection of points.
       /// </summary>
       /// <param name="points">The points to encapsulate.</param>
       /// <returns>The smallest AABB that contains all points.</returns>
       public static AABB FromPoints(Float3[] points)
       {
           if (points == null || points.Length == 0)
               return new AABB(Float3.Zero, Float3.Zero);

           Float3 min = points[0];
           Float3 max = points[0];

           for (int i = 1; i < points.Length; i++)
           {
               min = new Float3(
                   Maths.Min(min.X, points[i].X),
                   Maths.Min(min.Y, points[i].Y),
                   Maths.Min(min.Z, points[i].Z)
               );
               max = new Float3(
                   Maths.Max(max.X, points[i].X),
                   Maths.Max(max.Y, points[i].Y),
                   Maths.Max(max.Z, points[i].Z)
               );
           }

           return new AABB(min, max);
       }

       /// <summary>
       /// Creates an AABB that encompasses multiple AABBs.
       /// </summary>
       /// <param name="aabbs">The AABBs to encapsulate.</param>
       /// <returns>The smallest AABB that contains all input AABBs.</returns>
       public static AABB FromAABBs(AABB[] aabbs)
       {
           if (aabbs == null || aabbs.Length == 0)
               return new AABB(Float3.Zero, Float3.Zero);

           AABB result = aabbs[0];
           for (int i = 1; i < aabbs.Length; i++)
           {
               result.Encapsulate(aabbs[i]);
           }

           return result;
       }

       /// <summary>
       /// Creates an AABB from a sphere.
       /// </summary>
       /// <param name="sphere">The sphere to create an AABB from.</param>
       /// <returns>The AABB that tightly bounds the sphere.</returns>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public static AABB FromSphere(Sphere sphere)
       {
           Float3 radiusVector = new Float3(sphere.Radius, sphere.Radius, sphere.Radius);
           return new AABB(sphere.Center - radiusVector, sphere.Center + radiusVector);
       }

       // --- IBoundingShape Implementation ---

       /// <summary>
       /// Returns the point on the AABB that is farthest in the given direction.
       /// </summary>
       /// <param name="direction">The direction to search in.</param>
       /// <returns>The farthest corner in the given direction.</returns>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public Float3 SupportMap(Float3 direction)
       {
           return new Float3(
               direction.X >= 0 ? Max.X : Min.X,
               direction.Y >= 0 ? Max.Y : Min.Y,
               direction.Z >= 0 ? Max.Z : Min.Z
           );
       }

       /// <summary>
       /// Generates geometry data for this AABB as a BMesh-like structure.
       /// </summary>
       /// <param name="resolution">Unused for AABB (box topology is fixed).</param>
       /// <returns>GeometryData containing vertices, edges, and faces for the box.</returns>
       public GeometryData GetGeometryData(int resolution = 16)
       {
           var geometryData = new GeometryData();
           Float3[] corners = GetCorners();

           // Add 8 vertices
           var verts = new GeometryData.Vertex[8];
           for (int i = 0; i < 8; i++)
           {
               verts[i] = geometryData.AddVertex(corners[i]);
           }

           // Add 6 faces (quads)
           // Bottom face (Z = Min.Z)
           geometryData.AddFace(verts[0], verts[2], verts[3], verts[1]);
           // Top face (Z = Max.Z)
           geometryData.AddFace(verts[4], verts[5], verts[7], verts[6]);
           // Front face (Y = Min.Y)
           geometryData.AddFace(verts[0], verts[1], verts[5], verts[4]);
           // Back face (Y = Max.Y)
           geometryData.AddFace(verts[2], verts[6], verts[7], verts[3]);
           // Left face (X = Min.X)
           geometryData.AddFace(verts[0], verts[4], verts[6], verts[2]);
           // Right face (X = Max.X)
           geometryData.AddFace(verts[1], verts[3], verts[7], verts[5]);

           return geometryData;
       }

       // --- IEquatable & IFormattable Implementation ---
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public bool Equals(AABB other) => Min.Equals(other.Min) && Max.Equals(other.Max);

       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public override bool Equals(object? obj) => obj is AABB other && Equals(other);

       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public override int GetHashCode() => HashCode.Combine(Min, Max);

       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public string ToString(string? format, IFormatProvider? formatProvider = null)
       {
           return string.Format(formatProvider, "AABBD(Min: {0}, Max: {1})", 
               Min.ToString(format, formatProvider), Max.ToString(format, formatProvider));
       }

       public static bool operator ==(AABB left, AABB right) => left.Equals(right);
       public static bool operator !=(AABB left, AABB right) => !left.Equals(right);
   }
}
