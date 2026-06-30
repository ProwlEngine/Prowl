// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using OpenTK.Graphics.OpenGL4;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample;

/// <summary>
/// Specifies the rendering mode for geometry visualization.
/// </summary>
public enum MeshMode
{
    /// <summary>
    /// Render as wireframe/outline using lines.
    /// </summary>
    Wireframe,

    /// <summary>
    /// Render as solid filled geometry using triangles.
    /// </summary>
    Solid
}

/// <summary>
/// Flags for visualizing different elements of GeometryData (BMesh).
/// </summary>
[Flags]
public enum GeometryDataVisualization
{
    /// <summary>
    /// No visualization.
    /// </summary>
    None = 0,

    /// <summary>
    /// Draw vertex normals as lines extending from vertices.
    /// </summary>
    Normals = 1 << 0,

    /// <summary>
    /// Draw all edges as colored lines.
    /// </summary>
    Edges = 1 << 1,

    /// <summary>
    /// Draw loop positions (face corners) as small markers.
    /// </summary>
    Loops = 1 << 2,

    /// <summary>
    /// Draw vertices as crosses.
    /// </summary>
    Vertices = 1 << 3,

    /// <summary>
    /// Draw solid mesh first, then overlay debug info on top.
    /// </summary>
    Solid = 1 << 4,

    /// <summary>
    /// Draw all debug elements.
    /// </summary>
    All = Normals | Edges | Loops | Vertices | Solid
}

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
    /// Generic method to draw any IBoundingShape using its GetGeometryData implementation.
    /// </summary>
    public static void DrawShape(IBoundingShape shape, Float4 color, MeshMode mode, int resolution = 16)
    {
        GeometryData geometryData = shape.GetGeometryData(resolution);

        if (mode == MeshMode.Wireframe)
        {
            // Convert to line mesh and draw edges
            var lineMesh = geometryData.ToLineMesh();
            DrawLineMesh(lineMesh, color);
        }
        else
        {
            // Convert to triangle mesh and draw solid
            var triangleMesh = geometryData.ToTriangleMesh();
            DrawTriangleMesh(triangleMesh, color);
        }
    }

    /// <summary>
    /// Draw a triangle mesh from GeometryData.
    /// </summary>
    public static void DrawTriangleMesh(GeometryData.TriangleMesh mesh, Float4 color)
    {
        if (mesh.Vertices == null || mesh.Vertices.Length == 0)
            return;

        uint baseIndex = (uint)_solidVertices.Count;

        // Add vertices
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            _solidVertices.Add(new Vertex((Float3)mesh.Vertices[i], color));
        }

        // Add indices
        for (int i = 0; i < mesh.Indices.Length; i++)
        {
            _solidIndices.Add(baseIndex + mesh.Indices[i]);
        }
    }

    /// <summary>
    /// Draw a line mesh from GeometryData.
    /// </summary>
    public static void DrawLineMesh(GeometryData.LineMesh mesh, Float4 color)
    {
        if (mesh.Vertices == null || mesh.Vertices.Length == 0)
            return;

        uint baseIndex = (uint)_lineVertices.Count;

        // Add vertices
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            _lineVertices.Add(new Vertex((Float3)mesh.Vertices[i], color));
        }

        // Add indices
        for (int i = 0; i < mesh.Indices.Length; i++)
        {
            _lineIndices.Add(baseIndex + mesh.Indices[i]);
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

        Float3 n1 = p1.Normal;
        Float3 n2 = p2.Normal;
        Float3 n3 = p3.Normal;
        float d1 = p1.D;
        float d2 = p2.D;
        float d3 = p3.D;

        Float3 cross23 = Float3.Cross(n2, n3);
        Float3 cross31 = Float3.Cross(n3, n1);
        Float3 cross12 = Float3.Cross(n1, n2);

        float det = Float3.Dot(n1, cross23);

        if (Math.Abs(det) < 1e-6f)
            return Float3.Zero; // Planes don't intersect at a single point

        Float3 intersection = (cross23 * d1 + cross31 * d2 + cross12 * d3) / det;
        return (Float3)intersection;
    }

    public static void DrawConeWireframe(Cone cone, Float4 color, int segments = 16)
    {
        DrawShape(cone, color, MeshMode.Wireframe, segments);
    }

    /// <summary>
    /// Draws a GeometryData (BMesh) with various visualization options.
    /// When Solid flag is set, draws the solid mesh first, then overlays debug info on top.
    /// </summary>
    /// <param name="geometryData">The GeometryData to visualize</param>
    /// <param name="flags">Visualization flags</param>
    /// <param name="solidColor">Color for solid rendering (default: semi-transparent white)</param>
    /// <param name="normalColor">Color for normals (default: yellow)</param>
    /// <param name="edgeColor">Color for edges (default: cyan)</param>
    /// <param name="loopColor">Color for loops (default: magenta)</param>
    /// <param name="vertexColor">Color for vertices (default: red)</param>
    /// <param name="normalLength">Length of normal lines (default: 0.2)</param>
    /// <param name="vertexSize">Size of vertex crosses (default: 0.05)</param>
    /// <param name="loopSize">Size of loop markers (default: 0.03)</param>
    public static void DrawGeometryData(
        GeometryData geometryData,
        GeometryDataVisualization flags,
        Float4? solidColor = null,
        Float4? normalColor = null,
        Float4? edgeColor = null,
        Float4? loopColor = null,
        Float4? vertexColor = null,
        float normalLength = 0.2f,
        float vertexSize = 0.05f,
        float loopSize = 0.03f)
    {
        // Set default colors if not provided
        solidColor ??= new Float4(0.8f, 0.8f, 0.8f, 0.6f);
        normalColor ??= new Float4(1.0f, 1.0f, 0.0f, 1.0f); // Yellow
        edgeColor ??= new Float4(0.0f, 1.0f, 1.0f, 1.0f);   // Cyan
        loopColor ??= new Float4(1.0f, 0.0f, 1.0f, 1.0f);   // Magenta
        vertexColor ??= new Float4(1.0f, 0.0f, 0.0f, 1.0f); // Red

        // Step 1: Draw solid mesh first if requested
        if (flags.HasFlag(GeometryDataVisualization.Solid))
        {
            var triangleMesh = geometryData.ToTriangleMesh();
            DrawTriangleMesh(triangleMesh, solidColor.Value);
        }

        // Step 2: Draw edges
        if (flags.HasFlag(GeometryDataVisualization.Edges))
        {
            foreach (var edge in geometryData.Edges)
            {
                DrawLine((Float3)edge.Vert1.Point, (Float3)edge.Vert2.Point, edgeColor.Value);
            }
        }

        // Step 3: Draw loops (face corners) as arrows along edges
        if (flags.HasFlag(GeometryDataVisualization.Loops))
        {
            foreach (var face in geometryData.Faces)
            {
                if (face.Loop != null)
                {
                    // Calculate face normal and center for offsetting arrows inward
                    var verts = face.NeighborVertices();
                    Float3 faceNormal = Float3.UnitY;
                    Float3 faceCenter = face.Center();

                    if (verts.Count >= 3)
                    {
                        var v0 = verts[0].Point;
                        var v1 = verts[1].Point;
                        var v2 = verts[2].Point;
                        var edge1 = v1 - v0;
                        var edge2 = v2 - v0;
                        var normal = Float3.Cross(edge1, edge2);
                        if (Float3.LengthSquared(normal) > 1e-6)
                            faceNormal = Float3.Normalize(normal);
                    }

                    var it = face.Loop;
                    do
                    {
                        // Get edge direction
                        var nextLoop = it.Next;
                        if (nextLoop != null && nextLoop != it)
                        {
                            Float3 edgeStart = it.Vert.Point;
                            Float3 edgeEnd = nextLoop.Vert.Point;
                            Float3 edgeDir = Float3.Normalize(edgeEnd - edgeStart);
                            float edgeLength = Float3.Distance(edgeStart, edgeEnd);

                            // Position arrow slightly along the edge and offset inward toward face center
                            float arrowT = 0.4f; // Position along edge
                            Float3 arrowBase = Maths.Lerp(edgeStart, edgeEnd, arrowT);

                            // Offset slightly inward (toward face center) and up along normal
                            Float3 toCenter = Float3.Normalize(faceCenter - arrowBase);
                            arrowBase += toCenter * (edgeLength * 0.15f) + faceNormal * loopSize;

                            // Arrow points along edge direction
                            Float3 arrowTip = arrowBase + edgeDir * (edgeLength * 0.2f);

                            // Draw arrow shaft
                            DrawLine((Float3)arrowBase, (Float3)arrowTip, loopColor.Value);

                            // Draw arrowhead
                            Float3 perpendicular = Float3.Normalize(Float3.Cross(edgeDir, faceNormal));
                            Float3 arrowLeft = arrowTip - edgeDir * (edgeLength * 0.08f) + perpendicular * (edgeLength * 0.05f);
                            Float3 arrowRight = arrowTip - edgeDir * (edgeLength * 0.08f) - perpendicular * (edgeLength * 0.05f);

                            DrawLine((Float3)arrowTip, (Float3)arrowLeft, loopColor.Value);
                            DrawLine((Float3)arrowTip, (Float3)arrowRight, loopColor.Value);
                        }

                        it = it.Next;
                    } while (it != face.Loop && it != null);
                }
            }
        }

        // Step 4: Draw vertices
        if (flags.HasFlag(GeometryDataVisualization.Vertices))
        {
            foreach (var vertex in geometryData.Vertices)
            {
                DrawIntersectionPoint((Float3)vertex.Point, vertexColor.Value, vertexSize);
            }
        }

        // Step 5: Draw normals
        if (flags.HasFlag(GeometryDataVisualization.Normals))
        {
            foreach (var vertex in geometryData.Vertices)
            {
                // Try to get normal from vertex attributes
                Float3 normal = Float3.UnitY; // Default normal
                bool hasNormal = false;

                if (vertex.Attributes.TryGetValue("normal", out var normalAttr))
                {
                    if (normalAttr is GeometryData.FloatAttributeValue floatAttr && floatAttr.Data.Length >= 3)
                    {
                        normal = new Float3(floatAttr.Data[0], floatAttr.Data[1], floatAttr.Data[2]);
                        // Ensure normal is normalized
                        if (Float3.LengthSquared(normal) > 1e-6)
                        {
                            normal = Float3.Normalize(normal);
                            hasNormal = true;
                        }
                    }
                }

                // If no normal attribute, calculate from adjacent faces
                if (!hasNormal)
                {
                    var adjacentFaces = vertex.NeighborFaces().ToArray();
                    if (adjacentFaces.Length > 0)
                    {
                        normal = Float3.Zero;
                        foreach (var face in adjacentFaces)
                        {
                            // Calculate face normal
                            var verts = face.NeighborVertices();
                            if (verts.Count >= 3)
                            {
                                var v0 = verts[0].Point;
                                var v1 = verts[1].Point;
                                var v2 = verts[2].Point;
                                var edge1 = v1 - v0;
                                var edge2 = v2 - v0;
                                var faceNormal = Float3.Cross(edge1, edge2);
                                if (Float3.LengthSquared(faceNormal) > 1e-6)
                                {
                                    normal += Float3.Normalize(faceNormal);
                                }
                            }
                        }
                        if (Float3.LengthSquared(normal) > 1e-6)
                        {
                            normal = Float3.Normalize(normal);
                        }
                        else
                        {
                            normal = Float3.UnitY;
                        }
                    }
                }

                Float3 start = (Float3)vertex.Point;
                Float3 end = start + (Float3)normal * normalLength;
                DrawLine(start, end, normalColor.Value);
            }
        }
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
