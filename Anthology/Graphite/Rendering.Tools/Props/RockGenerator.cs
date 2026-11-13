using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>The shape of a generated rock.</summary>
public sealed class RockDesc
{
    public int Seed { get; init; }

    /// <summary>Metres across before stretching.</summary>
    public float Size { get; init; } = 1f;

    /// <summary>Scales the rock along x, y and z, so below 1 in y lies flatter.</summary>
    public Float3 Stretch { get; init; } = new(1.3f, 0.75f, 1f);

    /// <summary>How lumpy the broad shape is.</summary>
    public float Lumps { get; init; } = 0.3f;

    /// <summary>The flat faces chiselled into it, and how deep they cut, 0 to 1.</summary>
    public int Cuts { get; init; } = 7;
    public float CutDepth { get; init; } = 0.3f;

    /// <summary>The share of its height cut flat underneath, where it sits on the ground.</summary>
    public float Flatten { get; init; } = 0.25f;

    /// <summary>Texture repeats per metre.</summary>
    public float TextureScale { get; init; } = 0.5f;

    /// <summary>The share of its upward faces given a moss tint, 0 for bare rock.</summary>
    public float Moss { get; init; } = 0.4f;
}

/// <summary>
/// Rocks from a subdivided icosahedron: lumped by noise, chiselled by random planes into flat faces and cracks, cut flat
/// underneath and resting on y 0. Vertex colour darkens the hollows and the ground contact and tints the tops with moss.
/// The texture maps along each face's main axis. Any detail gives the same shape, so the levels of one rock match.
/// </summary>
public static class RockGenerator
{
    /// <summary>The rock at a detail, 0 the finest at 1280 triangles, each level a quarter of the one before.</summary>
    public static MeshData Build(RockDesc desc, int detail = 0)
    {
        (List<Float3> directions, List<int> triangles) = Icosphere(Math.Max(3 - detail, 1));
        PropNoise.Random random = new(desc.Seed);
        (Float3 Normal, float Offset)[] cuts = new (Float3, float)[desc.Cuts];
        for (int i = 0; i < cuts.Length; i++)
        {
            Float3 n = random.Direction();
            n = Float3.Normalize(new Float3(n.X, n.Y * 0.6f + 0.25f, n.Z));
            float reach = Float3.Length(n * desc.Stretch);
            cuts[i] = (n, reach * (1f - desc.CutDepth * (float)random.Range(0.3, 1.0)));
        }

        Float3[] positions = new Float3[directions.Count];
        for (int i = 0; i < positions.Length; i++)
        {
            Float3 d = directions[i];
            double r = 1 + desc.Lumps * PropNoise.Fbm(d * 1.3f + new Float3(desc.Seed * 0.37f, 0, 0), 3, desc.Seed);
            Float3 p = d * (float)r * desc.Stretch;
            foreach ((Float3 n, float offset) in cuts)
            {
                float over = Float3.Dot(p, n) - offset;
                if (over > 0)
                    p -= n * over;
            }
            p += d * (float)(0.025 * PropNoise.Fbm(d * 7f, 2, desc.Seed + 5));
            positions[i] = p;
        }

        float bottom = float.MaxValue, top = float.MinValue;
        foreach (Float3 p in positions)
            (bottom, top) = (MathF.Min(bottom, p.Y), MathF.Max(top, p.Y));
        float floor = bottom + (top - bottom) * desc.Flatten;
        for (int i = 0; i < positions.Length; i++)
            positions[i] = new Float3(positions[i].X, MathF.Max(positions[i].Y, floor) - floor, positions[i].Z) * (desc.Size * 0.5f);
        float height = (top - floor) * desc.Size * 0.5f;

        Float3[] normals = new Float3[positions.Length];
        Float3[] centroid = new Float3[positions.Length];
        int[] neighbours = new int[positions.Length];
        for (int t = 0; t < triangles.Count; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            Float3 face = Float3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += face;
            normals[b] += face;
            normals[c] += face;
            foreach ((int v, int o1, int o2) in ((int, int, int)[])[(a, b, c), (b, c, a), (c, a, b)])
            {
                centroid[v] += positions[o1] + positions[o2];
                neighbours[v] += 2;
            }
        }

        PropMesh mesh = new(1);
        Dictionary<(int, int), uint> split = new();
        Float4 Color(int i, Float3 n)
        {
            Float3 average = centroid[i] / Math.Max(neighbours[i], 1);
            float hollow = Math.Clamp(Float3.Dot(average - positions[i], n) / (desc.Size * 0.02f), -1f, 1f);
            float shade = 1f - 0.35f * MathF.Max(hollow, 0f) + 0.1f * MathF.Max(-hollow, 0f);
            shade *= 0.7f + 0.3f * Math.Clamp(positions[i].Y / MathF.Max(height * 0.25f, 1e-3f), 0f, 1f);
            Float3 tint = new(shade, shade, shade);
            float moss = desc.Moss * Math.Clamp((n.Y - 0.55f) * 3f, 0f, 1f) * (float)Math.Clamp(PropNoise.Fbm(positions[i] * 1.5f, 3, desc.Seed + 9) * 2 + 0.6, 0, 1);
            tint *= new Float3(1f, 1f, 1f) + (new Float3(0.55f, 0.75f, 0.35f) - new Float3(1f, 1f, 1f)) * moss;
            return new Float4(tint.X, tint.Y, tint.Z, 1f);
        }

        for (int t = 0; t < triangles.Count; t += 3)
        {
            Float3 face = Float3.Cross(positions[triangles[t + 1]] - positions[triangles[t]], positions[triangles[t + 2]] - positions[triangles[t]]);
            Float3 abs = new(MathF.Abs(face.X), MathF.Abs(face.Y), MathF.Abs(face.Z));
            int axis = abs.Y >= abs.X && abs.Y >= abs.Z ? 1 : abs.X >= abs.Z ? 0 : 2;
            int side = axis * 2 + ((axis == 0 ? face.X : axis == 1 ? face.Y : face.Z) < 0 ? 1 : 0);
            uint[] corner = new uint[3];
            for (int k = 0; k < 3; k++)
            {
                int i = triangles[t + k];
                if (!split.TryGetValue((i, side), out uint index))
                {
                    Float3 p = positions[i], n = Float3.Normalize(normals[i]);
                    float sign = (side & 1) == 0 ? 1f : -1f;
                    (Float2 uv, Float3 u) = axis switch
                    {
                        0 => (new Float2(p.Z * sign, -p.Y), new Float3(0f, 0f, sign)),
                        1 => (new Float2(p.X * sign, p.Z), new Float3(sign, 0f, 0f)),
                        _ => (new Float2(-p.X * sign, -p.Y), new Float3(-sign, 0f, 0f)),
                    };
                    Float3 tangent = Float3.Normalize(u - n * Float3.Dot(u, n));
                    index = mesh.Vertex(p, n, new Float4(tangent.X, tangent.Y, tangent.Z, 1f), uv * desc.TextureScale, Color(i, n));
                    split[(i, side)] = index;
                }
                corner[k] = index;
            }
            mesh.Triangle(0, corner[0], corner[1], corner[2]);
        }
        return mesh.ToMesh();
    }

    // A unit icosahedron subdivided the given times, wound counter clockwise from outside
    private static (List<Float3> Directions, List<int> Triangles) Icosphere(int subdivisions)
    {
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        List<Float3> v =
        [
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        ];
        for (int i = 0; i < v.Count; i++)
            v[i] = Float3.Normalize(v[i]);
        List<int> f =
        [
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        ];
        for (int s = 0; s < subdivisions; s++)
        {
            Dictionary<(int, int), int> middles = new();
            int Middle(int a, int b)
            {
                (int, int) key = a < b ? (a, b) : (b, a);
                if (!middles.TryGetValue(key, out int m))
                {
                    m = v.Count;
                    v.Add(Float3.Normalize(v[a] + v[b]));
                    middles[key] = m;
                }
                return m;
            }
            List<int> next = new(f.Count * 4);
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                next.AddRange([a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca]);
            }
            f = next;
        }
        return (v, f);
    }
}
