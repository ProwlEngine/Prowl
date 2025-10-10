// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

using Prowl.Vector;
using Prowl.Runtime.Resources;

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


public record DebugStackFrame(string fileName, int? line = null, int? column = null, MethodBase? methodBase = null)
{
    public override string ToString()
    {
        string locSuffix = line != null ? column != null ? $"({line},{column})" : $"({line})" : "";

        if (methodBase != null)
            return $"In {methodBase.DeclaringType.Name}.{methodBase.Name} at {fileName}{locSuffix}";
        else
            return $"At {fileName}{locSuffix}";
    }

}


public record DebugStackTrace(params DebugStackFrame[] stackFrames)
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

        for (int i = 0; i < stackFrames.Length; i++)
            sb.AppendLine($"\t{stackFrames[i]}");

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
            StackTrace trace = new StackTrace(2, true);
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

    public static (Mesh? wire, Mesh? solid) GetGizmoDrawData(bool cameraRelative, Double3 cameraPosition)
    {
        return s_gizmoBuilder.UpdateMesh(cameraRelative, cameraPosition);
    }

    public static List<GizmoBuilder.IconDrawCall> GetGizmoIcons()
    {
        return s_gizmoBuilder.GetIcons();
    }

    public static void PushMatrix(Double4x4 matrix)
    {
        s_gizmoBuilder.PushMatrix(matrix);
    }

    public static void PopMatrix()
    {
        s_gizmoBuilder.PopMatrix();
    }

    public static void DrawLine(Double3 start, Double3 end, Color color) => s_gizmoBuilder.DrawLine(start, end, color);
    public static void DrawTriangle(Double3 a, Double3 b, Double3 c, Color color) => s_gizmoBuilder.DrawTriangle(a, b, c, color);
    public static void DrawWireCube(Double3 center, Double3 halfExtents, Color color) => s_gizmoBuilder.DrawWireCube(center, halfExtents, color);
    public static void DrawCube(Double3 center, Double3 halfExtents, Color color) => s_gizmoBuilder.DrawCube(center, halfExtents, color);
    public static void DrawWireCircle(Double3 center, Double3 normal, double radius, Color color, int segments = 16) => s_gizmoBuilder.DrawCircle(center, normal, radius, color, segments);
    public static void DrawWireSphere(Double3 center, double radius, Color color, int segments = 16) => s_gizmoBuilder.DrawWireSphere(center, radius, color, segments);
    public static void DrawSphere(Double3 center, double radius, Color color, int segments = 16) => s_gizmoBuilder.DrawSphere(center, radius, color, segments);
    public static void DrawWireCone(Double3 start, Double3 direction, double radius, Color color, int segments = 16) => s_gizmoBuilder.DrawWireCone(start, direction, radius, color, segments);
    public static void DrawArrow(Double3 start, Double3 direction, Color color) => s_gizmoBuilder.DrawArrow(start, direction, color);

    public static void DrawIcon(Texture2D icon, Double3 center, double scale, Color color) => s_gizmoBuilder.DrawIcon(icon, center, scale, color);

    #endregion

}

public class GizmoBuilder
{
    private struct MeshData
    {
        public List<Double3> s_vertices = [];
        public List<Double2> s_uvs = [];
        public List<Color32> s_colors = [];
        public List<int> s_indices = [];

        public MeshData()
        {
        }

        public readonly void Clear()
        {
            s_vertices.Clear();
            s_uvs.Clear();
            s_colors.Clear();
            s_indices.Clear();
        }
    }

    private MeshData _wireData = new();
    private MeshData _solidData = new();
    private Mesh? _wire;
    private Mesh? _solid;

    public struct IconDrawCall
    {
        public Texture2D texture;
        public Double3 center;
        public double scale;
        public Color color;
    }

    private List<IconDrawCall> _icons = [];

    private Stack<Double4x4> _matrix4X4s = new();


    public void Clear()
    {
        _wireData.Clear();
        _solidData.Clear();

        _wire?.Clear();
        _solid?.Clear();

        _icons.Clear();

        _matrix4X4s.Clear();
    }

    private void AddLine(Double3 a, Double3 b, Color color)
    {
        if (_matrix4X4s.Count > 0)
        {
            var m = _matrix4X4s.Peek();
            a = Maths.TransformPoint(a, m);
            b = Maths.TransformPoint(b, m);
        }

        int index = _wireData.s_vertices.Count;
        _wireData.s_vertices.Add(a);
        _wireData.s_vertices.Add(b);

        _wireData.s_colors.Add(color);
        _wireData.s_colors.Add(color);

        _wireData.s_indices.Add(index);
        _wireData.s_indices.Add(index + 1);
    }

    private void AddTriangle(Double3 a, Double3 b, Double3 c, Double2 a_uv, Double2 b_uv, Double2 c_uv, Color color)
    {
        if (_matrix4X4s.Count > 0)
        {
            var m = _matrix4X4s.Peek();
            a = Maths.TransformPoint(a, m);
            b = Maths.TransformPoint(b, m);
            c = Maths.TransformPoint(c, m);
        }

        int index = _solidData.s_vertices.Count;

        _solidData.s_vertices.Add(a);
        _solidData.s_vertices.Add(b);
        _solidData.s_vertices.Add(c);

        _solidData.s_uvs.Add(a_uv);
        _solidData.s_uvs.Add(b_uv);
        _solidData.s_uvs.Add(c_uv);

        _solidData.s_colors.Add(color);
        _solidData.s_colors.Add(color);
        _solidData.s_colors.Add(color);

        _solidData.s_indices.Add(index);
        _solidData.s_indices.Add(index + 1);
        _solidData.s_indices.Add(index + 2);
    }

    public void PushMatrix(Double4x4 matrix)
    {
        _matrix4X4s.Push(matrix);
    }

    public void PopMatrix()
    {
        _matrix4X4s.Pop();
    }

    public void DrawLine(Double3 start, Double3 end, Color color) => AddLine(start, end, color);

    public void DrawTriangle(Double3 a, Double3 b, Double3 c, Color color) => AddTriangle(a, b, c, Double2.Zero, Double2.Zero, Double2.Zero, color);

    public void DrawWireCube(Double3 center, Double3 halfExtents, Color color)
    {
        Double3[] vertices = [
            new Double3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
            new Double3(center.X + halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
            new Double3(center.X + halfExtents.X, center.Y - halfExtents.Y, center.Z + halfExtents.Z),
            new Double3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z + halfExtents.Z),
            new Double3(center.X - halfExtents.X, center.Y + halfExtents.Y, center.Z - halfExtents.Z),
            new Double3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z - halfExtents.Z),
            new Double3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z),
            new Double3(center.X - halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z),
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

    public void DrawCube(Double3 center, Double3 halfExtents, Color color)
    {
        Double3[] vertices = [
            new Double3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
            new Double3(center.X + halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
            new Double3(center.X + halfExtents.X, center.Y - halfExtents.Y, center.Z + halfExtents.Z),
            new Double3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z + halfExtents.Z),
            new Double3(center.X - halfExtents.X, center.Y + halfExtents.Y, center.Z - halfExtents.Z),
            new Double3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z - halfExtents.Z),
            new Double3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z),
            new Double3(center.X - halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z),
        ];

        Double2[] uvs = [
            new Double2(0, 0),
            new Double2(1, 0),
            new Double2(1, 1),
            new Double2(0, 1),
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

    public void DrawWireSphere(Double3 center, double radius, Color color, int segments = 16)
    {
        double step = MathF.PI * 2 / segments;

        for (int i = 0; i < segments; i++)
        {
            double angle1 = i * step;
            double angle2 = (i + 1) * step;

            Double3 a = new(Math.Cos(angle1) * radius + center.X,
                            Math.Sin(angle1) * radius + center.Y,
                            center.Z
                        );

            Double3 b = new(Math.Cos(angle2) * radius + center.X,
                            Math.Sin(angle2) * radius + center.Y,
                            center.Z
                        );

            AddLine(a, b, color);
        }

        for (int i = 0; i < segments; i++)
        {
            double angle1 = i * step;
            double angle2 = (i + 1) * step;

            Double3 a = new(Math.Cos(angle1) * radius + center.X,
                            center.Y,
                            Math.Sin(angle1) * radius + center.Z
                        );

            Double3 b = new(Math.Cos(angle2) * radius + center.X,
                            center.Y,
                            Math.Sin(angle2) * radius + center.Z
                        );

            AddLine(a, b, color);
        }

        for (int i = 0; i < segments; i++)
        {
            double angle1 = i * step;
            double angle2 = (i + 1) * step;

            Double3 a = new(center.X,
                            Math.Cos(angle1) * radius + center.Y,
                            Math.Sin(angle1) * radius + center.Z
                        );

            Double3 b = new(center.X,
                            Math.Cos(angle2) * radius + center.Y,
                            Math.Sin(angle2) * radius + center.Z
                        );

            AddLine(a, b, color);
        }
    }

    public void DrawCircle(Double3 center, Double3 normal, double radius, Color color, int segments)
    {
        Double3 u = Maths.Normalize(Maths.Cross(normal, Double3.UnitY));
        Double3 v = Maths.Normalize(Maths.Cross(u, normal));
        double step = MathF.PI * 2 / segments;
        for (int i = 0; i < segments; i++)
        {
            double angle1 = i * step;
            double angle2 = (i + 1) * step;
            Double3 a = center + radius * (Math.Cos(angle1) * u + Math.Sin(angle1) * v);
            Double3 b = center + radius * (Math.Cos(angle2) * u + Math.Sin(angle2) * v);
            AddLine(a, b, color);
        }
    }

    public void DrawSphere(Double3 center, double radius, Color color, int segments = 16)
    {
        int latitudeSegments = segments;
        int longitudeSegments = segments * 2;

        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            double theta1 = lat * MathF.PI / latitudeSegments;
            double theta2 = (lat + 1) * MathF.PI / latitudeSegments;

            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                double phi1 = lon * 2 * MathF.PI / longitudeSegments;
                double phi2 = (lon + 1) * 2 * MathF.PI / longitudeSegments;

                Double3 v1 = CalculatePointOnSphere(theta1, phi1, radius, center);
                Double3 v2 = CalculatePointOnSphere(theta1, phi2, radius, center);
                Double3 v3 = CalculatePointOnSphere(theta2, phi1, radius, center);
                Double3 v4 = CalculatePointOnSphere(theta2, phi2, radius, center);

                // First triangle
                AddTriangle(v1, v2, v3, Double2.Zero, Double2.Zero, Double2.Zero, color);

                // Second triangle
                AddTriangle(v2, v4, v3, Double2.Zero, Double2.Zero, Double2.Zero, color);
            }
        }
    }

    private Double3 CalculatePointOnSphere(double theta, double phi, double radius, Double3 center)
    {
        double x = Math.Sin(theta) * Math.Cos(phi);
        double y = Math.Cos(theta);
        double z = Math.Sin(theta) * Math.Sin(phi);

        return new Double3(
            x * radius + center.X,
            y * radius + center.Y,
            z * radius + center.Z
        );
    }

    public void DrawWireCone(Double3 start, Double3 direction, double radius, Color color, int segments = 16)
    {
        double step = MathF.PI * 2 / segments;
        Double3 tip = start + direction;

        // Normalize the direction vector
        Double3 dir = Maths.Normalize(direction);

        // Find perpendicular vectors
        Double3 u = GetPerpendicularVector(dir);
        Double3 v = Maths.Cross(dir, u);

        for (int i = 0; i < segments; i++)
        {
            double angle1 = i * step;
            double angle2 = (i + 1) * step;

            // Calculate circle points using the perpendicular vectors
            Double3 a = start + radius * (Math.Cos(angle1) * u + Math.Sin(angle1) * v);
            Double3 b = start + radius * (Math.Cos(angle2) * u + Math.Sin(angle2) * v);

            AddLine(a, b, color);
            if (i == 0 || i == segments / 4 || i == segments / 2 || i == segments * 3 / 4)
                AddLine(a, tip, color);
        }
    }

    private Double3 GetPerpendicularVector(Double3 v)
    {
        Double3 result = Double3.UnitX;
        if (Math.Abs(v.X) > 0.1f)
            result = new Double3(v.Y, -v.X, 0);
        else if (Math.Abs(v.Y) > 0.1f)
            result = new Double3(0, v.Z, -v.Y);
        else
            result = new Double3(-v.Z, 0, v.X);
        return Maths.Normalize(result);
    }

    public void DrawArrow(Double3 start, Double3 direction, Color color)
    {
        Double3 axis = Maths.Normalize(direction);
        Double3 end = start + direction;
        AddLine(start, end, color);

        DrawWireCone(start + (direction * 0.9f), axis * 0.1f, 0.1f, color, 4);

    }

    public void DrawIcon(Texture2D icon, Double3 center, double scale, Color color) => _icons.Add(new IconDrawCall { texture = icon, center = center, scale = scale, color = color });

    public (Mesh? wire, Mesh? solid) UpdateMesh(bool cameraRelative, Double3 cameraPosition)
    {
        bool hasWire = _wireData.s_vertices.Count > 0;
        if (hasWire)
        {
            _wire ??= new()
            {
                MeshTopology = GraphicsBackend.Primitives.Topology.Lines,
                IndexFormat = IndexFormat.UInt16,
            };

            _wire.Vertices = [.. _wireData.s_vertices.Select(v => (Float3)v)];
            _wire.Colors = [.. _wireData.s_colors];
            _wire.Indices = _wireData.s_indices.Select(i => (uint)i).ToArray();

            if (cameraRelative)
            {
                // Convert vertices to be relative to the camera
                Float3[] vertices = new Float3[_wireData.s_vertices.Count];
                for (int i = 0; i < _wireData.s_vertices.Count; i++)
                    vertices[i] = (Float3)(_wireData.s_vertices[i] - cameraPosition);
                _wire.Vertices = vertices;
            }
            else
            {
                _wire.Vertices = [.. _wireData.s_vertices.Select(v => (Float3)v)];
            }
        }

        bool hasSolid = _solidData.s_vertices.Count > 0;
        if (hasSolid)
        {
            _solid ??= new()
            {
                MeshTopology = GraphicsBackend.Primitives.Topology.Triangles, 
                IndexFormat = IndexFormat.UInt16,
            };

            if (cameraRelative)
            {
                // Convert vertices to be relative to the camera
                Float3[] vertices2 = new Float3[_solidData.s_vertices.Count];
                for (int i = 0; i < _solidData.s_vertices.Count; i++)
                    vertices2[i] = (Float3)(_solidData.s_vertices[i] - cameraPosition);
                _solid.Vertices = vertices2;
            }
            else
            {
                _solid.Vertices = [.. _solidData.s_vertices.Select(v => (Float3)v)];
            }

            _solid.Colors = [.. _solidData.s_colors];
            _solid.UV = [.. _solidData.s_uvs.Select(v => (Float2)v)];
            _solid.Indices = _solidData.s_indices.Select(i => (uint)i).ToArray();
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
