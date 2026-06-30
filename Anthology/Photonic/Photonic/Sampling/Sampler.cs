using Prowl.Vector;

namespace Prowl.Photonic.Sampling;

/// <summary>
/// Cheap deterministic PRNG (splitmix64 + xoshiro fast forward). Per-texel seeded so bakes
/// are reproducible. Not cryptographic, not great for high-dim Monte Carlo, plenty good for
/// per-texel hemisphere sampling.
/// </summary>
internal struct Sampler
{
    private ulong _s0, _s1;

    /// <summary>Construct with a 64-bit seed (typically derived from texel coordinates).</summary>
    public Sampler(ulong seed)
    {
        _s0 = SplitMix64(ref seed);
        _s1 = SplitMix64(ref seed);
        if ((_s0 | _s1) == 0) _s1 = 0x9E3779B97F4A7C15UL;
    }

    /// <summary>Raw 32-bit value. Faster than <see cref="NextFloat"/> when you only need bits (e.g. table indexing).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public uint NextU32()
    {
        // xoroshiro128+ step, return top 32 bits.
        ulong s0 = _s0;
        ulong s1 = _s1;
        ulong result = s0 + s1;
        s1 ^= s0;
        _s0 = Rotl(s0, 24) ^ s1 ^ (s1 << 16);
        _s1 = Rotl(s1, 37);
        return (uint)(result >> 32);
    }

    /// <summary>Uniform [0, 1) float.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public float NextFloat()
    {
        // xoroshiro128+, top 24 bits -> float in [0,1)
        ulong s0 = _s0;
        ulong s1 = _s1;
        ulong result = s0 + s1;
        s1 ^= s0;
        _s0 = Rotl(s0, 24) ^ s1 ^ (s1 << 16);
        _s1 = Rotl(s1, 37);
        return (result >> 40) * (1f / 16777216f);
    }

    /// <summary>Uniform [0,1) Float2.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public Float2 NextFloat2() => new Float2(NextFloat(), NextFloat());

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

/// <summary>
/// Hemisphere sampling and lightmap-friendly utility transforms. All routines assume
/// the surface normal is unit length.
/// </summary>
internal static class Hemisphere
{
    /// <summary>
    /// Cosine-weighted hemisphere sample around <paramref name="normal"/>. Sampling PDF is
    /// <c>cosθ/π</c>, so the integrator cancels the cosθ term and multiplies by the lambert albedo / π.
    /// </summary>
    public static Float3 SampleCosine(Float3 normal, float u1, float u2)
    {
        // Malley's method: sample a disk, lift to the hemisphere.
        float r = (float)System.Math.Sqrt(u1);
        float phi = 2f * (float)System.Math.PI * u2;
        float x = r * (float)System.Math.Cos(phi);
        float y = r * (float)System.Math.Sin(phi);
        float z = (float)System.Math.Sqrt(System.Math.Max(0f, 1f - u1));

        BuildOrthonormalBasis(normal, out var t, out var b);
        return Float3.Normalize(t * x + b * y + normal * z);
    }

    /// <summary>
    /// Uniform (solid-angle) hemisphere sample around <paramref name="normal"/>. Sampling PDF is the
    /// constant 1/(2π). Preferred over cosine sampling when projecting incoming radiance onto a
    /// spherical-harmonic basis: it avoids the cosθ weighting that would otherwise have to be divided
    /// back out (and blow up near the horizon), giving a clean, low-variance projection.
    /// </summary>
    public static Float3 SampleUniform(Float3 normal, float u1, float u2)
    {
        float cosT = u1; // z uniform in [0,1] -> uniform over the hemisphere by solid angle
        float sinT = (float)System.Math.Sqrt(System.Math.Max(0f, 1f - u1 * u1));
        float phi = 2f * (float)System.Math.PI * u2;
        float x = sinT * (float)System.Math.Cos(phi);
        float y = sinT * (float)System.Math.Sin(phi);
        BuildOrthonormalBasis(normal, out var t, out var b);
        return Float3.Normalize(t * x + b * y + normal * cosT);
    }

    /// <summary>
    /// Frisvad's "Building an orthonormal basis, revisited": no branches, no trig. Produces a
    /// pair (tangent, bitangent) such that (t, b, n) is right-handed and orthonormal.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static void BuildOrthonormalBasis(Float3 n, out Float3 t, out Float3 b)
    {
        float sign = n.Z >= 0f ? 1f : -1f;
        float a = -1f / (sign + n.Z);
        float bb = n.X * n.Y * a;
        t = new Float3(1f + sign * n.X * n.X * a, sign * bb, -sign * n.X);
        b = new Float3(bb, sign + n.Y * n.Y * a, -n.Y);
    }
}
