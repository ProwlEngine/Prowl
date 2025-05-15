using System.Numerics;

namespace Prowl.Quill
{
    internal class SVGRenderer
    {
        private readonly Canvas _canvas;

        public SVGRenderer(Canvas canvas) => _canvas = canvas;
        DrawCommand[] drawCommands;

        public DrawCommand[] ParseSVG(string svgData)
        {
            //use anglesharp to parse svg document and create a DrawCommand array
            //because we don't want to parse the svg text every frame

            return new DrawCommand[0];
        }

        public void RenderSVG()
        {
            //render the svg every frame

            for (int i = 0; i < drawCommands.Length; i++)
            {
                var command = drawCommands[i];
                var data = command.data;
                switch (command.type)
                {
                    case DrawType.MoveTo:
                        _canvas.MoveTo(data.X, data.Y);
                        break;
                    case DrawType.LineTo:
                        _canvas.LineTo(data.X, data.Y);
                        break;
                    case DrawType.QuadraticBezier:
                        _canvas.QuadraticCurveTo(data.X, data.Y, data.Z, data.W);
                        break;
                    case DrawType.ClosePath:
                        _canvas.ClosePath();
                        break;
                }
            }
        }

        public struct DrawCommand
        {
            public DrawType type;
            public Vector4 data;
        }
    }


    public enum DrawType
    {
        MoveTo,
        LineTo,
        QuadraticBezier,
        ClosePath
    }
}
