// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    /// <summary>
    /// A high-quality pseudo-random number generator with extensive utility methods.
    /// Uses the xoshiro256** algorithm for fast, high-quality random number generation.
    /// All range methods are inclusive of both min and max values.
    /// </summary>
    public class RNG
    {
        #region --- Core PRNG State (xoshiro256**) ---

        private ulong _s0, _s1, _s2, _s3;

        /// <summary>Shared static instance for convenient access.</summary>
        public static RNG Shared { get; } = new RNG();

        #endregion

        #region --- Constructors ---

        /// <summary>
        /// Initializes a new Random instance with a time-based seed.
        /// </summary>
        public RNG() : this((ulong)Environment.TickCount ^ (ulong)DateTime.UtcNow.Ticks)
        {
        }

        /// <summary>
        /// Initializes a new Random instance with the specified seed.
        /// </summary>
        /// <param name="seed">The seed value for the random number generator.</param>
        public RNG(ulong seed)
        {
            SetSeed(seed);
        }

        /// <summary>
        /// Initializes a new Random instance with the specified int seed.
        /// </summary>
        /// <param name="seed">The seed value for the random number generator.</param>
        public RNG(int seed) : this((ulong)seed)
        {
        }

        #endregion

        #region --- Seed Management ---

        /// <summary>
        /// Sets the seed for the random number generator.
        /// </summary>
        /// <param name="seed">The new seed value.</param>
        public void SetSeed(ulong seed)
        {
            // Use SplitMix64 to initialize the state from a single seed
            ulong z = seed;
            _s0 = SplitMix64(ref z);
            _s1 = SplitMix64(ref z);
            _s2 = SplitMix64(ref z);
            _s3 = SplitMix64(ref z);
        }

        private static ulong SplitMix64(ref ulong x)
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        #endregion

        #region --- Core Random Generation (xoshiro256**) ---

        /// <summary>
        /// Generates the next random 64-bit value using xoshiro256**.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong NextUInt64()
        {
            ulong result = RotateLeft(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;

            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

        #endregion

        #region --- Basic Numeric Types ---

        /// <summary>
        /// Returns a random float in the range [0, 1] (inclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextFloat() =>
            // Use upper 24 bits for single precision
            (NextUInt64() >> 40) * (1.0f / 16777216.0f);

        /// <summary>
        /// Returns a random double in the range [0, 1] (inclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double NextDouble() =>
            // Use upper 53 bits for double precision
            (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>
        /// Returns a random int in the range [0, int.MaxValue] (inclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt() => (int)(NextUInt64() >> 33);

        /// <summary>
        /// Returns a random uint in the range [0, uint.MaxValue] (inclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NextUInt() => (uint)(NextUInt64() >> 32);

        /// <summary>
        /// Returns a random long in the range [0, long.MaxValue] (inclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long NextLong() => (long)(NextUInt64() >> 1);

        /// <summary>
        /// Returns a random bool value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool NextBool() => (NextUInt64() & 1) == 1;

        #endregion

        #region --- Range Methods ---

        /// <summary>
        /// Returns a random float in the range [min, max] (inclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Range(float min, float max) => min + NextFloat() * (max - min);

        /// <summary>
        /// Returns a random double in the range [min, max] (inclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Range(double min, double max) => min + NextDouble() * (max - min);

        /// <summary>
        /// Returns a random int in the range [min, max] (inclusive).
        /// </summary>
        public int Range(int min, int max)
        {
            if (min > max) throw new ArgumentException("min must be <= max");
            if (min == max) return min;

            ulong range = (ulong)(max - min + 1);
            ulong threshold = (0UL - range) % range;

            ulong value;
            do
            {
                value = NextUInt64();
            } while (value < threshold);

            return min + (int)(value % range);
        }

        /// <summary>
        /// Returns a random uint in the range [min, max] (inclusive).
        /// </summary>
        public uint Range(uint min, uint max)
        {
            if (min > max) throw new ArgumentException("min must be <= max");
            if (min == max) return min;

            ulong range = (ulong)(max - min + 1);
            ulong threshold = (0UL - range) % range;

            ulong value;
            do
            {
                value = NextUInt64();
            } while (value < threshold);

            return min + (uint)(value % range);
        }

        /// <summary>
        /// Returns a random long in the range [min, max] (inclusive).
        /// </summary>
        public long Range(long min, long max)
        {
            if (min > max) throw new ArgumentException("min must be <= max");
            if (min == max) return min;

            // For simplicity, use double for large ranges
            return min + (long)(NextDouble() * (max - min + 1));
        }

        #endregion

        #region --- Vector Types ---

        /// <summary>
        /// Returns a random Float2 with components in the range [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float2 NextFloat2() => new Float2(NextFloat(), NextFloat());

        /// <summary>
        /// Returns a random Float3 with components in the range [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 NextFloat3() => new Float3(NextFloat(), NextFloat(), NextFloat());

        /// <summary>
        /// Returns a random Float4 with components in the range [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float4 NextFloat4() => new Float4(NextFloat(), NextFloat(), NextFloat(), NextFloat());

        /// <summary>
        /// Returns a random Float2 with components in the range [min, max].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float2 Range(Float2 min, Float2 max) => new Float2(Range(min.X, max.X), Range(min.Y, max.Y));

        /// <summary>
        /// Returns a random Float3 with components in the range [min, max].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 Range(Float3 min, Float3 max) => new Float3(Range(min.X, max.X), Range(min.Y, max.Y), Range(min.Z, max.Z));

        /// <summary>
        /// Returns a random Float4 with components in the range [min, max].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float4 Range(Float4 min, Float4 max) => new Float4(Range(min.X, max.X), Range(min.Y, max.Y), Range(min.Z, max.Z), Range(min.W, max.W));


        /// <summary>
        /// Returns a random Double2 with components in the range [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double2 NextDouble2() => new Double2(NextDouble(), NextDouble());

        /// <summary>
        /// Returns a random Double3 with components in the range [0, 1].
        /// summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 NextDouble3() => new Double3(NextDouble(), NextDouble(), NextDouble());

        /// <summary>
        /// Returns a random Double4 with components in the range [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double4 NextDouble4() => new Double4(NextDouble(), NextDouble(), NextDouble(), NextDouble());

        /// <summary>
        /// Returns a random Double2 with components in the range [min, max].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double2 Range(Double2 min, Double2 max) => new Double2(Range(min.X, max.X), Range(min.Y, max.Y));

        /// <summary>
        /// Returns a random Double3 with components in the range [min, max].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 Range(Double3 min, Double3 max) => new Double3(Range(min.X, max.X), Range(min.Y, max.Y), Range(min.Z, max.Z));

        /// <summary>
        /// Returns a random Double4 with components in the range [min, max].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double4 Range(Double4 min, Double4 max) => new Double4(Range(min.X, max.X), Range(min.Y, max.Y), Range(min.Z, max.Z), Range(min.W, max.W));

        #endregion

        #region --- Directional and Angular ---

        /// <summary>
        /// Returns a random angle in radians in the range [0, 2π].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextAngle() => NextFloat() * (float)Maths.PI * 2.0f;

        /// <summary>
        /// Returns a random angle in degrees in the range [0, 360].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextAngleDegrees() => NextFloat() * 360.0f;

        /// <summary>
        /// Returns a random angle in the range [minRadians, maxRadians].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float AngleRange(float minRadians, float maxRadians) => Range(minRadians, maxRadians);

        /// <summary>
        /// Returns a uniformly distributed random point on the unit circle.
        /// </summary>
        public Float2 OnUnitCircle()
        {
            float angle = NextAngle();
            return new Float2(Maths.Cos(angle), Maths.Sin(angle));
        }

        /// <summary>
        /// Returns a uniformly distributed random point inside the unit circle.
        /// </summary>
        public Float2 InsideUnitCircle()
        {
            float angle = NextAngle();
            float radius = Maths.Sqrt(NextFloat()); // sqrt for uniform distribution
            return new Float2(Maths.Cos(angle) * radius, Maths.Sin(angle) * radius);
        }

        /// <summary>
        /// Returns a uniformly distributed random point on the unit sphere surface.
        /// </summary>
        public Float3 OnUnitSphere()
        {
            float u = NextFloat();
            float v = NextFloat();
            float theta = 2.0f * (float)Maths.PI * u;
            float phi = Maths.Acos(2.0f * v - 1.0f);

            float sinPhi = Maths.Sin(phi);
            return new Float3(
                sinPhi * Maths.Cos(theta),
                sinPhi * Maths.Sin(theta),
                Maths.Cos(phi)
            );
        }

        /// <summary>
        /// Returns a uniformly distributed random point inside the unit sphere.
        /// </summary>
        public Float3 InsideUnitSphere()
        {
            Float3 direction = OnUnitSphere();
            float radius = Maths.Pow(NextFloat(), 1.0f / 3.0f); // cube root for uniform distribution
            return direction * radius;
        }

        /// <summary>
        /// Returns a random direction vector in 2D (normalized).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float2 Direction2D() => OnUnitCircle();

        /// <summary>
        /// Returns a random direction vector in 3D (normalized).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 Direction3D() => OnUnitSphere();

        #endregion

        #region --- Color Generation ---

        /// <summary>
        /// Returns a random color with RGB components in [0, 1] and alpha = 1.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float4 NextColor() => new Float4(NextFloat(), NextFloat(), NextFloat(), 1.0f);

        /// <summary>
        /// Returns a random color with RGB components in [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float4 NextColor(float alpha) => new Float4(NextFloat(), NextFloat(), NextFloat(), alpha);

        /// <summary>
        /// Returns a random color with RGBA components in [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float4 NextColorWithAlpha() => NextFloat4();

        /// <summary>
        /// Returns a random color in HSV space converted to RGB.
        /// </summary>
        /// <param name="hueRange">Hue range in [0, 1].</param>
        /// <param name="saturationRange">Saturation range in [0, 1].</param>
        /// <param name="valueRange">Value range in [0, 1].</param>
        public Float4 NextColorHSV(Float2 hueRange, Float2 saturationRange, Float2 valueRange)
        {
            float h = Range(hueRange.X, hueRange.Y);
            float s = Range(saturationRange.X, saturationRange.Y);
            float v = Range(valueRange.X, valueRange.Y);

            return HSVToRGB(h, s, v);
        }

        private static Float4 HSVToRGB(float h, float s, float v)
        {
            float c = v * s;
            float x = c * (1.0f - Maths.Abs((h * 6.0f) % 2.0f - 1.0f));
            float m = v - c;

            Float3 rgb;
            if (h < 1.0f / 6.0f) rgb = new Float3(c, x, 0);
            else if (h < 2.0f / 6.0f) rgb = new Float3(x, c, 0);
            else if (h < 3.0f / 6.0f) rgb = new Float3(0, c, x);
            else if (h < 4.0f / 6.0f) rgb = new Float3(0, x, c);
            else if (h < 5.0f / 6.0f) rgb = new Float3(x, 0, c);
            else rgb = new Float3(c, 0, x);

            return new Float4(rgb.X + m, rgb.Y + m, rgb.Z + m, 1.0f);
        }

        #endregion

        #region --- Probability and Weighted ---

        /// <summary>
        /// Returns true with the specified probability.
        /// </summary>
        /// <param name="probability">Probability in the range [0, 1].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Chance(float probability) => NextFloat() <= probability;

        /// <summary>
        /// Returns true with the specified probability.
        /// </summary>
        /// <param name="probability">Probability in the range [0, 1].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Chance(double probability) => NextDouble() <= probability;

        /// <summary>
        /// Performs a weighted random selection from the given weights.
        /// </summary>
        /// <param name="weights">Array of weights (must be non-negative).</param>
        /// <returns>Index of the selected item.</returns>
        public int WeightedChoice(float[] weights)
        {
            if (weights == null || weights.Length == 0)
                throw new ArgumentException("Weights array cannot be null or empty");

            float totalWeight = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] < 0) throw new ArgumentException("Weights must be non-negative");
                totalWeight += weights[i];
            }

            if (totalWeight <= 0) throw new ArgumentException("Total weight must be positive (Atleast one weight must be positive)");

            float randomValue = NextFloat() * totalWeight;
            float cumulativeWeight = 0;

            for (int i = 0; i < weights.Length; i++)
            {
                cumulativeWeight += weights[i];
                if (randomValue <= cumulativeWeight)
                    return i;
            }

            return weights.Length - 1; // Fallback (shouldn't happen with proper weights)
        }

        #endregion

        #region --- Collections ---

        /// <summary>
        /// Returns a random element from the array.
        /// </summary>
        public T Choice<T>(T[] array)
        {
            if (array == null || array.Length == 0)
                throw new ArgumentException("Array cannot be null or empty");

            return array[Range(0, array.Length - 1)];
        }

        /// <summary>
        /// Shuffles the array in place using Fisher-Yates algorithm.
        /// </summary>
        public void Shuffle<T>(T[] array)
        {
            if (array == null) return;

            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = Range(0, i);
                (array[i], array[j]) = (array[j], array[i]);
            }
        }

        /// <summary>
        /// Returns a shuffled copy of the array.
        /// </summary>
        public T[] Shuffled<T>(T[] array)
        {
            if (array == null) return null;

            T[] result = new T[array.Length];
            Array.Copy(array, result, array.Length);
            Shuffle(result);
            return result;
        }

        #endregion

        #region --- Noise-like Functions ---

        /// <summary>
        /// Returns a random value with Gaussian (normal) distribution.
        /// Uses Box-Muller transform.
        /// </summary>
        /// <param name="mean">Mean of the distribution.</param>
        /// <param name="standardDeviation">Standard deviation of the distribution.</param>
        public float NextGaussian(float mean = 0.0f, float standardDeviation = 1.0f)
        {
            // Box-Muller transform
            (float, float) boxMuller = (float.NaN, float.NaN);

            if (!float.IsNaN(boxMuller.Item2))
            {
                float result = boxMuller.Item2;
                boxMuller.Item2 = float.NaN;
                return mean + result * standardDeviation;
            }

            float u1 = NextFloat();
            float u2 = NextFloat();

            float mag = standardDeviation * Maths.Sqrt(-2.0f * Maths.Log(u1));
            float z0 = mag * Maths.Cos(2.0f * (float)Maths.PI * u2);
            float z1 = mag * Maths.Sin(2.0f * (float)Maths.PI * u2);

            boxMuller.Item2 = z1;
            return mean + z0;
        }

        /// <summary>
        /// Returns a random value with exponential distribution.
        /// </summary>
        /// <param name="lambda">Rate parameter (must be positive).</param>
        public float NextExponential(float lambda = 1.0f)
        {
            if (lambda <= 0) throw new ArgumentException("Lambda must be positive");
            return -Maths.Log(1.0f - NextFloat()) / lambda;
        }

        #endregion

        #region --- Utility ---

        /// <summary>
        /// Fills the byte array with random bytes.
        /// </summary>
        public void NextBytes(byte[] bytes)
        {
            if (bytes == null) return;

            for (int i = 0; i < bytes.Length; i += 8)
            {
                ulong value = NextUInt64();
                int remaining = Math.Min(8, bytes.Length - i);

                for (int j = 0; j < remaining; j++)
                {
                    bytes[i + j] = (byte)(value >> (j * 8));
                }
            }
        }

        /// <summary>
        /// Returns a random string of the specified length using the given character set.
        /// </summary>
        public string NextString(int length, string charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")
        {
            if (length <= 0) return string.Empty;
            if (string.IsNullOrEmpty(charset)) throw new ArgumentException("Charset cannot be null or empty");

            char[] result = new char[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = charset[Range(0, charset.Length - 1)];
            }

            return new string(result);
        }

        #endregion
    }
}
