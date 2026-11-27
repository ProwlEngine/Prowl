using System;
using System.Collections.Generic;
using Prowl.Vector;

namespace Photonic.Demo;

/// <summary>
/// Procedural mesh generators used by <see cref="TestScene"/>. Every generator returns a complete
/// <see cref="LoadedModel"/> with charted lightmap UVs in <c>BestExistingUV</c>, so models can be
/// baked with <see cref="UV1Strategy.UseExisting"/> without running the unwrapper.
/// </summary>
internal static class ProceduralShapes
{
    private delegate void PatchPoint(float u, float v, out Float3 position, out Float3 normal, out Float2 lightmapUV);

    // ---- surfaces -----------------------------------------------------------------------------

    public static LoadedModel Ground(string name, float sizeX, float sizeZ, int segments, Float3 color,
                                     LoadedModel.TextureBlob? texture = null, float texelsPerTile = 4f)
    {
        var mb = new MeshBuilder();
        int tex = texture is null ? -1 : mb.AddTexture(texture);
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color, tex));

        float tilesX = sizeX / texelsPerTile;
        float tilesZ = sizeZ / texelsPerTile;
        AddPatch(mb, segments, segments, new Float2(tilesX, tilesZ), (float u, float v, out Float3 p, out Float3 n, out Float2 lm) =>
        {
            p = new Float3((u - 0.5f) * sizeX, 0f, (v - 0.5f) * sizeZ);
            n = new Float3(0, 1, 0);
            lm = new Float2(u * sizeX, v * sizeZ);
        });
        return mb.Build(name);
    }

    /// <summary>Single-sided quad in the XY plane facing -Z. Useful as a bounce card / occluder.</summary>
    public static LoadedModel Quad(string name, float width, float height, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));
        AddPatch(mb, 1, 1, Float2.One, (float u, float v, out Float3 p, out Float3 n, out Float2 lm) =>
        {
            p = new Float3((0.5f - u) * width, (v - 0.5f) * height, 0f);
            n = new Float3(0, 0, -1);
            lm = new Float2(u * width, v * height);
        });
        return mb.Build(name);
    }

    public static LoadedModel Terrain(string name, float size, int segments, float amplitude, int seed, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));
        var offset = new Float2(seed * 13.7f, seed * 7.3f);

        float Height(float x, float z) => MathF.Max(0f, Fbm(new Float2(x, z) * 0.09f + offset, 4) + 0.5f) * amplitude;

        AddPatch(mb, segments, segments, new Float2(size / 4f, size / 4f), (float u, float v, out Float3 p, out Float3 n, out Float2 lm) =>
        {
            float x = (u - 0.5f) * size;
            float z = (v - 0.5f) * size;
            float edge = EdgeFalloff(u) * EdgeFalloff(v);
            p = new Float3(x, Height(x, z) * edge, z);
            n = new Float3(0, 1, 0);
            lm = new Float2(u * size, v * size);
        });
        mb.SmoothNormals();
        return mb.Build(name);
    }

    public static LoadedModel Sphere(string name, float radius, int segments, int rings, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));
        AddSphereSurface(mb, radius, segments, rings, 0f, 1f, _ => radius);
        return mb.Build(name);
    }

    /// <summary>Noise-displaced sphere: an organic blob with smooth welded normals.</summary>
    public static LoadedModel Blob(string name, float radius, float lumpiness, int seed, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));
        var offset = new Float3(seed * 5.1f, seed * 2.7f, seed * 9.3f);
        AddSphereSurface(mb, radius, 40, 24, 0f, 1f,
            dir => radius * (1f + lumpiness * Fbm(dir * 1.9f + offset, 3)));
        mb.SmoothNormals();
        return mb.Build(name);
    }

    public static LoadedModel Torus(string name, float majorRadius, float minorRadius, int majorSegments, int minorSegments, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));
        AddPatch(mb, majorSegments, minorSegments, new Float2(majorSegments / 4f, minorSegments / 4f),
            (float u, float v, out Float3 p, out Float3 n, out Float2 lm) =>
        {
            float a = u * MathF.Tau;
            float b = v * MathF.Tau;
            var radial = new Float3(MathF.Cos(a), 0f, MathF.Sin(a));
            n = radial * MathF.Cos(b) + new Float3(0, MathF.Sin(b), 0);
            p = radial * majorRadius + n * minorRadius;
            lm = new Float2(u * MathF.Tau * majorRadius, v * MathF.Tau * minorRadius);
        }, chartCols: SquareSpan(majorSegments, MathF.Tau * majorRadius, MathF.Tau * minorRadius));
        return mb.Build(name);
    }

    public static LoadedModel TorusKnot(string name, float radius, float tubeRadius, int p, int q, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));

        const int steps = 220;
        var path = new Float3[steps];
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps * MathF.Tau;
            float r = radius * (2f + MathF.Cos(q * t)) / 3f;
            path[i] = new Float3(
                r * MathF.Cos(p * t),
                r * MathF.Sin(q * t) * 0.5f,
                r * MathF.Sin(p * t));
        }
        AddTube(mb, path, closed: true, tubeRadius, 16);
        mb.SmoothNormals();
        return mb.Build(name);
    }

    public static LoadedModel Cylinder(string name, float radius, float height, int segments, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));
        AddTaperedTube(mb, new Float3(0, 0, 0), new Float3(0, height, 0), radius, radius, segments, 1);
        AddDisc(mb, new Float3(0, height, 0), new Float3(0, 1, 0), radius, segments);
        AddDisc(mb, Float3.Zero, new Float3(0, -1, 0), radius, segments);
        return mb.Build(name);
    }

    public static LoadedModel Cone(string name, float radius, float height, int segments, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));
        AddTaperedTube(mb, Float3.Zero, new Float3(0, height, 0), radius, 0.001f, segments, 1);
        AddDisc(mb, Float3.Zero, new Float3(0, -1, 0), radius, segments);
        return mb.Build(name);
    }

    public static LoadedModel Capsule(string name, float radius, float cylinderHeight, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));
        AddTaperedTube(mb, Float3.Zero, new Float3(0, cylinderHeight, 0), radius, radius, 28, 1);
        AddSphereSurface(mb, radius, 28, 8, 0f, 0.5f, _ => radius, new Float3(0, cylinderHeight, 0));
        AddSphereSurface(mb, radius, 28, 8, 0.5f, 1f, _ => radius, Float3.Zero);
        return mb.Build(name);
    }

    // ---- boxy things --------------------------------------------------------------------------

    public static LoadedModel Box(string name, Float3 size, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));
        AddBox(mb, Float3.Zero, size);
        return mb.Build(name);
    }

    public static LoadedModel Wedge(string name, float width, float height, float depth, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));

        float hw = width * 0.5f, hd = depth * 0.5f;
        var a = new Float3(-hw, 0, -hd);
        var b = new Float3(hw, 0, -hd);
        var c = new Float3(hw, 0, hd);
        var d = new Float3(-hw, 0, hd);
        var e = new Float3(-hw, height, hd);
        var f = new Float3(hw, height, hd);

        var inside = new Float3(0, height * 0.25f, hd * 0.25f);
        AddConvexFace(mb, new[] { a, d, c, b }, inside);
        AddConvexFace(mb, new[] { d, e, f, c }, inside);
        AddConvexFace(mb, new[] { a, b, f, e }, inside);
        AddConvexFace(mb, new[] { a, e, d }, inside);
        AddConvexFace(mb, new[] { b, c, f }, inside);
        return mb.Build(name);
    }

    public static LoadedModel Pyramid(string name, float baseSize, float height, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));

        float h = baseSize * 0.5f;
        var a = new Float3(-h, 0, -h);
        var b = new Float3(h, 0, -h);
        var c = new Float3(h, 0, h);
        var d = new Float3(-h, 0, h);
        var apex = new Float3(0, height, 0);

        var inside = new Float3(0, height * 0.25f, 0);
        AddConvexFace(mb, new[] { a, d, c, b }, inside);
        AddConvexFace(mb, new[] { a, b, apex }, inside);
        AddConvexFace(mb, new[] { b, c, apex }, inside);
        AddConvexFace(mb, new[] { c, d, apex }, inside);
        AddConvexFace(mb, new[] { d, a, apex }, inside);
        return mb.Build(name);
    }

    public static LoadedModel Stairs(string name, int steps, float width, float stepHeight, float stepDepth, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));
        for (int i = 0; i < steps; i++)
        {
            float h = stepHeight * (i + 1);
            AddBox(mb, new Float3(0, h * 0.5f, stepDepth * (i + 0.5f)), new Float3(width, h, stepDepth));
        }
        return mb.Build(name);
    }

    /// <summary>Four walls with a doorway and two windows, plus a ceiling slab. A GI light-shaft test.</summary>
    public static LoadedModel Room(string name, float width, float depth, float height, float thickness, Float3 wallColor, Float3 ceilingColor)
    {
        var mb = new MeshBuilder();
        int wall = mb.AddMaterial($"{name}_wall", wallColor);
        int ceiling = mb.AddMaterial($"{name}_ceiling", ceilingColor);

        float hw = width * 0.5f, hd = depth * 0.5f, ht = thickness * 0.5f;
        mb.UseMaterial(wall);

        // -Z wall: doorway. +Z wall: high window. -X wall: wide window. +X wall: solid.
        AddWallWithHole(mb, new Float3(0, height * 0.5f, -hd + ht), new Float3(width, height, thickness), Axis.Z,
            new Float2(0f, 1.1f), new Float2(1.6f, 2.2f));
        AddWallWithHole(mb, new Float3(0, height * 0.5f, hd - ht), new Float3(width, height, thickness), Axis.Z,
            new Float2(0f, height * 0.62f), new Float2(2.4f, 1.2f));
        AddWallWithHole(mb, new Float3(-hw + ht, height * 0.5f, 0), new Float3(thickness, height, depth), Axis.X,
            new Float2(0f, height * 0.55f), new Float2(3.0f, 1.4f));
        AddBox(mb, new Float3(hw - ht, height * 0.5f, 0), new Float3(thickness, height, depth));

        mb.UseMaterial(ceiling);
        AddBox(mb, new Float3(0, height + ht, 0), new Float3(width, thickness, depth));
        return mb.Build(name);
    }

    /// <summary>Two straight legs joined by a semicircle, swept as one continuous tube.</summary>
    public static LoadedModel Arch(string name, float radius, float legHeight, float tubeRadius, Float3 color)
    {
        var mb = new MeshBuilder();
        mb.UseMaterial(mb.AddMaterial($"{name}_mat", color));

        var path = new List<Float3>();
        for (int i = 0; i < 4; i++)
            path.Add(new Float3(-radius, legHeight * i / 4f, 0));
        const int arcSteps = 24;
        for (int i = 0; i <= arcSteps; i++)
        {
            float a = MathF.PI - i / (float)arcSteps * MathF.PI;
            path.Add(new Float3(radius * MathF.Cos(a), legHeight + radius * MathF.Sin(a), 0));
        }
        for (int i = 3; i >= 0; i--)
            path.Add(new Float3(radius, legHeight * i / 4f, 0));

        AddTube(mb, path.ToArray(), closed: false, tubeRadius, 14);
        mb.SmoothNormals();
        return mb.Build(name);
    }

    // ---- tree ---------------------------------------------------------------------------------

    public static LoadedModel Tree(string name, int seed, float height, Float3 barkColor, Float3 leafColor)
    {
        var mb = new MeshBuilder();
        int bark = mb.AddMaterial($"{name}_bark", barkColor);
        int leaf = mb.AddMaterial($"{name}_leaf", leafColor);
        var rng = new RNG((ulong)seed);

        Branch(mb, rng, bark, leaf, Float3.Zero, new Float3(0, 1, 0), height * 0.42f, height * 0.045f, 4);
        mb.SmoothNormals();
        return mb.Build(name);
    }

    private static void Branch(MeshBuilder mb, RNG rng, int bark, int leaf,
                               Float3 origin, Float3 direction, float length, float radius, int depth)
    {
        var tip = origin + direction * length;
        float tipRadius = radius * 0.62f;

        mb.UseMaterial(bark);
        AddTaperedTube(mb, origin, tip, radius, tipRadius, depth >= 3 ? 12 : 7, depth >= 3 ? 3 : 1);

        if (depth <= 0)
        {
            mb.UseMaterial(leaf);
            float r = length * rng.Range(0.75f, 1.15f);
            var lumpOffset = rng.NextFloat3() * 12f;
            AddSphereSurface(mb, r, 16, 10, 0f, 1f,
                d => r * (1f + 0.28f * Fbm(d * 2.4f + lumpOffset, 2)), tip);
            return;
        }

        var side = PerpendicularTo(direction);
        var other = Float3.Cross(direction, side);
        int children = rng.Range(2, 3);
        for (int i = 0; i < children; i++)
        {
            float angle = rng.NextFloat() * MathF.Tau;
            float spread = rng.Range(0.45f, 0.85f);
            var offset = (side * MathF.Cos(angle) + other * MathF.Sin(angle)) * spread;
            var childDir = Float3.Normalize(direction + offset);
            var childOrigin = origin + direction * (length * rng.Range(0.72f, 1f));
            Branch(mb, rng, bark, leaf, childOrigin, childDir, length * rng.Range(0.6f, 0.78f), tipRadius, depth - 1);
        }
    }

    // ---- textures -----------------------------------------------------------------------------

    public static LoadedModel.TextureBlob Checker(string name, int size, int cells, Float3 a, Float3 b)
    {
        var rgba = new byte[size * size * 4];
        int cellPx = Math.Max(1, size / Math.Max(1, cells));
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool alt = ((x / cellPx) + (y / cellPx)) % 2 == 0;
            var c = alt ? a : b;
            int o = (y * size + x) * 4;
            rgba[o] = ToByte(c.X);
            rgba[o + 1] = ToByte(c.Y);
            rgba[o + 2] = ToByte(c.Z);
            rgba[o + 3] = 255;
        }
        return new LoadedModel.TextureBlob { Name = name, Width = size, Height = size, RGBA = rgba };
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    // ---- primitive emitters -------------------------------------------------------------------

    private enum Axis { X, Z }

    private static readonly Float3[] BoxFaceNormals =
    {
        new Float3(1, 0, 0), new Float3(-1, 0, 0),
        new Float3(0, 1, 0), new Float3(0, -1, 0),
        new Float3(0, 0, 1), new Float3(0, 0, -1),
    };

    /// <summary>
    /// Grid of quads over a parameter square. u = 0 and u = 1 are separate vertices, so wrapping
    /// surfaces get a clean chart seam. Long thin surfaces can be cut into blocks of
    /// <paramref name="chartCols"/> x <paramref name="chartRows"/> quads, one chart each, which packs
    /// far tighter than a single strip.
    /// </summary>
    private static void AddPatch(MeshBuilder mb, int nu, int nv, Float2 uv0Tiles, PatchPoint fn, int chartCols = 0, int chartRows = 0)
    {
        if (chartCols <= 0) chartCols = nu;
        if (chartRows <= 0) chartRows = nv;

        for (int u0 = 0; u0 < nu; u0 += chartCols)
        for (int v0 = 0; v0 < nv; v0 += chartRows)
        {
            int u1 = Math.Min(nu, u0 + chartCols);
            int v1 = Math.Min(nv, v0 + chartRows);
            int stride = u1 - u0 + 1;

            mb.BeginChart();
            int first = mb.VertexCount;
            var grid = new Float3[stride * (v1 - v0 + 1)];
            var gridNormals = new Float3[grid.Length];
            for (int j = v0; j <= v1; j++)
            for (int i = u0; i <= u1; i++)
            {
                float u = i / (float)nu;
                float v = j / (float)nv;
                fn(u, v, out var p, out var n, out var lm);
                int local = (j - v0) * stride + (i - u0);
                grid[local] = p;
                gridNormals[local] = n;
                mb.Vertex(p, n, new Float2(u * uv0Tiles.X, v * uv0Tiles.Y), lm);
            }

            bool flip = WindingDisagrees(grid, gridNormals, stride, u1 - u0, v1 - v0);
            for (int j = 0; j < v1 - v0; j++)
            for (int i = 0; i < u1 - u0; i++)
            {
                int local = j * stride + i;
                int a = first + local;
                if (flip) mb.Quad(a, a + stride, a + stride + 1, a + 1);
                else mb.Quad(a, a + 1, a + stride + 1, a + stride);
            }
        }
    }

    /// <summary>
    /// True when a grid's quad winding faces away from its own vertex normals, summed over the whole
    /// grid so degenerate quads at poles and apexes cannot decide it on their own.
    /// </summary>
    private static bool WindingDisagrees(Float3[] grid, Float3[] normals, int stride, int cols, int rows)
    {
        float agreement = 0f;
        for (int j = 0; j < rows; j++)
        for (int i = 0; i < cols; i++)
        {
            int a = j * stride + i;
            var face = Float3.Cross(grid[a + 1] - grid[a], grid[a + stride] - grid[a]);
            agreement += Float3.Dot(face, normals[a]);
        }
        return agreement < 0f;
    }

    /// <summary>Quads per chart along a run of <paramref name="lengthAlong"/> so charts come out roughly square.</summary>
    private static int SquareSpan(int segments, float lengthAlong, float lengthAcross)
    {
        if (lengthAlong <= lengthAcross || segments <= 1) return segments;
        return Math.Clamp((int)MathF.Round(segments * lengthAcross / lengthAlong), 1, segments);
    }

    private static void AddSphereSurface(MeshBuilder mb, float radius, int segments, int rings,
                                         float vFrom, float vTo, Func<Float3, float> radiusOf, Float3 center = default)
    {
        AddPatch(mb, segments, rings, new Float2(segments / 6f, rings / 4f),
            (float u, float v, out Float3 p, out Float3 n, out Float2 lm) =>
        {
            float vv = vFrom + (vTo - vFrom) * v;
            float theta = vv * MathF.PI;
            float phi = u * MathF.Tau;
            var dir = new Float3(
                MathF.Sin(theta) * MathF.Cos(phi),
                MathF.Cos(theta),
                MathF.Sin(theta) * MathF.Sin(phi));
            n = dir;
            p = center + dir * radiusOf(dir);
            lm = new Float2(u * MathF.Tau * radius, vv * MathF.PI * radius);
        });
    }

    private static void AddTaperedTube(MeshBuilder mb, Float3 from, Float3 to, float startRadius, float endRadius, int segments, int rings)
    {
        var axis = to - from;
        float length = Float3.Length(axis);
        if (length < 1e-5f) return;
        axis /= length;
        var side = PerpendicularTo(axis);
        var other = Float3.Cross(axis, side);
        float slope = startRadius - endRadius;
        float avgRadius = (startRadius + endRadius) * 0.5f;

        AddPatch(mb, segments, rings, new Float2(segments / 5f, length),
            (float u, float v, out Float3 p, out Float3 n, out Float2 lm) =>
        {
            float a = u * MathF.Tau;
            var radial = side * MathF.Cos(a) + other * MathF.Sin(a);
            float r = startRadius + (endRadius - startRadius) * v;
            p = from + axis * (length * v) + radial * r;
            n = Float3.Normalize(radial * length + axis * slope);
            lm = new Float2(u * MathF.Tau * avgRadius, v * length);
        });
    }

    private static void AddDisc(MeshBuilder mb, Float3 center, Float3 normal, float radius, int segments)
    {
        var side = PerpendicularTo(normal);
        var other = Float3.Cross(normal, side);

        mb.BeginChart();
        int middle = mb.Vertex(center, normal, new Float2(0.5f, 0.5f), Float2.Zero);
        int first = mb.VertexCount;
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * MathF.Tau;
            float c = MathF.Cos(a), s = MathF.Sin(a);
            var p = center + (side * c + other * s) * radius;
            mb.Vertex(p, normal, new Float2(c * 0.5f + 0.5f, s * 0.5f + 0.5f), new Float2(c * radius, s * radius));
        }
        for (int i = 0; i < segments; i++)
            mb.Triangle(middle, first + i, first + i + 1);
    }

    /// <summary>
    /// Flat convex polygon as its own chart, projected onto its own plane for lightmap UVs. The
    /// normal comes from the winding, and the winding is reversed when it disagrees with
    /// <paramref name="outwardHint"/>, so callers only need a roughly-correct outward direction.
    /// </summary>
    private static void AddPolygon(MeshBuilder mb, Float3[] points, Float3 outwardHint)
    {
        var normal = PolygonNormal(points);
        if (Float3.Dot(normal, outwardHint) < 0f)
        {
            Array.Reverse(points);
            normal = -normal;
        }

        var side = PerpendicularTo(normal);
        var other = Float3.Cross(normal, side);

        mb.BeginChart();
        int first = mb.VertexCount;
        for (int i = 0; i < points.Length; i++)
        {
            var lm = new Float2(Float3.Dot(points[i], side), Float3.Dot(points[i], other));
            mb.Vertex(points[i], normal, lm, lm);
        }
        for (int i = 1; i + 1 < points.Length; i++)
            mb.Triangle(first, first + i, first + i + 1);
    }

    /// <summary>Face of a convex solid: the outward direction is the face centre seen from a point inside.</summary>
    private static void AddConvexFace(MeshBuilder mb, Float3[] points, Float3 insidePoint)
    {
        var centre = Float3.Zero;
        foreach (var p in points) centre += p;
        AddPolygon(mb, points, centre / points.Length - insidePoint);
    }

    private static Float3 PolygonNormal(Float3[] points)
    {
        var n = Float3.Zero;
        for (int i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            n += new Float3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        return Float3.NormalizeSafe(n, new Float3(0, 1, 0));
    }

    private static void AddBox(MeshBuilder mb, Float3 center, Float3 size)
    {
        var half = size * 0.5f;
        for (int f = 0; f < BoxFaceNormals.Length; f++)
        {
            var n = BoxFaceNormals[f];
            var side = PerpendicularTo(n);
            var other = Float3.Cross(n, side);
            float eu = MathF.Abs(Float3.Dot(half, side));
            float ev = MathF.Abs(Float3.Dot(half, other));
            var faceCenter = center + n * MathF.Abs(Float3.Dot(half, n));
            AddPolygon(mb, new[]
            {
                faceCenter - side * eu - other * ev,
                faceCenter + side * eu - other * ev,
                faceCenter + side * eu + other * ev,
                faceCenter - side * eu + other * ev,
            }, n);
        }
    }

    /// <summary>Wall slab split into strips around a rectangular opening. Hole coords are (across, up) in wall space.</summary>
    private static void AddWallWithHole(MeshBuilder mb, Float3 center, Float3 size, Axis normalAxis, Float2 holeCenter, Float2 holeSize)
    {
        float across = normalAxis == Axis.Z ? size.X : size.Z;
        float height = size.Y;
        float minAcross = -across * 0.5f, maxAcross = across * 0.5f;
        float minUp = -height * 0.5f, maxUp = height * 0.5f;

        float holeMinAcross = Math.Clamp(holeCenter.X - holeSize.X * 0.5f, minAcross, maxAcross);
        float holeMaxAcross = Math.Clamp(holeCenter.X + holeSize.X * 0.5f, minAcross, maxAcross);
        float holeMinUp = Math.Clamp(holeCenter.Y - holeSize.Y * 0.5f - height * 0.5f, minUp, maxUp);
        float holeMaxUp = Math.Clamp(holeCenter.Y + holeSize.Y * 0.5f - height * 0.5f, minUp, maxUp);

        void Strip(float a0, float a1, float u0, float u1)
        {
            float w = a1 - a0, h = u1 - u0;
            if (w <= 1e-4f || h <= 1e-4f) return;
            var offset = normalAxis == Axis.Z
                ? new Float3((a0 + a1) * 0.5f, (u0 + u1) * 0.5f, 0f)
                : new Float3(0f, (u0 + u1) * 0.5f, (a0 + a1) * 0.5f);
            var stripSize = normalAxis == Axis.Z
                ? new Float3(w, h, size.Z)
                : new Float3(size.X, h, w);
            AddBox(mb, center + offset, stripSize);
        }

        Strip(minAcross, maxAcross, minUp, holeMinUp);
        Strip(minAcross, maxAcross, holeMaxUp, maxUp);
        Strip(minAcross, holeMinAcross, holeMinUp, holeMaxUp);
        Strip(holeMaxAcross, maxAcross, holeMinUp, holeMaxUp);
    }

    /// <summary>Sweep a circle along a polyline using a parallel-transported frame. One chart, UVs in arc length.</summary>
    private static void AddTube(MeshBuilder mb, Float3[] path, bool closed, float radius, int radialSegments)
    {
        int count = path.Length;
        if (count < 2) return;
        int rings = closed ? count + 1 : count;

        var tangents = new Float3[count];
        for (int i = 0; i < count; i++)
        {
            Float3 prev = i > 0 ? path[i - 1] : (closed ? path[count - 1] : path[0]);
            Float3 next = i + 1 < count ? path[i + 1] : (closed ? path[0] : path[count - 1]);
            tangents[i] = Float3.NormalizeSafe(next - prev, new Float3(0, 1, 0));
        }

        var frameSide = new Float3[count];
        var frameOther = new Float3[count];
        var up = PerpendicularTo(tangents[0]);
        for (int i = 0; i < count; i++)
        {
            up = Float3.NormalizeSafe(up - tangents[i] * Float3.Dot(up, tangents[i]), PerpendicularTo(tangents[i]));
            frameSide[i] = up;
            frameOther[i] = Float3.Cross(tangents[i], up);
        }

        var arc = new float[rings];
        for (int i = 1; i < rings; i++)
            arc[i] = arc[i - 1] + Float3.Distance(path[(i - 1) % count], path[i % count]);

        int stride = radialSegments + 1;
        int ringsPerChart = SquareSpan(rings - 1, arc[rings - 1], MathF.Tau * radius);
        for (int block = 0; block + 1 < rings; block += ringsPerChart)
        {
            int blockEnd = Math.Min(rings - 1, block + ringsPerChart);
            mb.BeginChart();
            int first = mb.VertexCount;
            var grid = new Float3[stride * (blockEnd - block + 1)];
            var gridNormals = new Float3[grid.Length];
            for (int j = block; j <= blockEnd; j++)
            {
                int k = j % count;
                for (int i = 0; i <= radialSegments; i++)
                {
                    float a = i / (float)radialSegments * MathF.Tau;
                    var radial = frameSide[k] * MathF.Cos(a) + frameOther[k] * MathF.Sin(a);
                    int local = (j - block) * stride + i;
                    grid[local] = path[k] + radial * radius;
                    gridNormals[local] = radial;
                    mb.Vertex(grid[local], radial,
                        new Float2(i / (float)radialSegments * 2f, arc[j] * 0.5f),
                        new Float2(a * radius, arc[j]));
                }
            }

            bool flip = WindingDisagrees(grid, gridNormals, stride, radialSegments, blockEnd - block);
            for (int j = 0; j < blockEnd - block; j++)
            for (int i = 0; i < radialSegments; i++)
            {
                int a = first + j * stride + i;
                if (flip) mb.Quad(a, a + stride, a + stride + 1, a + 1);
                else mb.Quad(a, a + 1, a + stride + 1, a + stride);
            }
        }
    }

    // ---- math helpers -------------------------------------------------------------------------

    private static Float3 PerpendicularTo(Float3 n)
    {
        var reference = MathF.Abs(n.Y) < 0.9f ? new Float3(0, 1, 0) : new Float3(1, 0, 0);
        return Float3.Normalize(Float3.Cross(reference, n));
    }

    private static float EdgeFalloff(float t)
    {
        float d = Math.Clamp(MathF.Min(t, 1f - t) * 5f, 0f, 1f);
        return d * d * (3f - 2f * d);
    }

    private static float Fbm(Float2 p, int octaves)
    {
        float sum = 0f, amp = 0.5f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Noise.SNoise(p) * amp;
            p *= 2.03f;
            amp *= 0.5f;
        }
        return sum;
    }

    private static float Fbm(Float3 p, int octaves)
    {
        float sum = 0f, amp = 0.5f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Noise.SNoise(p) * amp;
            p *= 2.03f;
            amp *= 0.5f;
        }
        return sum;
    }
}
