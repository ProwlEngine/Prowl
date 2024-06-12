using Veldrid;
using System;

namespace Prowl.Runtime
{
    /// <summary>
    /// General texture format and validation utilities 
    /// </summary>
    public static class TextureUtility
    {
        /// <summary>
        /// Gets the size in bytes of a <see cref="PixelFormat"/>.
        /// </summary>
        public static uint PixelFormatBytes(PixelFormat format)
        {
            return format switch
            {
                // BGRA 8-bit 4-channel
                PixelFormat.B8_G8_R8_A8_UNorm => 4,
                PixelFormat.B8_G8_R8_A8_UNorm_SRgb => 4,

                // Depth + stencil 2-channel
                PixelFormat.D24_UNorm_S8_UInt => 4,
                PixelFormat.D32_Float_S8_UInt => 5,

                // RGBA Packed 4-channel
                PixelFormat.R10_G10_B10_A2_UInt => 4,
                PixelFormat.R10_G10_B10_A2_UNorm => 4,

                // RGB Packed 3-channel
                PixelFormat.R11_G11_B10_Float => 4,

                // RGBA 16-bit 4-channel
                PixelFormat.R16_G16_B16_A16_Float => 8,
                PixelFormat.R16_G16_B16_A16_SInt => 8,
                PixelFormat.R16_G16_B16_A16_SNorm => 8,
                PixelFormat.R16_G16_B16_A16_UInt => 8,
                PixelFormat.R16_G16_B16_A16_UNorm => 8,

                // RGBA 32-bit 4-channel
                PixelFormat.R32_G32_B32_A32_Float => 16,
                PixelFormat.R32_G32_B32_A32_SInt => 16,
                PixelFormat.R32_G32_B32_A32_UInt => 16,

                // RG 16-bit 2-channel
                PixelFormat.R16_G16_Float => 4,
                PixelFormat.R16_G16_SInt => 4,
                PixelFormat.R16_G16_SNorm => 4,
                PixelFormat.R16_G16_UInt => 4,
                PixelFormat.R16_G16_UNorm => 4,

                // R 16-bit 1-channel
                PixelFormat.R16_Float => 2,
                PixelFormat.R16_SInt => 2,
                PixelFormat.R16_SNorm => 2,
                PixelFormat.R16_UInt => 2,
                PixelFormat.R16_UNorm => 2,
                PixelFormat.R32_Float => 4,

                // RG 32-bit 2-channel
                PixelFormat.R32_G32_Float => 8,
                PixelFormat.R32_G32_SInt => 8,
                PixelFormat.R32_G32_UInt => 8,

                // R 32-bit 1-channel
                PixelFormat.R32_SInt => 4,
                PixelFormat.R32_UInt => 4,

                // RGBA 8-bit 4-channel
                PixelFormat.R8_G8_B8_A8_SInt => 4,
                PixelFormat.R8_G8_B8_A8_SNorm => 4,
                PixelFormat.R8_G8_B8_A8_UInt => 4,
                PixelFormat.R8_G8_B8_A8_UNorm => 4,
                PixelFormat.R8_G8_B8_A8_UNorm_SRgb => 4,

                // RG 8-bit 2-channel
                PixelFormat.R8_G8_SInt => 2,
                PixelFormat.R8_G8_SNorm => 2,
                PixelFormat.R8_G8_UInt => 2,
                PixelFormat.R8_G8_UNorm => 2,

                // R 8-bit 1-channel
                PixelFormat.R8_SInt => 1,
                PixelFormat.R8_SNorm => 1,
                PixelFormat.R8_UInt => 1,
                PixelFormat.R8_UNorm => 1,

                // Unknwons such as compressed BC(1-4) formats:
                // Should be replaced with a more appripriate exception
                _ => throw new Exception($"Cannot determine format byte size for {format}"),
            };
        }

        public static uint GetMipDimension(uint largestLevelDimension, uint mipLevel)
        {
            uint ret = largestLevelDimension;
            for (uint i = 0; i < mipLevel; i++)
            {
                ret /= 2;
            }

            return Math.Max(1, ret);
        }

        public static bool IsSupportedDescription(TextureDescription description, out PixelFormatProperties properties, out Exception exception)
        {
            uint width = description.Width;
            uint height = description.Height;
            uint depth = description.Depth;
            uint layers = description.ArrayLayers;
            uint mipLevels = description.MipLevels;

            if (!Graphics.Device.GetPixelFormatSupport(description.Format, description.Type, description.Usage, out properties))
            {
                exception = new Exception($"Platform does not support ({description.Format}) format with a ({description.Usage} {description.Type})");
                return false;
            }

            if (description.Usage.HasFlag(TextureUsage.Staging) && description.Usage != TextureUsage.Staging)
            {
                exception = new Exception("Staging textures are incompatible with other texture usages.");
                return false;
            }

            if (description.SampleCount != TextureSampleCount.Count1)
            {
                exception = new Exception("Use of multisampled textures is not currently supported.");
                return false;
            }

            if (description.Usage.HasFlag(TextureUsage.Cubemap))
            {
                exception = new Exception("Use of cubemap textures is currently not supported.");
                return false;
            }

            if (!ValidateRange(width, 1, properties.MaxWidth, nameof(width), out exception))
                return false;

            if (!ValidateRange(height, 1, properties.MaxHeight, nameof(height), out exception))
                return false;

            if (!ValidateRange(depth, 1, properties.MaxDepth, nameof(depth), out exception))
                return false;

            if (!ValidateRange(layers, 1, properties.MaxArrayLayers, nameof(layers), out exception))
                return false;

            if (!ValidateRange(mipLevels, 1, properties.MaxMipLevels, nameof(mipLevels), out exception))
                return false;

            exception = null;
            return true;
        }

        private static bool ValidateRange(uint value, uint min, uint max, string argsName, out Exception outOfRangeException)
        {
            if (value < min || value > max)
            {
                outOfRangeException = new ArgumentOutOfRangeException(argsName, value, $"{argsName} must be in the range ({min}, {max})");
                return false;
            }

            outOfRangeException = null;
            return true;
        }
    }
}
