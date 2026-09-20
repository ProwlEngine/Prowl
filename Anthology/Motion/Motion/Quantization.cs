using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>
/// Animation track quantization: range-based 16-bit encoding for translation
/// and scale components, and a "smallest three" 48-bit encoding for rotations (2-bit largest-component
/// index packed into spare bits, plus three 15-bit components in [-1/sqrt2, 1/sqrt2]).
/// </summary>
public static class Quantization
{
    private const float QuatRange = 0.70710678f; // 1/sqrt(2)
    private const float TwoQuatRange = 2f * QuatRange;

    /// <summary>Encodes a float in [min, min+range] to a 16-bit unsigned normalized value.</summary>
    public static ushort EncodeFloat(float value, float min, float range)
    {
        if (range <= 1e-12f)
            return 0;
        float n = Math.Clamp((value - min) / range, 0f, 1f);
        return (ushort)MathF.Round(n * 65535f);
    }

    /// <summary>Decodes a 16-bit unsigned normalized value back to [min, min+range].</summary>
    public static float DecodeFloat(ushort quantized, float min, float range) => min + (quantized / 65535f) * range;

    /// <summary>Encodes a rotation with the smallest-three scheme into three 16-bit words.</summary>
    public static void EncodeQuaternion(Quaternion q, out ushort m0, out ushort m1, out ushort m2)
    {
        Quaternion n = Quaternion.Normalize(q);
        Span<float> c = stackalloc float[4] { n.X, n.Y, n.Z, n.W };

        int largest = 0;
        for (int i = 1; i < 4; i++)
            if (MathF.Abs(c[i]) > MathF.Abs(c[largest]))
                largest = i;

        // Make the largest component positive so it can be reconstructed as +sqrt(1 - rest).
        if (c[largest] < 0f)
            for (int i = 0; i < 4; i++)
                c[i] = -c[i];

        Span<ushort> encoded = stackalloc ushort[3];
        int j = 0;
        for (int i = 0; i < 4; i++)
            if (i != largest)
                encoded[j++] = Encode15(c[i]);

        // Pack the 2-bit largest index into the free top bit of the first two words.
        m0 = (ushort)(encoded[0] | ((largest & 1) << 15));
        m1 = (ushort)(encoded[1] | (((largest >> 1) & 1) << 15));
        m2 = encoded[2];
    }

    /// <summary>Decodes a smallest-three rotation from three 16-bit words.</summary>
    public static Quaternion DecodeQuaternion(ushort m0, ushort m1, ushort m2)
    {
        int largest = ((m0 >> 15) & 1) | (((m1 >> 15) & 1) << 1);

        float a = Decode15((ushort)(m0 & 0x7FFF));
        float b = Decode15((ushort)(m1 & 0x7FFF));
        float cc = Decode15((ushort)(m2 & 0x7FFF));
        float largestValue = MathF.Sqrt(MathF.Max(0f, 1f - (a * a + b * b + cc * cc)));

        Span<float> comps = stackalloc float[4];
        comps[largest] = largestValue;
        int j = 0;
        for (int i = 0; i < 4; i++)
        {
            if (i == largest)
                continue;
            comps[i] = j == 0 ? a : j == 1 ? b : cc;
            j++;
        }

        return Quaternion.Normalize(new Quaternion(comps[0], comps[1], comps[2], comps[3]));
    }

    private static ushort Encode15(float value)
    {
        float n = Math.Clamp((value + QuatRange) / TwoQuatRange, 0f, 1f);
        return (ushort)MathF.Round(n * 32767f);
    }

    private static float Decode15(ushort quantized) => (quantized / 32767f) * TwoQuatRange - QuatRange;
}
