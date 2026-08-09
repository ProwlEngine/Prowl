// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Prowl.Graphite;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Tasks;
using Prowl.Vector;

namespace Prowl.Runtime;

public enum LogSeverity
{
    Success = 1 << 0,
    Normal = 1 << 1,
    Warning = 1 << 2,
    Error = 1 << 3,
    Exception = 1 << 4
}


public delegate void OnLog(string message, DebugStackTrace? stackTrace, LogSeverity logSeverity);


public record DebugStackFrame(string FileName, int? Line = null, int? Column = null, string? Method = null)
{
    public override string ToString()
    {
        string locSuffix = Line != null ? Column != null ? $"({Line},{Column})" : $"({Line})" : "";

        if (!string.IsNullOrEmpty(Method))
            return $"In {Method} at {FileName}{locSuffix}";
        else
            return $"At {FileName}{locSuffix}";
    }

}


public record DebugStackTrace(params DebugStackFrame[] StackFrames)
{
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Diagnostic-only path: in a trimmed build, missing method metadata gracefully degrades to a stack frame without method info.")]
    public static explicit operator DebugStackTrace(StackTrace stackTrace)
    {
        DebugStackFrame[] stackFrames = new DebugStackFrame[stackTrace.FrameCount];

        for (int i = 0; i < stackFrames.Length; i++)
        {
            StackFrame srcFrame = stackTrace.GetFrame(i);

            MethodBase? m = srcFrame.GetMethod();
            string? method = m != null ? $"{m.DeclaringType?.Name}.{m.Name}" : null;

            stackFrames[i] = new DebugStackFrame(srcFrame.GetFileName(), srcFrame.GetFileLineNumber(), srcFrame.GetFileColumnNumber(), method);
        }

        return new DebugStackTrace(stackFrames);
    }


    public override string ToString()
    {
        StringBuilder sb = new();

        for (int i = 0; i < StackFrames.Length; i++)
            sb.AppendLine($"\t{StackFrames[i]}");

        return sb.ToString();
    }
}


public static class Debug
{
    public static event OnLog? OnLog;

    public static void Log(object message)
        => Log(message.ToString(), LogSeverity.Normal);

    public static void Log(string message)
        => Log(message, LogSeverity.Normal);

    public static void LogWarning(object message)
        => Log(message.ToString(), LogSeverity.Warning);

    public static void LogWarning(string message)
        => Log(message, LogSeverity.Warning);

    public static void LogError(object message)
        => Log(message.ToString(), LogSeverity.Error);

    public static void LogError(string message)
        => Log(message, LogSeverity.Error);

    public static void LogSuccess(object message)
        => Log(message.ToString(), LogSeverity.Success);

    public static void LogSuccess(string message)
        => Log(message, LogSeverity.Success);

    #region Deduplicated logging

    // Conditions that recur every frame - a collider re-baking, a query handed a NaN, a step callback
    // throwing - are worth hearing about once, not sixty times a second. The first report for an id gets
    // through and the rest are counted and dropped, which is what keeps callers from each inventing
    // their own "did I already warn" bool.
    private static readonly ConcurrentDictionary<string, int> s_reportCounts = new();

    /// <summary>
    /// Logs <paramref name="message"/> the first time this <paramref name="id"/> is seen, then stays
    /// quiet however often it recurs. Ids are cleared when play mode changes and when scripts reload, so
    /// a condition you have just fixed reports again on the next run rather than staying silent.
    /// <para/>
    /// Use a stable, specific id: <c>"MeshCollider.RelativeRebuild"</c>, not the message text.
    /// </summary>
    public static void LogOnce(string id, string message)
    {
        if (ShouldReport(id)) Log(Tag(id, message), LogSeverity.Normal);
    }

    /// <inheritdoc cref="LogOnce"/>
    public static void LogWarningOnce(string id, string message)
    {
        if (ShouldReport(id)) Log(Tag(id, message), LogSeverity.Warning);
    }

    /// <inheritdoc cref="LogOnce"/>
    public static void LogErrorOnce(string id, string message)
    {
        if (ShouldReport(id)) Log(Tag(id, message), LogSeverity.Error);
    }

    /// <summary>
    /// How many times an id has been reported, suppressed repeats included. Zero if it never has.
    /// Useful for surfacing "this happened 4,000 times" in a summary rather than in the log itself.
    /// </summary>
    public static int GetReportCount(string id)
        => s_reportCounts.TryGetValue(id, out int count) ? count : 0;

    /// <summary>
    /// Forgets every id, so recurring conditions report again. Driven by the play mode toggle and by
    /// script reloads; call it manually only if you are deliberately re-running something.
    /// </summary>
    public static void ClearReportedOnce() => s_reportCounts.Clear();

    // Counted rather than flagged, so GetReportCount can say how bad it got. Concurrent because logs
    // come off physics worker threads as well as the main one.
    private static bool ShouldReport(string id)
        => string.IsNullOrEmpty(id) || s_reportCounts.AddOrUpdate(id, 1, static (_, count) => count + 1) == 1;

    // Says both what the condition was and that the repeats are not being shown, so nobody reads a
    // single line and concludes it happened once.
    private static string Tag(string id, string message) => $"{message} [{id}, repeats suppressed]";

    /// <summary>
    /// Reports when a main-thread-only API is called from somewhere else, and says whether it was.
    /// Deduplicated per caller, because an offending call site usually repeats every frame.
    /// <para/>
    /// The point is to fail where the rule was broken. Touching scene or engine state off-thread
    /// usually surfaces as a corrupted read or a crash somewhere unrelated, long after the call that
    /// caused it. Callers that can bail should act on the result; the rest at least get a name.
    /// <para/>
    /// Always true before a <see cref="MainThreadContext"/> is installed (tests, tools, early startup),
    /// since there is no engine thread to be off.
    /// </summary>
    /// <param name="member">Defaults to the calling member.</param>
    /// <returns>True when the caller is on the main thread.</returns>
    public static bool EnsureMainThread([CallerMemberName] string member = "")
    {
        if (MainThreadContext.OnMainThread) return true;

        LogErrorOnce($"MainThread.{member}",
            $"{member} must be called on the main thread, and was called from thread {Environment.CurrentManagedThreadId}. Marshal the call back to the main thread.");
        return false;
    }

    #endregion

    public static void LogException(Exception exception)
    {
        ConsoleColor prevColor = Console.ForegroundColor;

        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine(exception.Message);

        if (exception.InnerException != null)
            Console.WriteLine(exception.InnerException.Message);

        DebugStackTrace trace = (DebugStackTrace)new StackTrace(exception.InnerException ?? exception, true);

        Console.WriteLine(trace.ToString());

        Console.ForegroundColor = prevColor;

        OnLog?.Invoke(exception.Message + "\n" + (exception.InnerException?.Message ?? ""), trace, LogSeverity.Exception);
    }

    // NOTE : StackTrace is pretty fast on modern .NET, so it's nice to keep it on by default, since it gives useful line numbers for debugging purposes.
    // For reference, getting a stack trace on a modern machine takes around 15 μs at a depth of 15.
    public static void Log(string message, LogSeverity logSeverity, DebugStackTrace? customTrace = null)
    {
        ConsoleColor prevColor = Console.ForegroundColor;

        Console.ForegroundColor = logSeverity switch
        {
            LogSeverity.Success => ConsoleColor.Green,
            LogSeverity.Warning => ConsoleColor.Yellow,
            LogSeverity.Error => ConsoleColor.Red,
            LogSeverity.Exception => ConsoleColor.DarkRed,
            _ => ConsoleColor.White
        };

        Console.WriteLine(message);

        if (customTrace != null)
        {
            Console.WriteLine(customTrace.ToString());
            OnLog?.Invoke(message, customTrace, logSeverity);
        }
        else
        {
            StackTrace trace = new(2, true);
            OnLog?.Invoke(message, (DebugStackTrace)trace, logSeverity);
        }

        Console.ForegroundColor = prevColor;
    }

    public static void If(bool condition, string message = "")
    {
        if (condition)
            throw new Exception(message);
    }

    public static void IfNull(object value, string message = "")
    {
        if (value is null)
            throw new Exception(message);
    }

    public static void IfNullOrEmpty(string value, string message = "")
    {
        if (string.IsNullOrEmpty(value))
            throw new Exception(message);
    }

    internal static void ErrorGuard(Action value)
    {
        try
        {
            value();
        }
        catch (Exception e)
        {
            LogError(e.Message);
        }
    }

    public static void Assert(bool condition, string? message)
        => System.Diagnostics.Debug.Assert(condition, message);

    public static void Assert(bool condition)
        => System.Diagnostics.Debug.Assert(condition);

    #region Gizmos

    private static readonly GizmoBuilder s_gizmoBuilder = new();

    public static void ClearGizmos()
    {
        s_gizmoBuilder.Clear();
    }

    public static (GizmoBuilder.Batch? wire, GizmoBuilder.Batch? solid) UploadGizmos()
    {
        return s_gizmoBuilder.Upload();
    }

    public static List<GizmoBuilder.IconDrawCall> GetGizmoIcons()
    {
        return s_gizmoBuilder.GetIcons();
    }

    public static void PushMatrix(Float4x4 matrix)
    {
        s_gizmoBuilder.PushMatrix(matrix);
    }

    public static void PopMatrix()
    {
        s_gizmoBuilder.PopMatrix();
    }

    public static void DrawLine(Float3 start, Float3 end, Color color) => s_gizmoBuilder.DrawLine(start, end, color);
    public static void DrawTriangle(Float3 a, Float3 b, Float3 c, Color color) => s_gizmoBuilder.DrawTriangle(a, b, c, color);
    public static void DrawWireCube(Float3 center, Float3 halfExtents, Color color) => s_gizmoBuilder.DrawWireCube(center, halfExtents, color);
    public static void DrawCube(Float3 center, Float3 halfExtents, Color color) => s_gizmoBuilder.DrawCube(center, halfExtents, color);
    public static void DrawWireCircle(Float3 center, Float3 normal, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawCircle(center, normal, radius, color, segments);

    /// <summary>
    /// Part of a circle, starting at the <paramref name="from"/> direction and sweeping
    /// <paramref name="sweepRadians"/> about <paramref name="normal"/>. Negative sweeps go the other way.
    /// </summary>
    public static void DrawWireArc(Float3 center, Float3 normal, Float3 from, float radius, float sweepRadians, Color color, int segments = 24) => s_gizmoBuilder.DrawWireArc(center, normal, from, radius, sweepRadians, color, segments);
    public static void DrawWireSphere(Float3 center, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawWireSphere(center, radius, color, segments);
    public static void DrawSphere(Float3 center, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawSphere(center, radius, color, segments);
    public static void DrawWireCone(Float3 start, Float3 direction, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawWireCone(start, direction, radius, color, segments);
    public static void DrawWireCapsule(Float3 point1, Float3 point2, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawWireCapsule(point1, point2, radius, color, segments);
    public static void DrawWireCylinder(Float3 center, Quaternion rotation, float radius, float height, Color color, int segments = 16) => s_gizmoBuilder.DrawWireCylinder(center, rotation, radius, height, color, segments);
    public static void DrawArrow(Float3 start, Float3 direction, Color color) => s_gizmoBuilder.DrawArrow(start, direction, color);

    public static void DrawIcon(Texture2D icon, Float3 center, float scale, Color color) => s_gizmoBuilder.DrawIcon(icon, center, scale, color);

    /// <summary>
    /// A line broken into dashes, so it reads as a relationship between two things rather than as an
    /// edge of something.
    /// </summary>
    public static void DrawDashedLine(Float3 from, Float3 to, Color color, int dashes = 8)
    {
        if (dashes < 1) dashes = 1;
        Float3 step = (to - from) / (dashes * 2 - 1);

        for (int i = 0; i < dashes; i++)
            DrawLine(from + step * (i * 2), from + step * (i * 2 + 1), color);
    }

    /// <summary>
    /// An axis through a point, drawn both ways because an axis has no near end, with an arrow to give
    /// it a positive direction.
    /// </summary>
    public static void DrawAxisLine(Float3 center, Float3 direction, float length, Color color)
    {
        Float3 half = Float3.Normalize(direction) * length;
        if (Float3.LengthSquared(half) <= 0.0f) return;

        DrawLine(center - half, center + half, color);
        DrawArrow(center, half, color);
    }

    /// <summary>A bar across an axis, for marking a hard stop along it.</summary>
    public static void DrawCrossBar(Float3 position, Float3 axis, float size, Color color)
    {
        PerpendicularAxes(axis, out Float3 u, out Float3 v);
        DrawLine(position - u * size, position + u * size, color);
        DrawLine(position - v * size, position + v * size, color);
    }

    /// <summary>
    /// An angular range about <paramref name="normal"/>, measured from the <paramref name="zero"/>
    /// direction: the swept band, plus a spoke at each end unless it goes all the way round.
    /// </summary>
    public static void DrawArcRange(Float3 center, Float3 normal, Float3 zero, float radius,
        float minDegrees, float maxDegrees, Color rangeColor, Color limitColor)
    {
        float min = minDegrees * Maths.Deg2Rad;
        float max = maxDegrees * Maths.Deg2Rad;
        if (max < min) (min, max) = (max, min);

        Float3 start = Quaternion.AxisAngle(Float3.Normalize(normal), min) * zero;
        DrawWireArc(center, normal, start, radius, max - min, rangeColor, 32);

        if (max - min >= Maths.PI * 2 - 1e-3f) return; // a full turn has no stops to mark

        Float3 end = Quaternion.AxisAngle(Float3.Normalize(normal), max) * zero;
        DrawLine(center, center + Float3.Normalize(start) * radius, limitColor);
        DrawLine(center, center + Float3.Normalize(end) * radius, limitColor);
    }

    /// <summary>
    /// A range along an axis. Ends that are infinite run on for <paramref name="openLength"/> with no
    /// stop drawn, which is what makes an unbounded range look unbounded.
    /// </summary>
    public static void DrawLinearRange(Float3 origin, Float3 direction, float minDistance, float maxDistance,
        float openLength, float stopSize, Color rangeColor, Color limitColor)
    {
        Float3 axis = Float3.Normalize(direction);
        if (Float3.LengthSquared(axis) <= 0.0f) return;

        bool hasMin = float.IsFinite(minDistance);
        bool hasMax = float.IsFinite(maxDistance);

        float from = hasMin ? minDistance : -openLength;
        float to = hasMax ? maxDistance : openLength;
        if (to < from) (from, to) = (to, from);

        DrawLine(origin + axis * from, origin + axis * to, rangeColor);

        if (hasMin) DrawCrossBar(origin + axis * from, axis, stopSize, limitColor);
        if (hasMax) DrawCrossBar(origin + axis * to, axis, stopSize, limitColor);
    }

    /// <summary>
    /// An arc with an arrowhead on the swept end, for showing a rotation direction. A negative
    /// <paramref name="sweepRadians"/> turns the other way.
    /// </summary>
    public static void DrawSpinArrow(Float3 center, Float3 normal, Float3 from, float radius, float sweepRadians, Color color)
    {
        DrawWireArc(center, normal, from, radius, sweepRadians, color, 24);

        Float3 end = Quaternion.AxisAngle(Float3.Normalize(normal), sweepRadians) * (Float3.Normalize(from) * radius);
        Float3 tangent = Float3.Cross(Float3.Normalize(normal), end);
        if (Float3.LengthSquared(tangent) <= 0.0f) return;

        DrawArrow(center + end, Float3.Normalize(tangent) * Maths.Sign(sweepRadians) * (radius * 0.5f), color);
    }

    /// <summary>Three rings around a point, the shorthand for "free to rotate any way".</summary>
    public static void DrawGimbal(Float3 center, float radius, Color color, int segments = 20)
    {
        DrawWireCircle(center, Float3.UnitX, radius, color, segments);
        DrawWireCircle(center, Float3.UnitY, radius, color, segments);
        DrawWireCircle(center, Float3.UnitZ, radius, color, segments);
    }

    /// <summary>An orientation tripod, X/Y/Z in red/green/blue.</summary>
    public static void DrawAxes(Float3 center, Quaternion rotation, float scale)
    {
        DrawLine(center, center + rotation * Float3.UnitX * scale, Color.Red);
        DrawLine(center, center + rotation * Float3.UnitY * scale, Color.Green);
        DrawLine(center, center + rotation * Float3.UnitZ * scale, Color.Blue);
    }

    /// <inheritdoc cref="DrawAxes(Float3, Quaternion, float)"/>
    public static void DrawAxes(Float3 center, Quaternion rotation, float scale, Color color)
    {
        DrawLine(center, center + rotation * Float3.UnitX * scale, color);
        DrawLine(center, center + rotation * Float3.UnitY * scale, color);
        DrawLine(center, center + rotation * Float3.UnitZ * scale, color);
    }

    /// <summary>A square patch of plane with a cross through it, so it reads as a surface.</summary>
    public static void DrawWirePlane(Float3 center, Float3 normal, float extent, Color color)
    {
        PerpendicularAxes(normal, out Float3 u, out Float3 v);

        Float3 a = center + (u + v) * extent;
        Float3 b = center + (u - v) * extent;
        Float3 c = center - (u + v) * extent;
        Float3 d = center - (u - v) * extent;

        DrawLine(a, b, color);
        DrawLine(b, c, color);
        DrawLine(c, d, color);
        DrawLine(d, a, color);
        DrawLine((a + b) * 0.5f, (c + d) * 0.5f, color);
        DrawLine((b + c) * 0.5f, (d + a) * 0.5f, color);
    }

    /// <summary>
    /// A cone described by the half-angle it opens to from its axis, for swing limits, spotlights and
    /// vision cones. <paramref name="length"/> is the slant length, so every drawn edge is that far from
    /// the apex.
    /// </summary>
    public static void DrawWireConeAngle(Float3 apex, Float3 axis, float angleDegrees, float length, Color color, int segments = 24)
    {
        Float3 dir = Float3.Normalize(axis);
        if (Float3.LengthSquared(dir) <= 0.0f) return;

        float angle = Maths.Clamp(angleDegrees, 0.0f, 180.0f) * Maths.Deg2Rad;

        // Rim on the sphere: it slides behind the apex past 90 degrees, which is exactly right.
        Float3 rimCenter = apex + dir * (length * Maths.Cos(angle));
        float radius = length * Maths.Sin(angle);
        DrawWireCircle(rimCenter, dir, radius, color, segments);

        // Four meridians out to that rim. Rotating the axis about one perpendicular sweeps it toward the
        // other, so the two perpendiculars and their negatives give the four quarters.
        PerpendicularAxes(dir, out Float3 u, out Float3 v);
        int arcSegments = Maths.Max(2, segments / 2);

        DrawWireArc(apex, v, dir, length, angle, color, arcSegments);
        DrawWireArc(apex, v, dir, length, -angle, color, arcSegments);
        DrawWireArc(apex, u, dir, length, angle, color, arcSegments);
        DrawWireArc(apex, u, dir, length, -angle, color, arcSegments);
    }

    /// <summary>
    /// Two unit vectors perpendicular to <paramref name="axis"/> and to each other. The reference is
    /// chosen away from the axis, since crossing with a fixed one collapses to zero when they align.
    /// </summary>
    public static void PerpendicularAxes(Float3 axis, out Float3 u, out Float3 v)
    {
        Float3 n = Float3.Normalize(axis);
        if (Float3.LengthSquared(n) <= 0.0f) n = Float3.UnitY;

        Float3 reference = Maths.Abs(n.Y) < 0.9f ? Float3.UnitY : Float3.UnitX;
        u = Float3.Normalize(Float3.Cross(reference, n));
        v = Float3.Normalize(Float3.Cross(n, u));
    }

    #endregion

}

public class GizmoBuilder
{
    private struct MeshData
    {
        public List<Float3> Vertices = [];
        public List<Float2> Uvs = [];
        public List<Color32> Colors = [];
        public List<int> Indices = [];

        public MeshData()
        {
        }

        public readonly void Clear()
        {
            Vertices.Clear();
            Uvs.Clear();
            Colors.Clear();
            Indices.Clear();
        }
    }

    private MeshData _wireData = new();
    private MeshData _solidData = new();
    private readonly Batch _wire = new(PrimitiveTopology.LineList);
    private readonly Batch _solid = new(PrimitiveTopology.TriangleList);

    public struct IconDrawCall
    {
        public Texture2D Texture;
        public Float3 Center;
        public float Scale;
        public Color Color;
    }

    private List<IconDrawCall> _icons = [];

    private Stack<Float4x4> _matrix4X4s = new();


    public void Clear()
    {
        _wireData.Clear();
        _solidData.Clear();

        //_wire?.Clear();
        //_solid?.Clear();

        _icons.Clear();

        _matrix4X4s.Clear();
    }

    private void AddLine(Float3 a, Float3 b, Color color)
    {
        if (_matrix4X4s.Count > 0)
        {
            Float4x4 m = _matrix4X4s.Peek();
            a = Float4x4.TransformPoint(a, m);
            b = Float4x4.TransformPoint(b, m);
        }

        int index = _wireData.Vertices.Count;
        _wireData.Vertices.Add(a);
        _wireData.Vertices.Add(b);

        _wireData.Colors.Add(color);
        _wireData.Colors.Add(color);

        _wireData.Indices.Add(index);
        _wireData.Indices.Add(index + 1);
    }

    private void AddTriangle(Float3 a, Float3 b, Float3 c, Float2 a_uv, Float2 b_uv, Float2 c_uv, Color color)
    {
        if (_matrix4X4s.Count > 0)
        {
            Float4x4 m = _matrix4X4s.Peek();
            a = Float4x4.TransformPoint(a, m);
            b = Float4x4.TransformPoint(b, m);
            c = Float4x4.TransformPoint(c, m);
        }

        int index = _solidData.Vertices.Count;

        _solidData.Vertices.Add(a);
        _solidData.Vertices.Add(b);
        _solidData.Vertices.Add(c);

        _solidData.Uvs.Add(a_uv);
        _solidData.Uvs.Add(b_uv);
        _solidData.Uvs.Add(c_uv);

        _solidData.Colors.Add(color);
        _solidData.Colors.Add(color);
        _solidData.Colors.Add(color);

        _solidData.Indices.Add(index);
        _solidData.Indices.Add(index + 1);
        _solidData.Indices.Add(index + 2);
    }

    public void PushMatrix(Float4x4 matrix)
    {
        _matrix4X4s.Push(matrix);
    }

    public void PopMatrix()
    {
        _matrix4X4s.Pop();
    }

    public void DrawLine(Float3 start, Float3 end, Color color) => AddLine(start, end, color);

    public void DrawTriangle(Float3 a, Float3 b, Float3 c, Color color) => AddTriangle(a, b, c, Float2.Zero, Float2.Zero, Float2.Zero, color);

    public void DrawWireCube(Float3 center, Float3 halfExtents, Color color)
    {
        Float3[] vertices = [
            new Float3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y - halfExtents.Y, center.Z + halfExtents.Z),
            new Float3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z + halfExtents.Z),
            new Float3(center.X - halfExtents.X, center.Y + halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z),
            new Float3(center.X - halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z),
        ];

        AddLine(vertices[0], vertices[1], color);
        AddLine(vertices[1], vertices[2], color);
        AddLine(vertices[2], vertices[3], color);
        AddLine(vertices[3], vertices[0], color);

        AddLine(vertices[4], vertices[5], color);
        AddLine(vertices[5], vertices[6], color);
        AddLine(vertices[6], vertices[7], color);
        AddLine(vertices[7], vertices[4], color);

        AddLine(vertices[0], vertices[4], color);
        AddLine(vertices[1], vertices[5], color);
        AddLine(vertices[2], vertices[6], color);
        AddLine(vertices[3], vertices[7], color);
    }

    public void DrawCube(Float3 center, Float3 halfExtents, Color color)
    {
        Float3[] vertices = [
            new Float3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y - halfExtents.Y, center.Z + halfExtents.Z),
            new Float3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z + halfExtents.Z),
            new Float3(center.X - halfExtents.X, center.Y + halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z - halfExtents.Z),
            new Float3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z),
            new Float3(center.X - halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z),
        ];

        Float2[] uvs = [
            new Float2(0, 0),
            new Float2(1, 0),
            new Float2(1, 1),
            new Float2(0, 1),
        ];

        AddTriangle(vertices[0], vertices[1], vertices[2], uvs[0], uvs[1], uvs[2], color);
        AddTriangle(vertices[0], vertices[2], vertices[3], uvs[0], uvs[2], uvs[3], color);

        AddTriangle(vertices[4], vertices[6], vertices[5], uvs[0], uvs[1], uvs[2], color);
        AddTriangle(vertices[4], vertices[7], vertices[6], uvs[0], uvs[2], uvs[3], color);

        AddTriangle(vertices[0], vertices[3], vertices[7], uvs[0], uvs[1], uvs[2], color);
        AddTriangle(vertices[0], vertices[7], vertices[4], uvs[0], uvs[2], uvs[3], color);

        AddTriangle(vertices[1], vertices[5], vertices[6], uvs[0], uvs[1], uvs[2], color);
        AddTriangle(vertices[1], vertices[6], vertices[2], uvs[0], uvs[2], uvs[3], color);

        AddTriangle(vertices[3], vertices[2], vertices[6], uvs[0], uvs[1], uvs[2], color);
        AddTriangle(vertices[3], vertices[6], vertices[7], uvs[0], uvs[2], uvs[3], color);

        AddTriangle(vertices[0], vertices[4], vertices[5], uvs[0], uvs[1], uvs[2], color);
        AddTriangle(vertices[0], vertices[5], vertices[1], uvs[0], uvs[2], uvs[3], color);
    }

    public void DrawWireSphere(Float3 center, float radius, Color color, int segments = 16)
    {
        float step = MathF.PI * 2 / segments;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;

            Float3 a = new(Maths.Cos(angle1) * radius + center.X,
                            Maths.Sin(angle1) * radius + center.Y,
                            center.Z
                        );

            Float3 b = new(Maths.Cos(angle2) * radius + center.X,
                            Maths.Sin(angle2) * radius + center.Y,
                            center.Z
                        );

            AddLine(a, b, color);
        }

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;

            Float3 a = new(Maths.Cos(angle1) * radius + center.X,
                            center.Y,
                            Maths.Sin(angle1) * radius + center.Z
                        );

            Float3 b = new(Maths.Cos(angle2) * radius + center.X,
                            center.Y,
                            Maths.Sin(angle2) * radius + center.Z
                        );

            AddLine(a, b, color);
        }

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;

            Float3 a = new(center.X,
                            Maths.Cos(angle1) * radius + center.Y,
                            Maths.Sin(angle1) * radius + center.Z
                        );

            Float3 b = new(center.X,
                            Maths.Cos(angle2) * radius + center.Y,
                            Maths.Sin(angle2) * radius + center.Z
                        );

            AddLine(a, b, color);
        }
    }

    public void DrawCircle(Float3 center, Float3 normal, float radius, Color color, int segments)
    {
        PlaneBasis(normal, out Float3 u, out Float3 v);
        DrawArcInternal(center, u, v, radius, 0.0f, MathF.PI * 2, color, segments);
    }

    public void DrawWireArc(Float3 center, Float3 normal, Float3 from, float radius, float sweepRadians, Color color, int segments)
    {
        PlaneBasis(normal, out Float3 u, out Float3 v);

        // Re-seat the basis so the arc starts exactly at `from`, which is what lets callers line an arc
        // up with a limit angle instead of an arbitrary reference direction.
        Float3 flattened = from - Float3.Dot(from, Float3.Normalize(normal)) * Float3.Normalize(normal);
        if (Float3.LengthSquared(flattened) > 1e-12f)
        {
            u = Float3.Normalize(flattened);
            v = Float3.Normalize(Float3.Cross(normal, u));
        }

        DrawArcInternal(center, u, v, radius, 0.0f, sweepRadians, color, Maths.Max(1, segments));
    }

    private void DrawArcInternal(Float3 center, Float3 u, Float3 v, float radius, float start, float sweep, Color color, int segments)
    {
        float step = sweep / segments;
        Float3 previous = center + radius * (Maths.Cos(start) * u + Maths.Sin(start) * v);

        for (int i = 1; i <= segments; i++)
        {
            float angle = start + i * step;
            Float3 point = center + radius * (Maths.Cos(angle) * u + Maths.Sin(angle) * v);
            AddLine(previous, point, color);
            previous = point;
        }
    }

    private static void PlaneBasis(Float3 normal, out Float3 u, out Float3 v)
    {
        Float3 n = Float3.Normalize(normal);
        if (Float3.LengthSquared(n) <= 0.0f) n = Float3.UnitY;

        Float3 reference = Maths.Abs(n.Y) < 0.9f ? Float3.UnitY : Float3.UnitX;
        u = Float3.Normalize(Float3.Cross(reference, n));
        v = Float3.Normalize(Float3.Cross(n, u));
    }

    public void DrawSphere(Float3 center, float radius, Color color, int segments = 16)
    {
        int latitudeSegments = segments;
        int longitudeSegments = segments * 2;

        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            float theta1 = lat * MathF.PI / latitudeSegments;
            float theta2 = (lat + 1) * MathF.PI / latitudeSegments;

            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                float phi1 = lon * 2 * MathF.PI / longitudeSegments;
                float phi2 = (lon + 1) * 2 * MathF.PI / longitudeSegments;

                Float3 v1 = CalculatePointOnSphere(theta1, phi1, radius, center);
                Float3 v2 = CalculatePointOnSphere(theta1, phi2, radius, center);
                Float3 v3 = CalculatePointOnSphere(theta2, phi1, radius, center);
                Float3 v4 = CalculatePointOnSphere(theta2, phi2, radius, center);

                // First triangle
                AddTriangle(v1, v2, v3, Float2.Zero, Float2.Zero, Float2.Zero, color);

                // Second triangle
                AddTriangle(v2, v4, v3, Float2.Zero, Float2.Zero, Float2.Zero, color);
            }
        }
    }

    private Float3 CalculatePointOnSphere(float theta, float phi, float radius, Float3 center)
    {
        float x = Maths.Sin(theta) * Maths.Cos(phi);
        float y = Maths.Cos(theta);
        float z = Maths.Sin(theta) * Maths.Sin(phi);

        return new Float3(
            x * radius + center.X,
            y * radius + center.Y,
            z * radius + center.Z
        );
    }

    public void DrawWireCone(Float3 start, Float3 direction, float radius, Color color, int segments = 16)
    {
        float step = MathF.PI * 2 / segments;
        Float3 tip = start + direction;

        // Normalize the direction vector
        Float3 dir = Float3.Normalize(direction);

        // Find perpendicular vectors
        Float3 u = GetPerpendicularVector(dir);
        Float3 v = Float3.Cross(dir, u);

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;

            // Calculate circle points using the perpendicular vectors
            Float3 a = start + radius * (Maths.Cos(angle1) * u + Maths.Sin(angle1) * v);
            Float3 b = start + radius * (Maths.Cos(angle2) * u + Maths.Sin(angle2) * v);

            AddLine(a, b, color);
            if (i == 0 || i == segments / 4 || i == segments / 2 || i == segments * 3 / 4)
                AddLine(a, tip, color);
        }
    }

    public void DrawWireCapsule(Float3 point1, Float3 point2, float radius, Color color, int segments = 16)
    {
        // Calculate the axis of the capsule
        Float3 axis = point2 - point1;
        float height = Float3.Length(axis);

        if (height < 1e-6)
        {
            // Degenerate case: draw a sphere
            DrawWireSphere(point1, radius, color, segments);
            return;
        }

        Float3 dir = axis / height;

        // Find perpendicular vectors
        Float3 u = GetPerpendicularVector(dir);
        Float3 v = Float3.Cross(dir, u);

        float step = MathF.PI * 2 / segments;

        // Draw the cylindrical body (circles at both ends and connecting lines)
        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;

            // Circle at point1
            Float3 a1 = point1 + radius * (Maths.Cos(angle1) * u + Maths.Sin(angle1) * v);
            Float3 b1 = point1 + radius * (Maths.Cos(angle2) * u + Maths.Sin(angle2) * v);

            // Circle at point2
            Float3 a2 = point2 + radius * (Maths.Cos(angle1) * u + Maths.Sin(angle1) * v);
            Float3 b2 = point2 + radius * (Maths.Cos(angle2) * u + Maths.Sin(angle2) * v);

            AddLine(a1, b1, color);
            AddLine(a2, b2, color);

            // Connecting lines every quarter
            if (i % (segments / 4) == 0)
            {
                AddLine(a1, a2, color);
            }
        }

        // Draw hemisphere at point1 (bottom cap)
        for (int i = 0; i < segments / 2; i++)
        {
            float theta1 = MathF.PI / 2 + i * MathF.PI / segments;
            float theta2 = MathF.PI / 2 + (i + 1) * MathF.PI / segments;

            for (int j = 0; j < segments; j++)
            {
                float phi1 = j * 2 * MathF.PI / segments;
                float phi2 = (j + 1) * 2 * MathF.PI / segments;

                Float3 v1 = point1 + radius * (Maths.Sin(theta1) * Maths.Cos(phi1) * u + Maths.Sin(theta1) * Maths.Sin(phi1) * v + Maths.Cos(theta1) * dir);
                Float3 v2 = point1 + radius * (Maths.Sin(theta1) * Maths.Cos(phi2) * u + Maths.Sin(theta1) * Maths.Sin(phi2) * v + Maths.Cos(theta1) * dir);
                Float3 v3 = point1 + radius * (Maths.Sin(theta2) * Maths.Cos(phi1) * u + Maths.Sin(theta2) * Maths.Sin(phi1) * v + Maths.Cos(theta2) * dir);

                if (j % (segments / 4) == 0)
                {
                    AddLine(v1, v3, color);
                }
                if (i == 0 || i == segments / 2 - 1)
                {
                    AddLine(v1, v2, color);
                }
            }
        }

        // Draw hemisphere at point2 (top cap)
        for (int i = 0; i < segments / 2; i++)
        {
            float theta1 = i * MathF.PI / segments;
            float theta2 = (i + 1) * MathF.PI / segments;

            for (int j = 0; j < segments; j++)
            {
                float phi1 = j * 2 * MathF.PI / segments;
                float phi2 = (j + 1) * 2 * MathF.PI / segments;

                Float3 v1 = point2 + radius * (Maths.Sin(theta1) * Maths.Cos(phi1) * u + Maths.Sin(theta1) * Maths.Sin(phi1) * v + Maths.Cos(theta1) * dir);
                Float3 v2 = point2 + radius * (Maths.Sin(theta1) * Maths.Cos(phi2) * u + Maths.Sin(theta1) * Maths.Sin(phi2) * v + Maths.Cos(theta1) * dir);
                Float3 v3 = point2 + radius * (Maths.Sin(theta2) * Maths.Cos(phi1) * u + Maths.Sin(theta2) * Maths.Sin(phi1) * v + Maths.Cos(theta2) * dir);

                if (j % (segments / 4) == 0)
                {
                    AddLine(v1, v3, color);
                }
                if (i == 0 || i == segments / 2 - 1)
                {
                    AddLine(v1, v2, color);
                }
            }
        }
    }

    public void DrawWireCylinder(Float3 center, Quaternion rotation, float radius, float height, Color color, int segments)
    {
        Float3 up = rotation * Float3.UnitY;
        Float3 forward = rotation * Float3.UnitZ;
        Float3 right = rotation * Float3.UnitX;
        Float3 topCenter = center + (up * (height / 2));
        Float3 bottomCenter = center - (up * (height / 2));
        float step = MathF.PI * 2 / segments;
        // Draw top and bottom circles
        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;
            Float3 topA = topCenter + radius * (Maths.Cos(angle1) * right + Maths.Sin(angle1) * forward);
            Float3 topB = topCenter + radius * (Maths.Cos(angle2) * right + Maths.Sin(angle2) * forward);
            Float3 bottomA = bottomCenter + radius * (Maths.Cos(angle1) * right + Maths.Sin(angle1) * forward);
            Float3 bottomB = bottomCenter + radius * (Maths.Cos(angle2) * right + Maths.Sin(angle2) * forward);
            AddLine(topA, topB, color);
            AddLine(bottomA, bottomB, color);
            // Connecting lines every quarter
            if (i % (segments / 4) == 0)
            {
                AddLine(topA, bottomA, color);
            }
        }
    }

    private Float3 GetPerpendicularVector(Float3 v)
    {
        Float3 result;
        if (Maths.Abs(v.X) > 0.1f)
            result = new Float3(v.Y, -v.X, 0);
        else if (Maths.Abs(v.Y) > 0.1f)
            result = new Float3(0, v.Z, -v.Y);
        else
            result = new Float3(-v.Z, 0, v.X);
        return Float3.Normalize(result);
    }

    public void DrawArrow(Float3 start, Float3 direction, Color color)
    {
        float length = Float3.Length(direction);
        if (length <= 0.0f) return;

        Float3 axis = direction / length;
        AddLine(start, start + direction, color);

        // Head sized from the arrow rather than fixed in world units, so a short arrow is not all head
        // and a long one still reads as an arrow.
        float head = length * 0.18f;
        DrawWireCone(start + direction - axis * head, axis * head, head * 0.4f, color, 4);
    }

    public void DrawIcon(Texture2D icon, Float3 center, float scale, Color color) => _icons.Add(new IconDrawCall { Texture = icon, Center = center, Scale = scale, Color = color });

    public (Batch? wire, Batch? solid) Upload()
    {
        _wire.Upload(_wireData.Vertices, _wireData.Colors, _wireData.Indices);
        _solid.Upload(_solidData.Vertices, _solidData.Colors, _solidData.Indices);

        return (
            _wire.HasData ? _wire : null,
            _solid.HasData ? _solid : null
            );
    }

    public List<IconDrawCall> GetIcons()
    {
        return _icons;
    }

    /// <summary>
    /// A ring-buffered vertex source for one gizmo primitive class (wire or solid). The gizmo
    /// geometry is rebuilt on the CPU every frame, so it is streamed into per-frame-in-flight
    /// <see cref="StreamingBuffer"/>s rather than a single <see cref="DeviceBuffer"/>, which would
    /// race with frames still being read by the GPU.
    /// </summary>
    public sealed class Batch : IVertexSource
    {
        private static readonly VertexAttributeID s_position = "POSITION0";

        private readonly PrimitiveTopology _topology;

        private DeviceBuffer? _positions;
        private DeviceBuffer? _colors;
        private DeviceBuffer? _indices;

        private uint _positionCapacity;
        private uint _colorCapacity;
        private uint _indexCapacity;

        // The concrete ring-slot buffers captured at upload time. The backend queries the vertex source
        // at command replay, which runs on a separate thread that may observe a newer CurrentFrame (and
        // thus a different ring slot) than the one we uploaded into; binding these captured references
        // instead of re-reading StreamingBuffer.Current keeps the draw pinned to the uploaded slot.
        private DeviceBuffer? _boundPositions;
        private DeviceBuffer? _boundColors;
        private DeviceBuffer? _boundIndices;

        private Color[] _colorScratch = [];
        private uint _indexCount;

        public Batch(PrimitiveTopology topology) => _topology = topology;

        public bool HasData => _indexCount > 0;

        PrimitiveTopology IVertexSource.Topology => _topology;

        public void Upload(List<Float3> positions, List<Color32> colors, List<int> indices)
        {
            _indexCount = (uint)indices.Count;
            if (_indexCount == 0)
                return;

            int vertexCount = positions.Count;

            // COLOR0 is a float4 in the shader; widen the packed Color32 stream into a reused scratch array.
            if (_colorScratch.Length < vertexCount)
                _colorScratch = new Color[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                _colorScratch[i] = (Color)colors[i];

            EnsureBuffer(ref _positions, ref _positionCapacity, (uint)(vertexCount * 12), BufferUsage.VertexBuffer | BufferUsage.Dynamic);
            EnsureBuffer(ref _colors, ref _colorCapacity, (uint)(vertexCount * 16), BufferUsage.VertexBuffer | BufferUsage.Dynamic);
            EnsureBuffer(ref _indices, ref _indexCapacity, _indexCount * sizeof(uint), BufferUsage.IndexBuffer | BufferUsage.Dynamic);

            _boundPositions = _positions!;
            _boundColors = _colors!;
            _boundIndices = _indices!;

            // Dynamic buffers are host-visible and persistently mapped, so the device write copies straight
            // into mapped GPU memory with no staging buffer or copy command (unlike CommandBuffer.UpdateBuffer).
            Graphics.Device.UpdateBuffer(_boundPositions, 0, CollectionsMarshal.AsSpan(positions));
            Graphics.Device.UpdateBuffer(_boundColors, 0, _colorScratch.AsSpan(0, vertexCount));
            Graphics.Device.UpdateBuffer(_boundIndices, 0, MemoryMarshal.Cast<int, uint>(CollectionsMarshal.AsSpan(indices)));
        }

        void IVertexSource.ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
        {
            DeviceBuffer buffer = layout.Elements[0].Name == s_position ? _boundPositions! : _boundColors!;
            binding = new VertexBinding(buffer);
        }

        bool IVertexSource.TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount)
        {
            buffer = _boundIndices!;
            format = IndexFormat.UInt32;
            indexCount = _indexCount;
            return true;
        }

        private static void EnsureBuffer(ref DeviceBuffer? buffer, ref uint capacity, uint sizeInBytes, BufferUsage usage)
        {
            if (buffer != null && sizeInBytes <= capacity)
                return;

            // Deferred, not immediate: a command buffer still queued for replay (possibly on a
            // separate thread) may reference the old buffer. Freeing it now would let that memory
            // get reused by the very next render (e.g. a material/mesh preview drawn later this
            // frame), so the stale gizmo draw call would read someone else's GPU data instead.
            if (buffer != null)
                Graphics.DisposeDeferred(buffer);
            uint newCapacity = (uint)(sizeInBytes * 1.5f) + 256;
            buffer = Graphics.Device.ResourceFactory.CreateBuffer(new BufferDescription(newCapacity, usage) { TransientWrites = true });
            buffer.Name = $"Gizmo {usage}";
            capacity = newCapacity;
        }
    }
}
