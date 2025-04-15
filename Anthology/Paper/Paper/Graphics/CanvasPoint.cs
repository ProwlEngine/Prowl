using System.Runtime.InteropServices;

namespace Prowl.PaperUI.Graphics
{
	[StructLayout(LayoutKind.Sequential)]
    internal struct CanvasPoint
	{
        public float X;
        public float Y;
        public float DeltaX;
		public float DeltaY;
        public float Length;
        public float Dmx;
        public float Dmy;
        public byte Flags;
    }
}