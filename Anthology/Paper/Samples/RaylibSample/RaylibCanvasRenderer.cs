// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Raylib_cs;

using static Raylib_cs.Raylib;

namespace RaylibSample;

public class RaylibCanvasRenderer : ICanvasRenderer
{
    // Raylib uses its own vertex attribute names (vertexPosition, vertexTexCoord, vertexColor)
    // and doesn't support custom vertex attributes, so Slug rendering is not available.
    // Generated from Quill/Shaders/Canvas.slang and Blur.slang; see Shaders/CanvasShaders.generated.cs

    Shader shader;
    int scissorTransformLoc;
    int scissorTranslationLoc;
    int scissorExtLoc;

    int _brushTransformLoc;
    int _brushTranslationLoc;
    int _brushTypeLoc;
    int _brushColor1Loc;
    int _brushColor2Loc;
    int _brushParamsLoc;
    int _brushParams2Loc;
    int _textureTransformLoc;
    int _textureTranslationLoc;
    int _sdfPxRangeLoc;
    float _sdfPxRange = 4f;
    int _atlasTexelSizeLoc;

    // Backdrop blur
    public bool SupportsBackdropBlur => true;
    // Raylib render textures are stored bottom-up; the scene sample needs no extra flip.
    private const int BackdropFlipY = 0;
    int _backdropTexLoc;
    int _fontTextureLoc;
    int _viewportSizeLoc;
    int _backdropBlurAmountLoc;
    int _backdropFlipYLoc;
    Shader _blurDown;
    Shader _blurUp;
    int _downHalfpixelLoc, _downOffsetLoc;
    int _upHalfpixelLoc, _upOffsetLoc;
    const int MaxBlurLevels = 6;
    RenderTexture2D _sceneRT;
    RenderTexture2D[] _blurLevels = new RenderTexture2D[MaxBlurLevels];
    int _rtWidth, _rtHeight;
    bool _backdropDirty = true;
    float _lastBlurRadius = -1f;

    public RaylibCanvasRenderer()
    {
        // Load shader with scissoring support
        shader = LoadShaderFromMemory(CanvasShaders.Vertex, CanvasShaders.Fragment);
        scissorTransformLoc = GetShaderLocation(shader, "scissorTransform");
        scissorTranslationLoc = GetShaderLocation(shader, "scissorTranslation");
        scissorExtLoc = GetShaderLocation(shader, "scissorExt");

        _brushTransformLoc = GetShaderLocation(shader, "brushTransform");
        _brushTranslationLoc = GetShaderLocation(shader, "brushTranslation");
        _brushTypeLoc = GetShaderLocation(shader, "brushType");
        _brushColor1Loc = GetShaderLocation(shader, "brushColor1");
        _brushColor2Loc = GetShaderLocation(shader, "brushColor2");
        _brushParamsLoc = GetShaderLocation(shader, "brushParams");
        _brushParams2Loc = GetShaderLocation(shader, "brushParams2");
        _textureTransformLoc = GetShaderLocation(shader, "textureTransform");
        _textureTranslationLoc = GetShaderLocation(shader, "textureTranslation");
        _sdfPxRangeLoc = GetShaderLocation(shader, "sdfPxRange");

        _fontTextureLoc = GetShaderLocation(shader, "fontTexture");
        _backdropTexLoc = GetShaderLocation(shader, "backdropTexture");
        _viewportSizeLoc = GetShaderLocation(shader, "viewportSize");
        _backdropBlurAmountLoc = GetShaderLocation(shader, "backdropBlurAmount");
        _backdropFlipYLoc = GetShaderLocation(shader, "backdropFlipY");

        _atlasTexelSizeLoc = GetShaderLocation(shader, "atlasTexelSize");

        _blurDown = LoadShaderFromMemory(null, CanvasShaders.BlurDownsample);
        _downHalfpixelLoc = GetShaderLocation(_blurDown, "halfpixel");
        _downOffsetLoc = GetShaderLocation(_blurDown, "offset");
        _blurUp = LoadShaderFromMemory(null, CanvasShaders.BlurUpsample);
        _upHalfpixelLoc = GetShaderLocation(_blurUp, "halfpixel");
        _upOffsetLoc = GetShaderLocation(_blurUp, "offset");
    }

    private void EnsureTargets(int w, int h)
    {
        if (_sceneRT.Id != 0 && _rtWidth == w && _rtHeight == h)
            return;
        if (_sceneRT.Id != 0)
        {
            UnloadRenderTexture(_sceneRT);
            for (int i = 0; i < MaxBlurLevels; i++) UnloadRenderTexture(_blurLevels[i]);
        }
        _sceneRT = LoadRenderTexture(w, h);
        SetTextureFilter(_sceneRT.Texture, TextureFilter.Bilinear);
        for (int i = 0; i < MaxBlurLevels; i++)
        {
            int lw = Math.Max(1, w >> (i + 1));
            int lh = Math.Max(1, h >> (i + 1));
            _blurLevels[i] = LoadRenderTexture(lw, lh);
            SetTextureFilter(_blurLevels[i].Texture, TextureFilter.Bilinear);
        }
        _rtWidth = w;
        _rtHeight = h;
    }

    private static void ComputeBlurParams(float radius, out int iterations, out float offset)
    {
        float r = MathF.Max(radius, 2f);
        iterations = Math.Clamp((int)MathF.Floor(MathF.Log2(r)) - 1, 1, MaxBlurLevels - 1);
        offset = Math.Clamp(r / (1 << (iterations + 1)), 0.5f, 6f);
    }

    // Blurs the scene texture into _blurLevels[0] using dual Kawase. Each pass maps the full
    // source into the full destination so orientation stays consistent across the chain.
    private void RenderBackdropBlur(Texture2D sceneTex, float radius)
    {
        // Two frosted shapes in a row see the same scene behind them.
        if (!_backdropDirty && radius == _lastBlurRadius) return;
        _backdropDirty = false;
        _lastBlurRadius = radius;

        ComputeBlurParams(radius, out int iterations, out float offset);

        BlurPass(_blurDown, _downHalfpixelLoc, _downOffsetLoc, offset, sceneTex, _blurLevels[0]);
        for (int i = 0; i < iterations; i++)
            BlurPass(_blurDown, _downHalfpixelLoc, _downOffsetLoc, offset, _blurLevels[i].Texture, _blurLevels[i + 1]);
        for (int i = iterations; i > 0; i--)
            BlurPass(_blurUp, _upHalfpixelLoc, _upOffsetLoc, offset, _blurLevels[i].Texture, _blurLevels[i - 1]);
    }

    private void BlurPass(Shader sh, int halfpixelLoc, int offsetLoc, float offset, Texture2D src, RenderTexture2D dst)
    {
        BeginTextureMode(dst);
        BeginShaderMode(sh);
        SetShaderValue(sh, halfpixelLoc, new Float2(0.5f / src.Width, 0.5f / src.Height), ShaderUniformDataType.Vec2);
        SetShaderValue(sh, offsetLoc, offset, ShaderUniformDataType.Float);
        // Map the whole source into the whole destination (UVs run 0..1).
        DrawTexturePro(src,
            new Rectangle(0, 0, src.Width, src.Height),
            new Rectangle(0, 0, dst.Texture.Width, dst.Texture.Height),
            new System.Numerics.Vector2(0, 0), 0f, Raylib_cs.Color.White);
        EndShaderMode();
        EndTextureMode();
    }

    private void SetupCanvasProjection(int w, int h)
    {
        Rlgl.MatrixMode(MatrixMode.Projection);
        Rlgl.LoadIdentity();
        Rlgl.Ortho(0, w, h, 0, -1, 1);
        Rlgl.MatrixMode(MatrixMode.ModelView);
        Rlgl.LoadIdentity();
    }

    public object CreateTexture(uint width, uint height)
    {
        unsafe
        {
            var data = new byte[width * height * 4];
            fixed (byte* dataPtr = data)
            {
                Image image = new Image {
                    Data = (void*)dataPtr,
                    Width = (int)width,
                    Height = (int)height,
                    Format = PixelFormat.UncompressedR8G8B8A8,
                    Mipmaps = 1
                };
                var texture = Raylib_cs.Raylib.LoadTextureFromImage(image);
                Raylib_cs.Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
                return texture;
            }
        }
    }

    public Int2 GetTextureSize(object texture)
    {
        if (texture is not Texture2D tex)
            throw new ArgumentException("Texture must be of type Texture2D");
        return new Int2(tex.Width, tex.Height);
    }

    public void SetTextureData(object texture, IntRect bounds, byte[] data)
    {
        // Update the texture data with the provided byte array
        if (texture is not Texture2D tex)
            throw new ArgumentException("Texture must be of type Texture2D");

        Rectangle updateRect = new Rectangle(bounds.Min.X, bounds.Min.Y, bounds.Size.X, bounds.Size.Y);
        Raylib_cs.Raylib.UpdateTextureRec(tex, updateRect, data);
    }

    void SetUniforms(Prowl.Quill.DrawCall drawCall, float dpiScale)
    {
        // Bind the texture if available, otherwise use default
        uint textureToUse = 0;
        if (drawCall.Texture != null)
            textureToUse = ((Texture2D)drawCall.Texture).Id;

        Rlgl.SetTexture(textureToUse);

        // Scissor and brush transforms are 2D affines with the framebuffer scale already folded in,
        // so no matrix, no transpose and no dpi divide in the shader.
        drawCall.GetScissor(dpiScale, out var scissorXf, out var scissorT, out var extent);


        {

            SetShaderValue(shader, scissorTransformLoc, new Float4((float)scissorXf.X, (float)scissorXf.Y, (float)scissorXf.Z, (float)scissorXf.W), ShaderUniformDataType.Vec4);

            SetShaderValue(shader, scissorTranslationLoc, new Float2((float)scissorT.X, (float)scissorT.Y), ShaderUniformDataType.Vec2);

            SetShaderValue(shader, scissorExtLoc, [(float)extent.X, (float)extent.Y], ShaderUniformDataType.Vec2);

        }

        // Set gradient parameters
        SetShaderValue(shader, _brushTypeLoc, (int)drawCall.Brush.Type, ShaderUniformDataType.Int);
        if (drawCall.Brush.Type != BrushType.None)
        {
            drawCall.GetBrushTransform(dpiScale, out var brushXf, out var brushT);
            SetShaderValue(shader, _brushTransformLoc, new Float4((float)brushXf.X, (float)brushXf.Y, (float)brushXf.Z, (float)brushXf.W), ShaderUniformDataType.Vec4);
            SetShaderValue(shader, _brushTranslationLoc, new Float2((float)brushT.X, (float)brushT.Y), ShaderUniformDataType.Vec2);
            var brcol1 = (Prowl.Vector.Color)drawCall.Brush.Color1;
            var brcol2 = (Prowl.Vector.Color)drawCall.Brush.Color2;
            SetShaderValue(shader, _brushColor1Loc, brcol1, ShaderUniformDataType.Vec4);
            SetShaderValue(shader, _brushColor2Loc, brcol2, ShaderUniformDataType.Vec4);
            SetShaderValue(shader, _brushParamsLoc, new Float4((float)drawCall.Brush.Point1.X, (float)drawCall.Brush.Point1.Y, (float)drawCall.Brush.Point2.X, (float)drawCall.Brush.Point2.Y), ShaderUniformDataType.Vec4);
            SetShaderValue(shader, _brushParams2Loc, new Float2((float)drawCall.Brush.CornerRadii, (float)drawCall.Brush.Feather), ShaderUniformDataType.Vec2);
        }

        // Set texture transform parameters
        drawCall.GetTextureTransform(dpiScale, out var texXf, out var texT);
        SetShaderValue(shader, _textureTransformLoc, new Float4((float)texXf.X, (float)texXf.Y, (float)texXf.Z, (float)texXf.W), ShaderUniformDataType.Vec4);
        SetShaderValue(shader, _textureTranslationLoc, new Float2((float)texT.X, (float)texT.Y), ShaderUniformDataType.Vec2);
        SetShaderValue(shader, _sdfPxRangeLoc, _sdfPxRange, ShaderUniformDataType.Float);

        // Font atlas on its own sampler so text batches with shapes (text samples it).
        if (drawCall.FontAtlas is Texture2D fontTex)
        {
            SetShaderValueTexture(shader, _fontTextureLoc, fontTex);
            // Feeds the distance-field range; the generated shader takes this as a uniform.
            SetShaderValue(shader, _atlasTexelSizeLoc,
                new Float2(fontTex.Width > 0 ? 1f / fontTex.Width : 0f, fontTex.Height > 0 ? 1f / fontTex.Height : 0f),
                ShaderUniformDataType.Vec2);
        }

        // Backdrop blur uniforms
        float blurAmount = (float)drawCall.Brush.BackdropBlur;
        SetShaderValue(shader, _backdropBlurAmountLoc, blurAmount, ShaderUniformDataType.Float);
        if (blurAmount > 0f)
        {
            SetShaderValue(shader, _viewportSizeLoc, new Float2(_rtWidth, _rtHeight), ShaderUniformDataType.Vec2);
            SetShaderValue(shader, _backdropFlipYLoc, BackdropFlipY, ShaderUniformDataType.Int);
            SetShaderValueTexture(shader, _backdropTexLoc, _blurLevels[0].Texture);
        }
    }

    void SetCustomUniforms(Shader customShader, ShaderUniforms uniforms)
    {
        foreach (var kvp in uniforms.Values)
        {
            int loc = GetShaderLocation(customShader, kvp.Key);
            if (loc < 0) continue;

            switch (kvp.Value)
            {
                case float f:
                    SetShaderValue(customShader, loc, f, ShaderUniformDataType.Float);
                    break;
                case int i:
                    SetShaderValue(customShader, loc, i, ShaderUniformDataType.Int);
                    break;
                case Float2 v2:
                    SetShaderValue(customShader, loc, v2, ShaderUniformDataType.Vec2);
                    break;
                case Prowl.Vector.Float3 v3:
                    SetShaderValue(customShader, loc, v3, ShaderUniformDataType.Vec3);
                    break;
                case Float4 v4:
                    SetShaderValue(customShader, loc, v4, ShaderUniformDataType.Vec4);
                    break;
                case Float4x4 mat:
                    SetShaderValueMatrix(customShader, loc, mat);
                    break;
            }
        }
    }

    public void RenderCalls(Canvas canvas, IReadOnlyList<Prowl.Quill.DrawCall> drawCalls)
    {
        _sdfPxRange = canvas.Text.FontEngine.DistanceRange;
        _backdropDirty = true;

        int w = GetRenderWidth();
        int h = GetRenderHeight();

        // If any shape needs a backdrop blur, render the whole canvas into an offscreen scene
        // target so the blur passes can sample what has been drawn so far, then blit to screen.
        bool anyBlur = false;
        for (int i = 0; i < drawCalls.Count; i++)
            if (drawCalls[i].Brush.BackdropBlur > 0f) { anyBlur = true; break; }

        if (anyBlur)
        {
            EnsureTargets(w, h);
            BeginTextureMode(_sceneRT);
            ClearBackground(Raylib_cs.Color.Blank);
        }

        SetupCanvasProjection(w, h);
        BeginBlendMode(BlendMode.AlphaPremultiply);
        Rlgl.DrawRenderBatchActive();

        // Hoisted out of the per-triangle loop: reading these through the canvas properties on
        // every vertex lookup was the dominant cost of this backend.
        ReadOnlySpan<Vertex> vertices = canvas.Vertices;
        ReadOnlySpan<uint> indices = canvas.Indices;

        int index = 0;

        foreach (var drawCall in drawCalls)
        {
            // Backdrop blur: flush the scene drawn so far, blur it, then resume.
            if (anyBlur && drawCall.Brush.BackdropBlur > 0f)
            {
                Rlgl.DrawRenderBatchActive();
                EndTextureMode();
                RenderBackdropBlur(_sceneRT.Texture, (float)drawCall.Brush.BackdropBlur);
                BeginTextureMode(_sceneRT);
                SetupCanvasProjection(w, h);
                BeginBlendMode(BlendMode.AlphaPremultiply);
            }

            // Determine which shader to use
            bool useCustomShader = drawCall.Shader is Shader;
            Shader activeShader = useCustomShader ? (Shader)drawCall.Shader : shader;

            BeginShaderMode(activeShader);

            // Draw the vertices for this draw call
            Rlgl.Begin(DrawMode.Triangles);

            // Bind the texture if available, otherwise use default
            uint textureToUse = 0;
            if (drawCall.Texture != null)
                textureToUse = ((Texture2D)drawCall.Texture).Id;
            Rlgl.SetTexture(textureToUse);

            if (useCustomShader)
            {
                // Set user-provided uniforms for custom shader
                if (drawCall.ShaderUniforms != null)
                    SetCustomUniforms(activeShader, drawCall.ShaderUniforms);
            }
            else
            {
                // Set default uniforms
                SetUniforms(drawCall, (float)canvas.FramebufferScale);
            }

            for (int i = 0; i < drawCall.ElementCount; i += 3)
            {
                if (Rlgl.CheckRenderBatchLimit(3))
                {
                    Rlgl.Begin(DrawMode.Triangles);
                    Rlgl.SetTexture(textureToUse);
                    if (useCustomShader)
                    {
                        if (drawCall.ShaderUniforms != null)
                            SetCustomUniforms(activeShader, drawCall.ShaderUniforms);
                    }
                    else
                    {
                        SetUniforms(drawCall, (float)canvas.FramebufferScale);
                    }
                }

                var a = vertices[(int)indices[index]];
                var b = vertices[(int)indices[index + 1]];
                var c = vertices[(int)indices[index + 2]];

                Rlgl.Color4ub(a.r, a.g, a.b, a.a);
                Rlgl.TexCoord2f(a.u, a.v);
                Rlgl.Vertex2f(a.x, a.y);

                Rlgl.Color4ub(b.r, b.g, b.b, b.a);
                Rlgl.TexCoord2f(b.u, b.v);
                Rlgl.Vertex2f(b.x, b.y);

                Rlgl.Color4ub(c.r, c.g, c.b, c.a);
                Rlgl.TexCoord2f(c.u, c.v);
                Rlgl.Vertex2f(c.x, c.y);

                index += 3;
            }
            Rlgl.End();
            Rlgl.DrawRenderBatchActive();
            EndShaderMode();
        }
        Rlgl.SetTexture(0);

        if (anyBlur)
        {
            EndTextureMode();
            // Blit the scene target to the screen. Negative source height flips the
            // render texture upright (Raylib stores render textures bottom-up).
            BeginBlendMode(BlendMode.AlphaPremultiply);
            DrawTexturePro(_sceneRT.Texture,
                new Rectangle(0, 0, _sceneRT.Texture.Width, -_sceneRT.Texture.Height),
                new Rectangle(0, 0, w, h),
                new System.Numerics.Vector2(0, 0), 0f, Raylib_cs.Color.White);
            EndBlendMode();
        }
    }

    static System.Numerics.Vector4 ToVec4(System.Drawing.Color color) => new System.Numerics.Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    public void Dispose()
    {
        UnloadShader(shader);
        UnloadShader(_blurDown);
        UnloadShader(_blurUp);
        if (_sceneRT.Id != 0)
        {
            UnloadRenderTexture(_sceneRT);
            for (int i = 0; i < MaxBlurLevels; i++) UnloadRenderTexture(_blurLevels[i]);
        }
    }
}
