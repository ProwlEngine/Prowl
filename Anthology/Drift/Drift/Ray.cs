using System.Numerics;

namespace Prowl.Drift
{
    public struct Ray
    {
        public Vector2 Origin;
        public Vector2 Direction;
        public float MaxDistance;

        public Ray(Vector2 origin, Vector2 direction, float maxDistance = float.MaxValue)
        {
            Origin = origin;
            Direction = Vector2.Normalize(direction);
            MaxDistance = maxDistance;
        }

        public Vector2 GetPoint(float distance) => Origin + Direction * distance;
    }

    public struct RaycastHit
    {
        public bool Hit;
        public float Distance;
        public Vector2 Point;
        public Vector2 Normal;
        public Body Body;
        public Shape Shape;

        public RaycastHit(bool hit, float distance, Vector2 point, Vector2 normal, Body body, Shape shape)
        {
            Hit = hit;
            Distance = distance;
            Point = point;
            Normal = normal;
            Body = body;
            Shape = shape;
        }

        public static RaycastHit Miss => new RaycastHit(false, float.MaxValue, Vector2.Zero, Vector2.Zero, null!, null!);
    }
}