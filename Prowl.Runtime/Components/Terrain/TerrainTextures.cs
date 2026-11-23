// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Terrain;

/// <summary>Reads textures back as square RGBA8 texels of one power of two size, for terrain layers and grass cards.</summary>
internal static class TerrainTextures
{
    /// <summary>The power of two size, at most <paramref name="largest"/>, holding the biggest of the textures.</summary>
    public static int SizeFor(int largest, params Texture2D?[] textures)
    {
        int size = 4;
        foreach (Texture2D? texture in textures)
            if (texture.IsValid() && texture.IsLoaded)
                size = Math.Max(size, (int)Math.Max(texture.Width, texture.Height));
        int power = 4;
        while (power < size && power < largest)
            power *= 2;
        return power;
    }

    /// <summary>
    /// The texture's texels resampled to the size, read from the smallest mip still at least that big, or null while it
    /// loads or has a format without four channels.
    /// </summary>
    public static byte[]? Read(Texture2D texture, int size)
    {
        uint mip = 0;
        while (mip + 1 < texture.MipCount && Math.Min(texture.Width >> (int)(mip + 1), texture.Height >> (int)(mip + 1)) >= size)
            mip++;
        if (texture.GetMipRgba8(mip) is not { } texels)
            return null;
        int width = Math.Max((int)texture.Width >> (int)mip, 1), height = Math.Max((int)texture.Height >> (int)mip, 1);
        return width == size && height == size ? texels : Resample(texels, width, height, size);
    }

    /// <summary>A square of one colour.</summary>
    public static byte[] Solid(int size, byte r, byte g, byte b, byte a)
    {
        byte[] texels = new byte[size * size * 4];
        for (int i = 0; i < size * size; i++)
            (texels[i * 4], texels[i * 4 + 1], texels[i * 4 + 2], texels[i * 4 + 3]) = (r, g, b, a);
        return texels;
    }

    private static byte[] Resample(byte[] texels, int width, int height, int size)
    {
        byte[] result = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = Math.Clamp((x + 0.5f) * width / size - 0.5f, 0f, width - 1), fy = Math.Clamp((y + 0.5f) * height / size - 0.5f, 0f, height - 1);
                int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
                float tx = fx - x0, ty = fy - y0;
                for (int c = 0; c < 4; c++)
                {
                    float a = texels[(y0 * width + x0) * 4 + c], b = texels[(y0 * width + x1) * 4 + c];
                    float d = texels[(y1 * width + x0) * 4 + c], e = texels[(y1 * width + x1) * 4 + c];
                    result[(y * size + x) * 4 + c] = (byte)MathF.Round((a + (b - a) * tx) * (1 - ty) + (d + (e - d) * tx) * ty);
                }
            }
        return result;
    }
}
