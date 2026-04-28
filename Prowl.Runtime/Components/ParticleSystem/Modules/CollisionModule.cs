// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Collision mode for particles.
/// </summary>
[Serializable]
public enum CollisionMode
{
    World,  // Collide with physics world
    Planes  // Collide with simple planes
}

/// <summary>
/// Quality level for collision detection.
/// </summary>
[Serializable]
public enum CollisionQuality
{
    High,    // Check every particle (slow but accurate)
    Medium,  // Use voxel approximation (balanced)
    Low      // Aggressive voxel approximation (fast but less accurate)
}

/// <summary>
/// Controls particle collision with the world or planes.
/// </summary>
[Serializable]
public class CollisionModule : ParticleSystemModule
{
    public CollisionMode Mode = CollisionMode.World;
    public CollisionQuality Quality = CollisionQuality.Medium;

    // Physics properties
    public float Dampen = 0.5f;           // Velocity damping on collision (0-1)
    public float Bounce = 0.3f;           // Bounciness (0-1)
    public float LifetimeLoss = 0.0f;     // Lifetime percentage lost on collision (0-1)
    public float MinKillSpeed = 0.0f;     // Kill particle if speed drops below this
    public float ParticleRadius = 0.05f;  // Radius for collision detection
    public float MaxCollisionDistance = 1.0f; // Maximum raycast distance

    // Layer filtering for world collision
    public LayerMask CollidesWith = LayerMask.Everything;

    // Voxel settings for optimization
    public float VoxelSize = 1.0f;        // Size of spatial voxels for approximation

    // Plane collision mode
    public List<Plane> Planes = new();

    // Spatial optimization cache
    [NonSerialized]
    private Dictionary<Int3, List<int>> _voxelGrid = new();

    [NonSerialized]
    private Dictionary<Int3, bool> _voxelCollisionCache = new();

    public override void OnParticleUpdate(ref Particle particle, float deltaTime)
    {
        // Collision is handled in bulk update for efficiency
    }

    /// <summary>
    /// Updates all particles with collision detection (called from ParticleSystemComponent).
    /// </summary>
    public void UpdateCollisions(List<Particle> particles, PhysicsWorld physics, float deltaTime, Transform particleSystemTransform, SimulationSpace simulationSpace)
    {
        if (!Enabled || particles.Count == 0)
            return;

        if (Mode == CollisionMode.Planes)
        {
            UpdatePlaneCollisions(particles, deltaTime, particleSystemTransform, simulationSpace);
        }
        else if (Mode == CollisionMode.World)
        {
            UpdateWorldCollisions(particles, physics, deltaTime, particleSystemTransform, simulationSpace);
        }
    }

    private void UpdatePlaneCollisions(List<Particle> particles, float deltaTime, Transform particleSystemTransform, SimulationSpace simulationSpace)
    {
        if (Planes.Count == 0)
            return;

        for (int i = 0; i < particles.Count; i++)
        {
            var particle = particles[i];

            // Transform to world space if needed
            Float3 worldPos = particle.Position;
            Float3 worldVel = particle.Velocity;
            if (simulationSpace == SimulationSpace.Local && particleSystemTransform != null)
            {
                var worldPosDouble = particleSystemTransform.LocalToWorldMatrix * new Float4(particle.Position, 1.0f);
                worldPos = new Float3((float)worldPosDouble.X, (float)worldPosDouble.Y, (float)worldPosDouble.Z);

                var worldVelDouble = particleSystemTransform.LocalToWorldMatrix * new Float4(particle.Velocity, 0.0f);
                worldVel = new Float3((float)worldVelDouble.X, (float)worldVelDouble.Y, (float)worldVelDouble.Z);
            }

            foreach (var plane in Planes)
            {
                Float3 nextPos = worldPos + worldVel * deltaTime;
                float distance = (float)plane.GetSignedDistanceToPoint(nextPos) - ParticleRadius;

                if (distance < 0) // Collision detected
                {
                    // Reflect velocity in world space
                    Float3 normal = (Float3)plane.Normal;
                    Float3 reflectedVelocity = worldVel - normal * (2.0f * Float3.Dot(worldVel, normal));
                    worldVel = reflectedVelocity * Bounce + worldVel * (1.0f - Bounce);
                    worldVel *= (1.0f - Dampen);

                    // Correct position in world space
                    worldPos = nextPos - normal * distance;

                    // Transform back to local space if needed
                    if (simulationSpace == SimulationSpace.Local && particleSystemTransform != null)
                    {
                        var localPosDouble = particleSystemTransform.WorldToLocalMatrix * new Float4(worldPos, 1.0f);
                        particle.Position = new Float3((float)localPosDouble.X, (float)localPosDouble.Y, (float)localPosDouble.Z);

                        var localVelDouble = particleSystemTransform.WorldToLocalMatrix * new Float4(worldVel, 0.0f);
                        particle.Velocity = new Float3((float)localVelDouble.X, (float)localVelDouble.Y, (float)localVelDouble.Z);
                    }
                    else
                    {
                        particle.Position = worldPos;
                        particle.Velocity = worldVel;
                    }

                    // Apply lifetime loss
                    if (LifetimeLoss > 0)
                    {
                        particle.Lifetime *= (1.0f - LifetimeLoss);
                    }

                    // Check kill speed
                    if (MinKillSpeed > 0 && Float3.Length(particle.Velocity) < MinKillSpeed)
                    {
                        particle.Lifetime = 0;
                    }

                    particles[i] = particle;
                    break; // Only collide with first plane hit
                }
            }
        }
    }

    private void UpdateWorldCollisions(List<Particle> particles, PhysicsWorld physics, float deltaTime, Transform particleSystemTransform, SimulationSpace simulationSpace)
    {
        if (physics == null)
            return;

        _voxelGrid.Clear();
        _voxelCollisionCache.Clear();

        switch (Quality)
        {
            case CollisionQuality.High:
                UpdateWorldCollisions_High(particles, physics, deltaTime, particleSystemTransform, simulationSpace);
                break;
            case CollisionQuality.Medium:
                UpdateWorldCollisions_Medium(particles, physics, deltaTime, particleSystemTransform, simulationSpace);
                break;
            case CollisionQuality.Low:
                UpdateWorldCollisions_Low(particles, physics, deltaTime, particleSystemTransform, simulationSpace);
                break;
        }
    }

    // High quality: Check every particle
    private void UpdateWorldCollisions_High(List<Particle> particles, PhysicsWorld physics, float deltaTime, Transform particleSystemTransform, SimulationSpace simulationSpace)
    {
        for (int i = 0; i < particles.Count; i++)
        {
            var particle = particles[i];
            CheckAndResolveCollision(ref particle, physics, deltaTime, particleSystemTransform, simulationSpace);
            particles[i] = particle;
        }
    }

    // Medium quality: Voxelize and check representative particles
    private void UpdateWorldCollisions_Medium(List<Particle> particles, PhysicsWorld physics, float deltaTime, Transform particleSystemTransform, SimulationSpace simulationSpace)
    {
        // Build voxel grid
        for (int i = 0; i < particles.Count; i++)
        {
            Int3 voxelKey = WorldToVoxel(particles[i].Position);
            if (!_voxelGrid.ContainsKey(voxelKey))
                _voxelGrid[voxelKey] = new List<int>();
            _voxelGrid[voxelKey].Add(i);
        }

        // Check one particle per voxel, apply to all
        foreach (var kvp in _voxelGrid)
        {
            Int3 voxelKey = kvp.Key;
            List<int> particleIndices = kvp.Value;

            if (particleIndices.Count == 0)
                continue;

            // Check first particle in voxel
            int representativeIndex = particleIndices[0];
            var testParticle = particles[representativeIndex];
            bool hadCollision = CheckAndResolveCollision(ref testParticle, physics, deltaTime, particleSystemTransform, simulationSpace);
            particles[representativeIndex] = testParticle;

            // Apply same collision response to other particles in voxel
            if (hadCollision && particleIndices.Count > 1)
            {
                for (int i = 1; i < particleIndices.Count; i++)
                {
                    int idx = particleIndices[i];
                    var particle = particles[idx];

                    // Apply similar response (damping and bounce)
                    particle.Velocity *= (1.0f - Dampen) * Bounce;

                    if (LifetimeLoss > 0)
                        particle.Lifetime *= (1.0f - LifetimeLoss);

                    if (MinKillSpeed > 0 && Float3.Length(particle.Velocity) < MinKillSpeed)
                        particle.Lifetime = 0;

                    particles[idx] = particle;
                }
            }
        }
    }

    // Low quality: Aggressive voxelization, check even fewer particles
    private void UpdateWorldCollisions_Low(List<Particle> particles, PhysicsWorld physics, float deltaTime, Transform particleSystemTransform, SimulationSpace simulationSpace)
    {
        float largeVoxelSize = VoxelSize * 2.0f;

        // Build voxel grid with larger voxels
        for (int i = 0; i < particles.Count; i++)
        {
            Int3 voxelKey = WorldToVoxel(particles[i].Position, largeVoxelSize);
            if (!_voxelGrid.ContainsKey(voxelKey))
                _voxelGrid[voxelKey] = new List<int>();
            _voxelGrid[voxelKey].Add(i);
        }

        // Check one particle per large voxel
        foreach (var kvp in _voxelGrid)
        {
            List<int> particleIndices = kvp.Value;
            if (particleIndices.Count == 0)
                continue;

            // Only check representative particle
            int representativeIndex = particleIndices[0];
            var testParticle = particles[representativeIndex];
            bool hadCollision = CheckAndResolveCollision(ref testParticle, physics, deltaTime, particleSystemTransform, simulationSpace);
            particles[representativeIndex] = testParticle;

            // Apply to all if collision detected
            if (hadCollision)
            {
                for (int i = 1; i < particleIndices.Count; i++)
                {
                    int idx = particleIndices[i];
                    var particle = particles[idx];
                    particle.Velocity *= (1.0f - Dampen) * Bounce;

                    if (LifetimeLoss > 0)
                        particle.Lifetime *= (1.0f - LifetimeLoss);

                    if (MinKillSpeed > 0 && Float3.Length(particle.Velocity) < MinKillSpeed)
                        particle.Lifetime = 0;

                    particles[idx] = particle;
                }
            }
        }
    }

    private bool CheckAndResolveCollision(ref Particle particle, PhysicsWorld physics, float deltaTime, Transform particleSystemTransform, SimulationSpace simulationSpace)
    {
        // Transform to world space if needed
        Float3 worldPos = particle.Position;
        Float3 worldVel = particle.Velocity;

        if (simulationSpace == SimulationSpace.Local && particleSystemTransform != null)
        {
            var worldPosDouble = particleSystemTransform.LocalToWorldMatrix * new Float4(particle.Position, 1.0f);
            worldPos = new Float3((float)worldPosDouble.X, (float)worldPosDouble.Y, (float)worldPosDouble.Z);

            var worldVelDouble = particleSystemTransform.LocalToWorldMatrix * new Float4(particle.Velocity, 0.0f);
            worldVel = new Float3((float)worldVelDouble.X, (float)worldVelDouble.Y, (float)worldVelDouble.Z);
        }

        Float3 currentPos = worldPos;
        Float3 nextPos = currentPos + worldVel * deltaTime;
        Float3 direction = Float3.Normalize(nextPos - currentPos);
        float distance = Float3.Distance(currentPos, nextPos) + ParticleRadius;

        // Clamp distance to max
        distance = Maths.Min(distance, MaxCollisionDistance);

        // Raycast from current to next position
        if (physics.Raycast((Float3)currentPos, (Float3)direction, out RaycastHit hit, distance, CollidesWith))
        {
            // Collision detected!
            Float3 hitPoint = (Float3)hit.Point;
            Float3 hitNormal = (Float3)hit.Normal;

            // Reflect velocity in world space
            Float3 reflectedVelocity = worldVel - hitNormal * (2.0f * Float3.Dot(worldVel, hitNormal));
            worldVel = reflectedVelocity * Bounce + worldVel * (1.0f - Bounce);
            worldVel *= (1.0f - Dampen);

            // Position at hit point with offset
            worldPos = hitPoint + hitNormal * ParticleRadius;

            // Transform back to local space if needed
            if (simulationSpace == SimulationSpace.Local && particleSystemTransform != null)
            {
                var localPosDouble = particleSystemTransform.WorldToLocalMatrix * new Float4(worldPos, 1.0f);
                particle.Position = new Float3((float)localPosDouble.X, (float)localPosDouble.Y, (float)localPosDouble.Z);

                var localVelDouble = particleSystemTransform.WorldToLocalMatrix * new Float4(worldVel, 0.0f);
                particle.Velocity = new Float3((float)localVelDouble.X, (float)localVelDouble.Y, (float)localVelDouble.Z);
            }
            else
            {
                particle.Position = worldPos;
                particle.Velocity = worldVel;
            }

            // Apply lifetime loss
            if (LifetimeLoss > 0)
            {
                particle.Lifetime *= (1.0f - LifetimeLoss);
            }

            // Check kill speed
            if (MinKillSpeed > 0 && Float3.Length(particle.Velocity) < MinKillSpeed)
            {
                particle.Lifetime = 0;
            }

            return true;
        }

        return false;
    }

    private Int3 WorldToVoxel(Float3 worldPos, float customVoxelSize = 0)
    {
        float voxelSize = customVoxelSize > 0 ? customVoxelSize : VoxelSize;
        return new Int3(
            (int)Maths.Floor(worldPos.X / voxelSize),
            (int)Maths.Floor(worldPos.Y / voxelSize),
            (int)Maths.Floor(worldPos.Z / voxelSize)
        );
    }
}

// Simple 3D integer vector for voxel keys
internal struct Int3 : IEquatable<Int3>
{
    public int X, Y, Z;

    public Int3(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public bool Equals(Int3 other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Int3 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
}
