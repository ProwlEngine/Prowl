// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using OpenTK.Graphics.OpenGL4;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample;

public static class Gizmo
{
    private struct Vertex
    {
        public Float3 Position;
        public Float4 Color;

        public Vertex(Float3 position, Float4 color)
        {
            Position = position;
            Color = color;
        }
    }

    private static readonly List<Vertex> _lineVertices = new();
    private static readonly List<uint> _lineIndices = new();
    private static readonly List<Vertex> _solidVertices = new();
    private static readonly List<uint> _solidIndices = new();

    private static uint _lineVAO, _lineVBO, _lineEBO;
    private static uint _solidVAO, _solidVBO, _solidEBO;
    private static uint _shaderProgram;

    private const string VertexShaderSource = @"
#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec4 aColor;
uniform mat4 uViewProjection;
out vec4 FragColor;
void main() {
    gl_Position = uViewProjection * vec4(aPosition, 1.0);
    FragColor = aColor;
}";

    private const string FragmentShaderSource = @"
#version 330 core
in vec4 FragColor;
out vec4 color;
void main() {
    color = FragColor;
}";

    public static void Initialize()
    {
        InitializeBuffers();
        CreateShaderProgram();
    }

    private static void InitializeBuffers()
    {
        // Line buffers
        _lineVAO = (uint)GL.GenVertexArray();
        _lineVBO = (uint)GL.GenBuffer();
        _lineEBO = (uint)GL.GenBuffer();

        GL.BindVertexArray(_lineVAO);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _lineVBO);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _lineEBO);

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        // Solid buffers
        _solidVAO = (uint)GL.GenVertexArray();
        _solidVBO = (uint)GL.GenBuffer();
        _solidEBO = (uint)GL.GenBuffer();

        GL.BindVertexArray(_solidVAO);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _solidVBO);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _solidEBO);

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        GL.BindVertexArray(0);
    }

    private static void CreateShaderProgram()
    {
        uint vs = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        uint fs = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

        _shaderProgram = (uint)GL.CreateProgram();
        GL.AttachShader(_shaderProgram, vs);
        GL.AttachShader(_shaderProgram, fs);
        GL.LinkProgram(_shaderProgram);

        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
    }

    private static uint CompileShader(ShaderType type, string source)
    {
        uint shader = (uint)GL.CreateShader(type);
        GL.ShaderSource((int)shader, source);
        GL.CompileShader(shader);
        return shader;
    }

    // Line drawing methods
    public static void DrawLine(Float3 start, Float3 end, Float4 color)
    {
        uint baseIndex = (uint)_lineVertices.Count;
        _lineVertices.Add(new Vertex(start, color));
        _lineVertices.Add(new Vertex(end, color));
        _lineIndices.Add(baseIndex);
        _lineIndices.Add(baseIndex + 1);
    }

    /// <summary>
    /// Generic method to draw any IBoundingShape using its GetMeshData implementation.
    /// </summary>
    public static void DrawShape(IBoundingShape shape, Float4 color, MeshMode mode, int resolution = 16)
    {
        MeshData meshData = shape.GetMeshData(mode, resolution);

        if (meshData.Vertices.Length == 0)
            return;

        bool isLine = meshData.Topology == MeshTopology.LineList || meshData.Topology == MeshTopology.LineStrip;
        var targetVertices = isLine ? _lineVertices : _solidVertices;
        var targetIndices = isLine ? _lineIndices : _solidIndices;

        uint baseIndex = (uint)targetVertices.Count;

        // Add vertices
        for (int i = 0; i < meshData.Vertices.Length; i++)
        {
            targetVertices.Add(new Vertex((Float3)meshData.Vertices[i], color));
        }

        // Add indices based on topology
        if (meshData.IsIndexed)
        {
            // Use provided indices
            for (int i = 0; i < meshData.Indices!.Length; i++)
            {
                targetIndices.Add(baseIndex + meshData.Indices[i]);
            }
        }
        else
        {
            // Generate indices based on topology
            if (meshData.Topology == MeshTopology.LineList || meshData.Topology == MeshTopology.TriangleList)
            {
                // Direct sequential indices
                for (uint i = 0; i < meshData.Vertices.Length; i++)
                {
                    targetIndices.Add(baseIndex + i);
                }
            }
            else if (meshData.Topology == MeshTopology.LineStrip)
            {
                // Convert line strip to line list
                for (uint i = 0; i < meshData.Vertices.Length - 1; i++)
                {
                    targetIndices.Add(baseIndex + i);
                    targetIndices.Add(baseIndex + i + 1);
                }
            }
            else if (meshData.Topology == MeshTopology.TriangleStrip)
            {
                // Convert triangle strip to triangle list
                for (uint i = 0; i < meshData.Vertices.Length - 2; i++)
                {
                    if (i % 2 == 0)
                    {
                        targetIndices.Add(baseIndex + i);
                        targetIndices.Add(baseIndex + i + 1);
                        targetIndices.Add(baseIndex + i + 2);
                    }
                    else
                    {
                        targetIndices.Add(baseIndex + i);
                        targetIndices.Add(baseIndex + i + 2);
                        targetIndices.Add(baseIndex + i + 1);
                    }
                }
            }
        }
    }

    #region --- Additional Geometry Gizmos ---

    /// <summary>
    /// Draws an AABB as a wireframe box.
    /// </summary>
    public static void DrawAABB(AABB aabb, Float4 color)
    {
        DrawShape(aabb, color, MeshMode.Wireframe);
    }

    /// <summary>
    /// Draws an AABB as a solid filled box.
    /// </summary>
    public static void DrawAABBSolid(AABB aabb, Float4 color)
    {
        DrawShape(aabb, color, MeshMode.Solid);
    }

    /// <summary>
    /// Draws a triangle as wireframe or filled.
    /// </summary>
    public static void DrawTriangle(Triangle triangle, Float4 color, bool filled = false)
    {
        DrawShape(triangle, color, filled ? MeshMode.Solid : MeshMode.Wireframe);
    }

    /// <summary>
    /// Draws a ray with an arrowhead.
    /// </summary>
    public static void DrawRay(Ray ray, float length, Float4 color)
    {
        Float3 end = (Float3)ray.GetPoint(length);
        DrawLine((Float3)ray.Origin, end, color);

        // Draw arrowhead
        Float3 arrowSize = Float3.One * (length * 0.1f);
        Float3 right = Float3.Normalize(Float3.Cross((Float3)ray.Direction, Float3.UnitY)) * arrowSize.X;
        Float3 up = Float3.Normalize(Float3.Cross((Float3)right, (Float3)ray.Direction)) * arrowSize.Y;

        Float3 arrowBase = end - (Float3)ray.Direction * arrowSize.Z;
        DrawLine(end, arrowBase + right, color);
        DrawLine(end, arrowBase - right, color);
        DrawLine(end, arrowBase + up, color);
        DrawLine(end, arrowBase - up, color);
    }

    /// <summary>
    /// Draws a sphere as wireframe with latitude and longitude lines.
    /// </summary>
    public static void DrawSphereWireframe(Sphere sphere, Float4 color, int segments = 16)
    {
        DrawShape(sphere, color, MeshMode.Wireframe, segments);
    }

    /// <summary>
    /// Draws a plane as a grid within specified bounds.
    /// </summary>
    public static void DrawPlane(Float3 center, Float3 normal, Float3 size, Float4 color, int gridLines = 10)
    {
        // Create a coordinate system for the plane
        Float3 right = Float3.Normalize(Float3.Cross(normal, Float3.UnitY));
        if (Float3.LengthSquared(right) < 0.1f) // Handle case where normal is parallel to Y
            right = Float3.Normalize(Float3.Cross(normal, Float3.UnitX));
        Float3 forward = Float3.Normalize(Float3.Cross(right, normal));

        Float3 halfSize = size * 0.5f;

        // Draw grid lines along right axis
        for (int i = 0; i <= gridLines; i++)
        {
            float t = (float)i / gridLines;
            float offset = Maths.Lerp(-halfSize.X, halfSize.X, t);
            Float3 start = center + right * offset - forward * halfSize.Z;
            Float3 end = center + right * offset + forward * halfSize.Z;
            DrawLine(start, end, color);
        }

        // Draw grid lines along forward axis
        for (int i = 0; i <= gridLines; i++)
        {
            float t = (float)i / gridLines;
            float offset = Maths.Lerp(-halfSize.Z, halfSize.Z, t);
            Float3 start = center + forward * offset - right * halfSize.X;
            Float3 end = center + forward * offset + right * halfSize.X;
            DrawLine(start, end, color);
        }
    }

    /// <summary>
    /// Draws an intersection point with a small cross.
    /// </summary>
    public static void DrawIntersectionPoint(Float3 point, Float4 color, float size = 0.1f)
    {
        Float3 offset = Float3.One * size;
        DrawLine(point - new Float3(offset.X, 0, 0), point + new Float3(offset.X, 0, 0), color);
        DrawLine(point - new Float3(0, offset.Y, 0), point + new Float3(0, offset.Y, 0), color);
        DrawLine(point - new Float3(0, 0, offset.Z), point + new Float3(0, 0, offset.Z), color);
    }

    /// <summary>
    /// Draws a frustum as wireframe showing all 6 planes.
    /// </summary>
    public static void DrawFrustum(Frustum frustum, Float4 color)
    {
        DrawShape(frustum, color, MeshMode.Wireframe);
    }

    /// <summary>
    /// Calculates the 8 corner points of a frustum by intersecting planes.
    /// </summary>
    private static Float3[] CalculateFrustumCorners(Frustum frustum)
    {
        var planes = frustum.Planes;
        if (planes == null || planes.Length != 6)
            return null;

        var corners = new Float3[8];

        // Near plane corners (intersections with near, left/right, top/bottom)
        corners[0] = IntersectThreePlanes(planes[0], planes[2], planes[5]); // near, left, bottom
        corners[1] = IntersectThreePlanes(planes[0], planes[3], planes[5]); // near, right, bottom
        corners[2] = IntersectThreePlanes(planes[0], planes[3], planes[4]); // near, right, top
        corners[3] = IntersectThreePlanes(planes[0], planes[2], planes[4]); // near, left, top

        // Far plane corners (intersections with far, left/right, top/bottom)
        corners[4] = IntersectThreePlanes(planes[1], planes[2], planes[5]); // far, left, bottom
        corners[5] = IntersectThreePlanes(planes[1], planes[3], planes[5]); // far, right, bottom
        corners[6] = IntersectThreePlanes(planes[1], planes[3], planes[4]); // far, right, top
        corners[7] = IntersectThreePlanes(planes[1], planes[2], planes[4]); // far, left, top

        return corners;
    }

    /// <summary>
    /// Finds the intersection point of three planes.
    /// </summary>
    private static Float3 IntersectThreePlanes(Plane p1, Plane p2, Plane p3)
    {
        // Using the formula: intersection = (cross(n2,n3)*d1 + cross(n3,n1)*d2 + cross(n1,n2)*d3) / det
        // where det = dot(n1, cross(n2,n3))
        // Plane equation: dot(normal, point) = D

        Double3 n1 = p1.Normal;
        Double3 n2 = p2.Normal;
        Double3 n3 = p3.Normal;
        double d1 = p1.D;
        double d2 = p2.D;
        double d3 = p3.D;

        Double3 cross23 = Double3.Cross(n2, n3);
        Double3 cross31 = Double3.Cross(n3, n1);
        Double3 cross12 = Double3.Cross(n1, n2);

        double det = Double3.Dot(n1, cross23);

        if (Math.Abs(det) < 1e-6)
            return Float3.Zero; // Planes don't intersect at a single point

        Double3 intersection = (cross23 * d1 + cross31 * d2 + cross12 * d3) / det;
        return (Float3)intersection;
    }

    public static void DrawConeWireframe(Cone cone, Float4 color, int segments = 16)
    {
        DrawShape(cone, color, MeshMode.Wireframe, segments);
    }

    #endregion

    public static void Render(Float4x4 viewProjectionMatrix)
    {
        GL.UseProgram(_shaderProgram);

        // Upload view-projection matrix
        float[] matrixArray = new float[16];
        matrixArray[0] = viewProjectionMatrix.c0.X; matrixArray[1] = viewProjectionMatrix.c0.Y; matrixArray[2] = viewProjectionMatrix.c0.Z; matrixArray[3] = viewProjectionMatrix.c0.W;
        matrixArray[4] = viewProjectionMatrix.c1.X; matrixArray[5] = viewProjectionMatrix.c1.Y; matrixArray[6] = viewProjectionMatrix.c1.Z; matrixArray[7] = viewProjectionMatrix.c1.W;
        matrixArray[8] = viewProjectionMatrix.c2.X; matrixArray[9] = viewProjectionMatrix.c2.Y; matrixArray[10] = viewProjectionMatrix.c2.Z; matrixArray[11] = viewProjectionMatrix.c2.W;
        matrixArray[12] = viewProjectionMatrix.c3.X; matrixArray[13] = viewProjectionMatrix.c3.Y; matrixArray[14] = viewProjectionMatrix.c3.Z; matrixArray[15] = viewProjectionMatrix.c3.W;

        int location = GL.GetUniformLocation(_shaderProgram, "uViewProjection");
        GL.UniformMatrix4(location, 1, false, matrixArray);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Render lines
        if (_lineVertices.Count > 0)
        {
            RenderBuffer(_lineVAO, _lineVBO, _lineEBO, _lineVertices, _lineIndices, PrimitiveType.Lines);
        }

        // Render solids
        if (_solidVertices.Count > 0)
        {
            RenderBuffer(_solidVAO, _solidVBO, _solidEBO, _solidVertices, _solidIndices, PrimitiveType.Triangles);
        }

        GL.Disable(EnableCap.Blend);
    }

    private static void RenderBuffer(uint vao, uint vbo, uint ebo, List<Vertex> vertices, List<uint> indices, PrimitiveType primitiveType)
    {
        GL.BindVertexArray(vao);

        // Upload vertex data
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        var vertexData = new float[vertices.Count * 7];
        for (int i = 0; i < vertices.Count; i++)
        {
            int offset = i * 7;
            vertexData[offset] = vertices[i].Position.X;
            vertexData[offset + 1] = vertices[i].Position.Y;
            vertexData[offset + 2] = vertices[i].Position.Z;
            vertexData[offset + 3] = vertices[i].Color.X;
            vertexData[offset + 4] = vertices[i].Color.Y;
            vertexData[offset + 5] = vertices[i].Color.Z;
            vertexData[offset + 6] = vertices[i].Color.W;
        }
        GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float), vertexData, BufferUsageHint.DynamicDraw);

        // Upload index data
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.DynamicDraw);

        GL.DrawElements(primitiveType, indices.Count, DrawElementsType.UnsignedInt, 0);
    }

    public static void Clear()
    {
        _lineVertices.Clear();
        _lineIndices.Clear();
        _solidVertices.Clear();
        _solidIndices.Clear();
    }

    public static void Dispose()
    {
        GL.DeleteVertexArray(_lineVAO);
        GL.DeleteBuffer(_lineVBO);
        GL.DeleteBuffer(_lineEBO);
        GL.DeleteVertexArray(_solidVAO);
        GL.DeleteBuffer(_solidVBO);
        GL.DeleteBuffer(_solidEBO);
        GL.DeleteProgram(_shaderProgram);
    }
}
