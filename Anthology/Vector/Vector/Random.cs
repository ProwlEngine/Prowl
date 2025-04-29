// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Vector
{
    /// <summary> A Utility class with a bunch of Random types/values - Based on Shared </summary>
    public static class Random
    {
        private static System.Random Shared = new System.Random();

        /// <summary> Returns a random value between 0 and 1 </summary>
        public static double Value => Shared.NextDouble();

        /// <summary> Randomly returns either -1 or 1 </summary>
        public static double Sign => Value > 0.5f ? 1f : -1f;

        /// <summary> Randomly returns a value between min and max </summary>
        /// <param name="min"> The minimum value [inclusive] </param>
        /// <param name="max"> The maximum value [inclusive] </param>
        public static double Range(double min, double max) => min + (Value * (max - min));

        /// <summary> Randomly returns a value between min and max</summary>
        /// <param name="min"> The minimum value [inclusive] </param>
        /// <param name="max"> The maximum value [exclusive] </param>
        public static int Range(int min, int max) => Shared.Next(min, max);

        /// <summary> Returns a random point on the unit circle </summary>
        public static Vector2 OnUnitCircle
        {
            get
            {
                double angle = Value * 6.283185307179586476925286766559;
                return Vector2.Normalize(new Vector2(Math.Cos(angle), Math.Sin(angle)));
            }
        }

        /// <summary> Returns a random point inside the unit circle </summary>
        public static Vector2 InUnitCircle => OnUnitCircle * Value;

        /// <summary> Returns a random point inside the unit square [0-1] </summary>
        public static Vector2 InUnitSquare => new Vector2(Value, Value);

        /// <summary> Returns a random point on the unit sphere </summary>
        public static Vector3 OnUnitSphere
        {
            get
            {
                double a = Value * 6.283185307179586476925286766559;
                double b = Value * Math.PI;
                double sinB = Math.Sin(b);
                return Vector3.Normalize(new Vector3(sinB * Math.Cos(a), sinB * Math.Sin(a), Math.Cos(b)));
            }
        }

        /// <summary> Returns a random point inside the unit sphere </summary>
        public static Vector3 InUnitSphere => OnUnitSphere * Value;

        /// <summary> Returns a random point inside the unit cube [0-1] </summary>
        public static Vector3 InUnitCube => new Vector3(Value, Value, Value);

        /// <summary> Returns a random angle in radians from 0 to TAU </summary>
        public static double Angle => Value * MathD.TAU;

        /// <summary> Returns a random uniformly distributed rotation </summary>
        public static Quaternion Rotation => new Quaternion(OnUnitSphere, Value * MathD.TAU); // Will this work? TODO: Test

        /// <summary> Returns a random Boolean value </summary>
        public static bool Boolean => Value > 0.5f;

        /// <summary> Returns a random uniformly distributed color </summary>
        public static Vector4 Color
        {
            get
            {
                unchecked
                {
                    uint val = (uint)Shared.Next();
                    return new Vector4((byte)(val & 255), (byte)((val >> 8) & 255), (byte)((val >> 16) & 255), (byte)(Shared.Next() & 255)) / 255;
                }
            }
        }

        /// <summary> Returns a random uniformly distributed color with an alpha of 1.0 </summary>
        public static Vector4 ColorFullAlpha
        {
            get
            {
                unchecked
                {
                    uint val = (uint)Shared.Next();
                    return new Vector4((byte)(val & 255), (byte)((val >> 8) & 255), (byte)((val >> 16) & 255), 255) / 255;
                }
            }
        }
    }
}
