using Common;
using Prowl.Quill;
using Prowl.Vector;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace RaylibExample
{
    public class Program
    {
        public static string Stroke_FS = @"
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
in vec2 fragPos;
out vec4 finalColor;

uniform sampler2D texture0;
uniform mat4 scissorMat;
uniform vec2 scissorExt;

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
    vec2 pixelSize = fwidth(fragTexCoord);
    vec2 edgeDistance = min(fragTexCoord, 1.0 - fragTexCoord);
    float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * smoothstep(0.0, pixelSize.y, edgeDistance.y);
    
    float mask = scissorMask(fragPos);
    vec4 color = fragColor;
    color *= texture(texture0, fragTexCoord);
    
    finalColor = vec4(color.rgb, color.a * edgeAlpha * mask);
}";

        public static string Vertex_VS = @"
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

        static void Main(string[] args)
        {
            // Initialize window
            int screenWidth = 1280;
            int screenHeight = 720;
            SetConfigFlags(ConfigFlags.ResizableWindow);
            InitWindow(screenWidth, screenHeight, "Raylib Quill Example");
            SetTargetFPS(60);

            // Load textures
            Texture2D whiteTexture = LoadTexture("Textures/white.png");
            Texture2D demoTexture = LoadTexture("Textures/wall.png");

            // Load shader with scissoring support
            Shader shader = LoadShaderFromMemory(Vertex_VS, Stroke_FS);
            int scissorMatLoc = GetShaderLocation(shader, "scissorMat");
            int scissorExtLoc = GetShaderLocation(shader, "scissorExt");

            Canvas canvas = new Canvas();
            CanvasDemo demo = new CanvasDemo(canvas, screenWidth, screenHeight, demoTexture);
            Vector2 offset = Vector2.zero;
            float zoom = 1.0f;
            float rotation = 0.0f;

            while (!WindowShouldClose())
            {
                canvas.Clear(); // Reset Canvas

                // Handle input
                if (IsMouseButtonDown(MouseButton.Left))
                {
                    System.Numerics.Vector2 delta = GetMouseDelta();
                    offset.x += delta.X * (1.0f / zoom);
                    offset.y += delta.Y * (1.0f / zoom);
                }

                if (GetMouseWheelMove() != 0)
                {
                    zoom += GetMouseWheelMove() * 0.1f;
                    if (zoom < 0.1f) zoom = 0.1f;
                }

                if (IsKeyDown(KeyboardKey.Q)) rotation += 10.0f * GetFrameTime();
                if (IsKeyDown(KeyboardKey.E)) rotation -= 10.0f * GetFrameTime();

                // Update demo
                demo.RenderFrame(GetFrameTime(), offset, zoom, rotation);

                // Draw Canvas
                BeginDrawing();
                ClearBackground(Color.Black);
                BeginBlendMode(BlendMode.Alpha);

                BeginShaderMode(shader);
                DrawCanvas(canvas, shader, scissorMatLoc, scissorExtLoc, whiteTexture);
                EndShaderMode();

                DrawFPS(10, 10);
                EndDrawing();
            }

            UnloadTexture(whiteTexture);
            UnloadTexture(demoTexture);
            UnloadShader(shader);
            CloseWindow();
        }

        static void DrawCanvas(Canvas canvas, Shader shader, int scissorMatLoc, int scissorExtLoc, Texture2D defaultTexture)
        {
            Rlgl.DrawRenderBatchActive();

            int index = 0;

            foreach (var drawCall in canvas.DrawCalls)
            {
                // Bind the texture if available, otherwise use default
                uint textureToUse = 0;
                if (drawCall.Texture != null)
                    textureToUse = ((Texture2D)drawCall.Texture).Id;

                // Set scissor rectangle
                drawCall.GetScissor(out var scissor, out var extent);
                scissor = Matrix4x4.Transpose(scissor);

                // Draw the vertices for this draw call
                Rlgl.Begin(DrawMode.Triangles);
                Rlgl.SetTexture(textureToUse);

                SetShaderValueMatrix(shader, scissorMatLoc, scissor.ToFloat());
                SetShaderValue(shader, scissorExtLoc, [(float)extent.x, (float)extent.y], ShaderUniformDataType.Vec2);

                for (int i = 0; i < drawCall.ElementCount; i += 3)
                {
                    if (Rlgl.CheckRenderBatchLimit(3))
                    {
                        Rlgl.Begin(DrawMode.Triangles);
                        Rlgl.SetTexture(textureToUse);

                        SetShaderValueMatrix(shader, scissorMatLoc, scissor.ToFloat());
                        SetShaderValue(shader, scissorExtLoc, [(float)extent.x, (float)extent.y], ShaderUniformDataType.Vec2);
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
        }
    }
}