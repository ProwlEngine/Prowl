// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>
/// Turns the three planes a lossy frame decodes to into colour. The chroma is at half resolution
/// each way, so each output pixel is a weighted blend of the four samples around it. Two rows are
/// built at once, since a row pair shares one pair of chroma rows and differs only in the weights.
/// </summary>
internal static class Vp8Yuv
{
    private const int Fixed = 6;
    private const int Mask = (256 << Fixed) - 1;

    private static byte Clip(int value) =>
        (value & ~Mask) == 0 ? (byte)(value >> Fixed) : value < 0 ? (byte)0 : (byte)255;

    private static int MultiplyHigh(int value, int coefficient) => (value * coefficient) >> 8;

    /// <summary>The three colours of one pixel, packed opaque. The brightness term is shared.</summary>
    private static uint Colour(int y, int u, int v)
    {
        int brightness = MultiplyHigh(y, 19077);

        byte red = Clip(brightness + MultiplyHigh(v, 26149) - 14234);
        byte green = Clip(brightness - MultiplyHigh(u, 6419) - MultiplyHigh(v, 13320) + 8708);
        byte blue = Clip(brightness + MultiplyHigh(u, 33050) - 17685);

        return 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
    }

    public static void ToColour(Vp8Frame frame, uint[] destination, int width, int height)
    {
        int chromaRows = (height + 1) / 2;

        // The first row has no chroma row above it, so it is blended against its own.
        Pair(frame, destination, width, 0, -1, 0, 0);

        int row = 0;
        for (int y = 0; y + 2 < height; y += 2)
        {
            int next = Math.Min(row + 1, chromaRows - 1);
            Pair(frame, destination, width, y + 1, y + 2, row, next);
            row = next;
        }

        // An even height leaves one row unpaired at the bottom, blended against its own chroma.
        if ((height & 1) == 0 && height > 1)
            Pair(frame, destination, width, height - 1, -1, row, row);
    }

    /// <summary>
    /// Builds one or two output rows from the two chroma rows they sit between. Each pixel takes
    /// nine parts of the chroma sample it covers, three each of the two beside it and one of the
    /// diagonal, which is the weighting the format defines.
    /// </summary>
    private static void Pair(Vp8Frame frame, uint[] destination, int width, int topRow,
                             int bottomRow, int topChroma, int bottomChroma)
    {
        byte[] luma = frame.Luma;
        byte[] u = frame.ChromaU;
        byte[] v = frame.ChromaV;

        int topAt = topChroma * frame.ChromaStride;
        int bottomAt = bottomChroma * frame.ChromaStride;

        int topLuma = topRow * frame.LumaStride;
        int bottomLuma = bottomRow * frame.LumaStride;
        int topOut = topRow * width;
        int bottomOut = bottomRow * width;

        int last = (width - 1) >> 1;

        int cornerU = u[topAt];
        int cornerV = v[topAt];
        int leftU = u[bottomAt];
        int leftV = v[bottomAt];

        Write(destination, topOut, luma[topLuma],
              ((3 * cornerU) + leftU + 2) >> 2, ((3 * cornerV) + leftV + 2) >> 2);

        if (bottomRow >= 0)
        {
            Write(destination, bottomOut, luma[bottomLuma],
                  ((3 * leftU) + cornerU + 2) >> 2, ((3 * leftV) + cornerV + 2) >> 2);
        }

        for (int x = 1; x <= last; x++)
        {
            int aboveU = u[topAt + x];
            int aboveV = v[topAt + x];
            int hereU = u[bottomAt + x];
            int hereV = v[bottomAt + x];

            int sumU = cornerU + aboveU + leftU + hereU + 8;
            int sumV = cornerV + aboveV + leftV + hereV + 8;

            int crossU = (sumU + (2 * (aboveU + leftU))) >> 3;
            int crossV = (sumV + (2 * (aboveV + leftV))) >> 3;
            int straightU = (sumU + (2 * (cornerU + hereU))) >> 3;
            int straightV = (sumV + (2 * (cornerV + hereV))) >> 3;

            Write(destination, topOut + (2 * x) - 1, luma[topLuma + (2 * x) - 1],
                  (crossU + cornerU) >> 1, (crossV + cornerV) >> 1);

            if (2 * x < width)
            {
                Write(destination, topOut + (2 * x), luma[topLuma + (2 * x)],
                      (straightU + aboveU) >> 1, (straightV + aboveV) >> 1);
            }

            if (bottomRow >= 0)
            {
                Write(destination, bottomOut + (2 * x) - 1, luma[bottomLuma + (2 * x) - 1],
                      (straightU + leftU) >> 1, (straightV + leftV) >> 1);

                if (2 * x < width)
                {
                    Write(destination, bottomOut + (2 * x), luma[bottomLuma + (2 * x)],
                          (crossU + hereU) >> 1, (crossV + hereV) >> 1);
                }
            }

            cornerU = aboveU;
            cornerV = aboveV;
            leftU = hereU;
            leftV = hereV;
        }

        if ((width & 1) != 0)
            return;

        Write(destination, topOut + width - 1, luma[topLuma + width - 1],
              ((3 * cornerU) + leftU + 2) >> 2, ((3 * cornerV) + leftV + 2) >> 2);

        if (bottomRow >= 0)
        {
            Write(destination, bottomOut + width - 1, luma[bottomLuma + width - 1],
                  ((3 * leftU) + cornerU + 2) >> 2, ((3 * leftV) + cornerV + 2) >> 2);
        }
    }

    private static void Write(uint[] destination, int at, int y, int u, int v) =>
        destination[at] = Colour(y, u, v);
}
