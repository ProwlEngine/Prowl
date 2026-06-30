using Prowl.Vector;

namespace Prowl.Photonic.Imaging;

/// <summary>
/// Lightmap-style edge dilation. Walks every empty pixel and fills it with the average of the
/// non-empty pixels in its 3x3 neighbourhood. Repeated <c>n</c> times to grow the lit area by
/// <c>n</c> pixels: enough to cover bilinear filter taps that would otherwise sample black.
/// </summary>
/// <remarks>
/// Each pass reads from a frozen snapshot and writes to the live <c>rgb</c> / <c>covered</c>
/// buffers; rows within a pass are independent, so the row loop runs through
/// <see cref="System.Threading.Tasks.Parallel.For(int,int,System.Action{int})"/>.
/// </remarks>
internal static class Dilate
{
    /// <summary>Run dilation in-place. <paramref name="covered"/> marks the already-valid texels.</summary>
    public static void Run(float[] rgb, bool[] covered, int width, int height, int passes)
    {
        if (passes <= 0) return;
        var tmpRGB = new float[rgb.Length];
        var tmpCov = new bool[covered.Length];
        for (int p = 0; p < passes; p++)
        {
            System.Array.Copy(rgb, tmpRGB, rgb.Length);
            System.Array.Copy(covered, tmpCov, covered.Length);
            System.Threading.Tasks.Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (tmpCov[i]) continue;
                    int n = 0;
                    float r = 0, g = 0, b = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int j = ny * width + nx;
                        if (!tmpCov[j]) continue;
                        r += tmpRGB[j * 3    ];
                        g += tmpRGB[j * 3 + 1];
                        b += tmpRGB[j * 3 + 2];
                        n++;
                    }
                    if (n > 0)
                    {
                        float inv = 1f / n;
                        rgb[i * 3    ] = r * inv;
                        rgb[i * 3 + 1] = g * inv;
                        rgb[i * 3 + 2] = b * inv;
                        covered[i] = true;
                    }
                }
            });
        }
    }
}
