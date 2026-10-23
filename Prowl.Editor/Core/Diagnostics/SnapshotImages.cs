// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;

using Prowl.Graphite;
using Prowl.Graphite.Debugging;

namespace Prowl.Editor.Core.Diagnostics;

/// <summary> Turns the texels of a recorded or replayed copy into 8 bit RGBA, so a pass output can be written as a PNG. </summary>
internal static class SnapshotImages
{
    public static bool IsDepth(PixelFormat format)
        => format is PixelFormat.D24_UNorm_S8_UInt or PixelFormat.D32_Float_S8_UInt or PixelFormat.D32_Float or PixelFormat.D16_UNorm;

    public static bool CanDecode(PixelFormat format) => IsDepth(format) || format switch
    {
        PixelFormat.R8_G8_B8_A8_UNorm or PixelFormat.R8_G8_B8_A8_UNorm_SRgb
            or PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm_SRgb
            or PixelFormat.R8_UNorm or PixelFormat.R8_G8_UNorm
            or PixelFormat.R16_UNorm or PixelFormat.R16_Float or PixelFormat.R16_G16_Float or PixelFormat.R16_G16_B16_A16_Float
            or PixelFormat.R32_Float or PixelFormat.R32_G32_Float or PixelFormat.R32_G32_B32_A32_Float
            or PixelFormat.R10_G10_B10_A2_UNorm or PixelFormat.R11_G11_B10_Float => true,
        _ => false,
    };

    /// <summary> Texels skipped per pixel so the longer side of the region fits in <paramref name="resolution"/>. 0 or less keeps every texel. </summary>
    public static int Step(in CopyRegion region, int resolution)
        => resolution <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Math.Max(region.Width, region.Height) / (double)resolution));

    public static (int Width, int Height) Size(in CopyRegion region, int step)
        => ((int)((region.Width + step - 1) / step), (int)((region.Height + step - 1) / step));

    /// <summary> Float colors are scaled by <paramref name="exposure"/> then clamped. Depth is stretched over the range it covers. Every <paramref name="step"/>th texel is kept. </summary>
    public static byte[] ToRgba8(ReadOnlySpan<byte> data, in CopyRegion region, float exposure, int step = 1)
    {
        (int width, int height) = Size(region, step);
        byte[] pixels = new byte[width * height * 4];
        bool depth = IsDepth(region.Format);
        float min = 0, max = 1;
        if (depth)
            DepthRange(data, region, step, out min, out max);

        bool single = region.Format is PixelFormat.R8_UNorm or PixelFormat.R16_UNorm or PixelFormat.R16_Float or PixelFormat.R32_Float;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (float r, float g, float b, float a) = Read(data, region, x * step, y * step);
                int o = (y * width + x) * 4;
                if (depth)
                {
                    byte d = Unit(max > min ? (r - min) / (max - min) : r);
                    pixels[o] = pixels[o + 1] = pixels[o + 2] = d;
                    pixels[o + 3] = 255;
                    continue;
                }

                if (single)
                    g = b = r;
                pixels[o] = Unit(r * exposure);
                pixels[o + 1] = Unit(g * exposure);
                pixels[o + 2] = Unit(b * exposure);
                pixels[o + 3] = Unit(a);
            }
        }
        return pixels;
    }

    private static (float R, float G, float B, float A) Read(ReadOnlySpan<byte> data, in CopyRegion region, int x, int y)
    {
        // Depth planes are copied at 4 bytes a texel, except D16 at 2.
        int stride = region.Format == PixelFormat.D16_UNorm ? 2 : IsDepth(region.Format) ? 4 : (int)region.Format.GetSizeInBytes();
        int offset = (int)region.Offset + (y * (int)region.Width + x) * stride;
        if (offset < 0 || offset + stride > data.Length)
            return default;

        ReadOnlySpan<byte> t = data.Slice(offset, stride);
        switch (region.Format)
        {
            case PixelFormat.R8_G8_B8_A8_UNorm:
            case PixelFormat.R8_G8_B8_A8_UNorm_SRgb: return (t[0] / 255f, t[1] / 255f, t[2] / 255f, t[3] / 255f);
            case PixelFormat.B8_G8_R8_A8_UNorm:
            case PixelFormat.B8_G8_R8_A8_UNorm_SRgb: return (t[2] / 255f, t[1] / 255f, t[0] / 255f, t[3] / 255f);
            case PixelFormat.R8_UNorm: return (t[0] / 255f, 0, 0, 1);
            case PixelFormat.R8_G8_UNorm: return (t[0] / 255f, t[1] / 255f, 0, 1);
            case PixelFormat.R16_UNorm:
            case PixelFormat.D16_UNorm: return (U16(t, 0) / 65535f, 0, 0, 1);
            case PixelFormat.R16_Float: return (H(t, 0), 0, 0, 1);
            case PixelFormat.R16_G16_Float: return (H(t, 0), H(t, 2), 0, 1);
            case PixelFormat.R16_G16_B16_A16_Float: return (H(t, 0), H(t, 2), H(t, 4), H(t, 6));
            case PixelFormat.R32_Float:
            case PixelFormat.D32_Float:
            case PixelFormat.D32_Float_S8_UInt: return (F(t, 0), 0, 0, 1);
            case PixelFormat.R32_G32_Float: return (F(t, 0), F(t, 4), 0, 1);
            case PixelFormat.R32_G32_B32_A32_Float: return (F(t, 0), F(t, 4), F(t, 8), F(t, 12));
            case PixelFormat.D24_UNorm_S8_UInt: return ((BinaryPrimitives.ReadUInt32LittleEndian(t) & 0xFFFFFF) / 16777215f, 0, 0, 1);
            case PixelFormat.R10_G10_B10_A2_UNorm:
            {
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(t);
                return ((v & 0x3FF) / 1023f, ((v >> 10) & 0x3FF) / 1023f, ((v >> 20) & 0x3FF) / 1023f, (v >> 30) / 3f);
            }
            case PixelFormat.R11_G11_B10_Float:
            {
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(t);
                return (Small(v & 0x7FF, 6), Small((v >> 11) & 0x7FF, 6), Small((v >> 22) & 0x3FF, 5), 1);
            }
            default: return default;
        }
    }

    private static void DepthRange(ReadOnlySpan<byte> data, in CopyRegion region, int step, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        for (int y = 0; y < region.Height; y += step)
        {
            for (int x = 0; x < region.Width; x += step)
            {
                float d = Read(data, region, x, y).R;
                if (d is <= 0f or >= 1f)
                    continue;
                min = MathF.Min(min, d);
                max = MathF.Max(max, d);
            }
        }

        if (min > max)
        {
            min = 0;
            max = 1;
        }
    }

    private static byte Unit(float v) => (byte)Math.Clamp(v * 255f + 0.5f, 0f, 255f);

    private static ushort U16(ReadOnlySpan<byte> t, int o) => BinaryPrimitives.ReadUInt16LittleEndian(t[o..]);

    private static float F(ReadOnlySpan<byte> t, int o) => BinaryPrimitives.ReadSingleLittleEndian(t[o..]);

    private static float H(ReadOnlySpan<byte> t, int o) => (float)BinaryPrimitives.ReadHalfLittleEndian(t[o..]);

    private static float Small(uint bits, int mantissaBits)
    {
        uint exponent = bits >> mantissaBits;
        uint mantissa = bits & ((1u << mantissaBits) - 1);
        float m = mantissa / (float)(1 << mantissaBits);
        if (exponent == 0)
            return MathF.Pow(2, -14) * m;
        if (exponent == 31)
            return mantissa == 0 ? float.PositiveInfinity : float.NaN;
        return MathF.Pow(2, (int)exponent - 15) * (1 + m);
    }
}
