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

    private PointRasterizer pointRasterizer;
    private LineRasterizer lineRasterizer;
    private TriangleRasterizer triangleRasterizer;

    public GraphicsDevice(int width, int height)
    {
        backBuffer = new FrameBuffer(this, width, height);

        CurrentFramebuffer = backBuffer;

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
    }

    public void DrawVertexBuffer(VertexBuffer buffer)
    {
        if (CurrentShader == null)
            throw new Exception("No shader bound");

        CurrentShader.BindVertexAttributes(buffer.VertexAttributes);

        for (var i = 0; i < buffer.Indices.Length; i += 3)
        {
            ProcessTriangle(buffer.Indices[i], buffer.Indices[i + 1], buffer.Indices[i + 2]);
        }
    }

    #region Triangle Rasterization

    // a temporary triangle used for processing
    private readonly RasterTriangle tempTriangle = new RasterTriangle() { Vertices = new RasterVertex[3] };

    private void ProcessTriangle(int a, int b, int c)
    {
        // Apply the Vertex shader and construct the triangle
        CurrentShader.SetVertexAttributes(a);
        var output = CurrentShader.VertexShader();
        if(output.Varyings == null) throw new Exception("Vertex shader did not output varyingsa, Cannot be null, Needs to atleast be an empty array.");
        int varyingCount = output.Varyings.Length;
        tempTriangle.Vertices[0] = new() { Position = output.GlPosition, Varyings = output.Varyings };

        CurrentShader.SetVertexAttributes(b);
        output = CurrentShader.VertexShader();
        if (output.Varyings == null) throw new Exception("Vertex shader did not output varyingsa, Cannot be null, Needs to atleast be an empty array.");
        if (output.Varyings.Length != varyingCount) throw new Exception("Vertex shader did not output the same number of varyings as the first vertex.");
        tempTriangle.Vertices[1] = new() { Position = output.GlPosition, Varyings = output.Varyings };

        CurrentShader.SetVertexAttributes(c);
        output = CurrentShader.VertexShader();
        if (output.Varyings == null) throw new Exception("Vertex shader did not output varyingsa, Cannot be null, Needs to atleast be an empty array.");
        if (output.Varyings.Length != varyingCount) throw new Exception("Vertex shader did not output the same number of varyings as the first vertex.");
        tempTriangle.Vertices[2] = new() { Position = output.GlPosition, Varyings = output.Varyings };


        RasterizerBase rasterizer = PolygonMode switch
        {
            PolygonMode.Point => pointRasterizer,
            PolygonMode.Line => lineRasterizer,
            PolygonMode.Fill => triangleRasterizer,
            _ => throw new Exception("Invalid polygon mode")
        };

        var nearPlane = new Plane { Normal = new Float3(0, 0, 1), Distance = -0.1f };
        var farPlane = new Plane { Normal = new Float3(0, 0, -1), Distance = 100f };

        var clippedTriangles = ClipTriangleAgainstPlane(tempTriangle, nearPlane, true)
                               .SelectMany(tri => ClipTriangleAgainstPlane(tri, farPlane, false));
        foreach (var tri in clippedTriangles)
        {
            var processedTri = new RasterTriangle
            {
                Vertices = tri.Vertices.Select(v =>
                {
                    float w = v.Position.W;
                    return new RasterVertex
                    {
                        ScreenPosition = new Float3(
                            (v.Position.X / w + 1) * CurrentFramebuffer.Width / 2,
                            (-v.Position.Y / w + 1) * CurrentFramebuffer.Height / 2,
                            v.Position.Z / w
                        ),
                        Varyings = v.Varyings
                    };
                }).ToArray()
            };

            rasterizer.Rasterize(processedTri);
        }
    }

    private List<RasterTriangle> ClipTriangleAgainstPlane(RasterTriangle triangle, Plane plane, bool isNearPlane)
    {
        var insideVertices = new List<RasterVertex>(3);
        var outsideVertices = new List<RasterVertex>(3);
        var insideIndices = new List<int>(3);

        for (int i = 0; i < 3; i++)
        {
            float distance = Maths.Dot(plane.Normal, new Float3(triangle.Vertices[i].Position.X, triangle.Vertices[i].Position.Y, triangle.Vertices[i].Position.Z)) + plane.Distance;
            if (distance >= 0)
            {
                insideVertices.Add(triangle.Vertices[i]);
                insideIndices.Add(i);
            }
            else
            {
                outsideVertices.Add(triangle.Vertices[i]);
            }
        }

        if (insideVertices.Count == 0)
            return [];
        if (insideVertices.Count == 3)
            return [triangle];

        var newTriangles = new List<RasterTriangle>();

        if (insideVertices.Count == 1)
        {
            var v0 = insideVertices[0];
            var v1 = outsideVertices[0];
            var v2 = outsideVertices[1];
            float t1 = IntersectLinePlane(v0.Position, v1.Position, plane);
            float t2 = IntersectLinePlane(v0.Position, v2.Position, plane);
            var newVertex1 = InterpolateVertex(v0, v1, t1);
            var newVertex2 = InterpolateVertex(v0, v2, t2);

            if (insideIndices[0] == 0)
            {
                newTriangles.Add(new RasterTriangle { Vertices = new[] { v0, newVertex1, newVertex2 } });
            }
            else if (insideIndices[0] == 1)
            {
                newTriangles.Add(new RasterTriangle { Vertices = new[] { newVertex2, newVertex1, v0 } });
            }
            else
            {
                newTriangles.Add(new RasterTriangle { Vertices = new[] { newVertex1, newVertex2, v0 } });
            }
        }
        else
        {
            var v0 = insideVertices[0];
            var v1 = insideVertices[1];
            var v2 = outsideVertices[0];
            float t1 = IntersectLinePlane(v0.Position, v2.Position, plane);
            float t2 = IntersectLinePlane(v1.Position, v2.Position, plane);
            var newVertex1 = InterpolateVertex(v0, v2, t1);
            var newVertex2 = InterpolateVertex(v1, v2, t2);

            if (insideIndices[0] == 0 && insideIndices[1] == 1)
            {
                newTriangles.Add(new RasterTriangle { Vertices = new[] { v0, v1, newVertex2 } });
                newTriangles.Add(new RasterTriangle { Vertices = new[] { v0, newVertex2, newVertex1 } });
            }
            else if (insideIndices[0] == 1 && insideIndices[1] == 2)
            {
                newTriangles.Add(new RasterTriangle { Vertices = new[] { newVertex1, v0, v1 } });
                newTriangles.Add(new RasterTriangle { Vertices = new[] { newVertex1, v1, newVertex2 } });
            }
            else
            {
                newTriangles.Add(new RasterTriangle { Vertices = new[] { v1, v0, newVertex1 } });
                newTriangles.Add(new RasterTriangle { Vertices = new[] { v1, newVertex1, newVertex2 } });
            }
        }

        if (isNearPlane)
        {
            foreach (var tri in newTriangles)
            {
                for (int i = 0; i < tri.Vertices.Length; i++)
                {
                    if (tri.Vertices[i].Position.W < 0)
                    {
                        tri.Vertices[i].Position = new Float4(
                            tri.Vertices[i].Position.X,
                            tri.Vertices[i].Position.Y,
                            tri.Vertices[i].Position.Z,
                            0.00001f
                        );
                    }
                }
            }
        }

        return newTriangles;
    }

    private float IntersectLinePlane(Float4 p0, Float4 p1, Plane plane)
    {
        float d0 = Maths.Dot(plane.Normal, new Float3(p0.X, p0.Y, p0.Z)) + plane.Distance;
        float d1 = Maths.Dot(plane.Normal, new Float3(p1.X, p1.Y, p1.Z)) + plane.Distance;
        return d0 / (d0 - d1);
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
}
