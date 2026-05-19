using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>Tight 2D oriented bounding box fit for a set of points.</summary>
internal readonly record struct OrientedBox2D(
    Double2 Center,
    Double2 AxisX,
    Double2 AxisY,
    Double2 Extent,
    double Area);

/// <summary>
/// Rotating-caliper OBB fitter. Walks the convex hull and tracks the tightest rectangle
/// at each edge orientation; returns whichever has the smallest area.
/// </summary>
internal static class OrientedBox2DFit
{
    public static OrientedBox2D Fit(Double2[] inputPoints, int pointCount)
    {
        Double2 axisX = new(1, 0);
        Double2 axisY = new(0, 1);
        Double2 center = default;
        Double2 minExtent = default;
        double minArea = 1e32;

        var points = new Double2[pointCount];
        System.Array.Copy(inputPoints, points, pointCount);
        int hullCount = ConvexHull2D.Build(points, pointCount);

        if (hullCount < 3 || PolygonArea(points, hullCount) < NumericHelpers.Tiny)
            return new OrientedBox2D(center, axisX, axisY, default, 0.0);

        // Seed with the axis-aligned box; calipers will refine from there.
        int[] extremaIndex = new int[4];
        {
            double[] extremaValue = { 1e32, -1e32, 1e32, -1e32 };
            for (int i = 0; i < hullCount; ++i)
            {
                double cx = points[i].X, cy = points[i].Y;
                Update(cx, i, ref extremaValue[0], ref extremaIndex[0], lessThan: true);
                Update(cx, i, ref extremaValue[1], ref extremaIndex[1], lessThan: false);
                Update(cy, i, ref extremaValue[2], ref extremaIndex[2], lessThan: true);
                Update(cy, i, ref extremaValue[3], ref extremaIndex[3], lessThan: false);
            }

            minArea = (extremaValue[1] - extremaValue[0]) * (extremaValue[3] - extremaValue[2]);
            minExtent = new Double2(extremaValue[1] - extremaValue[0], extremaValue[3] - extremaValue[2]);
            center = 0.5 * new Double2(extremaValue[1] + extremaValue[0], extremaValue[3] + extremaValue[2]);
        }

        Double2[] caliperStart = { new(0, -1), new(0, 1), new(1, 0), new(-1, 0) };
        Double2[] caliper = { caliperStart[0], caliperStart[1], caliperStart[2], caliperStart[3] };

        double rotation = 0.0;
        while (rotation < System.Math.PI * 0.5)
        {
            int[] nextExtrema =
            {
                extremaIndex[0] + 1 == hullCount ? 0 : extremaIndex[0] + 1,
                extremaIndex[1] + 1 == hullCount ? 0 : extremaIndex[1] + 1,
                extremaIndex[2] + 1 == hullCount ? 0 : extremaIndex[2] + 1,
                extremaIndex[3] + 1 == hullCount ? 0 : extremaIndex[3] + 1
            };

            Double2[] edges =
            {
                points[nextExtrema[0]] - points[extremaIndex[0]],
                points[nextExtrema[1]] - points[extremaIndex[1]],
                points[nextExtrema[2]] - points[extremaIndex[2]],
                points[nextExtrema[3]] - points[extremaIndex[3]]
            };

            double[] angles =
            {
                AngleBetween(caliper[0], edges[0]),
                AngleBetween(caliper[1], edges[1]),
                AngleBetween(caliper[2], edges[2]),
                AngleBetween(caliper[3], edges[3])
            };

            int minAngleI = ArgMin4(angles);
            double minAngle = angles[minAngleI];

            rotation += minAngle;

            // Degenerate edge: skip past it.
            if (Double2.Length(edges[minAngleI]) < NumericHelpers.Tiny)
            {
                extremaIndex[minAngleI] = nextExtrema[minAngleI];
                continue;
            }

            double cosA = System.Math.Cos(rotation);
            double sinA = System.Math.Sin(rotation);

            caliper[0] = Rotate(caliperStart[0], cosA, sinA);
            caliper[1] = Rotate(caliperStart[1], cosA, sinA);
            caliper[2] = Rotate(caliperStart[2], cosA, sinA);
            caliper[3] = Rotate(caliperStart[3], cosA, sinA);

            Double2 thisAxisX = Double2.Normalize(edges[minAngleI]);
            Double2 thisAxisY = new(-thisAxisX.Y, thisAxisX.X);

            Double2 linePoint = points[extremaIndex[minAngleI]];

            double[] xProjections =
            {
                ProjectOnto(points[extremaIndex[0]], linePoint, thisAxisX),
                ProjectOnto(points[extremaIndex[1]], linePoint, thisAxisX),
                ProjectOnto(points[extremaIndex[2]], linePoint, thisAxisX),
                ProjectOnto(points[extremaIndex[3]], linePoint, thisAxisX),
                ProjectOnto(points[nextExtrema[minAngleI]], linePoint, thisAxisX)
            };

            double[] yProjections =
            {
                ProjectOnto(points[extremaIndex[0]], linePoint, thisAxisY),
                ProjectOnto(points[extremaIndex[1]], linePoint, thisAxisY),
                ProjectOnto(points[extremaIndex[2]], linePoint, thisAxisY),
                ProjectOnto(points[extremaIndex[3]], linePoint, thisAxisY),
                ProjectOnto(points[nextExtrema[minAngleI]], linePoint, thisAxisY)
            };

            double minX = xProjections[ArgMin5(xProjections)], maxX = xProjections[ArgMax5(xProjections)];
            double minY = yProjections[ArgMin5(yProjections)], maxY = yProjections[ArgMax5(yProjections)];

            double area = (maxX - minX) * (maxY - minY);
            if (NumericHelpers.ApproxLess(area, minArea, 1e-6))
            {
                axisX = thisAxisX;
                axisY = thisAxisY;
                minArea = area;
                minExtent = new Double2(maxX - minX, maxY - minY);
                center = 0.5 * ((minX + maxX) * thisAxisX + (minY + maxY) * thisAxisY);
            }

            extremaIndex[minAngleI] = nextExtrema[minAngleI];
        }

        return new OrientedBox2D(center, axisX, axisY, minExtent, minArea);
    }

    private static double PolygonArea(Double2[] points, int count)
    {
        double area = 0.5 * System.Math.Abs(points[count - 1].X * points[0].Y - points[count - 1].Y * points[0].X);
        for (int i = 0; i < count - 1; ++i)
            area += 0.5 * System.Math.Abs(points[i].X * points[i + 1].Y - points[i].Y * points[i + 1].X);
        return area;
    }

    private static Double2 Rotate(Double2 v, double cosA, double sinA)
        => new(v.X * cosA - v.Y * sinA, v.X * sinA + v.Y * cosA);

    private static double AngleBetween(Double2 caliper, Double2 edge)
        => System.Math.Acos(System.Math.Clamp(Double2.Dot(caliper, Double2.Normalize(edge)), -1.0, 1.0));

    private static double ProjectOnto(Double2 point, Double2 linePoint, Double2 direction)
        => Double2.Dot(point - linePoint, direction);

    private static void Update(double value, int index, ref double extrema, ref int extremaIndex, bool lessThan)
    {
        bool replace = lessThan
            ? NumericHelpers.ApproxLess(value, extrema, 1e-6)
            : NumericHelpers.ApproxGreater(value, extrema, 1e-6);
        if (replace)
        {
            extrema = value;
            extremaIndex = index;
        }
    }

    private static int ArgMin4(double[] v)
    {
        int min01 = NumericHelpers.ApproxGreater(v[0], v[1], 1e-6) ? 1 : 0;
        int min23 = NumericHelpers.ApproxGreater(v[2], v[3], 1e-6) ? 3 : 2;
        return NumericHelpers.ApproxGreater(v[min01], v[min23], 1e-6) ? min23 : min01;
    }

    private static int ArgMin5(double[] v)
    {
        int min0123 = ArgMin4(v);
        return NumericHelpers.ApproxGreater(v[min0123], v[4], 1e-6) ? 4 : min0123;
    }

    private static int ArgMax5(double[] v)
    {
        int max01 = NumericHelpers.ApproxLess(v[0], v[1], 1e-6) ? 1 : 0;
        int max23 = NumericHelpers.ApproxLess(v[2], v[3], 1e-6) ? 3 : 2;
        int max0123 = NumericHelpers.ApproxLess(v[max01], v[max23], 1e-6) ? max23 : max01;
        return NumericHelpers.ApproxLess(v[max0123], v[4], 1e-6) ? 4 : max0123;
    }
}
