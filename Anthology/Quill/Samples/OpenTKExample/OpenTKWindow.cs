using Common;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Prowl.Quill;
using Prowl.Vector;
using StbImageSharp;

namespace OpenTKExample
{
    /// <summary>
    /// Window class that handles the application lifecycle and user input
    /// </summary>
    public class OpenTKWindow : GameWindow
    {
        // Canvas and demo
        private Canvas _canvas;
        private CanvasDemo _demo;
        private CanvasRenderer _renderer;

        // Camera/view properties
        private Vector2 _offset = Vector2.zero;
        private double _zoom = 1.0f;
        private double _rotation = 0.0f;

        private TextureTK _whiteTexture;
        private TextureTK _demoTexture;

        public OpenTKWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings) : base(gameWindowSettings, nativeWindowSettings)
        {
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            // Create the Demo's Texture
            _demoTexture = TextureTK.LoadFromFile("Textures/wall.png");
            _whiteTexture = TextureTK.LoadFromFile("Textures/white.png");

            // Set clear color to black
            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);

            // Initialize canvas, demo and renderer
            _canvas = new Canvas();
            _demo = new CanvasDemo(_canvas, ClientSize.X, ClientSize.Y, _demoTexture);
            _renderer = new CanvasRenderer();
            _renderer.Initialize(ClientSize.X, ClientSize.Y, _whiteTexture);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            // Clear the canvas for new frame
            _canvas.Clear();

            // Let demo render to canvas
            _demo.RenderFrame(args.Time, _offset, _zoom, _rotation);

            // Draw the canvas content using OpenGL
            GL.Clear(ClearBufferMask.ColorBufferBit);
            _renderer.RenderCanvas(_canvas);

            SwapBuffers();
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            var keyboard = KeyboardState;
            var mouse = MouseState;

            // Close on Escape
            if (keyboard.IsKeyDown(Keys.Escape))
            {
                Close();
            }

            // Zoom with mouse wheel
            if (mouse.ScrollDelta.Y != 0)
            {
                _zoom += mouse.ScrollDelta.Y * 0.1;
                if (_zoom < 0.1f) _zoom = 0.1;
            }

            // Pan with left mouse button
            if (mouse.IsButtonDown(MouseButton.Left))
            {
                var delta = mouse.Delta;
                _offset.x += delta.X * (1.0 / _zoom);
                _offset.y += delta.Y * (1.0 / _zoom);
            }

            // Rotate with Q/E keys
            if (keyboard.IsKeyDown(Keys.Q)) _rotation += 10.0 * args.Time;
            if (keyboard.IsKeyDown(Keys.E)) _rotation -= 10.0 * args.Time;
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
            _renderer.UpdateProjection(ClientSize.X, ClientSize.Y);
        }

        protected override void OnUnload()
        {
            _demoTexture.Dispose();
            _whiteTexture.Dispose();
            _renderer.Cleanup();
            base.OnUnload();
        }
    }
}