using Prowl.PaperUI;
using System.Runtime.InteropServices;

namespace Prowl.PaperUI.Graphics
{
    [StructLayout(LayoutKind.Sequential)]
	internal class CanvasState
	{
		public int ShapeAntiAlias;
		public Brush Fill;
		public Brush Stroke;
		public float StrokeWidth;
		public float MiterLimit;
		public LineCap LineJoin;
		public LineCap LineCap;
		public float Alpha;
		public Transform Transform = new Transform();
		public ScissorUniform Scissor;
        public Align TextAlign;

        public CanvasState Clone()
		{
			return new CanvasState
			{
				ShapeAntiAlias = ShapeAntiAlias,
				Fill = Fill,
				Stroke = Stroke,
				StrokeWidth = StrokeWidth,
				MiterLimit = MiterLimit,
				LineJoin = LineJoin,
				LineCap = LineCap,
				Alpha = Alpha,
				Transform = Transform,
				Scissor = Scissor,
                TextAlign = TextAlign
            };
		}
	}
}
