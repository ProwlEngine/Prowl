namespace Physics2D
{
    public abstract class Shape
    {
        private static int _idCounter = 0;

        public readonly int Id;
        public readonly int Type;

        public Body Body { get; internal set; }

        public float Elasticity = 0.0f;  // e
        public float Friction = 1.0f;    // u
        public float Density = 1.0f;

        public Bounds Bounds = new Bounds();

        public const int TypeCircle = 0;
        public const int TypeSegment = 1;
        public const int TypePoly = 2;

        protected Shape(int type)
        {
            Id = _idCounter++;
            Type = type;
        }

        public abstract Shape Duplicate();
        public abstract void Recenter(Vec2 c);
        public abstract void Transform(Transform xf);
        public abstract void Untransform(Transform xf);
        public abstract float Area();
        public abstract Vec2 Centroid();
        public abstract float Inertia(float mass);
        public abstract void CacheData(Transform xf);
        public abstract bool PointQuery(Vec2 p);
        public abstract int FindVertexByPoint(Vec2 p, float minDist);
        public abstract float DistanceOnPlane(Vec2 n, float d);
    }
}
