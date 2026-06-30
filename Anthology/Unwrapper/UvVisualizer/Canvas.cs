namespace Prowl.Unwrapper.Visualizer;

/// <summary>Tiny RGB canvas with point/line/clear primitives — enough to visualise UV islands.</summary>
internal sealed class Canvas
{
    public readonly int Width;
    public readonly int Height;
    public readonly byte[] Pixels;

    public Canvas(int width, int height)
    {
        Width = width;
        Height = height;
        // Long-cast prevents int overflow at 16K (16384*16384*3 = 805,306,368, which fits in
        // an int — but we'll use long arithmetic to keep that obvious).
        Pixels = new byte[(long)width * height * 3];
    }

    public void Clear(byte r, byte g, byte b)
    {
        for (int i = 0; i < Pixels.Length; i += 3)
        {
            Pixels[i + 0] = r;
            Pixels[i + 1] = g;
            Pixels[i + 2] = b;
        }
    }

    /// <summary>
    /// Min-blend a pixel toward (r,g,b) by <paramref name="coverage"/> (0=no change, 1=full).
    /// "Min-blend" means we only ever darken — so two thread writes to the same pixel still
    /// converge to the correct dark line colour, just with a tiny chance of one frame of brightness
    /// loss from a race. Acceptable for visualisation.
    /// </summary>
    public void Plot(int x, int y, double coverage, byte r, byte g, byte b)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        if (coverage <= 0.0) return;
        if (coverage > 1.0) coverage = 1.0;

        int idx = (y * Width + x) * 3;
        // Blend toward (r,g,b) from the current pixel; result = lerp(current, target, coverage).
        byte cr = Pixels[idx + 0];
        byte cg = Pixels[idx + 1];
        byte cb = Pixels[idx + 2];
        byte nr = (byte)(cr + (r - cr) * coverage);
        byte ng = (byte)(cg + (g - cg) * coverage);
        byte nb = (byte)(cb + (b - cb) * coverage);
        // Only darken: useful when the background is light and the lines are dark, the typical
        // case here.
        if (nr < cr) Pixels[idx + 0] = nr;
        if (ng < cg) Pixels[idx + 1] = ng;
        if (nb < cb) Pixels[idx + 2] = nb;
    }

    /// <summary>
    /// Xiaolin Wu's anti-aliased line. Plots two weighted pixels per step along the major axis.
    /// </summary>
    public void DrawLineAA(double x0, double y0, double x1, double y1, byte r, byte g, byte b)
    {
        bool steep = System.Math.Abs(y1 - y0) > System.Math.Abs(x1 - x0);
        if (steep)
        {
            (x0, y0) = (y0, x0);
            (x1, y1) = (y1, x1);
        }
        if (x0 > x1)
        {
            (x0, x1) = (x1, x0);
            (y0, y1) = (y1, y0);
        }

        double dx = x1 - x0;
        double dy = y1 - y0;
        double gradient = dx == 0.0 ? 1.0 : dy / dx;

        // First endpoint: round x, plot two pixels weighted by sub-pixel position.
        double xend1 = System.Math.Round(x0);
        double yend1 = y0 + gradient * (xend1 - x0);
        double xgap1 = Rfpart(x0 + 0.5);
        int xpxl1 = (int)xend1;
        int ypxl1 = (int)System.Math.Floor(yend1);
        if (steep)
        {
            Plot(ypxl1, xpxl1, Rfpart(yend1) * xgap1, r, g, b);
            Plot(ypxl1 + 1, xpxl1, Fpart(yend1) * xgap1, r, g, b);
        }
        else
        {
            Plot(xpxl1, ypxl1, Rfpart(yend1) * xgap1, r, g, b);
            Plot(xpxl1, ypxl1 + 1, Fpart(yend1) * xgap1, r, g, b);
        }

        // Second endpoint.
        double xend2 = System.Math.Round(x1);
        double yend2 = y1 + gradient * (xend2 - x1);
        double xgap2 = Fpart(x1 + 0.5);
        int xpxl2 = (int)xend2;
        int ypxl2 = (int)System.Math.Floor(yend2);
        if (steep)
        {
            Plot(ypxl2, xpxl2, Rfpart(yend2) * xgap2, r, g, b);
            Plot(ypxl2 + 1, xpxl2, Fpart(yend2) * xgap2, r, g, b);
        }
        else
        {
            Plot(xpxl2, ypxl2, Rfpart(yend2) * xgap2, r, g, b);
            Plot(xpxl2, ypxl2 + 1, Fpart(yend2) * xgap2, r, g, b);
        }

        // Main loop: step major axis by 1, accumulate gradient.
        double intery = yend1 + gradient;
        for (int x = xpxl1 + 1; x < xpxl2; ++x)
        {
            int yfloor = (int)System.Math.Floor(intery);
            double f = Fpart(intery);
            if (steep)
            {
                Plot(yfloor, x, 1.0 - f, r, g, b);
                Plot(yfloor + 1, x, f, r, g, b);
            }
            else
            {
                Plot(x, yfloor, 1.0 - f, r, g, b);
                Plot(x, yfloor + 1, f, r, g, b);
            }
            intery += gradient;
        }
    }

    private static double Fpart(double x) => x - System.Math.Floor(x);
    private static double Rfpart(double x) => 1.0 - Fpart(x);
}
