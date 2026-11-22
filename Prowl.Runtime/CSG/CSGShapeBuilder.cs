// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>Which shape a generator produces.</summary>
public enum CSGGeneratorKind
{
    Box,
    Cylinder,
    Sphere,
    LinearStairs,
    SpiralStairs,
    /// <summary>A hollow box: walls, floor and ceiling of <see cref="CSGGeneratorSettings.WallThickness"/>.</summary>
    Room,
}

/// <summary>Settings shared by the generators. Each uses the ones that apply to it.</summary>
public struct CSGGeneratorSettings
{
    /// <summary>Sides around a cylinder, or segments around a sphere.</summary>
    public int Sides;

    /// <summary>Horizontal slices of a sphere.</summary>
    public int Rings;

    /// <summary>Rotation of a cylinder's first side, in degrees.</summary>
    public float OffsetAngle;

    /// <summary>Measure the radius to a corner rather than to the middle of a side.</summary>
    public bool RadiusToCorner;

    /// <summary>Build only the upper half of a sphere.</summary>
    public bool Hemisphere;

    /// <summary>Height of one step. Steps are fitted to the drawn height.</summary>
    public float StepHeight;

    /// <summary>Depth of one step going up. Steps are fitted to the drawn depth.</summary>
    public float StepDepth;

    /// <summary>Fill under a staircase instead of leaving floating steps.</summary>
    public bool FilledStairs;

    /// <summary>Steps in one full turn of a spiral.</summary>
    public int StepsPerRevolution;

    /// <summary>Fraction of the radius left open in the middle of a spiral.</summary>
    public float InnerRadius;

    /// <summary>Put the curved faces in one smoothing group, so a cylinder reads as round.</summary>
    public bool SmoothShading;

    /// <summary>Centre the shape on its own bounds rather than on the circle it was built from.</summary>
    public bool Recentre;

    /// <summary>Exact number of steps, or 0 to derive it from the step size.</summary>
    public int TotalSteps;

    /// <summary>Extra depth on the top step, for a landing.</summary>
    public float ExtraDepth;

    /// <summary>Extra height under the whole flight, to lift it off the floor.</summary>
    public float ExtraHeight;

    /// <summary>Thickness of a room's walls, floor and ceiling.</summary>
    public float WallThickness;

    public static CSGGeneratorSettings Default => new()
    {
        Sides = 12,
        Rings = 6,
        StepHeight = 0.25f,
        StepDepth = 0.5f,
        FilledStairs = true,
        StepsPerRevolution = 12,
        InnerRadius = 0.2f,
        SmoothShading = true,
        WallThickness = 0.25f,
    };
}

/// <summary>One brush of a generated shape: its planes, and where it sits in the drawn bounds.</summary>
public readonly struct CSGShapePart
{
    public readonly Plane[] Planes;
    public readonly Float3 Offset;

    /// <summary>How many leading planes are the curved surface, smooth shading applies to <c>[0, SmoothPlanes)</c>.</summary>
    public readonly int SmoothPlanes;

    /// <summary>What the brush does inside the shape's group, as a room's inside carving out its solid.</summary>
    public readonly CSGOperation Operation;

    public CSGShapePart(Plane[] planes, Float3 offset, int smoothPlanes = 0, CSGOperation operation = CSGOperation.Additive)
    {
        Planes = planes;
        Offset = offset;
        SmoothPlanes = smoothPlanes;
        Operation = operation;
    }
}

/// <summary>Builds the plane sets a generator produces to fill a drawn box. Shapes are authored at unit size and scaled into the bounds.</summary>
public static class CSGShapeBuilder
{
    /// <summary>The brushes filling <paramref name="size"/>, more than one when the shape is not convex.</summary>
    public static List<CSGShapePart> Build(CSGGeneratorKind kind, Float3 size, CSGGeneratorSettings settings)
    {
        Float3 half = Maths.Max(Maths.Abs(size) * 0.5f, 1e-4f);

        switch (kind)
        {
            case CSGGeneratorKind.Cylinder:
            {
                Plane[] planes = Scale(UnitCylinder(settings, out int sides), half);
                return Single(planes, settings.SmoothShading ? sides : 0);
            }

            case CSGGeneratorKind.Sphere:
            {
                Plane[] planes = Scale(UnitSphere(settings, out int curved), half);
                return Single(planes, settings.SmoothShading ? curved : 0);
            }

            case CSGGeneratorKind.LinearStairs: return LinearStairs(half * 2f, settings);
            case CSGGeneratorKind.SpiralStairs: return SpiralStairs(half, settings);
            case CSGGeneratorKind.Room: return Room(half * 2f, settings);
            default: return Single(GeometryGenerator.BoxPlanes(half * 2f), 0);
        }
    }

    /// <summary>A solid box with a smaller box carved out of it, leaving walls of the set thickness.</summary>
    private static List<CSGShapePart> Room(Float3 size, CSGGeneratorSettings settings)
    {
        float wall = MathF.Max(1e-3f, settings.WallThickness);
        Float3 inner = size - new Float3(wall * 2f);
        var parts = new List<CSGShapePart> { new(GeometryGenerator.BoxPlanes(size), Float3.Zero) };
        if (inner.X > 1e-3f && inner.Y > 1e-3f && inner.Z > 1e-3f)
            parts.Add(new CSGShapePart(GeometryGenerator.BoxPlanes(inner), Float3.Zero, 0, CSGOperation.Subtractive));
        return parts;
    }

    private static List<CSGShapePart> Single(Plane[] planes, int smooth)
        => [new CSGShapePart(planes, Float3.Zero, smooth)];

    /// <summary>A prism of unit radius and height 2, so it fills a 2x2x2 box before scaling.</summary>
    private static Plane[] UnitCylinder(CSGGeneratorSettings settings, out int sides)
    {
        sides = Math.Clamp(settings.Sides, 3, 128);

        // Measuring to a corner puts the vertices on the unit circle, measuring to a side puts the faces there.
        float radius = settings.RadiusToCorner ? 1f : 1f / MathF.Cos(MathF.PI / sides);
        float start = settings.OffsetAngle * (MathF.PI / 180f);

        var ring = new Float3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = start + i * MathF.Tau / sides;
            ring[i] = new Float3(MathF.Cos(a) * radius, 0f, MathF.Sin(a) * radius);
        }

        if (settings.Recentre) Recentre(ring);

        var planes = new List<Plane>(sides + 2);
        for (int i = 0; i < sides; i++)
            planes.Add(EdgePlane(ring[i], ring[(i + 1) % sides]));

        planes.Add(new Plane(Float3.UnitY, 1f));
        planes.Add(new Plane(-Float3.UnitY, 1f));

        return planes.ToArray();
    }

    /// <summary>The outward plane of a vertical face through two points on the ring.</summary>
    private static Plane EdgePlane(Float3 a, Float3 b)
    {
        Float3 along = b - a;
        Float3 normal = Float3.NormalizeSafe(new Float3(along.Z, 0f, -along.X), Float3.UnitX);

        float d = Float3.Dot(normal, a);

        // The ring is centred on the origin, so the origin must end up inside.
        return d < 0f ? new Plane(-normal, -d) : new Plane(normal, d);
    }

    /// <summary>A convex polytope enclosing the unit sphere, tangent to it at each plane's direction.</summary>
    private static Plane[] UnitSphere(CSGGeneratorSettings settings, out int curved)
    {
        int segments = Math.Clamp(settings.Sides, 3, 64);
        int rings = Math.Clamp(settings.Rings, 2, 64);

        var planes = new List<Plane>(segments * rings + 2);

        // Rings skip the poles, the pole planes are the caps added once below.
        for (int r = 1; r < rings; r++)
        {
            float phi = MathF.PI * r / rings;
            float y = MathF.Cos(phi), radius = MathF.Sin(phi);

            if (settings.Hemisphere && y < -1e-4f) continue;

            for (int s = 0; s < segments; s++)
            {
                float theta = MathF.Tau * s / segments;
                planes.Add(new Plane(new Float3(MathF.Cos(theta) * radius, y, MathF.Sin(theta) * radius), 1f));
            }
        }

        // A hemisphere's flat underside stays out of the smoothing group.
        curved = planes.Count + 1;

        planes.Add(new Plane(Float3.UnitY, 1f));
        planes.Add(settings.Hemisphere ? new Plane(-Float3.UnitY, 0f) : new Plane(-Float3.UnitY, 1f));

        if (!settings.Hemisphere) curved = planes.Count;

        return planes.ToArray();
    }

    /// <summary>Shifts a ring so its bounds are centred, rather than the circle it came from.</summary>
    private static void Recentre(Float3[] ring)
    {
        Float3 min = ring[0], max = ring[0];
        foreach (Float3 p in ring) { min = Maths.Min(min, p); max = Maths.Max(max, p); }

        Float3 shift = (min + max) * 0.5f;
        for (int i = 0; i < ring.Length; i++) ring[i] -= shift;
    }

    /// <summary>A flight of steps climbing along +Z, one brush per step, with the count fitted to the drawn box.</summary>
    private static List<CSGShapePart> LinearStairs(Float3 size, CSGGeneratorSettings settings)
    {
        var parts = new List<CSGShapePart>();

        // A lift under the flight eats into the height the steps themselves get.
        float lift = MathF.Max(0f, settings.ExtraHeight);
        float climb = MathF.Max(1e-3f, size.Y - lift);

        int steps = settings.TotalSteps > 0
            ? Math.Clamp(settings.TotalSteps, 1, 256)
            : StepCount(climb, settings.StepHeight, size.Z, settings.StepDepth);

        float stepHeight = climb / steps;
        float stepDepth = MathF.Max(1e-3f, (size.Z - MathF.Max(0f, settings.ExtraDepth)) / steps);
        float bottom = -size.Y * 0.5f;

        for (int i = 0; i < steps; i++)
        {
            // The top step runs on to make a landing when extra depth was asked for.
            float depth = i == steps - 1 ? stepDepth + MathF.Max(0f, settings.ExtraDepth) : stepDepth;

            // A filled staircase has each step reach the floor, an open one is a floating slab.
            float height = settings.FilledStairs ? lift + (i + 1) * stepHeight : stepHeight;
            float centreY = settings.FilledStairs
                ? bottom + height * 0.5f
                : bottom + lift + i * stepHeight + stepHeight * 0.5f;

            float centreZ = -size.Z * 0.5f + i * stepDepth + depth * 0.5f;

            parts.Add(new CSGShapePart(
                GeometryGenerator.BoxPlanes(new Float3(size.X, height, depth)),
                new Float3(0f, centreY, centreZ)));
        }

        return parts;
    }

    /// <summary>Enough steps to fill the box without any being thinner than asked for.</summary>
    private static int StepCount(float height, float stepHeight, float depth, float stepDepth)
    {
        int byHeight = stepHeight > 1e-4f ? (int)MathF.Round(MathF.Abs(height) / stepHeight) : 1;
        int byDepth = stepDepth > 1e-4f ? (int)MathF.Round(MathF.Abs(depth) / stepDepth) : 1;

        return Math.Clamp(Math.Max(byHeight, byDepth), 1, 256);
    }

    /// <summary>Steps turning around the Y axis, each a convex wedge under half a turn.</summary>
    private static List<CSGShapePart> SpiralStairs(Float3 half, CSGGeneratorSettings settings)
    {
        var parts = new List<CSGShapePart>();

        int perTurn = Math.Clamp(settings.StepsPerRevolution, 3, 64);
        float outer = MathF.Max(half.X, half.Z);
        float inner = Math.Clamp(settings.InnerRadius, 0f, 0.9f) * outer;

        float totalHeight = half.Y * 2f;
        int steps = StepCount(totalHeight, settings.StepHeight, totalHeight, settings.StepHeight);
        float stepHeight = totalHeight / steps;

        for (int i = 0; i < steps; i++)
        {
            float from = MathF.Tau * i / perTurn;
            float to = MathF.Tau * (i + 1) / perTurn;

            float centreY = -half.Y + (i + 0.5f) * stepHeight;

            parts.Add(new CSGShapePart(
                WedgePlanes(inner, outer, from, to, stepHeight),
                new Float3(0f, centreY, 0f)));
        }

        return parts;
    }

    /// <summary>A convex slab between two radii and two angles, centred on the origin.</summary>
    private static Plane[] WedgePlanes(float inner, float outer, float from, float to, float height)
    {
        Float3 a = new(MathF.Cos(from), 0f, MathF.Sin(from));
        Float3 b = new(MathF.Cos(to), 0f, MathF.Sin(to));

        // The outer edge is a chord rather than an arc so the wedge stays flat sided.
        Plane outerFace = EdgePlane(a * outer, b * outer);

        // Side faces point away from the wedge, so each is the inward radial turned outward.
        Float3 sideFrom = Float3.NormalizeSafe(new Float3(a.Z, 0f, -a.X), Float3.UnitX);
        Float3 sideTo = -Float3.NormalizeSafe(new Float3(b.Z, 0f, -b.X), Float3.UnitX);

        // Inner face closes the hole in the middle, unless there is none.
        Float3 mid = Float3.NormalizeSafe(a + b, Float3.UnitX);

        var planes = new List<Plane>(6)
        {
            outerFace,
            new(sideFrom, 0f),
            new(sideTo, 0f),
            new(Float3.UnitY, height * 0.5f),
            new(-Float3.UnitY, height * 0.5f),
        };

        if (inner > 1e-4f) planes.Add(new Plane(-mid, -inner));

        return planes.ToArray();
    }

    /// <summary>Scales a plane set, renormalising each <c>dot(n / s, p) &lt;= d</c>.</summary>
    public static Plane[] Scale(Plane[] planes, Float3 scale)
    {
        Float3 safe = new(NonZero(scale.X), NonZero(scale.Y), NonZero(scale.Z));
        var result = new Plane[planes.Length];

        for (int i = 0; i < planes.Length; i++)
        {
            Float3 n = planes[i].Normal / safe;
            float length = Float3.Length(n);

            result[i] = length > 1e-9f
                ? new Plane(n / length, planes[i].D / length)
                : planes[i];
        }

        return result;
    }

    private static float NonZero(float v) => MathF.Abs(v) < 1e-4f ? (v < 0f ? -1e-4f : 1e-4f) : v;
}
