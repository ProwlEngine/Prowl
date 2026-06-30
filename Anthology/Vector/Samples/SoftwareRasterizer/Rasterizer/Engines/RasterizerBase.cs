using System.Numerics;

using Prowl.Vector;

namespace SoftwareRasterizer.Rasterizer.Engines;

internal abstract class RasterizerBase(GraphicsDevice device)
{
    internal GraphicsDevice Device = device;

    public abstract void Rasterize(RasterTriangle triangle);

    protected void DrawLine(RasterVertex v1, RasterVertex v2)
    {
        // Make sure Varying count is the same
        if (v1.Varyings.Length != v2.Varyings.Length)
            throw new InvalidOperationException("Varying count must be the same for both vertices.");

        // Clip line to screen bounds BEFORE running Bresenham
        if (!ClipLineToScreen(ref v1, ref v2))
            return; // Line is completely outside screen

        int x0 = (int)Maths.Round(v1.ScreenPosition.X);
        int y0 = (int)Maths.Round(v1.ScreenPosition.Y);
        int x1 = (int)Maths.Round(v2.ScreenPosition.X);
        int y1 = (int)Maths.Round(v2.ScreenPosition.Y);

        int dx = Maths.Abs(x1 - x0);
        int dy = Maths.Abs(y1 - y0);
        int sx = (x0 < x1) ? 1 : -1;
        int sy = (y0 < y1) ? 1 : -1;
        int err = dx - dy;

        // Calculate total distance for depth interpolation
        float totalDistance = Maths.Sqrt(dx * dx + dy * dy);
        if (totalDistance == 0) return; // Zero-length line

        float z1 = v1.ScreenPosition.Z;
        float z2 = v2.ScreenPosition.Z;
        int currentX = x0;
        int currentY = y0;

        while (true)
        {
            // Calculate interpolation factor based on distance traveled
            float currentDistance = Maths.Sqrt((currentX - x0) * (currentX - x0) + (currentY - y0) * (currentY - y0));
            float t = currentDistance / totalDistance;

            // Interpolate depth and varyings
            float interpolatedZ = Maths.Lerp(z1, z2, t);

            // Interpolate varyings
            Float4[] interpolatedVaryings = new Float4[v1.Varyings.Length];
            for (int i = 0; i < v1.Varyings.Length; i++)
            {
                interpolatedVaryings[i] = Maths.Lerp(v1.Varyings[i], v2.Varyings[i], t);
            }

            DrawPoint(new RasterVertex
            {
                ScreenPosition = new Vector3(currentX, currentY, interpolatedZ),
                Varyings = interpolatedVaryings
            });

            if ((currentX == x1) && (currentY == y1)) break;

            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                currentX += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                currentY += sy;
            }
        }
    }

    private bool ClipLineToScreen(ref RasterVertex v1, ref RasterVertex v2)
    {
        int screenWidth = Device.CurrentFramebuffer.Width;
        int screenHeight = Device.CurrentFramebuffer.Height;

        float x1 = v1.ScreenPosition.X;
        float y1 = v1.ScreenPosition.Y;
        float x2 = v2.ScreenPosition.X;
        float y2 = v2.ScreenPosition.Y;

        // Cohen-Sutherland line clipping
        const int INSIDE = 0; // 0000
        const int LEFT = 1;   // 0001
        const int RIGHT = 2;  // 0010
        const int BOTTOM = 4; // 0100
        const int TOP = 8;    // 1000

        int ComputeOutCode(float x, float y)
        {
            int code = INSIDE;
            if (x < 0) code |= LEFT;
            else if (x > screenWidth - 1) code |= RIGHT;
            if (y < 0) code |= BOTTOM;
            else if (y > screenHeight - 1) code |= TOP;
            return code;
        }

        int outcode1 = ComputeOutCode(x1, y1);
        int outcode2 = ComputeOutCode(x2, y2);

        while (true)
        {
            if ((outcode1 | outcode2) == 0)
            {
                // Both points inside - update vertices and return true
                v1.ScreenPosition.X = x1;
                v1.ScreenPosition.Y = y1;
                v2.ScreenPosition.X = x2;
                v2.ScreenPosition.Y = y2;
                return true;
            }
            else if ((outcode1 & outcode2) != 0)
            {
                // Both points share an outside zone - line is completely outside
                return false;
            }
            else
            {
                // Line crosses boundary - clip it
                float x = 0, y = 0;
                int outcodeOut = (outcode1 != 0) ? outcode1 : outcode2;

                if ((outcodeOut & TOP) != 0)
                {
                    x = x1 + (x2 - x1) * (screenHeight - 1 - y1) / (y2 - y1);
                    y = screenHeight - 1;
                }
                else if ((outcodeOut & BOTTOM) != 0)
                {
                    x = x1 + (x2 - x1) * (0 - y1) / (y2 - y1);
                    y = 0;
                }
                else if ((outcodeOut & RIGHT) != 0)
                {
                    y = y1 + (y2 - y1) * (screenWidth - 1 - x1) / (x2 - x1);
                    x = screenWidth - 1;
                }
                else if ((outcodeOut & LEFT) != 0)
                {
                    y = y1 + (y2 - y1) * (0 - x1) / (x2 - x1);
                    x = 0;
                }

                // Interpolate the vertex attributes at the clipped point
                float t = 0;
                if (Math.Abs(x2 - x1) > Math.Abs(y2 - y1))
                {
                    t = (x - x1) / (x2 - x1);
                }
                else if (Math.Abs(y2 - y1) > 1e-6f)
                {
                    t = (y - y1) / (y2 - y1);
                }

                if (outcodeOut == outcode1)
                {
                    // Interpolate vertex 1
                    v1.ScreenPosition.X = x;
                    v1.ScreenPosition.Y = y;
                    v1.ScreenPosition.Z = Maths.Lerp(v1.ScreenPosition.Z, v2.ScreenPosition.Z, t);

                    for (int i = 0; i < v1.Varyings.Length; i++)
                    {
                        v1.Varyings[i] = Maths.Lerp(v1.Varyings[i], v2.Varyings[i], t);
                    }

                    x1 = x;
                    y1 = y;
                    outcode1 = ComputeOutCode(x1, y1);
                }
                else
                {
                    // Interpolate vertex 2
                    v2.ScreenPosition.X = x;
                    v2.ScreenPosition.Y = y;
                    v2.ScreenPosition.Z = Maths.Lerp(v1.ScreenPosition.Z, v2.ScreenPosition.Z, t);

                    for (int i = 0; i < v2.Varyings.Length; i++)
                    {
                        v2.Varyings[i] = Maths.Lerp(v1.Varyings[i], v2.Varyings[i], t);
                    }

                    x2 = x;
                    y2 = y;
                    outcode2 = ComputeOutCode(x2, y2);
                }
            }
        }
    }

    protected void DrawPoint(RasterVertex vertex)
    {
        int x = Maths.RoundToInt(vertex.ScreenPosition.X);
        int y = Maths.RoundToInt(vertex.ScreenPosition.Y);

        if (x >= 0 && x < Device.CurrentFramebuffer.Width && y >= 0 && y < Device.CurrentFramebuffer.Height)
        {
            lock (Device.CurrentFramebuffer.GetPixelLockUnsafe(x, y))
            {
                // Depth test
                if (Device.DoDepthTest && !Device.CurrentShader.CanWriteDepth)
                {
                    if (vertex.ScreenPosition.Z >= Device.CurrentFramebuffer.GetDepth(x, y))
                        return; // Failed depth test
                }

                var fragmentOutput = Device.CurrentShader.FragmentShader(vertex.Varyings, new());

                Device.CurrentFramebuffer.SetPixelUnsafe(
                     x, y,
                     fragmentOutput.GlFragColor);

                Device.CurrentFramebuffer.SetDepthUnsafe(x, y, fragmentOutput.GlFragDepth ?? vertex.ScreenPosition.Z);
            }
        }
    }
}
