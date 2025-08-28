using System.Drawing;

using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;

using Prowl.Quill;
using Prowl.Vector;

namespace OpenTKSample
{
    internal class PaperRenderer : ICanvasRenderer
    {
        // Shader source for the fragment shader
        public const string STROKE_FRAGMENT_SHADER = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
in vec2 fragPos;
out vec4 finalColor;

uniform sampler2D texture0;
uniform mat4 scissorMat;
uniform vec2 scissorExt;

uniform mat4 brushMat;
uniform int brushType;       // 0=none, 1=linear, 2=radial, 3=box
uniform vec4 brushColor1;    // Start color
uniform vec4 brushColor2;    // End color
uniform vec4 brushParams;    // x,y = start point, z,w = end point (or center+radius for radial)
uniform vec2 brushParams2;   // x = Box radius, y = Box Feather

float calculateBrushFactor() {
    // No brush
    if (brushType == 0) return 0.0;
    
    vec2 transformedPoint = (brushMat * vec4(fragPos, 0.0, 1.0)).xy;

    // Linear brush - projects position onto the line between start and end
    if (brushType == 1) {
        vec2 startPoint = brushParams.xy;
        vec2 endPoint = brushParams.zw;
        vec2 line = endPoint - startPoint;
        float lineLength = length(line);
        
        if (lineLength < 0.001) return 0.0;
        
        vec2 posToStart = transformedPoint - startPoint;
        float projection = dot(posToStart, line) / (lineLength * lineLength);
        return clamp(projection, 0.0, 1.0);
    }
    
    // Radial brush - based on distance from center
    if (brushType == 2) {
        vec2 center = brushParams.xy;
        float innerRadius = brushParams.z;
        float outerRadius = brushParams.w;
        
        if (outerRadius < 0.001) return 0.0;
        
        float distance = smoothstep(innerRadius, outerRadius, length(transformedPoint - center));
        return clamp(distance, 0.0, 1.0);
    }
    
    // Box brush - like radial but uses max distance in x or y direction
    if (brushType == 3) {
        vec2 center = brushParams.xy;
        vec2 halfSize = brushParams.zw;
        float radius = brushParams2.x;
        float feather = brushParams2.y;
        
        if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;
        
        // Calculate distance from center (normalized by half-size)
        vec2 q = abs(transformedPoint - center) - (halfSize - vec2(radius));
        
        // Distance field calculation for rounded rectangle
        //float dist = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
        float dist = min(max(q.x,q.y),0.0) + length(max(q,0.0)) - radius;
        
        return clamp((dist + feather * 0.5) / feather, 0.0, 1.0);
    }
    
    return 0.0;
}

// Determines whether a point is within the scissor region and returns the appropriate mask value
// p: The point to test against the scissor region
// Returns: 1.0 for points fully inside, 0.0 for points fully outside, and a gradient for edge transition
float scissorMask(vec2 p) {
    // Early exit if scissoring is disabled (when any scissor dimension is negative)
    if(scissorExt.x < 0.0 || scissorExt.y < 0.0) return 1.0;
    
    // Transform point to scissor space
    vec2 transformedPoint = (scissorMat * vec4(p, 0.0, 1.0)).xy;
    
    // Calculate signed distance from scissor edges (negative inside, positive outside)
    vec2 distanceFromEdges = abs(transformedPoint) - scissorExt;
    
    // Apply offset for smooth edge transition (0.5 creates half-pixel anti-aliased edges)
    vec2 smoothEdges = vec2(0.5, 0.5) - distanceFromEdges;
    
    // Clamp each component and multiply to get final mask value
    // Result is 1.0 inside, 0.0 outside, with smooth transition at edges
    return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
}

void main()
{
    vec2 pixelSize = fwidth(fragTexCoord);
    vec2 edgeDistance = min(fragTexCoord, 1.0 - fragTexCoord);
    float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * smoothstep(0.0, pixelSize.y, edgeDistance.y);
    edgeAlpha = clamp(edgeAlpha, 0.0, 1.0);
    
    float mask = scissorMask(fragPos);
    vec4 color = fragColor;

    // Apply brush if active
    if (brushType > 0) {
        float factor = calculateBrushFactor();
        color = mix(brushColor1, brushColor2, factor);
    }
    
    vec4 textureColor = texture(texture0, fragTexCoord);
    color *= textureColor;
    color *= edgeAlpha * mask;
    finalColor = color;
}";

        // Shader source for the vertex shader
        private const string DEFAULT_VERTEX_SHADER = @"#version 330
uniform mat4 projection;
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;

out vec2 fragTexCoord;
out vec4 fragColor;
out vec2 fragPos;

void main()
{
    fragTexCoord = aTexCoord;
    fragColor = aColor;
	fragPos = aPosition;
    gl_Position = projection * vec4(aPosition, 0.0, 1.0);
}";

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

        private Matrix4 _projection;
        private TextureTK _defaultTexture;

        /// <summary>
        /// Initialize the renderer with the window dimensions
        /// </summary>
        public void Initialize(int width, int height)
        {
            InitializeShaders();

            // Create OpenGL buffer objects
            _vertexArrayObject = GL.GenVertexArray();
            _vertexBufferObject = GL.GenBuffer();
            _elementBufferObject = GL.GenBuffer();

            // Set the default texture
            TextureTK texture = TextureTK.CreateNew(1, 1);
            byte[] pixelData = new byte[] { 255, 255, 255, 255 };
            texture.SetData(new IntRect(0, 0, 1, 1), pixelData);
            _defaultTexture = texture;

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
        }

        private Matrix4 ToTK(Matrix4x4 mat) => new Matrix4(
            (float)mat.M11, (float)mat.M12, (float)mat.M13, (float)mat.M14,
            (float)mat.M21, (float)mat.M22, (float)mat.M23, (float)mat.M24,
            (float)mat.M31, (float)mat.M32, (float)mat.M33, (float)mat.M34,
            (float)mat.M41, (float)mat.M42, (float)mat.M43, (float)mat.M44
        );

        private OpenTK.Mathematics.Vector4 ToTK(Prowl.Vector.Vector4 v) => new OpenTK.Mathematics.Vector4(
            (float)v.x, (float)v.y, (float)v.z, (float)v.w
        );

        private OpenTK.Mathematics.Vector4 ToTK(Color color) => new OpenTK.Mathematics.Vector4(
            color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f
        );

        public object CreateTexture(uint width, uint height)
        {
            return TextureTK.CreateNew(width, height);
        }

        public Vector2Int GetTextureSize(object texture)
        {
            if (texture is not TextureTK tkTexture)
                throw new ArgumentException("Invalid texture type");

            return new Vector2Int((int)tkTexture.Width, (int)tkTexture.Height);
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

            // Upload vertex data
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, canvas.Vertices.Count * Vertex.SizeInBytes, canvas.Vertices.ToArray(), BufferUsageHint.StreamDraw);

            // Set up vertex attributes
            // Position attribute
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, Vertex.SizeInBytes, 0);

            // TexCoord attribute
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, Vertex.SizeInBytes, 2 * sizeof(float));

            // Color attribute
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, Vertex.SizeInBytes, 4 * sizeof(float));

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

                // Set scissor rectangle
                drawCall.GetScissor(out var scissor, out var extent);
                var tkScissor = ToTK(scissor);
                GL.UniformMatrix4(_scissorMatLoc, false, ref tkScissor);
                GL.Uniform2(_scissorExtLoc, (float)extent.x, (float)extent.y);

                // Set brush parameters
                var brushMat = ToTK(drawCall.Brush.BrushMatrix);
                GL.UniformMatrix4(_brushMatLoc, false, ref brushMat);
                GL.Uniform1(_brushTypeLoc, (int)drawCall.Brush.Type);
                GL.Uniform4(_brushColor1Loc, ToTK(drawCall.Brush.Color1));
                GL.Uniform4(_brushColor2Loc, ToTK(drawCall.Brush.Color2));
                GL.Uniform4(_brushParamsLoc, (float)drawCall.Brush.Point1.x, (float)drawCall.Brush.Point1.y, (float)drawCall.Brush.Point2.x, (float)drawCall.Brush.Point2.y);
                GL.Uniform2(_brushParams2Loc, (float)drawCall.Brush.CornerRadii, (float)drawCall.Brush.Feather);

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
