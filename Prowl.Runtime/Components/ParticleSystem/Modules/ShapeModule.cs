// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

public enum ParticleShapeType
{
    Sphere,
    Hemisphere,
    Cone,
    Box,
    Circle,
    Donut,
    Edge,
    Rectangle,
    Mesh
}

public enum ConeEmitFrom
{
    Base,
    Volume
}

public enum BoxEmitFrom
{
    Volume,
    Shell,
    Edge
}

public enum MeshEmitFrom
{
    Vertex,
    Triangle
}

/// <summary>
/// Where particles are born and which way they start moving. Shapes open along +Y: the cone and
/// hemisphere point up, the circle, donut and rectangle lie flat, the edge runs along X.
/// With the module off particles are born at the emitter origin moving up.
/// </summary>
[Serializable]
public class ShapeModule : ParticleSystemModule
{
    public ParticleShapeType Type = ParticleShapeType.Cone;

    [ShowIf(nameof(UsesRadius))]
    public float Radius = 1f;

    [ShowIf(nameof(UsesThickness)), Range(0f, 1f), Tooltip("0 emits from the surface only, 1 from the whole volume.")]
    public float RadiusThickness = 1f;

    [ShowIf(nameof(UsesArc)), Range(0f, 360f), Tooltip("Degrees of the circle that emit.")]
    public float Arc = 360f;

    [ShowIf(nameof(IsCone)), Range(0f, 90f), Tooltip("Degrees the edge of the cone leans out.")]
    public float Angle = 25f;

    [ShowIf(nameof(IsCone))]
    public ConeEmitFrom ConeEmitFrom = ConeEmitFrom.Base;

    [ShowIf(nameof(IsConeVolume)), Tooltip("How far up the cone volume particles can be born.")]
    public float Length = 5f;

    [ShowIf(nameof(IsBox))]
    public Float3 BoxSize = Float3.One;

    [ShowIf(nameof(IsBox))]
    public BoxEmitFrom BoxEmitFrom = BoxEmitFrom.Volume;

    [ShowIf(nameof(IsDonut)), Tooltip("Thickness of the ring.")]
    public float DonutRadius = 0.2f;

    [ShowIf(nameof(IsMesh))]
    public Mesh? Mesh;

    [ShowIf(nameof(IsMesh))]
    public MeshEmitFrom MeshEmitFrom = MeshEmitFrom.Triangle;

    [Header("Transform")]
    public Float3 Position = Float3.Zero;
    [Tooltip("Euler angles in degrees.")]
    public Float3 Rotation = Float3.Zero;
    public Float3 Scale = Float3.One;

    [Header("Direction")]
    [Tooltip("Turns particles to face their start direction. Shows on mesh particles and on billboards aligned to World or Local.")]
    public bool AlignToDirection = false;
    [Range(0f, 1f), Tooltip("Blends the start direction toward a random one.")]
    public float RandomizeDirection = 0f;
    [Range(0f, 1f), Tooltip("Blends the start direction toward pointing away from the shape center.")]
    public float SphericalizeDirection = 0f;
    [Tooltip("Moves each spawn point by up to this distance in a random direction.")]
    public float RandomizePosition = 0f;

    private bool UsesRadius => Type is ParticleShapeType.Sphere or ParticleShapeType.Hemisphere or ParticleShapeType.Cone or ParticleShapeType.Circle or ParticleShapeType.Donut or ParticleShapeType.Edge;
    private bool UsesThickness => Type is ParticleShapeType.Sphere or ParticleShapeType.Hemisphere or ParticleShapeType.Cone or ParticleShapeType.Circle or ParticleShapeType.Donut;
    private bool UsesArc => Type is ParticleShapeType.Cone or ParticleShapeType.Circle or ParticleShapeType.Donut;
    private bool IsCone => Type == ParticleShapeType.Cone;
    private bool IsConeVolume => IsCone && ConeEmitFrom == ConeEmitFrom.Volume;
    private bool IsBox => Type == ParticleShapeType.Box;
    private bool IsDonut => Type == ParticleShapeType.Donut;
    private bool IsMesh => Type == ParticleShapeType.Mesh;

    // Area weighted triangle table for mesh emission, rebuilt when the mesh changes.
    private Mesh? _cachedMesh;
    private uint _cachedMeshVersion;
    private float[] _triangleCdf = Array.Empty<float>();

    public ShapeModule() => Enabled = true;

    /// <summary>The shape's own placement inside the emitter.</summary>
    public Float4x4 ShapeMatrix => Float4x4.CreateTRS(Position, Quaternion.FromEuler(Rotation), Scale);

    /// <summary>Picks a spawn point and start direction in emitter space.</summary>
    internal void Sample(System.Random random, out Float3 position, out Float3 direction)
    {
        if (!Enabled)
        {
            position = Float3.Zero;
            direction = Float3.UnitY;
            return;
        }

        SampleLocal(random, out position, out direction);

        if (RandomizePosition > 0f)
            position += ParticleRandom.InUnitSphere(random) * RandomizePosition;

        if (SphericalizeDirection > 0f)
        {
            Float3 outward = Float3.NormalizeSafe(position, direction);
            direction = Float3.NormalizeSafe(Maths.Lerp(direction, outward, SphericalizeDirection), direction);
        }
        if (RandomizeDirection > 0f)
            direction = Float3.NormalizeSafe(Maths.Lerp(direction, ParticleRandom.OnUnitSphere(random), RandomizeDirection), direction);

        if (Position == Float3.Zero && Rotation == Float3.Zero && Scale == Float3.One)
            return;

        Float4x4 m = ShapeMatrix;
        position = (m * new Float4(position, 1f)).XYZ;
        direction = Float3.NormalizeSafe((m * new Float4(direction, 0f)).XYZ, Float3.UnitY);
    }

    private void SampleLocal(System.Random random, out Float3 position, out Float3 direction)
    {
        float radius = MathF.Max(0f, Radius);
        switch (Type)
        {
            case ParticleShapeType.Sphere:
            {
                direction = ParticleRandom.OnUnitSphere(random);
                position = direction * VolumeRadius(radius, 3f, random);
                return;
            }
            case ParticleShapeType.Hemisphere:
            {
                direction = ParticleRandom.OnUnitSphere(random);
                if (direction.Y < 0f) direction.Y = -direction.Y;
                position = direction * VolumeRadius(radius, 3f, random);
                return;
            }
            case ParticleShapeType.Circle:
            {
                float a = ArcAngle(random);
                direction = new Float3(MathF.Cos(a), 0f, MathF.Sin(a));
                position = direction * VolumeRadius(radius, 2f, random);
                return;
            }
            case ParticleShapeType.Cone:
            {
                float a = ArcAngle(random);
                Float3 radial = new(MathF.Cos(a), 0f, MathF.Sin(a));
                float r = VolumeRadius(radius, 2f, random);
                // Directions lean out in proportion to how far from the axis a particle starts, so the
                // spray matches the cone's walls. A point cone picks a random lean instead.
                float lean = radius > 1e-4f ? r / radius : MathF.Sqrt(random.NextSingle());
                float tilt = Maths.Clamp(Angle, 0f, 90f) * Maths.Deg2Rad * lean;
                direction = radial * MathF.Sin(tilt) + Float3.UnitY * MathF.Cos(tilt);
                position = radial * r;
                if (ConeEmitFrom == ConeEmitFrom.Volume)
                    position += direction * (random.NextSingle() * MathF.Max(0f, Length));
                return;
            }
            case ParticleShapeType.Donut:
            {
                float a = ArcAngle(random);
                Float3 radial = new(MathF.Cos(a), 0f, MathF.Sin(a));
                float tube = random.NextSingle() * MathF.PI * 2f;
                Float3 tubeDir = radial * MathF.Cos(tube) + Float3.UnitY * MathF.Sin(tube);
                direction = tubeDir;
                position = radial * radius + tubeDir * VolumeRadius(MathF.Max(0f, DonutRadius), 2f, random);
                return;
            }
            case ParticleShapeType.Box:
                SampleBox(random, out position);
                direction = Float3.UnitY;
                return;
            case ParticleShapeType.Edge:
                position = new Float3((random.NextSingle() * 2f - 1f) * radius, 0f, 0f);
                direction = Float3.UnitY;
                return;
            case ParticleShapeType.Rectangle:
                position = new Float3(random.NextSingle() - 0.5f, 0f, random.NextSingle() - 0.5f);
                direction = Float3.UnitY;
                return;
            case ParticleShapeType.Mesh:
                if (SampleMesh(random, out position, out direction))
                    return;
                break;
        }

        position = Float3.Zero;
        direction = Float3.UnitY;
    }

    // Uniform over a disc (power 2) or ball (power 3) between the inner shell and the radius.
    private float VolumeRadius(float radius, float power, System.Random random)
    {
        float inner = MathF.Pow(1f - Maths.Saturate(RadiusThickness), power);
        return radius * MathF.Pow(Maths.LerpUnclamped(inner, 1f, random.NextSingle()), 1f / power);
    }

    private float ArcAngle(System.Random random) => random.NextSingle() * Maths.Clamp(Arc, 0f, 360f) * Maths.Deg2Rad;

    private void SampleBox(System.Random random, out Float3 position)
    {
        Float3 h = BoxSize * 0.5f;
        Float3 p = new(random.NextSingle() * 2f - 1f, random.NextSingle() * 2f - 1f, random.NextSingle() * 2f - 1f);

        if (BoxEmitFrom == BoxEmitFrom.Shell)
        {
            // Pick a face weighted by its area, then push the point onto it.
            float xy = MathF.Abs(h.X * h.Y), yz = MathF.Abs(h.Y * h.Z), xz = MathF.Abs(h.X * h.Z);
            float pick = random.NextSingle() * (xy + yz + xz);
            float side = random.NextSingle() < 0.5f ? -1f : 1f;
            if (pick < xy) p.Z = side;
            else if (pick < xy + yz) p.X = side;
            else p.Y = side;
        }
        else if (BoxEmitFrom == BoxEmitFrom.Edge)
        {
            // Pick an axis weighted by edge length, the other two coordinates snap to corners.
            float x = MathF.Abs(h.X), y = MathF.Abs(h.Y), z = MathF.Abs(h.Z);
            float pick = random.NextSingle() * (x + y + z);
            float s1 = random.NextSingle() < 0.5f ? -1f : 1f;
            float s2 = random.NextSingle() < 0.5f ? -1f : 1f;
            if (pick < x) { p.Y = s1; p.Z = s2; }
            else if (pick < x + y) { p.X = s1; p.Z = s2; }
            else { p.X = s1; p.Y = s2; }
        }

        position = p * h;
    }

    private bool SampleMesh(System.Random random, out Float3 position, out Float3 direction)
    {
        position = Float3.Zero;
        direction = Float3.UnitY;

        Mesh? mesh = Mesh;
        if (mesh.IsNotValid() || !mesh.isReadable) return false;

        Float3[] vertices = mesh.Vertices;
        if (vertices.Length == 0) return false;
        Float3[] normals = mesh.Normals;
        bool hasNormals = normals.Length == vertices.Length;

        if (MeshEmitFrom == MeshEmitFrom.Vertex)
        {
            int v = random.Next(vertices.Length);
            position = vertices[v];
            direction = hasNormals ? Float3.NormalizeSafe(normals[v], Float3.UnitY) : Float3.UnitY;
            return true;
        }

        uint[] indices = mesh.Indices;
        if (indices.Length < 3) return false;
        EnsureTriangleTable(mesh, vertices, indices);
        if (_triangleCdf.Length == 0) return false;

        float total = _triangleCdf[^1];
        int tri = Array.BinarySearch(_triangleCdf, random.NextSingle() * total);
        if (tri < 0) tri = ~tri;
        tri = Math.Min(tri, _triangleCdf.Length - 1);

        int i0 = (int)indices[tri * 3], i1 = (int)indices[tri * 3 + 1], i2 = (int)indices[tri * 3 + 2];
        float u = random.NextSingle(), w = random.NextSingle();
        if (u + w > 1f) { u = 1f - u; w = 1f - w; }
        float b0 = 1f - u - w;

        position = vertices[i0] * b0 + vertices[i1] * u + vertices[i2] * w;
        Float3 face = Float3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
        direction = hasNormals
            ? Float3.NormalizeSafe(normals[i0] * b0 + normals[i1] * u + normals[i2] * w, Float3.UnitY)
            : Float3.NormalizeSafe(face, Float3.UnitY);
        return true;
    }

    private void EnsureTriangleTable(Mesh mesh, Float3[] vertices, uint[] indices)
    {
        if (ReferenceEquals(mesh, _cachedMesh) && mesh.Version == _cachedMeshVersion && _triangleCdf.Length > 0)
            return;

        int triangles = indices.Length / 3;
        _triangleCdf = new float[triangles];
        float sum = 0f;
        for (int t = 0; t < triangles; t++)
        {
            Float3 a = vertices[indices[t * 3]], b = vertices[indices[t * 3 + 1]], c = vertices[indices[t * 3 + 2]];
            sum += Float3.Length(Float3.Cross(b - a, c - a)) * 0.5f;
            _triangleCdf[t] = sum;
        }
        _cachedMesh = mesh;
        _cachedMeshVersion = mesh.Version;
    }

    /// <summary>Where particles can be born, in the emitter's space. With the module off it is the origin.</summary>
    internal AABB LocalBounds()
    {
        if (!Enabled) return new AABB(Float3.Zero, Float3.Zero);

        float r = MathF.Max(0f, Radius);
        Float3 extent = Type switch
        {
            ParticleShapeType.Sphere => new Float3(r),
            ParticleShapeType.Hemisphere => new Float3(r),
            ParticleShapeType.Circle => new Float3(r, 0f, r),
            ParticleShapeType.Donut => new Float3(r + DonutRadius, DonutRadius, r + DonutRadius),
            ParticleShapeType.Cone => ConeEmitFrom == ConeEmitFrom.Volume
                ? new Float3(r + MathF.Max(0f, Length), MathF.Max(0f, Length), r + MathF.Max(0f, Length))
                : new Float3(r, 0f, r),
            ParticleShapeType.Box => Maths.Abs(BoxSize) * 0.5f,
            ParticleShapeType.Edge => new Float3(r, 0f, 0f),
            ParticleShapeType.Rectangle => new Float3(0.5f, 0f, 0.5f),
            _ => Float3.Zero,
        };

        AABB local = Type == ParticleShapeType.Mesh && Mesh.IsValid()
            ? Mesh.bounds
            : new AABB(-extent, extent);
        local = new AABB(local.Min - new Float3(RandomizePosition), local.Max + new Float3(RandomizePosition));
        return local.TransformBy(ShapeMatrix);
    }

    /// <summary>Draws the emission shape as a wireframe, <paramref name="emitterToWorld"/> places the emitter.</summary>
    internal void DrawGizmo(Float4x4 emitterToWorld, Color color)
    {
        Float4x4 m = emitterToWorld * ShapeMatrix;
        float r = MathF.Max(0f, Radius);
        switch (Type)
        {
            case ParticleShapeType.Sphere:
                DrawRing(m, Float3.Zero, r, Axis.XZ, 360f, color);
                DrawRing(m, Float3.Zero, r, Axis.XY, 360f, color);
                DrawRing(m, Float3.Zero, r, Axis.YZ, 360f, color);
                break;
            case ParticleShapeType.Hemisphere:
                DrawRing(m, Float3.Zero, r, Axis.XZ, 360f, color);
                DrawRing(m, Float3.Zero, r, Axis.XY, 180f, color);
                DrawRing(m, Float3.Zero, r, Axis.YZ, 180f, color);
                break;
            case ParticleShapeType.Circle:
                DrawRing(m, Float3.Zero, r, Axis.XZ, Arc, color);
                break;
            case ParticleShapeType.Donut:
                DrawRing(m, Float3.Zero, r + DonutRadius, Axis.XZ, Arc, color);
                DrawRing(m, Float3.Zero, MathF.Max(0f, r - DonutRadius), Axis.XZ, Arc, color);
                break;
            case ParticleShapeType.Cone:
            {
                float length = ConeEmitFrom == ConeEmitFrom.Volume ? MathF.Max(Length, 0.01f) : 1f;
                float angle = Maths.Clamp(Angle, 0f, 90f) * Maths.Deg2Rad;
                float top = r + MathF.Sin(angle) * length;
                float height = MathF.Cos(angle) * length;
                DrawRing(m, Float3.Zero, r, Axis.XZ, Arc, color);
                DrawRing(m, new Float3(0f, height, 0f), top, Axis.XZ, Arc, color);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * MathF.PI * 0.5f;
                    Float3 dir = new(MathF.Cos(a), 0f, MathF.Sin(a));
                    Debug.DrawLine(Point(m, dir * r), Point(m, dir * top + new Float3(0f, height, 0f)), color);
                }
                break;
            }
            case ParticleShapeType.Box:
                DrawBox(m, BoxSize * 0.5f, color);
                break;
            case ParticleShapeType.Edge:
                Debug.DrawLine(Point(m, new Float3(-r, 0f, 0f)), Point(m, new Float3(r, 0f, 0f)), color);
                break;
            case ParticleShapeType.Rectangle:
                DrawBox(m, new Float3(0.5f, 0f, 0.5f), color);
                break;
            case ParticleShapeType.Mesh:
                if (Mesh.IsValid())
                    DrawBox(m * Float4x4.CreateTranslation(Mesh.bounds.Center), Mesh.bounds.Extents, color);
                break;
        }
    }

    private enum Axis { XZ, XY, YZ }

    private static Float3 Point(in Float4x4 m, Float3 p) => (m * new Float4(p, 1f)).XYZ;

    private static void DrawRing(in Float4x4 m, Float3 center, float radius, Axis axis, float arcDegrees, Color color)
    {
        const int segments = 32;
        float arc = Maths.Clamp(arcDegrees, 0f, 360f) * Maths.Deg2Rad;
        Float3 prev = default;
        for (int i = 0; i <= segments; i++)
        {
            float a = arc * i / segments;
            float c = MathF.Cos(a) * radius, s = MathF.Sin(a) * radius;
            Float3 local = axis switch
            {
                Axis.XZ => new Float3(c, 0f, s),
                Axis.XY => new Float3(c, s, 0f),
                _ => new Float3(0f, s, c)
            };
            Float3 world = Point(m, center + local);
            if (i > 0) Debug.DrawLine(prev, world, color);
            prev = world;
        }
    }

    private static void DrawBox(in Float4x4 m, Float3 h, Color color)
    {
        Span<Float3> c = stackalloc Float3[8];
        for (int i = 0; i < 8; i++)
            c[i] = Point(m, new Float3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z));
        for (int i = 0; i < 8; i++)
        {
            if ((i & 1) == 0) Debug.DrawLine(c[i], c[i | 1], color);
            if ((i & 2) == 0) Debug.DrawLine(c[i], c[i | 2], color);
            if ((i & 4) == 0) Debug.DrawLine(c[i], c[i | 4], color);
        }
    }
}
