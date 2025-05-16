using Prowl.Vector;
using System;
using System.Drawing;

namespace Prowl.Quill
{
    public static class SVGRenderer
    {
        public static void DrawToCanvas(Canvas canvas, SvgElement svgElement)
        {
            var elements = svgElement.Flatten();

            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                //Console.WriteLine(element);

                if (element is SvgPathElement pathElement)
                    DrawPath(canvas, pathElement);
            }
        }

        static void DrawPath(Canvas canvas, SvgPathElement pathElement)
        {
            canvas.BeginPath();
            canvas.SetStrokeColor(pathElement.stroke);
            canvas.SetFillColor(pathElement.fill);

            for (var i = 0; i < pathElement.drawCommands.Length; i++)
            {
                var cmd = pathElement.drawCommands[i];
                var offset = cmd.relative ? canvas.CurrentPoint : Vector2.zero;
                //Console.WriteLine(offset);
                //Console.WriteLine(cmd);

                //switch (cmd.type)
                //{
                //    case DrawType.MoveTo:
                //        canvas.MoveTo(offset.x + cmd.param[0], offset.y + cmd.param[1]);
                //        break;
                //    case DrawType.LineTo:
                //        canvas.LineTo(offset.x + cmd.param[0], offset.y + cmd.param[1]);
                //        break;
                //    case DrawType.HorizontalLineTo:
                //        canvas.LineTo(offset.x + cmd.param[0], canvas.CurrentPoint.y);
                //        break;
                //    case DrawType.VerticalLineTo:
                //        canvas.LineTo(canvas.CurrentPoint.x, offset.y + cmd.param[0]);
                //        break;
                //    case DrawType.QuadraticCurveTo:
                //        canvas.QuadraticCurveTo(offset.x + cmd.param[0], offset.y + cmd.param[1], offset.x + cmd.param[2], offset.y + cmd.param[3]);
                //        break;
                //    case DrawType.BezierCurveTo:
                //        canvas.BezierCurveTo(offset.x + cmd.param[0], offset.y + cmd.param[1], offset.x + cmd.param[2], offset.y + cmd.param[3], offset.x + cmd.param[4], offset.y + cmd.param[5]);
                //        break;
                //    case DrawType.ClosePath:
                //        canvas.ClosePath();
                //        break;
                //}
            }
            canvas.Stroke();
        }
    }
}
