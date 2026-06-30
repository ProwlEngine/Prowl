// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace SoftwareRasterizer.Rasterizer;

public class Texture2D(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public float[] Data { get; } = new float[width * height * 4];

    public void SetPixel(int x, int y, Float4 color)
    {
        int index = (y * Width + x) * 4;
        Data[index] = color.R;
        Data[index + 1] = color.G;
        Data[index + 2] = color.B;
        Data[index + 3] = color.A;
    }

    public Float4 GetPixel(int x, int y)
    {
        int index = (y * Width + x) * 4;
        return new Float4(
            Data[index],
            Data[index + 1],
            Data[index + 2],
            Data[index + 3]
            );
    }
}
