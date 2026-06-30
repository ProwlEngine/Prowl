using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// A 2D parametrised chart: three UVs per triangle, plus geometric/UV area accounting
/// and a tight 2D oriented box ready for the packer.
/// </summary>
internal sealed class UvChart
{
    public Double2 UvMin = new(1e32, 1e32);
    public Double2 UvMax = new(-1e32, -1e32);

    public Double3 Origin3D;
    public double SurfaceArea;
    public double UvArea;

    public MeshRegion? Region;
    public Double2[] UVs;

    /// <summary>Build a fresh chart over <paramref name="region"/>; pre-sums geometric area.</summary>
    public UvChart(MeshRegion region)
    {
        Region = region;
        UVs = new Double2[3 * region.Triangles.Length];

        foreach (int face in region.Triangles)
            SurfaceArea += region.Mesh.FaceAttributes[face].Area;
    }

    public void RefreshUvArea() => UvArea = ComputeUvArea();

    private double ComputeUvArea()
    {
        double area = 0.0;
        int faceCount = Region!.Triangles.Length;
        for (int f = 0; f < faceCount; ++f)
        {
            Double2 e1 = UVs[3 * f + 1] - UVs[3 * f + 0];
            Double2 e2 = UVs[3 * f + 2] - UVs[3 * f + 0];
            area += 0.5 * System.Math.Abs(e1.X * e2.Y - e1.Y * e2.X);
        }
        return area;
    }

    /// <summary>Copy UVs from a source chart through a per-face remap, then re-tighten.</summary>
    public void TakeFrom(UvChart source, int[] faceRemap)
    {
        int faceCount = Region!.Triangles.Length;
        for (int f = 0; f < faceCount; ++f)
        {
            int chartFace = faceRemap[f];
            UVs[3 * f + 0] = source.UVs[3 * chartFace + 0];
            UVs[3 * f + 1] = source.UVs[3 * chartFace + 1];
            UVs[3 * f + 2] = source.UVs[3 * chartFace + 2];
        }
        TightenAndOrient();
    }

    /// <summary>
    /// Re-orient the chart inside its tight 2D bounding box: wider than tall, axes flipped to
    /// canonical signs, centred at the origin.
    /// </summary>
    public void TightenAndOrient()
    {
        UvMin = new Double2(1e32, 1e32);
        UvMax = new Double2(-1e32, -1e32);

        var fit = OrientedBox2DFit.Fit(UVs, UVs.Length);
        Double2 axisX = fit.AxisX;
        Double2 axisY = fit.AxisY;
        Double2 ext = fit.Extent;

        // Land in landscape orientation so atlas packing has an easier time.
        if (NumericHelpers.ApproxLess(ext.X, ext.Y, 1e-6))
        {
            (axisX, axisY) = (axisY, axisX);
            ext = new Double2(ext.Y, ext.X);
        }

        // Enforce consistent axis signs so different runs agree on chart orientation.
        if (NumericHelpers.ApproxLess(axisX.X, 0.0, 1e-6)) axisX = -axisX;
        if (NumericHelpers.ApproxLess(axisY.Y, 0.0, 1e-6)) axisY = -axisY;

        double invN = 1.0 / (3.0 * Region!.Triangles.Length);

        Double2 centre = default;
        for (int i = 0; i < UVs.Length; ++i)
        {
            UVs[i] = new Double2(Double2.Dot(UVs[i], axisX), Double2.Dot(UVs[i], axisY));
            centre += UVs[i] * invN;
        }

        for (int i = 0; i < UVs.Length; ++i)
        {
            UVs[i] -= centre;
            if (UVs[i].X < UvMin.X) UvMin.X = UVs[i].X;
            if (UVs[i].Y < UvMin.Y) UvMin.Y = UVs[i].Y;
            if (UVs[i].X > UvMax.X) UvMax.X = UVs[i].X;
            if (UVs[i].Y > UvMax.Y) UvMax.Y = UVs[i].Y;
        }

        RefreshUvArea();
    }
}
