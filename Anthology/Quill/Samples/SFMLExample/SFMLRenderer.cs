using Prowl.Quill;
using Prowl.Vector;
using SFML.Graphics;
using SFML.Graphics.Glsl;
using SFML.System;
using Color = System.Drawing.Color;
using IntRect = Prowl.Vector.IntRect;

namespace SFMLExample
{
    /// <summary>
    /// Handles all SFML rendering logic for the vector graphics canvas
    /// </summary>
    public class SFMLRenderer : ICanvasRenderer, IDisposable
    {
        private RenderWindow _window;
        private Shader _shader;
        private Texture _defaultTexture;
        private VertexArray _vertexArray;
        private VertexBuffer _vertexBuffer;
        private View _projection;

        // Generated from Quill/Shaders/Canvas.slang and Blur.slang; see Shaders/CanvasShaders.generated.cs


        // Backdrop blur
        public bool SupportsBackdropBlur => true;
        // If the frosted glass appears vertically mirrored, flip this to 0.
        private const int BackdropFlipY = 1;
        private const int MaxBlurLevels = 6;
        private Shader _blurDown;
        private Shader _blurUp;
        private Texture _captureTex;
        private RenderTexture[] _blurLevels = new RenderTexture[MaxBlurLevels];
        private bool _backdropDirty = true;
        private float _lastBlurRadius = -1f;
        private int _fbWidth;
        private int _fbHeight;

        /// <summary>
        /// Initialize the renderer with the window dimensions
        /// </summary>
        public void Initialize(int width, int height, TextureSFML defaultTexture)
        {
            // Set the default texture
            _defaultTexture = defaultTexture.Handle;
            
            // Create vertex buffers
            _vertexArray = new VertexArray(PrimitiveType.Triangles);
            
            // Initialize shader if SFML supports shaders
            if (Shader.IsAvailable)
            {
                _shader = Shader.FromString(CanvasShaders.Vertex, null, CanvasShaders.Fragment);
                _shader.SetUniform("texture0", Shader.CurrentTexture);

                _blurDown = Shader.FromString(null, null, CanvasShaders.BlurDownsample);
                _blurUp = Shader.FromString(null, null, CanvasShaders.BlurUpsample);
            }

            UpdateProjection(width, height);
        }

        private void EnsureBlurTargets(int w, int h)
        {
            if (_captureTex != null && _fbWidth == w && _fbHeight == h && _blurLevels[0] != null)
                return;
            _captureTex?.Dispose();
            for (int i = 0; i < MaxBlurLevels; i++) _blurLevels[i]?.Dispose();

            _captureTex = new Texture((uint)w, (uint)h) { Smooth = true };
            for (int i = 0; i < MaxBlurLevels; i++)
            {
                int lw = Math.Max(1, w >> (i + 1));
                int lh = Math.Max(1, h >> (i + 1));
                _blurLevels[i] = new RenderTexture((uint)lw, (uint)lh) { Smooth = true };
            }
        }

        private static void ComputeBlurParams(float radius, out int iterations, out float offset)
        {
            float r = MathF.Max(radius, 2f);
            iterations = Math.Clamp((int)MathF.Floor(MathF.Log2(r)) - 1, 1, MaxBlurLevels - 1);
            offset = Math.Clamp(r / (1 << (iterations + 1)), 0.5f, 6f);
        }

        // Blurs the captured scene into _blurLevels[0] using dual Kawase. RenderTexture sources are
        // flipped vertically when sampled, so we flip their sprite rect to keep orientation uniform.
        private void RenderBackdropBlur(float radius)
        {
            ComputeBlurParams(radius, out int iterations, out float offset);

            BlurPass(_blurDown, _blurLevels[0], _captureTex, false, offset);
            for (int i = 0; i < iterations; i++)
                BlurPass(_blurDown, _blurLevels[i + 1], _blurLevels[i].Texture, true, offset);
            for (int i = iterations; i > 0; i--)
                BlurPass(_blurUp, _blurLevels[i - 1], _blurLevels[i].Texture, true, offset);
        }

        private void BlurPass(Shader sh, RenderTexture dst, Texture src, bool srcIsRenderTexture, float offset)
        {
            var sprite = new Sprite(src);
            sprite.Scale = new Vector2f(dst.Size.X / (float)src.Size.X, dst.Size.Y / (float)src.Size.Y);
            // RenderTexture contents are stored upside down; flip the source rect to present it upright.
            if (srcIsRenderTexture)
                sprite.TextureRect = new SFML.Graphics.IntRect(0, (int)src.Size.Y, (int)src.Size.X, -(int)src.Size.Y);

            sh.SetUniform("texture", Shader.CurrentTexture);
            sh.SetUniform("halfpixel", new Vec2(0.5f / src.Size.X, 0.5f / src.Size.Y));
            sh.SetUniform("offset", offset);

            dst.Clear(new SFML.Graphics.Color(0, 0, 0, 0));
            dst.Draw(sprite, new RenderStates(BlendMode.None, Transform.Identity, src, sh));
            dst.Display();
            sprite.Dispose();
        }

        /// <summary>
        /// Update the projection matrix when the window is resized
        /// </summary>
        public void UpdateProjection(int width, int height)
        {
            _fbWidth = width;
            _fbHeight = height;
            _projection = new View(new FloatRect(0, 0, width, height));
            
            if (Shader.IsAvailable)
            {
                // Create and set orthographic projection matrix
                Mat4 projMat = new(
                    2.0f/width, 0, 0, -1,
                    0, -2.0f/height, 0, 1,
                    0, 0, 1, 0,
                    0, 0, 0, 1
                );
                _shader.SetUniform("projection", projMat);
            }
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Cleanup()
        {
            ((ICanvasRenderer)this).Dispose();
        }

        public object CreateTexture(uint width, uint height)
        {
            return TextureSFML.CreateNew(width, height);
        }

        public Int2 GetTextureSize(object texture)
        {
            if (texture is not TextureSFML sfmlTexture)
                throw new ArgumentException("Invalid texture type");

            return new Int2((int)sfmlTexture.Width, (int)sfmlTexture.Height);
        }

        public void SetTextureData(object texture, IntRect bounds, byte[] data)
        {
            if (texture is not TextureSFML sfmlTexture)
                throw new ArgumentException("Invalid texture type");
            
            sfmlTexture.SetData(bounds, data);
        }

        public void SetRenderWindow(RenderWindow window)
        {
            _window = window;
        }

        private static Vec4 ToVec4(Color color)
        {
            return new Vec4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }

        private static Mat4 ToMat4(Float4x4 mat)
        {
            return new Mat4(
                mat[0, 0], mat[0, 1], mat[0, 2], mat[0, 3],
                mat[1, 0], mat[1, 1], mat[1, 2], mat[1, 3],
                mat[2, 0], mat[2, 1], mat[2, 2], mat[2, 3],
                mat[3, 0], mat[3, 1], mat[3, 2], mat[3, 3]
            );
        }

        private void SetCustomUniforms(Shader shader, ShaderUniforms uniforms)
        {
            foreach (var kvp in uniforms.Values)
            {
                try
                {
                    switch (kvp.Value)
                    {
                        case float f:
                            shader.SetUniform(kvp.Key, f);
                            break;
                        case int i:
                            shader.SetUniform(kvp.Key, i);
                            break;
                        case Float2 v2:
                            shader.SetUniform(kvp.Key, new Vec2((float)v2.X, (float)v2.Y));
                            break;
                        case Float3 v3:
                            shader.SetUniform(kvp.Key, new Vec3((float)v3.X, (float)v3.Y, (float)v3.Z));
                            break;
                        case Float4 v4:
                            shader.SetUniform(kvp.Key, new Vec4((float)v4.X, (float)v4.Y, (float)v4.Z, (float)v4.W));
                            break;
                        case Float4x4 mat:
                            shader.SetUniform(kvp.Key, ToMat4(mat));
                            break;
                    }
                }
                catch (Exception)
                {
                    // Uniform may not exist in the shader - ignore
                }
            }
        }

        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {
            if (_window == null || drawCalls.Count == 0)
                return;

            // Create the blend mode only once
            BlendMode premultipliedAlpha = new(
                BlendMode.Factor.One, // Source color factor
                BlendMode.Factor.OneMinusSrcAlpha, // Destination color factor
                BlendMode.Equation.Add, // Color equation
                BlendMode.Factor.One, // Source alpha factor
                BlendMode.Factor.OneMinusSrcAlpha, // Destination alpha factor 
                BlendMode.Equation.Add // Alpha equation
            );

            _backdropDirty = true;




            ReadOnlySpan<Prowl.Quill.Vertex> vertices = canvas.Vertices;
            ReadOnlySpan<uint> indices = canvas.Indices;

            // Draw all draw calls in the canvas
            int indexOffset = 0;
            for (int i = 0; i < drawCalls.Count; i++)
            {
                var drawCall = drawCalls[i];

                // Backdrop blur: capture the window so far and blur it before drawing this shape.
                if (drawCall.Brush.BackdropBlur > 0f && Shader.IsAvailable)
                {
                    EnsureBlurTargets(_fbWidth, _fbHeight);
                    // Two frosted shapes in a row see the same window behind them, so both the capture and
                    // the pyramid can be reused when nothing has been drawn since.
                    float blurRadius = (float)drawCall.Brush.BackdropBlur;
                    if (_backdropDirty || blurRadius != _lastBlurRadius)
                    {
                        _captureTex.Update(_window);
                        RenderBackdropBlur(blurRadius);
                        _backdropDirty = false;
                        _lastBlurRadius = blurRadius;
                    }
                }

                // Get texture to use
                Texture texture = (drawCall.Texture as TextureSFML)?.Handle ?? _defaultTexture;

                // Create vertex array for this draw call
                _vertexArray.Clear();

                // Create vertices for this draw call
                for (int j = 0; j < drawCall.ElementCount; j++)
                {
                    int idx = (int)indices[indexOffset + j];
                    var vertex = vertices[idx];

                    SFML.Graphics.Vertex sfmlVertex = new(
                        new((float)vertex.Position.X, (float)vertex.Position.Y),
                        new SFML.Graphics.Color(vertex.Color.R, vertex.Color.G, vertex.Color.B, vertex.Color.A),
                        new((float)vertex.UV.X, (float)vertex.UV.Y)
                    );

                    _vertexArray.Append(sfmlVertex);
                }

                // Determine which shader to use
                Shader activeShader = null;
                bool useCustomShader = drawCall.Shader is Shader;

                if (useCustomShader)
                {
                    activeShader = (Shader)drawCall.Shader;

                    // Set projection for custom shader
                    try
                    {
                        Mat4 projMat = new(
                            2.0f / _projection.Size.X, 0, 0, -1,
                            0, -2.0f / _projection.Size.Y, 0, 1,
                            0, 0, 1, 0,
                            0, 0, 0, 1
                        );
                        activeShader.SetUniform("projection", projMat);
                        activeShader.SetUniform("texture0", Shader.CurrentTexture);
                    }
                    catch (Exception) { }

                    // Set user-provided uniforms
                    if (drawCall.ShaderUniforms != null)
                        SetCustomUniforms(activeShader, drawCall.ShaderUniforms);
                }
                else if (Shader.IsAvailable && _shader != null)
                {
                    activeShader = _shader;

                    try
                    {
                        // Set DPI scale for converting pixel coords to logical coords in shader
                        _shader.SetUniform("sdfPxRange", canvas.Text.FontEngine.DistanceRange);

                        // Scissor and brush transforms are 2D affines with the framebuffer scale
                        // already folded in, so no matrix and no dpi divide in the shader.
                        float fbScale = canvas.FramebufferScale;

                        drawCall.GetScissor(fbScale, out var scissorXf, out var scissorT, out var extent);
                        _shader.SetUniform("scissorTransform", new Vec4((float)scissorXf.X, (float)scissorXf.Y, (float)scissorXf.Z, (float)scissorXf.W));
                        _shader.SetUniform("scissorTranslation", new Vec2((float)scissorT.X, (float)scissorT.Y));
                        _shader.SetUniform("scissorExt", new Vec2((float)extent.X, (float)extent.Y));

                        // Set brush parameters
                        drawCall.GetBrushTransform(fbScale, out var brushXf, out var brushT);
                        _shader.SetUniform("brushTransform", new Vec4((float)brushXf.X, (float)brushXf.Y, (float)brushXf.Z, (float)brushXf.W));
                        _shader.SetUniform("brushTranslation", new Vec2((float)brushT.X, (float)brushT.Y));
                        _shader.SetUniform("brushType", (int)drawCall.Brush.Type);
                        _shader.SetUniform("brushColor1", ToVec4(drawCall.Brush.Color1));
                        _shader.SetUniform("brushColor2", ToVec4(drawCall.Brush.Color2));
                        _shader.SetUniform("brushParams", new Vec4(
                            (float)drawCall.Brush.Point1.X, (float)drawCall.Brush.Point1.Y,
                            (float)drawCall.Brush.Point2.X, (float)drawCall.Brush.Point2.Y));
                        _shader.SetUniform("brushParams2", new Vec2(
                            (float)drawCall.Brush.CornerRadii, (float)drawCall.Brush.Feather));

                        // Font atlas on its own sampler so text batches with shapes (text samples it).
                        _shader.SetUniform("fontTexture", (drawCall.FontAtlas as TextureSFML)?.Handle ?? _defaultTexture);

                        // Set texture transform parameters
                        drawCall.GetTextureTransform(fbScale, out var texXf, out var texT);
                        _shader.SetUniform("textureTransform", new Vec4((float)texXf.X, (float)texXf.Y, (float)texXf.Z, (float)texXf.W));
                        _shader.SetUniform("textureTranslation", new Vec2((float)texT.X, (float)texT.Y));

                        // Backdrop blur uniforms
                        float blurAmount = (float)drawCall.Brush.BackdropBlur;
                        _shader.SetUniform("backdropBlurAmount", blurAmount);
                        if (blurAmount > 0f)
                        {
                            _shader.SetUniform("viewportSize", new Vec2(_fbWidth, _fbHeight));
                            _shader.SetUniform("backdropFlipY", BackdropFlipY);
                            _shader.SetUniform("backdropTexture", _blurLevels[0].Texture);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error setting shader uniforms: {ex.Message}");
                    }
                }

                // Draw current batch with appropriate texture and shader
                RenderStates states = new(
                    premultipliedAlpha,
                    Transform.Identity,
                    texture,
                    activeShader
                );

                _window.Draw(_vertexArray, states);

                _backdropDirty = true;

                indexOffset += drawCall.ElementCount;
            }
        }

        public void Dispose()
        {
            _shader?.Dispose();
            _vertexArray?.Dispose();
            _vertexBuffer?.Dispose();
            _blurDown?.Dispose();
            _blurUp?.Dispose();
            _captureTex?.Dispose();
            for (int i = 0; i < MaxBlurLevels; i++) _blurLevels[i]?.Dispose();
        }
    }
}