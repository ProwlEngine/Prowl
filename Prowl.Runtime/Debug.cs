// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

using Prowl.Runtime.Resources;
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


public record DebugStackFrame(string FileName, int? Line = null, int? Column = null, MethodBase? MethodBase = null)
{
    public override string ToString()
    {
        string locSuffix = Line != null ? Column != null ? $"({Line},{Column})" : $"({Line})" : "";

        if (MethodBase != null)
            return $"In {MethodBase.DeclaringType.Name}.{MethodBase.Name} at {FileName}{locSuffix}";
        else
            return $"At {FileName}{locSuffix}";
    }

}


public record DebugStackTrace(params DebugStackFrame[] StackFrames)
{
    public static explicit operator DebugStackTrace(StackTrace stackTrace)
    {
        DebugStackFrame[] stackFrames = new DebugStackFrame[stackTrace.FrameCount];

        for (int i = 0; i < stackFrames.Length; i++)
        {
            StackFrame srcFrame = stackTrace.GetFrame(i);
            stackFrames[i] = new DebugStackFrame(srcFrame.GetFileName(), srcFrame.GetFileLineNumber(), srcFrame.GetFileColumnNumber(), srcFrame.GetMethod());
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

    public static (Mesh? wire, Mesh? solid) GetGizmoDrawData()
    {
        return s_gizmoBuilder.UpdateMesh();
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
    public static void DrawWireSphere(Float3 center, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawWireSphere(center, radius, color, segments);
    public static void DrawSphere(Float3 center, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawSphere(center, radius, color, segments);
    public static void DrawWireCone(Float3 start, Float3 direction, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawWireCone(start, direction, radius, color, segments);
    public static void DrawWireCapsule(Float3 point1, Float3 point2, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawWireCapsule(point1, point2, radius, color, segments);
    public static void DrawWireCylinder(Float3 center, Quaternion rotation, float radius, float height, Color color, int segments = 16) => s_gizmoBuilder.DrawWireCylinder(center, rotation, radius, height, color, segments);
    public static void DrawArrow(Float3 start, Float3 direction, Color color) => s_gizmoBuilder.DrawArrow(start, direction, color);

    public static void DrawIcon(Texture2D icon, Float3 center, float scale, Color color) => s_gizmoBuilder.DrawIcon(icon, center, scale, color);

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
    private Mesh? _wire;
    private Mesh? _solid;

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
        Float3 u = Float3.Normalize(Float3.Cross(normal, Float3.UnitY));
        Float3 v = Float3.Normalize(Float3.Cross(u, normal));
        float step = MathF.PI * 2 / segments;
        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;
            Float3 a = center + radius * (Maths.Cos(angle1) * u + Maths.Sin(angle1) * v);
            Float3 b = center + radius * (Maths.Cos(angle2) * u + Maths.Sin(angle2) * v);
            AddLine(a, b, color);
        }
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
        Float3 axis = Float3.Normalize(direction);
        Float3 end = start + direction;
        AddLine(start, end, color);

        DrawWireCone(start + (direction * 0.9f), axis * 0.1f, 0.1f, color, 4);

    }

    public void DrawIcon(Texture2D icon, Float3 center, float scale, Color color) => _icons.Add(new IconDrawCall { Texture = icon, Center = center, Scale = scale, Color = color });

    public (Mesh? wire, Mesh? solid) UpdateMesh()
    {
        bool hasWire = _wireData.Vertices.Count > 0;
        if (hasWire)
        {
            _wire ??= new()
            {
                MeshTopology = Topology.Lines,
                IndexFormat = IndexFormat.UInt32,
            };

            _wire.Vertices = [.. _wireData.Vertices.Select(v => (Float3)v)];
            _wire.Colors = [.. _wireData.Colors];
            _wire.Indices = [.. _wireData.Indices.Select(i => (uint)i)];

            _wire.Vertices = [.. _wireData.Vertices.Select(v => (Float3)v)];
        }

        bool hasSolid = _solidData.Vertices.Count > 0;
        if (hasSolid)
        {
            _solid ??= new()
            {
                MeshTopology = Topology.Triangles,
                IndexFormat = IndexFormat.UInt32,
            };

            _solid.Vertices = [.. _solidData.Vertices.Select(v => (Float3)v)];

            _solid.Colors = [.. _solidData.Colors];
            _solid.UV = [.. _solidData.Uvs.Select(v => (Float2)v)];
            _solid.Indices = [.. _solidData.Indices.Select(i => (uint)i)];
        }

        return (
            hasWire ? _wire : null,
            hasSolid ? _solid : null
            );
    }

    public List<IconDrawCall> GetIcons()
    {
        return _icons;
    }
}
