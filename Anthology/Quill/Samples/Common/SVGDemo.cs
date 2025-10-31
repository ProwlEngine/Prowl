using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Spatial;
using System.Drawing;

namespace Common
{
    internal class SVGDemo : IDemo
    {
        private Canvas _canvas;
        private float _width;
        private float _height;

        List<SvgElement> svgElements;

        public SVGDemo(Canvas canvas, float width, float height)
        {
            _canvas = canvas;
            _width = width;
            _height = height;

            ParseSVG();
        }

        /// <summary>
        /// Updates and renders a frame
        /// </summary>
        public void RenderFrame(float deltaTime, Float2 offset, float zoom, float rotate)
        {
            _canvas.TransformBy(Transform2D.CreateTranslation(_width / 2, _height / 2));
            _canvas.TransformBy(Transform2D.CreateTranslation(offset.X, offset.Y) * Transform2D.CreateRotation(rotate) * Transform2D.CreateScale(zoom, zoom));
            _canvas.SetStrokeScale(zoom);

            //_canvas.SetTexture(_texture);
            //_canvas.Scissor(0, 0, 200, 200);

            DrawDemo2D();

            _canvas.ResetState();
        }

        public void ParseSVG()
        {
            //SVGRenderer.debug = true;
            svgElements = new List<SvgElement>();
            var svgFiles = Directory.GetFiles("SVGs/");

            foreach (var svgFile in svgFiles)
                if (Path.GetExtension(svgFile) == ".svg")
                {
                    var svgElement = SVGParser.ParseSVGDocument(svgFile);
                    svgElements.Add(svgElement);
                }
        }

        void DrawSVG()
        {
            _canvas.SetTessellationTolerance(0.01f);
            _canvas.SetRoundingMinDistance(0.5f);

            const int iconPerLine = 10;
            var offset = new Float2(30, 30);
            for (int i = 0; i < svgElements.Count; i++)
            {
                var x = i % iconPerLine;
                var y = i / iconPerLine;
                SVGRenderer.DrawToCanvas(_canvas, new Float2(x * 30, y * 30) + offset, svgElements[i]);
            }

            _canvas.ResetState();
        }

        private void DrawGrid(int x, int y, float cellSize, Color32 color)
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

        private void DrawEllipse(Color32 color)
        {
            _canvas.SetStrokeColor(color);
            _canvas.SetStrokeWidth(4);

            _canvas.Ellipse(0, 0, 250, 100);

            _canvas.Stroke();
        }

        private void DrawEllipseArc(Color32 color)
        {
            _canvas.SetStrokeColor(color);
            _canvas.SetStrokeWidth(4);

            _canvas.MoveTo(275, 175);
            _canvas.LineTo(275, 25);
            _canvas.EllipticalArcTo(150, 150, 0, false, false, 125, 175);
            _canvas.ClosePath();

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

            // Draw 2D grid for reference
            //DrawGrid(16, 17, 50, Color.FromArgb(40, 255, 255, 255));

            // Draw coordinate system at center
            DrawCoordinateSystem(0, 0, 50);
            //DrawEllipse(Color.Red);
            //DrawEllipseArc(Color.Green);

            DrawSVG();

            // Restore the canvas state
            _canvas.RestoreState();
        }
    }
}
