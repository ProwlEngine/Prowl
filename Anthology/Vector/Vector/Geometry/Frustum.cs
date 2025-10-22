// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Represents a 3D viewing frustum defined by 6 planes.
    /// Planes are ordered: Near, Far, Left, Right, Top, Bottom.
    /// </summary>
    public struct Frustum : IEquatable<Frustum>, IFormattable, IBoundingShape
    {
        /// <summary>The 6 frustum planes: Near, Far, Left, Right, Top, Bottom.</summary>
        public Plane[] Planes;

        /// <summary>
        /// Initializes a new frustum with the specified planes.
        /// </summary>
        /// <param name="planes">Array of 6 planes in order: Near, Far, Left, Right, Top, Bottom.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Frustum(Plane[] planes)
        {
            if (planes == null || planes.Length != 6)
                throw new ArgumentException("Frustum requires exactly 6 planes", nameof(planes));
            
            Planes = new Plane[6];
            Array.Copy(planes, Planes, 6);
        }

        /// <summary>
        /// Initializes a new frustum with individual planes.
        /// </summary>
        /// <param name="near">Near plane.</param>
        /// <param name="far">Far plane.</param>
        /// <param name="left">Left plane.</param>
        /// <param name="right">Right plane.</param>
        /// <param name="top">Top plane.</param>
        /// <param name="bottom">Bottom plane.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Frustum(Plane near, Plane far, Plane left, 
                             Plane right, Plane top, Plane bottom)
        {
            Planes = new Plane[6] { near, far, left, right, top, bottom };
        }

        /// <summary>Gets or sets the near plane.</summary>
        public Plane Near
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Planes[0];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Planes[0] = value;
        }

        /// <summary>Gets or sets the far plane.</summary>
        public Plane Far
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Planes[1];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Planes[1] = value;
        }

        /// <summary>Gets or sets the left plane.</summary>
        public Plane Left
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Planes[2];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Planes[2] = value;
        }

        /// <summary>Gets or sets the right plane.</summary>
        public Plane Right
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Planes[3];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Planes[3] = value;
        }

        /// <summary>Gets or sets the top plane.</summary>
        public Plane Top
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Planes[4];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Planes[4] = value;
        }

        /// <summary>Gets or sets the bottom plane.</summary>
        public Plane Bottom
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Planes[5];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Planes[5] = value;
        }

        /// <summary>
        /// Creates a frustum from a view-projection matrix.
        /// Extracts the 6 frustum planes from the combined matrix.
        /// </summary>
        /// <param name="viewProjectionMatrix">The combined view-projection matrix.</param>
        /// <returns>The extracted frustum.</returns>
        public static Frustum FromMatrix(Double4x4 viewProjectionMatrix)
        {
            var planes = new Plane[6];

            // Extract planes from matrix (Gribb/Hartmann method)
            // For column-major matrix: Row i = (c0[i], c1[i], c2[i], c3[i])

            // Left plane = row3 + row0
            Double3 leftNormal = new Double3(
                viewProjectionMatrix.c0.W + viewProjectionMatrix.c0.X,
                viewProjectionMatrix.c1.W + viewProjectionMatrix.c1.X,
                viewProjectionMatrix.c2.W + viewProjectionMatrix.c2.X
            );
            double leftD = viewProjectionMatrix.c3.W + viewProjectionMatrix.c3.X;

            // Right plane = row3 - row0
            Double3 rightNormal = new Double3(
                viewProjectionMatrix.c0.W - viewProjectionMatrix.c0.X,
                viewProjectionMatrix.c1.W - viewProjectionMatrix.c1.X,
                viewProjectionMatrix.c2.W - viewProjectionMatrix.c2.X
            );
            double rightD = viewProjectionMatrix.c3.W - viewProjectionMatrix.c3.X;

            // Bottom plane = row3 + row1
            Double3 bottomNormal = new Double3(
                viewProjectionMatrix.c0.W + viewProjectionMatrix.c0.Y,
                viewProjectionMatrix.c1.W + viewProjectionMatrix.c1.Y,
                viewProjectionMatrix.c2.W + viewProjectionMatrix.c2.Y
            );
            double bottomD = viewProjectionMatrix.c3.W + viewProjectionMatrix.c3.Y;

            // Top plane = row3 - row1
            Double3 topNormal = new Double3(
                viewProjectionMatrix.c0.W - viewProjectionMatrix.c0.Y,
                viewProjectionMatrix.c1.W - viewProjectionMatrix.c1.Y,
                viewProjectionMatrix.c2.W - viewProjectionMatrix.c2.Y
            );
            double topD = viewProjectionMatrix.c3.W - viewProjectionMatrix.c3.Y;

            // Near plane = row2 (for 0..1 depth range, DirectX style)
            Double3 nearNormal = new Double3(
                viewProjectionMatrix.c0.Z,
                viewProjectionMatrix.c1.Z,
                viewProjectionMatrix.c2.Z
            );
            double nearD = viewProjectionMatrix.c3.Z;

            // Far plane = row3 - row2
            Double3 farNormal = new Double3(
                viewProjectionMatrix.c0.W - viewProjectionMatrix.c0.Z,
                viewProjectionMatrix.c1.W - viewProjectionMatrix.c1.Z,
                viewProjectionMatrix.c2.W - viewProjectionMatrix.c2.Z
            );
            double farD = viewProjectionMatrix.c3.W - viewProjectionMatrix.c3.Z;

            // Create planes with proper D value sign conversion
            // Gribb/Hartmann extracts planes as: Ax + By + Cz + D = 0
            // Our Plane class uses: dot(normal, point) = D  (i.e., Ax + By + Cz - D = 0)
            // So we need to negate D when creating the planes
            // The Plane constructor will handle normalization correctly
            planes[0] = new Plane(nearNormal, -nearD);
            planes[1] = new Plane(farNormal, -farD);
            planes[2] = new Plane(leftNormal, -leftD);
            planes[3] = new Plane(rightNormal, -rightD);
            planes[4] = new Plane(topNormal, -topD);
            planes[5] = new Plane(bottomNormal, -bottomD);

            return new Frustum(planes);
        }

        /// <summary>
        /// Creates a frustum from separate view and projection matrices.
        /// </summary>
        /// <param name="viewMatrix">The view matrix.</param>
        /// <param name="projectionMatrix">The projection matrix.</param>
        /// <returns>The frustum.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Frustum FromMatrices(Double4x4 viewMatrix, Double4x4 projectionMatrix)
        {
            Double4x4 viewProjection = projectionMatrix * viewMatrix;
            return FromMatrix(viewProjection);
        }

        /// <summary>
        /// Checks if a point is contained within the frustum.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside the frustum.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Double3 point)
        {
            Double3[] normals = new Double3[6];
            double[] ds = new double[6];

            for (int i = 0; i < 6; i++)
            {
                normals[i] = Planes[i].Normal;
                ds[i] = Planes[i].D;
            }

            return Intersection.FrustumContainsPoint(normals, ds, point);
        }

        /// <summary>
        /// Checks if a sphere intersects with the frustum.
        /// </summary>
        /// <param name="sphere">The sphere to test.</param>
        /// <returns>True if the sphere intersects the frustum.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(Sphere sphere)
        {
            Double3[] normals = new Double3[6];
            double[] ds = new double[6];
            
            for (int i = 0; i < 6; i++)
            {
                normals[i] = Planes[i].Normal;
                ds[i] = Planes[i].D;
            }
            
            return Intersection.FrustumIntersectsSphere(normals, ds, sphere.Center, sphere.Radius);
        }

        /// <summary>
        /// Checks if an AABB intersects with the frustum.
        /// </summary>
        /// <param name="aabb">The AABB to test.</param>
        /// <returns>True if the AABB intersects the frustum.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(AABB aabb)
        {
            Double3[] normals = new Double3[6];
            double[] ds = new double[6];
            
            for (int i = 0; i < 6; i++)
            {
                normals[i] = Planes[i].Normal;
                ds[i] = Planes[i].D;
            }
            
            return Intersection.FrustumIntersectsAABB(normals, ds, aabb.Min, aabb.Max);
        }

        /// <summary>
        /// Transforms the frustum by a matrix.
        /// </summary>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>The transformed frustum.</returns>
        public Frustum Transform(Double4x4 matrix)
        {
            var transformedPlanes = new Plane[6];
            
            // Transform each plane by the inverse transpose of the matrix
            Double4x4 invTranspose = Double4x4.Transpose(matrix.Invert());
            
            for (int i = 0; i < 6; i++)
            {
                Double4 planeVec = new Double4(Planes[i].Normal, Planes[i].D);
                Double4 transformedPlaneVec = invTranspose * planeVec;
                transformedPlanes[i] = new Plane(transformedPlaneVec.XYZ, transformedPlaneVec.W);
            }
            
            return new Frustum(transformedPlanes);
        }

        /// <summary>
        /// Normalizes all frustum planes to ensure consistent distance calculations.
        /// </summary>
        public void Normalize()
        {
            for (int i = 0; i < 6; i++)
            {
                double length = Double3.Length(Planes[i].Normal);
                if (length > double.Epsilon)
                {
                    Planes[i] = new Plane(
                        Planes[i].Normal / length,
                        Planes[i].D / length
                    );
                }
            }
        }

        /// <summary>
        /// Returns a normalized version of this frustum.
        /// </summary>
        /// <returns>The normalized frustum.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Frustum Normalized()
        {
            var result = this;
            result.Normalize();
            return result;
        }

        /// <summary>
        /// Classifies a point relative to the frustum.
        /// </summary>
        /// <param name="point">The point to classify.</param>
        /// <returns>Number of planes the point is behind (0 = inside, 1-6 = partially/fully outside).</returns>
        public int ClassifyPoint(Double3 point)
        {
            int planesOutside = 0;
            for (int i = 0; i < 6; i++)
            {
                if (Planes[i].GetSignedDistanceToPoint(point) < -double.Epsilon)
                {
                    planesOutside++;
                }
            }
            return planesOutside;
        }
        
        /// <summary>
        /// Classifies a sphere relative to the frustum.
        /// </summary>
        /// <param name="sphere">The sphere to classify.</param>
        /// <returns>Number of planes the sphere is completely behind.</returns>
        public int ClassifySphere(Sphere sphere)
        {
            int planesOutside = 0;
            for (int i = 0; i < 6; i++)
            {
                double distance = Planes[i].GetSignedDistanceToPoint(sphere.Center);
                if (distance < -sphere.Radius - double.Epsilon)
                {
                    planesOutside++;
                }
            }
            return planesOutside;
        }
        
        /// <summary>
        /// Classifies an AABB relative to the frustum.
        /// </summary>
        /// <param name="aabb">The AABB to classify.</param>
        /// <returns>Number of planes the AABB is completely behind.</returns>
        public int ClassifyAABB(AABB aabb)
        {
            int planesOutside = 0;
            for (int i = 0; i < 6; i++)
            {
                var classification = Intersection.ClassifyAABBToPlane(aabb.Min, aabb.Max, Planes[i].Normal, Planes[i].D);
                if (classification == Intersection.PlaneIntersectionType.Back)
                {
                    planesOutside++;
                }
            }
            return planesOutside;
        }
        
        /// <summary>
        /// Checks if the frustum is valid (all planes properly oriented).
        /// </summary>
        /// <returns>True if the frustum is valid.</returns>
        public bool IsValid()
        {
            if (Planes == null || Planes.Length != 6)
                return false;
                
            // Check that all plane normals have reasonable length
            for (int i = 0; i < 6; i++)
            {
                if (Double3.LengthSquared(Planes[i].Normal) < double.Epsilon * double.Epsilon)
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Creates a frustum from camera parameters.
        /// </summary>
        /// <param name="position">Camera position.</param>
        /// <param name="forward">Camera forward direction (normalized).</param>
        /// <param name="up">Camera up direction (normalized).</param>
        /// <param name="fovY">Vertical field of view in radians.</param>
        /// <param name="aspect">Aspect ratio (width/height).</param>
        /// <param name="nearDist">Near plane distance.</param>
        /// <param name="farDist">Far plane distance.</param>
        /// <returns>The camera frustum.</returns>
        public static Frustum FromCamera(Double3 position, Double3 forward, Double3 up, 
                                               double fovY, double aspect, double nearDist, double farDist)
        {
            Double3 right = Double3.Normalize(Double3.Cross(forward, up));
            Double3 actualUp = Double3.Cross(right, forward);
            
            double halfFovY = fovY / 2.0;
            double tanHalfFovY = Maths.Tan(halfFovY);
            double tanHalfFovX = tanHalfFovY * aspect;
            
            // Calculate plane normals (pointing inward)
            Double3 nearCenter = position + forward * nearDist;
            Double3 farCenter = position + forward * farDist;
            
            // Near and far planes
            Plane near = new Plane(forward, Double3.Dot(forward, nearCenter));
            Plane far = new Plane(-forward, Double3.Dot(-forward, farCenter));
            
            // Side planes
            Double3 leftNormal = Double3.Normalize(Double3.Cross(actualUp, forward + right * tanHalfFovX));
            Double3 rightNormal = Double3.Normalize(Double3.Cross(forward - right * tanHalfFovX, actualUp));
            Double3 topNormal = Double3.Normalize(Double3.Cross(forward + actualUp * tanHalfFovY, right));
            Double3 bottomNormal = Double3.Normalize(Double3.Cross(right, forward - actualUp * tanHalfFovY));
            
            Plane p_left = new Plane(leftNormal, Double3.Dot(leftNormal, position));
            Plane p_right = new Plane(rightNormal, Double3.Dot(rightNormal, position));
            Plane p_top = new Plane(topNormal, Double3.Dot(topNormal, position));
            Plane p_bottom = new Plane(bottomNormal, Double3.Dot(bottomNormal, position));
            
            return new Frustum(near, far, p_left, p_right, p_top, p_bottom);
        }
        
        /// <summary>
        /// Creates an orthographic frustum.
        /// </summary>
        /// <param name="left">Left boundary.</param>
        /// <param name="right">Right boundary.</param>
        /// <param name="bottom">Bottom boundary.</param>
        /// <param name="top">Top boundary.</param>
        /// <param name="nearDist">Near plane distance.</param>
        /// <param name="farDist">Far plane distance.</param>
        /// <returns>The orthographic frustum.</returns>
        public static Frustum CreateOrthographic(double left, double right, double bottom, 
                                                        double top, double nearDist, double farDist)
        {
            var planes = new Plane[6];
            
            // Near and far planes (assuming Z forward)
            planes[0] = new Plane(new Double3(0.0, 0.0, 1.0), nearDist);
            planes[1] = new Plane(new Double3(0.0, 0.0, -1.0), -farDist);
            
            // Side planes
            planes[2] = new Plane(new Double3(1.0, 0.0, 0.0), -left);    // Left
            planes[3] = new Plane(new Double3(-1.0, 0.0, 0.0), right);   // Right
            planes[4] = new Plane(new Double3(0.0, -1.0, 0.0), top);     // Top
            planes[5] = new Plane(new Double3(0.0, 1.0, 0.0), -bottom);  // Bottom
            
            return new Frustum(planes);
        }
        
        /// <summary>
        /// Expands the frustum by a given amount along all plane normals.
        /// </summary>
        /// <param name="amount">Amount to expand by.</param>
        public void Expand(double amount)
        {
            for (int i = 0; i < 6; i++)
            {
                Planes[i] = new Plane(Planes[i].Normal, Planes[i].D - amount);
            }
        }
        
        /// <summary>
        /// Returns an expanded version of this frustum.
        /// </summary>
        /// <param name="amount">Amount to expand by.</param>
        /// <returns>The expanded frustum.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Frustum Expanded(double amount)
        {
            var result = this;
            result.Expand(amount);
            return result;
        }

        /// <summary>
        /// Computes the 8 corner points of the frustum by intersecting planes.
        /// </summary>
        /// <returns>Array of 8 corner points.</returns>
        public Double3[] GetCorners()
        {
            if (Planes == null || Planes.Length != 6)
                return new Double3[0];

            var corners = new Double3[8];

            // Corner indices represent binary combinations of Near/Far, Left/Right, Top/Bottom
            // 0: Near-Left-Bottom,  1: Near-Right-Bottom,  2: Near-Left-Top,  3: Near-Right-Top
            // 4: Far-Left-Bottom,   5: Far-Right-Bottom,   6: Far-Left-Top,   7: Far-Right-Top

            corners[0] = IntersectThreePlanes(Near, Left, Bottom);   // Near-Left-Bottom
            corners[1] = IntersectThreePlanes(Near, Right, Bottom);  // Near-Right-Bottom
            corners[2] = IntersectThreePlanes(Near, Left, Top);      // Near-Left-Top
            corners[3] = IntersectThreePlanes(Near, Right, Top);     // Near-Right-Top
            corners[4] = IntersectThreePlanes(Far, Left, Bottom);    // Far-Left-Bottom
            corners[5] = IntersectThreePlanes(Far, Right, Bottom);   // Far-Right-Bottom
            corners[6] = IntersectThreePlanes(Far, Left, Top);       // Far-Left-Top
            corners[7] = IntersectThreePlanes(Far, Right, Top);      // Far-Right-Top

            return corners;
        }

        /// <summary>
        /// Computes the intersection point of three planes.
        /// </summary>
        private static Double3 IntersectThreePlanes(Plane p1, Plane p2, Plane p3)
        {
            // Using Cramer's rule to solve the system:
            // n1·p = d1
            // n2·p = d2
            // n3·p = d3

            Double3 n1 = p1.Normal;
            Double3 n2 = p2.Normal;
            Double3 n3 = p3.Normal;

            double d1 = p1.D;
            double d2 = p2.D;
            double d3 = p3.D;

            // Calculate determinant
            Double3 cross = Double3.Cross(n2, n3);
            double det = Double3.Dot(n1, cross);

            // If determinant is near zero, planes don't intersect at a unique point
            if (Maths.Abs(det) < 1e-10)
                return Double3.Zero; // Fallback for degenerate case

            // Calculate intersection point
            Double3 result = (d1 * cross +
                            Double3.Cross(n1, d3 * n2 - d2 * n3)) / det;

            return result;
        }

        // --- IBoundingShape Implementation ---

        /// <summary>
        /// Returns the corner of the frustum that is farthest in the given direction.
        /// </summary>
        /// <param name="direction">The direction to search in.</param>
        /// <returns>The farthest corner in the given direction.</returns>
        public Double3 SupportMap(Double3 direction)
        {
            Double3[] corners = GetCorners();

            if (corners.Length == 0)
                return Double3.Zero;

            double maxDot = Double3.Dot(corners[0], direction);
            int maxIndex = 0;

            for (int i = 1; i < corners.Length; i++)
            {
                double dot = Double3.Dot(corners[i], direction);
                if (dot > maxDot)
                {
                    maxDot = dot;
                    maxIndex = i;
                }
            }

            return corners[maxIndex];
        }

        /// <summary>
        /// Generates mesh data for rendering this frustum.
        /// </summary>
        /// <param name="mode">Wireframe for edges. Solid mode not supported for Frustum.</param>
        /// <param name="resolution">Unused for Frustum (topology is fixed).</param>
        /// <returns>Mesh data for rendering.</returns>
        public MeshData GetMeshData(MeshMode mode, int resolution = 16)
        {
            Double3[] corners = GetCorners();

            if (corners == null || corners.Length != 8)
                return new MeshData(new Double3[0], MeshTopology.LineList);

            // Corner indices from GetCorners():
            // 0: Near-Left-Bottom,  1: Near-Right-Bottom,  2: Near-Left-Top,  3: Near-Right-Top
            // 4: Far-Left-Bottom,   5: Far-Right-Bottom,   6: Far-Left-Top,   7: Far-Right-Top

            // Frustum is always rendered as wireframe
            // 12 edges: 4 for near plane, 4 for far plane, 4 connecting edges
            var vertices = new Double3[24];
            int idx = 0;

            // Near plane rectangle: bottom edge (0-1), right edge (1-3), top edge (3-2), left edge (2-0)
            vertices[idx++] = corners[0]; vertices[idx++] = corners[1]; // Bottom
            vertices[idx++] = corners[1]; vertices[idx++] = corners[3]; // Right
            vertices[idx++] = corners[3]; vertices[idx++] = corners[2]; // Top
            vertices[idx++] = corners[2]; vertices[idx++] = corners[0]; // Left

            // Far plane rectangle: bottom edge (4-5), right edge (5-7), top edge (7-6), left edge (6-4)
            vertices[idx++] = corners[4]; vertices[idx++] = corners[5]; // Bottom
            vertices[idx++] = corners[5]; vertices[idx++] = corners[7]; // Right
            vertices[idx++] = corners[7]; vertices[idx++] = corners[6]; // Top
            vertices[idx++] = corners[6]; vertices[idx++] = corners[4]; // Left

            // Connecting edges from near to far
            vertices[idx++] = corners[0]; vertices[idx++] = corners[4]; // Left-Bottom
            vertices[idx++] = corners[1]; vertices[idx++] = corners[5]; // Right-Bottom
            vertices[idx++] = corners[2]; vertices[idx++] = corners[6]; // Left-Top
            vertices[idx++] = corners[3]; vertices[idx++] = corners[7]; // Right-Top

            return new MeshData(vertices, MeshTopology.LineList);
        }

        // --- IEquatable & IFormattable Implementation ---
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Frustum other)
        {
            if (Planes == null && other.Planes == null) return true;
            if (Planes == null || other.Planes == null) return false;
            if (Planes.Length != other.Planes.Length) return false;
            
            for (int i = 0; i < Planes.Length; i++)
            {
                if (!Planes[i].Equals(other.Planes[i]))
                    return false;
            }
            return true;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is Frustum other && Equals(other);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            if (Planes == null) return 0;
            
            var hash = new HashCode();
            for (int i = 0; i < Planes.Length; i++)
            {
                hash.Add(Planes[i]);
            }
            return hash.ToHashCode();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format, IFormatProvider? formatProvider = null)
        {
            if (Planes == null)
                return "FrustrumD(null)";
                
            return string.Format(formatProvider, 
                "FrustrumD(Near: {0}, Far: {1}, Left: {2}, Right: {3}, Top: {4}, Bottom: {5})",
                Planes[0].ToString(format, formatProvider),
                Planes[1].ToString(format, formatProvider),
                Planes[2].ToString(format, formatProvider),
                Planes[3].ToString(format, formatProvider),
                Planes[4].ToString(format, formatProvider),
                Planes[5].ToString(format, formatProvider));
        }
        
        public static bool operator ==(Frustum left, Frustum right) => left.Equals(right);
        public static bool operator !=(Frustum left, Frustum right) => !left.Equals(right);
    }
}
