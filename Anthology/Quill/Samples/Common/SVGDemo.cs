using Prowl.Quill;
using Prowl.Vector;
using System.Drawing;

namespace Common
{
    internal class SVGDemo : IDemo
    {
        private Canvas _canvas;
        private double _width;
        private double _height;

        SvgElement _svgElement;

        public SVGDemo(Canvas canvas, double width, double height)
        {
            _canvas = canvas;
            _width = width;
            _height = height;

            ParseSVG();
        }

        /// <summary>
        /// Updates and renders a frame
        /// </summary>
        public void RenderFrame(double deltaTime, Vector2 offset, double zoom, double rotate)
        {
            _canvas.TransformBy(Transform2D.CreateTranslation(_width / 2, _height / 2));
            _canvas.TransformBy(Transform2D.CreateRotate(rotate) * Transform2D.CreateTranslation(offset.x, offset.y) * Transform2D.CreateScale(zoom, zoom));
            _canvas.SetStrokeScale(zoom);

            //_canvas.SetTexture(_texture);
            //_canvas.Scissor(0, 0, 200, 200);

            DrawDemo2D();

            _canvas.ResetState();
        }

        void ParseSVG()
        {
            //const string input = "../../../../Common/SVGs/mlc.svg";
            //const string input = "../../../../Common/SVGs/bezier.svg";
            //const string input = "../../../../Common/SVGs/bx--calendar-x.svg";
            //const string input = "../../../../Common/SVGs/bx--arrow-to-top.svg";
            const string input = "../../../../Common/SVGs/bx--wifi.svg";
            _svgElement = SVGParser.ParseSVGDocument(input);

        }

        void DrawSVG()
        {
            SVGRenderer.DrawToCanvas(_canvas, _svgElement);
        }

        private void DrawGrid(int x, int y, double cellSize, Color color)
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
            //DrawGrid(16, 17, 50, Color.FromArgb(40, 255, 255, 255));

            // Draw coordinate system at center
            DrawCoordinateSystem(0, 0, 50);

            DrawSVG();

            // Restore the canvas state
            _canvas.RestoreState();
        }
    }
}
