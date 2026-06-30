using System.Collections.Generic;

namespace Prowl.Scribe.Sdf
{
    internal sealed class Contour
    {
        public readonly List<EdgeSegment> Edges = new List<EdgeSegment>();

        public void AddEdge(EdgeSegment edge) => Edges.Add(edge);

        public void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            foreach (var edge in Edges)
                edge.Bound(ref xMin, ref yMin, ref xMax, ref yMax);
        }
    }

    internal sealed class Shape
    {
        public readonly List<Contour> Contours = new List<Contour>();

        public Contour AddContour()
        {
            var c = new Contour();
            Contours.Add(c);
            return c;
        }

        public void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            foreach (var c in Contours)
                c.Bound(ref xMin, ref yMin, ref xMax, ref yMax);
        }
    }
}
