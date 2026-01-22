using Silk.NET.OpenGL;
using Prowl.Quill;
using Prowl.Vector;
using System.Drawing;
using Prowl.Vector.Geometry;

namespace SilkExample
{
    /// <summary>
    /// Handles all OpenGL rendering logic for the vector graphics canvas using Silk.NET
    /// </summary>
    public class SilkNetRenderer : ICanvasRenderer, IDisposable
    {
        // Shader source for the fragment shader
        public const string FRAGMENT_SHADER_SOURCE = @"#version 330
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

uniform mat4 brushTextureMat;     // Texture transform matrix (inverse)
uniform int useWorldTextureCoords; // 0 = use vertex UVs, 1 = use world coords with transform

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
        float dist = min(max(q.x,q.y),0.0) + length(max(q,0.0)) - radius;

        return clamp((dist + feather * 0.5) / feather, 0.0, 1.0);
    }

    return 0.0;
}

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
    float mask = scissorMask(fragPos);

    vec4 color = fragColor;

    // Apply brush if active
    if (brushType > 0) {
        float factor = calculateBrushFactor();
        color = mix(brushColor1, brushColor2, factor);
    }

    // Text mode: UV >= 2.0 means text rendering - fast path
    if (fragTexCoord.x >= 2.0) {
        finalColor = color * texture(texture0, fragTexCoord - vec2(2.0)) * mask;
        return;
    }
    
    // Edge anti-aliasing based on distance to edges by abusing fwidth and UVs
    vec2 pixelSize = fwidth(fragTexCoord);
    vec2 edgeDistance = min(fragTexCoord, 1.0 - fragTexCoord);
    float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * smoothstep(0.0, pixelSize.y, edgeDistance.y);
    edgeAlpha = clamp(edgeAlpha, 0.0, 1.0);

    // Use world position transformed by texture matrix
    // If Canvas texture was null, renderer should assign a default white texture, so any sample position is valid
    finalColor = color * texture(texture0, (brushTextureMat * vec4(fragPos, 0.0, 1.0)).xy) * edgeAlpha * mask;
}";

        // Shader source for the vertex shader
        private const string VERTEX_SHADER_SOURCE = @"#version 330
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

        private readonly GL _gl;
        private uint _program;
        private uint _vao;
        private uint _vbo;
        private uint _ebo;
        private int _projectionLocation;
        private int _textureSamplerLocation;
        private int _scissorMatLocation;
        private int _scissorExtLocation;
        private int _brushMatLocation;
        private int _brushTypeLocation;
        private int _brushColor1Location;
        private int _brushColor2Location;
        private int _brushParamsLocation;
        private int _brushParams2Location;
        private int _brushTextureMatLocation;

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
            _scissorMatLocation = _gl.GetUniformLocation(_program, "scissorMat");
            _scissorExtLocation = _gl.GetUniformLocation(_program, "scissorExt");
            _brushMatLocation = _gl.GetUniformLocation(_program, "brushMat");
            _brushTypeLocation = _gl.GetUniformLocation(_program, "brushType");
            _brushColor1Location = _gl.GetUniformLocation(_program, "brushColor1");
            _brushColor2Location = _gl.GetUniformLocation(_program, "brushColor2");
            _brushParamsLocation = _gl.GetUniformLocation(_program, "brushParams");
            _brushParams2Location = _gl.GetUniformLocation(_program, "brushParams2");
            _brushTextureMatLocation = _gl.GetUniformLocation(_program, "brushTextureMat");
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

            // Define vertex attributes
            _gl.EnableVertexAttribArray(0); // Position
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)Vertex.SizeInBytes, (void*)0);

            _gl.EnableVertexAttribArray(1); // TexCoord
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, (uint)Vertex.SizeInBytes, (void*)(2 * sizeof(float)));

            _gl.EnableVertexAttribArray(2); // Color
            _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, (uint)Vertex.SizeInBytes, (void*)(4 * sizeof(float)));

            // Create element buffer object
            _ebo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

            // Unbind VAO so it's not accidentally modified
            _gl.BindVertexArray(0);
        }

        public void UpdateProjection(int width, int height)
        {
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
            //float* matrixPtr = stackalloc float[16];
            //matrixPtr[0] = matrix.M11;  matrixPtr[1] = matrix.M12;  matrixPtr[2] = matrix.M13;  matrixPtr[3] = matrix.M14;
            //matrixPtr[4] = matrix.M21;  matrixPtr[5] = matrix.M22;  matrixPtr[6] = matrix.M23;  matrixPtr[7] = matrix.M24;
            //matrixPtr[8] = matrix.M31;  matrixPtr[9] = matrix.M32;  matrixPtr[10] = matrix.M33; matrixPtr[11] = matrix.M34;
            //matrixPtr[12] = matrix.M41; matrixPtr[13] = matrix.M42; matrixPtr[14] = matrix.M43; matrixPtr[15] = matrix.M44;

            _gl.UniformMatrix4(location, 1, false, in matrix.c0.X);
        }


        public unsafe void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {
            if (drawCalls.Count == 0 || canvas.Vertices.Count == 0 || canvas.Indices.Count == 0)
                return;

            // Set up rendering state
            SetupRenderState();
    
            // Upload vertex and index data
            UploadGeometryData(canvas);
    
            // Process each draw call
            int indexOffset = 0;
            foreach (var drawCall in drawCalls)
            {
                ProcessDrawCall(drawCall, indexOffset);
                indexOffset += drawCall.ElementCount;
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
            _gl.Uniform1(_textureSamplerLocation, 0); // Use texture unit 0
        }
        
        private unsafe void UploadGeometryData(Canvas canvas)
        {
            // Upload vertices
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (Vertex* vertexPtr = canvas.Vertices.ToArray())
            {
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    (nuint)(canvas.Vertices.Count * Vertex.SizeInBytes),
                    vertexPtr,
                    BufferUsageARB.StreamDraw
                );
            }
    
            // Upload indices
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            fixed (uint* indexPtr = canvas.Indices.ToArray())
            {
                _gl.BufferData(
                    BufferTargetARB.ElementArrayBuffer,
                    (nuint)(canvas.Indices.Count * sizeof(uint)),
                    indexPtr,
                    BufferUsageARB.StreamDraw
                );
            }
        }
        
        private unsafe void ProcessDrawCall(DrawCall drawCall, int indexOffset)
        {
            // Bind texture
            TextureSilk texture = (drawCall.Texture as TextureSilk) ?? _defaultTexture;
            texture.Use(TextureUnit.Texture0);
    
            // Set scissor and brush uniforms
            drawCall.GetScissor(out var scissorMat, out var scissorExt);
            SetScissorUniforms(scissorMat, scissorExt);
            SetBrushUniforms(drawCall.Brush);
    
            // Draw the elements
            _gl.DrawElements(
                PrimitiveType.Triangles,
                (uint)drawCall.ElementCount,
                DrawElementsType.UnsignedInt,
                (void*)(indexOffset * sizeof(uint))
            );
        }

        private void SetProjectionMatrix()
        {
            SetMatrix4Uniform(_projectionLocation, _projection);
        }

        private void SetScissorUniforms(Prowl.Vector.Float4x4 matrix, Float2 extent)
        {
            SetMatrix4Uniform(_scissorMatLocation, matrix);
            _gl.Uniform2(_scissorExtLocation, (float)extent.X, (float)extent.Y);
        }

        private void SetBrushUniforms(Brush brush)
        {
            // Set brush matrix using the helper
            SetMatrix4Uniform(_brushMatLocation, brush.BrushMatrix);

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

            // Set texture transform parameters
            SetMatrix4Uniform(_brushTextureMatLocation, brush.TextureMatrix);
        }

        public void Cleanup()
        {
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteBuffer(_ebo);
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteProgram(_program);
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}