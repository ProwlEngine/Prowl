using Prowl.Vector;
using System;

namespace Prowl.Quill
{
    public static class SVGRenderer
    {
        public static void DrawToCanvas(Canvas canvas, Vector2 position, SvgElement svgElement)
        {
            if (!printed)
                Console.WriteLine("DrawToCanvas");
            var elements = svgElement.Flatten();

            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];

                SetState(canvas, element);

                if (element is SvgPathElement pathElement)
                    DrawPath(canvas, position, pathElement);

                if (element is SvgCircleElement circleElement)
                    DrawCircle(canvas, position, circleElement);

            }
            printed = true;
        }

        static bool printed = false;

        static void SetState(Canvas canvas, SvgElement pathElement)
        {
            canvas.SetStrokeColor(pathElement.stroke);
            canvas.SetFillColor(pathElement.fill);
            canvas.SetStrokeWidth(pathElement.strokeWidth);
        }

        static void DrawPath(Canvas canvas, Vector2 position, SvgPathElement pathElement)
        {
            canvas.BeginPath();

            for (var i = 0; i < pathElement.drawCommands.Length; i++)
            {
                var cmd = pathElement.drawCommands[i];
                var offset = cmd.relative ? canvas.CurrentPoint : position;

                if (!printed)
                {
                    Console.WriteLine(offset);
                    Console.WriteLine(cmd);
                }

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
                        break;
                    case DrawType.BezierCurveTo:
                        canvas.BezierCurveTo(offset.x + cmd.param[0], offset.y + cmd.param[1], offset.x + cmd.param[2], offset.y + cmd.param[3], offset.x + cmd.param[4], offset.y + cmd.param[5]);
                        break;
                    case DrawType.ArcTo:
                        //todo add support for ellipse in canvas to fully support svg arc
                        canvas.ArcTo(offset.x, offset.y, offset.x + cmd.param[5], offset.y + cmd.param[6], cmd.param[0]);
                        break;
                    case DrawType.ClosePath:
                        canvas.ClosePath();

                        break;
                }
            }

            if (pathElement.hasFill)
                canvas.FillComplex();

            if (pathElement.hasStroke)
                canvas.Stroke();
        }

        static void DrawCircle(Canvas canvas, Vector2 position, SvgCircleElement element)
        {
            var pos = position + new Vector2(element.cx, element.cy);

            if (element.hasFill)
                canvas.CircleFilled(pos.x, pos.y, element.r, element.fill);
            else
                canvas.Circle(pos.x, pos.y, element.r);
        }
    }
}
