using System.Numerics;

using Prowl.Vector;

namespace SoftwareRasterizer.Rasterizer.Engines;

internal abstract class RasterizerBase(GraphicsDevice device)
{
    internal GraphicsDevice Device = device;

    public abstract void Rasterize(RasterTriangle triangle);

    protected void DrawLine(RasterVertex v1, RasterVertex v2)
    {
        int x0 = (int)Maths.Round(v1.ScreenPosition.X);
        int y0 = (int)Maths.Round(v1.ScreenPosition.Y);
        int x1 = (int)Maths.Round(v2.ScreenPosition.X);
        int y1 = (int)Maths.Round(v2.ScreenPosition.Y);

        int dx = Maths.Abs(x1 - x0);
        int dy = Maths.Abs(y1 - y0);
        int sx = (x0 < x1) ? 1 : -1;
        int sy = (y0 < y1) ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            DrawPoint(new RasterVertex
            {
                ScreenPosition = new Vector3(x0, y0, 0),
                Varyings = v1.Varyings
            });

            if ((x0 == x1) && (y0 == y1)) break;
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    protected void DrawPoint(RasterVertex vertex)
    {
        int x = Maths.RoundToInt(vertex.ScreenPosition.X);
        int y = Maths.RoundToInt(vertex.ScreenPosition.Y);

        if (x >= 0 && x < Device.CurrentFramebuffer.Width && y >= 0 && y < Device.CurrentFramebuffer.Height)
        {
            var fragmentOutput = Device.CurrentShader.FragmentShader(vertex.Varyings, new());

            Device.CurrentFramebuffer.SetPixelUnsafe(
                 x, y,
                 fragmentOutput.GlFragColor);

            Device.CurrentFramebuffer.SetDepthUnsafe(x, y, vertex.ScreenPosition.Z);
        }
    }
}
