using System.Numerics;

namespace Prowl.Drift
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
        public abstract void Recenter(Vector2 c);
        public abstract float Area();
        public abstract Vector2 Centroid();
        public abstract float Inertia(float mass);
        public abstract void CacheData(Body body);
        public abstract bool PointQuery(Vector2 p);
        public abstract int FindVertexByPoint(Vector2 p, float minDist);
        public abstract float DistanceOnPlane(Vector2 n, float d);
    }
}
