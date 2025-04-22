using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Prowl.Quill;
using Prowl.Vector;

namespace OpenTKExample
{
    /// <summary>
    /// Handles all OpenGL rendering logic for the vector graphics canvas
    /// </summary>
    public class CanvasRenderer
    {
        // Shader source for the fragment shader - handles edge anti-aliasing
        private const string STROKE_FRAGMENT_SHADER = @"
#version 330
uniform sampler2D textureSampler;

uniform mat4 scissorMat;
uniform vec2 scissorExt;

in vec2 fragTexCoord;
in vec4 fragColor;
in vec2 fragPos;
out vec4 finalColor;

// Determines whether a point is within the scissor region and returns the appropriate mask value
// p: The point to test against the scissor region
// Returns: 1.0 for points fully inside, 0.0 for points fully outside, and a gradient for edge transition
float scissorMask(vec2 p) {
    // Early exit if scissoring is disabled (when scissorExt.x is negative or zero)
    if(scissorExt.x <= 0.0) return 1.0;
    
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
    // Calculate pixel size for anti-aliasing
    vec2 pixelSize = fwidth(fragTexCoord);
    
    // Determine distance from edge
    vec2 edgeDistance = min(fragTexCoord, 1.0 - fragTexCoord);
    
    // Create smooth edge transition
    float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * 
                      smoothstep(0.0, pixelSize.y, edgeDistance.y);
    
    // Apply scissor mask
    float mask = scissorMask(fragPos);

    // This is a simple rectangle scissor
    vec4 color = fragColor;
    color *= texture(textureSampler, fragTexCoord);

    // Apply alpha for smooth edges
    finalColor = vec4(color.rgb, color.a * edgeAlpha * mask);
}";

        // Shader source for the vertex shader - transforms vertices
        private const string DEFAULT_VERTEX_SHADER = @"
#version 330
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
        private int _scissorScaleLoc = 0;
        private Matrix4 _projection;
        private TextureTK _defaultTexture;

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
        /// Render the canvas to the screen
        /// </summary>
        public void RenderCanvas(Canvas canvas)
        {
            // Skip if canvas is empty
            if (canvas.DrawCalls.Count == 0)
                return;

            // Configure OpenGL state
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Use shader and set projection
            GL.UseProgram(_shaderProgram);
            GL.UniformMatrix4(_projectionLocation, false, ref _projection);

            // Bind vertex array
            GL.BindVertexArray(_vertexArrayObject);

            // Upload vertex data
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, canvas.Vertices.Count * ProwlCanvasVertex.SizeInBytes, canvas.Vertices.ToArray(), BufferUsageHint.StreamDraw);

            // Set up vertex attributes
            // Position attribute
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, ProwlCanvasVertex.SizeInBytes, 0);

            // TexCoord attribute
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, ProwlCanvasVertex.SizeInBytes, 2 * sizeof(float));

            // Color attribute
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, ProwlCanvasVertex.SizeInBytes, 4 * sizeof(float));

            // Upload index data
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, canvas.Indices.Count * sizeof(uint), canvas.Indices.ToArray(), BufferUsageHint.StreamDraw);

            // Active texture unit for sampling
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(_textureSamplerLocation, 0); // texture unit 0

            // Draw all draw calls in the canvas
            int indexOffset = 0;
            foreach (var drawCall in canvas.DrawCalls)
            {
                // Handle texture binding
                (drawCall.Texture as TextureTK ?? _defaultTexture).Use(TextureUnit.Texture0);

                // Set scissor rectangle
                drawCall.GetScissor(out var scissor, out var extent);
                var tkScissor = ToTK(scissor);
                GL.UniformMatrix4(_scissorMatLoc, false, ref tkScissor);
                GL.Uniform2(_scissorExtLoc, (float)extent.x, (float)extent.y);

                GL.DrawElements(PrimitiveType.Triangles, drawCall.ElementCount, DrawElementsType.UnsignedInt, indexOffset * sizeof(uint));
                indexOffset += drawCall.ElementCount;
            }

            // Clean up
            GL.BindVertexArray(0);
        }

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
            _scissorScaleLoc = GL.GetUniformLocation(_shaderProgram, "scissorScale");
        }

        private Matrix4 ToTK(Matrix4x4 mat) => new Matrix4(
            (float)mat.M11, (float)mat.M12, (float)mat.M13, (float)mat.M14,
            (float)mat.M21, (float)mat.M22, (float)mat.M23, (float)mat.M24,
            (float)mat.M31, (float)mat.M32, (float)mat.M33, (float)mat.M34,
            (float)mat.M41, (float)mat.M42, (float)mat.M43, (float)mat.M44
        );
    }
}