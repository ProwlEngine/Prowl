using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>Distortion summary used to accept or reject a parametrisation.</summary>
internal struct DistortionStats
{
    /// <summary>Average per-corner angular distortion (0 = perfect).</summary>
    public float MeanAngularDistortion;

    /// <summary>Area-weighted average angular distortion.</summary>
    public float TotalAngularDistortion;

    /// <summary>Average per-face area distortion (0 = perfect).</summary>
    public float MeanAreaDistortion;

    /// <summary>UV-area / bounding-rect-area; closer to 1 = tighter chart bounds.</summary>
    public float UvAreaRatio;
}

internal static class DistortionMetrics
{
    private const double AngleTinyRadians = 1.0 * (System.Math.PI / 180.0);

    public static void EvaluateFull(UvChart chart, out DistortionStats stats)
    {
        stats = default;

        int triCount = chart.Region!.Triangles.Length;
        var pos3D = new Double3[3 * triCount];
        for (int triI = 0; triI < triCount; ++triI)
        {
            int face = chart.Region.Triangles[triI];
            HalfEdge edge = chart.Region.Mesh.Triangles[face].FirstEdge!;
            for (int side = 0; side < 3; ++side)
            {
                pos3D[3 * triI + side] = edge.Apex!.Position;
                edge = edge.Next!;
            }
        }

        int angleCount = 3 * triCount;
        var srcArea = new double[triCount];
        var uvArea = new double[triCount];
        var srcAngle = new double[angleCount];
        var uvAngle = new double[angleCount];

        for (int triI = 0; triI < triCount; ++triI)
        {
            AnalyseTriangle3D(pos3D, 3 * triI, out srcArea[triI], srcAngle, 3 * triI);
            AnalyseTriangle2D(chart.UVs, 3 * triI, out uvArea[triI], uvAngle, 3 * triI);
        }

        var angleDiff = new double[angleCount];
        for (int a = 0; a < angleCount; ++a)
        {
            angleDiff[a] = srcAngle[a] > AngleTinyRadians
                ? System.Math.Abs(uvAngle[a] - srcAngle[a]) / System.Math.Abs(srcAngle[a])
                : 0.0;
        }

        double weightedAngleDiff = 0.0, totalArea = 0.0;
        for (int a = 0; a < angleCount; ++a)
        {
            weightedAngleDiff += angleDiff[a] * srcArea[a / 3];
            totalArea += srcArea[a / 3];
        }
        weightedAngleDiff /= totalArea;

        double meanAngleDiff = ArrayMean(angleDiff, angleCount);
        stats.MeanAngularDistortion = (float)meanAngleDiff;
        stats.TotalAngularDistortion = (float)weightedAngleDiff;

        // Area distortion: how much the per-triangle scale varies around the chart mean.
        var areaDiff = new double[triCount];
        double targetScale = 0.0, totalUvArea = 0.0;
        for (int triI = 0; triI < triCount; ++triI)
        {
            areaDiff[triI] = srcArea[triI] / uvArea[triI];
            targetScale += areaDiff[triI];
            totalUvArea += uvArea[triI];
        }
        targetScale /= triCount;

        for (int triI = 0; triI < triCount; ++triI)
            areaDiff[triI] = System.Math.Abs(1.0 - areaDiff[triI] / targetScale);

        stats.MeanAreaDistortion = (float)ArrayMean(areaDiff, triCount);

        Double2 rectExt = chart.UvMax - chart.UvMin;
        double rectArea = rectExt.X * rectExt.Y;
        stats.UvAreaRatio = (float)(rectArea < NumericHelpers.FloatTiny ? 1.0 : totalUvArea / rectArea);
    }

    /// <summary>Lighter variant: only the angular part, fed direct per-corner angles.</summary>
    public static void EvaluateAngularOnly(HalfEdgeMesh mesh, double[] angles, out DistortionStats stats)
    {
        stats = default;

        var angleDiff = new double[3 * mesh.Triangles.Count];
        for (int faceI = 0; faceI < mesh.Triangles.Count; ++faceI)
        {
            HalfEdge edge = mesh.Triangles[faceI].FirstEdge!;
            for (int side = 0; side < 3; ++side)
            {
                double uv = angles[3 * faceI + side];
                double src = MeshOps.CornerAngleAt(edge);
                angleDiff[3 * faceI + side] = src > AngleTinyRadians
                    ? System.Math.Abs(uv - src) / System.Math.Abs(src)
                    : 0.0;
                edge = edge.Next!;
            }
        }

        double weightedAngleDiff = 0.0, totalArea = 0.0;
        for (int a = 0; a < 3 * mesh.Triangles.Count; ++a)
        {
            weightedAngleDiff += angleDiff[a] * mesh.FaceAttributes[a / 3].Area;
            totalArea += mesh.FaceAttributes[a / 3].Area;
        }
        weightedAngleDiff /= totalArea;

        stats.MeanAngularDistortion = (float)ArrayMean(angleDiff, angleDiff.Length);
        stats.TotalAngularDistortion = (float)weightedAngleDiff;
    }

    private static double ArrayMean(double[] values, int count)
    {
        double sum = 0.0;
        for (int i = 0; i < count; ++i) sum += values[i];
        return sum / count;
    }

    private static void AnalyseTriangle3D(Double3[] apex, int offset, out double area, double[] angle, int angleOffset)
    {
        Double3[] side =
        {
            apex[offset + 1] - apex[offset + 0],
            apex[offset + 2] - apex[offset + 1],
            apex[offset + 0] - apex[offset + 2],
        };
        double[] len = { Double3.Length(side[0]), Double3.Length(side[1]), Double3.Length(side[2]) };
        if (len[0] < NumericHelpers.FloatTiny || len[1] < NumericHelpers.FloatTiny || len[2] < NumericHelpers.FloatTiny)
        {
            angle[angleOffset + 0] = angle[angleOffset + 1] = angle[angleOffset + 2] = NumericHelpers.FloatTiny;
            area = NumericHelpers.FloatTiny;
            return;
        }
        area = 0.5 * Double3.Length(Double3.Cross(side[0], side[2]));
        for (int i = 0; i < 3; ++i) side[i] /= len[i];
        angle[angleOffset + 0] = System.Math.PI - System.Math.Acos(System.Math.Clamp(Double3.Dot(side[0], side[2]), -1.0, 1.0));
        angle[angleOffset + 1] = System.Math.PI - System.Math.Acos(System.Math.Clamp(Double3.Dot(side[1], side[0]), -1.0, 1.0));
        angle[angleOffset + 2] = System.Math.PI - System.Math.Acos(System.Math.Clamp(Double3.Dot(side[2], side[1]), -1.0, 1.0));
    }

    private static void AnalyseTriangle2D(Double2[] apex, int offset, out double area, double[] angle, int angleOffset)
    {
        Double2[] side =
        {
            apex[offset + 1] - apex[offset + 0],
            apex[offset + 2] - apex[offset + 1],
            apex[offset + 0] - apex[offset + 2],
        };
        double[] len = { Double2.Length(side[0]), Double2.Length(side[1]), Double2.Length(side[2]) };
        if (len[0] < NumericHelpers.FloatTiny || len[1] < NumericHelpers.FloatTiny || len[2] < NumericHelpers.FloatTiny)
        {
            angle[angleOffset + 0] = angle[angleOffset + 1] = angle[angleOffset + 2] = NumericHelpers.FloatTiny;
            area = NumericHelpers.FloatTiny;
            return;
        }
        area = 0.5 * System.Math.Abs(side[0].X * side[2].Y - side[0].Y * side[2].X);
        for (int i = 0; i < 3; ++i) side[i] /= len[i];
        angle[angleOffset + 0] = System.Math.PI - System.Math.Acos(System.Math.Clamp(Double2.Dot(side[0], side[2]), -1.0, 1.0));
        angle[angleOffset + 1] = System.Math.PI - System.Math.Acos(System.Math.Clamp(Double2.Dot(side[1], side[0]), -1.0, 1.0));
        angle[angleOffset + 2] = System.Math.PI - System.Math.Acos(System.Math.Clamp(Double2.Dot(side[2], side[1]), -1.0, 1.0));
    }
}
