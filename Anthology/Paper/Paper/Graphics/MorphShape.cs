using System.Numerics;

namespace Prowl.PaperUI.Graphics
{
    /// <summary>
    /// Represents a shape that can be used for morphing operations
    /// </summary>
    public class MorphShape
    {
        internal List<Command> Commands { get; private set; } = new List<Command>();

        #region Public Methods

        /// <summary>
        /// Precomputes the data needed for efficient morphing between two shapes
        /// </summary>
        /// <param name="fromShape">Source shape</param>
        /// <param name="toShape">Target shape</param>
        /// <param name="maxSegmentLength">Maximum segment length for point sampling</param>
        /// <returns>A CompiledMorph object for efficient runtime morphing</returns>
        public static CompiledMorph Compile(MorphShape fromShape, MorphShape toShape, int maxSegmentLength = 10)
        {
            // Extract points from both shapes
            var fromPoints = fromShape.CalculatePoints();
            var toPoints = toShape.CalculatePoints();

            // Normalize the rings (ensure they have the same number of points)
            var normalizedFrom = NormalizeRing(fromPoints, maxSegmentLength);
            var normalizedTo = NormalizeRing(toPoints, maxSegmentLength);

            // Add points if needed to make both rings have the same length
            int diff = normalizedFrom.Count - normalizedTo.Count;
            if (diff < 0)
            {
                AddPoints(normalizedFrom, -diff);
            }
            else if (diff > 0)
            {
                AddPoints(normalizedTo, diff);
            }

            // Rotate points to minimize distance between corresponding points
            RotateRing(normalizedFrom, normalizedTo);

            // Return the compiled morph with the preprocessed data
            return new CompiledMorph(fromShape, toShape, normalizedFrom, normalizedTo);
        }

        #endregion

        #region Private Methods

        private List<Vector2> CalculatePoints()
        {
            var points = new List<Vector2>();
            float currentX = 0, currentY = 0;

            foreach (var cmd in Commands)
            {
                switch (cmd.Type)
                {
                    case CommandType.MoveTo:
                        currentX = cmd.P1;
                        currentY = cmd.P2;
                        break;

                    case CommandType.LineTo:
                        points.Add(new Vector2(currentX, currentY));
                        currentX = cmd.P1;
                        currentY = cmd.P2;
                        points.Add(new Vector2(currentX, currentY));
                        break;

                    case CommandType.BezierTo:
                        // Sample points along the bezier curve
                        float startX = currentX;
                        float startY = currentY;
                        float c1x = cmd.P1, c1y = cmd.P2;
                        float c2x = cmd.P3, c2y = cmd.P4;
                        float endX = cmd.P5, endY = cmd.P6;

                        // Sample multiple points along the curve
                        const int segments = 5;
                        for (int i = 1; i <= segments; i++)
                        {
                            float t = i / (float)segments;

                            float u = 1 - t;
                            float tt = t * t;
                            float uu = u * u;
                            float uuu = uu * u;
                            float ttt = tt * t;

                            float x = uuu * startX +
                                     3 * uu * t * c1x +
                                     3 * u * tt * c2x +
                                     ttt * endX;

                            float y = uuu * startY +
                                     3 * uu * t * c1y +
                                     3 * u * tt * c2y +
                                     ttt * endY;

                            points.Add(new Vector2(x, y));
                        }

                        currentX = endX;
                        currentY = endY;
                        break;

                    case CommandType.Close:
                        // If we have points and the last point doesn't match the first,
                        // add the first point again to close the path
                        if (points.Count > 0 &&
                            (points[0].X != currentX || points[0].Y != currentY))
                        {
                            //points.Add(new Vector2(points[0].X, points[0].Y));
                        }
                        break;

                    case CommandType.Winding:
                        // No new points for winding command
                        break;
                }
            }

            return points;
        }

        private static List<Vector2> NormalizeRing(List<Vector2> ring, int maxSegmentLength)
        {
            var points = new List<Vector2>(ring);

            // If first and last points are the same, remove the last one
            if (points.Count > 1 && Vector2.Distance(points[0], points[points.Count - 1]) < 1e-6f)
            {
                points.RemoveAt(points.Count - 1);
            }

            // Make all rings clockwise
            if (PolygonArea(points) > 0)
            {
                points.Reverse();
            }

            // Bisect long segments
            if (maxSegmentLength > 0)
            {
                Bisect(points, maxSegmentLength);
            }

            return points;
        }

        private static void AddPoints(List<Vector2> ring, int numPoints)
        {
            if (numPoints <= 0) return;

            float perimeter = PolygonLength(ring);
            float step = perimeter / numPoints;

            int i = 0;
            float cursor = 0;
            float insertAt = step / 2;

            int desiredLength = ring.Count + numPoints;

            while (ring.Count < desiredLength)
            {
                Vector2 a = ring[i];
                Vector2 b = ring[(i + 1) % ring.Count];
                float segment = Vector2.Distance(a, b);

                if (insertAt <= cursor + segment)
                {
                    Vector2 point = segment > 0
                        ? PointAlong(a, b, (insertAt - cursor) / segment)
                        : a;

                    ring.Insert(i + 1, point);
                    insertAt += step;
                    continue;
                }

                cursor += segment;
                i++;
            }
        }

        private static void Bisect(List<Vector2> ring, float maxSegmentLength)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 a = ring[i];
                Vector2 b = i == ring.Count - 1 ? ring[0] : ring[i + 1];

                while (Vector2.Distance(a, b) > maxSegmentLength)
                {
                    b = PointAlong(a, b, 0.5f);
                    ring.Insert(i + 1, b);
                }
            }
        }

        private static float PolygonLength(List<Vector2> polygon)
        {
            float length = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                length += Vector2.Distance(a, b);
            }
            return length;
        }

        private static float PolygonArea(List<Vector2> polygon)
        {
            float area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                area += (a.X * b.Y) - (b.X * a.Y);
            }
            return area / 2;
        }

        private static Vector2 PointAlong(Vector2 a, Vector2 b, float pct)
        {
            return new Vector2(
                a.X + (b.X - a.X) * pct,
                a.Y + (b.Y - a.Y) * pct
            );
        }

        private static void RotateRing(List<Vector2> ring, List<Vector2> target)
        {
            int len = ring.Count;
            float minDistance = float.MaxValue;
            int bestOffset = 0;

            for (int offset = 0; offset < len; offset++)
            {
                float sumOfSquares = 0;

                for (int i = 0; i < target.Count; i++)
                {
                    float d = Vector2.Distance(ring[(offset + i) % len], target[i]);
                    sumOfSquares += d * d;
                }

                if (sumOfSquares < minDistance)
                {
                    minDistance = sumOfSquares;
                    bestOffset = offset;
                }
            }

            if (bestOffset > 0)
            {
                var spliced = ring.GetRange(0, bestOffset);
                ring.RemoveRange(0, bestOffset);
                ring.AddRange(spliced);
            }
        }

        #endregion
    }
}