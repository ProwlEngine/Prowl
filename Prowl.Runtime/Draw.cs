using Prowl.Runtime.Components;
using Raylib_cs;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace Prowl.Runtime
{
    public static class Draw
    {
        struct LineData
        {
            public Vector3 start;
            public Vector3 end;
            public Color color;
        }

        private readonly static List<LineData> lines = new();

        public static void Line(Vector3 pointA, Vector3 pointB, Color color)
        {
            lines.Add(new LineData() { start = pointA, end = pointB, color = color });
        }

        public static void Render(Camera cam)
        {
#warning TODO: Implement
        }

        public static void Clear()
        {
            lines.Clear();
        }
    }
}
