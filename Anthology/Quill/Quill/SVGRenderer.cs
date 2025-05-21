using Prowl.Vector;
using System;
using System.Drawing;

namespace Prowl.Quill
{
    public static class SVGRenderer
    {
        public static Color currentColor = Color.White;

        //for debug
        //static bool printed = false;

        public static void DrawToCanvas(Canvas canvas, Vector2 position, SvgElement svgElement)
        {
            //if (!printed)
            //    Console.WriteLine("DrawToCanvas");
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

                ApplyFillStroke(canvas, element);
            }
            //printed = true;
        }

        static void ApplyFillStroke(Canvas canvas, SvgElement element)
        {
            if (element.fillType != SvgElement.ColorType.none)
                canvas.FillComplex();

            if (element.strokeType != SvgElement.ColorType.none)
                canvas.Stroke();
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
                    canvas.SetFillColor(pathElement.stroke);
                    break;
                case SvgElement.ColorType.currentColor:
                    canvas.SetFillColor(currentColor);
                    break;
            }

            canvas.SetStrokeWidth(pathElement.strokeWidth);
        }

        static void DrawPath(Canvas canvas, Vector2 position, SvgPathElement pathElement)
        {
            if (pathElement.drawCommands == null)
                return;

            canvas.BeginPath();
            var lastControlPoint = Vector2.zero;

            for (var i = 0; i < pathElement.drawCommands.Length; i++)
            {
                var cmd = pathElement.drawCommands[i];
                var offset = cmd.relative ? canvas.CurrentPoint : position;
                var cp = ReflectPoint(canvas.CurrentPoint, lastControlPoint);
                //if (!printed)
                //{
                //    Console.WriteLine(offset);
                //    Console.WriteLine(cmd);
                //}

                switch (cmd.type)
                {
                    case DrawType.MoveTo:
                        canvas.MoveTo(offset.x + cmd.param[0], offset.y + cmd.param[1]);
                        break;
                    case DrawType.LineTo:
                        canvas.LineTo(offset.x + cmd.param[0], offset.y + cmd.param[1]);
                        break;
                    case DrawType.HorizontalLineTo:
                        canvas.LineTo(offset.x + cmd.param[0], canvas.CurrentPoint.y);
                        break;
                    case DrawType.VerticalLineTo:
                        canvas.LineTo(canvas.CurrentPoint.x, offset.y + cmd.param[0]);
                        break;
                    case DrawType.QuadraticCurveTo:
                        canvas.QuadraticCurveTo(offset.x + cmd.param[0], offset.y + cmd.param[1], offset.x + cmd.param[2], offset.y + cmd.param[3]);
                        lastControlPoint = new Vector2(offset.x + cmd.param[0], offset.y + cmd.param[1]);
                        break;
                    case DrawType.SmoothQuadraticCurveTo:
                        canvas.QuadraticCurveTo(cp.x, cp.y, offset.x + cmd.param[0], offset.y + cmd.param[1]);
                        lastControlPoint = new Vector2(offset.x + cmd.param[0], offset.y + cmd.param[1]);
                        break;
                    case DrawType.CubicCurveTo:
                        canvas.BezierCurveTo(offset.x + cmd.param[0], offset.y + cmd.param[1], offset.x + cmd.param[2], offset.y + cmd.param[3], offset.x + cmd.param[4], offset.y + cmd.param[5]);
                        lastControlPoint = new Vector2(offset.x + cmd.param[2], offset.y + cmd.param[3]);
                        break;
                    case DrawType.SmoothCubicCurveTo:
                        canvas.BezierCurveTo(cp.x, cp.y, offset.x + cmd.param[0], offset.y + cmd.param[1], offset.x + cmd.param[2], offset.y + cmd.param[3]);
                        lastControlPoint = new Vector2(offset.x + cmd.param[0], offset.y + cmd.param[1]);
                        break;
                    case DrawType.ArcTo:
                        canvas.EllipticalArcTo(cmd.param[0], cmd.param[1], cmd.param[2], cmd.param[3], cmd.param[4], offset.x + cmd.param[5], offset.y + cmd.param[6]);
                        break;
                    case DrawType.ClosePath:
                        canvas.ClosePath();
                        break;
                }
            }
        }

        static Vector2 ReflectPoint(Vector2 mirrorPoint, Vector2 inputPoint)
        {
            return 2 * mirrorPoint - inputPoint;
        }

        static void DrawCircle(Canvas canvas, Vector2 position, SvgCircleElement element)
        {
            var pos = position + new Vector2(element.cx, element.cy);

            if (element.fillType != SvgElement.ColorType.none)
                canvas.CircleFilled(pos.x, pos.y, element.r, element.fill);
            else
                canvas.Circle(pos.x, pos.y, element.r);
        }

        static void DrawRect(Canvas canvas, Vector2 position, SvgRectElement element)
        {
            var pos = element.pos;
            var size = element.size;

            if (element.radius.x == 0)
            {
                if (element.fillType != SvgElement.ColorType.none)
                    canvas.Rect(pos.x, pos.y, size.x, size.y);
                else
                    canvas.RectFilled(pos.x, pos.y, size.x, size.y, element.fill);
            }
            else
            {
                if (element.fillType != SvgElement.ColorType.none)
                    canvas.RoundedRect(pos.x, pos.y, size.x, size.y, element.radius.x);
                else
                    canvas.RoundedRectFilled(pos.x, pos.y, size.x, size.y, element.radius.x, element.fill);
            }
        }
    }
}
