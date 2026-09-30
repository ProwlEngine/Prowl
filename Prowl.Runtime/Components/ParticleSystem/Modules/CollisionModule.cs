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
    /// <summary>Every particle sweeps a sphere of its own size every frame.</summary>
    High,
    /// <summary>
    /// Surfaces found by earlier rays are cached per cell and direction of travel, and reused for a few
    /// frames. Particles in a cell with nothing cached once the frame's rays run out skip collision that frame.
    /// </summary>
    Medium,
    /// <summary>Like Medium with a quarter of the rays and a longer lived cache.</summary>
    Low
}

/// <summary>One particle hitting something.</summary>
public struct ParticleCollisionEvent
{
    public Float3 Intersection;
    public Float3 Normal;
    /// <summary>World velocity of the particle after the bounce, including animated velocity.</summary>
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

    // A particle already touching the surface at the start of the step, or meeting it slower than this,
    // is resting on it: it is held there and slides along it, but does not count as a collision, so
    // resting particles do not raise events or lose lifetime every step.
    private const float RestingSpeed = 0.1f;
    private const float ContactDistance = 1e-3f;

    private struct CachedSurface
    {
        public bool HasSurface;
        public Float3 Point;
        public Float3 Normal;
        public Collider? Collider;
        public Transform? Transform;
        public long Frame;
    }

    // Surfaces are cached per cell and per direction of travel, so a miss found by a particle moving one
    // way is never reused for a particle moving another way through the same cell.
    private readonly Dictionary<(int, int, int, int), CachedSurface> _cache = new();
    private long _queryFrame = -1;
    private int _queriesLeft;

    /// <summary>
    /// Sweeps the particle from <paramref name="previous"/> to where it moved this step and bounces it off
    /// the first surface in the way. Returns true when the hit should be reported, false for no hit or for
    /// a particle that is only resting on a surface.
    /// </summary>
    internal bool Apply(ParticleSystemComponent system, PhysicsWorld? physics, ref Particle p, Float3 previous, out ParticleCollisionEvent collision)
    {
        collision = default;

        Float3 from = system.SimPointToWorld(previous);
        Float3 to = system.SimPointToWorld(p.Position);
        float radius = MathF.Max(1e-4f, (MathF.Abs(p.Size.X) + MathF.Abs(p.Size.Y)) * 0.25f * RadiusScale * system.SizeScale);

        Float3 center;
        bool touching;
        bool hit = Type == ParticleCollisionType.Planes
            ? SweepPlanes(from, to, radius, ref collision, out center, out touching)
            : Quality == ParticleCollisionQuality.High
                ? SweepWorld(physics, from, to, radius, ref collision, out center, out touching)
                : SweepCached(physics, from, to, radius, ref collision, out center, out touching);
        if (!hit) return false;

        Float3 normal = collision.Normal;

        // Whatever of the move is left after the contact continues along the surface, so particles
        // slide instead of stopping dead wherever they first touch.
        Float3 rest = to - center;
        float restInto = Float3.Dot(rest, normal);
        if (restInto < 0f) rest -= normal * restInto;
        center += rest;
        Float3 animated = system.SimVectorToWorld(p.AnimatedVelocity);
        Float3 velocity = system.SimVectorToWorld(p.Velocity);
        float approach = -Float3.Dot(velocity + animated, normal);

        // Only the simulated velocity bounces. Animated velocity is rebuilt every step, so folding it in
        // here would make a particle pushed into a surface fly off it once the push ends.
        float into = Float3.Dot(velocity, normal);
        if (into < 0f)
            velocity -= normal * ((1f + Maths.Saturate(Bounce)) * into);
        velocity *= 1f - Maths.Saturate(Dampen);

        p.Position = system.WorldPointToSim(center);
        p.Velocity = system.WorldVectorToSim(velocity);
        collision.Velocity = velocity + animated;

        // Settled particles still die to the kill speeds, that is what Min Kill Speed is usually for.
        float speed = Float3.Length(collision.Velocity);
        if (speed < MinKillSpeed || speed > MaxKillSpeed)
            p.Lifetime = 0f;

        if (touching || approach < RestingSpeed)
            return false;

        if (LifetimeLoss > 0f)
            p.Lifetime -= p.StartLifetime * LifetimeLoss;

        return true;
    }

    private bool SweepWorld(PhysicsWorld? physics, Float3 from, Float3 to, float radius, ref ParticleCollisionEvent collision, out Float3 center, out bool touching)
    {
        center = default;
        touching = false;
        if (physics == null) return false;

        Float3 delta = to - from;
        float distance = Float3.Length(delta);
        if (distance < 1e-6f) return false;
        Float3 direction = delta / distance;

        // A sphere sweep, so a particle sliding along a surface touches it with its edge, not its center.
        if (!physics.SphereCast(from, radius, direction, distance, out ShapeCastHit hit, CollidesWith))
            return false;

        Float3 normal = Float3.NormalizeSafe(hit.Normal, -direction);
        if (Float3.Dot(normal, direction) > 0f) normal = -normal;

        center = hit.Penetration > 0f && hit.Distance <= 0f
            ? from + normal * hit.Penetration
            : from + direction * hit.Distance;
        touching = hit.Distance <= ContactDistance;
        collision.Intersection = hit.HitPoint;
        collision.Normal = normal;
        collision.Collider = hit.Collider;
        collision.Transform = hit.Transform;
        return true;
    }

    private bool SweepCached(PhysicsWorld? physics, Float3 from, Float3 to, float radius, ref ParticleCollisionEvent collision, out Float3 center, out bool touching)
    {
        center = default;
        touching = false;
        if (physics == null) return false;

        Float3 delta = to - from;
        float distance = Float3.Length(delta);
        if (distance < 1e-6f) return false;
        Float3 direction = delta / distance;

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
        var key = ((int)MathF.Floor(from.X / cell), (int)MathF.Floor(from.Y / cell), (int)MathF.Floor(from.Z / cell), DirectionBucket(direction));

        if (!_cache.TryGetValue(key, out CachedSurface surface) || frame - surface.Frame > lifetime)
        {
            if (_queriesLeft <= 0) return false;
            _queriesLeft--;

            surface = new CachedSurface { Frame = frame };
            if (physics.Raycast(from, direction, out RaycastHit hit, MathF.Max(distance + radius, cell * 2f), CollidesWith))
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
        if (!SweepPlane(from, to, radius, surface.Point, surface.Normal, out Float3 contact, out center, out touching)) return false;

        collision.Intersection = contact;
        collision.Normal = surface.Normal;
        collision.Collider = surface.Collider;
        collision.Transform = surface.Transform;
        return true;
    }

    /// <summary>Which of the six axis directions <paramref name="direction"/> leans toward most.</summary>
    private static int DirectionBucket(Float3 direction)
    {
        float x = MathF.Abs(direction.X), y = MathF.Abs(direction.Y), z = MathF.Abs(direction.Z);
        if (x >= y && x >= z) return direction.X >= 0f ? 0 : 1;
        if (y >= z) return direction.Y >= 0f ? 2 : 3;
        return direction.Z >= 0f ? 4 : 5;
    }

    private bool SweepPlanes(Float3 from, Float3 to, float radius, ref ParticleCollisionEvent collision, out Float3 center, out bool touching)
    {
        center = default;
        touching = false;
        float best = float.MaxValue;
        bool hit = false;
        foreach (GameObject plane in Planes)
        {
            if (plane.IsNotValid()) continue;
            Transform t = plane.Transform;
            Float3 normal = Float3.NormalizeSafe(t.Up, Float3.UnitY);
            if (!SweepPlane(from, to, radius, t.Position, normal, out Float3 contact, out Float3 resolved, out bool planeTouching)) continue;

            float d = Float3.LengthSquared(resolved - from);
            if (d >= best) continue;
            best = d;
            hit = true;
            center = resolved;
            touching = planeTouching;
            collision.Intersection = contact;
            collision.Normal = normal;
            collision.Collider = null;
            collision.Transform = t;
        }
        return hit;
    }

    /// <summary>
    /// A sphere moving from <paramref name="from"/> to <paramref name="to"/> against the front of a plane.
    /// Contact is the point on the plane, center is where the sphere comes to rest against it.
    /// </summary>
    private static bool SweepPlane(Float3 from, Float3 to, float radius, Float3 point, Float3 normal, out Float3 contact, out Float3 center, out bool touching)
    {
        float d0 = Float3.Dot(from - point, normal) - radius;
        float d1 = Float3.Dot(to - point, normal) - radius;
        contact = default;
        center = default;
        touching = d0 <= ContactDistance;
        if (d0 < -radius || d1 >= 0f || d1 >= d0) return false;

        float t = d0 <= 0f ? 0f : d0 / (d0 - d1);
        Float3 swept = from + (to - from) * t;
        contact = swept - normal * Float3.Dot(swept - point, normal);
        center = contact + normal * radius;
        return true;
    }
}
