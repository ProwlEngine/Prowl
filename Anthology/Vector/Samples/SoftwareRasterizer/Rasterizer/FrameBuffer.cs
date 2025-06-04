// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

using Prowl.Vector;

namespace SoftwareRasterizer.Rasterizer;

public class FrameBuffer
{
    public int Width { get; }
    public int Height { get; }
    public Texture2D ColorTexture { get; }
    public float[] DepthBuffer { get; }

    private GraphicsDevice _device;

    public FrameBuffer(GraphicsDevice device, int width, int height)
    {
        _device = device;
        Width = width;
        Height = height;
        ColorTexture = new Texture2D(width, height);
        DepthBuffer = new float[width * height];
    }

    public void Clear(float r, float g, float b, float a)
    {
        byte rByte = (byte)Math.Clamp(r * 255, 0, 255);
        byte gByte = (byte)Math.Clamp(g * 255, 0, 255);
        byte bByte = (byte)Math.Clamp(b * 255, 0, 255);
        byte aByte = (byte)Math.Clamp(a * 255, 0, 255);

        //for (int i = 0; i < Width * Height; i++)
        Parallel.For(0, Width * Height, i =>
        {
            int index = i * 4;
            ColorTexture.Data[index] = rByte;
            ColorTexture.Data[index + 1] = gByte;
            ColorTexture.Data[index + 2] = bByte;
            ColorTexture.Data[index + 3] = aByte;
            DepthBuffer[i] = 1.0f;
        }
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixelUnsafe(int x, int y, Float4 color)
    {
        //if (x < 0 || x >= Width || y < 0 || y >= Height)
        //    return;

        ColorTexture.SetPixel(x, y, _device.CurrentBlendFunction.Blend(color, ColorTexture.GetPixel(x, y)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetDepthUnsafe(int x, int y, float depth)
    {
        if (!_device.DoDepthWrite) return;

        //if (x >= 0 && x < Width && y >= 0 && y < Height)
            DepthBuffer[y * Width + x] = depth;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Float4 GetPixel(int x, int y) => ColorTexture.GetPixel(x, y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetDepth(int x, int y)
    {
        return DepthBuffer[y * Width + x];
    }
}
