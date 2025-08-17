using System.Drawing;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    /// <summary>
    /// Extensions for converting between different color vector types.
    /// </summary>
    public static class ColorConversionExtensions
    {
        /// <summary>
        /// Converts a Float3 color to Byte3 (0-1 range to 0-255 range).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Byte3 ToColor3b(this Float3 vector)
        {
            return new Byte3(
                (byte)(vector.X * 255f),
                (byte)(vector.Y * 255f),
                (byte)(vector.Z * 255f)
            );
        }

        /// <summary>
        /// Converts a Byte3 color to Float3 (0-255 range to 0-1 range).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 ToColor3f(this Byte3 vector)
        {
            return new Float3(
                vector.X / 255f,
                vector.Y / 255f,
                vector.Z / 255f
            );
        }

        /// <summary>
        /// Converts a Float4 color to Byte4 (0-1 range to 0-255 range).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Byte4 ToColor4b(this Float4 vector)
        {
            return new Byte4(
                (byte)(vector.X * 255f),
                (byte)(vector.Y * 255f),
                (byte)(vector.Z * 255f),
                (byte)(vector.W * 255f)
            );
        }

        /// <summary>
        /// Converts a Byte4 color to Float4 (0-255 range to 0-1 range).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 ToColor4f(this Byte4 vector)
        {
            return new Float4(
                vector.X / 255f,
                vector.Y / 255f,
                vector.Z / 255f,
                vector.W / 255f
            );
        }

        /// <summary>
        /// Converts a Byte4 color to System.Drawing.Color.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color ToColor(this Byte4 vector)
        {
            return Color.FromArgb(vector.W, vector.X, vector.Y, vector.Z);
        }

        /// <summary>
        /// Converts a Float4 color to System.Drawing.Color.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color ToColor(this Float4 vector)
        {
            return Color.FromArgb(
                (int)(vector.W * 255f),
                (int)(vector.X * 255f),
                (int)(vector.Y * 255f),
                (int)(vector.Z * 255f)
            );
        }

        /// <summary>
        /// Converts a Byte3 color to System.Drawing.Color.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color ToColor(this Byte3 vector, byte alpha = 255)
        {
            return Color.FromArgb(alpha, vector.X, vector.Y, vector.Z);
        }

        /// <summary>
        /// Converts a Float3 color to System.Drawing.Color.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color ToColor(this Float3 vector, float alpha = 1.0f)
        {
            return Color.FromArgb(
                (int)(alpha * 255f),
                (int)(vector.X * 255f),
                (int)(vector.Y * 255f),
                (int)(vector.Z * 255f)
            );
        }
    }
}
