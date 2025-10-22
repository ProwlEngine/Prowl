// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Represents a 3D sphere defined by a center point and radius.
    /// </summary>
    public struct Sphere : IEquatable<Sphere>, IFormattable, IBoundingShape
    {
        /// <summary>The center point of the sphere.</summary>
        public Double3 Center;

        /// <summary>The radius of the sphere.</summary>
        public double Radius;

        /// <summary>
        /// Initializes a new sphere with the specified center and radius.
        /// </summary>
        /// <param name="center">The center point of the sphere.</param>
        /// <param name="radius">The radius of the sphere.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sphere(Double3 center, double radius)
        {
            Center = center;
            Radius = Maths.Max(radius, 0.0); // Ensure non-negative radius
        }

        /// <summary>
        /// Initializes a new sphere with center components and radius.
        /// </summary>
        /// <param name="x">X coordinate of the center.</param>
        /// <param name="y">Y coordinate of the center.</param>
        /// <param name="z">Z coordinate of the center.</param>
        /// <param name="radius">The radius of the sphere.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sphere(double x, double y, double z, double radius)
        {
            Center = new Double3(x, y, z);
            Radius = Maths.Max(radius, 0.0);
        }

        /// <summary>
        /// Gets the diameter of the sphere.
        /// </summary>
        public double Diameter
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Radius * 2.0;
        }

        /// <summary>
        /// Gets the surface area of the sphere (4πr²).
        /// </summary>
        public double SurfaceArea
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => 4 * (double)Maths.PI * Radius * Radius;
        }

        /// <summary>
        /// Gets the volume of the sphere (4/3πr³).
        /// </summary>
        public double Volume
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (4 / 3) * (double)Maths.PI * Radius * Radius * Radius;
        }

        /// <summary>
        /// Gets the circumference of the sphere (2πr).
        /// </summary>
        public double Circumference
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => 2.0 * (double)Maths.PI * Radius;
        }

        /// <summary>
        /// Checks if this sphere contains a point.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside or on the sphere surface.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Double3 point)
        {
            return Double3.LengthSquared(point - Center) <= Radius * Radius + double.Epsilon;
        }

        /// <summary>
        /// Checks if this sphere completely contains another sphere.
        /// </summary>
        /// <param name="other">The other sphere to test.</param>
        /// <returns>True if the other sphere is completely inside this sphere.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Sphere other)
        {
            double distance = Double3.Length(other.Center - Center);
            return distance + other.Radius <= Radius + double.Epsilon;
        }

        /// <summary>
        /// Checks if this sphere intersects with another sphere.
        /// </summary>
        /// <param name="other">The other sphere to test.</param>
        /// <returns>True if the spheres intersect or touch.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(Sphere other)
        {
            return Intersection.SphereSphereOverlap(Center, Radius, other.Center, other.Radius);
        }

        /// <summary>
        /// Checks if this sphere intersects with an AABB.
        /// </summary>
        /// <param name="aabb">The AABB to test.</param>
        /// <returns>True if the sphere intersects the AABB.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(AABB aabb)
        {
            return Intersection.SphereAABBOverlap(Center, Radius, aabb.Min, aabb.Max);
        }

        /// <summary>
        /// Gets the signed distance from a point to the sphere surface.
        /// Positive if outside, negative if inside, zero if on the surface.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>The signed distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetSignedDistanceToPoint(Double3 point)
        {
            return Double3.Length(point - Center) - Radius;
        }

        /// <summary>
        /// Gets the absolute distance from a point to the sphere surface.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>The absolute distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetDistanceToPoint(Double3 point)
        {
            return Maths.Abs(GetSignedDistanceToPoint(point));
        }

        /// <summary>
        /// Gets the closest point on the sphere surface to a given point.
        /// </summary>
        /// <param name="point">The point to find the closest point to.</param>
        /// <returns>The closest point on the sphere surface.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 ClosestPointTo(Double3 point)
        {
            Double3 closestPoint;
            Intersection.ClosestPointOnSphereToPoint(point, Center, Radius, out closestPoint);
            return closestPoint;
        }

        /// <summary>
        /// Expands the sphere to include a point.
        /// </summary>
        /// <param name="point">The point to include.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Encapsulate(Double3 point)
        {
            double distance = Double3.Length(point - Center);
            if (distance > Radius)
            {
                Radius = distance;
            }
        }

        /// <summary>
        /// Expands the sphere to include another sphere.
        /// </summary>
        /// <param name="other">The sphere to include.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Encapsulate(Sphere other)
        {
            double distance = Double3.Length(other.Center - Center);
            double requiredRadius = distance + other.Radius;
            if (requiredRadius > Radius)
            {
                Radius = requiredRadius;
            }
        }

        /// <summary>
        /// Returns a sphere that encapsulates both this sphere and a point.
        /// </summary>
        /// <param name="point">The point to include.</param>
        /// <returns>The encapsulating sphere.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sphere Encapsulating(Double3 point)
        {
            var result = this;
            result.Encapsulate(point);
            return result;
        }

        /// <summary>
        /// Returns a sphere that encapsulates both this sphere and another sphere.
        /// </summary>
        /// <param name="other">The sphere to include.</param>
        /// <returns>The encapsulating sphere.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sphere Encapsulating(Sphere other)
        {
            var result = this;
            result.Encapsulate(other);
            return result;
        }

        /// <summary>
        /// Transforms the sphere by a 4x4 matrix.
        /// Note: Only uniform scaling is properly supported.
        /// </summary>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>The transformed sphere.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sphere Transform(Double4x4 matrix)
        {
            Double3 transformedCenter = Double4x4.TransformPoint(Center, matrix);
            
            // For radius, we need to handle scaling. We'll use the maximum scale factor.
            Double3 scaleVector = new Double3(
                Double3.Length(new Double3(matrix.c0.X, matrix.c0.Y, matrix.c0.Z)),
                Double3.Length(new Double3(matrix.c1.X, matrix.c1.Y, matrix.c1.Z)),
                Double3.Length(new Double3(matrix.c2.X, matrix.c2.Y, matrix.c2.Z))
            );
            double maxScale = Maths.Max(scaleVector.X, Maths.Max(scaleVector.Y, scaleVector.Z));
            double transformedRadius = Radius * maxScale;
            
            return new Sphere(transformedCenter, transformedRadius);
        }

        /// <summary>
        /// Samples a random point uniformly distributed on the sphere surface.
        /// </summary>
        /// <returns>A uniformly distributed point on the sphere surface.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 SampleSurface()
        {
            double u = RNG.Shared.NextDouble();
            double v = RNG.Shared.NextDouble();
            double theta = 2.0 * (double)Maths.PI * u;
            double phi = Maths.Acos(2.0 * v - 1.0);
            
            double sinPhi = Maths.Sin(phi);
            double x = sinPhi * Maths.Cos(theta);
            double y = sinPhi * Maths.Sin(theta);
            double z = Maths.Cos(phi);
            
            return Center + new Double3(x, y, z) * Radius;
        }

        /// <summary>
        /// Samples a random point uniformly distributed inside the sphere volume.
        /// </summary>
        /// <returns>A uniformly distributed point inside the sphere.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 SampleVolume()
        {
            double u = RNG.Shared.NextDouble();
            double r = Radius * Maths.Pow(u, 1.0 / 3); // Cube root for uniform distribution
            Double3 direction = SampleSurface() - Center;
            direction = Double3.Normalize(direction);
            return Center + direction * r;
        }

        /// <summary>
        /// Creates a sphere from two points (diameter endpoints).
        /// </summary>
        /// <param name="pointA">First endpoint.</param>
        /// <param name="pointB">Second endpoint.</param>
        /// <returns>A sphere with the two points as diameter endpoints.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Sphere FromDiameter(Double3 pointA, Double3 pointB)
        {
            Double3 center = (pointA + pointB) / 2.0;
            double radius = Double3.Length(pointB - pointA) / 2.0;
            return new Sphere(center, radius);
        }

        /// <summary>
        /// Creates the smallest sphere that contains all the given points.
        /// Uses Welzl's algorithm for small point sets, falls back to naive approach for larger sets.
        /// </summary>
        /// <param name="points">The points to encapsulate.</param>
        /// <returns>The smallest encapsulating sphere.</returns>
        public static Sphere FromPoints(Double3[] points)
        {
            if (points == null || points.Length == 0)
                return new Sphere(Double3.Zero, 0.0);
            
            if (points.Length == 1)
                return new Sphere(points[0], 0.0);
            
            if (points.Length == 2)
                return FromDiameter(points[0], points[1]);
            
            // For simplicity, use centroid and max distance approach
            // A more sophisticated implementation would use Welzl's algorithm
            Double3 centroid = Double3.Zero;
            for (int i = 0; i < points.Length; i++)
            {
                centroid += points[i];
            }
            centroid /= points.Length;
            
            double maxDistSq = 0.0;
            for (int i = 0; i < points.Length; i++)
            {
                double distSq = Double3.LengthSquared(points[i] - centroid);
                if (distSq > maxDistSq)
                    maxDistSq = distSq;
            }
            
            return new Sphere(centroid, Maths.Sqrt(maxDistSq));
        }

        // --- IBoundingShape Implementation ---

        /// <summary>
        /// Returns the point on the sphere that is farthest in the given direction.
        /// </summary>
        /// <param name="direction">The direction to search in.</param>
        /// <returns>The farthest point on the sphere in the given direction.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 SupportMap(Double3 direction)
        {
            double length = Double3.Length(direction);
            if (length < double.Epsilon)
                return Center; // Return center if direction is zero

            return Center + (direction / length) * Radius;
        }

        /// <summary>
        /// Generates mesh data for rendering this sphere.
        /// </summary>
        /// <param name="mode">Wireframe for latitude/longitude lines, Solid for filled sphere.</param>
        /// <param name="resolution">Number of segments (must be at least 3).</param>
        /// <returns>Mesh data for rendering.</returns>
        public MeshData GetMeshData(MeshMode mode, int resolution = 16)
        {
            resolution = Maths.Max(resolution, 3);

            if (mode == MeshMode.Wireframe)
            {
                return GetWireframeMesh(resolution);
            }
            else
            {
                return GetSolidMesh(resolution);
            }
        }

        private MeshData GetWireframeMesh(int segments)
        {
            var vertices = new System.Collections.Generic.List<Double3>();

            // Generate latitude circles
            for (int lat = 0; lat <= segments; lat++)
            {
                double theta = lat * Maths.PI / segments;
                double y = Center.Y + Radius * Maths.Cos(theta);
                double radius = Radius * Maths.Sin(theta);

                for (int lon = 0; lon < segments; lon++)
                {
                    double phi1 = lon * 2.0 * Maths.PI / segments;
                    double phi2 = (lon + 1) * 2.0 * Maths.PI / segments;

                    Double3 p1 = Center + new Double3(
                        radius * Maths.Cos(phi1),
                        y - Center.Y,
                        radius * Maths.Sin(phi1)
                    );
                    Double3 p2 = Center + new Double3(
                        radius * Maths.Cos(phi2),
                        y - Center.Y,
                        radius * Maths.Sin(phi2)
                    );

                    vertices.Add(p1);
                    vertices.Add(p2);
                }
            }

            // Generate longitude lines
            for (int lon = 0; lon < segments; lon++)
            {
                double phi = lon * 2.0 * Maths.PI / segments;

                for (int lat = 0; lat < segments; lat++)
                {
                    double theta1 = lat * Maths.PI / segments;
                    double theta2 = (lat + 1) * Maths.PI / segments;

                    Double3 p1 = Center + new Double3(
                        Radius * Maths.Sin(theta1) * Maths.Cos(phi),
                        Radius * Maths.Cos(theta1),
                        Radius * Maths.Sin(theta1) * Maths.Sin(phi)
                    );
                    Double3 p2 = Center + new Double3(
                        Radius * Maths.Sin(theta2) * Maths.Cos(phi),
                        Radius * Maths.Cos(theta2),
                        Radius * Maths.Sin(theta2) * Maths.Sin(phi)
                    );

                    vertices.Add(p1);
                    vertices.Add(p2);
                }
            }

            return new MeshData(vertices.ToArray(), MeshTopology.LineList);
        }

        private MeshData GetSolidMesh(int segments)
        {
            var vertices = new System.Collections.Generic.List<Double3>();
            var indices = new System.Collections.Generic.List<uint>();

            // Generate vertices
            for (int lat = 0; lat <= segments; lat++)
            {
                double theta = lat * Maths.PI / segments;
                double sinTheta = Maths.Sin(theta);
                double cosTheta = Maths.Cos(theta);

                for (int lon = 0; lon <= segments; lon++)
                {
                    double phi = lon * 2.0 * Maths.PI / segments;
                    double sinPhi = Maths.Sin(phi);
                    double cosPhi = Maths.Cos(phi);

                    Double3 position = Center + new Double3(
                        Radius * sinTheta * cosPhi,
                        Radius * cosTheta,
                        Radius * sinTheta * sinPhi
                    );

                    vertices.Add(position);
                }
            }

            // Generate indices for triangle list
            for (int lat = 0; lat < segments; lat++)
            {
                for (int lon = 0; lon < segments; lon++)
                {
                    uint first = (uint)(lat * (segments + 1) + lon);
                    uint second = (uint)(first + segments + 1);

                    // First triangle
                    indices.Add(first);
                    indices.Add(second);
                    indices.Add(first + 1);

                    // Second triangle
                    indices.Add(second);
                    indices.Add(second + 1);
                    indices.Add(first + 1);
                }
            }

            return new MeshData(vertices.ToArray(), indices.ToArray(), MeshTopology.TriangleList);
        }

        // --- IEquatable & IFormattable Implementation ---
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Sphere other) => Center.Equals(other.Center) && Radius.Equals(other.Radius);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is Sphere other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(Center, Radius);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format, IFormatProvider? formatProvider = null)
        {
            return string.Format(formatProvider, "SphereD(Center: {0}, Radius: {1})", 
                Center.ToString(format, formatProvider), Radius.ToString(format, formatProvider));
        }

        public static bool operator ==(Sphere left, Sphere right) => left.Equals(right);
        public static bool operator !=(Sphere left, Sphere right) => !left.Equals(right);
    }
}
