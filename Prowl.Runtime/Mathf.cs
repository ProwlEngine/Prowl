using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Prowl.Runtime
{
    // Some of these methods are taken from Freya Holmér's Mathfs class (https://github.com/FreyaHolmer/Mathfs)

    // This class is called Mathf to be consistent with Unity's naming conventions but its actually MathD as it uses doubles instead of doubles
    public static class Mathf
    {
        private const MethodImplOptions IN = MethodImplOptions.AggressiveInlining;

        #region Constants

        /// <summary>The circle constant. Defined as the circumference of a circle divided by its radius. Equivalent to 2*pi</summary>
        public const double TAU = 6.28318530717959;

        /// <summary>An obscure circle constant. Defined as the circumference of a circle divided by its diameter. Equivalent to 0.5*tau</summary>
        public const double PI = 3.14159265359;

        /// <summary>Euler's number. The base of the natural logarithm. f(x)=e^x is equal to its own derivative</summary>
        public const double E = 2.71828182846;

        /// <summary>The golden ratio. It is the value of a/b where a/b = (a+b)/a. It's the positive root of x^2-x-1</summary>
        public const double GOLDEN_RATIO = 1.61803398875;

        /// <summary>The square root of two. The length of the vector (1,1)</summary>
        public const double SQRT2 = 1.41421356237;

        /// <summary>Multiply an angle in degrees by this, to convert it to radians</summary>
        public const double Deg2Rad = TAU / 360;

        /// <summary>Multiply an angle in radians by this, to convert it to degrees</summary>
        public const double Rad2Deg = 360 / TAU;

        /// <inheritdoc cref="double.MinValue"/>
        public static readonly double Epsilon = double.MinValue;

        /// <inheritdoc cref="double.PositiveInfinity"/>
        public const double Infinity = double.PositiveInfinity;

        /// <inheritdoc cref="double.NegativeInfinity"/>
        public const double NegativeInfinity = double.NegativeInfinity;

        #endregion

        #region Operations/Methods

        [MethodImpl(IN)]
        public static bool IsValid(double x)
        {
            return !double.IsNaN(x) && !double.IsInfinity(x);
        }

        /// <inheritdoc cref="Math.Sin(double)"/>
        [MethodImpl(IN)] public static double Sin(double value) => Math.Sin(value);

        /// <inheritdoc cref="Math.Cos(double)"/>
        [MethodImpl(IN)] public static double Cos(double value) => Math.Cos(value);

        /// <inheritdoc cref="Math.Tan(double)"/>
        [MethodImpl(IN)] public static double Tan(double value) => Math.Tan(value);

        /// <inheritdoc cref="Math.Asin(double)"/>
        [MethodImpl(IN)] public static double Asin(double value) => Math.Asin(value);

        /// <inheritdoc cref="Math.Acos(double)"/>
        [MethodImpl(IN)] public static double Acos(double value) => Math.Acos(value);

        /// <inheritdoc cref="Math.Atan(double)"/>
        [MethodImpl(IN)] public static double Atan(double value) => Math.Atan(value);

        /// <inheritdoc cref="Math.Atan2(double, double)"/>
        [MethodImpl(IN)] public static double Atan(double y, double x) => Math.Atan2(y, x);

        /// <inheritdoc cref="Math.Sqrt(double)"/>
        [MethodImpl(IN)] public static double Sqrt(double value) => Math.Sqrt(value);

        /// <inheritdoc cref="Math.Abs(double)"/>
        [MethodImpl(IN)] public static double Abs(double value) => Math.Abs(value);

        /// <inheritdoc cref="Math.Pow(double, double)"/>
        [MethodImpl(IN)] public static double Pow(double value, double exponent) => Math.Pow(value, exponent);

        /// <inheritdoc cref="Math.Exp(double)"/>
        [MethodImpl(IN)] public static double Exp(double power) => Math.Exp(power);

        /// <inheritdoc cref="Math.Log(double, double)"/>
        [MethodImpl(IN)] public static double Log(double value, double newBase) => Math.Log(value, newBase);

        /// <inheritdoc cref="Math.Log(double)"/>
        [MethodImpl(IN)] public static double Log(double value) => Math.Log(value);

        /// <inheritdoc cref="Math.Log10(double)"/>
        [MethodImpl(IN)] public static double Log10(double value) => Math.Log10(value);

        /// <inheritdoc cref="Math.Clamp(double,double,double)"/>
        [MethodImpl(IN)] public static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);

        /// <inheritdoc cref="Math.Clamp(double,double,double)"/>
        [MethodImpl(IN)] public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

        [MethodImpl(IN)] public static double Clamp01(double value) => Clamp(value, 0, 1);

        [MethodImpl(IN)] public static double Min(params double[] values) => values.Min();

        [MethodImpl(IN)] public static double Max(params double[] values) => values.Max();

        [MethodImpl(IN)] public static int Min(params int[] values) => values.Min();

        [MethodImpl(IN)] public static int Max(params int[] values) => values.Max();

        [MethodImpl(IN)] public static double Sign(double value) => value >= 0 ? 1 : -1;

        [MethodImpl(IN)] public static double Floor(double value) => Math.Floor(value);

        [MethodImpl(IN)] public static int FloorToInt(double value) => (int)Math.Floor(value);

        [MethodImpl(IN)] public static double Ceil(double value) => Math.Ceiling(value);

        [MethodImpl(IN)] public static int CeilToInt(double value) => (int)Math.Ceiling(value);

        [MethodImpl(IN)] public static double Round(double value, MidpointRounding midpointRounding = MidpointRounding.ToEven) => (double)Math.Round(value, midpointRounding);

        [MethodImpl(IN)] public static double Round(double value, double snapInterval, MidpointRounding midpointRounding = MidpointRounding.ToEven) => Math.Round(value / snapInterval, midpointRounding) * snapInterval;

        [MethodImpl(IN)] public static int RoundToInt(double value, MidpointRounding midpointRounding = MidpointRounding.ToEven) => (int)Math.Round(value, midpointRounding);

        [MethodImpl(IN)] public static double Frac(double x) => x - Floor(x);

        /// <summary> Repeats the given value in the interval specified by length </summary>
        [MethodImpl(IN)] public static double Repeat(double value, double length) => Clamp(value - Floor(value / length) * length, 0.0, length);

        /// <summary> Repeats a value within a range, going back and forth </summary>
        [MethodImpl(IN)] public static double PingPong(double t, double length) => length - Abs(Repeat(t, length * 2) - length);

        /// <summary> Cubic EaseInOut </summary>
        [MethodImpl(IN)] public static double Smooth01(double x) => x * x * (3 - 2 * x);

        /// <summary> Quintic EaseInOut </summary>
        [MethodImpl(IN)] public static double Smoother01(double x) => x * x * x * (x * (x * 6 - 15) + 10);

        [MethodImpl(IN)] public static double Lerp(double a, double b, double t) => (1 - t) * a + t * b;

        [MethodImpl(IN)] public static Vector2 Lerp(Vector2 a, Vector2 b, Vector2 t) => new(Lerp(a.x, b.x, t.x), Lerp(a.y, b.y, t.y));

        [MethodImpl(IN)] public static Vector3 Lerp(Vector3 a, Vector3 b, Vector3 t) => new(Lerp(a.x, b.x, t.x), Lerp(a.y, b.y, t.y), Lerp(a.z, b.z, t.z));

        [MethodImpl(IN)] public static Vector4 Lerp(Vector4 a, Vector4 b, Vector4 t) => new(Lerp(a.x, b.x, t.x), Lerp(a.y, b.y, t.y), Lerp(a.z, b.z, t.z), Lerp(a.w, b.w, t.w));

        [MethodImpl(IN)] public static double LerpClamped(double a, double b, double t) => Lerp(a, b, Clamp01(t));

        [MethodImpl(IN)] public static double LerpSmooth(double a, double b, double t) => Lerp(a, b, Smooth01(Clamp01(t)));

        public static double MoveTowards(double current, double target, double maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta)
                return target;
            return current + Math.Sign(target - current) * maxDelta;
        }
        public static Vector2 ClampMagnitude(Vector2 v, double min, double max)
        {
            double mag = v.magnitude;
            return mag < min ? (v / mag) * min : mag > max ? (v / mag) * max : v;
        }

        public static Vector3 ClampMagnitude(Vector3 v, double min, double max)
        {
            double mag = v.magnitude;
            return mag < min ? (v / mag) * min : mag > max ? (v / mag) * max : v;
        }

        public static double Angle(Quaternion a, Quaternion b)
        {
            double v = Min(Abs(Quaternion.Dot(a, b)), 1);
            return v > 0.999998986721039 ? 0.0 : (Math.Acos(v) * 2.0);
        }

        public static double LerpAngle(double aRad, double bRad, double t)
        {
            double delta = Repeat(bRad - aRad, TAU);
            if (delta > PI) delta -= TAU;
            return aRad + delta * Clamp01(t);
        }

        [MethodImpl(IN)] public static uint Pack4f(this Vector4 color) => Pack4u((uint)(color.w * 255), (uint)(color.x * 255), (uint)(color.y * 255), (uint)(color.z * 255));

        [MethodImpl(IN)] public static uint Pack4u(uint a, uint r, uint g, uint b) => (a << 24) + (r << 16) + (g << 8) + b;

        [MethodImpl(IN)] public static int ComputeMipLevels(int width, int height) => (int)Math.Log2(Math.Max(width, height));

        #endregion

        #region Extensions

        public static Vector2 ToDouble(this System.Numerics.Vector2 v) => new(v.X, v.Y);
        public static Vector3 ToDouble(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
        public static Vector4 ToDouble(this System.Numerics.Vector4 v) => new(v.X, v.Y, v.Z, v.W);
        public static Quaternion ToDouble(this System.Numerics.Quaternion v) => new(v.X, v.Y, v.Z, v.W);

        public static Matrix4x4 ToDouble(this System.Numerics.Matrix4x4 m)
        {
            Matrix4x4 result;
            result.M11 = m.M11;
            result.M12 = m.M12;
            result.M13 = m.M13;
            result.M14 = m.M14;

            result.M21 = m.M21;
            result.M22 = m.M22;
            result.M23 = m.M23;
            result.M24 = m.M24;

            result.M31 = m.M31;
            result.M32 = m.M32;
            result.M33 = m.M33;
            result.M34 = m.M34;

            result.M41 = m.M41;
            result.M42 = m.M42;
            result.M43 = m.M43;
            result.M44 = m.M44;
            return result;
        }

        [MethodImpl(IN)]
        public static Vector3 GetRotation(this Quaternion r)
        {
            double yaw = Math.Atan2(2.0 * (r.y * r.w + r.x * r.z), 1.0 - 2.0 * (r.x * r.x + r.y * r.y));
            double pitch = Math.Asin(2.0 * (r.x * r.w - r.y * r.z));
            double roll = Math.Atan2(2.0 * (r.x * r.y + r.z * r.w), 1.0 - 2.0 * (r.x * r.x + r.z * r.z));
            // If any nan or inf, set that value to 0
            if (double.IsNaN(yaw) || double.IsInfinity(yaw)) yaw = 0;
            if (double.IsNaN(pitch) || double.IsInfinity(pitch)) pitch = 0;
            if (double.IsNaN(roll) || double.IsInfinity(roll)) roll = 0;
            return new Vector3(yaw, pitch, roll);
        }

        [MethodImpl(IN)] public static Vector3 ToDeg(this Vector3 v) => new((double)(v.x * Rad2Deg), (double)(v.y * Rad2Deg), (double)(v.z * Rad2Deg));

        [MethodImpl(IN)] public static Vector3 ToRad(this Vector3 v) => new((double)(v.x * Deg2Rad), (double)(v.y * Deg2Rad), (double)(v.z * Deg2Rad));

        [MethodImpl(IN)] public static double ToDeg(this double v) => (double)(v * Rad2Deg);

        [MethodImpl(IN)] public static double ToRad(this double v) => (double)(v * Deg2Rad);

        [MethodImpl(IN)] public static float ToDeg(this float v) => (float)(v * Rad2Deg);

        [MethodImpl(IN)] public static float ToRad(this float v) => (float)(v * Deg2Rad);
        [MethodImpl(IN)] public static Quaternion GetQuaternion(this Vector3 vector) => Quaternion.CreateFromYawPitchRoll(vector.x, vector.y, vector.z);

        [MethodImpl(IN)]
        public static Vector3 NormalizeEulerAngleDegrees(this Vector3 angle)
        {
            double normalizedX = angle.x % 360;
            double normalizedY = angle.y % 360;
            double normalizedZ = angle.z % 360;
            if (normalizedX < 0) {
                normalizedX += 360;
            }

            if (normalizedY < 0) {
                normalizedY += 360;
            }

            if (normalizedZ < 0) {
                normalizedZ += 360;
            }

            return new(normalizedX, normalizedY, normalizedZ);
        }
        #endregion
    }
}
