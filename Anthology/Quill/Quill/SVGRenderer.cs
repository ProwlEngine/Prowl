using System;
using System.IO;
using System.Numerics;

namespace Prowl.Quill
{
    public class SVGRenderer
    {
        private readonly Canvas _canvas;

        public SVGRenderer(Canvas canvas) => _canvas = canvas;

        public void DrawToCanvas()
        {
            //render the svg every frame

            //for (int i = 0; i < drawCommands.Length; i++)
            //{
            //    var command = drawCommands[i];
            //    var data = command.data;
            //    switch (command.type)
            //    {
            //        case DrawType.MoveTo:
            //            _canvas.MoveTo(data.X, data.Y);
            //            break;
            //        case DrawType.LineTo:
            //            _canvas.LineTo(data.X, data.Y);
            //            break;
            //        case DrawType.QuadraticBezier:
            //            _canvas.QuadraticCurveTo(data.X, data.Y, data.Z, data.W);
            //            break;
            //        case DrawType.ClosePath:
            //            _canvas.ClosePath();
            //            break;
            //    }
            //}
        }
    }
}
