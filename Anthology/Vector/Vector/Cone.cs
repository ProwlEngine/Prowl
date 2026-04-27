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
        public Float3 Apex;

        /// <summary>The center of the base circle.</summary>
        public Float3 BaseCenter;

        /// <summary>The radius of the base circle.</summary>
        public float BaseRadius;

        /// <summary>
        /// Initializes a new cone with the specified apex, base center, and base radius.
        /// </summary>
        /// <param name="apex">The tip of the cone.</param>
        /// <param name="baseCenter">The center of the base circle.</param>
        /// <param name="baseRadius">The radius of the base circle.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cone(Float3 apex, Float3 baseCenter, float baseRadius)
        {
            Apex = apex;
            BaseCenter = baseCenter;
            BaseRadius = baseRadius;
        }

        /// <summary>
        /// Initializes a new cone with the specified apex, base center, base radius using individual coordinate components.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cone(float apexX, float apexY, float apexZ,
                    float baseCenterX, float baseCenterY, float baseCenterZ,
                    float baseRadius)
        {
            Apex = new Float3(apexX, apexY, apexZ);
            BaseCenter = new Float3(baseCenterX, baseCenterY, baseCenterZ);
            BaseRadius = baseRadius;
        }

        /// <summary>
        /// Gets the height of the cone (distance from apex to base).
        /// </summary>
        public float Height
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Float3.Length(BaseCenter - Apex);
        }

        /// <summary>
        /// Gets the axis direction of the cone (normalized vector from apex to base center).
        /// </summary>
        public Float3 AxisDirection
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Float3.Normalize(BaseCenter - Apex);
        }

        /// <summary>
        /// Gets the volume of the cone.
        /// </summary>
        public float Volume
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Maths.PI * BaseRadius * BaseRadius * Height) / 3.0f;
        }

        /// <summary>
        /// Gets the lateral surface area of the cone (excluding the base).
        /// </summary>
        public float LateralSurfaceArea
        {
            get
            {
                float slantHeight = Maths.Sqrt(Height * Height + BaseRadius * BaseRadius);
                return Maths.PI * BaseRadius * slantHeight;
            }
        }

        /// <summary>
        /// Gets the total surface area of the cone (including the base).
        /// </summary>
        public float TotalSurfaceArea
        {
            get
            {
                float slantHeight = Maths.Sqrt(Height * Height + BaseRadius * BaseRadius);
                return Maths.PI * BaseRadius * (BaseRadius + slantHeight);
            }
        }

        /// <summary>
        /// Checks if a point is inside the cone.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside the cone.</returns>
        public bool Contains(Float3 point)
        {
            Float3 axis = BaseCenter - Apex;
            float height = Float3.Length(axis);

            if (height < float.Epsilon)
                return false; // Degenerate cone

            Float3 axisNorm = axis / height;
            Float3 apexToPoint = point - Apex;

            // Project point onto cone axis
            float projectionLength = Float3.Dot(apexToPoint, axisNorm);

            // Check if point is within height bounds
            if (projectionLength < 0.0 || projectionLength > height)
                return false;

            // Calculate the radius at this height
            float radiusAtHeight = (projectionLength / height) * BaseRadius;

            // Calculate perpendicular distance from axis
            Float3 projectionPoint = Apex + axisNorm * projectionLength;
            float perpDistance = Float3.Length(point - projectionPoint);

            return perpDistance <= radiusAtHeight;
        }

        /// <summary>
        /// Transforms the cone by a 4x4 matrix.
        /// Note: Non-uniform scaling may distort the cone into an elliptical cone.
        /// </summary>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>The transformed cone.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cone Transform(Float4x4 matrix)
        {
            Float3 transformedApex = Float4x4.TransformPoint(Apex, matrix);
            Float3 transformedBaseCenter = Float4x4.TransformPoint(BaseCenter, matrix);

            // For radius, we need to account for scaling
            // Calculate a point on the base circle and see how it transforms
            Float3 axis = BaseCenter - Apex;
            Float3 perpendicular = Maths.Abs(axis.X) < 0.9 ?
                Float3.Cross(axis, new Float3(1, 0, 0)) :
                Float3.Cross(axis, new Float3(0, 1, 0));
            perpendicular = Float3.Normalize(perpendicular);

            Float3 baseEdgePoint = BaseCenter + perpendicular * BaseRadius;
            Float3 transformedBaseEdge = Float4x4.TransformPoint(baseEdgePoint, matrix);

            float transformedRadius = Float3.Length(transformedBaseEdge - transformedBaseCenter);

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
        public static Cone FromAxisDirection(Float3 apex, Float3 axisDirection, float height, float baseRadius)
        {
            Float3 normalizedAxis = Float3.Normalize(axisDirection);
            Float3 baseCenter = apex + normalizedAxis * height;
            return new Cone(apex, baseCenter, baseRadius);
        }

        // --- IBoundingShape Implementation ---

        /// <summary>
        /// Returns the point on the cone that is farthest in the given direction.
        /// The support point is either the apex or a point on the base circle edge.
        /// </summary>
        /// <param name="direction">The direction to search in.</param>
        /// <returns>The farthest point on the cone in the given direction.</returns>
        public Float3 SupportMap(Float3 direction)
        {
            float dirLength = Float3.Length(direction);
            if (dirLength < float.Epsilon)
                return Apex;

            Float3 normalizedDir = direction / dirLength;

            // Calculate the cone's axis direction
            Float3 axis = BaseCenter - Apex;
            float height = Float3.Length(axis);

            if (height < float.Epsilon)
                return Apex; // Degenerate cone

            Float3 axisNorm = axis / height;

            // Check if the apex is the farthest point
            float apexDot = Float3.Dot(Apex, normalizedDir);

            // Calculate a perpendicular direction to the axis within the base plane
            // Project the search direction onto the base plane
            float projOnAxis = Float3.Dot(normalizedDir, axisNorm);
            Float3 dirInBasePlane = normalizedDir - axisNorm * projOnAxis;

            float basePlaneDirLength = Float3.Length(dirInBasePlane);

            Float3 baseEdgePoint;
            if (basePlaneDirLength < float.Epsilon)
            {
                // Direction is parallel to the axis
                // Choose the base center or apex based on which is farther
                float baseCenterDot = Float3.Dot(BaseCenter, normalizedDir);
                return apexDot >= baseCenterDot ? Apex : BaseCenter;
            }
            else
            {
                // Find the point on the base circle edge in the direction of dirInBasePlane
                Float3 radialDir = dirInBasePlane / basePlaneDirLength;
                baseEdgePoint = BaseCenter + radialDir * BaseRadius;
            }

            // Compare apex vs base edge point
            float baseEdgeDot = Float3.Dot(baseEdgePoint, normalizedDir);

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

            Float3 axis = BaseCenter - Apex;
            float height = Float3.Length(axis);

            if (height < float.Epsilon)
            {
                geometryData.AddVertex(Apex);
                return geometryData;
            }

            Float3 axisNorm = axis / height;

            // Find two perpendicular vectors to the axis
            Float3 perpendicular1 = Maths.Abs(axisNorm.X) < 0.9 ?
                Float3.Cross(axisNorm, new Float3(1, 0, 0)) :
                Float3.Cross(axisNorm, new Float3(0, 1, 0));
            perpendicular1 = Float3.Normalize(perpendicular1);

            Float3 perpendicular2 = Float3.Cross(axisNorm, perpendicular1);

            // Add apex vertex
            var apex = geometryData.AddVertex(Apex);

            // Add base center vertex
            var baseCenter = geometryData.AddVertex(BaseCenter);

            // Generate base circle vertices
            var baseVerts = new GeometryData.Vertex[resolution];
            for (int i = 0; i < resolution; i++)
            {
                float angle = i * 2.0f * Maths.PI / resolution;
                float cosAngle = Maths.Cos(angle);
                float sinAngle = Maths.Sin(angle);

                Float3 point = BaseCenter +
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
                   Maths.Abs(BaseRadius - other.BaseRadius) < float.Epsilon;
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
