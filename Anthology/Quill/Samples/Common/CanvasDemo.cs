using Prowl.Quill;
using Prowl.Vector;
using System.Drawing;

namespace Common
{
    internal class CanvasDemo
    {
        private Canvas _canvas;
        private Canvas3D _canvas3D;
        private double _width;
        private double _height;
        private object _texture;

        private double _time;

        // Demo state
        private double _rotation = 0f;

        // Performance monitoring
        private Queue<double> _frameTimeHistory = new Queue<double>();
        private Queue<double> _fpsHistory = new Queue<double>();
        private const int MAX_HISTORY_SAMPLES = 100;
        private double _fpsUpdateCounter = 0;
        private double _currentFps = 0;
        private const double FPS_UPDATE_INTERVAL = 0.5; // Update FPS display every half second

        public CanvasDemo(Canvas canvas, double width, double height, object texture)
        {
            _canvas = canvas;
            _width = width;
            _height = height;
            _canvas3D = new Canvas3D(canvas, width, height);
            _texture = texture;
        }

        /// <summary>
        /// Updates and renders a frame
        /// </summary>
        public void RenderFrame(double deltaTime, Vector2 offset, double zoom, double rotate)
        {
            // Update time
            _time += deltaTime;

            // Update performance metrics
            UpdatePerformanceMetrics(deltaTime);

            // Update rotation based on time
            _rotation += deltaTime * 30f; // 30 degrees per second

            _canvas.TransformBy(Transform2D.CreateTranslation(_width / 2, _height / 2));
            _canvas.TransformBy(Transform2D.CreateRotate(rotate) * Transform2D.CreateTranslation(offset.x, offset.y) * Transform2D.CreateScale(zoom, zoom));
            _canvas.SetStrokeScale(zoom);

            //_canvas.SetTexture(_texture);
            //_canvas.Scissor(0, 0, 200, 200);

            DrawDemo2D();

            _canvas.ResetState();

            // Draw performance overlay
            DrawPerformanceOverlay();
        }

        private void UpdatePerformanceMetrics(double deltaTime)
        {
            // Calculate FPS
            double instantFps = deltaTime > 0 ? 1.0 / deltaTime : 0;

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
            double overlayWidth = 200;
            double overlayHeight = 120;
            double padding = 10;

            // Position in top-right corner with padding
            double x = _width - overlayWidth - padding;
            double y = padding;

            // Background with semi-transparency
            _canvas.RectFilled(x, y, overlayWidth, overlayHeight, Color.FromArgb(180, 0, 0, 0));

            // Draw border
            _canvas.SetStrokeColor(Color.FromArgb(255, 100, 100, 100));
            _canvas.SetStrokeWidth(1);
            _canvas.Rect(x, y, overlayWidth, overlayHeight);
            _canvas.Stroke();

            // Draw FPS counter
            string fpsText = $"FPS: {_currentFps:F1}";
            double frameTimeAvg = _frameTimeHistory.Count > 0 ? _frameTimeHistory.Average() : 0;
            string frameTimeText = $"Frame Time: {frameTimeAvg:F1} ms";

            DrawText(fpsText, x + 10, y + 20, 14, Color.FromArgb(255, 100, 255, 100));
            DrawText(frameTimeText, x + 10, y + 40, 12, Color.White);

            // Draw performance graph
            double graphX = x + 10;
            double graphY = y + 60;
            double graphWidth = overlayWidth - 20;
            double graphHeight = 50;

            // Draw graph background
            _canvas.RectFilled(graphX, graphY, graphWidth, graphHeight, Color.FromArgb(60, 255, 255, 255));

            //// Draw FPS graph
            //if (_fpsHistory.Count > 1)
            //{
            //    // Find max value to scale the graph (with minimum range of 0-60 FPS)
            //    double maxFps = Math.Max(60, _fpsHistory.Max());
            //
            //    _canvas.SetStrokeColor(Color.FromArgb(255, 100, 255, 100));
            //    _canvas.SetStrokeWidth(1.5);
            //    _canvas.BeginPath();
            //
            //    // Draw the FPS graph line
            //    bool first = true;
            //    double xStep = graphWidth / (MAX_HISTORY_SAMPLES - 1);
            //    int i = 0;
            //
            //    foreach (double fps in _fpsHistory)
            //    {
            //        double normalizedValue = fps / maxFps; // 0.0 to 1.0
            //        double pointX = graphX + (i * xStep);
            //        double pointY = graphY + graphHeight - (normalizedValue * graphHeight);
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
            //    double targetY = graphY + graphHeight - ((60 / maxFps) * graphHeight);
            //    _canvas.SetStrokeColor(Color.FromArgb(100, 255, 100, 100));
            //    _canvas.SetStrokeWidth(1.0);
            //
            //    _canvas.BeginPath();
            //    _canvas.MoveTo(graphX, targetY);
            //    _canvas.LineTo(graphX + graphWidth, targetY);
            //    _canvas.Stroke();
            //}
        }

        private void DrawText(string text, double x, double y, double height, Color color)
        {
            VectorFont.DrawString(_canvas, text.ToUpper(), x, y, height, color, 2f);
        }

        private void DrawGroupBackground(double x, double y, double width, double height, string title)
        {
            // Background
            _canvas.RectFilled(x, y, width, height, Color.FromArgb(255, 0, 0, 0));

            _canvas.SetStrokeColor(Color.FromArgb(255, 190, 190, 190));
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
            double textWidth = VectorFont.MeasureString(title, 14);
            _canvas.BeginPath();
            _canvas.MoveTo(x + 5, y - 6);
            _canvas.LineTo(x + 5 + textWidth, y - 6);
            _canvas.Stroke();
        }

        private void DrawGrid(int x, int y, double cellSize, Color color)
        {
            _canvas.SetStrokeColor(color);
            _canvas.SetStrokeWidth(4);

            // Draw horizontal lines
            for (int i = 0; i <= x; i++)
            {
                _canvas.BeginPath();
                _canvas.MoveTo(0, i * cellSize);
                _canvas.LineTo((cellSize * x), i * cellSize);
                _canvas.Stroke();
            }

            // Draw vertical lines
            for (int i = 0; i <= y; i++)
            {
                _canvas.BeginPath();
                _canvas.MoveTo(i * cellSize, 0);
                _canvas.LineTo(i * cellSize, (cellSize * y));
                _canvas.Stroke();
            }
        }   

        private void DrawCoordinateSystem(double x, double y, double size)
        {
            // X axis
            _canvas.SetStrokeColor(Color.FromArgb(150, 255, 100, 100));
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
            _canvas.SetFillColor(Color.FromArgb(150, 255, 100, 100));
            _canvas.Fill();

            // Y axis
            _canvas.SetStrokeColor(Color.FromArgb(150, 100, 255, 100));
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
            _canvas.SetFillColor(Color.FromArgb(150, 100, 255, 100));
            _canvas.Fill();

            // Origin point
            _canvas.CircleFilled(x, y, 4, Color.FromArgb(150, 255, 255, 255));
        }

        private void DrawDemo2D()
        {
            // Save the canvas state
            _canvas.SaveState();

            // Draw 2D grid for reference
            DrawGrid(16, 16, 50, Color.FromArgb(40, 255, 255, 255));

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

            // Restore the canvas state
            _canvas.RestoreState();
        }

        #region 2D Drawing Demos

        private void DrawPathOperationsDemo(double x, double y, double width, double height)
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
            _canvas.SetStrokeColor(Color.FromArgb(255, 255, 100, 100));
            _canvas.SetFillColor(Color.FromArgb(100, 255, 100, 100));
            _canvas.FillAndStroke();

            // Triangle using path
            _canvas.BeginPath();
            _canvas.MoveTo(50, 30);
            _canvas.LineTo(80, 30);
            _canvas.LineTo(65, 0);
            _canvas.ClosePath();
            _canvas.SetStrokeColor(Color.FromArgb(255, 100, 255, 100));
            _canvas.SetFillColor(Color.FromArgb(100, 100, 255, 100));
            _canvas.FillAndStroke();

            // Demo 2: Line with Widths
            _canvas.TransformBy(Transform2D.CreateTranslation(100, 0));
            double lineWidth = 8;
            for (int i = 0; i < 7; i++) {
                lineWidth -= 1.1f;
                _canvas.SetStrokeWidth(lineWidth);
                _canvas.BeginPath();
                _canvas.MoveTo(0 + (i * 10), 0);
                _canvas.LineTo(10 + (i * 10), 50);
                _canvas.SetStrokeColor(Color.FromArgb(255, 100, 100, 255));
                _canvas.Stroke();
            }

            _canvas.SetStrokeWidth(2.0f);


            // Demo 3: Curved paths
            _canvas.TransformBy(Transform2D.CreateTranslation(-100, 50));
            
            // Arc
            _canvas.BeginPath();
            _canvas.Arc(20, 20, 20, 0, (double)Math.PI, false);
            _canvas.SetStrokeColor(Color.FromArgb(255, 255, 255, 100));
            _canvas.Stroke();
            
            // Bezier curve
            _canvas.BeginPath();
            _canvas.MoveTo(50, 30);
            _canvas.BezierCurveTo(60, 0, 80, 40, 90, 10);
            _canvas.SetStrokeColor(Color.FromArgb(255, 100, 200, 255));
            _canvas.Stroke();
            
            // Quadratic curve
            _canvas.BeginPath();
            _canvas.MoveTo(110, 30);
            _canvas.QuadraticCurveTo(140, 0, 160, 30);
            _canvas.SetStrokeColor(Color.FromArgb(255, 200, 100, 255));
            _canvas.Stroke();

            _canvas.RestoreState();
        }

        private void DrawTransformationsDemo(double x, double y, double width, double height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Transformations");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + width / 2, y + height / 2));

            // Rotating square
            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateRotate(_rotation));
            _canvas.RectFilled(-30, -30, 60, 60, Color.FromArgb(200, 100, 200, 255));
            _canvas.RestoreState();

            // Scaling rectangle
            double scale = 0.5f + 0.3f * (double)Math.Sin(_time * 2);
            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(70, 0));
            _canvas.TransformBy(Transform2D.CreateScale(scale, scale));
            _canvas.RectFilled(-20, -20, 40, 40, Color.FromArgb(200, 255, 150, 100));
            _canvas.RestoreState();

            // Translating circle
            double offsetY = 20 * (double)Math.Sin(_time * 3);
            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(-70, offsetY));
            _canvas.CircleFilled(0, 0, 20, Color.FromArgb(200, 100, 255, 150));
            _canvas.RestoreState();

            _canvas.RestoreState();
        }

        private void DrawShapesDemo(double x, double y, double width, double height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Shapes");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 30));

            // Rectangle
            _canvas.RectFilled(0, 0, 40, 30, Color.FromArgb(200, 255, 100, 100));

            // Circle
            _canvas.CircleFilled(80, 15, 15, Color.FromArgb(200, 100, 255, 100));

            // Pie (animated)
            double startAngle = 0;
            double endAngle = (double)(Math.PI * (1 + Math.Sin(_time)) / 2); // Animate between 0 and PI
            _canvas.PieFilled(140, 15, 15, startAngle, endAngle, Color.FromArgb(200, 100, 150, 255));

            // Animated star shape
            DrawStar(40, 80, 25, 10, 5, _time, Color.FromArgb(255, 255, 200, 100));

            // Rounded rectangle
            DrawRoundedRect(100, 60, 60, 40, 10, Color.FromArgb(200, 200, 100, 255));

            _canvas.RestoreState();
        }

        private void DrawLineStylesDemo(double x, double y, double width, double height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Lines");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 70));

            _canvas.SetStrokeColor(Color.FromArgb(255, 255, 255, 255));

            // Thin line
            double[] widths = [0.25f, 1.0f, 3.0f, 4.0f];
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
            _canvas.SetStrokeColor(Color.FromArgb(255, 255, 100, 100));
            _canvas.Stroke();

            // Dashed line (simulation with multiple segments)
            DrawDashedLine(0, 40 + 5, 160, 40 - 5, 10, 5, Color.FromArgb(255, 100, 255, 100), 4);

            // Dotted line (simulation with small segments)
            DrawDashedLine(0, 60 + 5, 160, 60 - 5, 2, 4, Color.FromArgb(255, 100, 100, 255), 4);

            _canvas.RestoreState();
        }

        private void Draw3DDemo(double x, double y, double width, double height)
        {
            DrawGroupBackground(x, y, width, height, "3D");

            // Save the canvas state
            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x, y));
            _canvas.SetStrokeWidth(2.0);

            // Setup the 3D viewport
            _canvas3D.ViewportWidth = width;
            _canvas3D.ViewportHeight = height;

            // Set up the camera
            double aspectRatio = width / height;
            _canvas3D.SetPerspectiveProjection((double)(Math.PI / 4), aspectRatio, 0.1f, 100f);

            // Position camera based on time
            double cameraX = (double)Math.Sin(_time * 0.2) * 8;
            double cameraZ = (double)Math.Cos(_time * 0.2) * 8;
            _canvas3D.SetLookAt(
                new Vector3(cameraX, 5, cameraZ),  // Orbiting camera
                Vector3.zero,                      // Look at origin
                Vector3.up                      // Up direction
            );

            // Draw 3D grid for reference
            Draw3DGrid(3, 0.5f, Color.FromArgb(30, 255, 255, 255));

            // Draw coordinate axes
            Draw3DCoordinateAxes(1.0f);

            // 1. Draw rotating cube
            Quaternion cubeRotation = Quaternion.CreateFromYawPitchRoll(
                _time * 0.5f, _time * 0.3f, 0);
            _canvas3D.SetWorldTransform(new Vector3(-2, 2, 0), cubeRotation, Vector3.one * 0.5);
            _canvas.SetStrokeColor(Color.FromArgb(255, 220, 100, 100));
            _canvas3D.DrawCubeStroked(Vector3.zero, 2.0f);

            // 2. Draw rotating sphere
            Quaternion sphereRotation = Quaternion.CreateFromYawPitchRoll(
                _time * 0.2f, _time * 0.4f, 0);
            _canvas3D.SetWorldTransform(new Vector3(2, 2, 0), sphereRotation, Vector3.one * 0.5);
            _canvas.SetStrokeColor(Color.FromArgb(255, 100, 220, 100));
            _canvas3D.DrawSphereStroked(Vector3.zero, 1.5f, 10);

            // Restore the canvas state
            _canvas.RestoreState();
        }

        private void DrawJoinStylesDemo(double x, double y, double width, double height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Joins");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 40));

            // Set stroke color
            _canvas.SetStrokeColor(Color.FromArgb(255, 255, 255, 255));

            // Draw heartbeat lines with different join styles
            void DrawHeartbeat(JointStyle join, double yOffset, Color color)
            {
                _canvas.SetStrokeJoint(join);
                _canvas.SetStrokeColor(color);
                _canvas.SetStrokeWidth(5);

                // Base animation for spikes
                double baseAnim = Math.Sin(_time * 1) * 0.5f + 0.5f; // 0 to 1 range

                _canvas.BeginPath();
                _canvas.MoveTo(0, yOffset);

                // First flat section
                _canvas.LineTo(10, yOffset);

                // First spike
                double p1Height = 10 * baseAnim;
                _canvas.LineTo(30, yOffset - p1Height);
                _canvas.LineTo(40, yOffset);

                // Flat section
                //_canvas.LineTo(60, yOffset);
                _canvas.BezierCurveTo(50, yOffset + p1Height, 60, yOffset + p1Height, 70, yOffset);

                // Major spike
                double qrsHeight = Math.Abs(40 * (0.5f * Math.Sin(_time * 1)));
                _canvas.LineTo(70, yOffset - qrsHeight);
                _canvas.LineTo(80, yOffset + (qrsHeight * 0.5));
                _canvas.LineTo(90, yOffset);

                // Flat section
                _canvas.LineTo(100, yOffset);

                // Small spike (T wave)
                double tHeight = 30 * (baseAnim * 0.7f);
                _canvas.LineTo(130, yOffset - (7 + tHeight));
                _canvas.LineTo(120, yOffset);

                // Final flat section
                _canvas.LineTo(160, yOffset);

                _canvas.Stroke();

                // Draw join style label
                DrawText(join.ToString(), 0, yOffset + 10, 10, color);
            }

            // Set miter limit for all joins
            _canvas.SetMiterLimit(100);

            // Draw three heartbeat lines with different join styles
            DrawHeartbeat(JointStyle.Bevel, 0, Color.FromArgb(255, 255, 100, 100));
            DrawHeartbeat(JointStyle.Round, 40, Color.FromArgb(255, 100, 255, 100));
            DrawHeartbeat(JointStyle.Miter, 80, Color.FromArgb(255, 100, 100, 255));

            // Restore the canvas state
            _canvas.RestoreState();
        }

        private void DrawCapStylesDemo(double x, double y, double width, double height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Caps");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 45));

            // Setup for drawing lines
            _canvas.SetStrokeWidth(17);
            double lineLength = width - 40;
            double spacing = 20;

            // Demo lines with different cap styles
            void DrawCapLine(EndCapStyle startCap, EndCapStyle endCap, double yOffset, Color color)
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
                DrawText(startCap.ToString(), 5, yOffset - 15, 10, color);
                DrawText(endCap.ToString(), lineLength - 30, yOffset, 10, color);
            }

            // Show all five cap styles with matching start/end caps
            DrawCapLine(EndCapStyle.Butt, EndCapStyle.Butt, 0, Color.FromArgb(255, 255, 100, 100));
            DrawCapLine(EndCapStyle.Square, EndCapStyle.Square, spacing, Color.FromArgb(255, 255, 180, 100));
            DrawCapLine(EndCapStyle.Round, EndCapStyle.Round, spacing * 2, Color.FromArgb(255, 100, 255, 100));
            DrawCapLine(EndCapStyle.Bevel, EndCapStyle.Bevel, spacing * 3, Color.FromArgb(255, 100, 180, 255));
            //DrawCapLine(EndCapStyle.TriangleOut, EndCapStyle.TriangleOut, spacing * 4, Color.FromArgb(255, 200, 100, 255));

            // Demonstrate mixing different start and end caps
            double mixedY = spacing * 4;
            DrawCapLine(EndCapStyle.Round, EndCapStyle.Bevel, mixedY, Color.FromArgb(255, 255, 255, 150));

            _canvas.RestoreState();
        }

        private void DrawScissorDemo(double x, double y, double width, double height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Scissor");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 20, y + 30));


            // Create a scissor region with animation
            double scissorX = 40 + (Math.Sin(_time) * 30);
            double scissorY = 10 + (Math.Cos(_time * 1.3) * 5);
            double scissorWidth = 80 + (Math.Sin(_time * 0.7) * 15);
            double scissorHeight = 60 + (Math.Cos(_time * 0.5) * 15);

            // Set scissor and visualize the scissor area
            _canvas.TransformBy(Transform2D.CreateRotate(_time * 15.0f, new(80, 40)));
            _canvas.IntersectScissor(scissorX, scissorY, scissorWidth, scissorHeight);

            // Draw a red rectangle to show the scissor area
            _canvas.SetStrokeColor(Color.FromArgb(255, 255, 100, 100));
            _canvas.SetStrokeWidth(2);
            _canvas.Rect(scissorX, scissorY, scissorWidth, scissorHeight);
            _canvas.Stroke();

            // Draw content that will be scissored
            _canvas.RectFilled(20, 10, 120, 80, Color.FromArgb(200, 255, 200, 100));

            // Draw some circles that will be scissored
            for (int i = 0; i < 5; i++)
            {
                double radius = 10 + i * 5;
                double circleX = 80 + Math.Cos(_time * (1 + i * 0.2)) * 40;
                double circleY = 40 + Math.Sin(_time * (1 + i * 0.2)) * 30;
                _canvas.CircleFilled(circleX, circleY, radius,
                    Color.FromArgb(150, 50 + i * 40, 100, 200 - i * 30));
            }

            // Reset scissor
            _canvas.ResetScissor();


            _canvas.RestoreState();
        }

        private void DrawImageDemo(double x, double y, double width, double height)
        {
            // Draw group background and title
            DrawGroupBackground(x, y, width, height, "Images");

            _canvas.SaveState();
            _canvas.TransformBy(Transform2D.CreateTranslation(x + 40, y + 20));

            // Basic image drawing
            if (_texture != null)
            {
                // Draw the texture at different scales and rotations

                // 1. Basic image drawing
                _canvas.Image(_texture, 0, 0, 50, 50, Color.White);

                // 2. Scaled image
                double scale = 0.7f + 0.3f * Math.Sin(_time);
                _canvas.SaveState();
                _canvas.TransformBy(Transform2D.CreateTranslation(80, 0));
                _canvas.TransformBy(Transform2D.CreateScale(scale, scale));
                _canvas.Image(_texture, 0, 0, 50, 50, Color.White);
                _canvas.RestoreState();

                // 3. Rotated image
                _canvas.SaveState();
                _canvas.TransformBy(Transform2D.CreateTranslation(30, 60));
                _canvas.TransformBy(Transform2D.CreateRotate(45));
                _canvas.Image(_texture, -10, 0, 50, 50, Color.White);
                _canvas.RestoreState();

                // 4. Image with transparency/tint
                _canvas.SaveState();
                _canvas.TransformBy(Transform2D.CreateTranslation(80, 60));

                // Apply color tint that changes over time
                double r = 0.5f + 0.5f * Math.Sin(_time);
                double g = 0.5f + 0.5f * Math.Sin(_time + Math.PI * 2 / 3);
                double b = 0.5f + 0.5f * Math.Sin(_time + Math.PI * 4 / 3);
                _canvas.Image(_texture, 0, 0, 60, 40, Color.FromArgb(200,
                    (int)(r * 255), (int)(g * 255), (int)(b * 255)));

                _canvas.RestoreState();

                // Reset texture to avoid affecting other drawing
                _canvas.SetTexture(null);
            }
            else
            {

                // Draw "No Image" text
                DrawText("No Image Available", 30, 45, 14, Color.FromArgb(255, 255, 255, 255));
            }

            _canvas.RestoreState();
        }

        #endregion

        #region Shapes/Lines

        private void Draw3DGrid(double size, double spacing, Color color)
        {
            _canvas3D.SetWorldTransform(Vector3.zero, Quaternion.identity, Vector3.one);

            int lineCount = (int)(size / spacing) * 2 + 1;
            double start = -size;

            _canvas.SetStrokeWidth(1.0f);
            _canvas.SetStrokeColor(color);

            for (int i = 0; i < lineCount; i++)
            {
                double pos = start + i * spacing;

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

        private void Draw3DCoordinateAxes(double length)
        {
            _canvas3D.SetWorldTransform(Vector3.zero, Quaternion.identity, Vector3.one);

            _canvas.SetStrokeWidth(2.0f);

            // X axis (red)
            _canvas3D.BeginPath();
            _canvas3D.MoveTo(0, 0, 0);
            _canvas3D.LineTo(length, 0, 0);
            _canvas.SetStrokeColor(Color.FromArgb(255, 255, 0, 0));
            _canvas3D.Stroke();

            // Y axis (green)
            _canvas3D.BeginPath();
            _canvas3D.MoveTo(0, 0, 0);
            _canvas3D.LineTo(0, length, 0);
            _canvas.SetStrokeColor(Color.FromArgb(255, 0, 255, 0));
            _canvas3D.Stroke();

            // Z axis (blue)
            _canvas3D.BeginPath();
            _canvas3D.MoveTo(0, 0, 0);
            _canvas3D.LineTo(0, 0, length);
            _canvas.SetStrokeColor(Color.FromArgb(255, 0, 0, 255));
            _canvas3D.Stroke();
        }

        private void DrawDashedLine(double x1, double y1, double x2, double y2, double dashLength, double gapLength, Color color, double width)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double distance = (double)Math.Sqrt(dx * dx + dy * dy);
            double dashCount = distance / (dashLength + gapLength);

            double xStep = dx / dashCount / (dashLength + gapLength) * dashLength;
            double yStep = dy / dashCount / (dashLength + gapLength) * dashLength;

            double gapXStep = dx / dashCount / (dashLength + gapLength) * gapLength;
            double gapYStep = dy / dashCount / (dashLength + gapLength) * gapLength;

            _canvas.SetStrokeColor(color);
            _canvas.SetStrokeWidth(width);

            for (int i = 0; i < dashCount; i++)
            {
                double startX = x1 + i * (xStep + gapXStep);
                double startY = y1 + i * (yStep + gapYStep);
                double endX = startX + xStep;
                double endY = startY + yStep;

                _canvas.BeginPath();
                _canvas.MoveTo(startX, startY);
                _canvas.LineTo(endX, endY);
                _canvas.Stroke();
            }
        }

        private void DrawStar(double x, double y, double outerRadius, double innerRadius, int points, double rotation, Color color)
        {
            _canvas.BeginPath();

            for (int i = 0; i < points * 2; i++)
            {
                double radius = i % 2 == 0 ? outerRadius : innerRadius;
                double angle = rotation + (double)(i * Math.PI / points);
                double px = x + radius * (double)Math.Cos(angle);
                double py = y + radius * (double)Math.Sin(angle);

                if (i == 0)
                    _canvas.MoveTo(px, py);
                else
                    _canvas.LineTo(px, py);
            }

            _canvas.ClosePath();
            _canvas.SetFillColor(color);
            _canvas.Fill();
        }

        private void DrawRoundedRect(double x, double y, double width, double height, double radius, Color color)
        {
            // Ensure radius is not too large
            radius = Math.Min(radius, Math.Min(width / 2, height / 2));

            _canvas.BeginPath();

            // Top-left corner
            _canvas.MoveTo(x + radius, y);

            // Top edge and top-right corner
            _canvas.LineTo(x + width - radius, y);
            _canvas.Arc(x + width - radius, y + radius, radius, -Math.PI / 2, 0, false);

            // Right edge and bottom-right corner
            _canvas.LineTo(x + width, y + height - radius);
            _canvas.Arc(x + width - radius, y + height - radius, radius, 0, Math.PI / 2, false);

            // Bottom edge and bottom-left corner
            _canvas.LineTo(x + radius, y + height);
            _canvas.Arc(x + radius, y + height - radius, radius, Math.PI / 2, Math.PI, false);

            // Left edge and top-left corner
            _canvas.LineTo(x, y + radius);
            _canvas.Arc(x + radius, y + radius, radius, Math.PI, 3 * Math.PI / 2, false);

            _canvas.ClosePath();
            _canvas.SetFillColor(color);
            _canvas.Fill();
        }

        #endregion

    }
}
