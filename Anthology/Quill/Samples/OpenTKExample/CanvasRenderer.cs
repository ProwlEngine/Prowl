using Common;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKExample
{
    /// <summary>
    /// Handles all OpenGL rendering logic for the vector graphics canvas
    /// </summary>
    public class CanvasRenderer : ICanvasRenderer
    {
        // Use shared shader source from Common
        public static string STROKE_FRAGMENT_SHADER => CanvasShaderSource.FragmentShader;
        private static string DEFAULT_VERTEX_SHADER => CanvasShaderSource.VertexShader;

        // OpenGL objects
        private int _shaderProgram;
        private int _vertexArrayObject;
        private int _vertexBufferObject;
        private int _elementBufferObject;
        private int _projectionLocation;
        private int _textureSamplerLocation;
        private int _scissorMatLoc = 0;
        private int _scissorExtLoc = 0;

        static int _brushMatLoc;
        static int _brushTypeLoc;
        static int _brushColor1Loc;
        static int _brushColor2Loc;
        static int _brushParamsLoc;
        static int _brushParams2Loc;
        static int _brushTextureMatLoc;
        static int _dpiScaleLoc;

        private Matrix4 _projection;
        private TextureTK _defaultTexture;

        // Slug uniform locations
        private int _slugCurveTexLoc;
        private int _slugBandTexLoc;
        private int _slugCurveTexSizeLoc;
        private int _slugBandTexSizeLoc;

        /// <summary>
        /// Initialize the renderer with the window dimensions
        /// </summary>
        public void Initialize(int width, int height, TextureTK defaultTexture)
        {
            InitializeShaders();

            // Create OpenGL buffer objects
            _vertexArrayObject = GL.GenVertexArray();
            _vertexBufferObject = GL.GenBuffer();
            _elementBufferObject = GL.GenBuffer();

            // Set the default texture
            _defaultTexture = defaultTexture;

            UpdateProjection(width, height);
        }

        /// <summary>
        /// Update the projection matrix when the window is resized
        /// </summary>
        public void UpdateProjection(int width, int height) => _projection = Matrix4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);

        /// <summary>
        /// Clean up OpenGL resources
        /// </summary>
        public void Cleanup()
        {
            GL.DeleteBuffer(_vertexBufferObject);
            GL.DeleteBuffer(_elementBufferObject);
            GL.DeleteVertexArray(_vertexArrayObject);
            GL.DeleteProgram(_shaderProgram);
        }

        private void InitializeShaders()
        {
            _shaderProgram = GL.CreateProgram();

            // Compile vertex shader
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, DEFAULT_VERTEX_SHADER);
            GL.CompileShader(vertexShader);

            // Compile fragment shader
            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, STROKE_FRAGMENT_SHADER);
            GL.CompileShader(fragmentShader);

            // Link the program
            GL.AttachShader(_shaderProgram, vertexShader);
            GL.AttachShader(_shaderProgram, fragmentShader);
            GL.LinkProgram(_shaderProgram);

            // Clean up shader objects
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            // Get location of the projection uniform
            _projectionLocation = GL.GetUniformLocation(_shaderProgram, "projection");
            _textureSamplerLocation = GL.GetUniformLocation(_shaderProgram, "textureSampler");
            _scissorMatLoc = GL.GetUniformLocation(_shaderProgram, "scissorMat");
            _scissorExtLoc = GL.GetUniformLocation(_shaderProgram, "scissorExt");

            _brushMatLoc = GL.GetUniformLocation(_shaderProgram, "brushMat");
            _brushTypeLoc = GL.GetUniformLocation(_shaderProgram, "brushType");
            _brushColor1Loc = GL.GetUniformLocation(_shaderProgram, "brushColor1");
            _brushColor2Loc = GL.GetUniformLocation(_shaderProgram, "brushColor2");
            _brushParamsLoc = GL.GetUniformLocation(_shaderProgram, "brushParams");
            _brushParams2Loc = GL.GetUniformLocation(_shaderProgram, "brushParams2");
            _brushTextureMatLoc = GL.GetUniformLocation(_shaderProgram, "brushTextureMat");
            _dpiScaleLoc = GL.GetUniformLocation(_shaderProgram, "dpiScale");
            _slugCurveTexLoc = GL.GetUniformLocation(_shaderProgram, "slugCurveTexture");
            _slugBandTexLoc = GL.GetUniformLocation(_shaderProgram, "slugBandTexture");
            _slugCurveTexSizeLoc = GL.GetUniformLocation(_shaderProgram, "slugCurveTexSize");
            _slugBandTexSizeLoc = GL.GetUniformLocation(_shaderProgram, "slugBandTexSize");
        }

        private Matrix4 ToTK(Float4x4 mat) => new Matrix4(
            (float)mat[0, 0], (float)mat[1, 0], (float)mat[2, 0], (float)mat[3, 0],
            (float)mat[0, 1], (float)mat[1, 1], (float)mat[2, 1], (float)mat[3, 1],
            (float)mat[0, 2], (float)mat[1, 2], (float)mat[2, 2], (float)mat[3, 2],
            (float)mat[0, 3], (float)mat[1, 3], (float)mat[2, 3], (float)mat[3, 3]
        );

        private OpenTK.Mathematics.Vector4 ToTK(Prowl.Vector.Float4 v) => new OpenTK.Mathematics.Vector4(
            (float)v.X, (float)v.Y, (float)v.Z, (float)v.W
        );

        private OpenTK.Mathematics.Vector4 ToTK(Color32 color) => new OpenTK.Mathematics.Vector4(
            color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f
        );

        private void SetCustomUniforms(int program, ShaderUniforms uniforms)
        {
            foreach (var kvp in uniforms.Values)
            {
                int loc = GL.GetUniformLocation(program, kvp.Key);
                if (loc < 0) continue;

                switch (kvp.Value)
                {
                    case float f:
                        GL.Uniform1(loc, f);
                        break;
                    case int i:
                        GL.Uniform1(loc, i);
                        break;
                    case Float2 v2:
                        GL.Uniform2(loc, (float)v2.X, (float)v2.Y);
                        break;
                    case Float3 v3:
                        GL.Uniform3(loc, (float)v3.X, (float)v3.Y, (float)v3.Z);
                        break;
                    case Float4 v4:
                        GL.Uniform4(loc, (float)v4.X, (float)v4.Y, (float)v4.Z, (float)v4.W);
                        break;
                    case Float4x4 mat:
                        var tkMat = ToTK(mat);
                        GL.UniformMatrix4(loc, false, ref tkMat);
                        break;
                }
            }
        }

        public object CreateTexture(uint width, uint height)
        {
            return TextureTK.CreateNew(width, height);
        }

        public object? CreateFloatTexture(int width, int height, int components, float[] data)
        {
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            if (components == 4)
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f,
                    width, height, 0, PixelFormat.Rgba, PixelType.Float, data);
            }
            else if (components == 2)
            {
                // Pack RG into RGBA (ZW = 0)
                float[] rgbaData = new float[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    rgbaData[i * 4 + 0] = data[i * 2 + 0];
                    rgbaData[i * 4 + 1] = data[i * 2 + 1];
                }
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f,
                    width, height, 0, PixelFormat.Rgba, PixelType.Float, rgbaData);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        public Int2 GetTextureSize(object texture)
        {
            if (texture is not TextureTK tkTexture)
                throw new ArgumentException("Invalid texture type");

            return new Int2((int)tkTexture.Width, (int)tkTexture.Height);
        }

        public void SetTextureData(object texture, IntRect bounds, byte[] data)
        {
            if (texture is not TextureTK tkTexture)
                throw new ArgumentException("Invalid texture type");
            tkTexture.SetData(bounds, data);
        }

        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {

            // Skip if canvas is empty
            if (drawCalls.Count == 0)
                return;

            // Configure OpenGL state
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);

            // Use shader and set projection
            GL.UseProgram(_shaderProgram);
            GL.UniformMatrix4(_projectionLocation, false, ref _projection);

            // Bind vertex array
            GL.BindVertexArray(_vertexArrayObject);

            // Upload vertex data (44 bytes per vertex: 20 core + 24 slug)
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, canvas.Vertices.Count * Vertex.SizeInBytes, canvas.Vertices.ToArray(), BufferUsageHint.StreamDraw);

            int stride = Vertex.SizeInBytes; // 44

            // Position (location 0): vec2 at offset 0
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);

            // TexCoord / EmCoord (location 1): vec2 at offset 8
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);

            // Color (location 2): vec4 ubyte normalized at offset 16
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

            // Slug band transform (location 3): vec4 at offset 20
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, 20);

            // Slug glyph info (location 4): vec2 at offset 36
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, stride, 36);

            // Upload index data
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, canvas.Indices.Count * sizeof(uint), canvas.Indices.ToArray(), BufferUsageHint.StreamDraw);

            // Active texture unit for sampling
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(_textureSamplerLocation, 0); // texture unit 0

            // Draw all draw calls in the canvas
            int indexOffset = 0;
            foreach (var drawCall in drawCalls)
            {
                // Handle texture binding
                (drawCall.Texture as TextureTK ?? _defaultTexture).Use(TextureUnit.Texture0);

                // Check for custom shader
                if (drawCall.Shader is int customProgram)
                {
                    // Use custom shader
                    GL.UseProgram(customProgram);

                    // Set projection (required for all shaders to work correctly)
                    int projLoc = GL.GetUniformLocation(customProgram, "projection");
                    if (projLoc >= 0)
                        GL.UniformMatrix4(projLoc, false, ref _projection);

                    // Set texture sampler
                    int texLoc = GL.GetUniformLocation(customProgram, "texture0");
                    if (texLoc >= 0)
                        GL.Uniform1(texLoc, 0);

                    // Set user-provided uniforms
                    if (drawCall.ShaderUniforms != null)
                        SetCustomUniforms(customProgram, drawCall.ShaderUniforms);
                }
                else
                {
                    // Use default shader
                    GL.UseProgram(_shaderProgram);
                    GL.UniformMatrix4(_projectionLocation, false, ref _projection);

                    // Set DPI scale for converting pixel coords to logical coords in shader
                    GL.Uniform1(_dpiScaleLoc, canvas.Scale);

                    // Set scissor rectangle
                    drawCall.GetScissor(out var scissor, out var extent);
                    var tkScissor = ToTK(scissor);
                    GL.UniformMatrix4(_scissorMatLoc, false, ref tkScissor);
                    GL.Uniform2(_scissorExtLoc, (float)extent.X, (float)extent.Y);

                    // Set brush parameters
                    var brushMat = ToTK(drawCall.Brush.BrushMatrix);
                    GL.UniformMatrix4(_brushMatLoc, false, ref brushMat);
                    GL.Uniform1(_brushTypeLoc, (int)drawCall.Brush.Type);
                    GL.Uniform4(_brushColor1Loc, ToTK(drawCall.Brush.Color1));
                    GL.Uniform4(_brushColor2Loc, ToTK(drawCall.Brush.Color2));
                    GL.Uniform4(_brushParamsLoc, (float)drawCall.Brush.Point1.X, (float)drawCall.Brush.Point1.Y, (float)drawCall.Brush.Point2.X, (float)drawCall.Brush.Point2.Y);
                    GL.Uniform2(_brushParams2Loc, (float)drawCall.Brush.CornerRadii, (float)drawCall.Brush.Feather);

                    // Set texture transform parameters
                    var textureMat = ToTK(drawCall.Brush.TextureMatrix);
                    GL.UniformMatrix4(_brushTextureMatLoc, false, ref textureMat);

                    // Bind Slug textures if this is a Slug draw call
                    if (drawCall.IsSlug)
                    {
                        GL.ActiveTexture(TextureUnit.Texture1);
                        GL.BindTexture(TextureTarget.Texture2D, (int)drawCall.SlugCurveTexture!);
                        GL.Uniform1(_slugCurveTexLoc, 1);

                        GL.ActiveTexture(TextureUnit.Texture2);
                        GL.BindTexture(TextureTarget.Texture2D, (int)drawCall.SlugBandTexture!);
                        GL.Uniform1(_slugBandTexLoc, 2);

                        GL.Uniform2(_slugCurveTexSizeLoc, (float)drawCall.SlugCurveTexWidth, (float)drawCall.SlugCurveTexHeight);
                        GL.Uniform2(_slugBandTexSizeLoc, (float)drawCall.SlugBandTexWidth, (float)drawCall.SlugBandTexHeight);

                        GL.ActiveTexture(TextureUnit.Texture0);
                    }
                }

                GL.DrawElements(PrimitiveType.Triangles, drawCall.ElementCount, DrawElementsType.UnsignedInt, indexOffset * sizeof(uint));
                indexOffset += drawCall.ElementCount;
            }

            // Clean up
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            // Dispose of OpenGL resources
            GL.DeleteBuffer(_vertexBufferObject);
            GL.DeleteBuffer(_elementBufferObject);
            GL.DeleteVertexArray(_vertexArrayObject);
            GL.DeleteProgram(_shaderProgram);
            // Dispose of the default texture
            _defaultTexture?.Dispose();
        }
    }
}