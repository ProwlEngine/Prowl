using System;
using System.IO;

using Prowl.Aperture;

namespace Prowl.Graphite.Rendering.Tools;

public static class TerrainDebug
{
    /// <summary>
    /// Writes a page as an image, apron included and z 0 on top: heights in grey stretched to the page's own range with
    /// holes in red on the left, normal x in red, ambient occlusion in green and normal z in blue on the right.
    /// </summary>
    public static void DumpPage(TerrainData data, TerrainPageKey key, string path)
    {
        TerrainPage page = data.GetPage(key) ?? throw new ArgumentException($"The terrain has no page {key}.", nameof(key));
        const int size = TerrainPage.Size;
        ushort low = ushort.MaxValue, high = 0;
        foreach (ushort h in page.Heights)
        {
            low = Math.Min(low, h);
            high = Math.Max(high, h);
        }
        double scale = high > low ? 255.0 / (high - low) : 0;

        byte[] pixels = new byte[size * 2 * size * 4];
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                int sample = z * size + x;
                Span<byte> left = pixels.AsSpan((z * size * 2 + x) * 4, 4), right = pixels.AsSpan((z * size * 2 + size + x) * 4, 4);
                byte grey = (byte)Math.Round((page.Heights[sample] - low) * scale);
                bool hole = page.Control != null && TerrainControl.IsHole(page.Control[sample]);
                left[0] = hole ? (byte)255 : grey;
                left[1] = hole ? (byte)0 : grey;
                left[2] = hole ? (byte)0 : grey;
                left[3] = 255;
                right[0] = page.NormalAo[sample * 4];
                right[1] = page.NormalAo[sample * 4 + 2];
                right[2] = page.NormalAo[sample * 4 + 1];
                right[3] = 255;
            }

        using Image image = Image.FromPixels(pixels, size * 2, size, Prowl.Aperture.PixelFormat.Rgba8);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        image.Save(path);
    }
}
