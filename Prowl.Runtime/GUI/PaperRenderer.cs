// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Quill;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.GUI;

public class PaperRenderer : ICanvasRenderer
{
    private GraphicsProgram _shaderProgram;
    private GraphicsVertexArray _vertexArrayObject;
    private GraphicsBuffer _vertexBuffer;
    private GraphicsBuffer _elementBuffer;
    private Texture2D _defaultTexture;

    private Float4x4 _projection;

    public void Initialize(int width, int height)
    {
        InitializeShaders();

        _vertexBuffer = Graphics.CreateBuffer<byte>(BufferType.VertexBuffer, Array.Empty<byte>(), true);
        _elementBuffer = Graphics.CreateBuffer<uint>(BufferType.ElementsBuffer, Array.Empty<uint>(), true);

        // Vertex format matches Quill's Vertex struct (44 bytes):
        //   0: Float2     position     (offset 0)
        //   1: Float2     UV           (offset 8)
        //   2: UByte4     color        (offset 16, normalized)
        //   3: Float4     slug band    (offset 20)
        //   4: Float2     slug glyph   (offset 36)
        var vertexFormat = new VertexFormat(
        [
            new((VertexFormat.VertexSemantic)0, VertexFormat.VertexType.Float, 2, 0),
            new((VertexFormat.VertexSemantic)1, VertexFormat.VertexType.Float, 2, 0),
            new((VertexFormat.VertexSemantic)2, VertexFormat.VertexType.UnsignedByte, 4, 0, true),
            new((VertexFormat.VertexSemantic)3, VertexFormat.VertexType.Float, 4, 0),
            new((VertexFormat.VertexSemantic)4, VertexFormat.VertexType.Float, 2, 0),
        ]);

        _vertexArrayObject = Graphics.CreateVertexArray(vertexFormat, _vertexBuffer, _elementBuffer);

        _defaultTexture = new Texture2D(1, 1);
        _defaultTexture.SetData(new Memory<byte>(new byte[] { 255, 255, 255, 255 }), 0, 0, 1, 1);

        UpdateProjection(width, height);
    }

    public void UpdateProjection(int width, int height)
    {
        _projection = Float4x4.CreateOrthoOffCenter(0, width, height, 0, -1, 1);
    }

    public void Cleanup()
    {
        _vertexBuffer?.Dispose();
        _elementBuffer?.Dispose();
        _vertexArrayObject?.Dispose();
        _shaderProgram?.Dispose();
        _defaultTexture?.Dispose();
    }

    private void InitializeShaders()
    {
        var shader = Shader.LoadDefault(DefaultShader.UI);
        if (shader.IsNotValid())
        {
            Debug.LogError("Failed to load UI shader.");
            return;
        }

        Rendering.Shaders.ShaderPass pass = shader.GetPass(0);
        if (!pass.TryGetVariantProgram(null, out _shaderProgram))
            Debug.LogError("Failed to compile UI shader.");
    }

    private static Float4 ToFloat4(Color32 color)
        => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    public object CreateTexture(uint width, uint height)
    {
        var tex = new Texture2D(width, height);

        Graphics.SetTextureFilters(tex.Handle, TextureMin.Linear, TextureMag.Linear);
        Graphics.SetWrapS(tex.Handle, TextureWrap.ClampToEdge);
        Graphics.SetWrapT(tex.Handle, TextureWrap.ClampToEdge);

        return tex;
    }

    public object? CreateFloatTexture(int width, int height, int components, float[] data)
    {
        var tex = new Texture2D((uint)width, (uint)height, false, TextureImageFormat.Float4);

        // Set nearest filtering and clamp-to-edge for data textures
        Graphics.SetTextureFilters(tex.Handle, TextureMin.Nearest, TextureMag.Nearest);
        Graphics.SetWrapS(tex.Handle, TextureWrap.ClampToEdge);
        Graphics.SetWrapT(tex.Handle, TextureWrap.ClampToEdge);

        // Expand 2-component data to RGBA (Quill slug textures use RG channels)
        float[] uploadData;
        if (components == 2)
        {
            uploadData = new float[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                uploadData[i * 4 + 0] = data[i * 2 + 0];
                uploadData[i * 4 + 1] = data[i * 2 + 1];
                // G and A stay 0
            }
        }
        else
        {
            uploadData = data;
        }

        tex.SetData<float>(new Memory<float>(uploadData));
        return tex;
    }

    public Int2 GetTextureSize(object texture)
    {
        if (texture is not Texture2D tex) throw new ArgumentException("Invalid texture type");
        return new Int2((int)tex.Width, (int)tex.Height);
    }

    public void SetTextureData(object texture, IntRect bounds, byte[] data)
    {
        if (texture is not Texture2D tex) throw new ArgumentException("Invalid texture type");
        tex.SetData(new Memory<byte>(data), bounds.Min.X, bounds.Min.Y, (uint)bounds.Size.X, (uint)bounds.Size.Y);
    }

    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        if (drawCalls.Count == 0) return;

        float dpiScale = canvas.FramebufferScale;

        var state = new RasterizerState
        {
            DepthTest = false,
            DoBlend = true,
            BlendSrc = RasterizerState.Blending.One,
            BlendDst = RasterizerState.Blending.OneMinusSrcAlpha,
            Blend = RasterizerState.BlendMode.Add,
            CullFace = RasterizerState.PolyFace.None,
        };

        using var cmd = Graphics.GetCommandBuffer("Paper UI");

        cmd.SetRasterState(state);
        cmd.SetShader(_shaderProgram);
        cmd.SetMatrix("projection", in _projection);
        cmd.SetFloat("dpiScale", dpiScale);

        // Upload raw Vertex data (44 bytes per vertex)
        if (canvas.Vertices.Count > 0)
        {
            var vertices = canvas.Vertices.ToArray();
            byte[] rawData = new byte[vertices.Length * Vertex.SizeInBytes];
            var handle = GCHandle.Alloc(vertices, GCHandleType.Pinned);
            try { Marshal.Copy(handle.AddrOfPinnedObject(), rawData, 0, rawData.Length); }
            finally { handle.Free(); }
            cmd.UpdateBuffer<byte>(_vertexBuffer, rawData);
        }

        if (canvas.Indices.Count > 0)
            cmd.UpdateBuffer<uint>(_elementBuffer, canvas.Indices.ToArray());

        int indexOffset = 0;
        foreach (DrawCall drawCall in drawCalls)
        {
            // Texture
            Texture2D texture = (drawCall.Texture as Texture2D) ?? _defaultTexture;
            cmd.SetTexture("texture0", texture);

            // Scissor
            drawCall.GetScissor(out Float4x4 scissor, out Float2 extent);
            cmd.SetMatrix("scissorMat", in scissor);
            cmd.SetVector("scissorExt", extent);

            // Brush
            Float4x4 brushMat = drawCall.Brush.BrushMatrix;
            cmd.SetMatrix("brushMat", in brushMat);
            cmd.SetInt("brushType", (int)drawCall.Brush.Type);
            cmd.SetVector("brushColor1", ToFloat4(drawCall.Brush.Color1));
            cmd.SetVector("brushColor2", ToFloat4(drawCall.Brush.Color2));
            cmd.SetVector("brushParams", new Float4(
                drawCall.Brush.Point1.X, drawCall.Brush.Point1.Y,
                drawCall.Brush.Point2.X, drawCall.Brush.Point2.Y));
            cmd.SetVector("brushParams2", new Float2(
                drawCall.Brush.CornerRadii, drawCall.Brush.Feather));
            Float4x4 brushTexMat = drawCall.Brush.TextureMatrix;
            cmd.SetMatrix("brushTextureMat", in brushTexMat);

            // Slug textures (GPU text rendering)
            if (drawCall.IsSlug)
            {
                if (drawCall.SlugCurveTexture is Texture2D curveTex)
                    cmd.SetTexture("slugCurveTexture", curveTex);
                if (drawCall.SlugBandTexture is Texture2D bandTex)
                    cmd.SetTexture("slugBandTexture", bandTex);

                cmd.SetVector("slugCurveTexSize",
                    new Float2(drawCall.SlugCurveTexWidth, drawCall.SlugCurveTexHeight));
                cmd.SetVector("slugBandTexSize",
                    new Float2(drawCall.SlugBandTexWidth, drawCall.SlugBandTexHeight));
            }

            cmd.DrawIndexed(_vertexArrayObject, Topology.Triangles, (uint)drawCall.ElementCount, (uint)indexOffset, 0, true);
            indexOffset += drawCall.ElementCount;
        }

        Graphics.Submit(cmd);
    }

    public void Dispose() => Cleanup();
}
