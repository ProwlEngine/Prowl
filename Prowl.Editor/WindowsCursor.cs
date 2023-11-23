using System.Drawing;
using System.Runtime.InteropServices;

namespace Prowl.Editor;

internal static class CursorPosition {
    [StructLayout(LayoutKind.Sequential)]
    public struct PointInter {
        public int X;
        public int Y;
        public static explicit operator System.Drawing.Point(PointInter point) => new System.Drawing.Point(point.X, point.Y);
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out PointInter lpPoint);

    // For your convenience
    public static System.Drawing.Point GetCursorPosition() {
        PointInter lpPoint;
        GetCursorPos(out lpPoint);
        return (System.Drawing.Point)lpPoint;
    }
}

