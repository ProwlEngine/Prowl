using System.Numerics;

namespace Prowl.Drift
{
    public class ShapeCircle : Shape
    {
        public Vector2 LocalCenter;
        public float Radius;
        public Vector2 TransformedCenter { get; private set; } = Vector2.Zero;

        public ShapeCircle(float x, float y, float radius) : base(TypeCircle)
        {
            LocalCenter = new Vector2(x, y);
            Radius = Math.Abs(radius);
        }

        public override Shape Duplicate() => new ShapeCircle(LocalCenter.X, LocalCenter.Y, Radius);

        public override void Recenter(Vector2 c) => LocalCenter -= c;

        public override float Area() => MathF.PI * (Radius * Radius);

        public override Vector2 Centroid() => LocalCenter;

        public override float Inertia(float mass) => Geometry.InertiaForCircle(mass, LocalCenter, Radius, 0);

        public override void CacheData(Body body)
        {
            TransformedCenter = body.TransformPoint(LocalCenter);
            Bounds = new Bounds(new(TransformedCenter.X - Radius, TransformedCenter.Y - Radius), new Vector2(TransformedCenter.X + Radius, TransformedCenter.Y + Radius));
            //Bounds.Mins.Set(TransformedCenter.X - Radius, TransformedCenter.Y - Radius);
            //Bounds.Maxs.Set(TransformedCenter.X + Radius, TransformedCenter.Y + Radius);
        }

        public override bool PointQuery(Vector2 p) => (TransformedCenter - p).LengthSquared() < Radius * Radius;

        public override int FindVertexByPoint(Vector2 p, float minDist)
        {
            float dsq = minDist * minDist;
            return (TransformedCenter - p).LengthSquared() < dsq ? 0 : -1;
        }

        public override float DistanceOnPlane(Vector2 n, float d) => Vector2.Dot(n, TransformedCenter) - Radius - d;
    }
}
