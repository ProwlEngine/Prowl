using System;
using System.Numerics;
using System.Runtime.CompilerServices;

#pragma warning disable CA1062 // Validate arguments of public methods

namespace Prowl.Runtime
{
    /// <summary>
    /// Provides some general help math-related functions and values.
    /// </summary>
    public static class MathUtilities
    {
        /// <summary>The value of PI divided by 2.</summary>
        public const float PiOver2 = 1.5707963267948966192313216916398f;

        /// <summary>The value of PI divided by 4.</summary>
        public const float PiOver4 = 0.78539816339744830961566084581988f;

        /// <summary>The value of PI multiplied by 3 and divided by 2.</summary>
        public const float ThreePiOver2 = 4.7123889803846898576939650749193f;

        /// <summary>The value of PI multiplied by 2.</summary>
        public const float TwoPI = 6.283185307179586476925286766559f;

        public const double DegToRadFactor = Math.PI / 180;
        public const double RadToDefFactor = 180 / Math.PI;

        public static float UnwindDegrees(float angle)
        {
            while (angle > 180.0f)
                angle -= 360.0f;
            while (angle < -180.0f)
                angle += 360.0f;
            return angle;
        }

        public static float ArcTanAngle(float X, float Y)
        {
            if (X == 0)
            {
                if (Y == 1) return PiOver2;
                else return -PiOver2;
            }
            else if (X > 0) return (float)Math.Atan(Y / X);
            else if (X < 0)
            {
                if (Y > 0) return (float)Math.Atan(Y / X) + (float)Math.PI;
                else return (float)Math.Atan(Y / X) - (float)Math.PI;
            }
            else return 0;
        }

        //returns Euler angles that point from one point to another
        public static Vector3 AngleTo(Vector3 from, Vector3 location)
        {
            Vector3 angle = new Vector3();
            Vector3 v3 = Vector3.Normalize(location - from);
            angle.X = (float)Math.Asin(v3.Y);
            angle.Y = ArcTanAngle(-v3.Z, -v3.X);
            return angle;
        }

        /// <summary>
        /// Interpolates linearly between two values.
        /// </summary>
        /// <param name="min">The initial value in the interpolation.</param>
        /// <param name="max">The final value in the interpolation.</param>
        /// <param name="amount">The amount of interpolation, measured between 0 and 1.</param>
        public static float Lerp(float min, float max, float amount)
        {
            return min + (max - min) * amount;
        }

        /// <summary>
        /// Interpolates linearly between two values.
        /// </summary>
        /// <param name="min">The initial value in the interpolation.</param>
        /// <param name="max">The final value in the interpolation.</param>
        /// <param name="amount">The amount of interpolation, measured between 0 and 1.</param>
        /// <remarks>
        /// In comparison to <see cref="Lerp(float, float, float)"/>, this function is more
        /// precise when working with big values.
        /// </remarks>
        public static float LerpPrecise(float min, float max, float amount)
        {
            return (1 - amount) * min + max * amount;
        }

        /// <summary>
        /// Interpolates between two values using a cubic equation.
        /// </summary>
        /// <param name="min">The initial value in the interpolation.</param>
        /// <param name="max">The final value in the interpolation.</param>
        /// <param name="amount">The amount of interpolation, measured between 0 and 1.</param>
        public static float SmoothStep(float min, float max, float amount)
        {
            // Lerp using the polynomial: 3xx - 2xxx
            return Lerp(min, max, (3 - 2 * amount) * amount * amount);
        }

        /// <summary>
        /// Interpolates between two values using a cubic equation.
        /// </summary>
        /// <param name="min">The initial value in the interpolation.</param>
        /// <param name="max">The final value in the interpolation.</param>
        /// <param name="amount">The amount of interpolation, measured between 0 and 1.</param>
        /// <remarks>
        /// In comparison to <see cref="SmoothStep(float, float, float)"/>, this function is more
        /// precise when working with big values.
        /// </remarks>
        public static float SmoothStepPrecise(float min, float max, float amount)
        {
            return LerpPrecise(min, max, (3 - 2 * amount) * amount * amount);
        }

        /// <summary>
        /// Interpolates between two values using a 5th-degree equation.
        /// </summary>
        /// <param name="min">The initial value in the interpolation.</param>
        /// <param name="max">The final value in the interpolation.</param>
        /// <param name="amount">The amount of interpolation, measured between 0 and 1.</param>
        public static float SmootherStep(float min, float max, float amount)
        {
            // Lerp using the polynomial: 6(x^5) - 15(x^4) + 10(x^3)
            return Lerp(min, max, (6 * amount * amount - 15 * amount + 10) * amount * amount * amount);
        }

        /// <summary>
        /// Interpolates between two values using a 5th-degree equation.
        /// </summary>
        /// <param name="min">The initial value in the interpolation.</param>
        /// <param name="max">The final value in the interpolation.</param>
        /// <param name="amount">The amount of interpolation, measured between 0 and 1.</param>
        /// <remarks>
        /// In comparison to <see cref="SmootherStep(float, float, float)"/>, this function is more
        /// precise when working with big values.
        /// </remarks>
        public static float SmootherStepPrecise(float min, float max, float amount)
        {
            // Lerp using the polynomial: 6(x^5) - 15(x^4) + 10(x^3)
            return LerpPrecise(min, max, (6 * amount * amount - 15 * amount + 10) * amount * amount * amount);
        }

        /// <summary>
        /// Calculates the size to use for an array that needs resizing, where the new size
        /// will be a power of two times the previous capacity.
        /// </summary>
        /// <param name="currentCapacity">The current length of the array.</param>
        /// <param name="requiredCapacity">The minimum required length for the array.</param>
        /// <remarks>
        /// This is calculated with the following equation:<para/>
        /// <code>
        /// newCapacity = currentCapacity * pow(2, ceiling(log2(requiredCapacity/currentCapacity)));
        /// </code>
        /// </remarks>
        public static int GetNextCapacity(int currentCapacity, int requiredCapacity)
        {
            // Finds the smallest number that is greater than requiredCapacity and satisfies this equation:
            // " newCapacity = oldCapacity * 2^X " where X is an integer

            const double log2 = 0.69314718055994530941723212145818;
            int power = (int)Math.Ceiling(Math.Log(requiredCapacity / (double)currentCapacity) / log2);
            return currentCapacity * IntegerPow(2, power);
        }

        /// <summary>
        /// Calculates an integer value, raised to an integer exponent. Only works with positive values.
        /// </summary>
        public static int IntegerPow(int value, int exponent)
        {
            int r = 1;
            while (exponent > 0)
            {
                if ((exponent & 1) == 1)
                    r *= value;
                exponent >>= 1;
                value *= value;
            }
            return r;
        }

        /// <summary>
        /// Returns a random direction, as a unit vector.
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        public static Vector2 RandomDirection2(this Random random)
        {
            float angle = (float)(random.NextDouble() * 6.283185307179586476925286766559);
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        /// <summary>
        /// Returns a random direction, as a vector of a specified length.
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        /// <param name="length">The desired length of the direction vector.</param>
        public static Vector2 RandomDirection2(this Random random, float length)
        {
            return random.RandomDirection2() * length;
        }

        /// <summary>
        /// Returns a random direction, as a unit vector.
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        public static Vector3 RandomDirection3(this Random random)
        {
            float a = (float)(random.NextDouble() * 6.283185307179586476925286766559);
            float b = (float)(random.NextDouble() * Math.PI);
            float sinB = MathF.Sin(b);
            return new Vector3(sinB * MathF.Cos(a), sinB * MathF.Sin(a), MathF.Cos(b));
        }

        /// <summary>
        /// Returns a random direction, as a vector of a specified length.
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        /// <param name="length">The desired length of the direction vector.</param>
        public static Vector3 RandomDirection3(this Random random, float length)
        {
            return random.RandomDirection3() * length;
        }

        /// <summary>
        /// Returns a random floating-point number in the range [0.0, 1.0).
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        public static float NextFloat(this Random random)
        {
            return (float)random.NextDouble();
        }

        /// <summary>
        /// Returns a random floating-point number in the range [0.0, max) (or (max, 0.0] if negative).
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        /// <param name="max">The exclusive maximum value of the random number to be generated.</param>
        public static float NextFloat(this Random random, float max)
        {
            return (float)random.NextDouble() * max;
        }

        /// <summary>
        /// Returns a random floating-point number in the range [min, max) (or (max, min] if min>max)
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        /// <param name="min">The inclusive minimum value of the random number to be generated.</param>
        /// <param name="max">The exclusive maximum value of the random number to be generated.</param>
        public static float NextFloat(this Random random, float min, float max)
        {
            return (float)random.NextDouble() * (max - min) + min;
        }

        /// <summary>
        /// Returns a random floating-point number in the range [0.0, max) (or (max, 0.0] if negative).
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        /// <param name="max">The exclusive maximum value of the random number to be generated.</param>
        public static double NextDouble(this Random random, double max)
        {
            return random.NextDouble() * max;
        }

        /// <summary>
        /// Returns a random floating-point number in the range [min, max) (or (max, min] if min>max)
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        /// <param name="min">The inclusive minimum value of the random number to be generated.</param>
        /// <param name="max">The exclusive maximum value of the random number to be generated.</param>
        public static double NextDouble(this Random random, double min, double max)
        {
            return random.NextDouble() * (max - min) + min;
        }

        /// <summary>
        /// Returns a random boolean value.
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        public static bool NextBool(this Random random)
        {
            return (random.Next() & 1) == 0;
        }

        /// <summary>
        /// Constructs a completely randomized <see cref="Color"/>.
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        public static Color NextColor(this Random random)
        {
            // A single random value isn't enough, it's only 31 bits...
            // So we use one for RGB and an extra one for Alpha.
            unchecked
            {
                uint val = (uint)random.Next();
                return new Color32((byte)(val & 255), (byte)((val >> 8) & 255), (byte)((val >> 16) & 255), (byte)(random.Next() & 255));
            }
        }

        /// <summary>
        /// Constructs a randomized <see cref="Color"/> with an alpha value of 255.
        /// </summary>
        /// <param name="random">The <see cref="Random"/> to use for randomizing.</param>
        public static Color NextColorFullAlpha(this Random random)
        {
            unchecked
            {
                uint val = (uint)random.Next();
                Color color = new Color32((byte)(val & 255), (byte)((val >> 8) & 255), (byte)((val >> 16) & 255), (byte)255);
                return color;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeMipLevels(int width, int height)
        {
            return (int)MathF.Log2(MathF.Max(width, height));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Round(this float x)
        {
            return (int)MathF.Floor(x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 RotationYawPitchRoll(float yaw, float pitch, float roll)
        {
            Quaternion quaternion = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
            return RotationQuaternion(quaternion);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateTransform(Vector3 pos, Vector3 rotation, Vector3 scale)
        {
            return Matrix4x4.CreateTranslation(pos) * Matrix4x4.CreateFromYawPitchRoll(rotation.X, rotation.Y, rotation.Z) * Matrix4x4.CreateScale(scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateTransform(Vector3 pos, Vector3 rotation, float scale)
        {
            return Matrix4x4.CreateTranslation(pos) * Matrix4x4.CreateFromYawPitchRoll(rotation.X, rotation.Y, rotation.Z) * Matrix4x4.CreateScale(scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateTransform(Vector3 pos, float scale)
        {
            return Matrix4x4.CreateTranslation(pos) * Matrix4x4.CreateScale(scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateTransform(Vector3 pos, Vector3 scale)
        {
            return Matrix4x4.CreateTranslation(pos) * Matrix4x4.CreateScale(scale);
        }

        /// <summary>Creates a new quaternion from the given yaw, pitch, and roll.</summary>
        /// <param name="yaw">The yaw angle, in radians, around the Y axis.</param>
        /// <returns>The resulting quaternion.</returns>
        public static Quaternion CreateFromYaw(float yaw)
        {
            //  Roll first, about axis the object is facing, then
            //  pitch upward, then yaw to face into the new heading
            float sy, cy;

            float halfYaw = yaw * 0.5f;
            sy = MathF.Sin(halfYaw);
            cy = MathF.Cos(halfYaw);

            Quaternion result;

            result.X = 0;
            result.Y = sy;
            result.Z = 0;
            result.W = cy;

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Transform(Vector3 value, float yaw)
        {
            float sy, cy;

            float halfYaw = yaw * 0.5f;
            sy = MathF.Sin(halfYaw);
            cy = MathF.Cos(halfYaw);

            float y2 = sy + sy;

            float wy2 = cy * y2;
            float yy2 = sy * y2;

            return new Vector3(
                value.X * (1.0f - yy2) + value.Z * wy2,
                value.Y,
                value.X * wy2 + value.Z * (1.0f - yy2)
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 TransformOnlyYaw(this Vector3 value, Quaternion r)
        {
            float yaw = MathF.Atan2(2.0f * (r.Y * r.W + r.X * r.Z), 1.0f - 2.0f * (r.X * r.X + r.Y * r.Y));

            float halfYaw = yaw * 0.5f;
            float sy = MathF.Sin(halfYaw);
            float cy = MathF.Cos(halfYaw);

            float y2 = sy + sy;

            float wy2 = cy * y2;
            float yy2 = sy * y2;

            return new Vector3(value.X * (1.0f - yy2) + value.Z * wy2, value.Y, value.X * wy2 + value.Z * (1.0f - yy2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetYawPitchRoll(this Quaternion r, out float yaw, out float pitch, out float roll)
        {
            yaw = MathF.Atan2(2.0f * (r.Y * r.W + r.X * r.Z), 1.0f - 2.0f * (r.X * r.X + r.Y * r.Y));
            pitch = MathF.Asin(2.0f * (r.X * r.W - r.Y * r.Z));
            roll = MathF.Atan2(2.0f * (r.X * r.Y + r.Z * r.W), 1.0f - 2.0f * (r.X * r.X + r.Z * r.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetYaw(this Quaternion r, out float yaw)
        {
            yaw = MathF.Atan2(2.0f * (r.Y * r.W + r.X * r.Z), 1.0f - 2.0f * (r.X * r.X + r.Y * r.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 GetRotation(this Quaternion r)
        {
            float yaw = MathF.Atan2(2.0f * (r.Y * r.W + r.X * r.Z), 1.0f - 2.0f * (r.X * r.X + r.Y * r.Y));
            float pitch = MathF.Asin(2.0f * (r.X * r.W - r.Y * r.Z));
            float roll = MathF.Atan2(2.0f * (r.X * r.Y + r.Z * r.W), 1.0f - 2.0f * (r.X * r.X + r.Z * r.Z));
            return new Vector3(yaw, pitch, roll);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 ApplyMatrix(this Vector4 self, Matrix4x4 matrix)
        {
            return new Vector4(
                matrix.M11 * self.X + matrix.M12 * self.Y + matrix.M13 * self.Z + matrix.M14 * self.W,
                matrix.M21 * self.X + matrix.M22 * self.Y + matrix.M23 * self.Z + matrix.M24 * self.W,
                matrix.M31 * self.X + matrix.M32 * self.Y + matrix.M33 * self.Z + matrix.M34 * self.W,
                matrix.M41 * self.X + matrix.M42 * self.Y + matrix.M43 * self.Z + matrix.M44 * self.W
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ApplyMatrix(this Vector3 self, Matrix4x4 matrix)
        {
            return new Vector3(
                matrix.M11 * self.X + matrix.M12 * self.Y + matrix.M13 * self.Z + matrix.M14,
                matrix.M21 * self.X + matrix.M22 * self.Y + matrix.M23 * self.Z + matrix.M24,
                matrix.M31 * self.X + matrix.M32 * self.Y + matrix.M33 * self.Z + matrix.M34
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToDeg(this Vector3 v)
        {
            return new Vector3((float)(v.X * RadToDefFactor), (float)(v.Y * RadToDefFactor), (float)(v.Z * RadToDefFactor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToRad(this Vector3 v)
        {
            return new Vector3((float)(v.X * DegToRadFactor), (float)(v.Y * DegToRadFactor), (float)(v.Z * DegToRadFactor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToDeg(this float v)
        {
            return (float)(v * RadToDefFactor);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToRad(this float v)
        {
            return (float)(v * DegToRadFactor);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion GetQuaternion(this Vector3 vector)
        {
            return Quaternion.CreateFromYawPitchRoll(vector.X, vector.Y, vector.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 RotationQuaternion(Quaternion rotation)
        {
            float xx = rotation.X * rotation.X;
            float yy = rotation.Y * rotation.Y;
            float zz = rotation.Z * rotation.Z;
            float xy = rotation.X * rotation.Y;
            float zw = rotation.Z * rotation.W;
            float zx = rotation.Z * rotation.X;
            float yw = rotation.Y * rotation.W;
            float yz = rotation.Y * rotation.Z;
            float xw = rotation.X * rotation.W;

            Matrix4x4 result = Matrix4x4.Identity;
            result.M11 = 1.0f - 2.0f * (yy + zz);
            result.M12 = 2.0f * (xy + zw);
            result.M13 = 2.0f * (zx - yw);
            result.M21 = 2.0f * (xy - zw);
            result.M22 = 1.0f - 2.0f * (zz + xx);
            result.M23 = 2.0f * (yz + xw);
            result.M31 = 2.0f * (zx + yw);
            result.M32 = 2.0f * (yz - xw);
            result.M33 = 1.0f - 2.0f * (yy + xx);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 NormalizeEulerAngleDegrees(this Vector3 angle)
        {
            float normalizedX = angle.X % 360;
            float normalizedY = angle.Y % 360;
            float normalizedZ = angle.Z % 360;
            if (normalizedX < 0)
            {
                normalizedX += 360;
            }

            if (normalizedY < 0)
            {
                normalizedY += 360;
            }

            if (normalizedZ < 0)
            {
                normalizedZ += 360;
            }

            return new(normalizedX, normalizedY, normalizedZ);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 NormalizeEulerAngleDegrees(this Vector2 angle)
        {
            float normalizedX = angle.X % 360;
            float normalizedY = angle.Y % 360;
            if (normalizedX < 0)
            {
                normalizedX += 360;
            }

            if (normalizedY < 0)
            {
                normalizedY += 360;
            }

            return new(normalizedX, normalizedY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Pack(this Vector4 color)
        {
            return Pack((uint)(color.W * 255), (uint)(color.X * 255), (uint)(color.Y * 255), (uint)(color.Z * 255));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Pack(uint a, uint r, uint g, uint b)
        {
            return (a << 24) + (r << 16) + (g << 8) + b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LockQuaternionAxis(Quaternion r, Quaternion q, Vector3 mask)
        {
            float x = MathF.Atan2(2.0f * (r.Y * r.W + r.X * r.Z), 1.0f - 2.0f * (r.X * r.X + r.Y * r.Y)) * mask.X;
            float y = MathF.Asin(2.0f * (r.X * r.W - r.Y * r.Z)) * mask.Y;
            float z = MathF.Atan2(2.0f * (r.X * r.Y + r.Z * r.W), 1.0f - 2.0f * (r.X * r.X + r.Z * r.Z)) * mask.Z;

            float xHalf = x * 0.5f;
            float yHalf = y * 0.5f;
            float zHalf = z * 0.5f;

            float cx = MathF.Cos(xHalf);
            float cy = MathF.Cos(yHalf);
            float cz = MathF.Cos(zHalf);
            float sx = MathF.Sin(xHalf);
            float sy = MathF.Sin(yHalf);
            float sz = MathF.Sin(zHalf);

            q.W = (cz * cx * cy) + (sz * sx * sy);
            q.X = (cz * sx * cy) - (sz * cx * sy);
            q.Y = (cz * cx * sy) + (sz * sx * cy);
            q.Z = (sz * cx * cy) - (cz * sx * sy);
        }
    }
}
