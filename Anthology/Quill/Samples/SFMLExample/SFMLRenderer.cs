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

uniform mat4 brushTextureMat;     // Texture transform matrix (inverse)

uniform float dpiScale;           // DPI scale factor (pixels / logical units)

// Backdrop blur
uniform sampler2D backdropTexture; // blurred copy of the scene behind the shape
uniform vec2 viewportSize;         // window size in pixels
uniform float backdropBlurAmount;  // > 0 when this fill is frosted glass
uniform int backdropFlipY;         // 1 to flip the backdrop sample vertically

varying vec2 v_position; // Add this

const float sdfPxRange = 4.0;
float sdfScreenPxRange(vec2 uv) {
    vec2 unitRange = vec2(sdfPxRange) / vec2(textureSize(texture0, 0));
    vec2 screenTexSize = vec2(1.0) / fwidth(uv);
    return max(0.5 * dot(unitRange, screenTexSize), 1.0);
}

float calculateBrushFactor(vec2 fragPos) {
    // No brush
    if (brushType == 0) return 0.0;

    // Convert fragPos from pixel coordinates to logical coordinates for brush calculations
    vec2 logicalPos = fragPos / dpiScale;
    vec2 transformedPoint = (brushMat * vec4(logicalPos, 0.0, 1.0)).xy;

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
    // Early exit if scissoring is disabled (when any scissor dimension is negative)
    if(scissorExt.x < 0.0 || scissorExt.y < 0.0) return 1.0;

    // Convert from pixel to logical coordinates, then transform to scissor space
    vec2 logicalP = p / dpiScale;
    vec2 transformedPoint = (scissorMat * vec4(logicalP, 0.0, 1.0)).xy;

    // Convert scissorExt from pixels to logical units to match transformedPoint
    vec2 logicalExt = scissorExt / dpiScale;

    // Calculate signed distance from scissor edges (negative inside, positive outside)
    vec2 distanceFromEdges = abs(transformedPoint) - logicalExt;

    // Apply offset for smooth edge transition (0.5 pixels converted to logical units)
    float halfPixelLogical = 0.5 / dpiScale;
    vec2 smoothEdges = vec2(halfPixelLogical) - distanceFromEdges;

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

    float mask = scissorMask(fragPos);

    vec4 color = fragColor;

    // Apply brush if active
    if (brushType > 0) {
        float factor = calculateBrushFactor(fragPos);
        color = mix(brushColor1, brushColor2, factor);
    }
    
    // Text mode: UV >= 2.0 means text rendering - fast path
    if (fragTexCoord.x >= 2.0) {
        vec2 uv = fragTexCoord - vec2(2.0, 2.0);
        float sd = texture(texture0, uv).r;
        float screenPxDistance = sdfScreenPxRange(uv) * (sd - 0.5);
        float coverage = clamp(screenPxDistance + 0.5, 0.0, 1.0);
        gl_FragColor = color * coverage * mask;
        return;
    }
    
    // Edge anti-aliasing: coverage is baked into the geometry (fringe vertices) and carried in
    // fragTexCoord.x (1 = solid core, 0 = outer fringe edge).
    float edgeAlpha = clamp(fragTexCoord.x, 0.0, 1.0);
    
    // Use world position transformed by texture matrix (convert to logical coords first)
    // If Canvas texture was null, renderer should assign a default white texture, so any sample position is valid
    vec2 logicalPos = fragPos / dpiScale;
    vec4 fill = color * texture(texture0, (brushTextureMat * vec4(logicalPos, 0.0, 1.0)).xy);

    // Backdrop blur: composite the fill over the blurred scene behind the shape.
    if (backdropBlurAmount > 0.0) {
        vec2 uv = fragPos / viewportSize;
        if (backdropFlipY == 1) uv.y = 1.0 - uv.y;
        vec3 blurred = texture(backdropTexture, uv).rgb;
        vec3 outRgb = blurred * (1.0 - fill.a) + fill.rgb;  // fill is premultiplied
        gl_FragColor = vec4(outRgb, 1.0) * edgeAlpha * mask;
        return;
    }

    gl_FragColor = fill * edgeAlpha * mask;
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

        // Dual Kawase blur passes (fragment-only; SFML's default vertex pipeline provides
        // normalized gl_TexCoord[0]). 'texture' is the source being sampled.
        private const string BLUR_DOWN_FS = @"
uniform sampler2D texture;
uniform vec2 halfpixel;
uniform float offset;
void main()
{
    vec2 uv = gl_TexCoord[0].xy;
    vec4 sum = texture2D(texture, uv) * 4.0;
    sum += texture2D(texture, uv - halfpixel.xy * offset);
    sum += texture2D(texture, uv + halfpixel.xy * offset);
    sum += texture2D(texture, uv + vec2(halfpixel.x, -halfpixel.y) * offset);
    sum += texture2D(texture, uv - vec2(halfpixel.x, -halfpixel.y) * offset);
    gl_FragColor = sum / 8.0;
}";

        private const string BLUR_UP_FS = @"
uniform sampler2D texture;
uniform vec2 halfpixel;
uniform float offset;
void main()
{
    vec2 uv = gl_TexCoord[0].xy;
    vec4 sum = texture2D(texture, uv + vec2(-halfpixel.x * 2.0, 0.0) * offset);
    sum += texture2D(texture, uv + vec2(-halfpixel.x, halfpixel.y) * offset) * 2.0;
    sum += texture2D(texture, uv + vec2(0.0, halfpixel.y * 2.0) * offset);
    sum += texture2D(texture, uv + vec2(halfpixel.x, halfpixel.y) * offset) * 2.0;
    sum += texture2D(texture, uv + vec2(halfpixel.x * 2.0, 0.0) * offset);
    sum += texture2D(texture, uv + vec2(halfpixel.x, -halfpixel.y) * offset) * 2.0;
    sum += texture2D(texture, uv + vec2(0.0, -halfpixel.y * 2.0) * offset);
    sum += texture2D(texture, uv + vec2(-halfpixel.x, -halfpixel.y) * offset) * 2.0;
    gl_FragColor = sum / 12.0;
}";

        // Backdrop blur
        public bool SupportsBackdropBlur => true;
        // If the frosted glass appears vertically mirrored, flip this to 0.
        private const int BackdropFlipY = 1;
        private const int MaxBlurLevels = 6;
        private Shader _blurDown;
        private Shader _blurUp;
        private Texture _captureTex;
        private RenderTexture[] _blurLevels = new RenderTexture[MaxBlurLevels];
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
                _shader = Shader.FromString(VERTEX_SHADER, null, FRAGMENT_SHADER);
                _shader.SetUniform("texture0", Shader.CurrentTexture);

                _blurDown = Shader.FromString(null, null, BLUR_DOWN_FS);
                _blurUp = Shader.FromString(null, null, BLUR_UP_FS);
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

            // Draw all draw calls in the canvas
            for (int i = 0; i < drawCalls.Count; i++)
            {
                var drawCall = drawCalls[i];

                // Backdrop blur: capture the window so far and blur it before drawing this shape.
                if (drawCall.Brush.BackdropBlur > 0f && Shader.IsAvailable)
                {
                    EnsureBlurTargets(_fbWidth, _fbHeight);
                    _captureTex.Update(_window);
                    RenderBackdropBlur((float)drawCall.Brush.BackdropBlur);
                }

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
                        _shader.SetUniform("dpiScale", (float)canvas.FramebufferScale);

                        // Get scissor parameters - this is crucial for the scissor to work
                        drawCall.GetScissor(out var scissor, out var extent);

                        // Convert and set the scissor matrix
                        Mat4 scissorMat = ToMat4(scissor);
                        _shader.SetUniform("scissorMat", scissorMat);

                        // Set the scissor extent
                        _shader.SetUniform("scissorExt", new Vec2((float)extent.X, (float)extent.Y));

                        // Set brush parameters
                        _shader.SetUniform("brushMat", ToMat4(drawCall.Brush.BrushMatrix));
                        _shader.SetUniform("brushType", (int)drawCall.Brush.Type);
                        _shader.SetUniform("brushColor1", ToVec4(drawCall.Brush.Color1));
                        _shader.SetUniform("brushColor2", ToVec4(drawCall.Brush.Color2));
                        _shader.SetUniform("brushParams", new Vec4(
                            (float)drawCall.Brush.Point1.X, (float)drawCall.Brush.Point1.Y,
                            (float)drawCall.Brush.Point2.X, (float)drawCall.Brush.Point2.Y));
                        _shader.SetUniform("brushParams2", new Vec2(
                            (float)drawCall.Brush.CornerRadii, (float)drawCall.Brush.Feather));

                        // Set texture transform parameters
                        _shader.SetUniform("brushTextureMat", ToMat4(drawCall.Brush.TextureMatrix));

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