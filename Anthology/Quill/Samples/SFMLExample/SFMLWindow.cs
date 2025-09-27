// SFMLWindow.cs
using Common;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Scribe.Internal;
using Prowl.Vector;
using SFML.Graphics;
using SFML.System;
using SFML.Window;

namespace SFMLExample
{
    /// <summary>
    /// Window class that handles the application lifecycle and user input
    /// </summary>
    public class SFMLWindow : IDisposable
    {
        // Window and rendering
        private RenderWindow _window;
        private SFMLRenderer _renderer;
        
        // Canvas and demo
        private Canvas _canvas;
        private List<IDemo> _demos;
        private int _currentDemoIndex;
        
        // Camera/view properties
        private Vector2 _offset = Vector2.zero;
        private double _zoom = 1.0f;
        private double _rotation = 0.0f;
        
        // Resources
        private TextureSFML _whiteTexture;
        private TextureSFML _demoTexture;
        
        // Fonts
        private FontFile RobotoFont;
        private FontFile AlamakFont;
        
        // Input tracking
        private Vector2i _lastMousePos;
        private Clock _clock = new Clock();

        // Key state tracking for demo switching
        private bool _leftKeyPressed = false;
        private bool _rightKeyPressed = false;
        private bool _spaceKeyPressed = false;
        
        public SFMLWindow(uint width, uint height, string title)
        {
            // Create SFML window with settings equivalent to OpenTK settings
            var contextSettings = new ContextSettings
            {
                DepthBits = 24,
                StencilBits = 8,
                AntialiasingLevel = 0,
                MajorVersion = 3,
                MinorVersion = 3
            };
            
            _window = new RenderWindow(
                new VideoMode(width, height),
                title,
                Styles.Default,
                contextSettings
            );
            
            // Set up event handlers
            _window.Closed += (_, _) => _window.Close();
            _window.Resized += OnResize;
            _window.MouseWheelScrolled += OnMouseWheelScrolled;
            
            // Initialize everything
            Initialize();
        }
        
        private void Initialize()
        {
            // Load textures
            _demoTexture = TextureSFML.LoadFromFile("Textures/wall.png");
            
            // Create white texture
            _whiteTexture = TextureSFML.CreateNew(1, 1);
            Image whitePixel = new Image(1, 1, new byte[] { 255, 255, 255, 255 });
            _whiteTexture.Handle.Update(whitePixel);
            
            // Initialize renderer
            _renderer = new SFMLRenderer();
            _renderer.Initialize((int)_window.Size.X, (int)_window.Size.Y, _whiteTexture);
            _renderer.SetRenderWindow(_window);
            
            // Initialize canvas
            _canvas = new Canvas(_renderer, new FontAtlasSettings());

            // Load fonts
            RobotoFont = new FontFile("Fonts/Roboto.ttf");
            AlamakFont = new FontFile("Fonts/Alamak.ttf");

            // Initialize demos
            _demos = new List<IDemo>
            {
                new CanvasDemo(_canvas, (int)_window.Size.X, (int)_window.Size.Y, _demoTexture, RobotoFont, AlamakFont),
                new SVGDemo(_canvas, (int)_window.Size.X, (int)_window.Size.Y),
                new BenchmarkScene(_canvas, RobotoFont, (int)_window.Size.X, (int)_window.Size.Y),
            };
        }
        
        private void OnResize(object sender, SizeEventArgs e)
        {
            // Update view when the window is resized
            _window.SetView(new View(new FloatRect(0, 0, e.Width, e.Height)));
            _renderer.UpdateProjection((int)e.Width, (int)e.Height);
        }
        
        private void OnMouseWheelScrolled(object sender, MouseWheelScrollEventArgs e)
        {
            // Zoom with mouse wheel
            _zoom += e.Delta * 0.1;
            if (_zoom < 0.1) _zoom = 0.1;
        }
        
        public void Run()
        {
            // Main loop
            DateTime now = DateTime.UtcNow;
            while (_window.IsOpen)
            {
                // Process events
                _window.DispatchEvents();
                
                // Handle input
                HandleInput();

                // Update
                //float deltaTime = _clock.Restart().AsSeconds();
                float deltaTime = (float)(DateTime.UtcNow - now).TotalSeconds;
                now = DateTime.UtcNow;

                // Clear the canvas for new frame
                _canvas.Clear();
                
                // Let demo render to canvas
                _demos[_currentDemoIndex].RenderFrame(deltaTime, _offset, _zoom, _rotation);
                
                // Draw using SFML
                _window.Clear(Color.Black);
                _canvas.Render();
                _window.Display();
            }
        }
        
        private void HandleInput()
        {
            // Close on Escape
            if (Keyboard.IsKeyPressed(Keyboard.Key.Escape))
                _window.Close();

            // Rotate with Q/E keys
            float deltaTime = 1.0f / 60.0f; // Approximate if not available
            if (Keyboard.IsKeyPressed(Keyboard.Key.Q))
                _rotation += 10.0 * deltaTime;
            if (Keyboard.IsKeyPressed(Keyboard.Key.E))
                _rotation -= 10.0 * deltaTime;

            // Demo switching with Left/Right keys (with key release detection)
            bool leftKeyCurrentlyPressed = Keyboard.IsKeyPressed(Keyboard.Key.Left);
            bool rightKeyCurrentlyPressed = Keyboard.IsKeyPressed(Keyboard.Key.Right);
            bool spaceKeyCurrentlyPressed = Keyboard.IsKeyPressed(Keyboard.Key.Space);

            if (leftKeyCurrentlyPressed && !_leftKeyPressed)
                _currentDemoIndex = _currentDemoIndex - 1 < 0 ? _demos.Count - 1 : _currentDemoIndex - 1;
            if (rightKeyCurrentlyPressed && !_rightKeyPressed)
                _currentDemoIndex = _currentDemoIndex + 1 == _demos.Count ? 0 : _currentDemoIndex + 1;
            if (spaceKeyCurrentlyPressed && !_spaceKeyPressed)
                if (_demos[_currentDemoIndex] is SVGDemo svgDemo)
                    svgDemo.ParseSVG();

            _leftKeyPressed = leftKeyCurrentlyPressed;
            _rightKeyPressed = rightKeyCurrentlyPressed;
            _spaceKeyPressed = spaceKeyCurrentlyPressed;

            Vector2i currentPos = Mouse.GetPosition(_window);
            if (Mouse.IsButtonPressed(Mouse.Button.Left))
            {
                Vector2f delta = new Vector2f(currentPos.X - _lastMousePos.X, currentPos.Y - _lastMousePos.Y);

                _offset.x += delta.X * (1.0 / _zoom);
                _offset.y += delta.Y * (1.0 / _zoom);
            }

            _lastMousePos = currentPos;
        }
        
        public void Dispose()
        {
            _renderer.Dispose();
            _whiteTexture.Dispose();
            _demoTexture.Dispose();
            _window.Dispose();
        }
    }
}