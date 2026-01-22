using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Scribe.Internal;
using Prowl.Vector;
using Prowl.Vector.Spatial;
using System.Drawing;
using Color = Prowl.Vector.Color;

namespace Common
{
    internal class CanvasDemo: IDemo
    {
        private Canvas _canvas;
        private Canvas3D _canvas3D;
        private float _width;
        private float _height;
        private object _texture;
        private FontFile _fontA;
        private FontFile _fontB;

        private float _time;

        // Demo state
        private float _rotation = 0f;

        // Performance monitoring
        private Queue<float> _frameTimeHistory = new Queue<float>();
        private Queue<float> _fpsHistory = new Queue<float>();
        private const int MAX_HISTORY_SAMPLES = 100;
        private float _fpsUpdateCounter = 0;
        private float _currentFps = 0;
        private const float FPS_UPDATE_INTERVAL = 0.5f; // Update FPS display every half second

        public CanvasDemo(Canvas canvas, float width, float height, object texture, FontFile fontA, FontFile fontB)
        {
            _canvas = canvas;
            _width = width;
            _height = height;
            _canvas3D = new Canvas3D(canvas, width, height);
            _texture = texture;
            _fontA = fontA;
            _fontB = fontB;
        }

        /// <summary>
        /// Updates and renders a frame
        /// </summary>
        public void RenderFrame(float deltaTime, Float2 offset, float zoom, float rotate)
        {
            // Update time
            _time += deltaTime;

            // Update performance metrics
            UpdatePerformanceMetrics(deltaTime);

            // Update rotation based on time
            _rotation += deltaTime * 30f; // 30 degrees per second

            _canvas.TransformBy(Transform2D.CreateTranslation(_width / 2, _height / 2));
            _canvas.TransformBy(Transform2D.CreateTranslation(offset.X, offset.Y) * Transform2D.CreateRotation(rotate) * Transform2D.CreateScale(zoom, zoom));
            _canvas.SetStrokeScale(zoom);

            //_canvas.SetTexture(_texture);
            //_canvas.Scissor(0, 0, 200, 200);

            DrawDemo2D();

            _canvas.ResetState();

            // Draw performance overlay
            DrawPerformanceOverlay();
        }

        private void UpdatePerformanceMetrics(float deltaTime)
        {
            // Calculate FPS
            float instantFps = deltaTime > 0 ? 1.0f / deltaTime : 0f;

            // Add to history queues, keeping a fixed size
            _frameTimeHistory.Enqueue(deltaTime * 1000); // Convert to milliseconds
            _fpsHistory.Enqueue(instantFps);

            if (_frameTimeHistory.Count > MAX_HISTORY_SAMPLES)
                _frameTimeHistory.Dequeue();

            if (_fpsHistory.Count > MAX_HISTORY_SAMPLES)
                _fpsHistory.Dequeue();

            // Update the FPS counter at intervals to make it readable
            _fpsUpdateCounter += deltaTime;
            if (_fpsUpdateCounter >= FPS_UPDATE_INTERVAL)
            {
                _currentFps = _fpsHistory.Count > 0 ? _fpsHistory.Average() : 0;
                _fpsUpdateCounter = 0;
            }
        }

        private void DrawPerformanceOverlay()
        {
            // Draw background for performance overlay
            float overlayWidth = 200;
            float overlayHeight = 120;
            float padding = 10;

            // Position in top-right corner with padding
            float x = _width - overlayWidth - padding;
            float y = padding;

            // Background with semi-transparency
            _canvas.RectFilled(x, y, overlayWidth, overlayHeight, Color32.FromArgb(180, 0, 0, 0));

            // Draw border
            _canvas.SetStrokeColor(Color32.FromArgb(255, 100, 100, 100));
            _canvas.SetStrokeWidth(1);
            _canvas.Rect(x, y, overlayWidth, overlayHeight);
            _canvas.Stroke();

            // Draw FPS counter
            string fpsText = $"FPS: {_currentFps:F1}";
            float frameTimeAvg = _frameTimeHistory.Count > 0 ? _frameTimeHistory.Average() : 0;
            string frameTimeText = $"Frame Time: {frameTimeAvg:F1} ms";

            DrawText(fpsText, x + 10, y + 20, 14, Color32.FromArgb(255, 100, 255, 100));
            DrawText(frameTimeText, x + 10, y + 40, 12, Color.White);

            // Draw performance graph
            float graphX = x + 10;
            float graphY = y + 60;
            float graphWidth = overlayWidth - 20;
            float graphHeight = 50;

            // Draw graph background
            _canvas.RectFilled(graphX, graphY, graphWidth, graphHeight, Color32.FromArgb(60, 255, 255, 255));

            //// Draw FPS graph
            //if (_fpsHistory.Count > 1)
            //{
            //    // Find max value to scale the graph (with minimum range of 0-60 FPS)
            //    float maxFps = Maths.Max(60, _fpsHistory.Max());
            //
            //    _canvas.SetStrokeColor(Color32.FromArgb(255, 100, 255, 100));
            //    _canvas.SetStrokeWidth(1.5);
            //    _canvas.BeginPath();
            //
            //    // Draw the FPS graph line
            //    bool first = true;
            //    float xStep = graphWidth / (MAX_HISTORY_SAMPLES - 1);
            //    int i = 0;
            //
            //    foreach (float fps in _fpsHistory)
            //    {
            //        float normalizedValue = fps / maxFps; // 0.0 to 1.0
            //        float pointX = graphX + (i * xStep);
            //        float pointY = graphY + graphHeight - (normalizedValue * graphHeight);
            //
            //        if (first)
            //        {
            //            _canvas.MoveTo(pointX, pointY);
            //            first = false;
            //        }
            //        else
            //        {
            //            _canvas.LineTo(pointX, pointY);
            //        }
            //
            //        i++;
            //    }
            //
            //    _canvas.Stroke();
            //
            //    // Draw the target 60 FPS line
            //    float targetY = graphY + graphHeight - ((60 / maxFps) * graphHeight);
            //    _canvas.SetStrokeColor(Color32.FromArgb(100, 255, 100, 100));
            //    _canvas.SetStrokeWidth(1.0);
            //
            //    _canvas.BeginPath();
            //    _canvas.MoveTo(graphX, targetY);
            //    _canvas.LineTo(graphX + graphWidth, targetY);
            //    _canvas.Stroke();
            //}
        }

        private void DrawText(string text, float x, float y, float height, Color32 color)
        {
            VectorFont.DrawString(_canvas, text.ToUpper(), x, y, height, color, 2f);
        }

        private void DrawGroupBackground(float x, float y, float width, float height, string title)
        {
            // Background
            _canvas.RectFilled(x, y, width, height, Color32.FromArgb(255, 0, 0, 0));

            _canvas.SetStrokeColor(Color32.FromArgb(255, 190, 190, 190));
            _canvas.SetStrokeWidth(4);

            // Border
            _canvas.BeginPath();
            _canvas.MoveTo(x, y);
            _canvas.LineTo(x + width, y);
            _canvas.LineTo(x + width, y + height);
            _canvas.LineTo(x, y + height);
            _canvas.ClosePath();
            _canvas.Stroke();

            // Title
            DrawText(title, x + 5, y - 25, 14, Color.White);

            // Underline
            float textWidth = VectorFont.MeasureString(title, 14);
            _canvas.BeginPath();
            _canvas.MoveTo(x + 5, y - 6);
            _canvas.LineTo(x + 5 + textWidth, y - 6);
            _canvas.Stroke();
        }

        private void DrawGrid(int x, int y, float cellSize, Color color)
        {
            _canvas.SetStrokeColor(color);
            _canvas.SetStrokeWidth(4);

            // Draw horizontal lines
            _canvas.BeginPath();
            for (int i = 0; i <= y; i++)
            {
                _canvas.MoveTo(0, i * cellSize);
                _canvas.LineTo((cellSize * x), i * cellSize);
            }
            _canvas.Stroke();

            // Draw vertical lines
            _canvas.BeginPath();
            for (int i = 0; i <= x; i++)
            {
                _canvas.MoveTo(i * cellSize, 0);
                _canvas.LineTo(i * cellSize, (cellSize * y));
            }
            _canvas.Stroke();
        }   

        private void DrawCoordinateSystem(float x, float y, float size)
        {
            // X axis
            _canvas.SetStrokeColor(Color32.FromArgb(150, 255, 100, 100));
            _canvas.SetStrokeWidth(2);
            _canvas.BeginPath();
            _canvas.MoveTo(x - size, y);
            _canvas.LineTo(x + size, y);
            _canvas.Stroke();

            // X arrow
            _canvas.BeginPath();
            _canvas.MoveTo(x + size, y);
            _canvas.LineTo(x + size - 10, y - 5);
            _canvas.LineTo(x + size - 10, y + 5);
            _canvas.LineTo(x + size, y);
            _canvas.SetFillColor(Color32.FromArgb(150, 255, 100, 100));
            _canvas.Fill();

            // Y axis
            _canvas.SetStrokeColor(Color32.FromArgb(150, 100, 255, 100));
            _canvas.BeginPath();
            _canvas.MoveTo(x, y + size);
            _canvas.LineTo(x, y - size);
            _canvas.Stroke();

            // Y arrow
            _canvas.BeginPath();
            _canvas.MoveTo(x, y - size);
            _canvas.LineTo(x - 5, y - size + 10);
            _canvas.LineTo(x + 5, y - size + 10);
            _canvas.LineTo(x, y - size);
            _canvas.SetFillColor(Color32.FromArgb(150, 100, 255, 100));
            _canvas.Fill();

            // Origin point
            _canvas.CircleFilled(x, y, 4, Color32.FromArgb(150, 255, 255, 255));
        }

        private void DrawDemo2D()
        {
            // Save the canvas state
            _canvas.SaveState();
            _canvas.SetBrushTexture(_texture);
            _canvas.SetBrushTextureTransform(Transform2D.CreateTranslation(0, 0) * Transform2D.CreateScale(128, 128));

            // Draw 2D grid for reference
            DrawGrid(16, 17, 50, Color32.FromArgb(40, 255, 255, 255));

            // Draw coordinate system at center
            DrawCoordinateSystem(0, 0, 50);

            // 1. Path Operations Demo
            DrawPathOperationsDemo(50, 50, 200, 150);

            // 2. Transformations Demo
            DrawTransformationsDemo(300, 50, 200, 150);
            
            // 3. Shapes Demo
            DrawShapesDemo(550, 50, 200, 150);

            // 4. Line Styles Demo
            DrawLineStylesDemo(50, 250, 200, 150);

            // 5. 3D Demo
            Draw3DDemo(300, 250, 200, 150);

            // 6. Join Styles Demo
            DrawJoinStylesDemo(550, 250, 200, 150);

            // 7. Cap Styles Demo
            DrawCapStylesDemo(50, 450, 200, 150);

            // 8. Scissor Demo
            DrawScissorDemo(300, 450, 200, 150);

            // 9. Image Demo
            DrawImageDemo(550, 450, 200, 150);

            // 10. Gradient Demo
            DrawGradientDemo(50, 650, 200, 150);

            // 11. Concave Polygon Demo
            DrawConcaveDemo(300, 650, 200, 150);

            // 12. Text Demo
            DrawTextDemo(550, 650, 200, 150);

            _canvas.ClearBrushTexture();
            // Restore the canvas state
            _canvas.RestoreState();
        }

        #region 2D Drawing Demos

        private void DrawPathOperationsDemo(float x, float y, float width, float height)
        {
            _canvas.SaveState();

            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Path Operations");

            _canvas.SetStrokeWidth(4);
            _canvas.SetStrokeCap(EndCapStyle.Butt);

            // Demo 1: Basic shapes with paths
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 40));


            // Rectangle using path
            _canvas.BeginPath();
            _canvas.MoveTo(0, 0);
            _canvas.LineTo(30, 0);
            _canvas.LineTo(30, 30);
            _canvas.LineTo(0, 30);
            _canvas.ClosePath();
            _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 100, 100));
            _canvas.SetFillColor(Color32.FromArgb(100, 255, 100, 100));
            _canvas.FillAndStroke();

            // Triangle using path
            _canvas.BeginPath();
            _canvas.MoveTo(50, 30);
            _canvas.LineTo(80, 30);
            _canvas.LineTo(65, 0);
            _canvas.ClosePath();
            _canvas.SetStrokeColor(Color32.FromArgb(255, 100, 255, 100));
            _canvas.SetFillColor(Color32.FromArgb(100, 100, 255, 100));
            _canvas.FillAndStroke();

            // Demo 2: Line with Widths
            _canvas.TransformBy(Transform2D.CreateTranslation(100, 0));
            float lineWidth = 8;
            for (int i = 0; i < 7; i++) {
                lineWidth -= 1.1f;
                _canvas.SetStrokeWidth(lineWidth);
                _canvas.BeginPath();
                _canvas.MoveTo(0 + (i * 10), 0);
                _canvas.LineTo(10 + (i * 10), 50);
                _canvas.SetStrokeColor(Color32.FromArgb(255, 100, 100, 255));
                _canvas.Stroke();
            }

            _canvas.SetStrokeWidth(2.0f);


            // Demo 3: Curved paths
            _canvas.TransformBy(Transform2D.CreateTranslation(-100, 50));
            
            // Arc
            _canvas.BeginPath();
            _canvas.Arc(20, 20, 20, 0, (float)Maths.PI, false);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 100));
            _canvas.Stroke();
            
            // Bezier curve
            _canvas.BeginPath();
            _canvas.MoveTo(50, 30);
            _canvas.BezierCurveTo(60, 0, 80, 40, 90, 10);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 100, 200, 255));
            _canvas.Stroke();
            
            // Quadratic curve
            _canvas.BeginPath();
            _canvas.MoveTo(110, 30);
            _canvas.QuadraticCurveTo(140, 0, 160, 30);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 200, 100, 255));
            _canvas.Stroke();

            _canvas.RestoreState();
        }

        private void DrawTransformationsDemo(float x, float y, float width, float height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Transformations");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + width / 2, y + height / 2));

            // Rotating square
            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateRotation(_rotation));
            _canvas.RectFilled(-30, -30, 60, 60, Color32.FromArgb(200, 100, 200, 255));
            _canvas.RestoreState();

            // Scaling rectangle
            float scale = 0.5f + 0.3f * (float)Maths.Sin(_time * 2);
            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(70, 0));
            _canvas.TransformBy(Transform2D.CreateScale(scale, scale));
            _canvas.RectFilled(-20, -20, 40, 40, Color32.FromArgb(200, 255, 150, 100));
            _canvas.RestoreState();

            // Translating circle
            float offsetY = 20 * (float)Maths.Sin(_time * 3);
            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(-70, offsetY));
            _canvas.CircleFilled(0, 0, 20, Color32.FromArgb(200, 100, 255, 150));
            _canvas.RestoreState();

            _canvas.RestoreState();
        }

        private void DrawShapesDemo(float x, float y, float width, float height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Shapes");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 30));

            // Rectangle
            _canvas.RectFilled(0, 0, 40, 30, Color32.FromArgb(200, 255, 100, 100));

            // Circle
            _canvas.CircleFilled(80, 15, 15, Color32.FromArgb(200, 100, 255, 100));

            // Pie (animated)
            float startAngle = 0;
            float endAngle = (float)(Maths.PI * (1 + Maths.Sin(_time)) / 2); // Animate between 0 and PI
            _canvas.PieFilled(140, 15, 15, startAngle, endAngle, Color32.FromArgb(200, 100, 150, 255));

            // Animated star shape
            DrawStar(40, 80, 25, 10, 5, _time, Color32.FromArgb(255, 255, 200, 100));

            // Rounded rectangle
            DrawRoundedRect(100, 60, 60, 40, 10, Color32.FromArgb(200, 200, 100, 255));

            _canvas.RestoreState();
        }

        private void DrawLineStylesDemo(float x, float y, float width, float height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Lines");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 70));

            _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 255));

            // Thin line
            float[] widths = [0.25f, 1.0f, 3.0f, 4.0f];
            for (int i=0; i<4; i++)
            {
                _canvas.SetStrokeWidth(widths[i]);
                _canvas.BeginPath();
                _canvas.MoveTo(0, -45 + (i * 17));
                _canvas.LineTo(160, -55 + (i * 17));
                _canvas.Stroke();
            }

            // Thick line
            _canvas.SetStrokeWidth(15);
            _canvas.BeginPath();
            _canvas.MoveTo(0, 20 + 5);
            _canvas.LineTo(160, 20 - 5);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 100, 100));
            _canvas.Stroke();

            // Dashed line
            _canvas.SetStrokeColor(Color32.FromArgb(255, 100, 255, 100));
            _canvas.SetStrokeWidth(4);

            _canvas.BeginPath();
            _canvas.MoveTo(0, 40 + 5);
            _canvas.LineTo(160, 40 - 5);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 100, 100));
            _canvas.SetStrokeDash(new List<float>() { 10, 5, 2, 2 }, 0);
            _canvas.Stroke();

            _canvas.BeginPath();
            _canvas.MoveTo(0, 60 + 5);
            _canvas.LineTo(160, 60 - 5);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 100, 100, 255));
            _canvas.SetStrokeDash(new List<float>() { 5, 5 }, 0);
            _canvas.Stroke();

            _canvas.RestoreState();
        }

        private void Draw3DDemo(float x, float y, float width, float height)
        {
            DrawGroupBackground(x, y, width, height, "3D");

            // Save the canvas state
            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x, y));
            _canvas.SetStrokeWidth(2.0f);

            // Setup the 3D viewport
            _canvas3D.ViewportWidth = width;
            _canvas3D.ViewportHeight = height;

            // Set up the camera
            float aspectRatio = width / height;
            _canvas3D.SetPerspectiveProjection((float)(Maths.PI / 4), aspectRatio, 0.1f, 100f);

            // Position camera based on time
            float cameraX = (float)Maths.Sin(_time * 0.2) * 8;
            float cameraZ = (float)Maths.Cos(_time * 0.2) * 8;
            _canvas3D.SetLookAt(
                new Float3(cameraX, 5, cameraZ),  // Orbiting camera
                Float3.Zero,                      // Look at origin
                Float3.UnitY                         // Up direction
            );

            // Draw 3D grid for reference
            Draw3DGrid(3, 0.5f, Color32.FromArgb(30, 255, 255, 255));

            // Draw coordinate axes
            Draw3DCoordinateAxes(1.0f);

            // 1. Draw rotating cube
            Quaternion cubeRotation = Quaternion.FromEuler(
                _time * 0.5f, _time * 0.3f, 0);
            _canvas3D.SetWorldTransform(new Float3(-2, 2, 0), cubeRotation, Float3.One * 0.5f);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 220, 100, 100));
            _canvas3D.DrawCubeStroked(Float3.Zero, 2.0f);

            // 2. Draw rotating sphere
            Quaternion sphereRotation = Quaternion.FromEuler(
                _time * 0.2f, _time * 0.4f, 0);
            _canvas3D.SetWorldTransform(new Float3(2, 2, 0), sphereRotation, Float3.One * 0.5f);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 100, 220, 100));
            _canvas3D.DrawSphereStroked(Float3.Zero, 1.5f, 10);

            // Restore the canvas state
            _canvas.RestoreState();
        }

        private void DrawJoinStylesDemo(float x, float y, float width, float height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Joins");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 40));

            // Set stroke color
            _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 255));

            // Draw heartbeat lines with different join styles
            void DrawHeartbeat(JointStyle join, float yOffset, Color color)
            {
                _canvas.SetStrokeJoint(join);
                _canvas.SetStrokeColor(color);
                _canvas.SetStrokeWidth(5);

                // Base animation for spikes
                float baseAnim = Maths.Sin(_time * 1) * 0.5f + 0.5f; // 0 to 1 range

                _canvas.BeginPath();
                _canvas.MoveTo(0, yOffset);

                // First flat section
                _canvas.LineTo(10, yOffset);

                // First spike
                float p1Height = 10 * baseAnim;
                _canvas.LineTo(30, yOffset - p1Height);
                _canvas.LineTo(40, yOffset);

                // Flat section
                //_canvas.LineTo(60, yOffset);
                _canvas.BezierCurveTo(50, yOffset + p1Height, 60, yOffset + p1Height, 70, yOffset);

                // Major spike
                float qrsHeight = Maths.Abs(40 * (0.5f * Maths.Sin(_time * 1)));
                _canvas.LineTo(70, yOffset - qrsHeight);
                _canvas.LineTo(80, yOffset + (qrsHeight * 0.5f));
                _canvas.LineTo(90, yOffset);

                // Flat section
                _canvas.LineTo(100, yOffset);

                // Small spike (T wave)
                float tHeight = 30 * (baseAnim * 0.7f);
                _canvas.LineTo(130, yOffset - (7 + tHeight));
                _canvas.LineTo(120, yOffset);

                // Final flat section
                _canvas.LineTo(160, yOffset);

                _canvas.Stroke();

                // Draw join style label
                //DrawText(join.ToString(), 0, yOffset + 10, 10, color);
            }

            // Set miter limit for all joins
            _canvas.SetMiterLimit(100);

            // Draw three heartbeat lines with different join styles
            DrawHeartbeat(JointStyle.Bevel, 0, Color32.FromArgb(255, 255, 100, 100));
            DrawHeartbeat(JointStyle.Round, 40, Color32.FromArgb(255, 100, 255, 100));
            DrawHeartbeat(JointStyle.Miter, 80, Color32.FromArgb(255, 100, 100, 255));

            // Restore the canvas state
            _canvas.RestoreState();
        }

        private void DrawCapStylesDemo(float x, float y, float width, float height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Caps");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 45));

            // Setup for drawing lines
            _canvas.SetStrokeWidth(17);
            float lineLength = width - 40;
            float spacing = 20;

            // Demo lines with different cap styles
            void DrawCapLine(EndCapStyle startCap, EndCapStyle endCap, float yOffset, Color color)
            {
                // Set the cap styles and color
                _canvas.SetStrokeStartCap(startCap);
                _canvas.SetStrokeEndCap(endCap);
                _canvas.SetStrokeColor(color);

                // Draw the line with a bit of animation
                _canvas.BeginPath();

                // Start with a straight segment
                _canvas.MoveTo(0, yOffset);

                // End with another straight segment
                _canvas.LineTo(lineLength, yOffset - spacing);

                _canvas.Stroke();

                // Draw labels for the caps
                //DrawText(startCap.ToString(), 5, yOffset - 15, 10, color);
                //DrawText(endCap.ToString(), lineLength - 30, yOffset, 10, color);
            }

            // Show all five cap styles with matching start/end caps
            DrawCapLine(EndCapStyle.Butt, EndCapStyle.Butt, 0, Color32.FromArgb(255, 255, 100, 100));
            DrawCapLine(EndCapStyle.Square, EndCapStyle.Square, spacing, Color32.FromArgb(255, 255, 180, 100));
            DrawCapLine(EndCapStyle.Round, EndCapStyle.Round, spacing * 2, Color32.FromArgb(255, 100, 255, 100));
            DrawCapLine(EndCapStyle.Bevel, EndCapStyle.Bevel, spacing * 3, Color32.FromArgb(255, 100, 180, 255));
            //DrawCapLine(EndCapStyle.TriangleOut, EndCapStyle.TriangleOut, spacing * 4, Color32.FromArgb(255, 200, 100, 255));

            // Demonstrate mixing different start and end caps
            float mixedY = spacing * 4;
            DrawCapLine(EndCapStyle.Round, EndCapStyle.Bevel, mixedY, Color32.FromArgb(255, 255, 255, 150));

            _canvas.RestoreState();
        }

        private void DrawScissorDemo(float x, float y, float width, float height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Scissor");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 30));


            // Create a scissor region with animation
            float scissorX = 40 + (Maths.Sin(_time) * 30);
            float scissorY = 10 + (Maths.Cos(_time * 1.3f) * 5);
            float scissorWidth = 80 + (Maths.Sin(_time * 0.7f) * 15);
            float scissorHeight = 60 + (Maths.Cos(_time * 0.5f) * 15);

            // Set scissor and visualize the scissor area
            var origin = new Float2(80, 40);
            _canvas.TransformBy(
                Transform2D.CreateTranslation(origin.X, origin.Y)
                * Transform2D.CreateRotation(_time * 15.0f)
                * Transform2D.CreateTranslation(-origin.X, -origin.Y)
            );
            _canvas.IntersectScissor(scissorX, scissorY, scissorWidth, scissorHeight);

            // Draw a red rectangle to show the scissor area
            _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 100, 100));
            _canvas.SetStrokeWidth(2);
            _canvas.Rect(scissorX, scissorY, scissorWidth, scissorHeight);
            _canvas.Stroke();

            // Draw content that will be scissored
            _canvas.RectFilled(20, 10, 120, 80, Color32.FromArgb(200, 255, 200, 100));

            // Draw some circles that will be scissored
            for (int i = 0; i < 5; i++)
            {
                float radius = 10 + i * 5;
                float circleX = 80 + Maths.Cos(_time * (1 + i * 0.2f)) * 40;
                float circleY = 40 + Maths.Sin(_time * (1 + i * 0.2f)) * 30;
                _canvas.CircleFilled(circleX, circleY, radius,
                    Color32.FromArgb(150, 50 + i * 40, 100, 200 - i * 30));
            }

            // Reset scissor
            _canvas.ResetScissor();


            _canvas.RestoreState();
        }

        private void DrawImageDemo(float x, float y, float width, float height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Images");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 40, y + 20));

            // Basic image drawing using brush textures
            if (_texture != null)
            {
                // Helper to create texture transform that maps a local rect to the full texture
                Transform2D TextureTransformForRect(float lx, float ly, float w, float h)
                {
                    return Transform2D.CreateTranslation(lx, ly) *
                           Transform2D.CreateScale(w, h);
                }

                // 1. Basic textured rectangle
                _canvas.SetBrushTexture(_texture);
                _canvas.SetBrushTextureTransform(TextureTransformForRect(0, 0, 50, 50));
                _canvas.RectFilled(0, 0, 50, 50, Color.White);
                _canvas.ClearBrushTexture();

                // 2. Scaled textured rectangle
                float scale = 0.7f + 0.3f * Maths.Sin(_time);
                _canvas.SaveState();
                _canvas.TransformBy(Transform2D.CreateTranslation(80, 0));
                _canvas.TransformBy(Transform2D.CreateScale(scale, scale));
                _canvas.SetBrushTexture(_texture);
                _canvas.SetBrushTextureTransform(TextureTransformForRect(0, 0, 50, 50));
                _canvas.RectFilled(0, 0, 50, 50, Color.White);
                _canvas.RestoreState();

                // 3. Rotated textured rectangle
                _canvas.SaveState();
                _canvas.TransformBy(Transform2D.CreateTranslation(30, 60));
                _canvas.TransformBy(Transform2D.CreateRotation(45));
                _canvas.SetBrushTexture(_texture);
                _canvas.SetBrushTextureTransform(TextureTransformForRect(-10, 0, 50, 50));
                _canvas.RectFilled(-10, 0, 50, 50, Color.White);
                _canvas.RestoreState();

                // 4. Textured rectangle with tint
                _canvas.SaveState();
                _canvas.TransformBy(Transform2D.CreateTranslation(80, 60));

                // Apply color tint that changes over time
                float r = 0.5f + 0.5f * Maths.Sin(_time);
                float g = 0.5f + 0.5f * Maths.Sin(_time + Maths.PI * 2 / 3);
                float b = 0.5f + 0.5f * Maths.Sin(_time + Maths.PI * 4 / 3);
                _canvas.SetBrushTexture(_texture);
                _canvas.SetBrushTextureTransform(TextureTransformForRect(0, 0, 60, 40));
                _canvas.RectFilled(0, 0, 60, 40, Color32.FromArgb(200,
                    (int)(r * 255), (int)(g * 255), (int)(b * 255)));

                _canvas.RestoreState();
            }
            else
            {

                // Draw "No Image" text
                DrawText("No Image Available", 30, 45, 14, Color32.FromArgb(255, 255, 255, 255));
            }

            _canvas.RestoreState();
        }

        private void DrawGradientDemo(float x, float y, float width, float height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Gradients");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 30));

            // Section spacing
            float spacing = 20;
            float boxSize = 40;
            float animatedFactor = (Maths.Sin(_time * 2.0f) * 0.5f + 0.5f); // 0 to 1 animation

            // 1
            // Horizontal linear gradient
            _canvas.SetLinearBrush(0, 0, boxSize, 0,
                Color32.FromArgb(255, 255, 100, 100),
                Color32.FromArgb((int)(255 * animatedFactor), 100, 100, 255));
            _canvas.RectFilled(0, 0, boxSize, boxSize, Color.White);

            // Diagonal linear gradient with animation
            _canvas.SetLinearBrush(
                0, boxSize + spacing * 2,
                boxSize, boxSize * 2 + spacing * 5 * animatedFactor,
                Color32.FromArgb(255, 255, 100, 255),
                Color32.FromArgb(255, 100, 255, 255));
            _canvas.RectFilled(0, boxSize + spacing, boxSize, boxSize, Color.White);

            // 2
            float col2X = boxSize + spacing;

            // Circle with linear gradient
            _canvas.SetLinearBrush(
                col2X, 0,
                col2X + boxSize, boxSize,
                Color32.FromArgb(255, 255, 100, 100),
                Color32.FromArgb(255, 100, 100, 255));
            _canvas.CircleFilled(col2X + boxSize / 2, boxSize / 2, boxSize / 2, Color.White);

            // Radial gradient with animation
            _canvas.SetRadialBrush(
                (col2X + boxSize / 2) - 5, (boxSize / 2 + boxSize + spacing) - 5,
                13,
                30,
                Color.PowderBlue,
                Color.MediumPurple);
            _canvas.CircleFilled(col2X + boxSize / 2, boxSize / 2 + boxSize + spacing, boxSize / 2, Color.White);


            // 3
            float col3X = (boxSize + spacing) * 2;

            // Basic box gradient
            _canvas.SetBoxBrush(
                col3X + boxSize / 2, boxSize / 2,
                boxSize * 0.7f, boxSize * 0.7f,
                5, 10,
                Color.Turquoise,
                Color.Tomato);
            _canvas.RectFilled(col3X, 0, boxSize, boxSize, Color.White);

            // Rounded rect with box gradient
            _canvas.SetBoxBrush(
                col3X + boxSize / 2, boxSize / 2 + boxSize + spacing,
                boxSize * 0.7f, boxSize * 0.7f,
                5, 10,
                Color32.FromArgb(255, 255, 255, 100),
                Color32.FromArgb(255, 50, 128, 50));

            // Use path-based drawing to show gradient with a rounded rect
            _canvas.RoundedRectFilled(
                col3X, boxSize + spacing,
                boxSize, boxSize,
                10, 10, 10, 10, Color.AliceBlue);


            // Clear gradient
            _canvas.ClearBrush();

            _canvas.RestoreState();
        }

        private void DrawConcaveDemo(float x, float y, float width, float height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Concave");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 30));

            // PART 1: Simple polygon with clockwise winding
            {
                _canvas.BeginPath();

                // Star-like concave shape with clockwise winding
                _canvas.MoveTo(20, 0);  // Top point
                _canvas.LineTo(25, 15); // Right shoulder
                _canvas.LineTo(40, 15); // Right arm
                _canvas.LineTo(30, 25); // Right side
                _canvas.LineTo(35, 40); // Right leg
                _canvas.LineTo(20, 30); // Bottom point
                _canvas.LineTo(5, 40);  // Left leg
                _canvas.LineTo(10, 25); // Left side
                _canvas.LineTo(0, 15);  // Left arm
                _canvas.LineTo(15, 15); // Left shoulder
                _canvas.ClosePath();

                // Set fill color and stroke
                _canvas.SetFillColor(Color32.FromArgb(255, 100, 200, 255));
                _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 255));
                _canvas.SetStrokeWidth(1.5f);

                // Fill and stroke
                _canvas.FillAndStroke();
            }

            // PART 2: Polygon with hole (counter-clockwise inner path)
            {
                // Outer path (clockwise)
                _canvas.BeginPath();
                _canvas.MoveTo(70, 5);
                _canvas.LineTo(110, 5);
                _canvas.LineTo(110, 45);
                _canvas.LineTo(70, 45);
                _canvas.ClosePath();

                // Inner path (counter-clockwise) - explicitly set as hole
                _canvas.MoveTo(100, 15); // Start point at top-right of hole
                _canvas.LineTo(80, 15);  // Top edge (right to left = CCW)
                _canvas.LineTo(80, 35);  // Left edge
                _canvas.LineTo(100, 35); // Bottom edge
                _canvas.ClosePath();

                // Set fill color and stroke
                _canvas.SetFillColor(Color32.FromArgb(255, 255, 150, 100));
                _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 255));
                _canvas.SetStrokeWidth(1.5f);

                // Fill complex shape (with hole)
                _canvas.FillComplex();
                _canvas.Stroke();
            }

            // PART 3: Animated complex shape with multiple holes
            {
                float time = _time;
                float wave = Maths.Sin(time) * 3;
            
                // Outer path
                _canvas.BeginPath();
                float centerX = 150;
                float centerY = 25;
                float radius = 20;
            
                // Create wavy outer circle
                int segments = 20;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = 2 * Maths.PI * i / segments;
                    float r = radius + Maths.Sin(angle * 5 + time * 2) * 5;
                    float px = centerX + r * Maths.Cos(angle);
                    float py = centerY + r * Maths.Sin(angle);
            
                    if (i == 0)
                        _canvas.MoveTo(px, py);
                    else
                        _canvas.LineTo(px, py);
                }
                _canvas.ClosePath();
            
                // Inner hole 1 (counter-clockwise)
                float hole1X = centerX - 5 + Maths.Sin(time * 1.5f) * 3;
                float hole1Y = centerY - 5 + Maths.Cos(time * 1.5f) * 3;
                float hole1Radius = 5 + Maths.Sin(time * 2) * 1;
            
                _canvas.MoveTo(hole1X + hole1Radius, hole1Y);
                for (int i = segments; i >= 0; i--)
                { // CCW direction
                    float angle = 2 * Maths.PI * i / segments;
                    float px = hole1X + hole1Radius * Maths.Cos(angle);
                    float py = hole1Y + hole1Radius * Maths.Sin(angle);
                    _canvas.LineTo(px, py);
                }
                _canvas.ClosePath();
                
                // Inner hole 2 (counter-clockwise)
                float hole2X = centerX + 10 + Maths.Cos(time) * 3;
                float hole2Y = centerY + 5 + Maths.Sin(time * 2) * 3;
                float hole2Radius = 4 + Maths.Cos(time * 3) * 1;
                
                _canvas.MoveTo(hole2X + hole2Radius, hole2Y);
                for (int i = segments; i >= 0; i--)
                { // CCW direction
                    float angle = 2 * Maths.PI * i / segments;
                    float px = hole2X + hole2Radius * Maths.Cos(angle);
                    float py = hole2Y + hole2Radius * Maths.Sin(angle);
                    _canvas.LineTo(px, py);
                }
                _canvas.ClosePath();

                // Set fill color and stroke
                _canvas.SetFillColor(Color32.FromArgb(255, 150, 255, 150));
                _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 255));
                _canvas.SetStrokeWidth(1.5f);
            
                // Fill complex shape
                _canvas.FillComplex();
                _canvas.Stroke();
            }
            
            // PART 4: Complex shape with animated holes
            {
            
                // Outer path (snake-like shape)
                _canvas.BeginPath();
                _canvas.MoveTo(00, 60);
                _canvas.BezierCurveTo(
                    120, 60,
                    140, 90,
                    160, 90);
                _canvas.LineTo(160, 110);
                _canvas.BezierCurveTo(
                    140, 110,
                    120, 80,
                    00, 80);
                _canvas.ClosePath();

                float time = _time;

                void AddHole(float holeX, float holeY, float holeSize)
                {
                    holeY += Maths.Sin(time * 0.5f) * 9;
                    holeSize *= Maths.Sin(time += 0.25f);
                    _canvas.MoveTo(holeX + holeSize, holeY);
                    for (int j = 20; j >= 0; j--)
                    {
                        float angle = 2 * Maths.PI * j / 20;
                        float px = holeX + holeSize * Maths.Cos(angle);
                        float py = holeY + holeSize * Maths.Sin(angle);
                        _canvas.LineTo(px, py);
                    }
                    _canvas.ClosePath();
                }


                // Add a few holes
                AddHole(20, 70, 8);
                AddHole(40, 71, 8);
                AddHole(60, 73, 8);
                AddHole(80, 76, 8);
                AddHole(100, 80, 8);
                AddHole(120, 86, 8);
                AddHole(140, 94, 8);

                // Set fill and stroke
                _canvas.SetFillColor(Color32.FromArgb(255, 255, 200, 100));
                _canvas.SetStrokeColor(Color.White);
                _canvas.SetStrokeWidth(1.5f);
            
                // Fill using auto-detection
                _canvas.FillComplexAA();
                //_canvas.Stroke();
            }

            _canvas.RestoreState();
        }

        private void DrawTextDemo(float x, float y, float width, float height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Text");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 5, y + 5));

            // Basic text rendering
            //_canvas.DrawText(_fontA, "Normal", 0, 0, Color.White);
            _canvas.DrawText("Normal", 0, 0, Color.White, 32, _fontA);

            // Text with different fonts
            _canvas.DrawText("Small", 0, 53, Color32.FromArgb(255, 200, 200, 200), 16, _fontA);
            _canvas.DrawText("Multiple", 0, 70, Color32.FromArgb(255, 200, 255, 200), 32, _fontB);

            // Different transformations

            // 1. Rotation
            _canvas.SaveState();
            float angle = Maths.Sin(_time) * 30; // Convert to radians, oscillate ±30°
            Float2 center = new Float2(150, 20);
            _canvas.TransformBy(Transform2D.CreateTranslation(center.X, center.Y));
            _canvas.TransformBy(Transform2D.CreateRotation(angle));
            _canvas.DrawText("Rotate", 0, 0, Color32.FromArgb(255, 255, 150, 150), 32, _fontA, origin: new Float2(0.5f, 0.5f));
            _canvas.RestoreState();

            // 2. Scaling
            _canvas.SaveState();
            float scale = 0.3f + 0.2f * Maths.Sin(_time * 1.5f);
            _canvas.TransformBy(Transform2D.CreateTranslation(40, 55));
            _canvas.TransformBy(Transform2D.CreateScale(scale, scale));
            _canvas.DrawText("Scaling", 0, 0, Color32.FromArgb(255, 150, 255, 150), 32, _fontA);
            _canvas.RestoreState();

            // 3. Character spacing
            float spacing = Maths.Sin(_time * 2) * 1.0f;
            _canvas.DrawText("Spacing", 90, 45, Color32.FromArgb(255, 150, 150, 255), 32, _fontA, (float)spacing);

            // 4. Path text (text following a curve)
            _canvas.SaveState();
            float centerX = 140;
            float centerY = 105;
            float radius = 30;

            // First, visualize the path for reference
            _canvas.SetStrokeColor(Color32.FromArgb(40, 255, 255, 255));
            _canvas.SetStrokeWidth(1);
            _canvas.BeginPath();
            _canvas.Arc(centerX, centerY, radius, 0, Maths.PI * 2, false);
            _canvas.Stroke();

            // Now draw text along the path
            string circularText = "Animating Characters ";
            int charCount = circularText.Length;

            for (int i = 0; i < charCount; i++)
            {
                float charAngle = 2 * Maths.PI * i / charCount - _time * 0.5f;
                float charX = centerX + radius * Maths.Cos(charAngle);
                float charY = centerY + radius * Maths.Sin(charAngle);

                _canvas.SaveState();
                _canvas.TransformBy(Transform2D.CreateTranslation(charX, charY));

                // Calculate color based on position
                byte r = (byte)(128 + 127 * Maths.Sin(charAngle));
                byte g = (byte)(128 + 127 * Maths.Sin(charAngle + 2 * Maths.PI / 3));
                byte b = (byte)(128 + 127 * Maths.Sin(charAngle + 4 * Maths.PI / 3));

                // Draw the character
                _canvas.DrawText(circularText[i].ToString(), 0, 0, Color32.FromArgb(255, r, g, b), 16, _fontA, origin: new Float2(0.5f, 0.5f));

                _canvas.RestoreState();
            }

            _canvas.RestoreState();
        }

        #endregion

        #region Shapes/Lines

        private void Draw3DGrid(float size, float spacing, Color color)
        {
            _canvas3D.SetWorldTransform(Float3.Zero, Quaternion.Identity, Float3.One);

            int lineCount = (int)(size / spacing) * 2 + 1;
            float start = -size;

            _canvas.SetStrokeWidth(1.0f);
            _canvas.SetStrokeColor(color);

            for (int i = 0; i < lineCount; i++)
            {
                float pos = start + i * spacing;

                // Draw X lines
                _canvas3D.BeginPath();
                _canvas3D.MoveTo(pos, 0, -size);
                _canvas3D.LineTo(pos, 0, size);
                _canvas3D.Stroke();

                // Draw Z lines
                _canvas3D.BeginPath();
                _canvas3D.MoveTo(-size, 0, pos);
                _canvas3D.LineTo(size, 0, pos);
                _canvas3D.Stroke();
            }
        }

        private void Draw3DCoordinateAxes(float length)
        {
            _canvas3D.SetWorldTransform(Float3.Zero, Quaternion.Identity, Float3.One);

            _canvas.SetStrokeWidth(2.0f);

            // X axis (red)
            _canvas3D.BeginPath();
            _canvas3D.MoveTo(0, 0, 0);
            _canvas3D.LineTo(length, 0, 0);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 255, 0, 0));
            _canvas3D.Stroke();

            // Y axis (green)
            _canvas3D.BeginPath();
            _canvas3D.MoveTo(0, 0, 0);
            _canvas3D.LineTo(0, length, 0);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 0, 255, 0));
            _canvas3D.Stroke();

            // Z axis (blue)
            _canvas3D.BeginPath();
            _canvas3D.MoveTo(0, 0, 0);
            _canvas3D.LineTo(0, 0, length);
            _canvas.SetStrokeColor(Color32.FromArgb(255, 0, 0, 255));
            _canvas3D.Stroke();
        }

        private void DrawDashedLine(float x1, float y1, float x2, float y2, float dashLength, float gapLength, Color color, float width)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float distance = (float)Maths.Sqrt(dx * dx + dy * dy);
            float dashCount = distance / (dashLength + gapLength);

            float xStep = dx / dashCount / (dashLength + gapLength) * dashLength;
            float yStep = dy / dashCount / (dashLength + gapLength) * dashLength;

            float gapXStep = dx / dashCount / (dashLength + gapLength) * gapLength;
            float gapYStep = dy / dashCount / (dashLength + gapLength) * gapLength;

            _canvas.SetStrokeColor(color);
            _canvas.SetStrokeWidth(width);

            for (int i = 0; i < dashCount; i++)
            {
                float startX = x1 + i * (xStep + gapXStep);
                float startY = y1 + i * (yStep + gapYStep);
                float endX = startX + xStep;
                float endY = startY + yStep;

                _canvas.BeginPath();
                _canvas.MoveTo(startX, startY);
                _canvas.LineTo(endX, endY);
                _canvas.Stroke();
            }
        }

        private void DrawStar(float x, float y, float outerRadius, float innerRadius, int points, float rotation, Color color)
        {
            _canvas.BeginPath();

            for (int i = 0; i < points * 2; i++)
            {
                float radius = i % 2 == 0 ? outerRadius : innerRadius;
                float angle = rotation + (float)(i * Maths.PI / points);
                float px = x + radius * (float)Maths.Cos(angle);
                float py = y + radius * (float)Maths.Sin(angle);

                if (i == 0)
                    _canvas.MoveTo(px, py);
                else
                    _canvas.LineTo(px, py);
            }

            _canvas.ClosePath();
            _canvas.SetFillColor(color);
            _canvas.Fill();
        }

        private void DrawRoundedRect(float x, float y, float width, float height, float radius, Color color)
        {
            // Ensure radius is not too large
            radius = Maths.Min(radius, Maths.Min(width / 2, height / 2));

            _canvas.BeginPath();

            // Top-left corner
            _canvas.MoveTo(x + radius, y);

            // Top edge and top-right corner
            _canvas.LineTo(x + width - radius, y);
            _canvas.Arc(x + width - radius, y + radius, radius, -Maths.PI / 2, 0, false);

            // Right edge and bottom-right corner
            _canvas.LineTo(x + width, y + height - radius);
            _canvas.Arc(x + width - radius, y + height - radius, radius, 0, Maths.PI / 2, false);

            // Bottom edge and bottom-left corner
            _canvas.LineTo(x + radius, y + height);
            _canvas.Arc(x + radius, y + height - radius, radius, Maths.PI / 2, Maths.PI, false);

            // Left edge and top-left corner
            _canvas.LineTo(x, y + radius);
            _canvas.Arc(x + radius, y + radius, radius, Maths.PI, 3 * Maths.PI / 2, false);

            _canvas.ClosePath();
            _canvas.SetFillColor(color);
            _canvas.Fill();
        }

        #endregion

    }
}
