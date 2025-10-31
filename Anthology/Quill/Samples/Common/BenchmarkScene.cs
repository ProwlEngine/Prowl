using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Scribe.Internal;
using Prowl.Vector;
using Prowl.Vector.Spatial;
using System.Drawing;
using Color = Prowl.Vector.Color;

namespace Common
{
    internal class BenchmarkScene : IDemo
    {
        private Canvas _canvas;
        private float _width;
        private float _height;
        private float _time;

        // Benchmark parameters
        private const int RECT_COUNT = 79000;
        private const int CIRCLE_COUNT = 1000;
        private const float MAX_SIZE = 20.0f;
        private const float MIN_SIZE = 5.0f;

        // Performance tracking
        private Queue<float> _frameTimeHistory = new Queue<float>();
        private const int MAX_HISTORY_SAMPLES = 60;
        private float _averageFrameTime = 0;

        private FontFile _font;

        public BenchmarkScene(Canvas canvas, FontFile font, float width, float height)
        {
            _canvas = canvas;
            _font = font;

            _width = width;
            _height = height;
        }

        public void RenderFrame(float deltaTime, Float2 offset, float zoom, float rotate)
        {
            _time += deltaTime;

            // Track performance
            UpdatePerformanceMetrics(deltaTime);

            // Clear the canvas
            _canvas.ResetState();
            _canvas.SaveState();

            _canvas.TransformBy(Transform2D.CreateTranslation(_width / 2, _height / 2));
            _canvas.TransformBy(Transform2D.CreateTranslation(offset.X, offset.Y) * Transform2D.CreateRotation(rotate) * Transform2D.CreateScale(zoom, zoom));
            _canvas.SetStrokeScale(zoom);

            // Run the benchmark
            DrawBenchmarkShapes();
            _canvas.RestoreState();

            // Draw performance overlay
            DrawPerformanceOverlay();
        }

        private uint _randomState = 42;

        private float NextFloat()
        {
            _randomState = _randomState * 1103515245u + 12345u;
            return (_randomState >> 8) * (1.0f / 16777216.0f);
        }

        private void DrawBenchmarkShapes()
        {
            // Move Random outside and reuse - creating Random is expensive
            //var _random = new System.Random(42);
            _randomState = 42;

            // Precompute time-dependent values once
            float timeComponent = _time * 30;
            float colorTimeR = _time * 2;
            float colorTimeG = _time * 1.5f;
            float colorTimeB = _time * 1.8f;

            // Cache size range
            float sizeRange = MAX_SIZE - MIN_SIZE;
            float radiusMin = MIN_SIZE / 2;
            float radiusRange = (MAX_SIZE - MIN_SIZE) / 2;

            _canvas.SaveState();
            var curTransform = _canvas.GetTransform();

            // Draw rectangles
            for (int i = 0; i < RECT_COUNT; i++)
            {
                // Precompute values instead of calling multiple times
                float x = NextFloat() * _width;
                float y = NextFloat() * _height;
                float rotation = (NextFloat() * 360) + (timeComponent * (i % 10));
                float scale = 0.5f + NextFloat() * 1.5f;
                float width = MIN_SIZE + NextFloat() * sizeRange;
                float height = MIN_SIZE + NextFloat() * sizeRange;

                // Optimize color calculations
                float iOffset = i * 0.01f;
                byte r = (byte)(128 + 127 * Maths.Sin(colorTimeR + iOffset));
                byte g = (byte)(128 + 127 * Maths.Sin(colorTimeG + i * 0.015));
                byte b = (byte)(128 + 127 * Maths.Sin(colorTimeB + i * 0.008));

                _canvas.CurrentTransform(curTransform);
                _canvas.TransformBy(Transform2D.CreateTranslation(x, y));
                _canvas.TransformBy(Transform2D.CreateRotation(rotation));
                _canvas.TransformBy(Transform2D.CreateScale(scale, scale));
                _canvas.RectFilled(-width / 2, -height / 2, width, height, Color32.FromArgb(180, r, g, b));
            }

            float circleColorTimeR = _time * 1.2f;
            float circleColorTimeG = _time * 2.1f;
            float circleColorTimeB = _time * 1.7f;
            float scaleTime = _time * 3;

            for (int i = 0; i < CIRCLE_COUNT; i++)
            {
                float x = NextFloat() * _width;
                float y = NextFloat() * _height;
                float baseScale = 0.3f + NextFloat() * 1.2f;
                float animScale = 1.0f + 0.2f * Maths.Sin(scaleTime + i * 0.02f);
                float totalScale = baseScale * animScale;
                float radius = radiusMin + NextFloat() * radiusRange;

                float iOffset = i * 0.012f;
                byte r = (byte)(128 + 127 * Maths.Cos(circleColorTimeR + iOffset));
                byte g = (byte)(128 + 127 * Maths.Cos(circleColorTimeG + i * 0.008));
                byte b = (byte)(128 + 127 * Maths.Cos(circleColorTimeB + i * 0.020));

                _canvas.CurrentTransform(curTransform);
                _canvas.TransformBy(Transform2D.CreateTranslation(x, y));
                _canvas.TransformBy(Transform2D.CreateScale(totalScale, totalScale));
                _canvas.CircleFilled(0, 0, radius, Color32.FromArgb(160, r, g, b));
            }

            _canvas.RestoreState();
        }

        private void UpdatePerformanceMetrics(float deltaTime)
        {
            _frameTimeHistory.Enqueue(deltaTime * 1000); // Convert to milliseconds

            if (_frameTimeHistory.Count > MAX_HISTORY_SAMPLES)
                _frameTimeHistory.Dequeue();

            _averageFrameTime = _frameTimeHistory.Count > 0 ? _frameTimeHistory.Average() : 0;
        }

        private void DrawPerformanceOverlay()
        {
            // Performance overlay background
            float overlayWidth = 300;
            float overlayHeight = 80;
            float padding = 10;
            float x = _width - overlayWidth - padding;
            float y = padding;

            _canvas.RectFilled(x, y, overlayWidth, overlayHeight, Color32.FromArgb(255, 0, 0, 0));

            // Border
            _canvas.SetStrokeColor(Color32.FromArgb(255, 100, 100, 100));
            _canvas.SetStrokeWidth(1);
            _canvas.Rect(x, y, overlayWidth, overlayHeight);
            _canvas.Stroke();

            // Performance text
            float fps = _averageFrameTime > 0 ? 1000.0f / _averageFrameTime : 0f;
            string perfText = $"BENCHMARK: {RECT_COUNT + CIRCLE_COUNT} SHAPES";
            string fpsText = $"FPS: {fps:F1}";
            string frameTimeText = $"Frame Time: {_averageFrameTime:F2} ms";

            DrawSimpleText(perfText, x + 10, y + 15, Color32.FromArgb(255, 255, 255, 100));
            DrawSimpleText(fpsText, x + 10, y + 35, Color32.FromArgb(255, 100, 255, 100));
            DrawSimpleText(frameTimeText, x + 10, y + 55, Color.White);

            Console.Title = $"FPS: {fps:F1} | Frame Time: {_averageFrameTime:F2} ms | Shapes: {RECT_COUNT + CIRCLE_COUNT}";
        }

        private void DrawSimpleText(string text, float x, float y, Color32 color)
        {
            _canvas.DrawText(text, x, y, color, 17, _font);
        }
    }
}
