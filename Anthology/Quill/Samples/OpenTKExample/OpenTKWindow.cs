using Common;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Scribe.Internal;
using Prowl.Vector;

namespace OpenTKExample
{
    /// <summary>
    /// Window class that handles the application lifecycle and user input
    /// </summary>
    public class OpenTKWindow : GameWindow
    {
        // Canvas and demo
        private Canvas _canvas;
        private List<IDemo> _demos;
        private int _currentDemoIndex;
        private CanvasRenderer _renderer;
        private BenchmarkScene _benchmarkScene;

        // Camera/view properties
        private Float2 _offset = Float2.Zero;
        private float _zoom = 1.0f;
        private float _rotation = 0.0f;

        private TextureTK _whiteTexture;
        private TextureTK _demoTexture;

        private FontFile RobotoFont;
        private FontFile AlamakFont;

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
            _renderer = new CanvasRenderer();
            _renderer.Initialize(ClientSize.X, ClientSize.Y, _whiteTexture);
            _canvas = new Canvas(_renderer, new FontAtlasSettings());

            RobotoFont = new FontFile("Fonts/Roboto.ttf");
            AlamakFont = new FontFile("Fonts/Alamak.ttf");

            _demos = new List<IDemo>
            {
                new CanvasDemo(_canvas, ClientSize.X, ClientSize.Y, _demoTexture, RobotoFont, AlamakFont),
                new SVGDemo(_canvas, ClientSize.X, ClientSize.Y),
                new BenchmarkScene(_canvas, RobotoFont, ClientSize.X, ClientSize.Y),
            };
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            // Clear the canvas for new frame
            _canvas.Clear();

            // Let demo render to canvas
            _demos[_currentDemoIndex].RenderFrame((float)args.Time, _offset, _zoom, _rotation);
            //_benchmarkScene.RenderFrame(args.Time, ClientSize.X, ClientSize.Y);

            // Draw the canvas content using OpenGL
            GL.Clear(ClearBufferMask.ColorBufferBit);
            _canvas.Render();

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
                _zoom += mouse.ScrollDelta.Y * 0.1f;
                if (_zoom < 0.1f) _zoom = 0.1f;
            }

            // Pan with left mouse button
            if (mouse.IsButtonDown(MouseButton.Left))
            {
                var delta = mouse.Delta;
                _offset.X += delta.X * (1.0f / _zoom);
                _offset.Y += delta.Y * (1.0f / _zoom);
            }

            // Rotate with Q/E keys
            if (keyboard.IsKeyDown(Keys.Q)) _rotation += 10.0f * (float)args.Time;
            if (keyboard.IsKeyDown(Keys.E)) _rotation -= 10.0f * (float)args.Time;


            if (keyboard.IsKeyReleased(Keys.Left))
                _currentDemoIndex = _currentDemoIndex - 1 < 0 ? _demos.Count - 1 : _currentDemoIndex - 1;
            if (keyboard.IsKeyReleased(Keys.Right))
                _currentDemoIndex = _currentDemoIndex + 1 == _demos.Count ? 0 : _currentDemoIndex + 1;
            if (keyboard.IsKeyReleased(Keys.Space))
                if (_demos[_currentDemoIndex] is SVGDemo svgDemo)
                    svgDemo.ParseSVG();
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