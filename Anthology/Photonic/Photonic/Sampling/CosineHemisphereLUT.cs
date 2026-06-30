using Prowl.Vector;

namespace Prowl.Photonic.Sampling;

/// <summary>
/// Static 16384-entry cosine-weighted hemisphere lookup, in tangent space (N = +Z). Generated
/// once with a Halton-2,3 low-discrepancy sequence: each consecutive pair of entries is well
/// stratified, which kills the banding you'd get from a naive random LUT.
/// </summary>
/// <remarks>
/// Use:
/// <code>
/// uint i = sampler.NextU32();
/// var tsd = CosineHemisphereLUT.Get(i);
/// Hemisphere.BuildOrthonormalBasis(normal, out var T, out var B);
/// var worldDir = T * tsd.X + B * tsd.Y + normal * tsd.Z;
/// </code>
/// The cost saved vs <see cref="Hemisphere.SampleCosine"/> is the sqrt+sin+cos per bounce ray.
/// On a Sponza-scale bake that's ~10M trig calls saved.
/// </remarks>
internal static class CosineHemisphereLUT
{
    /// <summary>Number of pre-generated tangent-space directions. Power of two so masking works.</summary>
    public const int Size = 16384;
    private const int Mask = Size - 1;

    private static readonly Float3[] _dirs = Build();

    /// <summary>Lookup by any 32-bit value (auto-masked). Returns a unit-length cosine-weighted hemisphere direction.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Float3 Get(uint index) => _dirs[(int)(index & Mask)];

    /// <summary>Direct array access (read-only). Useful for debug/diagnostic callers.</summary>
    public static System.ReadOnlySpan<Float3> Directions => _dirs;

    private static Float3[] Build()
    {
        var arr = new Float3[Size];
        // Halton-2,3 over [0,1)^2; map u1 via Malley's method to a cosine-weighted hemisphere.
        for (int i = 0; i < Size; i++)
        {
            float u1 = Halton(i + 1, 2);
            float u2 = Halton(i + 1, 3);
            float r = (float)System.Math.Sqrt(u1);
            float phi = 2f * (float)System.Math.PI * u2;
            float x = r * (float)System.Math.Cos(phi);
            float y = r * (float)System.Math.Sin(phi);
            float z = (float)System.Math.Sqrt(System.Math.Max(0f, 1f - u1));
            arr[i] = new Float3(x, y, z);
        }
        return arr;
    }

    private static float Halton(int index, int b)
    {
        float f = 1f, result = 0f;
        while (index > 0)
        {
            f /= b;
            result += f * (index % b);
            index /= b;
        }
        return result;
    }
}
