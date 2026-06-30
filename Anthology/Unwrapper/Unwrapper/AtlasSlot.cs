using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Per-chart book-keeping for the packer. Stores both source (pre-scale) and current iteration
/// origin/extent so the outer scale loop can rescale without losing the original layout.
/// </summary>
internal struct AtlasSlot
{
    public Double2 Origin;
    public Double2 SourceOrigin;
    public Double2 Extent;
    public Double2 SourceExtent;
    public Double3 Origin3D;
    public double Scale;
    public double RectangleArea;
    public double UvArea;
    public double SurfaceArea;
    public int ChartIndex;

    public static AtlasSlot Capture(UvChart chart, int chartIndex)
    {
        Double2 ext = chart.UvMax - chart.UvMin;
        return new AtlasSlot
        {
            SourceOrigin = chart.UvMin,
            Origin = chart.UvMin,
            SourceExtent = ext,
            Extent = ext,
            Origin3D = chart.Origin3D,
            Scale = 1.0,
            RectangleArea = ext.X * ext.Y,
            UvArea = chart.UvArea,
            SurfaceArea = chart.SurfaceArea,
            ChartIndex = chartIndex,
        };
    }

    /// <summary>Apply a uniform scale; <see cref="SourceExtent"/> is preserved as the unscaled reference.</summary>
    public void Rescale(double scale)
    {
        Extent = SourceExtent * scale;
        Scale = scale;
    }
}
