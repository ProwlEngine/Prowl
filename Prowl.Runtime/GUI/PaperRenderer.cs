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
    // How far below the framebuffer the blur pyramid starts: 1 = half res, 2 = quarter. The UI
    // shader composites straight from the base level, so this also decides the resolution the
    // backdrop is sampled at. Quarter is four times cheaper across every pass and is imperceptible
    // above roughly an eight pixel radius, since detail finer than the blur is destroyed anyway.
    private const int BlurBaseShift = 2;

    private const int MaxBlurLevels = 6;
    private static readonly TextureImageFormat[] BlurFormat = [TextureImageFormat.Color4b];
    private Resources.Material _blurMat;

    // The blur targets live as long as the renderer and are only resized when the framebuffer is.
    // The capture is half the framebuffer, and each pyramid level halves again from a quarter.
    private RenderTexture? _blurCapture;
    private readonly RenderTexture?[] _blurLevels = new RenderTexture?[MaxBlurLevels];
    private int _blurTargetWidth;
    private int _blurTargetHeight;

    // Whether anything has been drawn since the pyramid was last built, and the radius it was built
    // for. A frosted draw with neither changed can reuse the blur it already has.
    private bool _backdropDirty = true;
    private float _lastBlurRadius = -1f;

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
        if (_defaultTexture.IsValid()) _defaultTexture.Dispose();
        ReleaseBlurTargets();
    }

    private void ReleaseBlurTargets()
    {
        if (_blurCapture.IsValid()) _blurCapture.Dispose();
        _blurCapture = null;
        for (int i = 0; i < MaxBlurLevels; i++)
        {
            if (_blurLevels[i].IsValid()) _blurLevels[i]!.Dispose();
            _blurLevels[i] = null;
        }

        _blurTargetWidth = _blurTargetHeight = 0;
        _lastBlurRadius = -1f;
    }

    private void EnsureBlurTargets()
    {
        if (_blurCapture.IsValid() && _blurTargetWidth == _fbWidth && _blurTargetHeight == _fbHeight)
            return;

        _blurCapture = Resize(_blurCapture, _fbWidth >> 1, _fbHeight >> 1);
        for (int i = 0; i < MaxBlurLevels; i++)
            _blurLevels[i] = Resize(_blurLevels[i], _fbWidth >> (i + BlurBaseShift), _fbHeight >> (i + BlurBaseShift));

        _blurTargetWidth = _fbWidth;
        _blurTargetHeight = _fbHeight;
        _lastBlurRadius = -1f;

        static RenderTexture Resize(RenderTexture? target, int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (target.IsNotValid()) return new RenderTexture(width, height, false, BlurFormat);
            target!.Configure(width, height, false, BlurFormat);
            return target;
        }
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

        float fbScale = canvas.FramebufferScale;

        var state = new RasterizerState
        {
            DepthTest = false,
            DoBlend = true,
            BlendSrc = RasterizerState.Blending.One,
            BlendDst = RasterizerState.Blending.OneMinusSrcAlpha,
            Blend = RasterizerState.BlendMode.Add,
            CullFace = RasterizerState.PolyFace.None,
        };

        if (_blurMat.IsNotValid()) _blurMat = new Resources.Material(Shader.LoadDefault(DefaultShader.UI));

        using var cmd = Graphics.GetCommandBuffer("Paper UI");

        cmd.SetRasterState(state);
        cmd.SetShader(_shaderProgram);
        cmd.SetMatrix("projection", in _projection);
        cmd.SetTexture("backdropTexture", _defaultTexture);

        // Upload raw Vertex data (20 bytes per vertex). The canvas hands out its backing store
        // directly, so this reinterprets it in place rather than copying the geometry out twice.
        if (canvas.VertexCount > 0)
            cmd.UpdateBuffer<byte>(_vertexBuffer, MemoryMarshal.AsBytes(canvas.Vertices));

        if (canvas.IndexCount > 0)
            cmd.UpdateBuffer<uint>(_elementBuffer, canvas.Indices);

        // A new frame means the framebuffer behind any frosted shape has changed.
        _backdropDirty = true;

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
                cmd.SetTexture("backdropTexture", blurred.MainTexture);
                cmd.SetVector("viewportSize", new Float2(_fbWidth, _fbHeight));
                cmd.SetInt("backdropFlipY", BackdropFlipY);
            }
            cmd.SetFloat("backdropBlurAmount", blurAmount);

            // Texture. The brush/shape texture goes on texture0; the font atlas is bound separately
            // as a persistent sampler so text batches into the same draw call as surrounding shapes.
            Texture2D? texture = new AssetRef<Texture2D>(drawCall.Texture as Texture2D).Res;
            cmd.SetTexture("texture0", texture.IsValid() ? texture : _defaultTexture);

            Texture2D? fontTexture = new AssetRef<Texture2D>(drawCall.FontAtlas as Texture2D).Res;
            cmd.SetTexture("fontTexture", fontTexture.IsValid() ? fontTexture : _defaultTexture);

            // Font atlas metrics, so the text distance field resolves at any zoom.
            Int2 atlasSize = fontTexture.IsValid() ? new Int2((int)fontTexture.Width, (int)fontTexture.Height) : new Int2(1, 1);
            cmd.SetVector("atlasTexelSize", new Float2(
                atlasSize.X > 0 ? 1f / atlasSize.X : 0f,
                atlasSize.Y > 0 ? 1f / atlasSize.Y : 0f));
            cmd.SetFloat("sdfPxRange", canvas.Text.FontEngine.DistanceRange);

            // Scissor and brush transforms are 2D affines with the framebuffer scale already folded
            // in, so the shader needs neither a matrix nor a dpi divide.
            drawCall.GetScissor(fbScale, out Float4 scissorXf, out Float2 scissorT, out Float2 extent);
            cmd.SetVector("scissorTransform", scissorXf);
            cmd.SetVector("scissorTranslation", scissorT);
            cmd.SetVector("scissorExt", extent);

            // Brush
            drawCall.GetBrushTransform(fbScale, out Float4 brushXf, out Float2 brushT);
            cmd.SetVector("brushTransform", brushXf);
            cmd.SetVector("brushTranslation", brushT);
            cmd.SetInt("brushType", (int)drawCall.Brush.Type);
            cmd.SetVector("brushColor1", ToFloat4(drawCall.Brush.Color1));
            cmd.SetVector("brushColor2", ToFloat4(drawCall.Brush.Color2));
            cmd.SetVector("brushParams", new Float4(
                drawCall.Brush.Point1.X, drawCall.Brush.Point1.Y,
                drawCall.Brush.Point2.X, drawCall.Brush.Point2.Y));
            cmd.SetVector("brushParams2", new Float2(
                drawCall.Brush.CornerRadii, drawCall.Brush.Feather));
            drawCall.GetTextureTransform(fbScale, out Float4 texXf, out Float2 texT);
            cmd.SetVector("textureTransform", texXf);
            cmd.SetVector("textureTranslation", texT);

            cmd.DrawIndexed(_vertexArrayObject, Topology.Triangles, (uint)drawCall.ElementCount, (uint)indexOffset, 0, true);
            indexOffset += drawCall.ElementCount;
            if (drawCall.ElementCount > 0) _backdropDirty = true;
        }

        Graphics.Submit(cmd);
    }

    /// <summary>
    /// Maps a pixel blur radius onto a number of dual Kawase iterations plus a continuous sample
    /// offset so the effective blur scales smoothly with radius even as the iteration count steps.
    /// </summary>
    private static void ComputeBlurParams(float radius, out int iterations, out float offset)
    {
        // radius is in screen pixels, but the pyramid maths below works in base-level texels, and one
        // of those spans 1 << BlurBaseShift pixels. Converting here is what makes a 22 pixel blur
        // actually mean 22 pixels regardless of what resolution the pyramid starts at.
        float r = MathF.Max(radius / (1 << BlurBaseShift), 2f);
        iterations = Math.Clamp((int)MathF.Floor(MathF.Log2(r)) - 1, 1, MaxBlurLevels - 1);
        offset = Math.Clamp(r / (1 << (iterations + 1)), 0.5f, 6f);
    }

    /// <summary>
    /// Captures the backbuffer and dual Kawase blurs it, returning the pyramid's base level for the
    /// UI shader's backdrop composite. When nothing has been drawn since the last blur and the radius
    /// is unchanged, the existing result is returned without redoing any of it.
    /// </summary>
    private RenderTexture RenderBackdropBlur(CommandBuffer cmd, float radius)
    {
        EnsureBlurTargets();
        RenderTexture baseLevel = _blurLevels[0]!;
        if (!_backdropDirty && radius == _lastBlurRadius)
            return baseLevel;

        _backdropDirty = false;
        _lastBlurRadius = radius;
        ComputeBlurParams(radius, out int iterations, out float offset);

        // A linear blit at exactly half size averages each two by two block, so the capture is a
        // proper box filter. Blitting straight to quarter size would skip most pixels and shimmer
        // as things move behind the glass.
        RenderTexture capture = _blurCapture!;
        cmd.SetRenderTargets(capture.frameBuffer, null);
        cmd.BlitFramebuffer(0, 0, _fbWidth, _fbHeight, 0, 0, capture.Width, capture.Height, ClearFlags.Color, BlitFilter.Linear);

        // The step down to the base level only filters, so it uses the tightest spread and leaves
        // the radius to the passes below it.
        _blurMat.SetFloat("_Offset", 1f);
        cmd.Blit(capture, baseLevel, _blurMat, BlurDownPass);

        _blurMat.SetFloat("_Offset", offset);
        for (int i = 0; i < iterations; i++)
            cmd.Blit(_blurLevels[i], _blurLevels[i + 1], _blurMat, BlurDownPass);

        for (int i = iterations; i > 0; i--)
            cmd.Blit(_blurLevels[i], _blurLevels[i - 1], _blurMat, BlurUpPass);

        return baseLevel;
    }

    public void Dispose()
    {
        Cleanup();
        if (_blurMat.IsValid()) _blurMat.Dispose();
        _blurMat = null;
    }
}
