using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace RaylibExample
{
    public class RaylibCanvasRenderer : ICanvasRenderer
    {
        public const string Stroke_FS = @"
#version 330
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

        public const string Vertex_VS = @"
#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;

uniform mat4 mvp;

out vec2 fragTexCoord;
out vec4 fragColor;
out vec2 fragPos;

void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    fragPos = vertexPosition.xy;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}";
        
        Shader shader;
        int scissorMatLoc;
        int scissorExtLoc;

        int _brushMatLoc;
        int _brushTypeLoc;
        int _brushColor1Loc;
        int _brushColor2Loc;
        int _brushParamsLoc;
        int _brushParams2Loc;
        int _brushTextureMatLoc;

        public RaylibCanvasRenderer()
        {
            // Load shader with scissoring support
            shader = LoadShaderFromMemory(Vertex_VS, Stroke_FS);
            scissorMatLoc = GetShaderLocation(shader, "scissorMat");
            scissorExtLoc = GetShaderLocation(shader, "scissorExt");

            _brushMatLoc = GetShaderLocation(shader, "brushMat");
            _brushTypeLoc = GetShaderLocation(shader, "brushType");
            _brushColor1Loc = GetShaderLocation(shader, "brushColor1");
            _brushColor2Loc = GetShaderLocation(shader, "brushColor2");
            _brushParamsLoc = GetShaderLocation(shader, "brushParams");
            _brushParams2Loc = GetShaderLocation(shader, "brushParams2");
            _brushTextureMatLoc = GetShaderLocation(shader, "brushTextureMat");
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

        void SetUniforms(Prowl.Quill.DrawCall drawCall)
        {
            // Bind the texture if available, otherwise use default
            uint textureToUse = 0;
            if (drawCall.Texture != null)
                textureToUse = ((Texture2D)drawCall.Texture).Id;

            Rlgl.SetTexture(textureToUse);

            // Set scissor rectangle
            drawCall.GetScissor(out var scissor, out var extent);
            //scissor = Float4x4.Transpose(scissor);

            SetShaderValueMatrix(shader, scissorMatLoc, scissor);
            SetShaderValue(shader, scissorExtLoc, [(float)extent.X, (float)extent.Y], ShaderUniformDataType.Vec2);

            // Set gradient parameters
            SetShaderValue(shader, _brushTypeLoc, (int)drawCall.Brush.Type, ShaderUniformDataType.Int);
            if (drawCall.Brush.Type != BrushType.None)
            {
                //var brushMat = Float4x4.Transpose(drawCall.Brush.BrushMatrix);
                SetShaderValueMatrix(shader, _brushMatLoc, drawCall.Brush.BrushMatrix);
                var brcol1 = (Prowl.Vector.Color)drawCall.Brush.Color1;
                var brcol2 = (Prowl.Vector.Color)drawCall.Brush.Color2;
                SetShaderValue(shader, _brushColor1Loc, brcol1, ShaderUniformDataType.Vec4);
                SetShaderValue(shader, _brushColor2Loc, brcol2, ShaderUniformDataType.Vec4);
                SetShaderValue(shader, _brushParamsLoc, new Float4((float)drawCall.Brush.Point1.X, (float)drawCall.Brush.Point1.Y, (float)drawCall.Brush.Point2.X, (float)drawCall.Brush.Point2.Y), ShaderUniformDataType.Vec4);
                SetShaderValue(shader, _brushParams2Loc, new Float2((float)drawCall.Brush.CornerRadii, (float)drawCall.Brush.Feather), ShaderUniformDataType.Vec2);
            }

            // Set texture transform parameters
            SetShaderValueMatrix(shader, _brushTextureMatLoc, drawCall.Brush.TextureMatrix);
        }

        public void RenderCalls(Canvas canvas, IReadOnlyList<Prowl.Quill.DrawCall> drawCalls)
        {
            BeginBlendMode(BlendMode.AlphaPremultiply);
            BeginShaderMode(shader);

            Rlgl.DrawRenderBatchActive();

            int index = 0;

            foreach (var drawCall in canvas.DrawCalls)
            {

                // Draw the vertices for this draw call
                Rlgl.Begin(DrawMode.Triangles);
                SetUniforms(drawCall);

                for (int i = 0; i < drawCall.ElementCount; i += 3)
                {
                    if (Rlgl.CheckRenderBatchLimit(3))
                    {
                        Rlgl.Begin(DrawMode.Triangles);
                        SetUniforms(drawCall);
                    }

                    var a = canvas.Vertices[(int)canvas.Indices[index]];
                    var b = canvas.Vertices[(int)canvas.Indices[index + 1]];
                    var c = canvas.Vertices[(int)canvas.Indices[index + 2]];

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
            }
            Rlgl.SetTexture(0);

            EndShaderMode();
        }

        static System.Numerics.Vector4 ToVec4(System.Drawing.Color color) => new System.Numerics.Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

        public void Dispose() => UnloadShader(shader);
    }
}
