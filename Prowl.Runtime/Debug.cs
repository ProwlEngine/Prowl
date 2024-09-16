// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Veldrid;

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


public record DebugStackFrame(int line, int column, string? fileName = null, MethodBase? methodBase = null)
{
    public override string ToString()
    {
        if (methodBase != null)
            return $"In {methodBase.DeclaringType.Name}.{methodBase.Name} at {fileName}:{line}:{column}";
        else
            return $"At {fileName}:{line}:{column}";
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
            stackFrames[i] = new DebugStackFrame(srcFrame.GetFileLineNumber(), srcFrame.GetFileColumnNumber(), srcFrame.GetFileName(), srcFrame.GetMethod());
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
        => Log(message.ToString(), ConsoleColor.White, LogSeverity.Normal);

    public static void Log(string message)
        => Log(message, ConsoleColor.White, LogSeverity.Normal);

    public static void LogWarning(object message)
        => Log(message.ToString(), ConsoleColor.Yellow, LogSeverity.Warning);

    public static void LogWarning(string message)
        => Log(message, ConsoleColor.Yellow, LogSeverity.Warning);

    public static void LogError(object message)
        => Log(message.ToString(), ConsoleColor.Red, LogSeverity.Error);

    public static void LogError(string message)
        => Log(message, ConsoleColor.Red, LogSeverity.Error);

    public static void LogSuccess(object message)
        => Log(message.ToString(), ConsoleColor.Green, LogSeverity.Success);

    public static void LogSuccess(string message)
        => Log(message, ConsoleColor.Green, LogSeverity.Success);

    public static void LogException(Exception exception)
    {
        ConsoleColor prevColor = Console.ForegroundColor;

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(exception.Message);

        if (exception.InnerException != null)
            Console.WriteLine(exception.InnerException.Message);

        DebugStackTrace trace = (DebugStackTrace)new StackTrace(exception.InnerException ?? exception, true);

        Console.WriteLine(trace.ToString());

        Console.ForegroundColor = prevColor;

        OnLog?.Invoke(exception.Message + "\n" + exception.InnerException.Message, trace, LogSeverity.Exception);
    }

    // NOTE : StackTrace is pretty fast on modern .NET, so it's nice to keep it on by default, since it gives useful line numbers for debugging purposes.
    // For reference, getting a stack trace on a modern machine takes around 15 μs at a depth of 15.
    public static void Log(string message, ConsoleColor color, LogSeverity logSeverity, DebugStackTrace? customTrace = null)
    {
        ConsoleColor prevColor = Console.ForegroundColor;

        Console.ForegroundColor = color;
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

    public static (Mesh? wire, Mesh? solid) GetGizmoDrawData(bool cameraRelative, Vector3 cameraPosition)
    {
        return s_gizmoBuilder.UpdateMesh(cameraRelative, cameraPosition);
    }

    public static List<GizmoBuilder.IconDrawCall> GetGizmoIcons()
    {
        return s_gizmoBuilder.GetIcons();
    }

    public static void DrawLine(Vector3 start, Vector3 end, Color color) => s_gizmoBuilder.DrawLine(start, end, color);
    public static void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color color) => s_gizmoBuilder.DrawTriangle(a, b, c, color);
    public static void DrawWireCube(Vector3 center, Vector3 halfExtents, Color color) => s_gizmoBuilder.DrawWireCube(center, halfExtents, color);
    public static void DrawCube(Vector3 center, Vector3 halfExtents, Color color) => s_gizmoBuilder.DrawCube(center, halfExtents, color);
    public static void DrawWireSphere(Vector3 center, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawWireSphere(center, radius, color, segments);
    public static void DrawSphere(Vector3 center, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawSphere(center, radius, color, segments);
    public static void DrawWireCone(Vector3 start, Vector3 direction, float radius, Color color, int segments = 16) => s_gizmoBuilder.DrawWireCone(start, direction, radius, color, segments);
    public static void DrawArrow(Vector3 start, Vector3 direction, Color color) => s_gizmoBuilder.DrawArrow(start, direction, color);

    public static void DrawIcon(Texture2D icon, Vector3 center, float scale, Color color) => s_gizmoBuilder.DrawIcon(icon, center, scale, color);

    #endregion

}

public class GizmoBuilder
{
    private struct MeshData
    {
        public List<Vector3> s_vertices = [];
        public List<System.Numerics.Vector2> s_uvs = [];
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
        public Vector3 center;
        public float scale;
        public Color color;
    }

    private List<IconDrawCall> _icons = [];


    public void Clear()
    {
        _wireData.Clear();
        _solidData.Clear();

        _wire?.Clear();
        _solid?.Clear();

        _icons.Clear();
    }

    private void AddLine(Vector3 a, Vector3 b, Color color)
    {
        int index = _wireData.s_vertices.Count;
        _wireData.s_vertices.Add(a);
        _wireData.s_vertices.Add(b);

        _wireData.s_colors.Add(color);
        _wireData.s_colors.Add(color);

        _wireData.s_indices.Add(index);
        _wireData.s_indices.Add(index + 1);
    }

    private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector2 a_uv, Vector2 b_uv, Vector2 c_uv, Color color)
    {
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

    public void DrawLine(Vector3 start, Vector3 end, Color color) => AddLine(start, end, color);

    public void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color color) => AddTriangle(a, b, c, Vector2.zero, Vector2.zero, Vector2.zero, color);

    public void DrawWireCube(Vector3 center, Vector3 halfExtents, Color color)
    {
        Vector3[] vertices = [
            new Vector3(center.x - halfExtents.x, center.y - halfExtents.y, center.z - halfExtents.z),
            new Vector3(center.x + halfExtents.x, center.y - halfExtents.y, center.z - halfExtents.z),
            new Vector3(center.x + halfExtents.x, center.y - halfExtents.y, center.z + halfExtents.z),
            new Vector3(center.x - halfExtents.x, center.y - halfExtents.y, center.z + halfExtents.z),
            new Vector3(center.x - halfExtents.x, center.y + halfExtents.y, center.z - halfExtents.z),
            new Vector3(center.x + halfExtents.x, center.y + halfExtents.y, center.z - halfExtents.z),
            new Vector3(center.x + halfExtents.x, center.y + halfExtents.y, center.z + halfExtents.z),
            new Vector3(center.x - halfExtents.x, center.y + halfExtents.y, center.z + halfExtents.z),
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

    public void DrawCube(Vector3 center, Vector3 halfExtents, Color color)
    {
        Vector3[] vertices = [
            new Vector3(center.x - halfExtents.x, center.y - halfExtents.y, center.z - halfExtents.z),
            new Vector3(center.x + halfExtents.x, center.y - halfExtents.y, center.z - halfExtents.z),
            new Vector3(center.x + halfExtents.x, center.y - halfExtents.y, center.z + halfExtents.z),
            new Vector3(center.x - halfExtents.x, center.y - halfExtents.y, center.z + halfExtents.z),
            new Vector3(center.x - halfExtents.x, center.y + halfExtents.y, center.z - halfExtents.z),
            new Vector3(center.x + halfExtents.x, center.y + halfExtents.y, center.z - halfExtents.z),
            new Vector3(center.x + halfExtents.x, center.y + halfExtents.y, center.z + halfExtents.z),
            new Vector3(center.x - halfExtents.x, center.y + halfExtents.y, center.z + halfExtents.z),
        ];

        Vector2[] uvs = [
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1),
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

    public void DrawWireSphere(Vector3 center, float radius, Color color, int segments = 16)
    {
        float step = MathF.PI * 2 / segments;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;

            Vector3 a = new(MathF.Cos(angle1) * radius + center.x,
                            MathF.Sin(angle1) * radius + center.y,
                            center.z
                        );

            Vector3 b = new(MathF.Cos(angle2) * radius + center.x,
                            MathF.Sin(angle2) * radius + center.y,
                            center.z
                        );

            AddLine(a, b, color);
        }

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;

            Vector3 a = new(MathF.Cos(angle1) * radius + center.x,
                            center.y,
                            MathF.Sin(angle1) * radius + center.z
                        );

            Vector3 b = new(MathF.Cos(angle2) * radius + center.x,
                            center.y,
                            MathF.Sin(angle2) * radius + center.z
                        );

            AddLine(a, b, color);
        }

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;

            Vector3 a = new(center.x,
                            MathF.Cos(angle1) * radius + center.y,
                            MathF.Sin(angle1) * radius + center.z
                        );

            Vector3 b = new(center.x,
                            MathF.Cos(angle2) * radius + center.y,
                            MathF.Sin(angle2) * radius + center.z
                        );

            AddLine(a, b, color);
        }
    }

    public void DrawSphere(Vector3 center, float radius, Color color, int segments = 16)
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

                Vector3 v1 = CalculatePointOnSphere(theta1, phi1, radius, center);
                Vector3 v2 = CalculatePointOnSphere(theta1, phi2, radius, center);
                Vector3 v3 = CalculatePointOnSphere(theta2, phi1, radius, center);
                Vector3 v4 = CalculatePointOnSphere(theta2, phi2, radius, center);

                // First triangle
                AddTriangle(v1, v2, v3, Vector2.zero, Vector2.zero, Vector2.zero, color);

                // Second triangle
                AddTriangle(v2, v4, v3, Vector2.zero, Vector2.zero, Vector2.zero, color);
            }
        }
    }

    private Vector3 CalculatePointOnSphere(float theta, float phi, float radius, Vector3 center)
    {
        float x = MathF.Sin(theta) * MathF.Cos(phi);
        float y = MathF.Cos(theta);
        float z = MathF.Sin(theta) * MathF.Sin(phi);

        return new Vector3(
            x * radius + center.x,
            y * radius + center.y,
            z * radius + center.z
        );
    }

    public void DrawWireCone(Vector3 start, Vector3 direction, float radius, Color color, int segments = 16)
    {
        float step = MathF.PI * 2 / segments;
        Vector3 tip = start + direction;

        // Normalize the direction vector
        Vector3 dir = Vector3.Normalize(direction);

        // Find perpendicular vectors
        Vector3 u = GetPerpendicularVector(dir);
        Vector3 v = Vector3.Cross(dir, u);

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;

            // Calculate circle points using the perpendicular vectors
            Vector3 a = start + radius * (MathF.Cos(angle1) * u + MathF.Sin(angle1) * v);
            Vector3 b = start + radius * (MathF.Cos(angle2) * u + MathF.Sin(angle2) * v);

            AddLine(a, b, color);
            if (i == 0 || i == segments / 4 || i == segments / 2 || i == segments * 3 / 4)
                AddLine(a, tip, color);
        }
    }

    private Vector3 GetPerpendicularVector(Vector3 v)
    {
        Vector3 result = Vector3.right;
        if (Math.Abs(v.x) > 0.1f)
            result = new Vector3(v.y, -v.x, 0);
        else if (Math.Abs(v.y) > 0.1f)
            result = new Vector3(0, v.z, -v.y);
        else
            result = new Vector3(-v.z, 0, v.x);
        return Vector3.Normalize(result);
    }

    public void DrawArrow(Vector3 start, Vector3 direction, Color color)
    {
        Vector3 axis = Vector3.Normalize(direction);
        Vector3 end = start + direction;
        AddLine(start, end, color);

        DrawWireCone(start + (direction * 0.9f), axis * 0.1f, 0.1f, color, 4);

    }

    public void DrawIcon(Texture2D icon, Vector3 center, float scale, Color color) => _icons.Add(new IconDrawCall { texture = icon, center = center, scale = scale, color = color });

    public void DrawText(Font font, string text, Vector3 position, Color color) // TODO: Try to share the same code from UI rendering to avoid duplicate Text rendering code
    {

    }

    public (Mesh? wire, Mesh? solid) UpdateMesh(bool cameraRelative, Vector3 cameraPosition)
    {
        bool hasWire = _wireData.s_vertices.Count > 0;
        if (hasWire)
        {
            _wire ??= new()
            {
                Topology = PrimitiveTopology.LineList,
                IndexFormat = IndexFormat.UInt16,
            };

            _wire.Vertices = [.. _wireData.s_vertices];
            _wire.Colors = [.. _wireData.s_colors];
            _wire.Indices16 = _wireData.s_indices.Select(i => (ushort)i).ToArray();

            if (cameraRelative)
            {
                // Convert vertices to be relative to the camera
                System.Numerics.Vector3[] vertices = new System.Numerics.Vector3[_wireData.s_vertices.Count];
                for (int i = 0; i < _wireData.s_vertices.Count; i++)
                    vertices[i] = _wireData.s_vertices[i] - cameraPosition;
                _wire.Vertices = vertices;
            }
            else
            {
                _wire.Vertices = [.. _wireData.s_vertices];
            }
        }

        bool hasSolid = _solidData.s_vertices.Count > 0;
        if (hasSolid)
        {
            _solid ??= new()
            {
                Topology = PrimitiveTopology.TriangleList,
                IndexFormat = IndexFormat.UInt16,
            };

            if (cameraRelative)
            {
                // Convert vertices to be relative to the camera
                System.Numerics.Vector3[] vertices2 = new System.Numerics.Vector3[_solidData.s_vertices.Count];
                for (int i = 0; i < _solidData.s_vertices.Count; i++)
                    vertices2[i] = _solidData.s_vertices[i] - cameraPosition;
                _solid.Vertices = vertices2;
            }
            else
            {
                _solid.Vertices = [.. _solidData.s_vertices];
            }

            _solid.Colors = [.. _solidData.s_colors];
            _solid.UV = [.. _solidData.s_uvs];
            _solid.Indices16 = _solidData.s_indices.Select(i => (ushort)i).ToArray();
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
