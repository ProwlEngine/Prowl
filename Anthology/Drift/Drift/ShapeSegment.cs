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
    }
}
