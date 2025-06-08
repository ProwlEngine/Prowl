// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using SoftwareRasterizer.Rasterizer.Engines;

namespace SoftwareRasterizer.Rasterizer;

public partial class GraphicsDevice
{
    internal bool DoDepthWrite = true;
    internal bool DoDepthTest = true;
    internal CullMode CullMode = CullMode.Back;
    internal PolygonMode PolygonMode = PolygonMode.Fill;

    internal FrameBuffer CurrentFramebuffer;
    internal Shader CurrentShader;
    internal BlendFunction CurrentBlendFunction = BlendFunction.Normal;


    internal FrameBuffer backBuffer;

    private float s_halfWidth;
    private float s_halfHeight;

    private PointRasterizer pointRasterizer;
    private LineRasterizer lineRasterizer;
    private TriangleRasterizer triangleRasterizer;

    private RasterizerBase curRasterizer;

    public GraphicsDevice(int width, int height)
    {
        backBuffer = new FrameBuffer(this, width, height);

        CurrentFramebuffer = backBuffer;
        s_halfWidth = CurrentFramebuffer.Width * 0.5f;
        s_halfHeight = CurrentFramebuffer.Height * 0.5f;

        pointRasterizer = new(this);
        lineRasterizer = new(this);
        triangleRasterizer = new(this);

        curRasterizer = triangleRasterizer; // Default rasterizer
    }

    public FrameBuffer BackBuffer => backBuffer;

    public void SetPolygonMode(PolygonMode polygonMode)
    {
        PolygonMode = polygonMode;

        curRasterizer = polygonMode switch
        {
            PolygonMode.Point => pointRasterizer,
            PolygonMode.Line => lineRasterizer,
            PolygonMode.Fill => triangleRasterizer,
            _ => throw new Exception("Invalid polygon mode")
        };
    }
    public void SetCullMode(CullMode cullMode) => CullMode = cullMode;
    public void SetDoDepthWrite(bool doDepthWrite) => DoDepthWrite = doDepthWrite;
    public void SetDoDepthTest(bool doDepthTest) => DoDepthTest = doDepthTest;

    public void BindFramebuffer(FrameBuffer framebuffer) => CurrentFramebuffer = framebuffer;
    public void BindShader(Shader shader) => CurrentShader = shader;
    public void SetBlendFunction(BlendFunction blendFunction) => CurrentBlendFunction = blendFunction;

    public void ClearFramebuffer(float r, float g, float b, float a) => CurrentFramebuffer.Clear(r, g, b, a);

    public void BlitFramebuffer(FrameBuffer srcFramebuffer, int srcX, int srcY, int srcWidth, int srcHeight, int dstX, int dstY, int dstWidth, int dstHeight, bool blitColor, bool blitDepth) => BlitFramebuffer(srcFramebuffer, null, srcX, srcY, srcWidth, srcHeight, dstX, dstY, dstWidth, dstHeight, blitColor, blitDepth);
    public void BlitFramebuffer(FrameBuffer srcFramebuffer, FrameBuffer dstFramebuffer, int srcX, int srcY, int srcWidth, int srcHeight, int dstX, int dstY, int dstWidth, int dstHeight, bool blitColor, bool blitDepth)
    {
        // If no destination framebuffer is specified, use the back buffers
        dstFramebuffer ??= BackBuffer;

        // Simple and fast nearest-neighbor scaling
        int scaleX = dstWidth / srcWidth;
        int scaleY = dstHeight / srcHeight;

        for (int y = 0; y < dstHeight; y++)
        {
            for (var x = 0; x < dstWidth; x++)
            {
                int srcPixelX = (int)(Math.Floor(x / (float)scaleX) + srcX);
                int srcPixelY = (int)(Math.Floor(y / (float)scaleY) + srcY);

                if (srcPixelX >= srcX && srcPixelX < srcX + srcWidth &&
                    srcPixelY >= srcY && srcPixelY < srcY + srcHeight)
                {
                    var color = srcFramebuffer.GetPixel(srcPixelX, srcPixelY);
                    var srcDepth = srcFramebuffer.GetDepth(srcPixelX, srcPixelY);
                    int dstX_final = x + dstX;
                    int dstY_final = y + dstY;

                    // Always overwrite the destination pixel and depth
                    if (blitColor) dstFramebuffer.SetPixelUnsafe(dstX_final, dstY_final, color);
                    if (blitDepth) dstFramebuffer.SetDepthUnsafe(dstX_final, dstY_final, srcDepth);
                }
            }
        }
    }



    public void Resize(int width, int height)
    {
        backBuffer = new FrameBuffer(this, width, height);

        CurrentFramebuffer = backBuffer;
        s_halfWidth = CurrentFramebuffer.Width * 0.5f;
        s_halfHeight = CurrentFramebuffer.Height * 0.5f;
    }

    public void DrawVertexBuffer(VertexBuffer buffer)
    {
        if (CurrentShader == null)
            throw new Exception("No shader bound");

        CurrentShader.BindVertexAttributes(buffer.VertexAttributes);

        CurrentShader.Prepare();

        Parallel.For(0, buffer.Indices.Length / 3, triangleIndex =>
        {
            int baseIndex = triangleIndex * 3;
            ProcessTriangle(buffer.Indices[baseIndex], buffer.Indices[baseIndex + 1], buffer.Indices[baseIndex + 2]);
        });
        //for (var i = 0; i < buffer.Indices.Length; i += 3)
        //{
        //    ProcessTriangle(buffer.Indices[i], buffer.Indices[i + 1], buffer.Indices[i + 2]);
        //}
    }

    #region Triangle Rasterization

    // a temporary triangle used for processing
    private readonly ThreadLocal<RasterTriangle> tempTriangle = new ThreadLocal<RasterTriangle>(() => new RasterTriangle { Vertices = new RasterVertex[3] });
    private readonly ThreadLocal<RasterTriangle> _screenSpaceTriangle = new ThreadLocal<RasterTriangle>(() => new RasterTriangle { Vertices = new RasterVertex[3] });

    private bool QuickTriangleReject(RasterTriangle triangle)
    {
        const float nearPlane = -0.1f;
        const float farPlane = 100f;

        float z0 = triangle.Vertices[0].Position.Z;
        float z1 = triangle.Vertices[1].Position.Z;
        float z2 = triangle.Vertices[2].Position.Z;

        float minZ = Math.Min(z0, Math.Min(z1, z2));
        float maxZ = Math.Max(z0, Math.Max(z1, z2));

        return maxZ < nearPlane || minZ > farPlane;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsTriangleFullyInside(RasterTriangle triangle)
    {
        const float nearPlane = -0.1f;
        const float farPlane = 100f;

        // Check if all vertices are inside view frustum
        for (int i = 0; i < 3; i++)
        {
            var pos = triangle.Vertices[i].Position;
            float w = Math.Abs(pos.W) < 0.00001f ? 0.00001f : pos.W;

            if (pos.Z < nearPlane || pos.Z > farPlane ||
                pos.X < -w || pos.X > w ||
                pos.Y < -w || pos.Y > w)
            {
                return false;
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TransformToScreenSpaceFast(RasterTriangle clipSpaceTriangle, RasterTriangle screenSpaceTriangle)
    {
        for (int i = 0; i < 3; i++)
        {
            ref var vertex = ref clipSpaceTriangle.Vertices[i];
            ref var screenVertex = ref screenSpaceTriangle.Vertices[i];

            float w = Math.Abs(vertex.Position.W) < 0.00001f ? 0.00001f : vertex.Position.W;
            float invW = 1.0f / w;

            screenVertex.ScreenPosition.X = (vertex.Position.X * invW + 1.0f) * s_halfWidth;
            screenVertex.ScreenPosition.Y = (-vertex.Position.Y * invW + 1.0f) * s_halfHeight;
            screenVertex.ScreenPosition.Z = vertex.Position.Z * invW;
            screenVertex.Varyings = vertex.Varyings; // Reference copy, not deep copy
        }
    }

    private void ProcessTriangle(int a, int b, int c)
    {
        var tmp = tempTriangle.Value;

        // Vertex shader calls
        var output0 = CurrentShader.VertexShader(a);
        if (output0.Varyings == null) return; // Early exit

        var output1 = CurrentShader.VertexShader(b);
        if (output1.Varyings?.Length != output0.Varyings.Length) return;

        var output2 = CurrentShader.VertexShader(c);
        if (output2.Varyings?.Length != output0.Varyings.Length) return;

        // Reuse existing vertex objects
        tmp.Vertices[0].Position = output0.GlPosition;
        tmp.Vertices[0].Varyings = output0.Varyings;
        tmp.Vertices[1].Position = output1.GlPosition;
        tmp.Vertices[1].Varyings = output1.Varyings;
        tmp.Vertices[2].Position = output2.GlPosition;
        tmp.Vertices[2].Varyings = output2.Varyings;

        // Quick rejection test
        if (QuickTriangleReject(tmp))
            return;

        // Skip clipping if triangle is fully inside view frustum
        var ssTriangle = _screenSpaceTriangle.Value;
        //if (IsTriangleFullyInside(tmp))
        //{
        //    TransformToScreenSpaceFast(tmp, ssTriangle);
        //    curRasterizer.Rasterize(ssTriangle);
        //}
        //else
        //{
            var clippedTriangles = ClipTriangleAgainstPlanes(tmp);

            foreach (var tri in clippedTriangles)
            {
                TransformToScreenSpaceFast(tri, ssTriangle);
                curRasterizer.Rasterize(ssTriangle);
            }
        //}
    }

    #region CLipping Algorithm

    // Reusable arrays for clipping - prevents allocations
    private readonly ThreadLocal<RasterVertex[]> clipInputVertices = new(() => new RasterVertex[16]);
    private readonly ThreadLocal<RasterVertex[]> clipOutputVertices = new(() => new RasterVertex[16]);
    private readonly ThreadLocal<List<RasterTriangle>> clippedTriangles = new(() => new List<RasterTriangle>(8));

    private readonly PlaneFloat[] s_planes = new PlaneFloat[]
    {
        new PlaneFloat { Normal = new Float3(0, 0, 1), D = -0.1f },   // Near plane
        new PlaneFloat { Normal = new Float3(0, 0, -1), D = 100f }    // Far plane
    };

    private List<RasterTriangle> ClipTriangleAgainstPlanes(RasterTriangle triangle)
    {
        var inputVertices = clipInputVertices.Value;
        var outputVertices = clipOutputVertices.Value;
        var triangles = clippedTriangles.Value;


        triangles.Clear();

        // Fast path: check if clipping is actually needed
        bool needsClipping = false;
        foreach (var plane in s_planes)
        {
            for (int i = 0; i < 3; i++)
            {
                if (GetPlaneDistance(triangle.Vertices[i].Position, plane) < 0)
                {
                    needsClipping = true;
                    break;
                }
            }
            if (needsClipping) break;
        }

        if (!needsClipping)
        {
            triangles.Add(triangle);
            return triangles;
        }

        inputVertices[0] = triangle.Vertices[0];
        inputVertices[1] = triangle.Vertices[1];
        inputVertices[2] = triangle.Vertices[2];
        int vertexCount = 3;

        foreach (var plane in s_planes)
        {
            if (vertexCount == 0) break;

            vertexCount = ClipPolygonAgainstPlane(inputVertices, vertexCount, outputVertices, plane);

            var temp = inputVertices;
            clipInputVertices.Value = outputVertices;
            clipOutputVertices.Value = temp;
            inputVertices = clipInputVertices.Value;
            outputVertices = clipOutputVertices.Value;
        }

        if (vertexCount >= 3)
        {
            TriangulatePolygon(inputVertices, vertexCount, triangles);
        }

        return triangles;
    }

    private int ClipPolygonAgainstPlane(RasterVertex[] inputVertices, int inputCount,
                                       RasterVertex[] outputVertices, PlaneFloat plane)
    {
        if (inputCount == 0) return 0;

        int outputCount = 0;
        var previousVertex = inputVertices[inputCount - 1];
        float previousDistance = GetPlaneDistance(previousVertex.Position, plane);

        for (int i = 0; i < inputCount; i++)
        {
            var currentVertex = inputVertices[i];
            float currentDistance = GetPlaneDistance(currentVertex.Position, plane);

            if (currentDistance >= 0)
            {
                if (previousDistance < 0)
                {
                    float t = previousDistance / (previousDistance - currentDistance);
                    outputVertices[outputCount++] = InterpolateVertex(previousVertex, currentVertex, t);
                }

                outputVertices[outputCount++] = currentVertex;
            }
            else if (previousDistance >= 0)
            {
                float t = previousDistance / (previousDistance - currentDistance);
                outputVertices[outputCount++] = InterpolateVertex(previousVertex, currentVertex, t);
            }

            previousVertex = currentVertex;
            previousDistance = currentDistance;
        }

        return outputCount;
    }

    private float GetPlaneDistance(Float4 position, PlaneFloat plane)
    {
        return plane.Normal.X * position.X +
               plane.Normal.Y * position.Y +
               plane.Normal.Z * position.Z + plane.D;
    }

    private void TriangulatePolygon(RasterVertex[] vertices, int vertexCount, List<RasterTriangle> triangles)
    {
        for (int i = 1; i < vertexCount - 1; i++)
        {
            var triangle = new RasterTriangle
            {
                Vertices = new RasterVertex[3]
                {
                    vertices[0],
                    vertices[i],
                    vertices[i + 1]
                }
            };
            triangles.Add(triangle);
        }
    }

    private RasterVertex InterpolateVertex(RasterVertex v0, RasterVertex v1, float t)
    {
        var newPosition = Maths.Lerp(v0.Position, v1.Position, t);
        Float4[] newVaryings = new Float4[v0.Varyings.Length];
        for (int i = 0; i < v0.Varyings.Length; i++)
            newVaryings[i] = Maths.Lerp(v0.Varyings[i], v1.Varyings[i], t);

        return new RasterVertex
        {
            Position = newPosition,
            Varyings = newVaryings
        };
    }

    #endregion

    #endregion
}
