// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Numerics;


<<<<<<< TODO: Unmerged change from project 'Drift(net8.0)', Before:
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
=======
namespace Prowl.Drift;

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
>>>>>>> After
namespace Prowl.Drift;

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
