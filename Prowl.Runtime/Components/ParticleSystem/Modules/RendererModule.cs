// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

public enum ParticleRenderMode
{
    /// <summary>Quads facing the camera.</summary>
    Billboard,
    /// <summary>Quads facing the camera, stretched along the direction of travel.</summary>
    StretchedBillboard,
    /// <summary>Quads lying flat, facing up.</summary>
    HorizontalBillboard,
    /// <summary>Upright quads that turn around the world Y axis to face the camera.</summary>
    VerticalBillboard,
    /// <summary>A mesh per particle, using the particle's full 3D rotation and size.</summary>
    Mesh
}

public enum ParticleRenderAlignment
{
    /// <summary>Parallel to the camera's view plane.</summary>
    View,
    /// <summary>Turned toward the camera's position.</summary>
    Facing,
    /// <summary>Uses the particle's rotation in world axes.</summary>
    World,
    /// <summary>Uses the particle's rotation relative to the emitter.</summary>
    Local,
    /// <summary>Faces the direction of travel.</summary>
    Velocity
}

public enum ParticleSortMode
{
    None,
    /// <summary>Farthest first, the right order for alpha blending.</summary>
    ByDistance,
    OldestInFront,
    YoungestInFront
}

public enum ParticleBlendMode
{
    Alpha,
    Additive,
    /// <summary>For textures whose color is already multiplied by their alpha.</summary>
    Premultiplied
}

/// <summary>
/// How particles are drawn. The shading options (blend, lighting, soft particles, camera fade) are read
/// by the built in particle shader. Any other material still gets correctly oriented instances, since
/// billboarding is baked into each instance's matrix.
/// </summary>
[Serializable]
public class RendererModule : ParticleSystemModule
{
    [Tooltip("Empty uses the built in particle material.")]
    public Material? Material;

    [Tooltip("Material for trails. Empty uses the particle material.")]
    public Material? TrailMaterial;

    public ParticleRenderMode RenderMode = ParticleRenderMode.Billboard;

    [ShowIf(nameof(IsMesh))]
    public Mesh? Mesh;

    [ShowIf(nameof(UsesAlignment))]
    public ParticleRenderAlignment Alignment = ParticleRenderAlignment.View;

    [ShowIf(nameof(IsStretched)), Tooltip("Length as a multiple of the particle's size.")]
    public float LengthScale = 2f;

    [ShowIf(nameof(IsStretched)), Tooltip("Extra length per unit of speed.")]
    public float VelocityScale = 0f;

    public ParticleSortMode SortMode = ParticleSortMode.ByDistance;

    [Tooltip("Moves the whole system in the transparent draw order. Negative draws it later, in front.")]
    public float SortingFudge = 0f;

    [Header("Shading")]
    public ParticleBlendMode BlendMode = ParticleBlendMode.Alpha;

    [Tooltip("Shade particles with the scene's lights and ambient.")]
    public bool Lit = false;

    [Tooltip("Fades particles out as they get this close to opaque geometry behind them. 0 turns it off.")]
    public float SoftParticleDistance = 0f;

    [Tooltip("Particles are invisible nearer than this distance from the camera.")]
    public float CameraFadeStart = 0f;

    [Tooltip("Particles are fully visible past this distance. At or below the start turns the fade off.")]
    public float CameraFadeEnd = 0f;

    [Header("Size")]
    [Range(0f, 1f), Tooltip("Smallest size, as a share of the screen height.")]
    public float MinParticleSize = 0f;

    [Range(0f, 1f), Tooltip("Largest size, as a share of the screen height.")]
    public float MaxParticleSize = 0.5f;

    [Tooltip("Offset of the particle's center, in multiples of its size.")]
    public Float3 Pivot = Float3.Zero;

    [Range(0f, 1f), Tooltip("Chance a particle's texture is mirrored left to right.")]
    public float FlipU = 0f;

    [Range(0f, 1f), Tooltip("Chance a particle's texture is mirrored top to bottom.")]
    public float FlipV = 0f;

    private bool IsMesh => RenderMode == ParticleRenderMode.Mesh;
    private bool IsStretched => RenderMode == ParticleRenderMode.StretchedBillboard;
    private bool UsesAlignment => RenderMode is ParticleRenderMode.Billboard or ParticleRenderMode.Mesh;

    private static Mesh? s_quad;
    private static Material? s_defaultMaterial;

    private readonly ParticleRenderable _particles = new();
    private readonly ParticleRenderable _trails = new();
    private InstanceData[] _trailData = Array.Empty<InstanceData>();
    private AABB _trailBounds;
    private bool _hasTrailBounds;
    private long _trailFrame = -1;

    private float[] _sortKeys = Array.Empty<float>();
    private int[] _sortIndices = Array.Empty<int>();
    private Float3[] _worldPositions = Array.Empty<Float3>();

    public RendererModule() => Enabled = true;

    /// <summary>The unit quad billboards are drawn with, facing +Z, with normals for lit shading.</summary>
    internal static Mesh Quad
    {
        get
        {
            if (s_quad.IsValid()) return s_quad;
            s_quad = new Mesh
            {
                Name = "Particle Quad",
                Vertices = [new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f)],
            };
            s_quad.UV = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];
            s_quad.Normals = [Float3.UnitZ, Float3.UnitZ, Float3.UnitZ, Float3.UnitZ];
            s_quad.Indices = [0, 1, 2, 0, 2, 3];
            s_quad.RecalculateBounds();
            return s_quad;
        }
    }

    private Material? ResolveMaterial(Material? material)
    {
        if (material.IsValid()) return material;
        if (Material.IsValid()) return Material;
        if (s_defaultMaterial.IsNotValid())
            s_defaultMaterial = BuiltInAssets.Load<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Particle));
        return s_defaultMaterial;
    }

    /// <summary>Builds this camera's draw data. Returns true when any of it is in view.</summary>
    internal bool Collect(ParticleSystemComponent system, Camera camera, List<IRenderable> renderables, TextureSheetAnimationModule sheet, TrailModule trails, float time)
    {
        Float4x4 view = camera.ViewMatrix;
        Float4x4 projection = camera.ProjectionMatrix;
        Frustum frustum = Frustum.FromMatrix(projection * view);
        Float3 cameraPosition = camera.Transform.Position;
        Float3 cameraForward = camera.Transform.Forward;
        bool visible = false;

        if (system.ParticleCount > 0 && system.HasBounds && frustum.Intersects(system.WorldBounds))
        {
            visible = true;
            Material? material = ResolveMaterial(null);
            if (material != null)
            {
                Mesh mesh = IsMesh && Mesh.IsValid() ? Mesh : Quad;
                int count = BuildParticles(system, view, projection, cameraPosition, cameraForward);
                ConfigureProperties(_particles.Properties, system, 0, sheet.ShaderParams);
                _particles.Set(mesh, material, count, system.WorldBounds, system.GameObject.LayerIndex, cameraForward * SortingFudge);
                renderables.Add(_particles);
            }
        }

        if (trails.Enabled || trails.HasOrphans)
        {
            // Trail geometry is camera independent, the shader turns it to face each camera.
            if (_trailFrame != Time.FrameCount)
            {
                _trailFrame = Time.FrameCount;
                _hasTrailBounds = false;
                _trails.Count = trails.BuildSegments(system, time, ref _trailData, ref _trailBounds, ref _hasTrailBounds);
            }

            if (_trails.Count > 0 && _hasTrailBounds && frustum.Intersects(_trailBounds))
            {
                visible = true;
                Material? material = ResolveMaterial(TrailMaterial);
                if (material != null)
                {
                    ConfigureProperties(_trails.Properties, system, 1, new Float4(1f, 1f, 1f, 0f));
                    _trails.Data = _trailData;
                    _trails.Set(Quad, material, _trails.Count, _trailBounds, system.GameObject.LayerIndex, cameraForward * SortingFudge);
                    renderables.Add(_trails);
                }
            }
        }

        return visible;
    }

    private void ConfigureProperties(PropertyState properties, ParticleSystemComponent system, int mode, Float4 sheet)
    {
        properties.Clear();
        properties.SetInt("_ObjectID", system.InstanceID);
        properties.SetInt("_ParticleMode", mode);
        properties.SetVector("_ParticleSheet", sheet);
        properties.SetInt("_ParticleBlend", (int)BlendMode);
        properties.SetInt("_ParticleLit", Lit ? 1 : 0);
        properties.SetFloat("_ParticleSoft", MathF.Max(0f, SoftParticleDistance));
        properties.SetVector("_ParticleFade", new Float2(CameraFadeStart, CameraFadeEnd));
    }

    private int BuildParticles(ParticleSystemComponent system, Float4x4 view, Float4x4 projection, Float3 cameraPosition, Float3 cameraForward)
    {
        ReadOnlySpan<Particle> particles = system.Particles;
        int count = particles.Length;
        EnsureCapacity(count);

        for (int i = 0; i < count; i++)
        {
            _worldPositions[i] = system.SimPointToWorld(particles[i].Position);
            _sortIndices[i] = i;
            _sortKeys[i] = SortMode switch
            {
                ParticleSortMode.ByDistance => -Float3.LengthSquared(_worldPositions[i] - cameraPosition),
                ParticleSortMode.OldestInFront => particles[i].Age,
                ParticleSortMode.YoungestInFront => -particles[i].Age,
                _ => 0f
            };
        }
        if (SortMode != ParticleSortMode.None)
            Array.Sort(_sortKeys, _sortIndices, 0, count);

        // The view matrix rows are the camera's screen axes.
        Float3 cameraRight = new(view.c0.X, view.c1.X, view.c2.X);
        Float3 cameraUp = new(view.c0.Y, view.c1.Y, view.c2.Y);
        Float3 towardCamera = -cameraForward;
        bool orthographic = projection.c3.W > 0.5f;
        float screenScale = 2f / MathF.Max(MathF.Abs(projection.c1.Y), 1e-6f);
        bool clampSize = MinParticleSize > 0f || MaxParticleSize < 1f;
        Quaternion emitterRotation = system.Transform.Rotation;
        float sizeScale = system.SizeScale;

        InstanceData[] data = _particles.Data;
        for (int n = 0; n < count; n++)
        {
            int i = _sortIndices[n];
            ref readonly Particle p = ref particles[i];
            Float3 position = _worldPositions[i];
            Float3 size = p.Size * sizeScale;

            if (clampSize && RenderMode != ParticleRenderMode.Mesh)
            {
                float depth = orthographic ? 1f : MathF.Max(Float3.Dot(position - cameraPosition, cameraForward), 1e-4f);
                float screenHeight = screenScale * depth;
                float largest = MathF.Max(MathF.Abs(size.X), MathF.Abs(size.Y));
                if (largest > 0f)
                {
                    float clamped = Maths.Clamp(largest, MinParticleSize * screenHeight, MathF.Max(MinParticleSize, MaxParticleSize) * screenHeight);
                    size *= clamped / largest;
                }
            }

            Float4x4 matrix = RenderMode == ParticleRenderMode.Mesh
                ? MeshMatrix(system, in p, position, size, emitterRotation, cameraForward, cameraRight, cameraUp)
                : BillboardMatrix(system, in p, position, size, cameraPosition, cameraRight, cameraUp, towardCamera);

            data[n] = new InstanceData(matrix, p.Color, new Float4(p.UVFrame, p.NormalizedAge, p.CustomData.X, p.CustomData.Y));
        }
        return count;
    }

    private Float4x4 BillboardMatrix(ParticleSystemComponent system, in Particle p, Float3 position, Float3 size,
        Float3 cameraPosition, Float3 cameraRight, Float3 cameraUp, Float3 towardCamera)
    {
        float angle = p.Rotation.Z * Maths.Deg2Rad;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        Float3 right, up, normal;
        float width = size.X, height = size.Y;

        switch (RenderMode)
        {
            case ParticleRenderMode.StretchedBillboard:
            {
                Float3 velocity = system.SimVectorToWorld(p.TotalVelocity);
                float speed = Float3.Length(velocity);
                Float3 facing = Float3.NormalizeSafe(cameraPosition - position, towardCamera);
                Float3 side = Float3.Cross(velocity, facing);
                float sideLength = Float3.Length(side);
                if (speed < 1e-5f || sideLength < 1e-6f)
                    break;

                up = velocity / speed;
                right = side / sideLength;
                normal = facing;
                height = height * LengthScale + speed * VelocityScale;
                return Compose(position, right, up, normal, width, height, size, p);
            }
            case ParticleRenderMode.HorizontalBillboard:
                normal = Float3.UnitY;
                right = new Float3(cos, 0f, -sin);
                up = new Float3(sin, 0f, cos);
                return Compose(position, right, up, normal, width, height, size, p);
            case ParticleRenderMode.VerticalBillboard:
            {
                Float3 flat = new(cameraPosition.X - position.X, 0f, cameraPosition.Z - position.Z);
                normal = Float3.NormalizeSafe(flat, Float3.NormalizeSafe(new Float3(towardCamera.X, 0f, towardCamera.Z), Float3.UnitZ));
                Float3 flatRight = Float3.NormalizeSafe(new Float3(cameraRight.X, 0f, cameraRight.Z), Float3.Cross(Float3.UnitY, normal));
                right = flatRight * cos + Float3.UnitY * sin;
                up = Float3.UnitY * cos - flatRight * sin;
                return Compose(position, right, up, normal, width, height, size, p);
            }
        }

        switch (Alignment)
        {
            case ParticleRenderAlignment.World:
            case ParticleRenderAlignment.Local:
            {
                Quaternion q = Quaternion.FromEuler(p.Rotation);
                if (Alignment == ParticleRenderAlignment.Local)
                    q = system.Transform.Rotation * q;
                return Compose(position, q * Float3.UnitX, q * Float3.UnitY, q * Float3.UnitZ, width, height, size, p);
            }
            case ParticleRenderAlignment.Facing:
                normal = Float3.NormalizeSafe(cameraPosition - position, towardCamera);
                break;
            case ParticleRenderAlignment.Velocity:
                normal = Float3.NormalizeSafe(system.SimVectorToWorld(p.TotalVelocity), towardCamera);
                break;
            default:
                normal = towardCamera;
                break;
        }

        // Keep the camera's screen axes, bent onto the chosen plane, so textures stay upright.
        Float3 baseRight = Float3.NormalizeSafe(cameraRight - normal * Float3.Dot(cameraRight, normal), Float3.Cross(cameraUp, normal));
        Float3 baseUp = Float3.NormalizeSafe(Float3.Cross(normal, baseRight), cameraUp);
        if (Float3.Dot(baseUp, cameraUp) < 0f) baseUp = -baseUp;
        right = baseRight * cos + baseUp * sin;
        up = baseUp * cos - baseRight * sin;
        return Compose(position, right, up, normal, width, height, size, p);
    }

    private Float4x4 Compose(Float3 position, Float3 right, Float3 up, Float3 normal, float width, float height, Float3 size, in Particle p)
    {
        if (FlipU > 0f && p.Random(0x161) < FlipU) width = -width;
        if (FlipV > 0f && p.Random(0x162) < FlipV) height = -height;

        Float3 center = position + right * (Pivot.X * width) + up * (Pivot.Y * height) + normal * (Pivot.Z * size.Z);
        return new Float4x4(
            new Float4(right * width, 0f),
            new Float4(up * height, 0f),
            new Float4(normal, 0f),
            new Float4(center, 1f));
    }

    private Float4x4 MeshMatrix(ParticleSystemComponent system, in Particle p, Float3 position, Float3 size, Quaternion emitterRotation,
        Float3 cameraForward, Float3 cameraRight, Float3 cameraUp)
    {
        Quaternion q = Quaternion.FromEuler(p.Rotation);
        switch (Alignment)
        {
            case ParticleRenderAlignment.Local:
                q = emitterRotation * q;
                break;
            case ParticleRenderAlignment.Velocity:
            {
                Float3 velocity = system.SimVectorToWorld(p.TotalVelocity);
                if (Float3.LengthSquared(velocity) > 1e-10f)
                    q = Quaternion.LookRotation(Float3.Normalize(velocity), Float3.UnitY) * q;
                break;
            }
            case ParticleRenderAlignment.View:
            case ParticleRenderAlignment.Facing:
                q = Quaternion.LookRotation(cameraForward, cameraUp) * q;
                break;
        }

        Float3 offset = q * (Pivot * size);
        return Float4x4.CreateTRS(position + offset, q, size);
    }

    private void EnsureCapacity(int count)
    {
        if (_sortKeys.Length >= count) return;
        int size = Math.Max(count, _sortKeys.Length * 2);
        _sortKeys = new float[size];
        _sortIndices = new int[size];
        _worldPositions = new Float3[size];
        _particles.Data = new InstanceData[size];
    }

    /// <summary>A reused renderable over a pooled instance array. Only the first <see cref="Count"/> entries draw.</summary>
    private sealed class ParticleRenderable : IRenderable
    {
        public InstanceData[] Data = Array.Empty<InstanceData>();
        public int Count;
        public readonly PropertyState Properties = new();

        private Mesh _mesh = null!;
        private Material _material = null!;
        private AABB _bounds;
        private int _layer;
        private Float3 _sortOffset;

        public void Set(Mesh mesh, Material material, int count, AABB bounds, int layer, Float3 sortOffset)
        {
            _mesh = mesh;
            _material = material;
            Count = count;
            _bounds = bounds;
            _layer = layer;
            _sortOffset = sortOffset;
        }

        public Material GetMaterial() => _material;
        public int GetLayer() => _layer;
        public Float3 GetPosition() => _bounds.Center + _sortOffset;
        public int GetInstanceCount(InstanceData[] instanceData) => Count;

        public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
        {
            properties = Properties;
            mesh = _mesh;
            model = Float4x4.Identity;
            instanceData = Data;
        }

        public void GetCullingData(out bool isRenderable, out AABB bounds)
        {
            isRenderable = Count > 0 && _mesh != null && _material != null;
            bounds = _bounds;
        }
    }
}
