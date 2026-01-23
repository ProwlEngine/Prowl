using Prowl.Vector;
using System;
using Color = Prowl.Vector.Color;

namespace Prowl.Quill
{
    /// <summary>
    /// Provides methods for rendering parsed SVG elements to a Canvas.
    /// </summary>
    public static class SVGRenderer
    {
        /// <summary>
        /// The current color used when SVG elements specify "currentColor".
        /// </summary>
        public static Color32 currentColor = Color.White;

        /// <summary>
        /// When true, outputs debug information during rendering.
        /// </summary>
        public static bool debug;

        /// <summary>
        /// Draws an SVG element and all its children to a canvas at the specified position.
        /// </summary>
        /// <param name="canvas">The canvas to draw to.</param>
        /// <param name="position">The position offset for rendering.</param>
        /// <param name="svgElement">The root SVG element to render.</param>
        public static void DrawToCanvas(Canvas canvas, Float2 position, SvgElement svgElement)
        {
            var elements = svgElement.Flatten();

            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];

                SetState(canvas, element);

                if (element is SvgPathElement pathElement)
                    DrawPath(canvas, position, pathElement);
                else if (element is SvgCircleElement circleElement)
                    DrawCircle(canvas, position, circleElement);
                else if (element is SvgRectElement rectElement)
                    DrawRect(canvas, position, rectElement);
                else if (element is SvgLineElement lineElement)
                    DrawLine(canvas, position, lineElement);
                else if (element is SvgEllipseElement ellipseElement)
                    DrawEllipse(canvas, position, ellipseElement);
                else if (element is SvgPolylineElement polylineElement)
                    DrawPolyline(canvas, position, polylineElement);
                else if (element is SvgPolygonElement polygonElement)
                    DrawPolygon(canvas, position, polygonElement);
            }
            debug = false;
        }

        static void SetState(Canvas canvas, SvgElement pathElement)
        {
            switch (pathElement.strokeType)
            {
                case SvgElement.ColorType.specific:
                    canvas.SetStrokeColor(pathElement.stroke);
                    break;
                case SvgElement.ColorType.currentColor:
                    canvas.SetStrokeColor(currentColor);
                    break;
            }
            switch (pathElement.fillType)
            {
                case SvgElement.ColorType.specific:
                    canvas.SetFillColor(pathElement.fill);
                    break;
                case SvgElement.ColorType.currentColor:
                    canvas.SetFillColor(currentColor);
                    break;
            }

            canvas.SetStrokeWidth(pathElement.strokeWidth);
        }

        static void DrawPath(Canvas canvas, Float2 position, SvgPathElement element)
        {
            if (element.drawCommands == null)
                return;

            if (element.fillType != SvgElement.ColorType.none)
            {
                DrawElement(canvas, element, position);
                canvas.FillComplexAA();
            }

            if (element.strokeType != SvgElement.ColorType.none)
            {
                DrawElement(canvas, element, position);
                canvas.Stroke();
            }
        }

        static void DrawElement(Canvas canvas, SvgPathElement element, Float2 position)
        {
            canvas.BeginPath();
            var lastControlPoint = Float2.Zero;

            for (var i = 0; i < element.drawCommands.Length; i++)
            {
                var cmd = element.drawCommands[i];
                var currentPoint = i == 0 ? position : canvas.CurrentPoint;
                var offset = cmd.relative ? currentPoint : position;

                var cp = ReflectPoint(canvas.CurrentPoint, lastControlPoint);
                if (debug)
                    Console.WriteLine($"[{i}]{offset} cmd:{cmd}");

                switch (cmd.type)
                {
                    case DrawType.MoveTo:
                        canvas.MoveTo(offset.X + cmd.param[0], offset.Y + cmd.param[1]);
                        break;
                    case DrawType.LineTo:
                        canvas.LineTo(offset.X + cmd.param[0], offset.Y + cmd.param[1]);
                        break;
                    case DrawType.HorizontalLineTo:
                        canvas.LineTo(offset.X + cmd.param[0], canvas.CurrentPoint.Y);
                        break;
                    case DrawType.VerticalLineTo:
                        canvas.LineTo(canvas.CurrentPoint.X, offset.Y + cmd.param[0]);
                        break;
                    case DrawType.QuadraticCurveTo:
                        canvas.QuadraticCurveTo(offset.X + cmd.param[0], offset.Y + cmd.param[1], offset.X + cmd.param[2], offset.Y + cmd.param[3]);
                        lastControlPoint = new Float2(offset.X + cmd.param[0], offset.Y + cmd.param[1]);
                        break;
                    case DrawType.SmoothQuadraticCurveTo:
                        canvas.QuadraticCurveTo(cp.X, cp.Y, offset.X + cmd.param[0], offset.Y + cmd.param[1]);
                        lastControlPoint = new Float2(offset.X + cmd.param[0], offset.Y + cmd.param[1]);
                        break;
                    case DrawType.CubicCurveTo:
                        canvas.BezierCurveTo(offset.X + cmd.param[0], offset.Y + cmd.param[1], offset.X + cmd.param[2], offset.Y + cmd.param[3], offset.X + cmd.param[4], offset.Y + cmd.param[5]);
                        lastControlPoint = new Float2(offset.X + cmd.param[2], offset.Y + cmd.param[3]);
                        break;
                    case DrawType.SmoothCubicCurveTo:
                        canvas.BezierCurveTo(cp.X, cp.Y, offset.X + cmd.param[0], offset.Y + cmd.param[1], offset.X + cmd.param[2], offset.Y + cmd.param[3]);
                        lastControlPoint = new Float2(offset.X + cmd.param[0], offset.Y + cmd.param[1]);
                        break;
                    case DrawType.ArcTo:
                        canvas.EllipticalArcTo(cmd.param[0], cmd.param[1], cmd.param[2], cmd.param[3] != 0, cmd.param[4] != 0, offset.X + cmd.param[5], offset.Y + cmd.param[6]);
                        break;
                    case DrawType.ClosePath:
                        canvas.ClosePath();
                        break;
                }
            }
        }

        static Float2 ReflectPoint(Float2 mirrorPoint, Float2 inputPoint)
        {
            return 2 * mirrorPoint - inputPoint;
        }

        static void DrawCircle(Canvas canvas, Float2 position, SvgCircleElement element)
        {
            var pos = position + new Float2(element.cx, element.cy);

            if (element.fillType != SvgElement.ColorType.none)
            {
                canvas.CircleFilled(pos.X, pos.Y, element.r, element.fill);
                canvas.FillComplexAA();
            }
            else
            {
                canvas.Circle(pos.X, pos.Y, element.r);
                canvas.Stroke();
            }
        }

        static void DrawRect(Canvas canvas, Float2 position, SvgRectElement element)
        {
            var pos = element.pos;
            var size = element.size;

            if (element.radius.X == 0)
            {
                if (element.fillType != SvgElement.ColorType.none)
                {
                    canvas.Rect(pos.X, pos.Y, size.X, size.Y);
                    canvas.FillComplexAA();
                }
                else
                {
                    canvas.RectFilled(pos.X, pos.Y, size.X, size.Y, element.fill);
                    canvas.Stroke();
                }
            }
            else
            {
                if (element.fillType != SvgElement.ColorType.none)
                {
                    canvas.RoundedRect(pos.X, pos.Y, size.X, size.Y, element.radius.X);
                    canvas.FillComplexAA();
                }
                else
                {
                    canvas.RoundedRectFilled(pos.X, pos.Y, size.X, size.Y, element.radius.X, element.fill);
                    canvas.Stroke();
                }
            }
        }

        static void DrawLine(Canvas canvas, Float2 position, SvgLineElement element)
        {
            if (element.strokeType == SvgElement.ColorType.none)
                return;

            canvas.BeginPath();
            canvas.MoveTo(position.X + element.x1, position.Y + element.y1);
            canvas.LineTo(position.X + element.x2, position.Y + element.y2);
            canvas.Stroke();
        }

        static void DrawEllipse(Canvas canvas, Float2 position, SvgEllipseElement element)
        {
            var cx = position.X + element.cx;
            var cy = position.Y + element.cy;

            if (element.fillType != SvgElement.ColorType.none)
            {
                canvas.BeginPath();
                canvas.Ellipse(cx, cy, element.rx, element.ry);
                canvas.FillComplexAA();
            }

            if (element.strokeType != SvgElement.ColorType.none)
            {
                canvas.BeginPath();
                canvas.Ellipse(cx, cy, element.rx, element.ry);
                canvas.Stroke();
            }
        }

        static void DrawPolyline(Canvas canvas, Float2 position, SvgPolylineElement element)
        {
            DrawPoly(canvas, position, element.points, element, false);
        }

        static void DrawPolygon(Canvas canvas, Float2 position, SvgPolygonElement element)
        {
            DrawPoly(canvas, position, element.points, element, true);
        }

        static void DrawPoly(Canvas canvas, Float2 position, Float2[] points, SvgElement element, bool closed)
        {
            if (points == null || points.Length == 0)
                return;

            if (element.fillType != SvgElement.ColorType.none)
            {
                canvas.BeginPath();
                canvas.MoveTo(position.X + points[0].X, position.Y + points[0].Y);
                for (int i = 1; i < points.Length; i++)
                    canvas.LineTo(position.X + points[i].X, position.Y + points[i].Y);
                if (closed)
                    canvas.ClosePath();
                canvas.FillComplexAA();
            }

            if (element.strokeType != SvgElement.ColorType.none)
            {
                canvas.BeginPath();
                canvas.MoveTo(position.X + points[0].X, position.Y + points[0].Y);
                for (int i = 1; i < points.Length; i++)
                    canvas.LineTo(position.X + points[i].X, position.Y + points[i].Y);
                if (closed)
                    canvas.ClosePath();
                canvas.Stroke();
            }
        }
    }
}
