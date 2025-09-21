namespace Prowl.Drift
{
    public class ShapeCircle : Shape
    {
        public Vec2 LocalCenter;
        public float Radius;
        public Vec2 TransformedCenter { get; private set; } = Vec2.Zero;

        public ShapeCircle(float x, float y, float radius) : base(TypeCircle)
        {
            LocalCenter = new Vec2(x, y);
            Radius = Math.Abs(radius);
        }

        public override Shape Duplicate() => new ShapeCircle(LocalCenter.X, LocalCenter.Y, Radius);

        public override void Recenter(Vec2 c) => LocalCenter -= c;

        public override void Transform(Transform xf) => LocalCenter = xf.TransformPoint(LocalCenter);

        public override void Untransform(Transform xf) => LocalCenter = xf.UntransformPoint(LocalCenter);

        public override float Area() => MathF.PI * (Radius * Radius);

        public override Vec2 Centroid() => LocalCenter;

        public override float Inertia(float mass) => Geometry.InertiaForCircle(mass, LocalCenter, Radius, 0);

        public override void CacheData(Transform xf)
        {
            TransformedCenter = xf.TransformPoint(LocalCenter);
            Bounds = new Bounds(new(TransformedCenter.X - Radius, TransformedCenter.Y - Radius), new Vec2(TransformedCenter.X + Radius, TransformedCenter.Y + Radius));
            //Bounds.Mins.Set(TransformedCenter.X - Radius, TransformedCenter.Y - Radius);
            //Bounds.Maxs.Set(TransformedCenter.X + Radius, TransformedCenter.Y + Radius);
        }

        public override bool PointQuery(Vec2 p) => (TransformedCenter - p).LengthSquared() < Radius * Radius;

        public override int FindVertexByPoint(Vec2 p, float minDist)
        {
            float dsq = minDist * minDist;
            return (TransformedCenter - p).LengthSquared() < dsq ? 0 : -1;
        }

        public override float DistanceOnPlane(Vec2 n, float d) => Vec2.Dot(n, TransformedCenter) - Radius - d;
    }
}
