// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

using Prowl.Vector.Geometry;

namespace Prowl.Vector
{
    /// <summary>
    /// Represents a 3D cone defined by an apex point, a base center, and a base radius.
    /// The cone's axis runs from the apex to the base center.
    /// </summary>
    public struct Cone : IEquatable<Cone>, IFormattable, IBoundingShape
    {
        /// <summary>The apex (tip) of the cone.</summary>
        public Double3 Apex;

        /// <summary>The center of the base circle.</summary>
        public Double3 BaseCenter;

        /// <summary>The radius of the base circle.</summary>
        public double BaseRadius;

        /// <summary>
        /// Initializes a new cone with the specified apex, base center, and base radius.
        /// </summary>
        /// <param name="apex">The tip of the cone.</param>
        /// <param name="baseCenter">The center of the base circle.</param>
        /// <param name="baseRadius">The radius of the base circle.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cone(Double3 apex, Double3 baseCenter, double baseRadius)
        {
            Apex = apex;
            BaseCenter = baseCenter;
            BaseRadius = baseRadius;
        }

        /// <summary>
        /// Initializes a new cone with the specified apex, base center, base radius using individual coordinate components.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cone(double apexX, double apexY, double apexZ,
                    double baseCenterX, double baseCenterY, double baseCenterZ,
                    double baseRadius)
        {
            Apex = new Double3(apexX, apexY, apexZ);
            BaseCenter = new Double3(baseCenterX, baseCenterY, baseCenterZ);
            BaseRadius = baseRadius;
        }

        /// <summary>
        /// Gets the height of the cone (distance from apex to base).
        /// </summary>
        public double Height
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Double3.Length(BaseCenter - Apex);
        }

        /// <summary>
        /// Gets the axis direction of the cone (normalized vector from apex to base center).
        /// </summary>
        public Double3 AxisDirection
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Double3.Normalize(BaseCenter - Apex);
        }

        /// <summary>
        /// Gets the volume of the cone.
        /// </summary>
        public double Volume
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Maths.PI * BaseRadius * BaseRadius * Height) / 3.0;
        }

        /// <summary>
        /// Gets the lateral surface area of the cone (excluding the base).
        /// </summary>
        public double LateralSurfaceArea
        {
            get
            {
                double slantHeight = Maths.Sqrt(Height * Height + BaseRadius * BaseRadius);
                return Maths.PI * BaseRadius * slantHeight;
            }
        }

        /// <summary>
        /// Gets the total surface area of the cone (including the base).
        /// </summary>
        public double TotalSurfaceArea
        {
            get
            {
                double slantHeight = Maths.Sqrt(Height * Height + BaseRadius * BaseRadius);
                return Maths.PI * BaseRadius * (BaseRadius + slantHeight);
            }
        }

        /// <summary>
        /// Checks if a point is inside the cone.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside the cone.</returns>
        public bool Contains(Double3 point)
        {
            Double3 axis = BaseCenter - Apex;
            double height = Double3.Length(axis);

            if (height < double.Epsilon)
                return false; // Degenerate cone

            Double3 axisNorm = axis / height;
            Double3 apexToPoint = point - Apex;

            // Project point onto cone axis
            double projectionLength = Double3.Dot(apexToPoint, axisNorm);

            // Check if point is within height bounds
            if (projectionLength < 0.0 || projectionLength > height)
                return false;

            // Calculate the radius at this height
            double radiusAtHeight = (projectionLength / height) * BaseRadius;

            // Calculate perpendicular distance from axis
            Double3 projectionPoint = Apex + axisNorm * projectionLength;
            double perpDistance = Double3.Length(point - projectionPoint);

            return perpDistance <= radiusAtHeight;
        }

        /// <summary>
        /// Transforms the cone by a 4x4 matrix.
        /// Note: Non-uniform scaling may distort the cone into an elliptical cone.
        /// </summary>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>The transformed cone.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cone Transform(Double4x4 matrix)
        {
            Double3 transformedApex = Double4x4.TransformPoint(Apex, matrix);
            Double3 transformedBaseCenter = Double4x4.TransformPoint(BaseCenter, matrix);

            // For radius, we need to account for scaling
            // Calculate a point on the base circle and see how it transforms
            Double3 axis = BaseCenter - Apex;
            Double3 perpendicular = Maths.Abs(axis.X) < 0.9 ?
                Double3.Cross(axis, new Double3(1, 0, 0)) :
                Double3.Cross(axis, new Double3(0, 1, 0));
            perpendicular = Double3.Normalize(perpendicular);

            Double3 baseEdgePoint = BaseCenter + perpendicular * BaseRadius;
            Double3 transformedBaseEdge = Double4x4.TransformPoint(baseEdgePoint, matrix);

            double transformedRadius = Double3.Length(transformedBaseEdge - transformedBaseCenter);

            return new Cone(transformedApex, transformedBaseCenter, transformedRadius);
        }

        /// <summary>
        /// Creates a cone from an apex, axis direction, height, and base radius.
        /// </summary>
        /// <param name="apex">The tip of the cone.</param>
        /// <param name="axisDirection">The direction from apex to base (will be normalized).</param>
        /// <param name="height">The height of the cone.</param>
        /// <param name="baseRadius">The radius of the base.</param>
        /// <returns>The created cone.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Cone FromAxisDirection(Double3 apex, Double3 axisDirection, double height, double baseRadius)
        {
            Double3 normalizedAxis = Double3.Normalize(axisDirection);
            Double3 baseCenter = apex + normalizedAxis * height;
            return new Cone(apex, baseCenter, baseRadius);
        }

        // --- IBoundingShape Implementation ---

        /// <summary>
        /// Returns the point on the cone that is farthest in the given direction.
        /// The support point is either the apex or a point on the base circle edge.
        /// </summary>
        /// <param name="direction">The direction to search in.</param>
        /// <returns>The farthest point on the cone in the given direction.</returns>
        public Double3 SupportMap(Double3 direction)
        {
            double dirLength = Double3.Length(direction);
            if (dirLength < double.Epsilon)
                return Apex;

            Double3 normalizedDir = direction / dirLength;

            // Calculate the cone's axis direction
            Double3 axis = BaseCenter - Apex;
            double height = Double3.Length(axis);

            if (height < double.Epsilon)
                return Apex; // Degenerate cone

            Double3 axisNorm = axis / height;

            // Check if the apex is the farthest point
            double apexDot = Double3.Dot(Apex, normalizedDir);

            // Calculate a perpendicular direction to the axis within the base plane
            // Project the search direction onto the base plane
            double projOnAxis = Double3.Dot(normalizedDir, axisNorm);
            Double3 dirInBasePlane = normalizedDir - axisNorm * projOnAxis;

            double basePlaneDirLength = Double3.Length(dirInBasePlane);

            Double3 baseEdgePoint;
            if (basePlaneDirLength < double.Epsilon)
            {
                // Direction is parallel to the axis
                // Choose the base center or apex based on which is farther
                double baseCenterDot = Double3.Dot(BaseCenter, normalizedDir);
                return apexDot >= baseCenterDot ? Apex : BaseCenter;
            }
            else
            {
                // Find the point on the base circle edge in the direction of dirInBasePlane
                Double3 radialDir = dirInBasePlane / basePlaneDirLength;
                baseEdgePoint = BaseCenter + radialDir * BaseRadius;
            }

            // Compare apex vs base edge point
            double baseEdgeDot = Double3.Dot(baseEdgePoint, normalizedDir);

            return apexDot >= baseEdgeDot ? Apex : baseEdgePoint;
        }

        /// <summary>
        /// Generates mesh data for rendering this cone.
        /// </summary>
        /// <param name="mode">Wireframe for outline, Solid for filled cone.</param>
        /// <param name="resolution">Number of segments around the base circle.</param>
        /// <returns>Mesh data for rendering.</returns>
        public GeometryData GetGeometryData(int resolution = 16)
        {
            resolution = Maths.Max(resolution, 3);
            var geometryData = new GeometryData();

            Double3 axis = BaseCenter - Apex;
            double height = Double3.Length(axis);

            if (height < double.Epsilon)
            {
                geometryData.AddVertex(Apex);
                return geometryData;
            }

            Double3 axisNorm = axis / height;

            // Find two perpendicular vectors to the axis
            Double3 perpendicular1 = Maths.Abs(axisNorm.X) < 0.9 ?
                Double3.Cross(axisNorm, new Double3(1, 0, 0)) :
                Double3.Cross(axisNorm, new Double3(0, 1, 0));
            perpendicular1 = Double3.Normalize(perpendicular1);

            Double3 perpendicular2 = Double3.Cross(axisNorm, perpendicular1);

            // Add apex vertex
            var apex = geometryData.AddVertex(Apex);

            // Add base center vertex
            var baseCenter = geometryData.AddVertex(BaseCenter);

            // Generate base circle vertices
            var baseVerts = new GeometryData.Vertex[resolution];
            for (int i = 0; i < resolution; i++)
            {
                double angle = i * 2.0 * Maths.PI / resolution;
                double cosAngle = Maths.Cos(angle);
                double sinAngle = Maths.Sin(angle);

                Double3 point = BaseCenter +
                              perpendicular1 * (BaseRadius * cosAngle) +
                              perpendicular2 * (BaseRadius * sinAngle);
                baseVerts[i] = geometryData.AddVertex(point);
            }

            // Generate lateral surface triangles (apex to base edge)
            for (int i = 0; i < resolution; i++)
            {
                int next = (i + 1) % resolution;
                geometryData.AddFace(apex, baseVerts[i], baseVerts[next]);
            }

            // Generate base triangles (base center to edge)
            for (int i = 0; i < resolution; i++)
            {
                int next = (i + 1) % resolution;
                geometryData.AddFace(baseCenter, baseVerts[next], baseVerts[i]);
            }

            return geometryData;
        }

        // --- IEquatable & IFormattable Implementation ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Cone other)
        {
            return Apex.Equals(other.Apex) &&
                   BaseCenter.Equals(other.BaseCenter) &&
                   Maths.Abs(BaseRadius - other.BaseRadius) < double.Epsilon;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is Cone other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(Apex, BaseCenter, BaseRadius);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format, IFormatProvider? formatProvider = null)
        {
            return string.Format(formatProvider,
                "ConeD(Apex: {0}, BaseCenter: {1}, Radius: {2})",
                Apex.ToString(format, formatProvider),
                BaseCenter.ToString(format, formatProvider),
                BaseRadius.ToString(format, formatProvider));
        }

        public static bool operator ==(Cone left, Cone right) => left.Equals(right);
        public static bool operator !=(Cone left, Cone right) => !left.Equals(right);
    }
}
