using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// 2D convex hull construction. Implements Andrew's monotone-chain method; the resulting hull
/// is used as the polygon the rotating-caliper OBB fitter walks around.
/// </summary>
internal static class ConvexHull2D
{
    /// <summary>
    /// Build the convex hull of <paramref name="points"/> in place.
    /// On return, the first <c>hullCount</c> entries of <paramref name="points"/> form the
    /// hull in counter-clockwise order.
    /// </summary>
    public static int Build(Double2[] points, int pointCount)
    {
        if (pointCount <= 3) return pointCount;

        var hull = new Double2[pointCount + 1];
        int hullCount = 0;

        // Lexicographic sort (x ascending, y as tie-breaker).
        var sorted = new Double2[pointCount];
        System.Array.Copy(points, sorted, pointCount);
        System.Array.Sort(sorted, LexicographicCompare);
        System.Array.Copy(sorted, points, pointCount);

        // Lower hull: scan left-to-right, popping points that would form a right turn.
        for (int i = 0; i < pointCount; ++i)
        {
            while (hullCount >= 2 && SignedDoubledArea(hull[hullCount - 2], hull[hullCount - 1], points[i]) < NumericHelpers.Tiny)
                --hullCount;
            hull[hullCount++] = points[i];
        }

        // Upper hull: scan right-to-left starting one before the rightmost point.
        int stopHull = hullCount + 1;
        for (int i = pointCount - 2; i >= 0; --i)
        {
            while (hullCount >= stopHull && SignedDoubledArea(hull[hullCount - 2], hull[hullCount - 1], points[i]) < NumericHelpers.Tiny)
                --hullCount;
            hull[hullCount++] = points[i];
        }

        // The first lower-hull vertex and last upper-hull vertex coincide.
        --hullCount;

        for (int i = 0; i < hullCount; ++i)
            points[i] = hull[i];

        return hullCount;
    }

    private static int LexicographicCompare(Double2 a, Double2 b)
    {
        if (NumericHelpers.ApproxLess(a.X, b.X, 1e-6)) return -1;
        if (NumericHelpers.ApproxGreater(a.X, b.X, 1e-6)) return 1;
        if (NumericHelpers.ApproxLess(a.Y, b.Y, 1e-6)) return -1;
        if (NumericHelpers.ApproxGreater(a.Y, b.Y, 1e-6)) return 1;
        return 0;
    }

    /// <summary>Twice the signed area of the triangle (p0, p1, p2). Positive = CCW.</summary>
    private static double SignedDoubledArea(Double2 p0, Double2 p1, Double2 p2)
        => (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
}
