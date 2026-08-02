using Common;
using Silk.NET.OpenGL;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace SilkExample
{
    /// <summary>
    /// Handles all OpenGL rendering logic for the vector graphics canvas using Silk.NET
    /// </summary>
    public class SilkNetRenderer : ICanvasRenderer, IDisposable
    {
        // Shader source for the fragment shader
        // Use shared shader source from Common
        public static string FRAGMENT_SHADER_SOURCE => CanvasShaders.Fragment;
        private static string VERTEX_SHADER_SOURCE => CanvasShaders.Vertex;

        private readonly GL _gl;
        private uint _program;
        private uint _vao;
        private uint _vbo;
        private uint _ebo;
        private int _projectionLocation;
        private int _textureSamplerLocation;
        private int _fontTextureLocation;
        private int _scissorTransformLocation;
        private int _scissorTranslationLocation;
        private int _scissorExtLocation;
        private int _brushTransformLocation;
        private int _brushTranslationLocation;
        private int _brushTypeLocation;
        private int _brushColor1Location;
        private int _brushColor2Location;
        private int _brushParamsLocation;
        private int _brushParams2Location;
        private int _textureTransformLocation;
        private int _textureTranslationLocation;
        private int _sdfPxRangeLocation;
        private float _sdfPxRange = 4f;
        private int _atlasTexelSizeLocation;
        private int _backdropFlipYLocation;

        // Backdrop blur (dual Kawase)
        private int _backdropTexLocation;
        private int _viewportSizeLocation;
        private int _backdropBlurAmountLocation;
        private uint _blurDownProgram;
        private uint _blurUpProgram;
        private int _downSrcLoc, _downHalfpixelLoc, _downOffsetLoc;
        private int _upSrcLoc, _upHalfpixelLoc, _upOffsetLoc;
        private uint _blurVao;
        private uint _blurFbo;
        // How far below the framebuffer the blur pyramid starts: 1 = half res, 2 = quarter. The canvas
        // composites straight from level 0, so this also decides the resolution the backdrop is sampled
        // at. Quarter is four times cheaper across every pass and is imperceptible above roughly an
        // eight pixel radius, since detail finer than the blur is destroyed anyway.
        private const int BlurBaseShift = 2;
        private const int MaxBlurLevels = 6;
        private readonly uint[] _blurTex = new uint[MaxBlurLevels];   // mip pyramid, level 0 is half the viewport
        private readonly Int2[] _blurSize = new Int2[MaxBlurLevels];
        private int _fbWidth;
        private int _fbHeight;
        private int _vertexBufferCapacity;
        private int _indexBufferCapacity;
        private bool _backdropDirty = true;
        private float _lastBlurRadius = -1f;

        // Offscreen scene target, used only on frames that actually contain a frosted shape. Drawing
        // through it lets the blur sample the scene as a texture instead of blitting the default
        // framebuffer once per blurred draw call, and removes the mid-frame read-back stall.
        private uint _sceneFbo;
        private uint _sceneTex;
        private int _sceneW, _sceneH;
        private bool _renderingOffscreen;



        private int _blurBaseW;
        private int _blurBaseH;

        public bool SupportsBackdropBlur => true;

        private Float4x4 _projection;
        private TextureSilk _defaultTexture;
        public SilkNetRenderer(GL gl)
        {
            _gl = gl;
        }

        public unsafe void Initialize(int width, int height, TextureSilk defaultTexture)
        {
            _defaultTexture = defaultTexture;
            CreateShaderProgram();
            CreateBuffers();
            UpdateProjection(width, height);
        }

        private void CreateShaderProgram()
        {
            _program = _gl.CreateProgram();
    
            // Create and attach shaders
            uint vertShader = CompileShader(ShaderType.VertexShader, VERTEX_SHADER_SOURCE);
            uint fragShader = CompileShader(ShaderType.FragmentShader, FRAGMENT_SHADER_SOURCE);
    
            _gl.AttachShader(_program, vertShader);
            _gl.AttachShader(_program, fragShader);
            _gl.LinkProgram(_program);
            CheckProgramLinking(_program);
    
            // Cleanup shader objects
            _gl.DetachShader(_program, vertShader);
            _gl.DetachShader(_program, fragShader);
            _gl.DeleteShader(vertShader);
            _gl.DeleteShader(fragShader);

            // Cache uniform locations
            CacheUniformLocations();

            // Dual Kawase blur programs and shared blur objects
            _blurDownProgram = BuildBlurProgram(CanvasShaders.BlurDownsample);
            _downSrcLoc = _gl.GetUniformLocation(_blurDownProgram, "src");
            _downHalfpixelLoc = _gl.GetUniformLocation(_blurDownProgram, "halfpixel");
            _downOffsetLoc = _gl.GetUniformLocation(_blurDownProgram, "offset");
            _blurUpProgram = BuildBlurProgram(CanvasShaders.BlurUpsample);
            _upSrcLoc = _gl.GetUniformLocation(_blurUpProgram, "src");
            _upHalfpixelLoc = _gl.GetUniformLocation(_blurUpProgram, "halfpixel");
            _upOffsetLoc = _gl.GetUniformLocation(_blurUpProgram, "offset");
            _blurVao = _gl.GenVertexArray();
            _blurFbo = _gl.GenFramebuffer();
        }

        private uint BuildBlurProgram(string fragmentSource)
        {
            uint program = _gl.CreateProgram();
            uint vs = CompileShader(ShaderType.VertexShader, CanvasShaders.BlurVertex);
            uint fs = CompileShader(ShaderType.FragmentShader, fragmentSource);
            _gl.AttachShader(program, vs);
            _gl.AttachShader(program, fs);
            _gl.LinkProgram(program);
            CheckProgramLinking(program);
            _gl.DeleteShader(vs);
            _gl.DeleteShader(fs);
            return program;
        }
        
        private uint CompileShader(ShaderType type, string source)
        {
            uint shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);
    
            _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
            if (status != (int)GLEnum.True)
            {
                string log = _gl.GetShaderInfoLog(shader);
                throw new Exception($"{type} shader compilation failed: {log}");
            }
    
            return shader;
        }
        
        private void CacheUniformLocations()
        {
            // Get all uniform locations at once
            _projectionLocation = _gl.GetUniformLocation(_program, "projection");
            _textureSamplerLocation = _gl.GetUniformLocation(_program, "texture0");
            _fontTextureLocation = _gl.GetUniformLocation(_program, "fontTexture");
            _scissorTransformLocation = _gl.GetUniformLocation(_program, "scissorTransform");
            _scissorTranslationLocation = _gl.GetUniformLocation(_program, "scissorTranslation");
            _scissorExtLocation = _gl.GetUniformLocation(_program, "scissorExt");
            _brushTransformLocation = _gl.GetUniformLocation(_program, "brushTransform");
            _brushTranslationLocation = _gl.GetUniformLocation(_program, "brushTranslation");
            _brushTypeLocation = _gl.GetUniformLocation(_program, "brushType");
            _brushColor1Location = _gl.GetUniformLocation(_program, "brushColor1");
            _brushColor2Location = _gl.GetUniformLocation(_program, "brushColor2");
            _brushParamsLocation = _gl.GetUniformLocation(_program, "brushParams");
            _brushParams2Location = _gl.GetUniformLocation(_program, "brushParams2");
            _textureTransformLocation = _gl.GetUniformLocation(_program, "textureTransform");
            _textureTranslationLocation = _gl.GetUniformLocation(_program, "textureTranslation");
            _sdfPxRangeLocation = _gl.GetUniformLocation(_program, "sdfPxRange");
            _backdropTexLocation = _gl.GetUniformLocation(_program, "backdropTexture");
            _viewportSizeLocation = _gl.GetUniformLocation(_program, "viewportSize");
            _backdropBlurAmountLocation = _gl.GetUniformLocation(_program, "backdropBlurAmount");
            _atlasTexelSizeLocation = _gl.GetUniformLocation(_program, "atlasTexelSize");
            _backdropFlipYLocation = _gl.GetUniformLocation(_program, "backdropFlipY");
        }
        
        private void CheckProgramLinking(uint program)
        {
            _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
            if (status != (int)GLEnum.True)
            {
                string log = _gl.GetProgramInfoLog(program);
                throw new Exception($"Program linking failed: {log}");
            }
        }

        private unsafe void CreateBuffers()
        {
            // Create vertex array object
            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            // Create vertex buffer object
            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

            // Define vertex attributes (20-byte vertex)
            uint stride = (uint)Vertex.SizeInBytes; // 44

            _gl.EnableVertexAttribArray(0); // Position: vec2 at offset 0
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);

            _gl.EnableVertexAttribArray(1); // TexCoord/EmCoord: vec2 at offset 8
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)8);

            _gl.EnableVertexAttribArray(2); // Color: vec4 ubyte normalized at offset 16
            _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)16);

            // Create element buffer object
            _ebo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

            // Unbind VAO so it's not accidentally modified
            _gl.BindVertexArray(0);
        }

        public void UpdateProjection(int width, int height)
        {
            _fbWidth = width;
            _fbHeight = height;
            _projection = Float4x4.CreateOrthoOffCenter(0, width, height, 0, -1, 1);
        }

        public object CreateTexture(uint width, uint height)
        {
            return TextureSilk.CreateNew(_gl, width, height);
        }

        public Int2 GetTextureSize(object texture)
        {
            if (texture is not TextureSilk silkTexture)
                throw new ArgumentException("Invalid texture type");

            return new Int2((int)silkTexture.Width, (int)silkTexture.Height);
        }

        public void SetTextureData(object texture, IntRect bounds, byte[] data)
        {
            if (texture is not TextureSilk silkTexture)
                throw new ArgumentException("Invalid texture type");
            silkTexture.SetData(bounds, data);
        }
        
        private unsafe void SetMatrix4Uniform(int location, Float4x4 matrix)
        {
            _gl.UniformMatrix4(location, 1, false, in matrix.c0.X);
        }

        /// <summary>
        /// Uploads a matrix for the built-in canvas program, which is generated from Slang and so
        /// expects the transpose (Slang emits mul(M, v) as v * M). Custom user shaders keep the
        /// untransposed convention through <see cref="SetMatrix4Uniform"/>.
        /// </summary>
        private unsafe void SetCanvasMatrixUniform(int location, Float4x4 matrix)
        {
            _gl.UniformMatrix4(location, 1, true, in matrix.c0.X);
        }


        public unsafe void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {
            if (drawCalls.Count == 0 || canvas.VertexCount == 0 || canvas.IndexCount == 0)
                return;

            // Set up rendering state
            _sdfPxRange = canvas.Text.FontEngine.DistanceRange;

            // A fresh frame means the framebuffer behind any frosted shape has changed, and another
            // renderer may have driven this program since, so the per-group cache is dropped too.
            _backdropDirty = true;

            // Only frames that actually contain a frosted shape pay for the offscreen target; anything
            // else draws straight to the default framebuffer exactly as before.
            _renderingOffscreen = false;
            for (int i = 0; i < drawCalls.Count; i++)
            {
                if (drawCalls[i].Brush.BackdropBlur > 0f) { _renderingOffscreen = true; break; }
            }

            if (_renderingOffscreen)
            {
                EnsureSceneTarget(_fbWidth, _fbHeight);

                // Seed with what is already on screen, so a frosted shape blurs whatever the host
                // drew underneath the canvas and not just the canvas's own content.
                _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
                _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _sceneFbo);
                _gl.BlitFramebuffer(0, 0, _fbWidth, _fbHeight, 0, 0, _fbWidth, _fbHeight,
                    ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);
                _gl.Viewport(0, 0, (uint)_fbWidth, (uint)_fbHeight);
            }

            SetupRenderState();
    
            // Upload vertex and index data
            UploadGeometryData(canvas);
    
            // Process each draw call
            int indexOffset = 0;
            foreach (var drawCall in drawCalls)
            {
                ProcessDrawCall(drawCall, indexOffset, (float)canvas.FramebufferScale);
                indexOffset += drawCall.ElementCount;
            }
    
            // Present the offscreen scene to the window.
            if (_renderingOffscreen)
            {
                _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _sceneFbo);
                _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
                _gl.BlitFramebuffer(0, 0, _fbWidth, _fbHeight, 0, 0, _fbWidth, _fbHeight,
                    ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                _renderingOffscreen = false;
            }

            // Cleanup state
            _gl.BindVertexArray(0);
            _gl.UseProgram(0);
        }
        
        private void SetupRenderState()
        {
            _gl.Disable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
            _gl.UseProgram(_program);
            SetProjectionMatrix();
            _gl.BindVertexArray(_vao);
            _gl.Uniform1(_textureSamplerLocation, 0); // brush/shape texture on unit 0
            _gl.Uniform1(_fontTextureLocation, 1);    // font atlas on unit 1
        }
        
        private unsafe void UploadGeometryData(Canvas canvas)
        {
            // Upload vertices straight from the canvas backing store
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (Vertex* vertexPtr = canvas.VertexBuffer)
                UploadStream(BufferTargetARB.ArrayBuffer, ref _vertexBufferCapacity,
                    canvas.VertexCount * Vertex.SizeInBytes, vertexPtr);

            // Upload indices
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            fixed (uint* indexPtr = canvas.IndexBuffer)
                UploadStream(BufferTargetARB.ElementArrayBuffer, ref _indexBufferCapacity,
                    canvas.IndexCount * sizeof(uint), indexPtr);
        }

        /// <summary>
        /// Streams a frame's geometry into a buffer. The store is reallocated only when it needs to
        /// grow; otherwise it is orphaned and refilled, so the driver hands back fresh memory instead
        /// of stalling on the previous frame's contents still being read.
        /// </summary>
        private unsafe void UploadStream(BufferTargetARB target, ref int capacity, int sizeInBytes, void* data)
        {
            // Round up on growth so a steadily growing canvas does not resize every frame.
            if (sizeInBytes > capacity)
                capacity = Math.Max(sizeInBytes, capacity == 0 ? 64 * 1024 : capacity * 2);

            // Same call either way: it allocates on the first pass and orphans on every later one.
            _gl.BufferData(target, (nuint)capacity, (void*)0, BufferUsageARB.StreamDraw);
            _gl.BufferSubData(target, 0, (nuint)sizeInBytes, data);
        }
        
        private unsafe uint CreateBlurTexture(int w, int h)
        {
            uint tex = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, tex);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)0);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        private unsafe void EnsureSceneTarget(int w, int h)
        {
            if (_sceneTex != 0 && _sceneW == w && _sceneH == h)
                return;

            if (_sceneTex != 0) _gl.DeleteTexture(_sceneTex);
            if (_sceneFbo == 0) _sceneFbo = _gl.GenFramebuffer();

            _sceneTex = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _sceneTex);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)0);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _gl.BindTexture(TextureTarget.Texture2D, 0);

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _sceneTex, 0);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            _sceneW = w;
            _sceneH = h;
        }



        private void EnsureBlurTargets(int baseW, int baseH)
        {
            if (_blurTex[0] != 0 && _blurBaseW == baseW && _blurBaseH == baseH)
                return;
            for (int i = 0; i < MaxBlurLevels; i++)
                if (_blurTex[i] != 0) _gl.DeleteTexture(_blurTex[i]);

            for (int i = 0; i < MaxBlurLevels; i++)
            {
                int w = Math.Max(1, baseW >> (i + BlurBaseShift));
                int h = Math.Max(1, baseH >> (i + BlurBaseShift));
                _blurSize[i] = new Int2(w, h);
                _blurTex[i] = CreateBlurTexture(w, h);
            }
            _blurBaseW = baseW;
            _blurBaseH = baseH;
        }

        private static void ComputeBlurParams(float radius, out int iterations, out float offset)
        {
            // radius is in screen pixels, but the pyramid maths below works in level-0 texels, and one of

            // those spans 1 << BlurBaseShift pixels. Converting here is what makes SetBackdropBlur(22)

            // actually mean 22 pixels regardless of what resolution the pyramid starts at.

            float r = MathF.Max(radius / (1 << BlurBaseShift), 2f);
            iterations = Math.Clamp((int)MathF.Floor(MathF.Log2(r)) - 1, 1, MaxBlurLevels - 1);
            offset = Math.Clamp(r / (1 << (iterations + 1)), 0.5f, 6f);
        }

        /// <summary>
        /// Captures the framebuffer behind the shape and dual-Kawase blurs it into _blurTex[0],
        /// leaving it bound on texture unit 3 for the canvas shader composite.
        /// </summary>
        private void RenderBackdropBlur(float radius)
        {
            // Two frosted shapes in a row see the same framebuffer behind them, so the pyramid only
            // needs rebuilding when something has been drawn since the last one, or the radius moved.
            if (!_backdropDirty && radius == _lastBlurRadius && _blurTex[0] != 0)
            {
                _gl.ActiveTexture(TextureUnit.Texture3);
                _gl.BindTexture(TextureTarget.Texture2D, _blurTex[0]);
                _gl.ActiveTexture(TextureUnit.Texture0);
                return;
            }

            _backdropDirty = false;
            _lastBlurRadius = radius;

            EnsureBlurTargets(_fbWidth, _fbHeight);
            ComputeBlurParams(radius, out int iterations, out float offset);

            _gl.Disable(EnableCap.Blend);
            _gl.BindVertexArray(_blurVao);
            _gl.ActiveTexture(TextureUnit.Texture0);

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _blurFbo);

            // Downsample chain. The first pass reads the scene texture straight into level 0, so the
            // full-resolution blit the old capture needed is gone, and the scene is filtered by the
            // five-tap kernel rather than a single bilinear step (which would alias at this ratio).
            _gl.UseProgram(_blurDownProgram);
            _gl.Uniform1(_downSrcLoc, 0);
            _gl.Uniform1(_downOffsetLoc, offset);
            BlurPass(_sceneTex, _blurTex[0], _blurSize[0], _downHalfpixelLoc, new Int2(_fbWidth, _fbHeight));
            for (int i = 0; i < iterations; i++)
                BlurPass(_blurTex[i], _blurTex[i + 1], _blurSize[i + 1], _downHalfpixelLoc, _blurSize[i]);

            _gl.UseProgram(_blurUpProgram);
            _gl.Uniform1(_upSrcLoc, 0);
            _gl.Uniform1(_upOffsetLoc, offset);
            for (int i = iterations; i > 0; i--)
                BlurPass(_blurTex[i], _blurTex[i - 1], _blurSize[i - 1], _upHalfpixelLoc, _blurSize[i - 1]);

            // Restore state for canvas drawing, back into whichever target the frame is drawing to.
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _renderingOffscreen ? _sceneFbo : 0);
            _gl.Viewport(0, 0, (uint)_fbWidth, (uint)_fbHeight);
            _gl.Enable(EnableCap.Blend);
            _gl.BindVertexArray(_vao);

            _gl.ActiveTexture(TextureUnit.Texture3);
            _gl.BindTexture(TextureTarget.Texture2D, _blurTex[0]);
            _gl.ActiveTexture(TextureUnit.Texture0);
        }

        private void BlurPass(uint srcTex, uint dstTex, Int2 dstSize, int halfpixelLoc, Int2 halfpixelBasis)
        {
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, dstTex, 0);
            _gl.Viewport(0, 0, (uint)dstSize.X, (uint)dstSize.Y);
            _gl.Uniform2(halfpixelLoc, 0.5f / halfpixelBasis.X, 0.5f / halfpixelBasis.Y);
            _gl.BindTexture(TextureTarget.Texture2D, srcTex);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        private unsafe void ProcessDrawCall(DrawCall drawCall, int indexOffset, float dpiScale)
        {
            // Backdrop blur: capture and blur the framebuffer behind this shape first.
            if (drawCall.Brush.BackdropBlur > 0f)
                RenderBackdropBlur((float)drawCall.Brush.BackdropBlur);

            // Bind font atlas on unit 1 (for text), then brush/shape texture on unit 0.
            ((drawCall.FontAtlas as TextureSilk) ?? _defaultTexture).Use(TextureUnit.Texture1);
            TextureSilk texture = (drawCall.Texture as TextureSilk) ?? _defaultTexture;
            texture.Use(TextureUnit.Texture0);

            // Check for custom shader
            if (drawCall.Shader is uint customProgram)
            {
                // Use custom shader
                _gl.UseProgram(customProgram);

                // Set projection (required for all shaders to work correctly)
                int projLoc = _gl.GetUniformLocation(customProgram, "projection");
                if (projLoc >= 0)
                    SetMatrix4Uniform(projLoc, _projection);

                // Set texture sampler
                int texLoc = _gl.GetUniformLocation(customProgram, "texture0");
                if (texLoc >= 0)
                    _gl.Uniform1(texLoc, 0);

                // Set user-provided uniforms
                if (drawCall.ShaderUniforms != null)
                    SetCustomUniforms(customProgram, drawCall.ShaderUniforms);
            }
            else
            {
                // Use default shader
                _gl.UseProgram(_program);
                SetProjectionMatrix();

                // Scissor and brush transforms are 2D affines with the framebuffer scale already
                // folded in, so the shader needs neither a matrix nor a dpi divide.
                drawCall.GetScissor(dpiScale, out var scissorXf, out var scissorT, out var scissorExt);
                SetScissorUniforms(scissorXf, scissorT, scissorExt);
                SetBrushUniforms(in drawCall, dpiScale);
                _gl.Uniform1(_sdfPxRangeLocation, _sdfPxRange);

                // Font atlas texel size, so the text distance field resolves at any zoom. The
                // generated shader takes this as a uniform rather than calling textureSize.
                var atlas = drawCall.FontAtlas as TextureSilk ?? _defaultTexture;
                _gl.Uniform2(_atlasTexelSizeLocation, atlas.Width > 0 ? 1f / atlas.Width : 0f, atlas.Height > 0 ? 1f / atlas.Height : 0f);

                // Backdrop blur uniforms (blurred texture bound on unit 3 by RenderBackdropBlur).
                // The default framebuffer is bottom-left origin here, so the sample flips.
                _gl.Uniform2(_viewportSizeLocation, (float)_fbWidth, (float)_fbHeight);
                _gl.Uniform1(_backdropTexLocation, 3);
                _gl.Uniform1(_backdropBlurAmountLocation, (float)drawCall.Brush.BackdropBlur);
                _gl.Uniform1(_backdropFlipYLocation, 1);
            }

            // Draw the elements
            _gl.DrawElements(
                PrimitiveType.Triangles,
                (uint)drawCall.ElementCount,
                DrawElementsType.UnsignedInt,
                (void*)(indexOffset * sizeof(uint))
            );

            _backdropDirty = true;
        }

        private void SetProjectionMatrix()
        {
            SetCanvasMatrixUniform(_projectionLocation, _projection);
        }


        private void SetScissorUniforms(Float4 transform, Float2 translation, Float2 extent)
        {
            _gl.Uniform4(_scissorTransformLocation, (float)transform.X, (float)transform.Y, (float)transform.Z, (float)transform.W);
            _gl.Uniform2(_scissorTranslationLocation, (float)translation.X, (float)translation.Y);
            _gl.Uniform2(_scissorExtLocation, (float)extent.X, (float)extent.Y);
        }

        private void SetBrushUniforms(in DrawCall drawCall, float framebufferScale)
        {
            Brush brush = drawCall.Brush;

            drawCall.GetBrushTransform(framebufferScale, out Float4 brushXf, out Float2 brushT);
            drawCall.GetTextureTransform(framebufferScale, out Float4 texXf, out Float2 texT);

            _gl.Uniform4(_textureTransformLocation, (float)texXf.X, (float)texXf.Y, (float)texXf.Z, (float)texXf.W);
            _gl.Uniform2(_textureTranslationLocation, (float)texT.X, (float)texT.Y);

            _gl.Uniform4(_brushTransformLocation, (float)brushXf.X, (float)brushXf.Y, (float)brushXf.Z, (float)brushXf.W);
            _gl.Uniform2(_brushTranslationLocation, (float)brushT.X, (float)brushT.Y);

            // Set other brush parameters
            _gl.Uniform1(_brushTypeLocation, (int)brush.Type);

            _gl.Uniform4(
                _brushColor1Location,
                brush.Color1.R / 255f,
                brush.Color1.G / 255f,
                brush.Color1.B / 255f,
                brush.Color1.A / 255f);

            _gl.Uniform4(
                _brushColor2Location,
                brush.Color2.R / 255f,
                brush.Color2.G / 255f,
                brush.Color2.B / 255f,
                brush.Color2.A / 255f);

            _gl.Uniform4(
                _brushParamsLocation,
                (float)brush.Point1.X,
                (float)brush.Point1.Y,
                (float)brush.Point2.X,
                (float)brush.Point2.Y);

            _gl.Uniform2(
                _brushParams2Location,
                (float)brush.CornerRadii,
                (float)brush.Feather);

        }

        private unsafe void SetCustomUniforms(uint program, ShaderUniforms uniforms)
        {
            foreach (var kvp in uniforms.Values)
            {
                int loc = _gl.GetUniformLocation(program, kvp.Key);
                if (loc < 0) continue;

                switch (kvp.Value)
                {
                    case float f:
                        _gl.Uniform1(loc, f);
                        break;
                    case int i:
                        _gl.Uniform1(loc, i);
                        break;
                    case Float2 v2:
                        _gl.Uniform2(loc, (float)v2.X, (float)v2.Y);
                        break;
                    case Float3 v3:
                        _gl.Uniform3(loc, (float)v3.X, (float)v3.Y, (float)v3.Z);
                        break;
                    case Float4 v4:
                        _gl.Uniform4(loc, (float)v4.X, (float)v4.Y, (float)v4.Z, (float)v4.W);
                        break;
                    case Float4x4 mat:
                        SetMatrix4Uniform(loc, mat);
                        break;
                }
            }
        }

        public void Cleanup()
        {
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteBuffer(_ebo);
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteProgram(_program);

            if (_blurDownProgram != 0) _gl.DeleteProgram(_blurDownProgram);
            if (_blurUpProgram != 0) _gl.DeleteProgram(_blurUpProgram);
            if (_blurVao != 0) _gl.DeleteVertexArray(_blurVao);
            if (_blurFbo != 0) _gl.DeleteFramebuffer(_blurFbo);
            for (int i = 0; i < MaxBlurLevels; i++)
                if (_blurTex[i] != 0) { _gl.DeleteTexture(_blurTex[i]); _blurTex[i] = 0; }
            _blurDownProgram = _blurUpProgram = _blurVao = _blurFbo = 0;
            _blurBaseW = _blurBaseH = 0;
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}