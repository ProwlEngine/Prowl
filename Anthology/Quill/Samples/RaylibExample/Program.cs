using Common;
using FontStashSharp;
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

        static SpriteFontBase RobotoFont32;
        static SpriteFontBase RobotoFont16;
        static SpriteFontBase AlamakFont32;

        static void Main(string[] args)
        {
            // Initialize window
            int screenWidth = 1280;
            int screenHeight = 720;
            SetConfigFlags(ConfigFlags.ResizableWindow);
            InitWindow(screenWidth, screenHeight, "Raylib Quill Example");
            SetTargetFPS(60);

            var renderer = new RaylibCanvasRenderer();

            // Load textures
            Texture2D demoTexture = LoadTexture("Textures/wall.png");
            FontSystem fonts = new FontSystem();
            using(var stream = File.OpenRead("Fonts/Roboto.ttf"))
            {
                fonts.AddFont(stream);
                RobotoFont32 = fonts.GetFont(32);
                RobotoFont16 = fonts.GetFont(16);
            }
            fonts = new FontSystem();
            using (var stream = File.OpenRead("Fonts/Alamak.ttf"))
            {
                fonts.AddFont(stream);
                AlamakFont32 = fonts.GetFont(32);
            }

            Canvas canvas = new Canvas(renderer);

            var demos = new List<IDemo>
            {
                new CanvasDemo(canvas, screenWidth, screenHeight, demoTexture, RobotoFont32, RobotoFont16, AlamakFont32),
                new SVGDemo(canvas, screenWidth, screenHeight),
                new BenchmarkScene(canvas, RobotoFont16, screenWidth, screenHeight)
            };


            int currentDemoIndex = 0;

            // In your render loop
            while (!WindowShouldClose())
            {
                HandleDemoInput(ref offset, ref zoom, ref rotation, ref currentDemoIndex, demos.Count);
                screenWidth = GetScreenWidth();
                screenHeight = GetScreenHeight();

                // Reset Canvas
                canvas.Clear();

                // Draw demo into canvas
                demos[currentDemoIndex].RenderFrame(GetFrameTime(), offset, zoom, rotation);

                // Draw Canvas
                BeginDrawing();
                ClearBackground(Color.Black);

                canvas.Render();

                EndDrawing();
            }

            UnloadTexture(demoTexture);
            canvas.Dispose();
            CloseWindow();
        }

        private static void HandleDemoInput(ref Vector2 offset, ref float zoom, ref float rotation, ref int currentDemoIndex, int demoCount)
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

            if (IsKeyPressed(KeyboardKey.Left))
                currentDemoIndex = currentDemoIndex - 1 < 0 ? demoCount - 1 : currentDemoIndex - 1;
            if (IsKeyPressed(KeyboardKey.Right))
                currentDemoIndex = currentDemoIndex + 1 == demoCount ? 0 : currentDemoIndex + 1;
        }
    }
}