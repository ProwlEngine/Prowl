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
    private int _fbWidth;
    private int _fbHeight;

    // Backdrop blur (dual Kawase, shares the UI shader's BlurDown/BlurUp passes)
    public bool SupportsBackdropBlur => true;
    // If the frosted glass appears vertically mirrored, flip this to 0.
    private const int BackdropFlipY = 1;
    private const int BlurDownPass = 1;
    private const int BlurUpPass = 2;
    private const int MaxBlurLevels = 6;
    private Resources.Material _blurMat;
    private readonly List<RenderTexture> _tempBlurRTs = new();

    public void Initialize(int width, int height)
    {
        InitializeShaders();

        _vertexBuffer = Graphics.CreateBuffer<byte>(BufferType.VertexBuffer, Array.Empty<byte>(), true);
        _elementBuffer = Graphics.CreateBuffer<uint>(BufferType.ElementsBuffer, Array.Empty<uint>(), true);

        // Vertex format matches Quill's Vertex struct (20 bytes):
        //   0: Float2     position     (offset 0)
        //   1: Float2     UV           (offset 8)
        //   2: UByte4     color        (offset 16, normalized)
        var vertexFormat = new VertexFormat(
        [
            new((VertexFormat.VertexSemantic)0, VertexFormat.VertexType.Float, 2, 0),
            new((VertexFormat.VertexSemantic)1, VertexFormat.VertexType.Float, 2, 0),
            new((VertexFormat.VertexSemantic)2, VertexFormat.VertexType.UnsignedByte, 4, 0, true),
        ]);

        _vertexArrayObject = Graphics.CreateVertexArray(vertexFormat, _vertexBuffer, _elementBuffer);

        _defaultTexture = new Texture2D(1, 1);
        _defaultTexture.SetData(new Memory<byte>(new byte[] { 255, 255, 255, 255 }), 0, 0, 1, 1);

        UpdateProjection(width, height);
    }

    public void UpdateProjection(int width, int height)
    {
        _fbWidth = width;
        _fbHeight = height;
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

        _blurMat ??= new Resources.Material(Shader.LoadDefault(DefaultShader.UI));

        using var cmd = Graphics.GetCommandBuffer("Paper UI");

        cmd.SetRasterState(state);
        cmd.SetShader(_shaderProgram);
        cmd.SetMatrix("projection", in _projection);
        cmd.SetFloat("dpiScale", dpiScale);
        cmd.SetTexture("backdropTexture", _defaultTexture);

        // Upload raw Vertex data (20 bytes per vertex)
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
            // Backdrop blur: capture the framebuffer behind this shape, blur it, then restore
            // the UI render state so the shape composites over the blurred backdrop.
            float blurAmount = (float)drawCall.Brush.BackdropBlur;
            if (blurAmount > 0f)
            {
                RenderTexture blurred = RenderBackdropBlur(cmd, blurAmount);

                cmd.SetRenderTarget(null);
                cmd.SetViewport(0, 0, (uint)_fbWidth, (uint)_fbHeight);
                cmd.SetRasterState(state);
                cmd.SetShader(_shaderProgram);
                cmd.SetMatrix("projection", in _projection);
                cmd.SetFloat("dpiScale", dpiScale);
                cmd.SetTexture("backdropTexture", blurred.MainTexture);
                cmd.SetVector("viewportSize", new Float2(_fbWidth, _fbHeight));
                cmd.SetInt("backdropFlipY", BackdropFlipY);
            }
            cmd.SetFloat("backdropBlurAmount", blurAmount);

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

            cmd.DrawIndexed(_vertexArrayObject, Topology.Triangles, (uint)drawCall.ElementCount, (uint)indexOffset, 0, true);
            indexOffset += drawCall.ElementCount;
        }

        Graphics.Submit(cmd);

        // Release pooled blur targets now that the command buffer has been submitted.
        if (_tempBlurRTs.Count > 0)
        {
            foreach (RenderTexture rt in _tempBlurRTs)
                RenderTexture.ReleaseTemporaryRT(rt);
            _tempBlurRTs.Clear();
        }
    }

    /// <summary>
    /// Maps a pixel blur radius onto a number of dual Kawase iterations plus a continuous sample
    /// offset so the effective blur scales smoothly with radius even as the iteration count steps.
    /// </summary>
    private static void ComputeBlurParams(float radius, out int iterations, out float offset)
    {
        float r = MathF.Max(radius, 2f);
        iterations = Math.Clamp((int)MathF.Floor(MathF.Log2(r)) - 1, 1, MaxBlurLevels - 1);
        offset = Math.Clamp(r / (1 << (iterations + 1)), 0.5f, 6f);
    }

    /// <summary>
    /// Captures the current backbuffer into a half-res target and dual-Kawase blurs it, returning
    /// the blurred render texture (sampled by the UI shader's backdrop composite). Temporary
    /// targets are tracked and released after the command buffer is submitted.
    /// </summary>
    private RenderTexture RenderBackdropBlur(CommandBuffer cmd, float radius)
    {
        ComputeBlurParams(radius, out int iterations, out float offset);

        int w = Math.Max(1, _fbWidth / 2);
        int h = Math.Max(1, _fbHeight / 2);

        // Capture the backbuffer (read) into a half-res render texture (draw) via a linear blit.
        RenderTexture capture = RenderTexture.GetTemporaryRT(w, h, false, [TextureImageFormat.Color4b]);
        _tempBlurRTs.Add(capture);
        cmd.SetRenderTargets(capture.frameBuffer, null);
        cmd.BlitFramebuffer(0, 0, _fbWidth, _fbHeight, 0, 0, w, h, ClearFlags.Color, BlitFilter.Linear);

        _blurMat.SetFloat("_Offset", offset);

        var chain = new List<RenderTexture> { capture };
        RenderTexture current = capture;
        for (int i = 0; i < iterations; i++)
        {
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            RenderTexture down = RenderTexture.GetTemporaryRT(w, h, false, [TextureImageFormat.Color4b]);
            _tempBlurRTs.Add(down);
            cmd.Blit(current, down, _blurMat, BlurDownPass);
            chain.Add(down);
            current = down;
        }

        for (int i = chain.Count - 1; i > 0; i--)
            cmd.Blit(chain[i], chain[i - 1], _blurMat, BlurUpPass);

        return chain[0];
    }

    public void Dispose()
    {
        Cleanup();
        _blurMat?.Dispose();
        _blurMat = null;
    }
}
