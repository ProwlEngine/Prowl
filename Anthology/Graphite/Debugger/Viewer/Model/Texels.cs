using System;
using System.Buffers.Binary;

using Prowl.Graphite.Debugging;
using Prowl.Vector;

namespace Prowl.Graphite.Debugger.Viewer;

internal static class Texels
{
    public static bool IsDepth(PixelFormat format) => format is PixelFormat.D24_UNorm_S8_UInt or PixelFormat.D32_Float_S8_UInt;

    public static bool CanDecode(PixelFormat format) => !format.ToString().StartsWith("BC", StringComparison.Ordinal) && !format.ToString().StartsWith("ETC", StringComparison.Ordinal);

    public static int Stride(PixelFormat format) => IsDepth(format) ? 4 : (int)format.GetSizeInBytes();

    public static Float4 Read(ReadOnlySpan<byte> data, in CopyRegion region, int x, int y)
    {
        int stride = Stride(region.Format);
        int offset = (int)region.Offset + (y * (int)region.Width + x) * stride;
        if (offset < 0 || offset + stride > data.Length)
            return default;

        ReadOnlySpan<byte> t = data.Slice(offset, stride);
        switch (region.Format)
        {
            case PixelFormat.R8_G8_B8_A8_UNorm: return new Float4(t[0], t[1], t[2], t[3]) / 255f;
            case PixelFormat.B8_G8_R8_A8_UNorm: return new Float4(t[2], t[1], t[0], t[3]) / 255f;
            case PixelFormat.R8_G8_B8_A8_SNorm: return new Float4((sbyte)t[0], (sbyte)t[1], (sbyte)t[2], (sbyte)t[3]) / 127f;
            case PixelFormat.R8_G8_B8_A8_UInt: return new Float4(t[0], t[1], t[2], t[3]);
            case PixelFormat.R8_G8_B8_A8_SInt: return new Float4((sbyte)t[0], (sbyte)t[1], (sbyte)t[2], (sbyte)t[3]);
            case PixelFormat.R8_UNorm: return new Float4(t[0] / 255f, 0, 0, 1);
            case PixelFormat.R8_SNorm: return new Float4((sbyte)t[0] / 127f, 0, 0, 1);
            case PixelFormat.R8_UInt: return new Float4(t[0], 0, 0, 1);
            case PixelFormat.R8_SInt: return new Float4((sbyte)t[0], 0, 0, 1);
            case PixelFormat.R8_G8_UNorm: return new Float4(t[0] / 255f, t[1] / 255f, 0, 1);
            case PixelFormat.R8_G8_SNorm: return new Float4((sbyte)t[0] / 127f, (sbyte)t[1] / 127f, 0, 1);
            case PixelFormat.R8_G8_UInt: return new Float4(t[0], t[1], 0, 1);
            case PixelFormat.R8_G8_SInt: return new Float4((sbyte)t[0], (sbyte)t[1], 0, 1);
            case PixelFormat.R16_UNorm: return new Float4(U16(t, 0) / 65535f, 0, 0, 1);
            case PixelFormat.R16_SNorm: return new Float4(S16(t, 0) / 32767f, 0, 0, 1);
            case PixelFormat.R16_UInt: return new Float4(U16(t, 0), 0, 0, 1);
            case PixelFormat.R16_SInt: return new Float4(S16(t, 0), 0, 0, 1);
            case PixelFormat.R16_Float: return new Float4(H(t, 0), 0, 0, 1);
            case PixelFormat.R16_G16_UNorm: return new Float4(U16(t, 0) / 65535f, U16(t, 2) / 65535f, 0, 1);
            case PixelFormat.R16_G16_SNorm: return new Float4(S16(t, 0) / 32767f, S16(t, 2) / 32767f, 0, 1);
            case PixelFormat.R16_G16_UInt: return new Float4(U16(t, 0), U16(t, 2), 0, 1);
            case PixelFormat.R16_G16_SInt: return new Float4(S16(t, 0), S16(t, 2), 0, 1);
            case PixelFormat.R16_G16_Float: return new Float4(H(t, 0), H(t, 2), 0, 1);
            case PixelFormat.R16_G16_B16_A16_UNorm: return new Float4(U16(t, 0), U16(t, 2), U16(t, 4), U16(t, 6)) / 65535f;
            case PixelFormat.R16_G16_B16_A16_SNorm: return new Float4(S16(t, 0), S16(t, 2), S16(t, 4), S16(t, 6)) / 32767f;
            case PixelFormat.R16_G16_B16_A16_UInt: return new Float4(U16(t, 0), U16(t, 2), U16(t, 4), U16(t, 6));
            case PixelFormat.R16_G16_B16_A16_SInt: return new Float4(S16(t, 0), S16(t, 2), S16(t, 4), S16(t, 6));
            case PixelFormat.R16_G16_B16_A16_Float: return new Float4(H(t, 0), H(t, 2), H(t, 4), H(t, 6));
            case PixelFormat.R32_Float: return new Float4(F(t, 0), 0, 0, 1);
            case PixelFormat.R32_UInt: return new Float4(U32(t, 0), 0, 0, 1);
            case PixelFormat.R32_SInt: return new Float4(S32(t, 0), 0, 0, 1);
            case PixelFormat.R32_G32_Float: return new Float4(F(t, 0), F(t, 4), 0, 1);
            case PixelFormat.R32_G32_UInt: return new Float4(U32(t, 0), U32(t, 4), 0, 1);
            case PixelFormat.R32_G32_SInt: return new Float4(S32(t, 0), S32(t, 4), 0, 1);
            case PixelFormat.R32_G32_B32_A32_Float: return new Float4(F(t, 0), F(t, 4), F(t, 8), F(t, 12));
            case PixelFormat.R32_G32_B32_A32_UInt: return new Float4(U32(t, 0), U32(t, 4), U32(t, 8), U32(t, 12));
            case PixelFormat.R32_G32_B32_A32_SInt: return new Float4(S32(t, 0), S32(t, 4), S32(t, 8), S32(t, 12));
            case PixelFormat.R10_G10_B10_A2_UNorm:
                {
                    uint v = BinaryPrimitives.ReadUInt32LittleEndian(t);
                    return new Float4((v & 0x3FF) / 1023f, ((v >> 10) & 0x3FF) / 1023f, ((v >> 20) & 0x3FF) / 1023f, (v >> 30) / 3f);
                }
            case PixelFormat.R10_G10_B10_A2_UInt:
                {
                    uint v = BinaryPrimitives.ReadUInt32LittleEndian(t);
                    return new Float4(v & 0x3FF, (v >> 10) & 0x3FF, (v >> 20) & 0x3FF, v >> 30);
                }
            case PixelFormat.R11_G11_B10_Float:
                {
                    uint v = BinaryPrimitives.ReadUInt32LittleEndian(t);
                    return new Float4(Small(v & 0x7FF, 6), Small((v >> 11) & 0x7FF, 6), Small((v >> 22) & 0x3FF, 5), 1);
                }
            case PixelFormat.D24_UNorm_S8_UInt: return new Float4((U32(t, 0) & 0xFFFFFF) / 16777215f, 0, 0, 1);
            case PixelFormat.D32_Float_S8_UInt: return new Float4(F(t, 0), 0, 0, 1);
            default: return default;
        }
    }

    public static byte[] ToRgba8(ReadOnlySpan<byte> data, in CopyRegion region)
    {
        int width = (int)region.Width, height = (int)region.Height;
        byte[] pixels = new byte[width * height * 4];
        if (!CanDecode(region.Format))
            return pixels;

        bool depth = IsDepth(region.Format);
        bool single = Channels(region.Format) == 1;
        float min = 0, max = 1;
        if (depth)
            Range(data, region, out min, out max);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Float4 v = Read(data, region, x, y);
                int o = (y * width + x) * 4;
                if (depth)
                {
                    byte d = Unit(max > min ? (v.X - min) / (max - min) : v.X);
                    pixels[o] = pixels[o + 1] = pixels[o + 2] = d;
                    pixels[o + 3] = 255;
                    continue;
                }

                if (single)
                    v = new Float4(v.X, v.X, v.X, 1);
                pixels[o] = Unit(v.X);
                pixels[o + 1] = Unit(v.Y);
                pixels[o + 2] = Unit(v.Z);
                pixels[o + 3] = Unit(v.W);
            }

        return pixels;
    }

    public static int Channels(PixelFormat format)
    {
        string name = format.ToString();
        if (name.StartsWith("D", StringComparison.Ordinal))
            return 1;
        int count = 0;
        foreach (string part in name.Split('_'))
            if (part.Length > 1 && part[0] is 'R' or 'G' or 'B' or 'A' && char.IsDigit(part[1]))
                count++;
        return Math.Max(1, count);
    }

    private static void Range(ReadOnlySpan<byte> data, in CopyRegion region, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        for (int y = 0; y < region.Height; y++)
            for (int x = 0; x < region.Width; x++)
            {
                float d = Read(data, region, x, y).X;
                if (d is <= 0f or >= 1f)
                    continue;
                min = MathF.Min(min, d);
                max = MathF.Max(max, d);
            }

        if (min > max)
        {
            min = 0;
            max = 1;
        }
    }

    private static byte Unit(float v) => (byte)Math.Clamp(v * 255f + 0.5f, 0f, 255f);

    private static ushort U16(ReadOnlySpan<byte> t, int o) => BinaryPrimitives.ReadUInt16LittleEndian(t[o..]);

    private static short S16(ReadOnlySpan<byte> t, int o) => BinaryPrimitives.ReadInt16LittleEndian(t[o..]);

    private static uint U32(ReadOnlySpan<byte> t, int o) => BinaryPrimitives.ReadUInt32LittleEndian(t[o..]);

    private static int S32(ReadOnlySpan<byte> t, int o) => BinaryPrimitives.ReadInt32LittleEndian(t[o..]);

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
