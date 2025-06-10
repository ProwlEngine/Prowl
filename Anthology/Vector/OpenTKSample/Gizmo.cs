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

    #region --- Additional Geometry Gizmos ---

    /// <summary>
    /// Draws an AABB as a wireframe box.
    /// </summary>
    public static void DrawAABB(AABBFloat aabb, Float4 color)
    {
        Float3[] corners = aabb.GetCorners();

        // Draw the 12 edges of the box
        // Bottom face (Z = min)
        DrawLine(corners[0], corners[1], color); // min->max X
        DrawLine(corners[1], corners[3], color); // max X->max Y
        DrawLine(corners[3], corners[2], color); // max Y->min X
        DrawLine(corners[2], corners[0], color); // min X->min Y

        // Top face (Z = max)
        DrawLine(corners[4], corners[5], color); // min->max X
        DrawLine(corners[5], corners[7], color); // max X->max Y
        DrawLine(corners[7], corners[6], color); // max Y->min X
        DrawLine(corners[6], corners[4], color); // min X->min Y

        // Vertical edges
        DrawLine(corners[0], corners[4], color); // min X, min Y
        DrawLine(corners[1], corners[5], color); // max X, min Y
        DrawLine(corners[2], corners[6], color); // min X, max Y
        DrawLine(corners[3], corners[7], color); // max X, max Y
    }

    /// <summary>
    /// Draws an AABB as a solid filled box.
    /// </summary>
    public static void DrawAABBSolid(AABBFloat aabb, Float4 color)
    {
        Float3[] corners = aabb.GetCorners();
        uint baseIndex = (uint)_solidVertices.Count;

        // Add all 8 corners as vertices
        for (int i = 0; i < 8; i++)
        {
            _solidVertices.Add(new Vertex(corners[i], color));
        }

        // Define the 12 triangles (counter-clockwise winding for outward-facing normals)
        // Corner indices from GetCorners():
        // 0: (Min.X, Min.Y, Min.Z)  1: (Max.X, Min.Y, Min.Z)
        // 2: (Min.X, Max.Y, Min.Z)  3: (Max.X, Max.Y, Min.Z)
        // 4: (Min.X, Min.Y, Max.Z)  5: (Max.X, Min.Y, Max.Z)
        // 6: (Min.X, Max.Y, Max.Z)  7: (Max.X, Max.Y, Max.Z)

        // Bottom face (Z = Min.Z) - corners 0,1,2,3
        _solidIndices.AddRange(new uint[] { baseIndex + 0, baseIndex + 2, baseIndex + 1 });
        _solidIndices.AddRange(new uint[] { baseIndex + 1, baseIndex + 2, baseIndex + 3 });

        // Top face (Z = Max.Z) - corners 4,5,6,7
        _solidIndices.AddRange(new uint[] { baseIndex + 4, baseIndex + 5, baseIndex + 6 });
        _solidIndices.AddRange(new uint[] { baseIndex + 5, baseIndex + 7, baseIndex + 6 });

        // Front face (Y = Min.Y) - corners 0,1,4,5
        _solidIndices.AddRange(new uint[] { baseIndex + 0, baseIndex + 1, baseIndex + 4 });
        _solidIndices.AddRange(new uint[] { baseIndex + 1, baseIndex + 5, baseIndex + 4 });

        // Back face (Y = Max.Y) - corners 2,3,6,7
        _solidIndices.AddRange(new uint[] { baseIndex + 2, baseIndex + 6, baseIndex + 3 });
        _solidIndices.AddRange(new uint[] { baseIndex + 3, baseIndex + 6, baseIndex + 7 });

        // Left face (X = Min.X) - corners 0,2,4,6
        _solidIndices.AddRange(new uint[] { baseIndex + 0, baseIndex + 4, baseIndex + 2 });
        _solidIndices.AddRange(new uint[] { baseIndex + 2, baseIndex + 4, baseIndex + 6 });

        // Right face (X = Max.X) - corners 1,3,5,7
        _solidIndices.AddRange(new uint[] { baseIndex + 1, baseIndex + 3, baseIndex + 5 });
        _solidIndices.AddRange(new uint[] { baseIndex + 3, baseIndex + 7, baseIndex + 5 });
    }

    /// <summary>
    /// Draws a triangle as wireframe or filled.
    /// </summary>
    public static void DrawTriangle(TriangleFloat triangle, Float4 color, bool filled = false)
    {
        if (filled)
        {
            // Add triangle as solid geometry
            uint baseIndex = (uint)_solidVertices.Count;
            _solidVertices.Add(new Vertex(triangle.V0, color));
            _solidVertices.Add(new Vertex(triangle.V1, color));
            _solidVertices.Add(new Vertex(triangle.V2, color));
            _solidIndices.AddRange(new uint[] { baseIndex, baseIndex + 1, baseIndex + 2 });
            _solidIndices.AddRange(new uint[] { baseIndex, baseIndex + 2, baseIndex + 1 });
        }
        else
        {
            // Draw wireframe
            DrawLine(triangle.V0, triangle.V1, color);
            DrawLine(triangle.V1, triangle.V2, color);
            DrawLine(triangle.V2, triangle.V0, color);
        }
    }

    /// <summary>
    /// Draws a ray with an arrowhead.
    /// </summary>
    public static void DrawRay(RayFloat ray, float length, Float4 color)
    {
        Float3 end = ray.GetPoint(length);
        DrawLine(ray.Origin, end, color);

        // Draw arrowhead
        Float3 arrowSize = Float3.One * (length * 0.1f);
        Float3 right = Maths.Normalize(Maths.Cross(ray.Direction, Float3.UnitY)) * arrowSize.X;
        Float3 up = Maths.Normalize(Maths.Cross(right, ray.Direction)) * arrowSize.Y;

        Float3 arrowBase = end - ray.Direction * arrowSize.Z;
        DrawLine(end, arrowBase + right, color);
        DrawLine(end, arrowBase - right, color);
        DrawLine(end, arrowBase + up, color);
        DrawLine(end, arrowBase - up, color);
    }

    /// <summary>
    /// Draws a sphere as wireframe with latitude and longitude lines.
    /// </summary>
    public static void DrawSphereWireframe(SphereFloat sphere, Float4 color, int segments = 16)
    {
        // Draw latitude circles
        for (int lat = 0; lat <= segments; lat++)
        {
            float theta = lat * (float)Maths.PI / segments;
            float y = sphere.Center.Y + sphere.Radius * Maths.Cos(theta);
            float radius = sphere.Radius * Maths.Sin(theta);

            for (int lon = 0; lon < segments; lon++)
            {
                float phi1 = lon * 2 * (float)Maths.PI / segments;
                float phi2 = (lon + 1) * 2 * (float)Maths.PI / segments;

                Float3 p1 = sphere.Center + new Float3(
                    radius * Maths.Cos(phi1),
                    y - sphere.Center.Y,
                    radius * Maths.Sin(phi1)
                );
                Float3 p2 = sphere.Center + new Float3(
                    radius * Maths.Cos(phi2),
                    y - sphere.Center.Y,
                    radius * Maths.Sin(phi2)
                );

                DrawLine(p1, p2, color);
            }
        }

        // Draw longitude lines
        for (int lon = 0; lon < segments; lon++)
        {
            float phi = lon * 2 * (float)Maths.PI / segments;

            for (int lat = 0; lat < segments; lat++)
            {
                float theta1 = lat * (float)Maths.PI / segments;
                float theta2 = (lat + 1) * (float)Maths.PI / segments;

                Float3 p1 = sphere.Center + new Float3(
                    sphere.Radius * Maths.Sin(theta1) * Maths.Cos(phi),
                    sphere.Radius * Maths.Cos(theta1),
                    sphere.Radius * Maths.Sin(theta1) * Maths.Sin(phi)
                );
                Float3 p2 = sphere.Center + new Float3(
                    sphere.Radius * Maths.Sin(theta2) * Maths.Cos(phi),
                    sphere.Radius * Maths.Cos(theta2),
                    sphere.Radius * Maths.Sin(theta2) * Maths.Sin(phi)
                );

                DrawLine(p1, p2, color);
            }
        }
    }

    /// <summary>
    /// Draws a plane as a grid within specified bounds.
    /// </summary>
    public static void DrawPlane(Float3 center, Float3 normal, Float3 size, Float4 color, int gridLines = 10)
    {
        // Create a coordinate system for the plane
        Float3 right = Maths.Normalize(Maths.Cross(normal, Float3.UnitY));
        if (Maths.LengthSquared(right) < 0.1f) // Handle case where normal is parallel to Y
            right = Maths.Normalize(Maths.Cross(normal, Float3.UnitX));
        Float3 forward = Maths.Normalize(Maths.Cross(right, normal));

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
