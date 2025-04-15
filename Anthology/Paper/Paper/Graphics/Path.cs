using System.Collections.Generic;

namespace Prowl.PaperUI.Graphics
{
	internal class Path
	{
		public bool Closed;
		public int BevelCount;
		public int FillOffset, FillCount;
		public int StrokeOffset, StrokeCount;
		public Winding Winding;
		public bool Convex;
		public readonly List<CanvasPoint> Points = [];

		public CanvasPoint this[int index]
		{
			get => Points[index];
			set => Points[index] = value;
		}

		public int Count => Points.Count;

		public CanvasPoint FirstPoint { get => Points[0]; set => Points[0] = value; }
        public CanvasPoint LastPoint { get => Points[Count - 1]; set => Points[Count - 1] = value; }
    }
}
