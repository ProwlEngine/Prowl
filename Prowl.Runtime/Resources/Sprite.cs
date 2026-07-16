// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

/// <summary>
/// Named pivot presets. <see cref="SpriteAlignment.Custom"/> uses the normalized pivot value directly.
/// </summary>
public enum SpriteAlignment
{
    Center,
    TopLeft,
    TopCenter,
    TopRight,
    LeftCenter,
    RightCenter,
    BottomLeft,
    BottomCenter,
    BottomRight,
    Custom
}

/// <summary>
/// A pixel-space rectangle into a source texture. The origin is the bottom-left corner,
/// matching UV space (so <c>uv = new(X / texWidth, Y / texHeight)</c>). Editor tooling that works
/// in top-left display space is responsible for converting.
/// </summary>
[FixedEchoStructure]
public struct SpriteRect
{
    public int X;
    public int Y;
    public int Width;
    public int Height;

    public SpriteRect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public readonly int MaxX => X + Width;
    public readonly int MaxY => Y + Height;

    public override readonly string ToString() => $"({X}, {Y}, {Width}, {Height})";
}

/// <summary>
/// A single drawable sprite: a rectangle into a source texture plus the intrinsic data needed to place
/// and render it (pivot, pixels-per-unit, 9-slice border, cached geometry, optional secondary maps and
/// a polygon outline). It carries no draw mode consumers (<c>SpriteRenderer</c>, UI) pick Simple / Sliced /
/// Tiled / Filled per instance, so the same sprite can be drawn several ways.
/// </summary>
public sealed class Sprite : EngineObject
{
    /// <summary>The source texture this sprite is cut from.</summary>
    public AssetRef<Texture2D> Texture;

    /// <summary>The pixel rectangle inside <see cref="Texture"/> (bottom-left origin).</summary>
    public SpriteRect Rect;

    /// <summary>Normalized pivot within <see cref="Rect"/>. (0,0) is bottom-left, (1,1) top-right.</summary>
    public Float2 Pivot = new(0.5f, 0.5f);

    /// <summary>How many source pixels map to one world unit.</summary>
    public float PixelsPerUnit = 100f;

    /// <summary>9-slice border widths in pixels, ordered (Left, Top, Right, Bottom) to match the UI system. Zero means no border.</summary>
    public Float4 Border = default;

    /// <summary>
    /// Named secondary maps aligned to the same <see cref="Rect"/> (e.g. "_NormalMap", "_MaskMap").
    /// Sampled by lit / masked sprite shaders.
    /// </summary>
    public Dictionary<string, AssetRef<Texture2D>> SecondaryTextures = new();

    /// <summary>Cached mesh positions in local units, pivot-relative (x right, y up, z assumed 0).</summary>
    public Float2[] Vertices = Array.Empty<Float2>();

    /// <summary>Cached UVs (0..1 texture space), parallel to <see cref="Vertices"/>.</summary>
    public Float2[] UV = Array.Empty<Float2>();

    /// <summary>Cached triangle indices into <see cref="Vertices"/>.</summary>
    public ushort[] Indices = Array.Empty<ushort>();

    /// <summary>
    /// Polygon outline paths in local units (pivot-relative), one array per closed loop. Produced by the
    /// tight-mesh tracer and reusable for a future 2D physics collider. Null for a plain quad sprite.
    /// </summary>
    public Float2[][]? PhysicsShape;

    public Sprite() : base("Sprite") { }

    /// <summary>True if any border edge is non-zero (i.e. the sprite can be 9-sliced).</summary>
    public bool HasBorder { get { EnsureNotDisposed(); return Border.X > 0 || Border.Y > 0 || Border.Z > 0 || Border.W > 0; } }

    /// <summary>The sprite's size in world units (rect size divided by pixels-per-unit).</summary>
    public Float2 SizeInUnits { get { EnsureNotDisposed(); return new(Rect.Width / PixelsPerUnit, Rect.Height / PixelsPerUnit); } }

    private Float2 _boundsMin;
    private Float2 _boundsMax;

    /// <summary>Minimum corner of the local-space geometry bounds (pivot-relative).</summary>
    public Float2 BoundsMin { get { EnsureNotDisposed(); return _boundsMin; } private set => _boundsMin = value; }

    /// <summary>Maximum corner of the local-space geometry bounds (pivot-relative).</summary>
    public Float2 BoundsMax { get { EnsureNotDisposed(); return _boundsMax; } private set => _boundsMax = value; }

    /// <summary>
    /// Builds a simple full-rect quad (two triangles) for this sprite, resolving the texture dimensions
    /// from <see cref="Texture"/>. Tight-mesh generation is a separate step that overwrites this geometry.
    /// </summary>
    public void BuildQuadGeometry()
    {
        EnsureNotDisposed();
        Texture2D? tex = Texture.Res;
        int texW = (int)(tex?.Width ?? (uint)Math.Max(1, Rect.MaxX));
        int texH = (int)(tex?.Height ?? (uint)Math.Max(1, Rect.MaxY));
        BuildQuadGeometry(texW, texH);
    }

    /// <summary>
    /// Builds a simple full-rect quad using the given texture dimensions. Used by the importer, where the
    /// texture is already resolved, to avoid a redundant asset load.
    /// </summary>
    public void BuildQuadGeometry(int textureWidth, int textureHeight)
    {
        EnsureNotDisposed();
        float w = Rect.Width / PixelsPerUnit;
        float h = Rect.Height / PixelsPerUnit;

        // Pivot offset in units, measured from the bottom-left corner.
        float px = Pivot.X * w;
        float py = Pivot.Y * h;

        float x0 = -px, y0 = -py;
        float x1 = w - px, y1 = h - py;

        Vertices = new Float2[]
        {
            new(x0, y0), // bottom-left
            new(x1, y0), // bottom-right
            new(x1, y1), // top-right
            new(x0, y1), // top-left
        };

        float u0 = Rect.X / (float)textureWidth;
        float v0 = Rect.Y / (float)textureHeight;
        float u1 = Rect.MaxX / (float)textureWidth;
        float v1 = Rect.MaxY / (float)textureHeight;

        UV = new Float2[]
        {
            new(u0, v0),
            new(u1, v0),
            new(u1, v1),
            new(u0, v1),
        };

        Indices = new ushort[] { 0, 1, 2, 2, 3, 0 };

        PhysicsShape = new[] { new[] { Vertices[0], Vertices[1], Vertices[2], Vertices[3] } };

        BoundsMin = new Float2(x0, y0);
        BoundsMax = new Float2(x1, y1);
    }

    /// <summary>
    /// Replaces this sprite's geometry with a tight silhouette mesh from <see cref="SpriteMeshTracer"/>.
    /// Traced coordinates are in the rect's pixel space (bottom-left origin); they map to pivot-relative
    /// local units for the vertices and to atlas UVs. The simplified outline becomes <see cref="PhysicsShape"/>.
    /// Falls back to a quad if the traced mesh is degenerate.
    /// </summary>
    public void BuildTightGeometry(SpriteMeshTracer.TracedMesh traced, int textureWidth, int textureHeight)
    {
        EnsureNotDisposed();
        if (traced.Vertices.Length < 3 || traced.Indices.Length < 3)
        {
            BuildQuadGeometry(textureWidth, textureHeight);
            return;
        }

        float ppu = PixelsPerUnit > 0 ? PixelsPerUnit : 100f;
        float pivX = Pivot.X * Rect.Width;
        float pivY = Pivot.Y * Rect.Height;

        Vertices = new Float2[traced.Vertices.Length];
        UV = new Float2[traced.Vertices.Length];
        for (int i = 0; i < traced.Vertices.Length; i++)
        {
            Float2 px = traced.Vertices[i];
            Vertices[i] = new Float2((px.X - pivX) / ppu, (px.Y - pivY) / ppu);
            UV[i] = new Float2((Rect.X + px.X) / textureWidth, (Rect.Y + px.Y) / textureHeight);
        }
        Indices = traced.Indices;

        PhysicsShape = new Float2[traced.Contours.Count][];
        for (int c = 0; c < traced.Contours.Count; c++)
        {
            Float2[] contour = traced.Contours[c];
            var path = new Float2[contour.Length];
            for (int i = 0; i < contour.Length; i++)
                path[i] = new Float2((contour[i].X - pivX) / ppu, (contour[i].Y - pivY) / ppu);
            PhysicsShape[c] = path;
        }

        RecalculateBounds();
    }

    /// <summary>Recomputes <see cref="BoundsMin"/>/<see cref="BoundsMax"/> from the current vertices.</summary>
    public void RecalculateBounds()
    {
        EnsureNotDisposed();
        if (Vertices.Length == 0)
        {
            BoundsMin = BoundsMax = default;
            return;
        }

        Float2 min = Vertices[0];
        Float2 max = Vertices[0];
        for (int i = 1; i < Vertices.Length; i++)
        {
            Float2 v = Vertices[i];
            if (v.X < min.X) min.X = v.X;
            if (v.Y < min.Y) min.Y = v.Y;
            if (v.X > max.X) max.X = v.X;
            if (v.Y > max.Y) max.Y = v.Y;
        }
        BoundsMin = min;
        BoundsMax = max;
    }

    /// <summary>Creates a quad sprite for the given texture rectangle.</summary>
    public static Sprite Create(Texture2D texture, SpriteRect rect, Float2 pivot, float pixelsPerUnit = 100f, string? name = null)
    {
        var sprite = new Sprite
        {
            Texture = texture,
            Rect = rect,
            Pivot = pivot,
            PixelsPerUnit = pixelsPerUnit,
        };
        if (name != null)
            sprite.Name = name;
        sprite.BuildQuadGeometry((int)texture.Width, (int)texture.Height);
        return sprite;
    }

    /// <summary>Creates a quad sprite covering the entire texture, pivoted at its center.</summary>
    public static Sprite CreateFullTexture(Texture2D texture, float pixelsPerUnit = 100f)
        => Create(texture, new SpriteRect(0, 0, (int)texture.Width, (int)texture.Height), new Float2(0.5f, 0.5f), pixelsPerUnit, texture.Name);

    /// <summary>A minimal spec for building a quad sprite - the shared primitive used by built-in default
    /// sprites and the editor's sprite importer (which layers slicing / tight-mesh on top).</summary>
    public readonly struct SpriteDef
    {
        public readonly SpriteRect Rect;
        public readonly Float2 Pivot;
        public readonly float PixelsPerUnit;
        public readonly Float4 Border;

        public SpriteDef(SpriteRect rect, Float2 pivot, float pixelsPerUnit, Float4 border)
        {
            Rect = rect;
            Pivot = pivot;
            PixelsPerUnit = pixelsPerUnit;
            Border = border;
        }
    }

    /// <summary>Builds a quad sprite from a spec against the given texture (no tight-mesh tracing).</summary>
    public static Sprite Build(Texture2D texture, in SpriteDef def, string? name = null)
    {
        var sprite = new Sprite
        {
            Texture = texture,
            Rect = def.Rect,
            Pivot = def.Pivot,
            PixelsPerUnit = def.PixelsPerUnit,
            Border = def.Border,
        };
        if (name != null)
            sprite.Name = name;
        sprite.BuildQuadGeometry((int)texture.Width, (int)texture.Height);
        return sprite;
    }

    /// <summary>
    /// Gets the shared instance of a built-in default sprite. Returns the same instance across the app;
    /// callers needing a mutable copy should build their own <see cref="Sprite"/>.
    /// </summary>
    public static Sprite LoadDefault(DefaultSprite sprite)
    {
        if (BuiltInAssets.Get(BuiltInAssets.GuidFor(sprite)) is Sprite cached)
            return cached;
        return ParseDefault(sprite);
    }

    /// <summary>
    /// Raw build of a default sprite, invoked by <see cref="BuiltInAssets"/> on first cache miss.
    /// Public callers should use <see cref="LoadDefault"/>.
    /// </summary>
    private readonly struct DefaultSpriteDef
    {
        public readonly DefaultTexture Texture;
        public readonly SpriteAlignment Pivot;
        public readonly float PixelsPerUnit;
        public readonly Float4 Border;

        public DefaultSpriteDef(DefaultTexture texture, SpriteAlignment pivot, float pixelsPerUnit, Float4 border)
        {
            Texture = texture;
            Pivot = pivot;
            PixelsPerUnit = pixelsPerUnit;
            Border = border;
        }
    }

    // Declarative table of built-in sprites. Built-in (embedded) textures have no .meta/importer, so their
    // "Single + border" sprite config lives here in code and is built via the same Sprite.Build primitive.
    private static readonly Dictionary<DefaultSprite, DefaultSpriteDef> s_defaultSprites = new()
    {
        [DefaultSprite.UIPanel] = new DefaultSpriteDef(DefaultTexture.UIPanel, SpriteAlignment.Center, 100f, new Float4(32f, 32f, 32f, 32f)),
    };

    internal static Sprite ParseDefault(DefaultSprite sprite)
    {
        if (!s_defaultSprites.TryGetValue(sprite, out DefaultSpriteDef def))
            throw new ArgumentException($"Unknown default sprite: {sprite}");

        Texture2D tex = Texture2D.LoadDefault(def.Texture);
        var rect = new SpriteRect(0, 0, (int)tex.Width, (int)tex.Height);
        return Build(tex, new SpriteDef(rect, PivotFromAlignment(def.Pivot), def.PixelsPerUnit, def.Border), sprite.ToString());
        // AssetID/AssetPath/Name are set by BuiltInAssets.Get after this returns.
    }

    /// <summary>Resolves a named pivot preset to a normalized pivot. <paramref name="custom"/> is returned for <see cref="SpriteAlignment.Custom"/>.</summary>
    public static Float2 PivotFromAlignment(SpriteAlignment alignment, Float2 custom = default) => alignment switch
    {
        SpriteAlignment.Center => new Float2(0.5f, 0.5f),
        SpriteAlignment.TopLeft => new Float2(0f, 1f),
        SpriteAlignment.TopCenter => new Float2(0.5f, 1f),
        SpriteAlignment.TopRight => new Float2(1f, 1f),
        SpriteAlignment.LeftCenter => new Float2(0f, 0.5f),
        SpriteAlignment.RightCenter => new Float2(1f, 0.5f),
        SpriteAlignment.BottomLeft => new Float2(0f, 0f),
        SpriteAlignment.BottomCenter => new Float2(0.5f, 0f),
        SpriteAlignment.BottomRight => new Float2(1f, 0f),
        _ => custom,
    };
}

#region Tight-mesh tracing (SpriteMeshTracer)

/// <summary>
/// Turns a sprite's alpha silhouette into a tight polygon mesh: it traces the opaque region's outline,
/// simplifies it, and triangulates it. The outline paths are returned alongside the mesh so a future 2D
/// physics collider can reuse the exact same geometry. All coordinates are in pixel space with a
/// bottom-left origin (matching <see cref="SpriteRect"/> / UV space).
/// </summary>
public static class SpriteMeshTracer
{
    /// <summary>The traced result in pixel space: the simplified outline loops plus a triangulated mesh.</summary>
    public sealed class TracedMesh
    {
        public List<Float2[]> Contours = new();
        public Float2[] Vertices = Array.Empty<Float2>();
        public ushort[] Indices = Array.Empty<ushort>();
    }

    /// <summary>
    /// Traces + simplifies + triangulates a binary alpha mask. <paramref name="alpha"/> is row-major with a
    /// bottom-left origin (length <c>width*height</c>); a texel counts as opaque when its value is &gt;=
    /// <paramref name="alphaThreshold"/>. <paramref name="simplifyTolerance"/> is the maximum deviation (in
    /// pixels) allowed when collapsing the staircase outline into straight edges; higher = fewer vertices.
    /// </summary>
    public static TracedMesh Generate(byte[] alpha, int width, int height, byte alphaThreshold = 1, float simplifyTolerance = 1.5f)
    {
        var result = new TracedMesh();
        if (alpha == null || width <= 0 || height <= 0 || alpha.Length < width * height)
            return result;

        List<Float2[]> raw = TraceContours(alpha, width, height, alphaThreshold);

        var verts = new List<Float2>();
        var indices = new List<ushort>();
        var tris = new List<int>();

        foreach (Float2[] contour in raw)
        {
            Float2[] simplified = Simplify(contour, simplifyTolerance);
            if (simplified.Length < 3)
                continue;

            result.Contours.Add(simplified);

            tris.Clear();
            if (!Triangulate(simplified, tris))
                continue;

            int baseIdx = verts.Count;
            if (baseIdx + simplified.Length > ushort.MaxValue)
                break; // keep indices 16-bit; sprites never realistically hit this

            verts.AddRange(simplified);
            foreach (int i in tris)
                indices.Add((ushort)(baseIdx + i));
        }

        result.Vertices = verts.ToArray();
        result.Indices = indices.ToArray();
        return result;
    }

    /// <summary>
    /// Extracts the opaque region's boundary as closed loops of grid-corner points. Each unit boundary
    /// between an opaque and a transparent (or out-of-bounds) cell becomes an oriented edge with the opaque
    /// side on its left; the edges are then stitched into loops. Outer loops wind counter-clockwise.
    /// </summary>
    public static List<Float2[]> TraceContours(byte[] alpha, int width, int height, byte alphaThreshold = 1)
    {
        bool Opaque(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && alpha[y * width + x] >= alphaThreshold;

        static long Corner(int x, int y) => ((long)x << 32) | (uint)y;

        // start-corner -> end-corner. A well-formed boundary uses each start corner once.
        var edges = new Dictionary<long, long>(256);
        void AddEdge(int sx, int sy, int ex, int ey) => edges[Corner(sx, sy)] = Corner(ex, ey);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Opaque(x, y)) continue;

                // Cell (x,y) covers the square [x,x+1] x [y,y+1]. Emit an edge along each side whose
                // neighbor is empty, oriented so the opaque cell stays on the left (CCW outer winding).
                if (!Opaque(x, y - 1)) AddEdge(x, y, x + 1, y);         // bottom
                if (!Opaque(x + 1, y)) AddEdge(x + 1, y, x + 1, y + 1); // right
                if (!Opaque(x, y + 1)) AddEdge(x + 1, y + 1, x, y + 1); // top
                if (!Opaque(x - 1, y)) AddEdge(x, y + 1, x, y);         // left
            }
        }

        var contours = new List<Float2[]>();
        var visited = new HashSet<long>();

        foreach (long startKey in edges.Keys)
        {
            if (visited.Contains(startKey)) continue;

            var loop = new List<Float2>();
            long cur = startKey;
            int guard = edges.Count + 4;

            while (guard-- > 0)
            {
                if (!visited.Add(cur)) break;
                loop.Add(new Float2(cur >> 32, (int)(cur & 0xffffffff)));
                if (!edges.TryGetValue(cur, out long next) || next == startKey)
                    break;
                cur = next;
            }

            if (loop.Count >= 3)
                contours.Add(loop.ToArray());
        }

        return contours;
    }

    /// <summary>Douglas-Peucker simplification of a closed polygon, keeping deviation within <paramref name="tolerance"/> pixels.</summary>
    public static Float2[] Simplify(Float2[] points, float tolerance)
    {
        int n = points.Length;
        if (n < 4 || tolerance <= 0f)
            return points;

        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        SimplifySegment(points, 0, n - 1, tolerance, keep);

        var outPts = new List<Float2>(n);
        for (int i = 0; i < n; i++)
            if (keep[i]) outPts.Add(points[i]);

        return outPts.Count >= 3 ? outPts.ToArray() : points;
    }

    private static void SimplifySegment(Float2[] pts, int first, int last, float tol, bool[] keep)
    {
        if (last <= first + 1) return;

        float maxDist = -1f;
        int index = -1;
        Float2 a = pts[first], b = pts[last];

        for (int i = first + 1; i < last; i++)
        {
            float d = PerpendicularDistance(pts[i], a, b);
            if (d > maxDist) { maxDist = d; index = i; }
        }

        if (maxDist > tol && index > 0)
        {
            keep[index] = true;
            SimplifySegment(pts, first, index, tol, keep);
            SimplifySegment(pts, index, last, tol, keep);
        }
    }

    private static float PerpendicularDistance(Float2 p, Float2 a, Float2 b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-9f)
            return MathF.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

        float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        float projX = a.X + t * dx, projY = a.Y + t * dy;
        return MathF.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
    }

    /// <summary>
    /// Ear-clipping triangulation of a simple polygon. Appends triangle indices (into <paramref name="poly"/>)
    /// to <paramref name="outIndices"/> and returns false if the polygon is degenerate. Holes aren't handled;
    /// a hole loop just triangulates as a solid, which is harmless since its texels are transparent.
    /// </summary>
    public static bool Triangulate(Float2[] poly, List<int> outIndices)
    {
        int n = poly.Length;
        if (n < 3) return false;

        // Work on a mutable index ring; force counter-clockwise so the ear test is consistent.
        var v = new List<int>(n);
        if (SignedArea(poly) < 0f)
            for (int i = n - 1; i >= 0; i--) v.Add(i);
        else
            for (int i = 0; i < n; i++) v.Add(i);

        int guard = n * n;
        int count = v.Count;
        while (count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < count; i++)
            {
                int i0 = v[(i - 1 + count) % count];
                int i1 = v[i];
                int i2 = v[(i + 1) % count];

                Float2 a = poly[i0], b = poly[i1], c = poly[i2];

                if (Cross(a, b, c) <= 0f)
                    continue; // reflex or colinear - not an ear

                bool hasInside = false;
                for (int j = 0; j < count; j++)
                {
                    int vj = v[j];
                    if (vj == i0 || vj == i1 || vj == i2) continue;
                    if (PointInTriangle(poly[vj], a, b, c)) { hasInside = true; break; }
                }
                if (hasInside) continue;

                outIndices.Add(i0);
                outIndices.Add(i1);
                outIndices.Add(i2);
                v.RemoveAt(i);
                count--;
                clipped = true;
                break;
            }
            if (!clipped) break; // no ear found (numerical issue) - bail with what we have
        }

        if (count == 3)
        {
            outIndices.Add(v[0]);
            outIndices.Add(v[1]);
            outIndices.Add(v[2]);
        }

        return outIndices.Count >= 3;
    }

    private static float SignedArea(Float2[] p)
    {
        float area = 0f;
        for (int i = 0; i < p.Length; i++)
        {
            Float2 a = p[i], b = p[(i + 1) % p.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area * 0.5f;
    }

    // Positive when (a,b,c) turn counter-clockwise.
    private static float Cross(Float2 a, Float2 b, Float2 c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool PointInTriangle(Float2 p, Float2 a, Float2 b, Float2 c)
    {
        float d1 = Cross(a, b, p);
        float d2 = Cross(b, c, p);
        float d3 = Cross(c, a, p);
        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNeg && hasPos);
    }
}

#endregion
