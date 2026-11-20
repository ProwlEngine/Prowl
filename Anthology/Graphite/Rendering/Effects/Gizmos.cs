using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal struct GizmoVertex
{
    public const uint SizeInBytes = 56;

    public Float3 A;
    public Float3 B;
    public Float3 C;

    /// <summary>The width or size in pixels, the kind, the head length or outline width in pixels, and the icon texture's index.</summary>
    public Float4 Shape;
    public Color32 Color;
}

internal enum GizmoKind
{
    Line = 0,
    RoundPoint = 1,
    SquarePoint = 2,
    ArrowShaft = 3,
    ArrowHead = 4,
    ScreenCircle = 5,
    ScreenSquare = 6,
    Icon = 7,
    Fill = 8,
    FillOneSided = 9,
}

/// <summary>How the parts of a gizmo that the scene hides are drawn.</summary>
public enum HiddenStyle
{
    /// <summary>Faint, with lines dashed and fills hatched.</summary>
    Dashed,

    /// <summary>Faint.</summary>
    Faded,

    /// <summary>Not drawn.</summary>
    Gone,

    /// <summary>Drawn as if nothing were in front.</summary>
    Visible,
}

/// <summary>Ends a <see cref="GizmoBatch.Push(in Float4x4)"/> or <see cref="GizmoBatch.For"/> when disposed.</summary>
public readonly struct GizmoScope : IDisposable
{
    private readonly GizmoBatch _batch;
    private readonly bool _duration;

    internal GizmoScope(GizmoBatch batch, bool duration)
    {
        _batch = batch;
        _duration = duration;
    }

    public void Dispose()
    {
        if (_duration)
            _batch.PopDuration();
        else
            _batch.Pop();
    }
}

/// <summary>
/// Lines, points, shapes, icons and fills with sizes in pixels, built on the host thread, one record each, drawn as
/// instances of a quad. Positions are relative to <see cref="Origin"/> and go through the transform pushed last.
/// A batch added to <see cref="Gizmos"/> is uploaded again only after it changes.
/// </summary>
public sealed class GizmoBatch
{
    public static readonly Color32 AxisX = new(235, 75, 75, 255), AxisY = new(100, 210, 90, 255), AxisZ = new(80, 135, 245, 255);

    internal readonly struct Run(Texture? texture, int start, int count)
    {
        public readonly Texture? Texture = texture;
        public readonly int Start = start;
        public readonly int Count = count;
    }

    internal GizmoVertex[] Vertices = new GizmoVertex[256];
    internal double[] Expiry = new double[256];
    internal int Count;
    internal int Version;

    private readonly List<Texture> _textures = new();
    private readonly List<Float4x4> _matrices = new();
    private readonly List<float> _durations = new();
    private Float4x4 _matrix = Float4x4.Identity;
    private float _duration;
    private int _timed;

    /// <summary>Where the batch's positions are measured from, in the world.</summary>
    public Double3 Origin;

    public bool Visible = true;

    /// <summary>The world whose views draw the batch, every view the feature is in when null.</summary>
    public RenderWorld? World;

    public HiddenStyle Hidden = HiddenStyle.Dashed;

    /// <summary>How much of a hidden part shows when it is dashed or faded.</summary>
    public float HiddenAlpha = 0.35f;

    /// <summary>The length of a dash and of the gap after it, and the spacing of a hidden fill's hatching, in pixels.</summary>
    public float DashPixels = 6f;

    /// <summary>How much nearer the gizmos count as when tested against the scene, as a part of their distance, so lines on a surface stay above it.</summary>
    public float DepthBias = 0.002f;

    /// <summary>The distances from the camera the gizmos fade out between, no fade when the end is 0.</summary>
    public float FadeStart, FadeEnd;

    public int PrimitiveCount => Count;

    internal static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>Removes everything, and ends any transform or duration scope left open.</summary>
    public void Clear()
    {
        _matrices.Clear();
        _matrix = Float4x4.Identity;
        _durations.Clear();
        _duration = 0;
        if (Count == 0)
            return;
        Count = 0;
        _timed = 0;
        _textures.Clear();
        Version++;
    }

    /// <summary>The ends of a primitive drawn as a line, after its transform, false for the other kinds.</summary>
    public bool TryGetLine(int index, out Float3 a, out Float3 b)
    {
        a = b = default;
        if ((uint)index >= (uint)Count)
            return false;
        ref GizmoVertex vertex = ref Vertices[index];
        int kind = (int)vertex.Shape.Y;
        a = vertex.A;
        b = vertex.B;
        return kind == (int)GizmoKind.Line || kind == (int)GizmoKind.ArrowShaft;
    }

    /// <summary>Makes room for this many more primitives at once, so adding them does not grow the batch step by step.</summary>
    public void Reserve(int primitives)
    {
        int needed = Count + primitives;
        if (needed > Vertices.Length)
            Grow(needed);
    }

    /// <summary>Transforms everything added until the scope ends by this matrix, after the transforms pushed before it.</summary>
    public GizmoScope Push(in Float4x4 matrix)
    {
        _matrices.Add(_matrix);
        _matrix = _matrix * matrix;
        return new GizmoScope(this, false);
    }

    public GizmoScope Push(Float3 position, Quaternion rotation) => Push(Float4x4.CreateTRS(position, rotation, new Float3(1, 1, 1)));

    public GizmoScope Push(Float3 position, Quaternion rotation, Float3 scale) => Push(Float4x4.CreateTRS(position, rotation, scale));

    /// <summary>Ends the transform pushed last, as disposing its scope does.</summary>
    public void Pop()
    {
        if (_matrices.Count == 0)
            return;
        _matrix = _matrices[^1];
        _matrices.RemoveAt(_matrices.Count - 1);
    }

    /// <summary>
    /// Keeps everything added until the scope ends for this many seconds, even in an immediate batch that is cleared
    /// every frame, so one off events stay in view.
    /// </summary>
    public GizmoScope For(float seconds)
    {
        _durations.Add(_duration);
        _duration = seconds;
        return new GizmoScope(this, true);
    }

    internal void PopDuration()
    {
        if (_durations.Count == 0)
            return;
        _duration = _durations[^1];
        _durations.RemoveAt(_durations.Count - 1);
    }

    public void Line(Float3 a, Float3 b, Color32 color, float width = 1.5f) => Add(a, b, a, color, width, GizmoKind.Line);

    public void Point(Float3 position, Color32 color, float size = 6f, bool round = true)
        => Add(position, position, position, color, size, round ? GizmoKind.RoundPoint : GizmoKind.SquarePoint);

    public void Polyline(ReadOnlySpan<Float3> points, Color32 color, float width = 1.5f, bool closed = false)
    {
        for (int i = 1; i < points.Length; i++)
            Line(points[i - 1], points[i], color, width);
        if (closed && points.Length > 2)
            Line(points[^1], points[0], color, width);
    }

    /// <summary>The edges of an axis aligned box.</summary>
    public void Box(Float3 min, Float3 max, Color32 color, float width = 1.5f)
        => Box((min + max) * 0.5f, (max - min) * 0.5f, Quaternion.Identity, color, width);

    /// <summary>The edges of a rotated box.</summary>
    public void Box(Float3 center, Float3 halfExtents, Quaternion rotation, Color32 color, float width = 1.5f)
    {
        Span<Float3> c = stackalloc Float3[8];
        BoxCorners(center, halfExtents, rotation, c);
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit < 8; bit <<= 1)
            {
                if ((i & bit) == 0)
                    Line(c[i], c[i | bit], color, width);
            }
        }
    }

    /// <summary>The edges of a unit cube centred on the origin, through a matrix.</summary>
    public void Box(in Float4x4 transform, Color32 color, float width = 1.5f)
    {
        using (Push(transform))
            Box(default, new Float3(0.5f, 0.5f, 0.5f), Quaternion.Identity, color, width);
    }

    public void Circle(Float3 center, Float3 normal, float radius, Color32 color, float width = 1.5f, int segments = 48)
    {
        (Float3 u, Float3 v) = Basis(normal);
        Ring(center, u * radius, v * radius, 0, MathF.Tau, color, width, segments);
    }

    /// <summary>Part of a circle around a normal, from a direction for an angle in radians, counterclockwise looking down the normal.</summary>
    public void Arc(Float3 center, Float3 normal, Float3 from, float angle, float radius, Color32 color, float width = 1.5f, int segments = 48)
    {
        Float3 n = Float3.Normalize(normal);
        Float3 u = Float3.NormalizeSafe(from - n * Float3.Dot(from, n), Basis(n).U);
        Float3 v = Float3.Cross(n, u);
        int count = Math.Max(2, (int)MathF.Ceiling(segments * MathF.Abs(angle) / MathF.Tau));
        Ring(center, u * radius, v * radius, 0, angle, color, width, count);
    }

    /// <summary>Three rings around a sphere, one across each axis.</summary>
    public void Sphere(Float3 center, float radius, Color32 color, float width = 1.5f, int segments = 48)
    {
        Float3 x = new(radius, 0, 0), y = new(0, radius, 0), z = new(0, 0, radius);
        Ring(center, x, z, 0, MathF.Tau, color, width, segments);
        Ring(center, x, y, 0, MathF.Tau, color, width, segments);
        Ring(center, y, z, 0, MathF.Tau, color, width, segments);
    }

    /// <summary>A capsule between the centres of its two end caps.</summary>
    public void Capsule(Float3 a, Float3 b, float radius, Color32 color, float width = 1.5f, int segments = 48)
    {
        Float3 axis = b - a;
        float length = Float3.Length(axis);
        if (length < 1e-6f)
        {
            Sphere(a, radius, color, width, segments);
            return;
        }
        Float3 n = axis / length;
        (Float3 u, Float3 v) = Basis(n);
        u *= radius;
        v *= radius;
        Ring(a, u, v, 0, MathF.Tau, color, width, segments);
        Ring(b, u, v, 0, MathF.Tau, color, width, segments);
        Line(a + u, b + u, color, width);
        Line(a - u, b - u, color, width);
        Line(a + v, b + v, color, width);
        Line(a - v, b - v, color, width);
        int half = Math.Max(segments / 2, 2);
        Ring(b, u, n * radius, 0, MathF.PI, color, width, half);
        Ring(b, v, n * radius, 0, MathF.PI, color, width, half);
        Ring(a, u, -n * radius, 0, MathF.PI, color, width, half);
        Ring(a, v, -n * radius, 0, MathF.PI, color, width, half);
    }

    /// <summary>A cylinder between the centres of its two ends.</summary>
    public void Cylinder(Float3 a, Float3 b, float radius, Color32 color, float width = 1.5f, int segments = 48)
    {
        (Float3 u, Float3 v) = Basis(b - a);
        u *= radius;
        v *= radius;
        Ring(a, u, v, 0, MathF.Tau, color, width, segments);
        Ring(b, u, v, 0, MathF.Tau, color, width, segments);
        Line(a + u, b + u, color, width);
        Line(a - u, b - u, color, width);
        Line(a + v, b + v, color, width);
        Line(a - v, b - v, color, width);
    }

    /// <summary>A cone from its apex along a direction, opening by an angle in radians from its axis, as a spot light's.</summary>
    public void Cone(Float3 apex, Float3 direction, float length, float angle, Color32 color, float width = 1.5f, int segments = 48)
    {
        Float3 n = Float3.Normalize(direction);
        (Float3 u, Float3 v) = Basis(n);
        float radius = length * MathF.Tan(angle);
        Float3 center = apex + n * length;
        u *= radius;
        v *= radius;
        Ring(center, u, v, 0, MathF.Tau, color, width, segments);
        Line(apex, center + u, color, width);
        Line(apex, center - u, color, width);
        Line(apex, center + v, color, width);
        Line(apex, center - v, color, width);
    }

    /// <summary>A perspective camera's frustum, looking along the rotation's +Z with +Y up, the vertical field of view in radians.</summary>
    public void Frustum(Float3 position, Quaternion rotation, float verticalFov, float aspect, float near, float far, Color32 color, float width = 1.5f)
    {
        Span<Float3> c = stackalloc Float3[8];
        float tan = MathF.Tan(verticalFov * 0.5f);
        for (int i = 0; i < 8; i++)
        {
            float distance = (i & 4) == 0 ? near : far;
            float y = distance * tan, x = y * aspect;
            c[i] = position + rotation * new Float3((i & 1) == 0 ? -x : x, (i & 2) == 0 ? -y : y, distance);
        }
        BoxEdges(c, color, width);
    }

    /// <summary>The frustum a view projection matrix sees, between depths 0 and 1. A far plane at infinity is left open.</summary>
    public void Frustum(in Float4x4 viewProjection, Color32 color, float width = 1.5f)
    {
        Float4x4 inverse = viewProjection.Invert();
        Span<Float3> c = stackalloc Float3[8];
        Span<bool> valid = stackalloc bool[8];
        for (int i = 0; i < 8; i++)
        {
            Float4 p = inverse * new Float4((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? 0 : 1, 1);
            valid[i] = MathF.Abs(p.W) > 1e-7f;
            c[i] = valid[i] ? new Float3(p.X, p.Y, p.Z) / p.W : default;
        }
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit < 8; bit <<= 1)
            {
                if ((i & bit) == 0 && valid[i] && valid[i | bit])
                    Line(c[i], c[i | bit], color, width);
            }
        }
    }

    /// <summary>A square on a plane with an arrow along its normal.</summary>
    public void Plane(Float3 point, Float3 normal, float size, Color32 color, float width = 1.5f)
    {
        (Float3 u, Float3 v) = Basis(normal);
        float half = size * 0.5f;
        u *= half;
        v *= half;
        Line(point - u - v, point + u - v, color, width);
        Line(point + u - v, point + u + v, color, width);
        Line(point + u + v, point - u + v, color, width);
        Line(point - u + v, point - u - v, color, width);
        Arrow(point, point + Float3.Normalize(normal) * half, color, width);
    }

    /// <summary>A line with a head at its end that keeps its size in pixels.</summary>
    public void Arrow(Float3 from, Float3 to, Color32 color, float width = 1.5f, float headPixels = 12f)
    {
        Add(from, to, from, color, width, GizmoKind.ArrowShaft, headPixels);
        Add(from, to, from, color, headPixels, GizmoKind.ArrowHead, headPixels);
    }

    /// <summary>An arrow from an origin along a direction, as long as the direction.</summary>
    public void Ray(Float3 origin, Float3 direction, Color32 color, float width = 1.5f, float headPixels = 10f)
        => Arrow(origin, origin + direction, color, width, headPixels);

    /// <summary>A hit on a surface: a point, a ring on the surface and its normal, the size in world units.</summary>
    public void Hit(Float3 point, Float3 normal, Color32 color, float size = 0.25f, float width = 1.5f)
    {
        Float3 n = Float3.Normalize(normal);
        Point(point, color, 5f);
        Circle(point, n, size * 0.4f, color, width, 24);
        Arrow(point, point + n * size, color, width, 8f);
    }

    /// <summary>An axis through a point, drawn both ways, with a head on its positive end.</summary>
    public void AxisLine(Float3 center, Float3 direction, float length, Color32 color, float width = 1.5f, float headPixels = 12f)
    {
        Float3 half = Float3.NormalizeSafe(direction, default) * length;
        if (Float3.LengthSquared(half) <= 0)
            return;
        Arrow(center - half, center + half, color, width, headPixels);
    }

    /// <summary>A cross across an axis, marking a stop along it.</summary>
    public void CrossBar(Float3 position, Float3 axis, float size, Color32 color, float width = 1.5f)
    {
        (Float3 u, Float3 v) = Basis(axis);
        Line(position - u * size, position + u * size, color, width);
        Line(position - v * size, position + v * size, color, width);
    }

    /// <summary>An arc with a head on its swept end, showing a turn. A negative sweep turns the other way.</summary>
    public void SpinArrow(Float3 center, Float3 normal, Float3 from, float radius, float sweep, Color32 color, float width = 1.5f, float headPixels = 12f)
    {
        Arc(center, normal, from, sweep, radius, color, width);
        Float3 n = Float3.Normalize(normal);
        Float3 u = Float3.NormalizeSafe(from - n * Float3.Dot(from, n), Basis(n).U);
        Float3 end = center + Quaternion.AxisAngle(n, sweep) * u * radius;
        Float3 before = center + Quaternion.AxisAngle(n, sweep * 0.98f) * u * radius;
        Add(before, end, before, color, headPixels, GizmoKind.ArrowHead, headPixels);
    }

    /// <summary>
    /// An angular range around a normal from a zero direction, angles in radians: the swept arc, and a spoke to each
    /// stop unless it goes all the way round.
    /// </summary>
    public void ArcRange(Float3 center, Float3 normal, Float3 zero, float radius, float min, float max, Color32 rangeColor, Color32 limitColor, float width = 1.5f)
    {
        if (max < min)
            (min, max) = (max, min);
        Float3 n = Float3.Normalize(normal);
        Float3 start = Quaternion.AxisAngle(n, min) * zero;
        Arc(center, n, start, max - min, radius, rangeColor, width);
        if (max - min >= MathF.Tau - 1e-3f)
            return;
        Float3 end = Quaternion.AxisAngle(n, max) * zero;
        Line(center, center + Float3.Normalize(start) * radius, limitColor, width);
        Line(center, center + Float3.Normalize(end) * radius, limitColor, width);
    }

    /// <summary>
    /// A range along an axis with a cross bar at each stop. An infinite end runs on for the open length with no stop,
    /// so an unbounded range looks unbounded.
    /// </summary>
    public void LinearRange(Float3 origin, Float3 direction, float min, float max, float openLength, float stopSize, Color32 rangeColor, Color32 limitColor, float width = 1.5f)
    {
        Float3 axis = Float3.NormalizeSafe(direction, default);
        if (Float3.LengthSquared(axis) <= 0)
            return;
        bool hasMin = float.IsFinite(min), hasMax = float.IsFinite(max);
        float from = hasMin ? min : -openLength;
        float to = hasMax ? max : openLength;
        if (to < from)
            (from, to) = (to, from);
        Line(origin + axis * from, origin + axis * to, rangeColor, width);
        if (hasMin)
            CrossBar(origin + axis * from, axis, stopSize, limitColor, width);
        if (hasMax)
            CrossBar(origin + axis * to, axis, stopSize, limitColor, width);
    }

    /// <summary>
    /// A cone opening by an angle in radians from its axis, up to a half turn, its edges arcs a slant length from the
    /// apex, as a swing limit or a vision cone. Past a quarter turn the rim lies behind the apex.
    /// </summary>
    public void SlantCone(Float3 apex, Float3 axis, float angle, float length, Color32 color, float width = 1.5f, int segments = 48)
    {
        Float3 n = Float3.NormalizeSafe(axis, default);
        if (Float3.LengthSquared(n) <= 0)
            return;
        angle = Math.Clamp(angle, 0, MathF.PI);
        Circle(apex + n * (length * MathF.Cos(angle)), n, length * MathF.Sin(angle), color, width, segments);
        (Float3 u, Float3 v) = Basis(n);
        Arc(apex, v, n, angle, length, color, width, segments);
        Arc(apex, v, n, -angle, length, color, width, segments);
        Arc(apex, u, n, angle, length, color, width, segments);
        Arc(apex, u, n, -angle, length, color, width, segments);
    }

    /// <summary>Arrows along a frame's X, Y and Z axes in red, green and blue.</summary>
    public void Axes(Float3 position, Quaternion rotation, float size, float width = 2f, float headPixels = 12f)
    {
        Arrow(position, position + rotation * new Float3(size, 0, 0), AxisX, width, headPixels);
        Arrow(position, position + rotation * new Float3(0, size, 0), AxisY, width, headPixels);
        Arrow(position, position + rotation * new Float3(0, 0, size), AxisZ, width, headPixels);
    }

    /// <summary>
    /// A ground grid on the XZ plane around the origin: a line every step, every majorEvery of them in the major colour,
    /// and the two through the origin in the axis colours.
    /// </summary>
    public void Grid(float extent, float step, int majorEvery, Color32 minor, Color32 major, Color32 axisX, Color32 axisZ, float width = 1f)
    {
        int count = (int)MathF.Floor(extent / step);
        float reach = count * step;
        for (int i = -count; i <= count; i++)
        {
            float at = i * step;
            bool isMajor = majorEvery > 0 && i % majorEvery == 0;
            Line(new Float3(-reach, 0, at), new Float3(reach, 0, at), i == 0 ? axisX : isMajor ? major : minor, i == 0 ? width * 1.5f : width);
            Line(new Float3(at, 0, -reach), new Float3(at, 0, reach), i == 0 ? axisZ : isMajor ? major : minor, i == 0 ? width * 1.5f : width);
        }
    }

    /// <summary>A circle outline that keeps its radius in pixels.</summary>
    public void ScreenCircle(Float3 position, float radiusPixels, Color32 color, float width = 1.5f)
        => Add(position, position, position, color, radiusPixels * 2, GizmoKind.ScreenCircle, width);

    /// <summary>A square outline that keeps its half size in pixels.</summary>
    public void ScreenSquare(Float3 position, float halfPixels, Color32 color, float width = 1.5f)
        => Add(position, position, position, color, halfPixels * 2, GizmoKind.ScreenSquare, width);

    /// <summary>A texture facing the camera that keeps its size in pixels, tinted by a colour.</summary>
    public void Icon(Float3 position, Texture icon, float sizePixels, Color32 tint) => AddIcon(position, icon, sizePixels, tint, false);

    /// <summary>A texture facing the camera, this many world units across, tinted by a colour.</summary>
    public void WorldIcon(Float3 position, Texture icon, float size, Color32 tint) => AddIcon(position, icon, size, tint, true);

    private void AddIcon(Float3 position, Texture icon, float size, Color32 tint, bool world)
    {
        int index = _textures.IndexOf(icon);
        if (index < 0)
        {
            index = _textures.Count;
            _textures.Add(icon);
        }
        Add(position, position, position, tint, size, GizmoKind.Icon, world ? 1 : 0, index);
    }

    public void Icon(Float3 position, Texture icon, float sizePixels = 32f) => Icon(position, icon, sizePixels, new Color32(255, 255, 255, 255));

    /// <summary>A filled triangle, seen from both sides.</summary>
    public void Triangle(Float3 a, Float3 b, Float3 c, Color32 color) => Add(a, b, c, color, 0, GizmoKind.Fill);

    /// <summary>A filled quad from four corners in order around it, seen from both sides.</summary>
    public void Quad(Float3 a, Float3 b, Float3 c, Float3 d, Color32 color)
    {
        Triangle(a, b, c, color);
        Triangle(a, c, d, color);
    }

    public void SolidBox(Float3 min, Float3 max, Color32 color)
        => SolidBox((min + max) * 0.5f, (max - min) * 0.5f, Quaternion.Identity, color);

    /// <summary>A filled box, its back faces left out so its colour is even.</summary>
    public void SolidBox(Float3 center, Float3 halfExtents, Quaternion rotation, Color32 color)
    {
        Span<Float3> c = stackalloc Float3[8];
        BoxCorners(center, halfExtents, rotation, c);
        Reserve(12);
        Face(c[0], c[2], c[3], c[1], color);
        Face(c[4], c[5], c[7], c[6], color);
        Face(c[0], c[1], c[5], c[4], color);
        Face(c[2], c[6], c[7], c[3], color);
        Face(c[0], c[4], c[6], c[2], color);
        Face(c[1], c[3], c[7], c[5], color);
    }

    /// <summary>A filled sphere, its back faces left out so its colour is even.</summary>
    public void SolidSphere(Float3 center, float radius, Color32 color, int segments = 24)
    {
        segments = Math.Max(segments, 4);
        int rings = Math.Max(segments / 2, 2);
        Reserve(segments * rings * 2);
        for (int r = 0; r < rings; r++)
        {
            float t0 = MathF.PI * r / rings, t1 = MathF.PI * (r + 1) / rings;
            for (int s = 0; s < segments; s++)
            {
                float p0 = MathF.Tau * s / segments, p1 = MathF.Tau * (s + 1) / segments;
                Float3 a = center + Spherical(t0, p0) * radius, b = center + Spherical(t0, p1) * radius;
                Float3 c = center + Spherical(t1, p1) * radius, d = center + Spherical(t1, p0) * radius;
                if (r > 0)
                    Add(a, c, b, color, 0, GizmoKind.FillOneSided);
                if (r < rings - 1)
                    Add(a, d, c, color, 0, GizmoKind.FillOneSided);
            }
        }
    }

    /// <summary>A filled disc, seen from both sides.</summary>
    public void SolidDisc(Float3 center, Float3 normal, float radius, Color32 color, int segments = 48)
    {
        (Float3 u, Float3 v) = Basis(normal);
        segments = Math.Max(segments, 3);
        Reserve(segments);
        Float3 previous = center + u * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * MathF.Tau / segments;
            Float3 next = center + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            Triangle(center, previous, next, color);
            previous = next;
        }
    }

    // A point on the unit sphere from the angle down from +Y and the angle around it
    private static Float3 Spherical(float theta, float phi)
        => new(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Sin(phi));

    // Two triangles of a box face, wound so the side facing out is the front
    private void Face(Float3 a, Float3 b, Float3 c, Float3 d, Color32 color)
    {
        Add(a, c, b, color, 0, GizmoKind.FillOneSided);
        Add(a, d, c, color, 0, GizmoKind.FillOneSided);
    }

    private static void BoxCorners(Float3 center, Float3 halfExtents, Quaternion rotation, Span<Float3> corners)
    {
        for (int i = 0; i < 8; i++)
        {
            Float3 local = new((i & 1) == 0 ? -halfExtents.X : halfExtents.X, (i & 2) == 0 ? -halfExtents.Y : halfExtents.Y, (i & 4) == 0 ? -halfExtents.Z : halfExtents.Z);
            corners[i] = center + rotation * local;
        }
    }

    private void BoxEdges(ReadOnlySpan<Float3> c, Color32 color, float width)
    {
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit < 8; bit <<= 1)
            {
                if ((i & bit) == 0)
                    Line(c[i], c[i | bit], color, width);
            }
        }
    }

    private static (Float3 U, Float3 V) Basis(Float3 normal)
    {
        Float3 n = Float3.Normalize(normal);
        Float3 helper = MathF.Abs(n.Y) < 0.99f ? new Float3(0, 1, 0) : new Float3(1, 0, 0);
        Float3 u = Float3.Normalize(Float3.Cross(helper, n));
        return (u, Float3.Cross(n, u));
    }

    private void Ring(Float3 center, Float3 u, Float3 v, float start, float sweep, Color32 color, float width, int segments)
    {
        segments = Math.Max(segments, 2);
        Reserve(segments);
        Float3 previous = center + u * MathF.Cos(start) + v * MathF.Sin(start);
        for (int i = 1; i <= segments; i++)
        {
            float angle = start + i * sweep / segments;
            Float3 next = center + u * MathF.Cos(angle) + v * MathF.Sin(angle);
            Line(previous, next, color, width);
            previous = next;
        }
    }

    private void Grow(int needed)
    {
        int size = Math.Max(needed, Vertices.Length * 2);
        Array.Resize(ref Vertices, size);
        Array.Resize(ref Expiry, size);
    }

    private void Add(Float3 a, Float3 b, Float3 c, Color32 color, float size, GizmoKind kind, float extra = 0, int texture = 0)
    {
        if (Count == Vertices.Length)
            Grow(Count + 1);
        if (_matrices.Count > 0)
        {
            a = Float4x4.TransformPoint(a, _matrix);
            b = Float4x4.TransformPoint(b, _matrix);
            c = Float4x4.TransformPoint(c, _matrix);
        }
        ref GizmoVertex vertex = ref Vertices[Count];
        vertex.A = a;
        vertex.B = b;
        vertex.C = c;
        vertex.Shape = new Float4(size, (float)kind, extra, texture);
        vertex.Color = color;
        if (_duration > 0)
        {
            Expiry[Count] = Now + _duration;
            _timed++;
        }
        else
        {
            Expiry[Count] = 0;
        }
        Count++;
        Version++;
    }

    /// <summary>Drops what lasted its time, and with untimed also everything added without a duration.</summary>
    internal void Expire(double now, bool untimed)
    {
        if (_timed == 0 && !untimed)
            return;
        int kept = 0;
        _timed = 0;
        for (int i = 0; i < Count; i++)
        {
            double expiry = Expiry[i];
            bool keep = expiry == 0 ? !untimed : expiry > now;
            if (!keep)
                continue;
            if (expiry != 0)
                _timed++;
            Vertices[kept] = Vertices[i];
            Expiry[kept] = expiry;
            kept++;
        }
        if (kept == Count)
            return;
        Count = kept;
        if (Count == 0)
            _textures.Clear();
        Version++;
    }

    /// <summary>
    /// The records with icons grouped by texture after everything else, in an array rented from the shared pool that
    /// holds at least <see cref="Count"/> of them, and the runs to draw them in.
    /// </summary>
    internal GizmoVertex[] Build(out Run[] runs)
    {
        GizmoVertex[] built = ArrayPool<GizmoVertex>.Shared.Rent(Count);
        if (_textures.Count == 0)
        {
            Vertices.AsSpan(0, Count).CopyTo(built);
            runs = [new Run(null, 0, Count)];
            return built;
        }

        Span<int> counts = stackalloc int[_textures.Count + 1];
        for (int i = 0; i < Count; i++)
            counts[Slot(i)]++;
        Span<int> starts = stackalloc int[counts.Length];
        List<Run> list = new(counts.Length);
        for (int s = 0, at = 0; s < counts.Length; s++)
        {
            starts[s] = at;
            if (counts[s] > 0)
                list.Add(new Run(s == 0 ? null : _textures[s - 1], at, counts[s]));
            at += counts[s];
        }
        for (int i = 0; i < Count; i++)
            built[starts[Slot(i)]++] = Vertices[i];
        runs = list.ToArray();
        return built;
    }

    private int Slot(int i) => (int)Vertices[i].Shape.Y == (int)GizmoKind.Icon ? (int)Vertices[i].Shape.W + 1 : 0;
}

/// <summary>
/// Draws gizmo batches over a view's colour, tested against the scene's depth in the fragment stage so hidden parts can
/// be dashed, faded or dropped. It runs after the tonemapper by default, so colours are display colours and are not
/// exposed or bloomed. The view needs a feature that wrote a colour of its own before it, as a tonemapper does.
/// Single layer views only.
/// </summary>
public sealed class Gizmos : RenderFeature, IDisposable
{
    private const string Source = """
        Shared
        {
            uniform mat4 _GizmoViewProjection;
            uniform vec4 _GizmoOffset;
            uniform vec4 _GizmoScreen;
            uniform vec4 _GizmoDepth;
            uniform vec4 _GizmoStyle;
            uniform sampler2D _GizmoSceneDepth;
            uniform sampler2D _GizmoIcon;
        }
        Vertex
        {
            in vec3 POSITION0;
            in vec3 TEXCOORD0;
            in vec3 TEXCOORD2;
            in vec4 TEXCOORD1;
            in vec4 COLOR0;

            out vec4 vColor;
            out vec4 vShape;
            out vec3 vLocal;
            out float vDistance;

            // The corners of the quad every primitive is an instance of: which end, then which side
            const vec2 corners[6] = vec2[6](vec2(0.0, -1.0), vec2(0.0, 1.0), vec2(1.0, -1.0), vec2(1.0, -1.0), vec2(0.0, 1.0), vec2(1.0, 1.0));

            void main()
            {
                vec2 corner = corners[gl_VertexIndex];
                float size = TEXCOORD1.x;
                float kind = TEXCOORD1.y;
                float extra = TEXCOORD1.z;
                float halfWidth = size * 0.5;
                vec4 clip;
                vec2 local = vec2(0.0);
                vec2 pixels = vec2(0.0);
                float extent = 0.0;

                if (kind > 7.5)
                {
                    // A fill: the first three corners are the triangle, the rest fold onto its first corner
                    vec3 p = gl_VertexIndex == 1 ? TEXCOORD0 : gl_VertexIndex == 2 ? TEXCOORD2 : POSITION0;
                    clip = _GizmoViewProjection * vec4(p + _GizmoOffset.xyz, 1.0);
                }
                else if ((kind > 0.5 && kind < 2.5) || kind > 4.5)
                {
                    // Points, screen shapes and icons: a square of pixels around the position
                    clip = _GizmoViewProjection * vec4(POSITION0 + _GizmoOffset.xyz, 1.0);
                    // A world sized icon: its size in pixels at its distance
                    if (kind > 6.5 && extra > 0.5)
                        halfWidth *= _GizmoStyle.w / max(clip.w, 1e-4);
                    float pad = halfWidth + 1.0;
                    local = vec2(corner.x * 2.0 - 1.0, corner.y) * pad;
                    pixels = local;
                    extent = extra;
                }
                else
                {
                    vec4 a = _GizmoViewProjection * vec4(POSITION0 + _GizmoOffset.xyz, 1.0);
                    vec4 b = _GizmoViewProjection * vec4(TEXCOORD0 + _GizmoOffset.xyz, 1.0);

                    // Bring an end behind the camera up to it, so the line keeps its direction on screen
                    const float nearW = 1e-4;
                    if (a.w < nearW && b.w > nearW)
                        a = mix(a, b, (nearW - a.w) / (b.w - a.w));
                    else if (b.w < nearW && a.w > nearW)
                        b = mix(b, a, (nearW - b.w) / (a.w - b.w));
                    vec2 delta = (b.xy / b.w - a.xy / a.w) * 0.5 * _GizmoScreen.xy;
                    float len = length(delta);
                    vec2 direction = len > 1e-5 ? delta / len : vec2(1.0, 0.0);
                    vec2 normal = vec2(-direction.y, direction.x);
                    bool first = corner.x < 0.5;

                    if (kind > 3.5)
                    {
                        // An arrow head: its tip at b, local x across it and y along it from the tip
                        float across = extra * 0.35 + 1.0;
                        local = vec2(corner.y * across, first ? -extra - 1.0 : 1.0);
                        clip = b;
                        pixels = normal * local.x + direction * local.y;
                        extent = extra;
                    }
                    else
                    {
                        // A line: local x across it and y along it from a, with square caps half its width long
                        float pad = halfWidth + 1.0;
                        float shorten = kind > 2.5 ? min(extra * 0.6, len) : 0.0;
                        extent = len - shorten;
                        local = vec2(corner.y * pad, first ? -pad : extent + pad);
                        clip = first ? a : b;
                        pixels = normal * local.x + direction * (first ? -pad : pad - shorten);
                    }
                }

                clip.xy += pixels * 2.0 * _GizmoScreen.zw * clip.w;
                vColor = COLOR0;
                vShape = vec4(halfWidth, kind, extent, 0.0);
                // Multiplied by w so dividing by the interpolated w in the fragment stage gives values linear on screen
                vLocal = vec3(local * clip.w, clip.w);
                vDistance = clip.w;
                gl_Position = clip;
            }
        }
        Fragment
        {
            in vec4 vColor;
            in vec4 vShape;
            in vec3 vLocal;
            in float vDistance;

            layout(location = 0) out vec4 outColor;

            void main()
            {
                vec2 local = vLocal.xy / vLocal.z;
                float halfWidth = vShape.x;
                float kind = vShape.y;
                float extent = vShape.z;
                bool line = kind < 0.5 || (kind > 2.5 && kind < 3.5);
                bool fill = kind > 7.5;
                vec4 color = vColor;

                float alpha;
                if (fill)
                {
                    if (kind > 8.5 && !gl_FrontFacing)
                        discard;
                    alpha = 1.0;
                }
                else if (kind > 6.5)
                {
                    vec2 uv = clamp(local / halfWidth, -1.0, 1.0) * 0.5 + 0.5;
                    color *= texture(_GizmoIcon, uv);
                    alpha = clamp(halfWidth + 0.5 - max(abs(local.x), abs(local.y)), 0.0, 1.0);
                }
                else if (kind > 4.5)
                {
                    float radius = kind < 5.5 ? length(local) : max(abs(local.x), abs(local.y));
                    alpha = clamp(extent * 0.5 + 0.5 - abs(radius - halfWidth + extent * 0.5), 0.0, 1.0);
                }
                else if (line)
                {
                    float side = clamp(halfWidth + 0.5 - abs(local.x), 0.0, 1.0);
                    float cap = clamp(halfWidth + 0.5 - max(-local.y, local.y - extent), 0.0, 1.0);
                    alpha = side * cap;
                }
                else if (kind > 3.5)
                {
                    float across = extent * 0.35;
                    float edge = (local.y * across + abs(local.x) * extent) * inversesqrt(across * across + extent * extent);
                    alpha = clamp(0.5 - max(edge, -local.y - extent), 0.0, 1.0);
                }
                else
                {
                    float distance = kind < 1.5 ? length(local) : max(abs(local.x), abs(local.y));
                    alpha = clamp(halfWidth + 0.5 - distance, 0.0, 1.0);
                }

                // Reversed Z: a larger depth is nearer the camera
                float scene = texelFetch(_GizmoSceneDepth, ivec2(gl_FragCoord.xy * _GizmoDepth.zw), 0).r;
                if (gl_FragCoord.z * (1.0 + _GizmoDepth.y) < scene)
                {
                    alpha *= _GizmoDepth.x;
                    float dash = _GizmoStyle.x;
                    if (dash > 0.0 && line)
                    {
                        float t = mod(local.y, dash * 2.0);
                        alpha *= clamp(min(t, dash - t) + 0.5, 0.0, 1.0);
                    }
                    else if (dash > 0.0 && fill && mod(gl_FragCoord.x + gl_FragCoord.y, dash) >= dash * 0.5)
                    {
                        alpha = 0.0;
                    }
                }
                if (_GizmoStyle.z > 0.0)
                    alpha *= 1.0 - smoothstep(_GizmoStyle.y, _GizmoStyle.z, vDistance);
                if (alpha <= 0.0)
                    discard;
                outColor = vec4(color.rgb, color.a * alpha);
            }
        }
        """;

    private static readonly PropertyID s_viewProjection = "_GizmoViewProjection", s_offset = "_GizmoOffset", s_screen = "_GizmoScreen",
        s_depth = "_GizmoDepth", s_style = "_GizmoStyle", s_sceneDepth = "_GizmoSceneDepth", s_icon = "_GizmoIcon";

    private const ulong RetireFrames = 8;

    private readonly struct Draw
    {
        public readonly GizmoBatch Batch;
        public readonly RenderWorld? World;
        public readonly Double3 Origin;
        public readonly Float2 Depth;
        public readonly Float4 Style;

        public Draw(GizmoBatch batch)
        {
            Batch = batch;
            World = batch.World;
            Origin = batch.Origin;
            float hiddenAlpha = batch.Hidden switch
            {
                HiddenStyle.Gone => 0,
                HiddenStyle.Visible => 1,
                _ => batch.HiddenAlpha,
            };
            Depth = new Float2(hiddenAlpha, batch.DepthBias);
            Style = new Float4(batch.Hidden == HiddenStyle.Dashed ? batch.DashPixels : 0, batch.FadeStart, batch.FadeEnd, 0);
        }
    }

    private readonly struct Upload(GizmoBatch batch, GizmoVertex[]? vertices, int count, GizmoBatch.Run[] runs)
    {
        public readonly GizmoBatch Batch = batch;
        public readonly GizmoVertex[]? Vertices = vertices;
        public readonly int Count = count;
        public readonly GizmoBatch.Run[] Runs = runs;
    }

    private sealed class GpuBatch
    {
        public DeviceBuffer? Buffer;
        public GizmoBatch.Run[] Runs = [];
        public GizmoVertex[]? Pending;
        public int PendingCount;
        public GizmoBatch.Run[] PendingRuns = [];
    }

    private readonly GraphicsDevice _device;
    private readonly GraphicsProgram _program;
    private readonly FullscreenProgram _copy;
    private readonly Sampler _point;
    private readonly Sampler _linear;
    private readonly Texture _white;

    // Host thread
    private readonly List<GizmoBatch> _batches = new();
    private readonly Dictionary<GizmoBatch, int> _sent = new();
    private readonly List<Draw> _drawing = new();
    private readonly ConcurrentQueue<Upload> _uploads = new();
    private volatile Draw[] _frame = [];

    // Render thread
    private readonly Dictionary<GizmoBatch, GpuBatch> _gpu = new();
    private readonly List<(DeviceBuffer Buffer, ulong Frame)> _retired = new();

    public Gizmos(Renderer renderer, RenderSlot slot = RenderSlot.DisplayPost, int order = 0) : base(slot, order)
    {
        _device = renderer.Device;
        _point = renderer.Resources.PointClamp;
        _linear = renderer.Resources.LinearClamp;
        _white = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        _white.Name = "Gizmo white";
        _device.UpdateTexture<byte>(_white, [255, 255, 255, 255], TextureRegion.Whole(_white));
        _copy = renderer.CreateFullscreenProgram("void main() { outColor = Graphite_SampleSource(vUV); }", "Graphite/GizmoCopy");

        ShaderPass pass = new() { State = new PassState(), Source = Source, SourceName = "Graphite/Gizmos" };
        ShaderDescription description = pass.Variants(renderer.Compiler, _device.BackendType).Require();
        description.VertexLayouts =
        [
            new VertexLayoutDescription(0, GizmoVertex.SizeInBytes, VertexStepRate.PerInstance,
                new VertexElementDescription("POSITION0", VertexElementFormat.Float3, 0),
                new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float3, 12),
                new VertexElementDescription("TEXCOORD2", VertexElementFormat.Float3, 24),
                new VertexElementDescription("TEXCOORD1", VertexElementFormat.Float4, 36),
                new VertexElementDescription("COLOR0", VertexElementFormat.Byte4_Norm, 52)),
        ];
        description.BlendState = BlendStateDescription.SingleAlphaBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        _program = _device.ResourceFactory.CreateGraphicsProgram(description);
        _program.Name = "Graphite/Gizmos";
        Add(Immediate);
    }

    /// <summary>Gizmos for the next frame only, cleared when it is submitted, except what was added in a <see cref="GizmoBatch.For"/> scope.</summary>
    public GizmoBatch Immediate { get; } = new();

    /// <summary>
    /// Draws a batch every frame until it is removed. Host thread. A batch is uploaded again only when it changed and is
    /// visible, so refilling a hidden batch costs the render thread nothing.
    /// </summary>
    public void Add(GizmoBatch batch)
    {
        if (!_batches.Contains(batch))
            _batches.Add(batch);
    }

    public void Remove(GizmoBatch batch)
    {
        if (batch == Immediate || !_batches.Remove(batch))
            return;
        _sent.Remove(batch);
        _uploads.Enqueue(new Upload(batch, null, 0, []));
    }

    public override void Extract()
    {
        double now = GizmoBatch.Now;
        _drawing.Clear();
        foreach (GizmoBatch batch in _batches)
        {
            batch.Expire(now, false);
            if (!batch.Visible || batch.Count == 0)
                continue;
            if (!_sent.TryGetValue(batch, out int version) || version != batch.Version)
            {
                _sent[batch] = batch.Version;
                GizmoVertex[] vertices = batch.Build(out GizmoBatch.Run[] runs);
                _uploads.Enqueue(new Upload(batch, vertices, batch.Count, runs));
            }
            _drawing.Add(new Draw(batch));
        }
        Immediate.Expire(now, true);
        _frame = _drawing.Count == 0 ? [] : _drawing.ToArray();
    }

    public override bool IsActive(in FeatureView view) => view.LayerCount == 1;

    public override bool HasWork(in FeatureView view)
    {
        foreach (Draw draw in _frame)
        {
            if (draw.World == null || draw.World == view.World)
                return true;
        }
        return false;
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        // Without a colour of its own yet, as with no tonemapper, the view's colour is copied out of the scene first
        if (builder.ColorIsScene)
            builder.WriteColor();
        else
            builder.DrawToColor();
    }

    public override void Render(FeatureContext context)
    {
        if (context.Color != null)
            context.Fullscreen(_copy);
        else
            context.BeginOutput();
        CommandBuffer cmd = context.Command;
        ulong frameIndex = context.Time.FrameIndex;
        Retire(frameIndex, false);
        TakeUploads();

        ViewBlock block = context.Block;
        Double3 camera = new((double)block.CameraHigh.X + block.CameraLow.X, (double)block.CameraHigh.Y + block.CameraLow.Y, (double)block.CameraHigh.Z + block.CameraLow.Z);
        Float4x4 viewProjection = Unjittered(block);
        Texture depth = context.Depth!;
        FeatureView view = context.View;
        Float4 screen = new(view.Width, view.Height, 1f / view.Width, 1f / view.Height);
        Float2 depthScale = new((float)depth.Width / view.Width, (float)depth.Height / view.Height);
        float pixelsPerUnit = 0.5f * view.Height * block.Projection.c1.Y;

        foreach (Draw draw in _frame)
        {
            if (draw.World != null && draw.World != view.World)
                continue;
            DeviceBuffer? vertices = Resident(cmd, draw.Batch, frameIndex, out GizmoBatch.Run[] runs);
            if (vertices == null)
                continue;

            Double3 offset = draw.Origin - camera;
            PropertySet properties = new();
            properties.SetMatrix(s_viewProjection, viewProjection);
            properties.SetFloat4(s_offset, new Float4((float)offset.X, (float)offset.Y, (float)offset.Z, 0));
            properties.SetFloat4(s_screen, screen);
            properties.SetFloat4(s_depth, new Float4(draw.Depth.X, draw.Depth.Y, depthScale.X, depthScale.Y));
            properties.SetFloat4(s_style, new Float4(draw.Style.X, draw.Style.Y, draw.Style.Z, pixelsPerUnit));
            properties.SetTexture(s_sceneDepth, depth, _point);
            cmd.SetShader(_program);
            cmd.SetVertexSource(new VertexSource().SetBuffer("POSITION0", vertices));
            foreach (GizmoBatch.Run run in runs)
            {
                properties.SetTexture(s_icon, run.Texture ?? _white, _linear);
                cmd.SetProperties(properties);
                cmd.Draw(6, (uint)run.Count, 0, (uint)run.Start);
            }
        }
    }

    // The view's projection without the temporal jitter, so gizmos drawn after the resolve hold still
    private static Float4x4 Unjittered(in ViewBlock block)
    {
        Float4x4 projection = block.Projection;
        if (projection.c3.W > 0.5f)
            (projection.c3.X, projection.c3.Y) = (projection.c3.X - block.Jitter.X, projection.c3.Y - block.Jitter.Y);
        else
            (projection.c2.X, projection.c2.Y) = (projection.c2.X - block.Jitter.X, projection.c2.Y - block.Jitter.Y);
        return projection * block.View;
    }

    private void TakeUploads()
    {
        while (_uploads.TryDequeue(out Upload upload))
        {
            if (upload.Vertices == null)
            {
                if (_gpu.Remove(upload.Batch, out GpuBatch? removed))
                {
                    if (removed.Buffer != null)
                        _retired.Add((removed.Buffer, ulong.MaxValue));
                    if (removed.Pending != null)
                        ArrayPool<GizmoVertex>.Shared.Return(removed.Pending);
                }
                continue;
            }
            if (!_gpu.TryGetValue(upload.Batch, out GpuBatch? gpu))
                _gpu[upload.Batch] = gpu = new GpuBatch();
            if (gpu.Pending != null)
                ArrayPool<GizmoVertex>.Shared.Return(gpu.Pending);
            gpu.Pending = upload.Vertices;
            gpu.PendingCount = upload.Count;
            gpu.PendingRuns = upload.Runs;
        }
    }

    // Uploads a batch's new records on the first view that draws it, later views of the frame reusing them
    private DeviceBuffer? Resident(CommandBuffer cmd, GizmoBatch batch, ulong frameIndex, out GizmoBatch.Run[] runs)
    {
        runs = [];
        if (!_gpu.TryGetValue(batch, out GpuBatch? gpu))
            return null;
        if (gpu.Pending != null)
        {
            uint size = (uint)Math.Max(gpu.PendingCount, 64) * GizmoVertex.SizeInBytes;
            if (gpu.Buffer == null || gpu.Buffer.SizeInBytes < size)
            {
                if (gpu.Buffer != null)
                    _retired.Add((gpu.Buffer, frameIndex));
                gpu.Buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(size + size / 2, BufferUsage.VertexBuffer));
                gpu.Buffer.Name = "Gizmo batch";
            }
            cmd.UpdateBuffer<GizmoVertex>(gpu.Buffer, 0, gpu.Pending.AsSpan(0, gpu.PendingCount));
            ArrayPool<GizmoVertex>.Shared.Return(gpu.Pending);
            gpu.Runs = gpu.PendingRuns;
            gpu.Pending = null;
        }
        runs = gpu.Runs;
        return gpu.Buffer;
    }

    // Buffers a frame still in flight may read are freed a few frames after they were replaced
    private void Retire(ulong frameIndex, bool all)
    {
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            (DeviceBuffer buffer, ulong frame) = _retired[i];
            if (frame == ulong.MaxValue && !all)
            {
                _retired[i] = (buffer, frameIndex);
                continue;
            }
            if (all || frameIndex - frame > RetireFrames)
            {
                buffer.Dispose();
                _retired.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        Retire(0, true);
        foreach (GpuBatch gpu in _gpu.Values)
        {
            gpu.Buffer?.Dispose();
            if (gpu.Pending != null)
                ArrayPool<GizmoVertex>.Shared.Return(gpu.Pending);
        }
        _gpu.Clear();
        _program.Dispose();
        _copy.Dispose();
        _white.Dispose();
    }
}
