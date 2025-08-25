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

        // Shader sources directly corresponding to the OpenGL shaders
        private const string FRAGMENT_SHADER = @"
uniform sampler2D texture0;
uniform mat4 scissorMat;
uniform vec2 scissorExt;

uniform mat4 brushMat;
uniform int brushType;       // 0=none, 1=linear, 2=radial, 3=box
uniform vec4 brushColor1;    // Start color
uniform vec4 brushColor2;    // End color
uniform vec4 brushParams;    // x,y = start point, z,w = end point (or center+radius for radial)
uniform vec2 brushParams2;   // x = Box radius, y = Box Feather

varying vec2 v_position; // Add this

float calculateBrushFactor(vec2 fragPos) {
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

// Determines whether a point is within the scissor region and returns the appropriate mask value
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
    return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
}

void main()
{
    // In SFML, gl_TexCoord[0].xy contains texture coordinates
    vec2 fragTexCoord = gl_TexCoord[0].xy;
    // We'll pass position in a custom vertex attribute
    vec2 fragPos = v_position; // Use this instead of gl_TexCoord[0].zw
    // Color comes from vertex color
    vec4 fragColor = gl_Color;
    
    vec2 pixelSize = fwidth(fragTexCoord);
    vec2 edgeDistance = min(fragTexCoord, 1.0 - fragTexCoord);
    float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * smoothstep(0.0, pixelSize.y, edgeDistance.y);
    edgeAlpha = clamp(edgeAlpha, 0.0, 1.0);
    
    float mask = scissorMask(fragPos);
    vec4 color = fragColor;

    // Apply brush if active
    if (brushType > 0) {
        float factor = calculateBrushFactor(fragPos);
        color = mix(brushColor1, brushColor2, factor);
    }
    
    vec4 textureColor = texture2D(texture0, fragTexCoord);
    color *= textureColor;
    
    color *= edgeAlpha * mask;
    
    gl_FragColor = color;
}";

        private const string VERTEX_SHADER = @"
uniform mat4 projection;
varying vec2 v_position; // Make sure this is declared

void main()
{
    // Pass color and texture coordinates to fragment shader
    gl_FrontColor = gl_Color;
    gl_TexCoord[0] = gl_MultiTexCoord0;
    
    // Pass position to fragment shader as varying variable
    v_position = gl_Vertex.xy; // This correctly sets the varying variable
    
    // Apply projection matrix to position
    gl_Position = projection * gl_Vertex;
}";

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
                _shader = Shader.FromString(VERTEX_SHADER, null, FRAGMENT_SHADER);
                _shader.SetUniform("texture0", Shader.CurrentTexture);
            }

            UpdateProjection(width, height);
        }

        /// <summary>
        /// Update the projection matrix when the window is resized
        /// </summary>
        public void UpdateProjection(int width, int height)
        {
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

        public Vector2Int GetTextureSize(object texture)
        {
            if (texture is not TextureSFML sfmlTexture)
                throw new ArgumentException("Invalid texture type");

            return new Vector2Int((int)sfmlTexture.Width, (int)sfmlTexture.Height);
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

        private static Mat4 ToMat4(Matrix4x4 mat)
        {
            // Try transposing the matrix when converting
            // This swaps the row-major and column-major formats
            return new Mat4(
                (float)mat.M11, (float)mat.M21, (float)mat.M31, (float)mat.M41,
                (float)mat.M12, (float)mat.M22, (float)mat.M32, (float)mat.M42,
                (float)mat.M13, (float)mat.M23, (float)mat.M33, (float)mat.M43,
                (float)mat.M14, (float)mat.M24, (float)mat.M34, (float)mat.M44
            );
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

            // Draw all draw calls in the canvas
            for (int i = 0; i < drawCalls.Count; i++)
            {
                var drawCall = drawCalls[i];

                // Get texture to use
                Texture texture = (drawCall.Texture as TextureSFML)?.Handle ?? _defaultTexture;

                // Calculate start index
                int indexOffset = 0;
                for (int j = 0; j < i; j++)
                {
                    indexOffset += drawCalls[j].ElementCount;
                }

                // Create vertex array for this draw call
                _vertexArray.Clear();

                // Create vertices for this draw call
                for (int j = 0; j < drawCall.ElementCount; j++)
                {
                    int idx = (int)canvas.Indices[indexOffset + j];
                    var vertex = canvas.Vertices[idx];

                    SFML.Graphics.Vertex sfmlVertex = new(
                        new Vector2f((float)vertex.Position.x, (float)vertex.Position.y),
                        new SFML.Graphics.Color(vertex.Color.R, vertex.Color.G, vertex.Color.B, vertex.Color.A),
                        new Vector2f((float)vertex.UV.x, (float)vertex.UV.y)
                    );

                    _vertexArray.Append(sfmlVertex);
                }

                // Set shader parameters for this draw call
                if (Shader.IsAvailable && _shader != null)
                {
                    try
                    {
                        // Get scissor parameters - this is crucial for the scissor to work
                        drawCall.GetScissor(out var scissor, out var extent);

                        // Convert and set the scissor matrix
                        Mat4 scissorMat = ToMat4(scissor);
                        _shader.SetUniform("scissorMat", scissorMat);

                        // Set the scissor extent
                        _shader.SetUniform("scissorExt", new Vec2((float)extent.x, (float)extent.y));

                        // Set brush parameters
                        _shader.SetUniform("brushMat", ToMat4(drawCall.Brush.BrushMatrix));
                        _shader.SetUniform("brushType", (int)drawCall.Brush.Type);
                        _shader.SetUniform("brushColor1", ToVec4(drawCall.Brush.Color1));
                        _shader.SetUniform("brushColor2", ToVec4(drawCall.Brush.Color2));
                        _shader.SetUniform("brushParams", new Vec4(
                            (float)drawCall.Brush.Point1.x, (float)drawCall.Brush.Point1.y,
                            (float)drawCall.Brush.Point2.x, (float)drawCall.Brush.Point2.y));
                        _shader.SetUniform("brushParams2", new Vec2(
                            (float)drawCall.Brush.CornerRadii, (float)drawCall.Brush.Feather));
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
                    (Shader.IsAvailable && _shader != null) ? _shader : null
                );

                _window.Draw(_vertexArray, states);
            }
        }

        public void Dispose()
        {
            _shader?.Dispose();
            _vertexArray?.Dispose();
            _vertexBuffer?.Dispose();
        }
    }
}