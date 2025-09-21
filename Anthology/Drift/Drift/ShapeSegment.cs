namespace Prowl.Drift
{
    public class ShapeSegment : Shape
    {
        public Vec2 A, B;
        public float Radius;
        public Vec2 Normal;

        public Vec2 TransformedA { get; private set; } = Vec2.Zero;
        public Vec2 TransformedB { get; private set; } = Vec2.Zero;
        public Vec2 TransformedNormal { get; private set; } = Vec2.Zero;

        public ShapeSegment(Vec2 a, Vec2 b, float radius) : base(TypeSegment)
        {
            A = a;
            B = b;
            Radius = Math.Abs(radius);
            Normal = Vec2.Normalize(Vec2.Perp(b - a));
        }

        public override Shape Duplicate() => new ShapeSegment(A, B, Radius);

        public override void Recenter(Vec2 c) { A -= c; B -= c; }

        public override float Area() => Geometry.AreaForSegment(A, B, Radius);

        public override Vec2 Centroid() => Geometry.CentroidForSegment(A, B);

        public override float Inertia(float mass) => Geometry.InertiaForSegment(mass, A, B);

        public override void CacheData(Body body)
        {
            TransformedA = body.TransformPoint(A);
            TransformedB = body.TransformPoint(B);
            TransformedNormal = Vec2.Normalize(Vec2.Perp(TransformedB - TransformedA));

            float l = MathF.Min(TransformedA.X, TransformedB.X);
            float r = MathF.Max(TransformedA.X, TransformedB.X);
            float b = MathF.Min(TransformedA.Y, TransformedB.Y);
            float t = MathF.Max(TransformedA.Y, TransformedB.Y);

            Bounds.Mins.Set(l - Radius, b - Radius);
            Bounds.Maxs.Set(r + Radius, t + Radius);
        }

        public override bool PointQuery(Vec2 p)
        {
            if (!Bounds.ContainsPoint(p)) return false;

            float dn = Vec2.Dot(TransformedNormal, p) - Vec2.Dot(TransformedA, TransformedNormal);
            if (MathF.Abs(dn) > Radius) return false;

            float dt = Vec2.Cross(p, TransformedNormal);
            float dta = Vec2.Cross(TransformedA, TransformedNormal);
            float dtb = Vec2.Cross(TransformedB, TransformedNormal);

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

        public override int FindVertexByPoint(Vec2 p, float minDist)
        {
            float dsq = minDist * minDist;
            if ((TransformedA - p).LengthSquared() < dsq) return 0;
            if ((TransformedB - p).LengthSquared() < dsq) return 1;
            return -1;
        }

        public override float DistanceOnPlane(Vec2 n, float d)
        {
            float a = Vec2.Dot(n, TransformedA) - Radius;
            float b = Vec2.Dot(n, TransformedB) - Radius;
            return MathF.Min(a, b) - d;
        }
    }
}
