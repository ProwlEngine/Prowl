// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Tasks;
using Prowl.Vector;
using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime;

public enum LogSeverity
{
    Success = 1 << 0,
    Normal = 1 << 1,
    Warning = 1 << 2,
    Error = 1 << 3,
    Exception = 1 << 4
}

/// <summary>
/// Represents a logged event with a message, severity, and optional stack trace.
/// </summary>
public readonly struct LogEvent
{
    /// <summary>
    /// The log message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The severity of the message (info, warning, etc).
    /// </summary>
    public required LogSeverity Severity { get; init; }

    /// <summary>
    /// The stack trace, or <see langword="null"/> if no stack trace is available.
    /// </summary>
    public DebugStackTrace? StackTrace { get; init; }
}

/// <summary>
/// Represents a stack trace, either for an exception or a point-in-time capture.
/// </summary>
public sealed class DebugStackTrace
{
    private readonly Exception? _exception;
    private readonly StackTrace _stackTrace;    // When initialized from an exception, this is the inner-most exception.

    private DebugStackTrace(Exception? exception, StackTrace? stackTrace)
    {
        _exception = exception;
        _stackTrace = stackTrace;
    }

    /// <summary>
    /// Creates a <see cref="DebugStackTrace"/> from a <see cref="Exception"/>, returning <see langword="null"/> if
    /// no info is available.
    /// </summary>
    internal static DebugStackTrace? FromException(Exception exception, out string message)
    {
        Exception baseException = exception.GetBaseException();
        var result = new DebugStackTrace(exception: exception, stackTrace: new StackTrace(baseException, fNeedFileInfo: true));
        message = result.Message;
        return result._stackTrace.FrameCount == 0 ? null : result;
    }

    /// <summary>
    /// Creates a <see cref="DebugStackTrace"/> from a <see cref="StackTrace"/>, returning <see langword="null"/> if
    /// no info is available.
    /// </summary>
    internal static DebugStackTrace? FromStackTrace(StackTrace stackTrace)
    {
        var result = new DebugStackTrace(exception: null, stackTrace: stackTrace);
        return result._stackTrace.FrameCount == 0 ? null : result;
    }

    // A cached textual version of the stack trace.
    private string Text => field ??= _exception?.ToString() ?? _stackTrace.ToString();

    // A cached set of lines, one for each stack frame. Leading whitespace is skipped.
    private Range[] Lines
    {
        get
        {
            if (field is not null)
                return field;

            var ranges = new List<Range>(capacity: _stackTrace.FrameCount + 2);
            ReadOnlySpan<char> span = Text.AsSpan();
            int currentPos = 0;
            while (currentPos < span.Length)
            {
                // Skip past whitespace.
                while (currentPos < span.Length && char.IsWhiteSpace(span[currentPos]))
                    currentPos++;
                if (currentPos == span.Length)
                    break;

                // Slice the remaining text to search within
                ReadOnlySpan<char> remaining = span.Slice(currentPos);
                int indexInRemaining = remaining.IndexOfAny(['\r', '\n']);

                // If no more line endings are found, the rest of the string is the last line.
                if (indexInRemaining == -1)
                {
                    ranges.Add(new Range(currentPos, span.Length));
                    break;
                }

                // Calculate the absolute position of the delimiter in the original string
                int delimiterPos = currentPos + indexInRemaining;

                // Add the range of the current line (excluding the delimiter)
                ranges.Add(new Range(currentPos, delimiterPos));

                // Start searching for whitespace just past the delimiter.
                currentPos = delimiterPos + 1;
            }

            field = [.. ranges];
            return field;
        }
    }

    /// <summary>
    /// Returns the number of lines of <see cref="Text"/> that comprise the exception message. Returns <c>0</c> if no
    /// exception is available.
    /// </summary>
    private int MessageLineCount
    {
        get
        {
            if (_exception is null)
                return 0;
            if (field != 0)
                return field;
            int result = 1;
            for (int i = 1; i < Lines.Length; i++)
            {
                if (Text[Lines[i]].StartsWith("---> ", StringComparison.Ordinal))
                    result++;
                else
                    break;
            }
            field = result;
            return field;
        }
    }

    /// <summary>
    /// The exception message, if this <see cref="DebugStackTrace "/> was initialized from an exception.
    /// </summary>
    private string Message
    {
        get
        {
            if (_exception is null)
                return string.Empty;
            return Text[new Range(Lines[0].Start, Lines[MessageLineCount - 1].End)];
        }
    }

    /// <summary>
    /// Gets the number of lines in the stack trace. This may not equal the number of frames when inner exceptions are
    /// present.
    /// </summary>
    public int LineCount => Lines.Length - MessageLineCount;

    /// <summary>
    /// Gets the line of text to display.
    /// </summary>
    /// <param name="index"> The index of the line, from zero to <see cref="LineCount"/> - 1. </param>
    /// <returns> The line of text. </returns>
    public string GetLine(int index) => Text[Lines[index + MessageLineCount]];

    /// <summary>
    /// The class the stack trace originated from (the first captured frame's declaring type).
    /// </summary>
    public string? TypeName
    {
        get
        {
            string? typeName = DiagnosticMethodInfo.Create(_stackTrace.GetFrame(0))?.DeclaringTypeName;
            if (typeName is null) return null;
            return typeName[(typeName.LastIndexOf('.') + 1)..];
        }
    }

    /// <summary>
    /// Gets the file path, line number, and column number, or <c>null</c> if no matching source file is found.
    /// </summary>
    /// <param name="assetsPath">The path prefix used to locate an asset source file.</param>
    public (string Path, int Line, int Column)? GetAssetSourceLocation(string assetsPath)
    {
        for (int i = 0; i < _stackTrace.FrameCount; i ++)
        {
            StackFrame frame = _stackTrace.GetFrame(i);
            if (string.IsNullOrEmpty(frame.GetFileName()) || !System.IO.Path.IsPathRooted(frame.GetFileName())) continue;
            string full = System.IO.Path.GetFullPath(frame.GetFileName());
            if (full.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(full))
                return (full, frame.GetFileLineNumber(), frame.GetFileColumnNumber());
        }
        return null;
    }

    /// <summary>A textual representation of this object.</summary>
    public override string ToString() => Text;
}


public static class Debug
{
    /// <summary>
    /// Triggered when a message is logged.
    /// </summary>
    public static event Action<LogEvent>? OnLog;

    /// <summary>
    /// Logs a message at normal severity.
    /// </summary>
    /// <param name="message"> The message to log. Can be <see langword="null"/>. </param>
    public static void Log(object? message)
        => LogCore(message?.ToString() ?? "null", LogSeverity.Normal);

    /// <summary>
    /// Logs a message at normal severity.
    /// </summary>
    /// <param name="message"> The message to log. </param>
    public static void Log(string message)
        => LogCore(message, LogSeverity.Normal);

    /// <summary>
    /// Logs a message with a custom severity.
    /// </summary>
    /// <param name="message"> The message to log. </param>
    /// <param name="logSeverity"> The severity of the log entry. </param>
    public static void Log(string message, LogSeverity logSeverity)
        => LogCore(message, logSeverity);

    /// <summary>
    /// Logs a warning.
    /// </summary>
    /// <param name="message"> The warning message to log. Can be <see langword="null"/>. </param>
    public static void LogWarning(object? message)
        => LogCore(message?.ToString() ?? "null", LogSeverity.Warning);

    /// <summary>
    /// Logs an warning.
    /// </summary>
    /// <param name="message"> The warning message to log. </param>
    public static void LogWarning(string message)
        => LogCore(message, LogSeverity.Warning);

    /// <summary>
    /// Logs an error.
    /// </summary>
    /// <param name="message"> The error message to log. Can be <see langword="null"/>. </param>
    public static void LogError(object? message)
        => LogCore(message?.ToString() ?? "null", LogSeverity.Error);

    /// <summary>
    /// Logs an error.
    /// </summary>
    /// <param name="message"> The error message to log. </param>
    public static void LogError(string message)
        => LogCore(message, LogSeverity.Error);

    /// <summary>
    /// Logs a success message.
    /// </summary>
    /// <param name="message"> The message to log. Can be <see langword="null"/>. </param>
    public static void LogSuccess(object? message)
        => LogCore(message?.ToString() ?? "null", LogSeverity.Success);

    /// <summary>
    /// Logs a success message.
    /// </summary>
    /// <param name="message"> The message to log. </param>
    public static void LogSuccess(string message)
        => LogCore(message, LogSeverity.Success);

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
        if (ShouldReport(id)) LogCore(Tag(id, message), LogSeverity.Normal);
    }

    /// <inheritdoc cref="LogOnce"/>
    public static void LogWarningOnce(string id, string message)
    {
        if (ShouldReport(id)) LogCore(Tag(id, message), LogSeverity.Warning);
    }

    /// <inheritdoc cref="LogOnce"/>
    public static void LogErrorOnce(string id, string message)
    {
        if (ShouldReport(id)) LogCore(Tag(id, message), LogSeverity.Error);
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

    /// <summary>
    /// Indicates whether the logging methods should output to the console. Defaults to <see langword="true"/>.
    /// </summary>
    public static bool LogToConsole { get; set; } = true;

    /// <summary>
    /// Logs an exception with context.
    /// </summary>
    /// <param name="context"> A short line of context that preceeds the exception message in logs. </param>
    /// <param name="exception"> The exception to log. </param>
    /// <remarks>
    /// Exceptions are logged with severity <see cref="LogSeverity.Exception"/>.
    /// </remarks>
    public static void LogException(string context, Exception exception)
    {
        // Capture the exception's stack trace.
        DebugStackTrace? trace = DebugStackTrace.FromException(exception, out string message);

        // Log it.
        LogCore(context + " " + message, LogSeverity.Exception, trace);
    }

    /// <summary>
    /// Logs an exception without context.
    /// </summary>
    /// <param name="exception"> The exception to log. </param>
    /// <remarks>
    /// Exceptions are logged with severity <see cref="LogSeverity.Exception"/>.
    /// </remarks>
    public static void LogException(Exception exception)
    {
        LogException(string.Empty, exception);
    }

    // Color and text are two console calls, so logs from different threads would otherwise swap colors.
    private static readonly Lock s_consoleLock = new();

    /// <summary>
    /// Writes a message with the specified severity and triggers the <see cref="OnLog"/> event.
    /// </summary>
    /// <param name="message"> The message to log. </param>
    /// <param name="logSeverity"> The severity of the message. </param>
    /// <param name="customTrace"> An optional stack trace to use and display with the message. If omitted, a stack
    /// trace is captured automatically.</param>
    /// <remarks>
    /// The message is written to the console when console logging is enabled. The log event receives the message,
    /// stack trace, and severity.
    /// </remarks>
    private static void LogCore(string message, LogSeverity logSeverity, DebugStackTrace? customTrace = null)
    {
        // NOTE : StackTrace is pretty fast on modern .NET, so it's nice to keep it on by default, since it gives useful line numbers for debugging purposes.
        // For reference, getting a stack trace on a modern machine takes around 15 μs at a depth of 15.
        // NOTE 2 : Assumes that the first two frames are this method and the public logging method, so the caller is at frame 2.
        DebugStackTrace? trace = customTrace ?? DebugStackTrace.FromStackTrace(new StackTrace(skipFrames: 2, fNeedFileInfo: true));

        if (LogToConsole)
        {
            lock (s_consoleLock)
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
                if (customTrace != null) Console.WriteLine(customTrace.ToString());
                Console.ForegroundColor = prevColor;
            }
        }

        OnLog?.Invoke(new LogEvent
        {
            Message = message,
            Severity = logSeverity,
            StackTrace = trace,
        });
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

    /// <summary>Logs an error when <paramref name="condition"/> is false, in every build, and carries on.</summary>
    public static void Assert(bool condition, string? message)
    {
        if (!condition) LogCore(message == null ? "Assertion failed" : $"Assertion failed: {message}", LogSeverity.Error);
    }

    /// <inheritdoc cref="Assert(bool, string?)"/>
    public static void Assert(bool condition)
    {
        if (!condition) LogCore("Assertion failed", LogSeverity.Error);
    }

    #region Gizmos

    // Draw calls land in the scene whose code is running, so a preview never draws the game's lines.
    private static GizmoBuilder s_gizmoBuilder => Resources.Scene.Active.Gizmos;

    // Built games draw no gizmos, so their draw calls return before touching a scene
    private static GizmoBatch? Gizmos => Application.IsEditor ? s_gizmoBuilder.Batch : null;

    private static Prowl.Graphite.Rendering.Gizmos? s_gizmoFeature;

    /// <summary>The render feature every view showing gizmos draws them with, made on first use.</summary>
    internal static Prowl.Graphite.Rendering.Gizmos GizmoFeature => s_gizmoFeature ??= new Prowl.Graphite.Rendering.Gizmos(Graphics.Renderer!);

    internal static void DisposeGizmoFeature()
    {
        s_gizmoFeature?.Dispose();
        s_gizmoFeature = null;
    }

    /// <summary>Clears every scene's gizmos for the next frame, handing the frame's to the renderer.</summary>
    public static void ClearGizmos()
    {
        foreach (Resources.Scene scene in Resources.Scene.Live)
            scene.Gizmos.Clear();
    }

    /// <summary>Shows a scene's gizmos for this frame in the views of a world that draw gizmos.</summary>
    internal static void SubmitGizmos(Resources.Scene? scene, RenderWorld world)
    {
        if (scene.IsNotValid() || !Application.IsEditor) return;
        scene.Gizmos.Submit(world, GizmoFeature);
    }

    public static void PushMatrix(Float4x4 matrix)
    {
        if (Application.IsEditor) s_gizmoBuilder.PushMatrix(matrix);
    }

    public static void PopMatrix()
    {
        if (Application.IsEditor) s_gizmoBuilder.PopMatrix();
    }

    public static void DrawLine(Float3 start, Float3 end, Color color) => Gizmos?.Line(start, end, color);
    public static void DrawTriangle(Float3 a, Float3 b, Float3 c, Color color) => Gizmos?.Triangle(a, b, c, color);
    public static void DrawWireCube(Float3 center, Float3 halfExtents, Color color) => Gizmos?.Box(center, halfExtents, Quaternion.Identity, color);
    public static void DrawCube(Float3 center, Float3 halfExtents, Color color) => Gizmos?.SolidBox(center, halfExtents, Quaternion.Identity, color);
    public static void DrawWireCircle(Float3 center, Float3 normal, float radius, Color color, int segments = 32) => Gizmos?.Circle(center, normal, radius, color, segments: segments);

    /// <summary>
    /// Part of a circle about <paramref name="normal"/>, starting at the <paramref name="from"/> direction and sweeping
    /// <paramref name="sweepRadians"/>, counterclockwise looking down the normal. A negative sweep goes the other way.
    /// </summary>
    public static void DrawWireArc(Float3 center, Float3 normal, Float3 from, float radius, float sweepRadians, Color color, int segments = 24)
    {
        float turn = MathF.Abs(sweepRadians) / MathF.Tau;
        Gizmos?.Arc(center, normal, from, sweepRadians, radius, color, segments: turn > 1e-4f ? (int)MathF.Ceiling(Maths.Max(1, segments) / turn) : segments);
    }

    public static void DrawWireSphere(Float3 center, float radius, Color color, int segments = 32) => Gizmos?.Sphere(center, radius, color, segments: segments);
    public static void DrawSphere(Float3 center, float radius, Color color, int segments = 16) => Gizmos?.SolidSphere(center, radius, color, segments);

    /// <summary>A cone with its base around <paramref name="start"/> and its tip at <paramref name="start"/> plus <paramref name="direction"/>.</summary>
    public static void DrawWireCone(Float3 start, Float3 direction, float radius, Color color, int segments = 32)
    {
        float length = Float3.Length(direction);
        if (length <= 0.0f) return;
        Gizmos?.Cone(start + direction, -direction, length, MathF.Atan2(radius, length), color, segments: segments);
    }

    public static void DrawWireCapsule(Float3 point1, Float3 point2, float radius, Color color, int segments = 32) => Gizmos?.Capsule(point1, point2, radius, color, segments: segments);

    /// <summary>A cylinder along the rotation's up axis, <paramref name="height"/> long and centred on <paramref name="center"/>.</summary>
    public static void DrawWireCylinder(Float3 center, Quaternion rotation, float radius, float height, Color color, int segments = 32)
    {
        Float3 half = rotation * Float3.UnitY * (height * 0.5f);
        Gizmos?.Cylinder(center - half, center + half, radius, color, segments: segments);
    }

    /// <summary>A line from <paramref name="start"/> along <paramref name="direction"/>, with a head that keeps its size on screen.</summary>
    public static void DrawArrow(Float3 start, Float3 direction, Color color)
    {
        if (Float3.LengthSquared(direction) <= 0.0f) return;
        Gizmos?.Arrow(start, start + direction, color);
    }

    /// <summary>A camera facing icon <paramref name="scale"/> world units across. It can be clicked to select the component drawing it.</summary>
    public static void DrawIcon(Texture2D icon, Float3 center, float scale, Color color)
    {
        if (Application.IsEditor) s_gizmoBuilder.DrawIcon(icon, center, scale, color);
    }

    /// <summary>
    /// Lines drawn until the scope is disposed can be clicked in the scene view to select the component drawing
    /// them. Meant for outlines that always show, such as a volume's bounds, so the volume selects from its edges
    /// without taking clicks meant for what is inside it.
    /// </summary>
    public static GizmoBuilder.HitboxLineScope GizmoHitboxLines() => Application.IsEditor ? s_gizmoBuilder.HitboxLines() : default;

    /// <summary>A sphere that can be clicked in the scene view to select the component drawing it.</summary>
    public static void GizmoHitboxSphere(Float3 center, float radius)
    {
        if (Application.IsEditor) s_gizmoBuilder.HitboxSphere(center, radius);
    }

    /// <summary>
    /// A line broken into dashes, so it reads as a relationship between two things rather than as an
    /// edge of something.
    /// </summary>
    public static void DrawDashedLine(Float3 from, Float3 to, Color color, int dashes = 8)
    {
        if (Gizmos is not { } gizmos) return;
        if (dashes < 1) dashes = 1;
        Float3 step = (to - from) / (dashes * 2 - 1);

        for (int i = 0; i < dashes; i++)
            gizmos.Line(from + step * (i * 2), from + step * (i * 2 + 1), color);
    }

    /// <summary>
    /// An axis through a point, drawn both ways because an axis has no near end, with an arrow head to give
    /// it a positive direction.
    /// </summary>
    public static void DrawAxisLine(Float3 center, Float3 direction, float length, Color color) => Gizmos?.AxisLine(center, direction, length, color);

    /// <summary>A bar across an axis, for marking a hard stop along it.</summary>
    public static void DrawCrossBar(Float3 position, Float3 axis, float size, Color color) => Gizmos?.CrossBar(position, axis, size, color);

    /// <summary>
    /// An angular range about <paramref name="normal"/>, measured from the <paramref name="zero"/>
    /// direction: the swept band, plus a spoke at each end unless it goes all the way round.
    /// </summary>
    public static void DrawArcRange(Float3 center, Float3 normal, Float3 zero, float radius,
        float minDegrees, float maxDegrees, Color rangeColor, Color limitColor)
        => Gizmos?.ArcRange(center, normal, zero, radius, minDegrees * Maths.Deg2Rad, maxDegrees * Maths.Deg2Rad, rangeColor, limitColor);

    /// <summary>
    /// A range along an axis. Ends that are infinite run on for <paramref name="openLength"/> with no
    /// stop drawn, which is what makes an unbounded range look unbounded.
    /// </summary>
    public static void DrawLinearRange(Float3 origin, Float3 direction, float minDistance, float maxDistance,
        float openLength, float stopSize, Color rangeColor, Color limitColor)
        => Gizmos?.LinearRange(origin, direction, minDistance, maxDistance, openLength, stopSize, rangeColor, limitColor);

    /// <summary>
    /// An arc with an arrowhead on the swept end, for showing a rotation direction. A negative
    /// <paramref name="sweepRadians"/> turns the other way.
    /// </summary>
    public static void DrawSpinArrow(Float3 center, Float3 normal, Float3 from, float radius, float sweepRadians, Color color)
        => Gizmos?.SpinArrow(center, normal, from, radius, sweepRadians, color);

    /// <summary>Three rings around a point, the shorthand for "free to rotate any way".</summary>
    public static void DrawGimbal(Float3 center, float radius, Color color, int segments = 32) => Gizmos?.Sphere(center, radius, color, segments: segments);

    /// <summary>An orientation tripod, X/Y/Z in red/green/blue.</summary>
    public static void DrawAxes(Float3 center, Quaternion rotation, float scale) => Gizmos?.Axes(center, rotation, scale);

    /// <inheritdoc cref="DrawAxes(Float3, Quaternion, float)"/>
    public static void DrawAxes(Float3 center, Quaternion rotation, float scale, Color color)
    {
        if (Gizmos is not { } gizmos) return;
        gizmos.Line(center, center + rotation * Float3.UnitX * scale, color);
        gizmos.Line(center, center + rotation * Float3.UnitY * scale, color);
        gizmos.Line(center, center + rotation * Float3.UnitZ * scale, color);
    }

    /// <summary>A square patch of plane with a cross through it, so it reads as a surface.</summary>
    public static void DrawWirePlane(Float3 center, Float3 normal, float extent, Color color)
    {
        if (Gizmos is not { } gizmos) return;
        PerpendicularAxes(normal, out Float3 u, out Float3 v);

        Float3 a = center + (u + v) * extent;
        Float3 b = center + (u - v) * extent;
        Float3 c = center - (u + v) * extent;
        Float3 d = center - (u - v) * extent;

        gizmos.Line(a, b, color);
        gizmos.Line(b, c, color);
        gizmos.Line(c, d, color);
        gizmos.Line(d, a, color);
        gizmos.Line((a + b) * 0.5f, (c + d) * 0.5f, color);
        gizmos.Line((b + c) * 0.5f, (d + a) * 0.5f, color);
    }

    /// <summary>
    /// A cone described by the half-angle it opens to from its axis, for swing limits, spotlights and
    /// vision cones. <paramref name="length"/> is the slant length, so every drawn edge is that far from
    /// the apex.
    /// </summary>
    public static void DrawWireConeAngle(Float3 apex, Float3 axis, float angleDegrees, float length, Color color, int segments = 32)
        => Gizmos?.SlantCone(apex, axis, angleDegrees * Maths.Deg2Rad, length, color, segments: segments);

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

/// <summary>
/// A scene's gizmos, drawn through Graphite's gizmo feature. Draws fill one batch while the renderer shows the other,
/// which <see cref="Clear"/> swaps at the end of a frame, so the frame's gizmos reach the renderer after they are cleared.
/// </summary>
public class GizmoBuilder
{
    private GizmoBatch _building = new() { Visible = false };
    private GizmoBatch _shown = new() { Visible = false };
    private RenderWorld? _world;
    private Prowl.Graphite.Rendering.Gizmos? _feature;
    private readonly Stack<Float4x4> _matrices = new();

    /// <summary>The batch draws this frame go into.</summary>
    public GizmoBatch Batch => _building;

    /// <summary>Whether nothing was drawn into this builder since it was last cleared.</summary>
    internal bool IsEmpty => _building.PrimitiveCount == 0;

    // ---- hitboxes ------------------------------------------------------------------------------------

    /// <summary>
    /// Whether gizmo draws record what they can be clicked by. Only the editor's scene view picks gizmos, so it
    /// turns this on around its own gizmo pass and a game drawing gizmos pays nothing for it.
    /// </summary>
    internal static bool RecordHitboxes { get; set; }

    /// <summary>The component whose gizmos are being drawn, which every hitbox recorded meanwhile selects.</summary>
    internal static Component? CurrentOwner { get; private set; }

    private List<GizmoHitbox> _hitboxes = [];
    private List<GizmoHitbox> _publishedHitboxes = [];
    private int _lineHitboxDepth;
    private int _lineHitboxStart;

    /// <summary>
    /// The hitboxes of the last full frame of gizmos. Picking runs before the frame's gizmos are drawn, so it reads
    /// these, which <see cref="Clear"/> publishes from the frame that just finished.
    /// </summary>
    internal IReadOnlyList<GizmoHitbox> Hitboxes => _publishedHitboxes;

    /// <summary>Makes <paramref name="owner"/> the component the hitboxes recorded until disposal belong to.</summary>
    internal static OwnerScope Owner(Component? owner)
    {
        Component? previous = CurrentOwner;
        CurrentOwner = owner;
        return new OwnerScope(previous);
    }

    internal readonly struct OwnerScope(Component? previous) : IDisposable
    {
        public void Dispose() => CurrentOwner = previous;
    }

    /// <summary>Lines drawn until disposal can be clicked to select the component drawing them.</summary>
    public HitboxLineScope HitboxLines()
    {
        if (_lineHitboxDepth++ == 0)
            _lineHitboxStart = _building.PrimitiveCount;
        return new HitboxLineScope(this);
    }

    public readonly struct HitboxLineScope : IDisposable
    {
        private readonly GizmoBuilder? _builder;
        internal HitboxLineScope(GizmoBuilder builder) => _builder = builder;
        public void Dispose() => _builder?.EndHitboxLines();
    }

    // Every line drawn in the outermost scope becomes a segment hitbox when it ends
    private void EndHitboxLines()
    {
        if (_lineHitboxDepth == 0 || --_lineHitboxDepth > 0) return;
        if (!RecordHitboxes) return;
        for (int i = _lineHitboxStart; i < _building.PrimitiveCount; i++)
        {
            if (_building.TryGetLine(i, out Float3 a, out Float3 b))
                AddHitbox(GizmoHitboxKind.Segment, a, b, 0f);
        }
    }

    /// <summary>A sphere that can be clicked to select the component drawing it.</summary>
    public void HitboxSphere(Float3 center, float radius)
    {
        if (_matrices.Count > 0)
        {
            // The radius scales with the matrix's largest axis, so the hitbox covers the sphere drawn with it.
            Float4x4 m = _matrices.Peek();
            Float3 c = Float4x4.TransformPoint(center, m);
            radius = MathF.Max(MathF.Max(
                Float3.Distance(c, Float4x4.TransformPoint(center + new Float3(radius, 0, 0), m)),
                Float3.Distance(c, Float4x4.TransformPoint(center + new Float3(0, radius, 0), m))),
                Float3.Distance(c, Float4x4.TransformPoint(center + new Float3(0, 0, radius), m)));
            center = c;
        }
        AddHitbox(GizmoHitboxKind.Sphere, center, center, radius);
    }

    private void AddHitbox(GizmoHitboxKind kind, Float3 a, Float3 b, float radius)
    {
        if (!RecordHitboxes || CurrentOwner is not { } owner || owner.GameObject is not { } go) return;
        _hitboxes.Add(new GizmoHitbox(go, kind, a, b, radius));
    }

    /// <summary>Transforms what is drawn until <see cref="PopMatrix"/> by a matrix, after the matrices pushed before it.</summary>
    public void PushMatrix(Float4x4 matrix)
    {
        _matrices.Push(_matrices.Count > 0 ? _matrices.Peek() * matrix : matrix);
        _building.Push(matrix);
    }

    public void PopMatrix()
    {
        if (_matrices.Count == 0) return;
        _matrices.Pop();
        _building.Pop();
    }

    /// <summary>A camera facing icon <paramref name="scale"/> world units across. It can be clicked to select the component drawing it.</summary>
    public void DrawIcon(Texture2D icon, Float3 center, float scale, Color color)
    {
        if (icon.IsNotValid() || icon.GpuTexture is not { } texture) return;
        if (_matrices.Count > 0)
            center = Float4x4.TransformPoint(center, _matrices.Peek());
        _building.WorldIcon(center, texture, scale, color);
        AddHitbox(GizmoHitboxKind.Icon, center, center, scale * 0.5f);
    }

    /// <summary>Shows this frame's gizmos in the views of a world that draw gizmos with the feature.</summary>
    internal void Submit(RenderWorld world, Prowl.Graphite.Rendering.Gizmos feature)
    {
        if (_feature != feature)
        {
            _feature?.Remove(_building);
            _feature?.Remove(_shown);
            _feature = feature;
            feature.Add(_building);
            feature.Add(_shown);
        }
        _world = world;
    }

    /// <summary>Hands the frame's gizmos to the renderer and starts the next frame's empty.</summary>
    public void Clear()
    {
        (_shown, _building) = (_building, _shown);
        _shown.World = _world;
        _shown.Visible = _world != null;
        _world = null;
        _building.Clear();
        _building.Visible = false;
        _matrices.Clear();

        (_publishedHitboxes, _hitboxes) = (_hitboxes, _publishedHitboxes);
        _hitboxes.Clear();
        _lineHitboxDepth = 0;
    }

    /// <summary>Stops drawing this scene's gizmos, when the scene is gone.</summary>
    internal void Release()
    {
        _feature?.Remove(_building);
        _feature?.Remove(_shown);
        _feature = null;
    }
}

/// <summary>The shape a <see cref="GizmoHitbox"/> is clicked by.</summary>
public enum GizmoHitboxKind
{
    /// <summary>A camera facing icon, <see cref="GizmoHitbox.Radius"/> being half its width. Drawn over the scene, so it wins over geometry.</summary>
    Icon,
    /// <summary>A sphere around <see cref="GizmoHitbox.A"/>.</summary>
    Sphere,
    /// <summary>A line from <see cref="GizmoHitbox.A"/> to <see cref="GizmoHitbox.B"/>, clicked within a few pixels.</summary>
    Segment,
}

/// <summary>A world space shape a gizmo can be clicked by, which selects <see cref="Owner"/>.</summary>
public readonly record struct GizmoHitbox(GameObject Owner, GizmoHitboxKind Kind, Float3 A, Float3 B, float Radius);
