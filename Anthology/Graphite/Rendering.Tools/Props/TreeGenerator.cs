using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering.Tools;

public enum TreeShape : byte
{
    /// <summary>A trunk forking into a round crown of leaf clusters.</summary>
    Broadleaf,

    /// <summary>A straight trunk to the tip ringed by drooping branches of needles, narrowing upward.</summary>
    Conifer,

    /// <summary>Stems from the ground, low and wide.</summary>
    Bush,
}

/// <summary>The look of a generated tree.</summary>
public sealed class TreeDesc
{
    public int Seed { get; init; }
    public TreeShape Shape { get; init; }

    /// <summary>Metres to the top of the crown.</summary>
    public float Height { get; init; } = 10f;

    /// <summary>The trunk's radius above the root flare.</summary>
    public float TrunkRadius { get; init; } = 0.22f;

    /// <summary>Branches off the trunk, each splitting into fewer for every level after.</summary>
    public int Branches { get; init; } = 8;
    public int Levels { get; init; } = 3;

    /// <summary>Degrees between a branch and its parent.</summary>
    public float BranchAngle { get; init; } = 50f;

    /// <summary>How much branches wander from straight, 0 to 1.</summary>
    public float Gnarl { get; init; } = 0.35f;

    /// <summary>Metres across a leaf card, and cards per metre of twig.</summary>
    public float LeafSize { get; init; } = 1.5f;
    public float LeafDensity { get; init; } = 1.2f;

    /// <summary>Metres of bark one texture repeat covers around the trunk.</summary>
    public float BarkTile { get; init; } = 1.2f;
}

/// <summary>
/// Trees and bushes grown as a skeleton of tapering, wandering branches, each level branching from the one before,
/// skinned as tubes of bark in submesh 0 with leaf cards of a leaf cluster or needle texture at the twigs in submesh 1.
/// A conifer's finest twigs get no bark, the needles hide them. Leaf normals bend out from the crown's middle so a
/// canopy lights as one soft mass, and vertex colour darkens its inside. Every detail grows the same skeleton: coarser ones skin it with fewer sides, drop thin twigs and merge leaves
/// into fewer larger cards, so the levels of one tree match.
/// </summary>
public static class TreeGenerator
{
    private sealed class Branch
    {
        public readonly List<Float3> Points = new();
        public readonly List<float> Radii = new();
        public int Level;
        public float Length;
    }

    /// <summary>The tree at a detail, 0 the finest, up to 2.</summary>
    public static MeshData Build(TreeDesc desc, int detail = 0)
    {
        detail = Math.Clamp(detail, 0, 2);
        List<Branch> branches = Grow(desc);
        (Float3 crown, float crownRadius) = Crown(branches, desc);

        PropMesh mesh = new(2);
        foreach (Branch branch in branches)
        {
            int sides = branch.Level switch { 0 => 10, 1 => 5, _ => 3 };
            sides = Math.Max(sides >> detail, 3);
            bool hidden = desc.Shape == TreeShape.Conifer && branch.Level == desc.Levels && branch.Level > 1;
            if (hidden || detail >= 1 && branch.Level >= desc.Levels || detail >= 2 && branch.Level >= desc.Levels - 1 && branch.Level > 0)
                continue;
            Skin(mesh, branch, sides, desc, detail);
        }

        PropNoise.Random leafRandom = new(desc.Seed * 31 + 7);
        float keep = detail switch { 0 => 1f, 1 => 0.45f, _ => 0.2f };
        float grow = 1f / MathF.Sqrt(keep);
        int card = 0;
        foreach (Branch branch in branches)
        {
            if (branch.Level < Math.Max(desc.Levels - 1, desc.Shape == TreeShape.Conifer ? 1 : 0))
                continue;
            int count = Math.Max((int)(branch.Length * desc.LeafDensity), 1);
            for (int i = 0; i < count; i++)
            {
                double along = desc.Shape == TreeShape.Conifer ? (i + 0.5) / count : 0.15 + 0.85 * (i + 0.5) / count;
                Float3 roll = leafRandom.Direction();
                double size = leafRandom.Range(0.75, 1.25);
                double tint = leafRandom.Range(0.85, 1.1);
                if (PropNoise.Unit(PropNoise.Hash(card++, 5, 5, desc.Seed)) >= keep)
                    continue;
                (Float3 at, Float3 dir) = Along(branch, along);
                Leaf(mesh, desc, at, dir, roll, (float)(size * desc.LeafSize * grow), (float)tint, crown, crownRadius);
            }
        }
        return mesh.ToMesh();
    }

    private static List<Branch> Grow(TreeDesc desc)
    {
        PropNoise.Random random = new(desc.Seed);
        List<Branch> branches = new();
        Float3 up = new(0f, 1f, 0f);
        if (desc.Shape == TreeShape.Bush)
        {
            int stems = Math.Max(desc.Branches / 2, 3);
            for (int i = 0; i < stems; i++)
            {
                float yaw = (float)(i * 2.39996 + random.Range(-0.3, 0.3));
                float lean = (float)random.Range(0.35, 0.8);
                Float3 dir = Float3.Normalize(new Float3(MathF.Cos(yaw) * lean, 1f, MathF.Sin(yaw) * lean));
                Stem(branches, random, desc, Float3.Zero, dir, desc.Height * (float)random.Range(0.5, 0.65), desc.TrunkRadius, 1);
            }
            return branches;
        }

        Branch trunk = Tube(random, Float3.Zero, up, desc.Shape == TreeShape.Conifer ? desc.Height : desc.Height * 0.62f, desc.TrunkRadius, 0, desc.Gnarl * 0.5f, 0.02f, 14);
        branches.Add(trunk);
        Children(branches, random, desc, trunk);
        return branches;
    }

    private static void Stem(List<Branch> branches, PropNoise.Random random, TreeDesc desc, Float3 start, Float3 dir, float length, float radius, int level)
    {
        Branch stem = Tube(random, start, dir, length, radius, level, desc.Gnarl, 0.05f, 8);
        branches.Add(stem);
        Children(branches, random, desc, stem);
    }

    // Spreads a branch's children along it, turned by the golden angle, shorter toward its end and in the crown's outline
    private static void Children(List<Branch> branches, PropNoise.Random random, TreeDesc desc, Branch parent)
    {
        int level = parent.Level + 1;
        if (level > desc.Levels)
            return;
        bool conifer = desc.Shape == TreeShape.Conifer;
        int count = parent.Level == 0 ? (conifer ? desc.Branches * 4 : desc.Branches) : Math.Max(desc.Branches - 2 * level, 2) + (conifer ? 4 : 0);
        double first = parent.Level == 0 ? (conifer ? 0.18 : 0.45) : 0.25;
        float turn = (float)random.Range(0, Math.PI * 2);
        for (int i = 0; i < count; i++)
        {
            double t = first + (0.97 - first) * (i + random.Range(0.1, 0.9)) / count;
            (Float3 at, Float3 along) = Along(parent, t);
            turn += 2.39996f;
            Float3 side = Perpendicular(along, turn);
            float angle = desc.BranchAngle * (float)random.Range(0.8, 1.2);
            float length, droop;
            if (conifer)
            {
                angle = parent.Level == 0 ? 95f + (float)random.Range(-10, 10) : 45f;
                length = parent.Level == 0 ? desc.Height * 0.36f * (float)(1 - t) + 0.3f : parent.Length * 0.3f * (float)(1 - t * 0.5);
                droop = parent.Level == 0 ? -0.06f : -0.02f;
            }
            else
            {
                double profile = parent.Level == 0 ? Math.Sin(Math.PI * Math.Clamp((t - 0.3) / 0.75, 0, 1)) * 0.75 + 0.35 : 1 - t * 0.5;
                length = parent.Level == 0 ? desc.Height * 0.48f * (float)profile : parent.Length * 0.55f * (float)profile;
                droop = desc.Shape == TreeShape.Bush ? 0.02f : 0.04f;
            }
            float rad = angle * MathF.PI / 180f;
            Float3 dir = Float3.Normalize(along * MathF.Cos(rad) + side * MathF.Sin(rad));
            float radius = MathF.Min(Radius(parent, t) * 0.62f, desc.TrunkRadius * MathF.Pow(0.55f, level));
            Branch child = Tube(random, at, dir, length * (float)random.Range(0.85, 1.1), radius, level, desc.Gnarl, droop, level == 1 ? 8 : 5);
            branches.Add(child);
            Children(branches, random, desc, child);
        }

        // Broadleaf trunks carry on past their branches as a leader into the top of the crown
        if (parent.Level == 0 && desc.Shape == TreeShape.Broadleaf)
        {
            (Float3 top, Float3 dir) = Along(parent, 1.0);
            Branch leader = Tube(random, top, dir, desc.Height * 0.3f, Radius(parent, 1.0), 1, desc.Gnarl, 0.02f, 6);
            branches.Add(leader);
            Children(branches, random, desc, leader);
        }
    }

    private static Branch Tube(PropNoise.Random random, Float3 start, Float3 dir, float length, float radius, int level, float gnarl, float rise, int segments)
    {
        Branch branch = new() { Level = level, Length = length };
        Float3 at = start, d = dir;
        float step = length / segments;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            branch.Points.Add(at);
            branch.Radii.Add(radius * MathF.Max(1f - t * 0.85f, 0.12f));
            d = Float3.Normalize(d + random.Direction() * gnarl * 0.35f + new Float3(0f, rise * (1f + level), 0f));
            at += d * step;
        }
        return branch;
    }

    private static (Float3 Center, float Radius) Crown(List<Branch> branches, TreeDesc desc)
    {
        Float3 sum = Float3.Zero;
        int count = 0;
        foreach (Branch branch in branches)
            if (branch.Level > 0)
            {
                sum += branch.Points[^1];
                count++;
            }
        Float3 center = count > 0 ? sum / count : new Float3(0f, desc.Height * 0.6f, 0f);
        float radius = 0f;
        foreach (Branch branch in branches)
            if (branch.Level > 0)
                radius = MathF.Max(radius, Float3.Length(branch.Points[^1] - center));
        return (center, MathF.Max(radius, 0.5f));
    }

    private static (Float3 At, Float3 Direction) Along(Branch branch, double t)
    {
        double f = Math.Clamp(t, 0, 1) * (branch.Points.Count - 1);
        int i = Math.Min((int)f, branch.Points.Count - 2);
        float s = (float)(f - i);
        Float3 a = branch.Points[i], b = branch.Points[i + 1];
        return (a + (b - a) * s, Float3.Normalize(b - a));
    }

    private static float Radius(Branch branch, double t)
    {
        double f = Math.Clamp(t, 0, 1) * (branch.Radii.Count - 1);
        int i = Math.Min((int)f, branch.Radii.Count - 2);
        return branch.Radii[i] + (branch.Radii[i + 1] - branch.Radii[i]) * (float)(f - i);
    }

    private static Float3 Perpendicular(Float3 axis, float turn)
    {
        Float3 helper = MathF.Abs(axis.Y) < 0.9f ? new Float3(0f, 1f, 0f) : new Float3(1f, 0f, 0f);
        Float3 a = Float3.Normalize(Float3.Cross(axis, helper)), b = Float3.Cross(axis, a);
        return a * MathF.Cos(turn) + b * MathF.Sin(turn);
    }

    // Rings around the branch framed by parallel transport, the trunk's foot flared into root buttresses
    private static void Skin(PropMesh mesh, Branch branch, int sides, TreeDesc desc, int detail)
    {
        int rings = branch.Points.Count;
        int step = (detail > 0 || branch.Level > 0) && rings > 4 ? 2 : 1;
        Float3 axis0 = Float3.Normalize(branch.Points[1] - branch.Points[0]);
        Float3 normal = Perpendicular(axis0, 0f);
        float around = MathF.Max(MathF.Round(branch.Radii[0] * MathF.PI * 2f / desc.BarkTile), 1f);
        float travelled = 0f;
        uint previous = 0;
        bool first = true;
        Float3 last = branch.Points[0];
        for (int r = 0; r < rings; r += step)
        {
            if (r + step >= rings && r != rings - 1)
                r = rings - 1;
            Float3 at = branch.Points[r];
            Float3 axis = Float3.Normalize(branch.Points[Math.Min(r + 1, rings - 1)] - branch.Points[Math.Max(r - 1, 0)]);
            normal = Float3.Normalize(normal - axis * Float3.Dot(normal, axis));
            Float3 binormal = Float3.Cross(axis, normal);
            travelled += Float3.Length(at - last);
            last = at;
            float radius = branch.Radii[r];
            float height = at.Y;
            uint ring = (uint)mesh.Count;
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * MathF.PI * 2f;
                Float3 radial = normal * MathF.Cos(a) + binormal * MathF.Sin(a);
                float flare = 1f;
                if (branch.Level == 0 && desc.Shape != TreeShape.Bush)
                {
                    float foot = MathF.Max(1f - height / (desc.TrunkRadius * 5f), 0f);
                    flare += foot * foot * (0.9f + 0.6f * MathF.Pow(MathF.Max(MathF.Cos(a * 5f + desc.Seed), 0f), 3f));
                }
                Float3 tangent = Float3.Cross(axis, radial);
                float shade = branch.Level == 0 ? 0.75f + 0.25f * Math.Clamp(height / 1.5f, 0f, 1f) : 1f;
                mesh.Vertex(at + radial * radius * flare, radial, new Float4(tangent.X, tangent.Y, tangent.Z, 1f),
                    new Float2(s / (float)sides * around, travelled / desc.BarkTile), new Float4(shade, shade, shade, 1f));
            }
            if (!first)
                for (uint s = 0; s < sides; s++)
                    mesh.Quad(0, previous + s, ring + s, ring + s + 1, previous + s + 1);
            previous = ring;
            first = false;
        }
    }

    // A card from the twig outward, the texture's stem at the twig, turned by a random roll, its normal bent out from the crown
    private static void Leaf(PropMesh mesh, TreeDesc desc, Float3 at, Float3 twig, Float3 roll, float size, float tint, Float3 crown, float crownRadius)
    {
        Float3 outward = at - crown;
        float distance = Float3.Length(outward);
        outward = distance > 1e-4f ? outward / distance : new Float3(0f, 1f, 0f);
        Float3 along;
        if (desc.Shape == TreeShape.Conifer)
            along = Float3.Normalize(twig + new Float3(0f, -0.15f, 0f));
        else
            along = Float3.Normalize(twig * 0.4f + outward * 0.6f + roll * 0.5f + new Float3(0f, 0.2f, 0f));
        Float3 across = Float3.Cross(along, roll);
        if (Float3.Length(across) < 1e-3f)
            across = Perpendicular(along, 0f);
        across = Float3.Normalize(across);
        Float3 face = Float3.Normalize(Float3.Cross(across, along));
        if (Float3.Dot(face, outward) < 0f)
            face = -face;
        Float3 normal = Float3.Normalize(face * 0.35f + outward * 0.65f);
        float inside = Math.Clamp(distance / crownRadius, 0f, 1f);
        float shade = (0.45f + 0.55f * inside) * tint;
        Float4 color = new(shade, shade, shade, 1f);
        Float4 tangent = new(across.X, across.Y, across.Z, 1f);
        Float3 half = across * (size * 0.5f), start = at - along * (size * 0.05f), end = start + along * size;
        uint a = mesh.Vertex(start - half, normal, tangent, new Float2(0f, 1f), color);
        uint b = mesh.Vertex(start + half, normal, tangent, new Float2(1f, 1f), color);
        uint c = mesh.Vertex(end + half, normal, tangent, new Float2(1f, 0f), color);
        uint d = mesh.Vertex(end - half, normal, tangent, new Float2(0f, 0f), color);
        mesh.Quad(1, a, b, c, d);
    }
}
