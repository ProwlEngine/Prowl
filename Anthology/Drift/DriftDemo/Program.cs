using Prowl.Drift;
using SFML.Graphics;
using SFML.Window;
using SFML.System;
using Drift.Joints;
using System.Diagnostics;
using System.Numerics;

namespace DriftDemo
{
    class Program
    {
        private static RenderWindow? _window;
        private static Space? _space;
        private static Renderer? _renderer;
        private static List<IDemo> _demos = new();
        private static int _currentDemo = 0;
        private static Font? _font;
        private static Text? _titleText;
        private static Text? _instructionText;
        private static Body? _mouseBody;
        private static MouseJoint? _mouseJoint;

        static void Main()
        {
            // Initialize window
            _window = new RenderWindow(new VideoMode(1024, 768), "Drift Physics Demo");
            _window.Closed += (s, e) => _window.Close();
            _window.KeyPressed += OnKeyPressed;
            _window.MouseButtonPressed += OnMousePressed;
            _window.MouseButtonReleased += OnMouseReleased;
            _window.MouseMoved += OnMouseMoved;

            // Load font (using default system font)
            try
            {
                _font = new Font("C:/Windows/Fonts/arial.ttf");
            }
            catch
            {
                // Fallback - create without font for now
                Console.WriteLine("Warning: Could not load font, text will not display");
            }

            // Initialize text
            if (_font != null)
            {
                _titleText = new Text("", _font, 24)
                {
                    FillColor = Color.White,
                    Position = new Vector2f(10, 10)
                };

                _instructionText = new Text("LEFT/RIGHT: change demos | R: reset | SPACE: demo action | LEFT CLICK+DRAG: move objects | ESC: quit", _font, 16)
                {
                    FillColor = Color.Yellow,
                    Position = new Vector2f(10, 740)
                };
            }

            // Initialize physics
            _space = new Space();
            _space.Gravity = new Vector2(0, -10f);
            _space.Damping = 0f;

            // Create mouse body for interaction
            _mouseBody = new Body(Body.BodyType.Kinetic, Vector2.Zero);
            _space.AddBody(_mouseBody);

            // Initialize renderer
            _renderer = new Renderer(_window);

            // Register demos
            _demos.Add(new DemoBounce());
            _demos.Add(new DemoCircles());
            _demos.Add(new DemoPyramid());
            _demos.Add(new DemoCircleBox());
            _demos.Add(new DemoSeesaw());
            _demos.Add(new DemoRope());
            _demos.Add(new DemoCar());
            _demos.Add(new DemoRagdoll());
            _demos.Add(new DemoCrank());
            _demos.Add(new DemoWeb());

            // Start first demo
            LoadDemo(_currentDemo);

            // Main loop
            const double fixedDt = 1f / 60f;
            double accumulator = 0f;
            var last = (double)Stopwatch.GetTimestamp();

            while (_window.IsOpen)
            {
                var now = (double)Stopwatch.GetTimestamp();
                var deltaTime = (now - last) / Stopwatch.Frequency;
                last = now;

                accumulator += deltaTime;
                while (accumulator >= fixedDt)
                {
                    _space.Step((float)fixedDt, 32, 32, true);
                    accumulator -= fixedDt;
                }


                // Run demo frame
                _demos[_currentDemo].RunFrame();

                _window.DispatchEvents();
                // Render
                _window.Clear(Color.Black);

                _renderer.DrawSpace(_space);

                // Draw UI
                if (_titleText != null)
                {
                    _titleText.DisplayedString = $"{_demos[_currentDemo].Name} ({_currentDemo + 1}/{_demos.Count})";
                    _window.Draw(_titleText);

                    // Show FPS
                    var fps = (int)(1f / deltaTime);
                    var fpsText = new Text($"FPS: {fps}", _font, 16);

                    fpsText.FillColor = fps >= 50 ? Color.Green : (fps >= 30 ? Color.Yellow : Color.Red);
                    fpsText.Position = new Vector2f(_window.Size.X - 100, 10);
                    _window.Draw(fpsText);

                    // Show number of bodies, Joints and Contacts
                    var statsText = new Text($"Bodies: {_space.Bodies.Count}  Joints: {_space.Joints.Count}  Contacts: {_space.Contacts.Count}", _font, 16)
                    {
                        FillColor = Color.Cyan,
                        Position = new Vector2f(_window.Size.X - 300, 30)
                    };

                    _window.Draw(statsText);
                }

                if (_instructionText != null)
                {
                    _window.Draw(_instructionText);
                }

                _window.Display();
            }
        }

        private static void OnKeyPressed(object? sender, KeyEventArgs e)
        {
            switch (e.Code)
            {
                case Keyboard.Key.Left:
                    _currentDemo = (_currentDemo - 1 + _demos.Count) % _demos.Count;
                    LoadDemo(_currentDemo);
                    break;

                case Keyboard.Key.Right:
                    _currentDemo = (_currentDemo + 1) % _demos.Count;
                    LoadDemo(_currentDemo);
                    break;

                case Keyboard.Key.R:
                    LoadDemo(_currentDemo);
                    break;

                case Keyboard.Key.Escape:
                    _window?.Close();
                    break;

                case Keyboard.Key.Space:
                    _demos[_currentDemo].KeyDown(' ');
                    break;

                default:
                    // Pass other keys to current demo
                    if (e.Code >= Keyboard.Key.A && e.Code <= Keyboard.Key.Z)
                    {
                        char key = (char)('a' + (int)(e.Code - Keyboard.Key.A));
                        _demos[_currentDemo].KeyDown(key);
                    }
                    break;
            }
        }

        private static void LoadDemo(int index)
        {
            if (_space == null) return;

            // Remove existing mouse joint
            _mouseJoint = null;

            // Clear space
            _space.Clear();

            // Re-create mouse body for interaction
            _mouseBody = new Body(Body.BodyType.Kinetic, Vector2.Zero);
            _space.AddBody(_mouseBody);

            // Initialize demo
            _demos[index].Init(_space);

            Console.WriteLine($"Loaded demo: {_demos[index].Name}");
        }

        private static Vector2 ScreenToWorld(Vector2i screenPos)
        {
            if (_window == null) return Vector2.Zero;

            // Convert screen coordinates to world coordinates
            // Invert the WorldToScreen transformation from Renderer.cs
            const float pixelsPerMeter = 50f;
            var center = new Vector2f(_window.Size.X / 2f, _window.Size.Y / 2f);

            float worldX = (screenPos.X - center.X) / pixelsPerMeter;
            float worldY = (center.Y + _window.Size.Y * 0.5f - screenPos.Y) / pixelsPerMeter;
            return new Vector2(worldX, worldY);
        }

        private static void OnMousePressed(object? sender, MouseButtonEventArgs e)
        {
            if (_space == null || _mouseBody == null || e.Button != Mouse.Button.Left) return;

            // Remove existing mouse joint
            if (_mouseJoint != null)
            {
                _space.RemoveJoint(_mouseJoint);
                _mouseJoint = null;
            }

            // Convert mouse position to world coordinates
            Vector2 worldPos = ScreenToWorld(new Vector2i(e.X, e.Y));

            // Find body at mouse position
            Body? targetBody = _space.FindBodyByPoint(worldPos);
            if (targetBody != null && targetBody != _mouseBody && targetBody.Type != Body.BodyType.Static)
            {
                // Update mouse body position
                _mouseBody.Position = worldPos;

                // Create mouse joint
                _mouseJoint = new MouseJoint(_mouseBody, targetBody, worldPos);
                _mouseJoint.MaxForce = targetBody.Mass * 1000f;
                _space.AddJoint(_mouseJoint);
            }
        }

        private static void OnMouseReleased(object? sender, MouseButtonEventArgs e)
        {
            if (_space == null || e.Button != Mouse.Button.Left) return;

            // Remove mouse joint
            if (_mouseJoint != null)
            {
                _space.RemoveJoint(_mouseJoint);
                _mouseJoint = null;
            }
        }

        private static void OnMouseMoved(object? sender, MouseMoveEventArgs e)
        {
            if (_space == null || _mouseBody == null || _mouseJoint == null) return;

            // Update mouse body position
            Vector2 worldPos = ScreenToWorld(new Vector2i(e.X, e.Y));
            _mouseBody.Position = worldPos;
        }
    }
}