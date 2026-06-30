using System.Numerics;

namespace Prowl.Drift
{
    public class ShapeSegment : Shape
    {
        public Vector2 A, B;
        public float Radius;
        public Vector2 Normal;

        public Vector2 TransformedA { get; private set; } = Vector2.Zero;
        public Vector2 TransformedB { get; private set; } = Vector2.Zero;
        public Vector2 TransformedNormal { get; private set; } = Vector2.Zero;

        public ShapeSegment(Vector2 a, Vector2 b, float radius) : base(TypeSegment)
        {
            A = a;
            B = b;
            Radius = Math.Abs(radius);
            Normal = Vector2.Normalize(MathUtil.Perp(b - a));
        }

        public override Shape Duplicate() => new ShapeSegment(A, B, Radius);

        public override void Recenter(Vector2 c) { A -= c; B -= c; }

        public override float Area() => Geometry.AreaForSegment(A, B, Radius);

        public override Vector2 Centroid() => Geometry.CentroidForSegment(A, B);

        public override float Inertia(float mass) => Geometry.InertiaForSegment(mass, A, B);

        public override void CacheData(Body body)
        {
            TransformedA = body.TransformPoint(A);
            TransformedB = body.TransformPoint(B);
            TransformedNormal = Vector2.Normalize(MathUtil.Perp(TransformedB - TransformedA));

            float l = MathF.Min(TransformedA.X, TransformedB.X);
            float r = MathF.Max(TransformedA.X, TransformedB.X);
            float b = MathF.Min(TransformedA.Y, TransformedB.Y);
            float t = MathF.Max(TransformedA.Y, TransformedB.Y);

            //Bounds.Mins.Set(l - Radius, b - Radius);
            Bounds.Mins = new Vector2(l - Radius, b - Radius);
            //Bounds.Maxs.Set(r + Radius, t + Radius);
            Bounds.Maxs = new Vector2(r + Radius, t + Radius);
        }

        public override bool PointQuery(Vector2 p)
        {
            if (!Bounds.ContainsPoint(p)) return false;

            float dn = Vector2.Dot(TransformedNormal, p) - Vector2.Dot(TransformedA, TransformedNormal);
            if (MathF.Abs(dn) > Radius) return false;

            float dt = MathUtil.Cross(p, TransformedNormal);
            float dta = MathUtil.Cross(TransformedA, TransformedNormal);
            float dtb = MathUtil.Cross(TransformedB, TransformedNormal);

            if (dt <= dta)
            {
                if (dt < dta - Radius) return false;
                return (TransformedA - p).LengthSquared() < Radius * Radius;
            }
            else if (dt > dtb)
            {
                if (dt > dtb + Radius) return false;
                return (TransformedB - p).LengthSquared() < Radius * Radius;
            }

            return true;
        }

        public override int FindVertexByPoint(Vector2 p, float minDist)
        {
            float dsq = minDist * minDist;
            if ((TransformedA - p).LengthSquared() < dsq) return 0;
            if ((TransformedB - p).LengthSquared() < dsq) return 1;
            return -1;
        }

        public override float DistanceOnPlane(Vector2 n, float d)
        {
            float a = Vector2.Dot(n, TransformedA) - Radius;
            float b = Vector2.Dot(n, TransformedB) - Radius;
            return MathF.Min(a, b) - d;
        }

        public override RaycastHit Raycast(Ray ray)
        {
            if (!Bounds.IntersectsRay(ray.Origin, ray.Direction, ray.MaxDistance))
                return RaycastHit.Miss;

            float nearestDistance = float.MaxValue;
            Vector2 nearestHitPoint = Vector2.Zero;
            Vector2 nearestNormal = Vector2.Zero;
            bool hit = false;

            // Create the two parallel planes for the capsule sides
            Vector2 normal1 = TransformedNormal;
            Vector2 normal2 = -TransformedNormal;

            Vector2 plane1Point = TransformedA + normal1 * Radius;
            Vector2 plane2Point = TransformedA + normal2 * Radius;

            // Test against plane 1 (positive normal side)
            float denom1 = Vector2.Dot(ray.Direction, normal1);
            if (MathF.Abs(denom1) > 0.0001f)
            {
                float t1 = Vector2.Dot(plane1Point - ray.Origin, normal1) / denom1;
                if (t1 >= 0 && t1 <= ray.MaxDistance)
                {
                    Vector2 hitPoint = ray.Origin + ray.Direction * t1;

                    // Check if hit point is within the segment bounds (clip to line length)
                    Vector2 segmentDir = TransformedB - TransformedA;
                    float segmentLength = segmentDir.Length();

                    if (segmentLength > 0.0001f)
                    {
                        segmentDir /= segmentLength;
                        float projection = Vector2.Dot(hitPoint - TransformedA, segmentDir);

                        if (projection >= 0 && projection <= segmentLength && t1 < nearestDistance)
                        {
                            nearestDistance = t1;
                            nearestHitPoint = hitPoint;
                            nearestNormal = denom1 < 0 ? normal1 : -normal1; // Normal points towards ray origin
                            hit = true;
                        }
                    }
                }
            }

            // Test against plane 2 (negative normal side)
            float denom2 = Vector2.Dot(ray.Direction, normal2);
            if (MathF.Abs(denom2) > 0.0001f)
            {
                float t2 = Vector2.Dot(plane2Point - ray.Origin, normal2) / denom2;
                if (t2 >= 0 && t2 <= ray.MaxDistance)
                {
                    Vector2 hitPoint = ray.Origin + ray.Direction * t2;

                    // Check if hit point is within the segment bounds (clip to line length)
                    Vector2 segmentDir = TransformedB - TransformedA;
                    float segmentLength = segmentDir.Length();

                    if (segmentLength > 0.0001f)
                    {
                        segmentDir /= segmentLength;
                        float projection = Vector2.Dot(hitPoint - TransformedA, segmentDir);

                        if (projection >= 0 && projection <= segmentLength && t2 < nearestDistance)
                        {
                            nearestDistance = t2;
                            nearestHitPoint = hitPoint;
                            nearestNormal = denom2 < 0 ? normal2 : -normal2; // Normal points towards ray origin
                            hit = true;
                        }
                    }
                }
            }

            // Test against end caps (circles)
            var circleHitA = RaycastCircle(ray, TransformedA, Radius);
            var circleHitB = RaycastCircle(ray, TransformedB, Radius);

            if (circleHitA.Hit && circleHitA.Distance < nearestDistance)
            {
                nearestDistance = circleHitA.Distance;
                nearestHitPoint = circleHitA.Point;
                nearestNormal = circleHitA.Normal;
                hit = true;
            }

            if (circleHitB.Hit && circleHitB.Distance < nearestDistance)
            {
                nearestDistance = circleHitB.Distance;
                nearestHitPoint = circleHitB.Point;
                nearestNormal = circleHitB.Normal;
                hit = true;
            }

            if (!hit) return RaycastHit.Miss;

            return new RaycastHit(true, nearestDistance, nearestHitPoint, nearestNormal, Body, this);
        }

        private static RaycastHit RaycastCircle(Ray ray, Vector2 center, float radius)
        {
            Vector2 toCenter = center - ray.Origin;
            float projectedLength = Vector2.Dot(toCenter, ray.Direction);

            if (projectedLength < 0) return RaycastHit.Miss;

            Vector2 closestPoint = ray.Origin + ray.Direction * projectedLength;
            float distanceToCenter = (center - closestPoint).Length();

            if (distanceToCenter > radius) return RaycastHit.Miss;

            float halfChord = MathF.Sqrt(radius * radius - distanceToCenter * distanceToCenter);
            float distance = projectedLength - halfChord;

            if (distance < 0 || distance > ray.MaxDistance) return RaycastHit.Miss;

            Vector2 hitPoint = ray.Origin + ray.Direction * distance;
            Vector2 normal = Vector2.Normalize(hitPoint - center);

            return new RaycastHit(true, distance, hitPoint, normal, null!, null!);
        }
    }
}
