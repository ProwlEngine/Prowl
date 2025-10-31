// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

using Prowl.Vector.Geometry;

namespace Prowl.Vector
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
        public static Frustum FromMatrix(Float4x4 viewProjectionMatrix)
        {
            var planes = new Plane[6];

            // Extract planes from matrix (Gribb/Hartmann method)
            // For column-major matrix: Row i = (c0[i], c1[i], c2[i], c3[i])

            // Left plane = row3 + row0
            Float3 leftNormal = new Float3(
                viewProjectionMatrix.c0.W + viewProjectionMatrix.c0.X,
                viewProjectionMatrix.c1.W + viewProjectionMatrix.c1.X,
                viewProjectionMatrix.c2.W + viewProjectionMatrix.c2.X
            );
            float leftD = viewProjectionMatrix.c3.W + viewProjectionMatrix.c3.X;

            // Right plane = row3 - row0
            Float3 rightNormal = new Float3(
                viewProjectionMatrix.c0.W - viewProjectionMatrix.c0.X,
                viewProjectionMatrix.c1.W - viewProjectionMatrix.c1.X,
                viewProjectionMatrix.c2.W - viewProjectionMatrix.c2.X
            );
            float rightD = viewProjectionMatrix.c3.W - viewProjectionMatrix.c3.X;

            // Bottom plane = row3 + row1
            Float3 bottomNormal = new Float3(
                viewProjectionMatrix.c0.W + viewProjectionMatrix.c0.Y,
                viewProjectionMatrix.c1.W + viewProjectionMatrix.c1.Y,
                viewProjectionMatrix.c2.W + viewProjectionMatrix.c2.Y
            );
            float bottomD = viewProjectionMatrix.c3.W + viewProjectionMatrix.c3.Y;

            // Top plane = row3 - row1
            Float3 topNormal = new Float3(
                viewProjectionMatrix.c0.W - viewProjectionMatrix.c0.Y,
                viewProjectionMatrix.c1.W - viewProjectionMatrix.c1.Y,
                viewProjectionMatrix.c2.W - viewProjectionMatrix.c2.Y
            );
            float topD = viewProjectionMatrix.c3.W - viewProjectionMatrix.c3.Y;

            // Near plane = row2 (for 0..1 depth range, DirectX style)
            Float3 nearNormal = new Float3(
                viewProjectionMatrix.c0.Z,
                viewProjectionMatrix.c1.Z,
                viewProjectionMatrix.c2.Z
            );
            float nearD = viewProjectionMatrix.c3.Z;

            // Far plane = row3 - row2
            Float3 farNormal = new Float3(
                viewProjectionMatrix.c0.W - viewProjectionMatrix.c0.Z,
                viewProjectionMatrix.c1.W - viewProjectionMatrix.c1.Z,
                viewProjectionMatrix.c2.W - viewProjectionMatrix.c2.Z
            );
            float farD = viewProjectionMatrix.c3.W - viewProjectionMatrix.c3.Z;

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
        public static Frustum FromMatrices(Float4x4 viewMatrix, Float4x4 projectionMatrix)
        {
            Float4x4 viewProjection = projectionMatrix * viewMatrix;
            return FromMatrix(viewProjection);
        }

        /// <summary>
        /// Checks if a point is contained within the frustum.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside the frustum.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Float3 point)
        {
            Float3[] normals = new Float3[6];
            float[] ds = new float[6];

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
            Float3[] normals = new Float3[6];
            float[] ds = new float[6];
            
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
            Float3[] normals = new Float3[6];
            float[] ds = new float[6];
            
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
        public Frustum Transform(Float4x4 matrix)
        {
            var transformedPlanes = new Plane[6];
            
            // Transform each plane by the inverse transpose of the matrix
            Float4x4 invTranspose = Float4x4.Transpose(matrix.Invert());
            
            for (int i = 0; i < 6; i++)
            {
                Float4 planeVec = new Float4(Planes[i].Normal, Planes[i].D);
                Float4 transformedPlaneVec = invTranspose * planeVec;
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
                float length = Float3.Length(Planes[i].Normal);
                if (length > float.Epsilon)
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
        public int ClassifyPoint(Float3 point)
        {
            int planesOutside = 0;
            for (int i = 0; i < 6; i++)
            {
                if (Planes[i].GetSignedDistanceToPoint(point) < -float.Epsilon)
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
                float distance = Planes[i].GetSignedDistanceToPoint(sphere.Center);
                if (distance < -sphere.Radius - float.Epsilon)
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
                if (Float3.LengthSquared(Planes[i].Normal) < float.Epsilon * float.Epsilon)
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
        public static Frustum FromCamera(Float3 position, Float3 forward, Float3 up, 
                                               float fovY, float aspect, float nearDist, float farDist)
        {
            Float3 right = Float3.Normalize(Float3.Cross(forward, up));
            Float3 actualUp = Float3.Cross(right, forward);
            
            float halfFovY = fovY / 2.0f;
            float tanHalfFovY = Maths.Tan(halfFovY);
            float tanHalfFovX = tanHalfFovY * aspect;
            
            // Calculate plane normals (pointing inward)
            Float3 nearCenter = position + forward * nearDist;
            Float3 farCenter = position + forward * farDist;
            
            // Near and far planes
            Plane near = new Plane(forward, Float3.Dot(forward, nearCenter));
            Plane far = new Plane(-forward, Float3.Dot(-forward, farCenter));
            
            // Side planes
            Float3 leftNormal = Float3.Normalize(Float3.Cross(actualUp, forward + right * tanHalfFovX));
            Float3 rightNormal = Float3.Normalize(Float3.Cross(forward - right * tanHalfFovX, actualUp));
            Float3 topNormal = Float3.Normalize(Float3.Cross(forward + actualUp * tanHalfFovY, right));
            Float3 bottomNormal = Float3.Normalize(Float3.Cross(right, forward - actualUp * tanHalfFovY));
            
            Plane p_left = new Plane(leftNormal, Float3.Dot(leftNormal, position));
            Plane p_right = new Plane(rightNormal, Float3.Dot(rightNormal, position));
            Plane p_top = new Plane(topNormal, Float3.Dot(topNormal, position));
            Plane p_bottom = new Plane(bottomNormal, Float3.Dot(bottomNormal, position));
            
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
        public static Frustum CreateOrthographic(float left, float right, float bottom, 
                                                        float top, float nearDist, float farDist)
        {
            var planes = new Plane[6];
            
            // Near and far planes (assuming Z forward)
            planes[0] = new Plane(new Float3(0.0f, 0.0f, 1.0f), nearDist);
            planes[1] = new Plane(new Float3(0.0f, 0.0f, -1.0f), -farDist);
            
            // Side planes
            planes[2] = new Plane(new Float3(1.0f, 0.0f, 0.0f), -left);    // Left
            planes[3] = new Plane(new Float3(-1.0f, 0.0f, 0.0f), right);   // Right
            planes[4] = new Plane(new Float3(0.0f, -1.0f, 0.0f), top);     // Top
            planes[5] = new Plane(new Float3(0.0f, 1.0f, 0.0f), -bottom);  // Bottom
            
            return new Frustum(planes);
        }
        
        /// <summary>
        /// Expands the frustum by a given amount along all plane normals.
        /// </summary>
        /// <param name="amount">Amount to expand by.</param>
        public void Expand(float amount)
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
        public Frustum Expanded(float amount)
        {
            var result = this;
            result.Expand(amount);
            return result;
        }

        /// <summary>
        /// Computes the 8 corner points of the frustum by intersecting planes.
        /// </summary>
        /// <returns>Array of 8 corner points.</returns>
        public Float3[] GetCorners()
        {
            if (Planes == null || Planes.Length != 6)
                return new Float3[0];

            var corners = new Float3[8];

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
        private static Float3 IntersectThreePlanes(Plane p1, Plane p2, Plane p3)
        {
            // Using Cramer's rule to solve the system:
            // n1·p = d1
            // n2·p = d2
            // n3·p = d3

            Float3 n1 = p1.Normal;
            Float3 n2 = p2.Normal;
            Float3 n3 = p3.Normal;

            float d1 = p1.D;
            float d2 = p2.D;
            float d3 = p3.D;

            // Calculate determinant
            Float3 cross = Float3.Cross(n2, n3);
            float det = Float3.Dot(n1, cross);

            // If determinant is near zero, planes don't intersect at a unique point
            if (Maths.Abs(det) < 1e-10)
                return Float3.Zero; // Fallback for degenerate case

            // Calculate intersection point
            Float3 result = (d1 * cross +
                            Float3.Cross(n1, d3 * n2 - d2 * n3)) / det;

            return result;
        }

        // --- IBoundingShape Implementation ---

        /// <summary>
        /// Returns the corner of the frustum that is farthest in the given direction.
        /// </summary>
        /// <param name="direction">The direction to search in.</param>
        /// <returns>The farthest corner in the given direction.</returns>
        public Float3 SupportMap(Float3 direction)
        {
            Float3[] corners = GetCorners();

            if (corners.Length == 0)
                return Float3.Zero;

            float maxDot = Float3.Dot(corners[0], direction);
            int maxIndex = 0;

            for (int i = 1; i < corners.Length; i++)
            {
                float dot = Float3.Dot(corners[i], direction);
                if (dot > maxDot)
                {
                    maxDot = dot;
                    maxIndex = i;
                }
            }

            return corners[maxIndex];
        }

        /// <summary>
        /// Generates geometry data for this frustum as a BMesh-like structure.
        /// </summary>
        /// <param name="resolution">Unused for Frustum (topology is fixed).</param>
        /// <returns>GeometryData containing vertices, edges, and quad faces for the frustum.</returns>
        public GeometryData GetGeometryData(int resolution = 16)
        {
            var geometryData = new GeometryData();
            Float3[] corners = GetCorners();

            if (corners == null || corners.Length != 8)
                return geometryData;

            // Corner indices from GetCorners():
            // 0: Near-Left-Bottom,  1: Near-Right-Bottom,  2: Near-Left-Top,  3: Near-Right-Top
            // 4: Far-Left-Bottom,   5: Far-Right-Bottom,   6: Far-Left-Top,   7: Far-Right-Top

            // Add 8 vertices
            var verts = new GeometryData.Vertex[8];
            for (int i = 0; i < 8; i++)
            {
                verts[i] = geometryData.AddVertex(corners[i]);
            }

            // Add 6 quad faces for the frustum
            // Near plane (looking from inside the frustum)
            geometryData.AddFace(verts[0], verts[1], verts[3], verts[2]);
            // Far plane
            geometryData.AddFace(verts[4], verts[6], verts[7], verts[5]);
            // Bottom plane
            geometryData.AddFace(verts[0], verts[4], verts[5], verts[1]);
            // Top plane
            geometryData.AddFace(verts[2], verts[3], verts[7], verts[6]);
            // Left plane
            geometryData.AddFace(verts[0], verts[2], verts[6], verts[4]);
            // Right plane
            geometryData.AddFace(verts[1], verts[5], verts[7], verts[3]);

            return geometryData;
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
