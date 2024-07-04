using System;

namespace Prowl.Runtime.Raycast;

/// <summary>
/// Information returned by the different simulation test methods in <see cref="Stride.BepuPhysics.Configurations.BepuSimulation"/>
/// </summary>
/// <param name="Point">The position where the intersection occured</param>
/// <param name="Normal">The direction of the surface hit</param>
/// <param name="Distance">The distance along the ray where the hit occured</param>
/// <param name="Container">The container hit</param>
public readonly record struct HitInfo : IComparable<HitInfo>
{
    public Vector3 Point { get; init; }
    public Vector3 Normal { get; init; }
    public float Distance { get; init; }
    public PhysicsBody Container { get; init; }

    public HitInfo(Vector3 point, Vector3 normal, float distance, PhysicsBody container)
    {
        Point = point;
        Normal = normal;
        Distance = distance;
        Container = container;
    }

    public int CompareTo(HitInfo other)
    {
        return Distance.CompareTo(other.Distance);
    }
}
