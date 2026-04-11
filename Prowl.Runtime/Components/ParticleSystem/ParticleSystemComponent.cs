// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.ParticleSystem;

/// <summary>
/// A GPU-instanced particle system component.
/// Particles are rendered using instanced rendering for optimal performance.
/// </summary>
[AddComponentMenu("Effects/Particle System")]
[ExecuteAlways]
public class ParticleSystemComponent : MonoBehaviour
{
    #region Configuration

    public AssetRef<Material> Material;
    public int MaxParticles = 1000;
    public float Duration = 5.0f;
    public bool Looping = true;
    public bool PlayOnEnable = true;
    public bool Prewarm = false;
    public SimulationSpace SimulationSpace = SimulationSpace.Local;

    #endregion

    #region Modules

    public InitialModule Initial = new() { Enabled = true };
    public EmissionModule Emission = new() { Enabled = true };
    public SizeOverLifetimeModule SizeOverLifetime = new();
    public ColorOverLifetimeModule ColorOverLifetime = new();
    public RotationOverLifetimeModule RotationOverLifetime = new();
    public VelocityOverLifetimeModule VelocityOverLifetime = new();
    public CollisionModule Collision = new();
    public UVModule UV = new();

    #endregion

    #region State

    private List<Particle> _particles = new();
    private Random _random = new();
    private float _time = 0;
    private bool _isPlaying = false;
    private SimulationSpace _prevSimulationSpace = SimulationSpace.Local;
    private PropertyState _properties = new();

    // GPU instancing data - separate arrays for clean API
    private Mesh _quadMesh;
    private Float4x4[] _transforms = Array.Empty<Float4x4>();
    private Float4[] _colors = Array.Empty<Float4>();
    private Float4[] _customData = Array.Empty<Float4>();
    private AABB _bounds; // Computed bounds from particle positions + sizes

    #endregion

    #region Lifecycle

    public override void OnEnable()
    {
        base.OnEnable();

        // Create quad mesh for particle rendering if not already created
        if (_quadMesh == null)
        {
            CreateQuadMesh();
        }

        if (PlayOnEnable && !_isPlaying)
        {
            Play();
        }
    }

    public override void Update()
    {
        // Migrate particles when simulation space changes
        if (_prevSimulationSpace != SimulationSpace)
        {
            MigrateParticleSpace(_prevSimulationSpace, SimulationSpace);
            _prevSimulationSpace = SimulationSpace;
        }

        if (!_isPlaying)
            return;

        float deltaTime = Time.DeltaTime;

        // Update time
        _time += deltaTime;

        // Check if we should stop (non-looping systems)
        if (!Looping && _time >= Duration)
        {
            _time = Duration;
            _isPlaying = false;
        }

        // Emit new particles
        int emitCount = Emission.CalculateEmitCount(deltaTime, _time / Duration, _random);
        for (int i = 0; i < emitCount && _particles.Count < MaxParticles; i++)
        {
            EmitParticle();
        }

        // Update existing particles
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var particle = _particles[i];

            // Update lifetime
            particle.Lifetime -= deltaTime;
            particle.TotalTime += deltaTime;

            if (particle.Lifetime <= 0)
            {
                _particles.RemoveAt(i);
                continue;
            }

            // Update particle through modules
            Initial.OnParticleUpdate(ref particle, deltaTime);
            SizeOverLifetime.OnParticleUpdate(ref particle, deltaTime);
            ColorOverLifetime.OnParticleUpdate(ref particle, deltaTime);
            RotationOverLifetime.OnParticleUpdate(ref particle, deltaTime);
            VelocityOverLifetime.OnParticleUpdate(ref particle, deltaTime);
            UV.OnParticleUpdate(ref particle, deltaTime);

            // Update position
            particle.Position += particle.Velocity * deltaTime;

            _particles[i] = particle;
        }

        // Update collisions (bulk operation for efficiency)
        if (Collision.Enabled)
        {
            Collision.UpdateCollisions(_particles, GameObject.Scene?.Physics, deltaTime, Transform, SimulationSpace);
        }
    }

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        if (_particles.Count <= 0 || Material.Res == null || _quadMesh == null) return;

        // Update instance data from particles
        UpdateInstanceData();

        // Setup per-object properties
        _properties.Clear();
        _properties.SetInt("_ObjectID", InstanceID);

        // Create batched instanced renderables
        InstancedMeshRenderable.CreateBatched(
            renderables,
            _quadMesh,
            Material.Res,
            _transforms,
            Transform.Position,
            _colors,
            _customData,
            GameObject.LayerIndex,
            _properties,
            _bounds
        );
    }

    public override void OnDisable()
    {
        base.OnDisable();
        Stop();

        // Clean up resources
        _quadMesh?.Dispose();
        _quadMesh = null;
    }

    #endregion

    #region Particle System Control

    public void Play()
    {
        _isPlaying = true;
        _time = 0;
        Emission.Reset();

        if (Prewarm && Looping)
        {
            PrewarmSystem();
        }
    }

    public void Stop()
    {
        _isPlaying = false;
        _time = 0;
        _particles.Clear();
    }

    public void Pause()
    {
        _isPlaying = false;
    }

    public void Clear()
    {
        _particles.Clear();
    }

    private void MigrateParticleSpace(SimulationSpace from, SimulationSpace to)
    {
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];

            if (from == SimulationSpace.Local && to == SimulationSpace.World)
            {
                p.Position = (Float3)Transform.Position + (Transform.Rotation * p.Position);
                p.Velocity = Transform.Rotation * p.Velocity;
            }
            else if (from == SimulationSpace.World && to == SimulationSpace.Local)
            {
                var invRot = Quaternion.Inverse(Transform.Rotation);
                p.Position = invRot * (p.Position - (Float3)Transform.Position);
                p.Velocity = invRot * p.Velocity;
            }

            _particles[i] = p;
        }
    }

    public bool IsPlaying => _isPlaying;
    public int ParticleCount => _particles.Count;

    #endregion

    #region Particle Emission

    private void EmitParticle()
    {
        // Get spawn position and direction from emission shape
        Float3 spawnPosition = Float3.Zero;
        Float3 spawnDirection = new Float3(0, 1, 0);

        if (Emission.Enabled)
        {
            Emission.GetShapePositionAndDirection(_random, out spawnPosition, out spawnDirection);
        }

        var particle = new Particle
        {
            Position = spawnPosition,
            Velocity = spawnDirection * 1.0f, // Default velocity, will be modified by modules
            Rotation = 0,
            RotationalSpeed = 0,
            StartSize = 1,
            Size = 1,
            StartColor = Color.White,
            Color = Color.White,
            StartLifetime = 1,
            Lifetime = 1,
            RandomSeed = (uint)_random.Next(),
            UVFrame = 0,
            TotalTime = 0
        };

        // Initialize through modules
        Initial.OnParticleSpawn(ref particle, _random);
        RotationOverLifetime.OnParticleSpawn(ref particle, _random);
        UV.OnParticleSpawn(ref particle, _random);

        // Transform to world space if needed
        if (SimulationSpace == SimulationSpace.World)
        {
            // Apply the GameObject's rotation to position offset and velocity direction
            var rotation = Transform.Rotation;
            particle.Position = (Float3)Transform.Position + (rotation * particle.Position);
            particle.Velocity = rotation * particle.Velocity;
        }

        _particles.Add(particle);
    }

    private Float3 RandomDirection()
    {
        // Random direction on unit sphere
        float theta = (float)(_random.NextDouble() * Maths.PI * 2);
        float phi = (float)(Maths.Acos(2.0 * _random.NextDouble() - 1.0));

        return new Float3(
            (float)(Maths.Sin(phi) * Maths.Cos(theta)),
            (float)(Maths.Sin(phi) * Maths.Sin(theta)),
            (float)Maths.Cos(phi)
        );
    }

    #endregion

    #region Prewarm

    private void PrewarmSystem()
    {
        if (!Looping || Duration <= 0)
            return;

        // Simulate the system for one duration cycle
        float prewarmTime = Duration;
        float step = 0.05f; // 50ms timesteps
        float elapsed = 0;

        while (elapsed < prewarmTime)
        {
            float deltaTime = Maths.Min(step, prewarmTime - elapsed);

            // Emit particles
            int emitCount = Emission.CalculateEmitCount(deltaTime, elapsed / Duration, _random);
            for (int i = 0; i < emitCount && _particles.Count < MaxParticles; i++)
            {
                EmitParticle();
            }

            // Update particles
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var particle = _particles[i];
                particle.Lifetime -= deltaTime;
                particle.TotalTime += deltaTime;

                if (particle.Lifetime <= 0)
                {
                    _particles.RemoveAt(i);
                    continue;
                }

                Initial.OnParticleUpdate(ref particle, deltaTime);
                SizeOverLifetime.OnParticleUpdate(ref particle, deltaTime);
                ColorOverLifetime.OnParticleUpdate(ref particle, deltaTime);
                RotationOverLifetime.OnParticleUpdate(ref particle, deltaTime);
                VelocityOverLifetime.OnParticleUpdate(ref particle, deltaTime);
                UV.OnParticleUpdate(ref particle, deltaTime);

                particle.Position += particle.Velocity * deltaTime;
                _particles[i] = particle;
            }

            // Update collisions during prewarm
            if (Collision.Enabled)
            {
                Collision.UpdateCollisions(_particles, GameObject.Scene?.Physics, deltaTime, Transform, SimulationSpace);
            }

            elapsed += deltaTime;
        }
    }

    #endregion


    #region GPU Instancing

    private void CreateQuadMesh()
    {
        // Create a simple quad mesh for particle rendering
        // Vertices are in local space, centered at origin
        Float3[] vertices = new Float3[]
        {
            new Float3(-0.5f, -0.5f, 0),
            new Float3( 0.5f, -0.5f, 0),
            new Float3( 0.5f,  0.5f, 0),
            new Float3(-0.5f,  0.5f, 0),
        };

        Float2[] uvs = new Float2[]
        {
            new Float2(0, 0),
            new Float2(1, 0),
            new Float2(1, 1),
            new Float2(0, 1),
        };

        uint[] indices = new uint[] { 0, 1, 2, 0, 2, 3 };

        _quadMesh = new Mesh();
        _quadMesh.Vertices = vertices;
        _quadMesh.UV = uvs;
        _quadMesh.Indices = indices;
        _quadMesh.RecalculateBounds();
        _quadMesh.Upload();
    }

    private void UpdateInstanceData()
    {
        // Resize arrays if needed
        if (_transforms.Length != _particles.Count)
        {
            _transforms = new Float4x4[_particles.Count];
            _colors = new Float4[_particles.Count];
            _customData = new Float4[_particles.Count];
        }

        // Initialize bounds tracking
        Float3 min = new Float3(float.MaxValue);
        Float3 max = new Float3(float.MinValue);

        // Fill separate arrays from particles
        for (int i = 0; i < _particles.Count; i++)
        {
            var particle = _particles[i];

            // Create transform matrix for this particle
            Float3 position = particle.Position;
            float rotation = particle.Rotation;
            float size = particle.Size;

            // Transform to world space if in local simulation space
            if (SimulationSpace == SimulationSpace.Local)
            {
                var worldPos = Transform.LocalToWorldMatrix * new Float4(position, 1.0f);
                position = new Float3(worldPos.X, worldPos.Y, worldPos.Z);
            }

            // Update bounds - expand by particle size (radius = size * 0.5)
            float radius = size * 0.5f;
            Float3 particleMin = new Float3(position.X - radius, position.Y - radius, position.Z - radius);
            Float3 particleMax = new Float3(position.X + radius, position.Y + radius, position.Z + radius);
            min = new Float3(
                Maths.Min(min.X, particleMin.X),
                Maths.Min(min.Y, particleMin.Y),
                Maths.Min(min.Z, particleMin.Z)
            );
            max = new Float3(
                Maths.Max(max.X, particleMax.X),
                Maths.Max(max.Y, particleMax.Y),
                Maths.Max(max.Z, particleMax.Z)
            );

            // Build transformation matrix: Translation * Rotation * Scale
            // For billboarding, we'd want to face the camera, but for now use Z-axis rotation
            Float4x4 translation = Float4x4.CreateTranslation(position);

            // Create rotation matrix around Z axis manually
            float cos = (float)Maths.Cos(rotation);
            float sin = (float)Maths.Sin(rotation);
            Float4x4 rotationMat = new Float4x4(
                new Float4(cos, sin, 0, 0),
                new Float4(-sin, cos, 0, 0),
                new Float4(0, 0, 1, 0),
                new Float4(0, 0, 0, 1)
            );

            Float4x4 scale = Float4x4.CreateScale(size);
            _transforms[i] = translation * rotationMat * scale;

            // Get UV tile info if UV module is enabled
            Float4 uvTileInfo = UV.Enabled ? UV.GetUVTileInfo(particle) : new Float4(0, 0, 1, 1);

            // Store color and custom data
            _colors[i] = particle.Color;

            // CustomData: X=normalized lifetime, Y=UV offsetX, Z=UV offsetY, W=UV scale
            _customData[i] = new Float4(particle.NormalizedLifetime, uvTileInfo.X, uvTileInfo.Y, uvTileInfo.Z);
        }

        // Store computed bounds
        _bounds = _particles.Count > 0 ? new AABB(min, max) : new AABB(Float3.Zero, Float3.Zero);
    }

    #endregion
}

public enum SimulationSpace
{
    Local,
    World
}
