// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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

    public GraphicsDevice(int width, int height)
    {
        backBuffer = new FrameBuffer(this, width, height);

        CurrentFramebuffer = backBuffer;
        s_halfWidth = CurrentFramebuffer.Width * 0.5f;
        s_halfHeight = CurrentFramebuffer.Height * 0.5f;

        pointRasterizer = new(this);
        lineRasterizer = new(this);
        triangleRasterizer = new(this);
    }

    public FrameBuffer BackBuffer => backBuffer;

    public void SetPolygonMode(PolygonMode polygonMode) => PolygonMode = polygonMode;
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

        for (var i = 0; i < buffer.Indices.Length; i += 3)
        {
            ProcessTriangle(buffer.Indices[i], buffer.Indices[i + 1], buffer.Indices[i + 2]);
        }
    }

    #region Triangle Rasterization

    // a temporary triangle used for processing
    private readonly RasterTriangle tempTriangle = new RasterTriangle() { Vertices = new RasterVertex[3] };
    private readonly RasterTriangle _screenSpaceTriangle = new RasterTriangle { Vertices = new RasterVertex[3] };

    private bool QuickTriangleReject(RasterTriangle triangle)
    {
        // Check if all vertices are on the wrong side of near/far planes
        const float nearPlane = -0.1f;
        const float farPlane = 100f;

        bool allBehindNear = true;
        bool allBehindFar = true;

        for (int i = 0; i < 3; i++)
        {
            float z = triangle.Vertices[i].Position.Z;
            if (z >= nearPlane) allBehindNear = false;
            if (z <= farPlane) allBehindFar = false;
        }

        return allBehindNear || allBehindFar;
    }

    private void ProcessTriangle(int a, int b, int c)
    {
        // Apply the Vertex shader and construct the triangle
        //CurrentShader.SetCurrentVertex(a);
        var output = CurrentShader.VertexShader(a);
        if(output.Varyings == null) throw new Exception("Vertex shader did not output varyingsa, Cannot be null, Needs to atleast be an empty array.");
        int varyingCount = output.Varyings.Length;
        tempTriangle.Vertices[0] = new() { Position = output.GlPosition, Varyings = output.Varyings };

        //CurrentShader.SetCurrentVertex(b);
        output = CurrentShader.VertexShader(b);
        if (output.Varyings == null) throw new Exception("Vertex shader did not output varyingsa, Cannot be null, Needs to atleast be an empty array.");
        if (output.Varyings.Length != varyingCount) throw new Exception("Vertex shader did not output the same number of varyings as the first vertex.");
        tempTriangle.Vertices[1] = new() { Position = output.GlPosition, Varyings = output.Varyings };

        //CurrentShader.SetCurrentVertex(c);
        output = CurrentShader.VertexShader(c);
        if (output.Varyings == null) throw new Exception("Vertex shader did not output varyingsa, Cannot be null, Needs to atleast be an empty array.");
        if (output.Varyings.Length != varyingCount) throw new Exception("Vertex shader did not output the same number of varyings as the first vertex.");
        tempTriangle.Vertices[2] = new() { Position = output.GlPosition, Varyings = output.Varyings };

        // Quick rejection test
        if (QuickTriangleReject(tempTriangle))
            return;

        RasterizerBase rasterizer = PolygonMode switch
        {
            PolygonMode.Point => pointRasterizer,
            PolygonMode.Line => lineRasterizer,
            PolygonMode.Fill => triangleRasterizer,
            _ => throw new Exception("Invalid polygon mode")
        };

        var nearPlane = new PlaneFloat { Normal = new Float3(0, 0, 1), D = -0.1f };
        var farPlane = new PlaneFloat { Normal = new Float3(0, 0, -1), D = 100f };

        var clippedTriangles = ClipTriangleAgainstPlanes(tempTriangle);

        foreach (var tri in clippedTriangles)
        {
            for (int i = 0; i < 3; i++)
            {
                ref var vertex = ref tri.Vertices[i];
                float w = vertex.Position.W;
                if (Math.Abs(w) < 0.00001f) w = 0.00001f;

                float invW = 1.0f / w;

                _screenSpaceTriangle.Vertices[i] = new RasterVertex
                {
                    ScreenPosition = new Float3(
                        (vertex.Position.X * invW + 1.0f) * s_halfWidth,
                        (-vertex.Position.Y * invW + 1.0f) * s_halfHeight,
                        vertex.Position.Z * invW
                    ),
                    Varyings = vertex.Varyings
                };
            }

            rasterizer.Rasterize(_screenSpaceTriangle);
        }
    }

    #region CLipping Algorithm

    // Reusable arrays for clipping - prevents allocations
    private RasterVertex[] _clipInputVertices = new RasterVertex[16];
    private RasterVertex[] _clipOutputVertices = new RasterVertex[16];
    private readonly List<RasterTriangle> _clippedTriangles = new List<RasterTriangle>(8);

    // Define clipping planes (near and far)
    private readonly PlaneFloat[] s_planes = new PlaneFloat[]
    {
            new PlaneFloat { Normal = new Float3(0, 0, 1), D = -0.1f },   // Near plane
            new PlaneFloat { Normal = new Float3(0, 0, -1), D = 100f }    // Far plane
    };

    private List<RasterTriangle> ClipTriangleAgainstPlanes(RasterTriangle triangle)
    {
        _clippedTriangles.Clear();

        // Start with the original triangle vertices
        _clipInputVertices[0] = triangle.Vertices[0];
        _clipInputVertices[1] = triangle.Vertices[1];
        _clipInputVertices[2] = triangle.Vertices[2];
        int vertexCount = 3;

        // Clip against each plane sequentially (Sutherland-Hodgman)
        foreach (var plane in s_planes)
        {
            if (vertexCount == 0) break;

            vertexCount = ClipPolygonAgainstPlane(_clipInputVertices, vertexCount, _clipOutputVertices, plane);

            // Swap input/output arrays for next iteration
            var temp = _clipInputVertices;
            _clipInputVertices = _clipOutputVertices;
            _clipOutputVertices = temp;
        }

        // Convert the clipped polygon back to triangles using fan triangulation
        if (vertexCount >= 3)
        {
            TriangulatePolygon(_clipInputVertices, vertexCount, _clippedTriangles);
        }

        return _clippedTriangles;
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

            // Current vertex is inside
            if (currentDistance >= 0)
            {
                // Previous vertex was outside, so we need intersection
                if (previousDistance < 0)
                {
                    float t = previousDistance / (previousDistance - currentDistance);
                    outputVertices[outputCount++] = InterpolateVertex(previousVertex, currentVertex, t);
                }

                // Add current vertex
                outputVertices[outputCount++] = currentVertex;
            }
            // Current vertex is outside, previous was inside
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
        // Fan triangulation: connect vertex 0 to all other adjacent pairs
        for (int i = 1; i < vertexCount - 1; i++)
        {
            var triangle = new RasterTriangle
            {
                Vertices = new RasterVertex[3]
                {
                    vertices[0],     // Fan center
                    vertices[i],     // Current vertex
                    vertices[i + 1]  // Next vertex
                }
            };
            triangles.Add(triangle);
        }
    }

    #endregion

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
}
