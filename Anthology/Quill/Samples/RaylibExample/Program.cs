using Common;
using Prowl.Quill;
using Prowl.Vector;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace RaylibExample
{
    public class Program
    {
        static Vector2 offset = Vector2.zero;
        static float zoom = 1.0f;
        static float rotation = 0.0f;

        static void Main(string[] args)
        {
            // Initialize window
            int screenWidth = 1280;
            int screenHeight = 720;
            SetConfigFlags(ConfigFlags.ResizableWindow);
            InitWindow(screenWidth, screenHeight, "Raylib Quill Example");
            SetTargetFPS(60);

            CanvasRenderer.Initialize();

            // Load textures
            Texture2D demoTexture = LoadTexture("Textures/wall.png");

            Canvas canvas = new Canvas();
            CanvasDemo demo = new CanvasDemo(canvas, screenWidth, screenHeight, demoTexture);

            while (!WindowShouldClose())
            {
                HandleDemoInput(ref offset, ref zoom, ref rotation);

                // Reset Canvas
                canvas.Clear(); 

                // Draw demo into canvas
                demo.RenderFrame(GetFrameTime(), offset, zoom, rotation);

                // Draw Canvas
                BeginDrawing();
                ClearBackground(Color.Black);
                CanvasRenderer.Render(canvas);
                EndDrawing();
            }

            UnloadTexture(demoTexture);
            CanvasRenderer.Dispose();
            CloseWindow();
        }

        private static void HandleDemoInput(ref Vector2 offset, ref float zoom, ref float rotation)
        {

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
        }
    }
}