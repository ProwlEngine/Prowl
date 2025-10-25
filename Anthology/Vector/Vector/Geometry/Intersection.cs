// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Contains static methods for low-level 3D intersection, containment, and closest point tests
    /// All direction vectors (ray directions, plane normals) are assumed to be normalized unless specified.
    /// </summary>
    public static class Intersection
    {
        public const double INTERSECTION_EPSILON = 0.00001;

        #region --- Ray Intersection Tests ---

        /// <summary>
        /// Calculates the intersection of a ray with a plane.
        /// </summary>
        /// <returns>True if the ray intersects the plane in the forward direction, false otherwise. Outputs the distance to intersection.</returns>
        public static bool RayPlane(
            Double3 rayOrigin,
            Double3 rayDir, // Assumed normalized
            Double3 planeNormal, // Assumed normalized
            double planeD, // Distance from origin along the normal (Ax + By + Cz = D form)
            out double distance)
        {
            double nd = Double3.Dot(rayDir, planeNormal);
            double pn = Double3.Dot(rayOrigin, planeNormal);

            if (Maths.Abs(nd) < INTERSECTION_EPSILON) // Ray is parallel to the plane
            {
                distance = 0.0;
                return false;
            }

            distance = (planeD - pn) / nd;

            return distance >= 0.0; // Intersection must be in the forward direction of the ray
        }

        /// <summary>
        /// Calculates the intersection of a ray with a triangle (Moller-Trumbore algorithm).
        /// Outputs distance along the ray and barycentric coordinates (u,v). w = 1-u-v.
        /// </summary>
        /// <returns>True if the ray intersects the triangle (front or back face), false otherwise.</returns>
        public static bool RayTriangle(
            Double3 rayOrigin,
            Double3 rayDir, // Assumed normalized
            Double3 v0, Double3 v1, Double3 v2,
            out double distance, out double u, out double v)
        {
            distance = 0.0; u = 0.0; v = 0.0;

            Double3 edge1 = v1 - v0;
            Double3 edge2 = v2 - v0;

            Double3 pvec = Double3.Cross(rayDir, edge2);
            double det = Double3.Dot(edge1, pvec);

            if (Maths.Abs(det) < INTERSECTION_EPSILON) // Ray is parallel to triangle plane or backface culling if det < Epsilon
                return false;

            double invDet = 1.0 / det;

            Double3 tvec = rayOrigin - v0;
            u = Double3.Dot(tvec, pvec) * invDet;
            if (u < 0.0 - INTERSECTION_EPSILON || u > 1.0 + INTERSECTION_EPSILON)
                return false;

            Double3 qvec = Double3.Cross(tvec, edge1);
            v = Double3.Dot(rayDir, qvec) * invDet;
            if (v < 0.0 - INTERSECTION_EPSILON || u + v > 1.0 + INTERSECTION_EPSILON)
                return false;

            distance = Double3.Dot(edge2, qvec) * invDet;

            return distance >= INTERSECTION_EPSILON; // Intersection must be in the forward direction
        }

        /// <summary>
        /// Calculates intersection of a ray with an Axis-Aligned Bounding Box (AABB).
        /// Uses the slabs method.
        /// </summary>
        /// <returns>True if intersection occurs. out tMin is entry distance, out tMax is exit distance.</returns>
        public static bool RayAABB(
            Double3 rayOrigin,
            Double3 rayDir, // Does not need to be normalized
            Double3 boxMin,
            Double3 boxMax,
            out double tMin, out double tMax)
        {
            tMin = 0.0;
            tMax = double.MaxValue;

            // X slab
            if (Maths.Abs(rayDir.X) < INTERSECTION_EPSILON)
            {
                if (rayOrigin.X < boxMin.X || rayOrigin.X > boxMax.X) { return false; }
            }
            else
            {
                double invDirX = 1.0 / rayDir.X;
                double t1x = (boxMin.X - rayOrigin.X) * invDirX;
                double t2x = (boxMax.X - rayOrigin.X) * invDirX;
                if (t1x > t2x) {
                    (t2x, t1x) = (t1x, t2x);
                }
                tMin = Maths.Max(tMin, t1x);
                tMax = Maths.Min(tMax, t2x);
                if (tMin > tMax) { return false; }
            }

            // Y slab
            if (Maths.Abs(rayDir.Y) < INTERSECTION_EPSILON)
            {
                if (rayOrigin.Y < boxMin.Y || rayOrigin.Y > boxMax.Y) { return false; }
            }
            else
            {
                double invDirY = 1.0 / rayDir.Y;
                double t1y = (boxMin.Y - rayOrigin.Y) * invDirY;
                double t2y = (boxMax.Y - rayOrigin.Y) * invDirY;
                if (t1y > t2y) {
                    (t2y, t1y) = (t1y, t2y);
                }
                tMin = Maths.Max(tMin, t1y);
                tMax = Maths.Min(tMax, t2y);
                if (tMin > tMax) { return false; }
            }

            // Z slab
            if (Maths.Abs(rayDir.Z) < INTERSECTION_EPSILON)
            {
                if (rayOrigin.Z < boxMin.Z || rayOrigin.Z > boxMax.Z) { return false; }
            }
            else
            {
                double invDirZ = 1.0 / rayDir.Z;
                double t1z = (boxMin.Z - rayOrigin.Z) * invDirZ;
                double t2z = (boxMax.Z - rayOrigin.Z) * invDirZ;
                if (t1z > t2z) {
                    (t2z, t1z) = (t1z, t2z);
                }
                tMin = Maths.Max(tMin, t1z);
                tMax = Maths.Min(tMax, t2z);
                if (tMin > tMax) { return false; }
            }

            return tMax >= 0.0 && tMin <= tMax;
        }

        /// <summary>
        /// Calculates intersection of a ray with a sphere.
        /// </summary>
        /// <returns>True if intersection. out t0 and t1 are distances (t0 <= t1). If only one intersection (tangent) or ray starts inside, t0 may be negative.</returns>
        public static bool RaySphere(
            Double3 rayOrigin,
            Double3 rayDir, // Assumed normalized
            Double3 sphereCenter,
            double sphereRadius,
            out double t0, out double t1)
        {
            t0 = t1 = 0.0;

            Double3 oc = rayOrigin - sphereCenter;

            //double a = Maths.Dot(rayDir, rayDir); // Should be 1.0 if rayDir is normalized
            const double a = 1.0;

            double b = 2.0 * Double3.Dot(oc, rayDir);
            double c = Double3.LengthSquared(oc) - sphereRadius * sphereRadius;

            double discriminant = b * b - 4 * a * c; // a is 1.0

            if (discriminant < 0.0) return false;

            double sqrtDiscriminant = Maths.Sqrt(discriminant);
            // Denominator is 2*a, which is 2.0 since a=1.0
            t0 = (-b - sqrtDiscriminant) / 2.0;
            t1 = (-b + sqrtDiscriminant) / 2.0;

            if (t0 > t1) {
                (t1, t0) = (t0, t1);
            }

            return true;
        }

        /// <summary>
        /// Calculates intersection of a ray with an infinite cylinder defined by an axis, radius, and a point on the axis.
        /// </summary>
        /// <returns>True if intersection. out t0 and t1 are distances along the ray (t0 <= t1).</returns>
        public static bool RayCylinderInfinite(
            Double3 rayOrigin, Double3 rayDir, // rayDir assumed normalized
            Double3 cylinderAxisPoint, Double3 cylinderAxisDir, // cylinderAxisDir assumed normalized
            double cylinderRadius,
            out double t0, out double t1)
        {
            t0 = t1 = 0.0;
            Double3 oc = rayOrigin - cylinderAxisPoint;

            double card = Double3.Dot(cylinderAxisDir, rayDir);
            double caoc = Double3.Dot(cylinderAxisDir, oc);

            double a = 1.0 - card * card; // Since rayDir and cylinderAxisDir are normalized, Dot(rayDir,rayDir) = 1
            double b = 2.0 * (Double3.Dot(oc, rayDir) - caoc * card);
            double c = Double3.Dot(oc, oc) - caoc * caoc - cylinderRadius * cylinderRadius;

            if (Maths.Abs(a) < INTERSECTION_EPSILON) // Ray is parallel to cylinder axis
            {
                // Check if ray origin is inside the cylinder's radius projected onto the plane
                // perpendicular to the axis passing through cylinderAxisPoint.
                // Distance_sq(rayOrigin to line) = Dot(oc, oc) - caoc*caoc
                if (c > 0.0) return false; // Ray is outside and parallel
                // Ray is inside or on the surface and parallel
                t0 = double.NegativeInfinity;
                t1 = double.PositiveInfinity;
                return true;
            }

            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0.0) return false;

            double sqrtDiscriminant = Maths.Sqrt(discriminant);
            t0 = (-b - sqrtDiscriminant) / (2.0 * a);
            t1 = (-b + sqrtDiscriminant) / (2.0 * a);

            if (t0 > t1) { double temp = t0; t0 = t1; t1 = temp; }
            return true;
        }

        /// <summary>
        /// Calculates intersection of a ray with a capped cylinder.
        /// </summary>
        /// <returns>True if intersection. out distance is the closest valid intersection distance.</returns>
        public static bool RayCylinderCapped(
            Double3 rayOrigin, Double3 rayDir, // rayDir assumed normalized
            Double3 capA_Center, Double3 capB_Center, // Centers of the two end caps
            double radius,
            out double distance)
        {
            distance = double.MaxValue;
            bool intersected = false;

            Double3 cylinderAxisDir = capB_Center - capA_Center;
            double heightSq = Double3.LengthSquared(cylinderAxisDir);
            if (heightSq < INTERSECTION_EPSILON * INTERSECTION_EPSILON)
            {
                double t0, t1;
                if (RaySphere(rayOrigin, rayDir, capA_Center, radius, out t0, out t1))
                {
                    if (t0 >= 0.0 && t0 < distance) { distance = t0; intersected = true; }
                    else if (t1 >= 0.0 && t1 < distance) { distance = t1; intersected = true; }
                }
                return intersected;
            }
            cylinderAxisDir = cylinderAxisDir / Maths.Sqrt(heightSq); // Normalize

            double t0_inf, t1_inf;
            if (RayCylinderInfinite(rayOrigin, rayDir, capA_Center, cylinderAxisDir, radius, out t0_inf, out t1_inf))
            {
                double height = Maths.Sqrt(heightSq);
                if (t0_inf >= 0.0)
                {
                    Double3 p0 = rayOrigin + t0_inf * rayDir;
                    double proj0 = Double3.Dot(p0 - capA_Center, cylinderAxisDir);
                    if (proj0 >= -INTERSECTION_EPSILON && proj0 <= height + INTERSECTION_EPSILON)
                    {
                        if (t0_inf < distance) { distance = t0_inf; intersected = true; }
                    }
                }
                if (t1_inf >= 0.0)
                {
                    Double3 p1 = rayOrigin + t1_inf * rayDir;
                    double proj1 = Double3.Dot(p1 - capA_Center, cylinderAxisDir);
                    if (proj1 >= -INTERSECTION_EPSILON && proj1 <= height + INTERSECTION_EPSILON)
                    {
                        if (t1_inf < distance) { distance = t1_inf; intersected = true; }
                    }
                }
            }

            double capDist;
            if (RayPlane(rayOrigin, rayDir, -cylinderAxisDir, Double3.Dot(-cylinderAxisDir, capA_Center), out capDist))
            {
                if (capDist >= 0.0 && capDist < distance)
                {
                    Double3 p_capA = rayOrigin + capDist * rayDir;
                    if (Double3.LengthSquared(p_capA - capA_Center) <= radius * radius + INTERSECTION_EPSILON)
                    {
                        distance = capDist;
                        intersected = true;
                    }
                }
            }
            if (RayPlane(rayOrigin, rayDir, cylinderAxisDir, Double3.Dot(cylinderAxisDir, capB_Center), out capDist))
            {
                if (capDist >= 0.0 && capDist < distance)
                {
                    Double3 p_capB = rayOrigin + capDist * rayDir;
                    if (Double3.LengthSquared(p_capB - capB_Center) <= radius * radius + INTERSECTION_EPSILON)
                    {
                        distance = capDist;
                        intersected = true;
                    }
                }
            }
            return intersected;
        }

        #endregion

        #region --- Closest Point / Distance Tests ---

        /// <summary>
        /// Calculates the signed distance from a point to a plane.
        /// Distance is positive if point is on the side of the normal, negative otherwise.
        /// </summary>
        public static double SignedDistancePointToPlane(
            Double3 point,
            Double3 planeNormal, // Assumed normalized
            double planeD) // Ax + By + Cz = D form (D is dot(Normal, PointOnPlane))
        {
            return Double3.Dot(planeNormal, point) - planeD;
        }

        /// <summary>
        /// Calculates the closest point on a plane to a given point.
        /// </summary>
        public static void ClosestPointOnPlaneToPoint(
            Double3 point,
            Double3 planeNormal, // Assumed normalized
            double planeD,
            out Double3 closestPoint)
        {
            double signedDist = SignedDistancePointToPlane(point, planeNormal, planeD);
            closestPoint = point - signedDist * planeNormal;
        }

        /// <summary>
        /// Calculates the closest point on an infinite line (defined by two points) to a given point.
        /// </summary>
        public static void ClosestPointOnLineToPoint(
            Double3 lineA, Double3 lineB,
            Double3 point,
            out Double3 closestPoint)
        {
            Double3 ab = lineB - lineA;
            Double3 ap = point - lineA;

            double dot_ab_ap = Double3.Dot(ab, ap);
            double dot_ab_ab = Double3.LengthSquared(ab);

            double t = 0.0;
            if (dot_ab_ab > INTERSECTION_EPSILON)
                t = dot_ab_ap / dot_ab_ab;

            closestPoint = lineA + t * ab;
        }

        /// <summary>
        /// Calculates the closest point on a line segment to a given point.
        /// </summary>
        public static void ClosestPointOnLineSegmentToPoint(
            Double3 segA, Double3 segB,
            Double3 point,
            out Double3 closestPoint)
        {
            Double3 ab = segB - segA;
            Double3 ap = point - segA;

            double dot_ab_ap = Double3.Dot(ab, ap);

            if (dot_ab_ap <= 0.0)
            {
                closestPoint = segA;
                return;
            }

            double dot_ab_ab = Double3.LengthSquared(ab);

            if (dot_ab_ap >= dot_ab_ab)
            {
                closestPoint = segB;
                return;
            }

            double t = 0.0;
            if (dot_ab_ab > INTERSECTION_EPSILON)
                t = dot_ab_ap / dot_ab_ab;

            closestPoint = segA + t * ab;
        }

        /// <summary>
        /// Calculates the square of the distance from a point to a line segment.
        /// </summary>
        public static double DistanceSqPointToLineSegment(
            Double3 segA, Double3 segB,
            Double3 point)
        {
            ClosestPointOnLineSegmentToPoint(segA, segB, point, out Double3 closestPoint);
            Double3 diff = point - closestPoint;
            return Double3.LengthSquared(diff);
        }

        /// <summary>
        /// Calculates the closest point on a triangle to a given point.
        /// </summary>
        public static void ClosestPointOnTriangleToPoint(
            Double3 point,
            Double3 v0, Double3 v1, Double3 v2,
            out Double3 result)
        {
            Double3 ab = v1 - v0;
            Double3 ac = v2 - v0;
            Double3 ap = point - v0;

            double d1 = Double3.Dot(ab, ap);
            double d2 = Double3.Dot(ac, ap);
            if (d1 <= 0.0 && d2 <= 0.0) { result = v0; return; }

            Double3 bp = point - v1;
            double d3 = Double3.Dot(ab, bp);
            double d4 = Double3.Dot(ac, bp);
            if (d3 >= 0.0 && d4 <= d3) { result = v1; return; }

            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0)
            {
                double v_param = (Maths.Abs(d1 - d3) < INTERSECTION_EPSILON) ? 0.0 : d1 / (d1 - d3);
                result = v0 + v_param * ab;
                return;
            }

            Double3 cp = point - v2;
            double d5 = Double3.Dot(ab, cp);
            double d6 = Double3.Dot(ac, cp);
            if (d6 >= 0.0 && d5 <= d6) { result = v2; return; }

            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0)
            {
                double w_param = (Maths.Abs(d2 - d6) < INTERSECTION_EPSILON) ? 0.0 : d2 / (d2 - d6);
                result = v0 + w_param * ac;
                return;
            }

            double va = d3 * d6 - d5 * d4;
            if (va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0)
            {
                double denom_bc = (d4 - d3) + (d5 - d6);
                double w_param = (Maths.Abs(denom_bc) < INTERSECTION_EPSILON) ? 0.0 : (d4 - d3) / denom_bc;
                result = v1 + w_param * (v2 - v1);
                return;
            }

            double denom_bary = va + vb + vc;
            if (Maths.Abs(denom_bary) < INTERSECTION_EPSILON) { result = v0; return; }
            double v_bary = vb / denom_bary;
            double w_bary = vc / denom_bary;
            result = v0 + ab * v_bary + ac * w_bary;
        }

        /// <summary>
        /// Calculates the closest point on an AABB to a given point.
        /// </summary>
        public static void ClosestPointOnAABBToPoint(
            Double3 point,
            Double3 boxMin, Double3 boxMax,
            out Double3 closestPoint)
        {
            // Assuming Maths.Clamp(value, min, max) exists for scalars
            double x = Maths.Clamp(point.X, boxMin.X, boxMax.X);
            double y = Maths.Clamp(point.Y, boxMin.Y, boxMax.Y);
            double z = Maths.Clamp(point.Z, boxMin.Z, boxMax.Z);
            closestPoint = new Double3(x, y, z);
        }

        /// <summary>
        /// Calculates the closest point on the surface of a sphere to a given point.
        /// If the point is at the center of the sphere, returns a point on the surface (e.g., center + radius on X axis).
        /// </summary>
        public static void ClosestPointOnSphereToPoint(
            Double3 point,
            Double3 sphereCenter,
            double sphereRadius,
            out Double3 closestPoint)
        {
            Double3 dir = point - sphereCenter;
            double distSq = Double3.LengthSquared(dir);

            if (distSq < INTERSECTION_EPSILON * INTERSECTION_EPSILON)
            {
                closestPoint = sphereCenter + new Double3(sphereRadius, 0.0, 0.0);
                return;
            }

            // dir.Normalize() * sphereRadius + sphereCenter
            closestPoint = sphereCenter + (dir / Maths.Sqrt(distSq)) * sphereRadius;
        }

        /// <summary>
        /// Computes the closest points on two line segments.
        /// Outputs the points c1 on segment p1-q1 and c2 on segment p2-q2,
        /// and parameters s and t for these points along their respective segments.
        /// </summary>
        public static void ClosestPointsLineSegmentLineSegment(
            Double3 p1, Double3 q1, // Segment 1
            Double3 p2, Double3 q2, // Segment 2
            out Double3 c1, out Double3 c2,
            out double s, out double t)
        {
            Double3 d1 = q1 - p1; // Direction vector of segment S1
            Double3 d2 = q2 - p2; // Direction vector of segment S2
            Double3 r = p1 - p2;  // Vector between segment starts

            double a = Double3.Dot(d1, d1); // Squared length of segment S1
            double e = Double3.Dot(d2, d2); // Squared length of segment S2
            double f = Double3.Dot(d2, r);

            // Check if either or both segments are points
            if (a <= INTERSECTION_EPSILON && e <= INTERSECTION_EPSILON) // Both segments are points
            {
                s = t = 0.0;
                c1 = p1;
                c2 = p2;
                return;
            }
            if (a <= INTERSECTION_EPSILON) // First segment is a point
            {
                s = 0.0;
                t = Maths.Clamp(f / e, 0.0, 1.0); // Clamp t to 0..1
            }
            else
            {
                double c_val = Double3.Dot(d1, r);
                if (e <= INTERSECTION_EPSILON) // Second segment is a point
                {
                    t = 0.0;
                    s = Maths.Clamp(-c_val / a, 0.0, 1.0); // Clamp s to 0..1
                }
                else // General case
                {
                    double b = Double3.Dot(d1, d2);
                    double denom = a * e - b * b; // Denominator

                    // If segments are parallel, handle specially
                    if (denom <= INTERSECTION_EPSILON)
                    {
                        s = 0.0; // Arbitrarily pick s=0
                        t = (b > e) ? f / b : f / e; // Simplified handling for parallel lines
                    }
                    else
                    {
                        s = (b * f - c_val * e) / denom;
                        t = (a * f - b * c_val) / denom;
                    }

                    // Clamp parameters to the segment lengths [0,1]
                    s = Maths.Clamp(s, 0.0, 1.0);
                    t = Maths.Clamp(t, 0.0, 1.0);
                }
            }

            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }

        /// <summary>
        /// Computes the square of the shortest distance between two line segments.
        /// </summary>
        public static double DistanceSqSegmentSegment(Double3 p1, Double3 q1, Double3 p2, Double3 q2)
        {
            Double3 c1, c2;
            double s, t;
            ClosestPointsLineSegmentLineSegment(p1, q1, p2, q2, out c1, out c2, out s, out t);
            return Double3.LengthSquared(c1 - c2);
        }

        #endregion

        #region --- Shape vs. Shape Overlap/Intersection Tests (Boolean) ---

        /// <summary>
        /// Checks if two spheres overlap or touch.
        /// </summary>
        public static bool SphereSphereOverlap(
            Double3 centerA, double radiusA,
            Double3 centerB, double radiusB)
        {
            double distSq = Double3.LengthSquared(centerA - centerB);
            double sumRadii = radiusA + radiusB;
            return distSq <= sumRadii * sumRadii;
        }

        /// <summary>
        /// Tests if two 2D line segments intersect and returns the intersection point and parameter t.
        /// </summary>
        /// <param name="p1">Start point of first segment</param>
        /// <param name="p2">End point of first segment</param>
        /// <param name="p3">Start point of second segment</param>
        /// <param name="p4">End point of second segment</param>
        /// <param name="intersection">The intersection point if segments intersect</param>
        /// <param name="t">Parameter t along first segment (0 to 1) where intersection occurs</param>
        /// <returns>True if segments intersect, false otherwise</returns>
        public static bool SegmentSegment2DOverlap(Double2 p1, Double2 p2, Double2 p3, Double2 p4, out Double2 intersection, out double t)
        {
            intersection = Double2.Zero;
            t = 0;

            double x1 = p1.X, y1 = p1.Y;
            double x2 = p2.X, y2 = p2.Y;
            double x3 = p3.X, y3 = p3.Y;
            double x4 = p4.X, y4 = p4.Y;

            // Calculate the denominator
            double denom = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1);

            // If denominator is zero, lines are parallel
            if (Maths.Abs(denom) < INTERSECTION_EPSILON)
            {
                return false;
            }

            // Calculate parameter for first line segment
            double ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denom;

            // Calculate parameter for second line segment
            double ub = ((x2 - x1) * (y1 - y3) - (y2 - y1) * (x1 - x3)) / denom;

            // Check if intersection point lies on both segments
            if (ua >= 0.0 && ua <= 1.0 && ub >= 0.0 && ub <= 1.0)
            {
                // Calculate intersection point
                intersection = new Double2(
                    x1 + ua * (x2 - x1),
                    y1 + ua * (y2 - y1)
                );
                t = ua;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if two AABBs overlap or touch.
        /// </summary>
        public static bool AABBAABBOverlap(
            Double3 minA, Double3 maxA,
            Double3 minB, Double3 maxB)
        {
            if (maxA.X < minB.X || minA.X > maxB.X) return false;
            if (maxA.Y < minB.Y || minA.Y > maxB.Y) return false;
            if (maxA.Z < minB.Z || minA.Z > maxB.Z) return false;
            return true;
        }

        /// <summary>
        /// Checks if a sphere and an AABB overlap or touch.
        /// </summary>
        public static bool SphereAABBOverlap(
            Double3 sphereCenter, double sphereRadius,
            Double3 boxMin, Double3 boxMax)
        {
            ClosestPointOnAABBToPoint(sphereCenter, boxMin, boxMax, out Double3 closestPointOnBox);
            double distSq = Double3.LengthSquared(sphereCenter - closestPointOnBox);
            return distSq <= sphereRadius * sphereRadius;
        }

        /// <summary>
        /// Checks if a triangle and a sphere overlap using GJK.
        /// </summary>
        public static bool TriangleSphereOverlap(
            Double3 v0, Double3 v1, Double3 v2,
            Double3 sphereCenter, double sphereRadius)
        {
            Triangle tri = new Triangle(v0, v1, v2);
            Sphere sphere = new Sphere(sphereCenter, sphereRadius);
            return GJK.Intersects(tri, sphere);
        }

        /// <summary>
        /// Checks if a triangle and an AABB overlap using GJK.
        /// </summary>
        public static bool TriangleAABBOverlap(
            Double3 v0, Double3 v1, Double3 v2,
            Double3 boxMin, Double3 boxMax)
        {
            Triangle tri = new Triangle(v0, v1, v2);
            AABB aabb = new AABB(boxMin, boxMax);
            return GJK.Intersects(tri, aabb);
        }

        /// <summary>
        /// Checks if a line segment and a sphere overlap using GJK.
        /// </summary>
        public static bool LineSegmentSphereOverlap(
            Double3 segStart, Double3 segEnd,
            Double3 sphereCenter, double sphereRadius)
        {
            LineSegment seg = new LineSegment(segStart, segEnd);
            Sphere sphere = new Sphere(sphereCenter, sphereRadius);
            return GJK.Intersects(seg, sphere);
        }

        /// <summary>
        /// Checks if a line segment and an AABB overlap using GJK.
        /// </summary>
        public static bool LineSegmentAABBOverlap(
            Double3 segStart, Double3 segEnd,
            Double3 boxMin, Double3 boxMax)
        {
            LineSegment seg = new LineSegment(segStart, segEnd);
            AABB aabb = new AABB(boxMin, boxMax);
            return GJK.Intersects(seg, aabb);
        }

        /// <summary>
        /// Checks if a line segment and a triangle overlap using GJK.
        /// </summary>
        public static bool LineSegmentTriangleOverlap(
            Double3 segStart, Double3 segEnd,
            Double3 v0, Double3 v1, Double3 v2)
        {
            LineSegment seg = new LineSegment(segStart, segEnd);
            Triangle tri = new Triangle(v0, v1, v2);
            return GJK.Intersects(seg, tri);
        }

        /// <summary>
        /// Checks if two line segments overlap using GJK.
        /// </summary>
        public static bool LineSegmentLineSegmentOverlap(
            Double3 seg1Start, Double3 seg1End,
            Double3 seg2Start, Double3 seg2End)
        {
            LineSegment seg1 = new LineSegment(seg1Start, seg1End);
            LineSegment seg2 = new LineSegment(seg2Start, seg2End);
            return GJK.Intersects(seg1, seg2);
        }

        /// <summary>
        /// Checks if a frustum and a triangle overlap using GJK.
        /// </summary>
        public static bool FrustumTriangleOverlap(Frustum frustum, Double3 v0, Double3 v1, Double3 v2)
        {
            Triangle tri = new Triangle(v0, v1, v2);
            return GJK.Intersects(frustum, tri);
        }

        /// <summary>
        /// Checks if a frustum and a line segment overlap using GJK.
        /// </summary>
        public static bool FrustumLineSegmentOverlap(Frustum frustum, Double3 segStart, Double3 segEnd)
        {
            LineSegment seg = new LineSegment(segStart, segEnd);
            return GJK.Intersects(frustum, seg);
        }

        /// <summary>
        /// Checks if two frustums overlap using GJK.
        /// </summary>
        public static bool FrustumFrustumOverlap(Frustum frustumA, Frustum frustumB)
        {
            return GJK.Intersects(frustumA, frustumB);
        }

        /// <summary>
        /// Checks if a cone and a sphere overlap using GJK.
        /// </summary>
        public static bool ConeSphereOverlap(Cone cone, Double3 sphereCenter, double sphereRadius)
        {
            Sphere sphere = new Sphere(sphereCenter, sphereRadius);
            return GJK.Intersects(cone, sphere);
        }

        /// <summary>
        /// Checks if a cone and an AABB overlap using GJK.
        /// </summary>
        public static bool ConeAABBOverlap(Cone cone, Double3 boxMin, Double3 boxMax)
        {
            AABB aabb = new AABB(boxMin, boxMax);
            return GJK.Intersects(cone, aabb);
        }

        /// <summary>
        /// Checks if a cone and a triangle overlap using GJK.
        /// </summary>
        public static bool ConeTriangleOverlap(Cone cone, Double3 v0, Double3 v1, Double3 v2)
        {
            Triangle tri = new Triangle(v0, v1, v2);
            return GJK.Intersects(cone, tri);
        }

        /// <summary>
        /// Checks if a cone and a line segment overlap using GJK.
        /// </summary>
        public static bool ConeLineSegmentOverlap(Cone cone, Double3 segStart, Double3 segEnd)
        {
            LineSegment seg = new LineSegment(segStart, segEnd);
            return GJK.Intersects(cone, seg);
        }

        /// <summary>
        /// Checks if a cone and a frustum overlap using GJK.
        /// </summary>
        public static bool ConeFrustumOverlap(Cone cone, Frustum frustum)
        {
            return GJK.Intersects(cone, frustum);
        }

        /// <summary>
        /// Checks if two cones overlap using GJK.
        /// </summary>
        public static bool ConeConeOverlap(Cone coneA, Cone coneB)
        {
            return GJK.Intersects(coneA, coneB);
        }

        /// <summary>
        /// Checks if a line segment and a plane overlap or touch, If so outputs the intersection point.
        /// </summary>
        /// <returns>True if the segment intersects the plane, false otherwise.</returns>
        public static bool LineSegmentPlane(
            Double3 segA, Double3 segB,
            Double3 planeNormal, double planeD, // Assumed planeNormal is normalized
            out Double3 intersectionPoint)
        {
            intersectionPoint = Double3.Zero;
            Double3 ab = segB - segA;
            double ab_dot_n = Double3.Dot(ab, planeNormal);

            if (Maths.Abs(ab_dot_n) < INTERSECTION_EPSILON) // Segment is parallel to plane
            {
                // Check if segment start point is on the plane (coplanar)
                if (Maths.Abs(SignedDistancePointToPlane(segA, planeNormal, planeD)) < INTERSECTION_EPSILON)
                {
                    // Segment is coplanar with the plane.
                    // This Could be considered an intersection.
                    // But for now let's just say it doesn't produce an intersection point.
                    return false;
                }
                return false;
            }

            double t = (planeD - Double3.Dot(segA, planeNormal)) / ab_dot_n;

            if (t >= -INTERSECTION_EPSILON && t <= 1.0 + INTERSECTION_EPSILON) // Intersection point lies on the segment
            {
                intersectionPoint = segA + t * ab;
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Tests if two triangles intersect in 3D space.
        /// Uses the separating axis theorem with cross products of triangle edges.
        /// </summary>
        /// <param name="a0">First triangle vertex 0.</param>
        /// <param name="a1">First triangle vertex 1.</param>
        /// <param name="a2">First triangle vertex 2.</param>
        /// <param name="b0">Second triangle vertex 0.</param>
        /// <param name="b1">Second triangle vertex 1.</param>
        /// <param name="b2">Second triangle vertex 2.</param>
        /// <returns>True if the triangles intersect or touch.</returns>
        public static bool TriangleTriangle(Double3 a0, Double3 a1, Double3 a2, Double3 b0, Double3 b1, Double3 b2)
        {
            const double eps = INTERSECTION_EPSILON;

            // Quick AABB test first (super cheap early-out)
            double minAx = Maths.Min(a0.X, Maths.Min(a1.X, a2.X));
            double maxAx = Maths.Max(a0.X, Maths.Max(a1.X, a2.X));
            double minBx = Maths.Min(b0.X, Maths.Min(b1.X, b2.X));
            double maxBx = Maths.Max(b0.X, Maths.Max(b1.X, b2.X));
            if (maxAx < minBx - eps || maxBx < minAx - eps) return false;

            // Compute normals (cheap early-out #2)
            Double3 normalA = Double3.Cross(a1 - a0, a2 - a0);
            Double3 normalB = Double3.Cross(b1 - b0, b2 - b0);

            double lenSqA = Double3.LengthSquared(normalA);
            double lenSqB = Double3.LengthSquared(normalB);
            if (lenSqA < eps * eps || lenSqB < eps * eps)
                return false;

            // Normalize in-place
            double invLenA = 1.0 / Math.Sqrt(lenSqA);
            double invLenB = 1.0 / Math.Sqrt(lenSqB);
            normalA *= invLenA;
            normalB *= invLenB;

            // Test triangle normals (inline projection for speed)
            double dA = Double3.Dot(normalA, a0);
            double proj0 = Double3.Dot(normalA, b0);
            double proj1 = Double3.Dot(normalA, b1);
            double proj2 = Double3.Dot(normalA, b2);
            double minB = Maths.Min(proj0, Maths.Min(proj1, proj2));
            double maxB = Maths.Max(proj0, Maths.Max(proj1, proj2));
            if (dA < minB - eps || dA > maxB + eps)
                return false;

            double dB = Double3.Dot(normalB, b0);
            proj0 = Double3.Dot(normalB, a0);
            proj1 = Double3.Dot(normalB, a1);
            proj2 = Double3.Dot(normalB, a2);
            double minA = Maths.Min(proj0, Maths.Min(proj1, proj2));
            double maxA = Maths.Max(proj0, Maths.Max(proj1, proj2));
            if (dB < minA - eps || dB > maxA + eps)
                return false;

            // SAT test on edge cross products (no allocations)
            for (int i = 0; i < 3; i++)
            {
                Double3 edgeA = (i == 0 ? a1 - a0 : i == 1 ? a2 - a1 : a0 - a2);

                for (int j = 0; j < 3; j++)
                {
                    Double3 edgeB = (j == 0 ? b1 - b0 : j == 1 ? b2 - b1 : b0 - b2);
                    Double3 axis = Double3.Cross(edgeA, edgeB);

                    double axisSqLen = Double3.LengthSquared(axis);
                    if (axisSqLen < eps * eps)
                        continue;

                    // Normalize
                    double invLen = 1.0 / Math.Sqrt(axisSqLen);
                    axis *= invLen;

                    // Project A
                    proj0 = Double3.Dot(axis, a0);
                    proj1 = Double3.Dot(axis, a1);
                    proj2 = Double3.Dot(axis, a2);
                    minA = Maths.Min(proj0, Maths.Min(proj1, proj2));
                    maxA = Maths.Max(proj0, Maths.Max(proj1, proj2));

                    // Project B
                    proj0 = Double3.Dot(axis, b0);
                    proj1 = Double3.Dot(axis, b1);
                    proj2 = Double3.Dot(axis, b2);
                    minB = Maths.Min(proj0, Maths.Min(proj1, proj2));
                    maxB = Maths.Max(proj0, Maths.Max(proj1, proj2));

                    // Separation test
                    if (maxA < minB - eps || maxB < minA - eps)
                        return false;
                }
            }

            return true;
        }
        
        /// <summary>
        /// Projects a triangle onto an axis and returns the min/max projection values.
        /// </summary>
        /// <param name="v0">Triangle vertex 0.</param>
        /// <param name="v1">Triangle vertex 1.</param>
        /// <param name="v2">Triangle vertex 2.</param>
        /// <param name="axis">The axis to project onto (assumed normalized).</param>
        /// <param name="min">Minimum projection value.</param>
        /// <param name="max">Maximum projection value.</param>
        private static void ProjectTriangleOntoAxis(Double3 v0, Double3 v1, Double3 v2, Double3 axis, out double min, out double max)
        {
            double p0 = Double3.Dot(v0, axis);
            double p1 = Double3.Dot(v1, axis);
            double p2 = Double3.Dot(v2, axis);
        
            min = Maths.Min(p0, Maths.Min(p1, p2));
            max = Maths.Max(p0, Maths.Max(p1, p2));
        }
        
        #region --- Plane Classification ---
        
        public enum PlaneIntersectionType { Front, Back, Intersecting }
        
        /// <summary>
        /// Classifies a point with respect to a plane.
        /// </summary>
        public static PlaneIntersectionType ClassifyPointToPlane(
             Double3 point,
             Double3 planeNormal, double planeD)
        {
            double dist = SignedDistancePointToPlane(point, planeNormal, planeD);
            if (dist > INTERSECTION_EPSILON) return PlaneIntersectionType.Front;
            if (dist < -INTERSECTION_EPSILON) return PlaneIntersectionType.Back;
            return PlaneIntersectionType.Intersecting;
        }
        
        /// <summary>
        /// Classifies a sphere with respect to a plane.
        /// </summary>
        public static PlaneIntersectionType ClassifySphereToPlane(
            Double3 sphereCenter, double sphereRadius,
            Double3 planeNormal, double planeD)
        {
            double signedDist = SignedDistancePointToPlane(sphereCenter, planeNormal, planeD);
            if (signedDist > sphereRadius) return PlaneIntersectionType.Front;
            if (signedDist < -sphereRadius) return PlaneIntersectionType.Back;
            return PlaneIntersectionType.Intersecting;
        }
        
        /// <summary>
        /// Classifies an AABB with respect to a plane.
        /// </summary>
        public static PlaneIntersectionType ClassifyAABBToPlane(
            Double3 boxMin, Double3 boxMax,
            Double3 planeNormal, double planeD)
        {
            Double3 center = (boxMin + boxMax) * (1.0 / 2.0);
            Double3 extents = (boxMax - boxMin) * (1.0 / 2.0);
        
            double r = extents.X * Maths.Abs(planeNormal.X) +
                      extents.Y * Maths.Abs(planeNormal.Y) +
                      extents.Z * Maths.Abs(planeNormal.Z);
        
            double s = SignedDistancePointToPlane(center, planeNormal, planeD);
        
            if (s > r) return PlaneIntersectionType.Front;
            if (s < -r) return PlaneIntersectionType.Back;
            return PlaneIntersectionType.Intersecting;
        }
        
        #endregion
        
        #region --- Frustum Intersection/Containment Tests ---
        
        // FrustumPlanes should be an array of 6 planes (normals and D values)
        // Order could be: Near, Far, Left, Right, Top, Bottom
        
        /// <summary>
        /// Checks if a point is contained within a frustum defined by 6 planes.
        /// </summary>
        /// <param name="planeNormals">Array of 6 plane normals.</param>
        /// <param name="planeDs">Array of 6 plane D values.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside or on all planes (on the positive/normal side), false otherwise.</returns>
        public static bool FrustumContainsPoint(Double3[] planeNormals, double[] planeDs, Double3 point)
        {
            if (planeNormals == null || planeNormals.Length < 6 || planeDs == null || planeDs.Length < 6)
                throw new ArgumentException("Frustum planes must be provided as 6 normals and 6 D values.");
        
            for (int i = 0; i < 6; i++)
            {
                if (SignedDistancePointToPlane(point, planeNormals[i], planeDs[i]) < -INTERSECTION_EPSILON) // Point is outside (behind) this plane
                {
                    return false;
                }
            }
            return true; // Point is inside or on all planes
        }
        
        /// <summary>
        /// Checks if a sphere intersects or is contained within a frustum.
        /// </summary>
        /// <returns>True if the sphere intersects the frustum, false if it's completely outside.</returns>
        public static bool FrustumIntersectsSphere(Double3[] planeNormals, double[] planeDs, Double3 sphereCenter, double sphereRadius)
        {
            if (planeNormals == null || planeNormals.Length < 6 || planeDs == null || planeDs.Length < 6)
                throw new ArgumentException("Frustum planes must be provided as 6 normals and 6 D values.");
        
            for (int i = 0; i < 6; i++)
            {
                // If sphere is completely behind any plane, it's outside the frustum
                if (SignedDistancePointToPlane(sphereCenter, planeNormals[i], planeDs[i]) < -sphereRadius)
                {
                    return false;
                }
            }
            // If not completely behind any plane, it must be intersecting or inside.
            return true;
        }
        
        /// <summary>
        /// Checks if an AABB intersects or is contained within a frustum.
        /// </summary>
        /// <returns>True if the AABB intersects the frustum, false if it's completely outside.</returns>
        public static bool FrustumIntersectsAABB(Double3[] planeNormals, double[] planeDs, Double3 boxMin, Double3 boxMax)
        {
            if (planeNormals == null || planeNormals.Length < 6 || planeDs == null || planeDs.Length < 6)
                throw new ArgumentException("Frustum planes must be provided as 6 normals and 6 D values.");
        
            for (int i = 0; i < 6; i++)
            {
                // Use the p-vertex/n-vertex test.
                // Find the vertex of the AABB that is "most positive" in the direction of the plane normal (p-vertex)
                // Find the vertex of the AABB that is "most negative" in the direction of the plane normal (n-vertex)
        
                Double3 pVertex = boxMin; // Start with min
                if (planeNormals[i].X >= 0.0) pVertex.X = boxMax.X;
                if (planeNormals[i].Y >= 0.0) pVertex.Y = boxMax.Y;
                if (planeNormals[i].Z >= 0.0) pVertex.Z = boxMax.Z;
        
                // If p-vertex is behind the plane, the entire box is behind (outside)
                if (SignedDistancePointToPlane(pVertex, planeNormals[i], planeDs[i]) < -INTERSECTION_EPSILON)
                {
                    return false;
                }
            }
            // If not completely behind any plane, it must be intersecting or inside.
            return true;
        }
        
        #endregion
        
        #region --- Point in Triangle & Barycentric ---
        
        /// <summary>
        /// Computes the barycentric coordinates of a point with respect to a triangle.
        /// The point is projected onto the triangle's plane first.
        /// </summary>
        /// <param name="point">The point to compute coordinates for.</param>
        /// <param name="v0">Triangle vertex 0.</param>
        /// <param name="v1">Triangle vertex 1.</param>
        /// <param name="v2">Triangle vertex 2.</param>
        /// <param name="u">Barycentric coordinate u (weight for v1).</param>
        /// <param name="v">Barycentric coordinate v (weight for v2).</param>
        /// <remarks>w (weight for v0) = 1 - u - v.</remarks>
        public static void PointTriangleBarycentric(Double3 point, Double3 v0, Double3 v1, Double3 v2, out double u, out double v)
        {
            Double3 edge1 = v1 - v0; // v1 - v0
            Double3 edge2 = v2 - v0; // v2 - v0
            Double3 pv = point - v0;
        
            double d00 = Double3.Dot(edge1, edge1);
            double d01 = Double3.Dot(edge1, edge2);
            double d11 = Double3.Dot(edge2, edge2);
            double d20 = Double3.Dot(pv, edge1);
            double d21 = Double3.Dot(pv, edge2);
        
            double denom = d00 * d11 - d01 * d01;
            if (Maths.Abs(denom) < INTERSECTION_EPSILON) // Triangle is degenerate
            {
                u = 0.0; v = 0.0;
                return;
            }
        
            double invDenom = 1.0 / denom;
            u = (d11 * d20 - d01 * d21) * invDenom;
            v = (d00 * d21 - d01 * d20) * invDenom;
        }
        
        /// <summary>
        /// Checks if a point (defined by its barycentric coordinates u,v) is inside the triangle.
        /// Assumes the point is coplanar with the triangle.
        /// </summary>
        /// <param name="u">Barycentric coordinate u (weight for v1).</param>
        /// <param name="v">Barycentric coordinate v (weight for v2).</param>
        /// <returns>True if the point is inside or on the edge of the triangle.</returns>
        public static bool IsPointInTriangle(double u, double v)
        {
            return (u >= -INTERSECTION_EPSILON) && (v >= -INTERSECTION_EPSILON) && (u + v <= 1.0 + INTERSECTION_EPSILON);
        }
        
        #endregion
        
        #endregion
    }
}
