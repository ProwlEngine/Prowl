using Common;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Scribe.Internal;
using Prowl.Vector;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace RaylibExample
{
    public class Program
    {
        static Float2 offset = Float2.Zero;
        static float zoom = 1.0f;
        static float rotation = 0.0f;

        static FontFile RobotoFont;
        static FontFile AlamakFont;

        static void Main(string[] args)
        {
            // Initialize window with high-DPI support
            int screenWidth = 1280;
            int screenHeight = 720;
            SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.HighDpiWindow);
            InitWindow(screenWidth, screenHeight, "Raylib Quill Example");
            SetTargetFPS(60);

            var renderer = new RaylibCanvasRenderer();

            // Load textures
            Texture2D demoTexture = LoadTexture("Textures/wall.png");

            Canvas canvas = new Canvas(renderer, new FontAtlasSettings());

            RobotoFont = new FontFile("Fonts/Roboto.ttf");
            AlamakFont = new FontFile("Fonts/Alamak.ttf");

            var demos = new List<IDemo>
            {
                new CanvasDemo(canvas, screenWidth, screenHeight, demoTexture, RobotoFont, AlamakFont),
                new SVGDemo(canvas, screenWidth, screenHeight),
                new BenchmarkScene(canvas, RobotoFont, screenWidth, screenHeight)
            };

            int currentDemoIndex = 0;

            // In your render loop
            while (!WindowShouldClose())
            {
                HandleDemoInput(ref offset, ref zoom, ref rotation, ref currentDemoIndex, demos.Count);

                float dpiScale = (float)GetRenderWidth() / GetScreenWidth();

                // Begin frame with logical size and DPI scale - this also clears the canvas
                canvas.BeginFrame(GetScreenWidth(), GetScreenHeight(), dpiScale);

                // Draw demo into canvas
                demos[currentDemoIndex].RenderFrame(GetFrameTime(), offset, zoom, rotation);

                // Draw Canvas
                BeginDrawing();
                ClearBackground(Raylib_cs.Color.Black);

                canvas.Render();

                EndDrawing();
            }

            UnloadTexture(demoTexture);
            canvas.Dispose();
            CloseWindow();
        }

        private static void HandleDemoInput(ref Float2 offset, ref float zoom, ref float rotation, ref int currentDemoIndex, int demoCount)
        {
            // Handle input
            if (IsMouseButtonDown(MouseButton.Left))
            {
                Float2 delta = GetMouseDelta();
                offset.X += delta.X * (1.0f / zoom);
                offset.Y += delta.Y * (1.0f / zoom);
            }

            if (GetMouseWheelMove() != 0)
            {
                zoom += GetMouseWheelMove() * 0.1f;
                if (zoom < 0.1f) zoom = 0.1f;
            }

            if (IsKeyDown(KeyboardKey.Q)) rotation += 10.0f * GetFrameTime();
            if (IsKeyDown(KeyboardKey.E)) rotation -= 10.0f * GetFrameTime();

            if (IsKeyPressed(KeyboardKey.Left))
                currentDemoIndex = currentDemoIndex - 1 < 0 ? demoCount - 1 : currentDemoIndex - 1;
            if (IsKeyPressed(KeyboardKey.Right))
                currentDemoIndex = currentDemoIndex + 1 == demoCount ? 0 : currentDemoIndex + 1;
        }
    }
}