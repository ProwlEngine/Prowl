// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

public enum ParticleCollisionType
{
    /// <summary>Collide with the physics world.</summary>
    World,
    /// <summary>Collide with infinite planes, one per GameObject, facing along its up axis.</summary>
    Planes
}

public enum ParticleCollisionQuality
{
    /// <summary>Every particle casts a ray every frame.</summary>
    High,
    /// <summary>Surfaces found by earlier rays are cached per voxel and reused for a few frames.</summary>
    Medium,
    /// <summary>Like Medium with a quarter of the rays and a longer lived cache.</summary>
    Low
}

/// <summary>One particle hitting something.</summary>
public struct ParticleCollisionEvent
{
    public Float3 Intersection;
    public Float3 Normal;
    /// <summary>World velocity of the particle after the bounce.</summary>
    public Float3 Velocity;
    /// <summary>The collider hit, null for planes and for geometry no collider owns.</summary>
    public Collider? Collider;
    /// <summary>The transform of what was hit.</summary>
    public Transform? Transform;

    public readonly GameObject? Other => Transform?.GameObject;
}

/// <summary>
/// Implement on a component to hear about particles hitting its GameObject. Only called when the
/// system's <see cref="CollisionModule.SendCollisionMessages"/> is on.
/// </summary>
public interface IParticleCollisionHandler
{
    void OnParticleCollision(ParticleSystemComponent system, in ParticleCollisionEvent collision);
}

/// <summary>Makes particles bounce off the world or off planes.</summary>
[Serializable]
public class CollisionModule : ParticleSystemModule
{
    public ParticleCollisionType Type = ParticleCollisionType.World;

    [ShowIf(nameof(IsPlanes))]
    public List<GameObject> Planes = new();

    [Range(0f, 1f), Tooltip("Share of speed lost on every hit.")]
    public float Dampen = 0f;

    [Range(0f, 1f), Tooltip("Share of the speed into the surface kept as speed away from it.")]
    public float Bounce = 1f;

    [Range(0f, 1f), Tooltip("Share of the start lifetime lost on every hit.")]
    public float LifetimeLoss = 0f;

    [Tooltip("Particles slower than this after a hit die.")]
    public float MinKillSpeed = 0f;

    [Tooltip("Particles faster than this after a hit die.")]
    public float MaxKillSpeed = 10000f;

    [Tooltip("Collision radius as a share of half the particle's size.")]
    public float RadiusScale = 1f;

    [ShowIf(nameof(IsWorld))]
    public LayerMask CollidesWith = LayerMask.Everything;

    [ShowIf(nameof(IsWorld))]
    public ParticleCollisionQuality Quality = ParticleCollisionQuality.High;

    [ShowIf(nameof(IsCached)), Tooltip("Size of the cells cached surfaces are stored in.")]
    public float VoxelSize = 0.5f;

    [ShowIf(nameof(IsCached)), Tooltip("Rays cast per frame to fill the cache.")]
    public int MaxCollisionQueries = 256;

    [Tooltip("Call IParticleCollisionHandler on what was hit.")]
    public bool SendCollisionMessages = false;

    private bool IsPlanes => Type == ParticleCollisionType.Planes;
    private bool IsWorld => Type == ParticleCollisionType.World;
    private bool IsCached => IsWorld && Quality != ParticleCollisionQuality.High;

    private struct CachedSurface
    {
        public bool HasSurface;
        public Float3 Point;
        public Float3 Normal;
        public Collider? Collider;
        public Transform? Transform;
        public long Frame;
    }

    private readonly Dictionary<(int, int, int), CachedSurface> _cache = new();
    private long _queryFrame = -1;
    private int _queriesLeft;

    /// <summary>
    /// Sweeps the particle from <paramref name="previous"/> to where it moved this step and bounces it off
    /// the first surface in the way. Returns true on a hit.
    /// </summary>
    internal bool Apply(ParticleSystemComponent system, PhysicsWorld? physics, ref Particle p, Float3 previous, out ParticleCollisionEvent collision)
    {
        collision = default;

        Float3 from = system.SimPointToWorld(previous);
        Float3 to = system.SimPointToWorld(p.Position);
        float radius = MathF.Max(1e-4f, (MathF.Abs(p.Size.X) + MathF.Abs(p.Size.Y)) * 0.25f * RadiusScale * system.SizeScale);

        bool hit = Type == ParticleCollisionType.Planes
            ? SweepPlanes(from, to, radius, ref collision)
            : Quality == ParticleCollisionQuality.High
                ? SweepWorld(physics, from, to, radius, ref collision)
                : SweepCached(physics, from, to, radius, ref collision);
        if (!hit) return false;

        Float3 velocity = system.SimVectorToWorld(p.TotalVelocity);
        float into = Float3.Dot(velocity, collision.Normal);
        if (into < 0f)
            velocity -= collision.Normal * ((1f + Maths.Saturate(Bounce)) * into);
        velocity *= 1f - Maths.Saturate(Dampen);

        p.Position = system.WorldPointToSim(collision.Intersection + collision.Normal * radius);
        p.Velocity = system.WorldVectorToSim(velocity) - p.AnimatedVelocity;

        if (LifetimeLoss > 0f)
            p.Lifetime -= p.StartLifetime * LifetimeLoss;

        float speed = Float3.Length(velocity);
        if (speed < MinKillSpeed || speed > MaxKillSpeed)
            p.Lifetime = 0f;

        collision.Velocity = velocity;
        return true;
    }

    private bool SweepWorld(PhysicsWorld? physics, Float3 from, Float3 to, float radius, ref ParticleCollisionEvent collision)
    {
        if (physics == null) return false;

        Float3 delta = to - from;
        float distance = Float3.Length(delta);
        if (distance < 1e-6f) return false;

        if (!physics.Raycast(from, delta / distance, out RaycastHit hit, distance + radius, CollidesWith))
            return false;

        collision.Intersection = hit.Point;
        collision.Normal = hit.Normal;
        collision.Collider = hit.Collider;
        collision.Transform = hit.Transform;
        return true;
    }

    private bool SweepCached(PhysicsWorld? physics, Float3 from, Float3 to, float radius, ref ParticleCollisionEvent collision)
    {
        if (physics == null) return false;

        long frame = Time.FrameCount;
        bool low = Quality == ParticleCollisionQuality.Low;
        int lifetime = low ? 30 : 8;
        if (_queryFrame != frame)
        {
            _queryFrame = frame;
            _queriesLeft = Math.Max(1, low ? MaxCollisionQueries / 4 : MaxCollisionQueries);
            if (_cache.Count > 8192) _cache.Clear();
        }

        float cell = MathF.Max(VoxelSize, 0.01f);
        var key = ((int)MathF.Floor(from.X / cell), (int)MathF.Floor(from.Y / cell), (int)MathF.Floor(from.Z / cell));

        if (!_cache.TryGetValue(key, out CachedSurface surface) || frame - surface.Frame > lifetime)
        {
            if (_queriesLeft <= 0) return false;
            _queriesLeft--;

            Float3 delta = to - from;
            float distance = Float3.Length(delta);
            if (distance < 1e-6f) return false;

            surface = new CachedSurface { Frame = frame };
            if (physics.Raycast(from, delta / distance, out RaycastHit hit, MathF.Max(distance + radius, cell * 2f), CollidesWith))
            {
                surface.HasSurface = true;
                surface.Point = hit.Point;
                surface.Normal = hit.Normal;
                surface.Collider = hit.Collider;
                surface.Transform = hit.Transform;
            }
            _cache[key] = surface;
        }

        if (!surface.HasSurface) return false;
        if (!SweepPlane(from, to, radius, surface.Point, surface.Normal, out Float3 contact)) return false;

        collision.Intersection = contact;
        collision.Normal = surface.Normal;
        collision.Collider = surface.Collider;
        collision.Transform = surface.Transform;
        return true;
    }

    private bool SweepPlanes(Float3 from, Float3 to, float radius, ref ParticleCollisionEvent collision)
    {
        float best = float.MaxValue;
        bool hit = false;
        foreach (GameObject plane in Planes)
        {
            if (plane.IsNotValid()) continue;
            Transform t = plane.Transform;
            Float3 normal = Float3.NormalizeSafe(t.Up, Float3.UnitY);
            if (!SweepPlane(from, to, radius, t.Position, normal, out Float3 contact)) continue;

            float d = Float3.LengthSquared(contact - from);
            if (d >= best) continue;
            best = d;
            hit = true;
            collision.Intersection = contact;
            collision.Normal = normal;
            collision.Collider = null;
            collision.Transform = t;
        }
        return hit;
    }

    /// <summary>A sphere moving from <paramref name="from"/> to <paramref name="to"/> against the front of a plane. Contact is the point on the plane.</summary>
    private static bool SweepPlane(Float3 from, Float3 to, float radius, Float3 point, Float3 normal, out Float3 contact)
    {
        float d0 = Float3.Dot(from - point, normal) - radius;
        float d1 = Float3.Dot(to - point, normal) - radius;
        contact = default;
        if (d0 < -radius || d1 >= 0f || d1 >= d0) return false;

        float t = d0 <= 0f ? 0f : d0 / (d0 - d1);
        Float3 center = from + (to - from) * t;
        contact = center - normal * (Float3.Dot(center - point, normal));
        return true;
    }
}
