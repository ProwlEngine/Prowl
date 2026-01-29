// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

namespace Prowl.Runtime.Graphite.OpenGL;

/// <summary>
/// OpenGL implementation of a GPU texture.
/// </summary>
public class GLTexture : Texture
{
    private readonly GLGraphiteDevice _device;
    internal uint Handle { get; private set; }
    internal TextureTarget Target { get; private set; }

    internal GLTexture(GLGraphiteDevice device, in TextureDescriptor descriptor)
    {
        _device = device;

        Dimension = descriptor.Dimension;
        Width = descriptor.Width;
        Height = descriptor.Height;
        Depth = descriptor.Depth;
        MipLevels = descriptor.MipLevels == 0 ? CalculateMaxMipLevels(descriptor) : descriptor.MipLevels;
        ArrayLayers = descriptor.ArrayLayers;
        Format = descriptor.Format;
        Usage = descriptor.Usage;
        SampleCount = descriptor.SampleCount;
        DebugName = descriptor.DebugName;

        Handle = _device.GL.GenTexture();
        Target = GetTextureTarget(descriptor);

        _device.GL.BindTexture(Target, Handle);
        AllocateStorage(descriptor);
        SetDefaultSamplerParams();
        _device.GL.BindTexture(Target, 0);
    }

    private static uint CalculateMaxMipLevels(in TextureDescriptor descriptor)
    {
        uint maxDimension = Math.Max(Math.Max(descriptor.Width, descriptor.Height), descriptor.Depth);
        return (uint)Math.Floor(Math.Log2(maxDimension)) + 1;
    }

    private TextureTarget GetTextureTarget(in TextureDescriptor descriptor)
    {
        bool isArray = descriptor.ArrayLayers > 1 && descriptor.Dimension != TextureDimension.TextureCube;
        bool isMultisample = descriptor.SampleCount != SampleCount.Count1;

        return descriptor.Dimension switch
        {
            TextureDimension.Texture1D => isArray ? TextureTarget.Texture1DArray : TextureTarget.Texture1D,
            TextureDimension.Texture2D => isMultisample
                ? (isArray ? TextureTarget.Texture2DMultisampleArray : TextureTarget.Texture2DMultisample)
                : (isArray ? TextureTarget.Texture2DArray : TextureTarget.Texture2D),
            TextureDimension.Texture3D => TextureTarget.Texture3D,
            TextureDimension.TextureCube => descriptor.ArrayLayers > 6
                ? TextureTarget.TextureCubeMapArray
                : TextureTarget.TextureCubeMap,
            _ => TextureTarget.Texture2D,
        };
    }

    private unsafe void AllocateStorage(in TextureDescriptor descriptor)
    {
        var internalFormat = GetInternalFormat(descriptor.Format);
        int width = (int)descriptor.Width;
        int height = (int)descriptor.Height;
        int depth = (int)descriptor.Depth;
        int levels = (int)MipLevels;
        int layers = (int)descriptor.ArrayLayers;
        int samples = (int)descriptor.SampleCount;

        switch (Target)
        {
            case TextureTarget.Texture1D:
                _device.GL.TexStorage1D(Target, (uint)levels, internalFormat, (uint)width);
                break;

            case TextureTarget.Texture1DArray:
                _device.GL.TexStorage2D(Target, (uint)levels, internalFormat, (uint)width, (uint)layers);
                break;

            case TextureTarget.Texture2D:
                _device.GL.TexStorage2D(Target, (uint)levels, internalFormat, (uint)width, (uint)height);
                break;

            case TextureTarget.Texture2DArray:
                _device.GL.TexStorage3D(Target, (uint)levels, internalFormat, (uint)width, (uint)height, (uint)layers);
                break;

            case TextureTarget.Texture2DMultisample:
                _device.GL.TexStorage2DMultisample(Target, (uint)samples, internalFormat, (uint)width, (uint)height, true);
                break;

            case TextureTarget.Texture2DMultisampleArray:
                _device.GL.TexStorage3DMultisample(Target, (uint)samples, internalFormat, (uint)width, (uint)height, (uint)layers, true);
                break;

            case TextureTarget.Texture3D:
                _device.GL.TexStorage3D(Target, (uint)levels, internalFormat, (uint)width, (uint)height, (uint)depth);
                break;

            case TextureTarget.TextureCubeMap:
                _device.GL.TexStorage2D(Target, (uint)levels, internalFormat, (uint)width, (uint)height);
                break;

            case TextureTarget.TextureCubeMapArray:
                _device.GL.TexStorage3D(Target, (uint)levels, internalFormat, (uint)width, (uint)height, (uint)layers);
                break;
        }
    }

    private void SetDefaultSamplerParams()
    {
        // Set default sampling parameters
        _device.GL.TexParameter(Target, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _device.GL.TexParameter(Target, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _device.GL.TexParameter(Target, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _device.GL.TexParameter(Target, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);

        if (MipLevels > 1)
        {
            _device.GL.TexParameter(Target, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            _device.GL.TexParameter(Target, TextureParameterName.TextureBaseLevel, 0);
            _device.GL.TexParameter(Target, TextureParameterName.TextureMaxLevel, (int)(MipLevels - 1));
        }
    }

    internal unsafe void Update(in TextureUpdateDescriptor descriptor, ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();

        // Validate - multisample textures cannot be updated via CPU
        if (Target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray)
        {
            throw new NotSupportedException(
                "Cannot update multisample textures. Multisample textures can only be rendered to.");
        }

        _device.GL.BindTexture(Target, Handle);

        int x = (int)descriptor.X;
        int y = (int)descriptor.Y;
        int z = (int)descriptor.Z;
        int w = (int)descriptor.Width;
        int h = (int)descriptor.Height;
        int d = (int)descriptor.Depth;
        int level = (int)descriptor.MipLevel;
        int layer = (int)descriptor.ArrayLayer;

        fixed (byte* ptr = data)
        {
            if (IsCompressedFormat(Format))
            {
                // Use compressed texture upload functions
                var internalFormat = (InternalFormat)GetInternalFormat(Format);
                uint imageSize = (uint)data.Length;

                switch (Target)
                {
                    case TextureTarget.Texture2D:
                        _device.GL.CompressedTexSubImage2D(Target, level, x, y, (uint)w, (uint)h,
                            internalFormat, imageSize, ptr);
                        break;

                    case TextureTarget.Texture2DArray:
                    case TextureTarget.Texture3D:
                        _device.GL.CompressedTexSubImage3D(Target, level, x, y,
                            Target == TextureTarget.Texture2DArray ? layer : z,
                            (uint)w, (uint)h, (uint)d, internalFormat, imageSize, ptr);
                        break;

                    case TextureTarget.TextureCubeMap:
                        _device.GL.CompressedTexSubImage2D(GetCubemapFaceTarget(layer), level, x, y, (uint)w, (uint)h,
                            internalFormat, imageSize, ptr);
                        break;

                    case TextureTarget.TextureCubeMapArray:
                        _device.GL.CompressedTexSubImage3D(Target, level, x, y, layer,
                            (uint)w, (uint)h, 1, internalFormat, imageSize, ptr);
                        break;

                    default:
                        throw new NotSupportedException(
                            $"Compressed texture format not supported for target: {Target}");
                }
            }
            else
            {
                // Use standard texture upload functions
                var format = GetPixelFormat(Format);
                var type = GetPixelType(Format);

                switch (Target)
                {
                    case TextureTarget.Texture1D:
                        _device.GL.TexSubImage1D(Target, level, x, (uint)w, format, type, ptr);
                        break;

                    case TextureTarget.Texture1DArray:
                    case TextureTarget.Texture2D:
                        if (Target == TextureTarget.Texture1DArray)
                            _device.GL.TexSubImage2D(Target, level, x, layer, (uint)w, 1, format, type, ptr);
                        else
                            _device.GL.TexSubImage2D(Target, level, x, y, (uint)w, (uint)h, format, type, ptr);
                        break;

                    case TextureTarget.Texture2DArray:
                    case TextureTarget.Texture3D:
                        _device.GL.TexSubImage3D(Target, level, x, y, Target == TextureTarget.Texture2DArray ? layer : z,
                            (uint)w, (uint)h, (uint)d, format, type, ptr);
                        break;

                    case TextureTarget.TextureCubeMap:
                        _device.GL.TexSubImage2D(GetCubemapFaceTarget(layer), level, x, y, (uint)w, (uint)h, format, type, ptr);
                        break;

                    case TextureTarget.TextureCubeMapArray:
                        _device.GL.TexSubImage3D(Target, level, x, y, layer, (uint)w, (uint)h, 1, format, type, ptr);
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported texture target for update: {Target}");
                }
            }
        }

        _device.GL.BindTexture(Target, 0);
    }

    /// <summary>
    /// Converts a cubemap face index (0-5) to the corresponding OpenGL TextureTarget.
    /// Face order: +X, -X, +Y, -Y, +Z, -Z
    /// </summary>
    private static TextureTarget GetCubemapFaceTarget(int faceIndex)
    {
        if (faceIndex < 0 || faceIndex > 5)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), $"Cubemap face index must be 0-5, got {faceIndex}");
        return (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + faceIndex);
    }

    /// <summary>
    /// Returns true if the texture format is a compressed format (BC1-BC7).
    /// </summary>
    internal static bool IsCompressedFormat(TextureFormat format) => format is
        TextureFormat.BC1Unorm or TextureFormat.BC1UnormSrgb or
        TextureFormat.BC2Unorm or TextureFormat.BC2UnormSrgb or
        TextureFormat.BC3Unorm or TextureFormat.BC3UnormSrgb or
        TextureFormat.BC4Unorm or TextureFormat.BC4Snorm or
        TextureFormat.BC5Unorm or TextureFormat.BC5Snorm or
        TextureFormat.BC6HUfloat or TextureFormat.BC6HSfloat or
        TextureFormat.BC7Unorm or TextureFormat.BC7UnormSrgb;

    /// <summary>
    /// Public accessor for internal format conversion.
    /// </summary>
    internal static SizedInternalFormat GetInternalFormatPublic(TextureFormat format) => GetInternalFormat(format);

    internal void GenerateMipmaps()
    {
        ThrowIfDisposed();
        _device.GL.BindTexture(Target, Handle);
        _device.GL.GenerateMipmap(Target);
        _device.GL.BindTexture(Target, 0);
    }

    private static SizedInternalFormat GetInternalFormat(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm => SizedInternalFormat.R8,
        TextureFormat.R8Snorm => SizedInternalFormat.R8SNorm,
        TextureFormat.R8Uint => SizedInternalFormat.R8ui,
        TextureFormat.R8Sint => SizedInternalFormat.R8i,
        TextureFormat.RG8Unorm => SizedInternalFormat.RG8,
        TextureFormat.RG8Snorm => SizedInternalFormat.RG8SNorm,
        TextureFormat.RG8Uint => SizedInternalFormat.RG8ui,
        TextureFormat.RG8Sint => SizedInternalFormat.RG8i,
        TextureFormat.RGBA8Unorm => SizedInternalFormat.Rgba8,
        TextureFormat.RGBA8UnormSrgb => SizedInternalFormat.Srgb8Alpha8,
        TextureFormat.RGBA8Snorm => SizedInternalFormat.Rgba8SNorm,
        TextureFormat.BGRA8Unorm => SizedInternalFormat.Rgba8, // OpenGL uses RGBA internal, BGRA pixel format
        TextureFormat.BGRA8UnormSrgb => SizedInternalFormat.Srgb8Alpha8,
        TextureFormat.RGBA8Uint => SizedInternalFormat.Rgba8ui,
        TextureFormat.RGBA8Sint => SizedInternalFormat.Rgba8i,
        TextureFormat.R16Uint => SizedInternalFormat.R16ui,
        TextureFormat.R16Sint => SizedInternalFormat.R16i,
        TextureFormat.R16Float => SizedInternalFormat.R16f,
        TextureFormat.RG16Uint => SizedInternalFormat.RG16ui,
        TextureFormat.RG16Sint => SizedInternalFormat.RG16i,
        TextureFormat.RG16Float => SizedInternalFormat.RG16f,
        TextureFormat.RGBA16Uint => SizedInternalFormat.Rgba16ui,
        TextureFormat.RGBA16Sint => SizedInternalFormat.Rgba16i,
        TextureFormat.RGBA16Float => SizedInternalFormat.Rgba16f,
        TextureFormat.R32Uint => SizedInternalFormat.R32ui,
        TextureFormat.R32Sint => SizedInternalFormat.R32i,
        TextureFormat.R32Float => SizedInternalFormat.R32f,
        TextureFormat.RG32Uint => SizedInternalFormat.RG32ui,
        TextureFormat.RG32Sint => SizedInternalFormat.RG32i,
        TextureFormat.RG32Float => SizedInternalFormat.RG32f,
        TextureFormat.RGBA32Uint => SizedInternalFormat.Rgba32ui,
        TextureFormat.RGBA32Sint => SizedInternalFormat.Rgba32i,
        TextureFormat.RGBA32Float => SizedInternalFormat.Rgba32f,
        TextureFormat.RGB10A2Unorm => SizedInternalFormat.Rgb10A2,
        TextureFormat.RG11B10Float => SizedInternalFormat.R11fG11fB10f,
        TextureFormat.Depth16Unorm => SizedInternalFormat.DepthComponent16,
        TextureFormat.Depth24Plus => SizedInternalFormat.DepthComponent24,
        TextureFormat.Depth24PlusStencil8 => SizedInternalFormat.Depth24Stencil8,
        TextureFormat.Depth32Float => SizedInternalFormat.DepthComponent32f,
        TextureFormat.Depth32FloatStencil8 => SizedInternalFormat.Depth32fStencil8,
        // S3TC/DXT compressed formats (extension constants)
        TextureFormat.BC1Unorm => (SizedInternalFormat)0x83F1, // GL_COMPRESSED_RGBA_S3TC_DXT1_EXT
        TextureFormat.BC1UnormSrgb => (SizedInternalFormat)0x8C4D, // GL_COMPRESSED_SRGB_ALPHA_S3TC_DXT1_EXT
        TextureFormat.BC2Unorm => (SizedInternalFormat)0x83F2, // GL_COMPRESSED_RGBA_S3TC_DXT3_EXT
        TextureFormat.BC2UnormSrgb => (SizedInternalFormat)0x8C4E, // GL_COMPRESSED_SRGB_ALPHA_S3TC_DXT3_EXT
        TextureFormat.BC3Unorm => (SizedInternalFormat)0x83F3, // GL_COMPRESSED_RGBA_S3TC_DXT5_EXT
        TextureFormat.BC3UnormSrgb => (SizedInternalFormat)0x8C4F, // GL_COMPRESSED_SRGB_ALPHA_S3TC_DXT5_EXT
        // RGTC compressed formats
        TextureFormat.BC4Unorm => (SizedInternalFormat)InternalFormat.CompressedRedRgtc1,
        TextureFormat.BC4Snorm => (SizedInternalFormat)InternalFormat.CompressedSignedRedRgtc1,
        TextureFormat.BC5Unorm => (SizedInternalFormat)InternalFormat.CompressedRGRgtc2,
        TextureFormat.BC5Snorm => (SizedInternalFormat)InternalFormat.CompressedSignedRGRgtc2,
        // BPTC compressed formats
        TextureFormat.BC6HUfloat => (SizedInternalFormat)InternalFormat.CompressedRgbBptcUnsignedFloat,
        TextureFormat.BC6HSfloat => (SizedInternalFormat)InternalFormat.CompressedRgbBptcSignedFloat,
        TextureFormat.BC7Unorm => (SizedInternalFormat)InternalFormat.CompressedRgbaBptcUnorm,
        TextureFormat.BC7UnormSrgb => (SizedInternalFormat)InternalFormat.CompressedSrgbAlphaBptcUnorm,
        _ => SizedInternalFormat.Rgba8,
    };

    private static PixelFormat GetPixelFormat(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm or TextureFormat.R8Snorm => PixelFormat.Red,
        TextureFormat.R8Uint or TextureFormat.R8Sint => PixelFormat.RedInteger,
        TextureFormat.R16Uint or TextureFormat.R16Sint => PixelFormat.RedInteger,
        TextureFormat.R16Float or TextureFormat.R32Float => PixelFormat.Red,
        TextureFormat.R32Uint or TextureFormat.R32Sint => PixelFormat.RedInteger,
        TextureFormat.RG8Unorm or TextureFormat.RG8Snorm => PixelFormat.RG,
        TextureFormat.RG8Uint or TextureFormat.RG8Sint => PixelFormat.RGInteger,
        TextureFormat.RG16Uint or TextureFormat.RG16Sint => PixelFormat.RGInteger,
        TextureFormat.RG16Float or TextureFormat.RG32Float => PixelFormat.RG,
        TextureFormat.RG32Uint or TextureFormat.RG32Sint => PixelFormat.RGInteger,
        TextureFormat.RGBA8Unorm or TextureFormat.RGBA8UnormSrgb or TextureFormat.RGBA8Snorm => PixelFormat.Rgba,
        TextureFormat.RGBA8Uint or TextureFormat.RGBA8Sint => PixelFormat.RgbaInteger,
        TextureFormat.BGRA8Unorm or TextureFormat.BGRA8UnormSrgb => PixelFormat.Bgra,
        TextureFormat.RGBA16Uint or TextureFormat.RGBA16Sint => PixelFormat.RgbaInteger,
        TextureFormat.RGBA16Float => PixelFormat.Rgba,
        TextureFormat.RGBA32Uint or TextureFormat.RGBA32Sint => PixelFormat.RgbaInteger,
        TextureFormat.RGBA32Float => PixelFormat.Rgba,
        TextureFormat.RGB10A2Unorm or TextureFormat.RG11B10Float => PixelFormat.Rgba,
        TextureFormat.Depth16Unorm or TextureFormat.Depth24Plus or TextureFormat.Depth32Float => PixelFormat.DepthComponent,
        TextureFormat.Depth24PlusStencil8 or TextureFormat.Depth32FloatStencil8 => PixelFormat.DepthStencil,
        _ => PixelFormat.Rgba,
    };

    private static PixelType GetPixelType(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm or TextureFormat.R8Uint or TextureFormat.RG8Unorm or TextureFormat.RG8Uint
            or TextureFormat.RGBA8Unorm or TextureFormat.RGBA8UnormSrgb or TextureFormat.RGBA8Uint
            or TextureFormat.BGRA8Unorm or TextureFormat.BGRA8UnormSrgb => PixelType.UnsignedByte,
        TextureFormat.R8Snorm or TextureFormat.R8Sint or TextureFormat.RG8Snorm or TextureFormat.RG8Sint
            or TextureFormat.RGBA8Snorm or TextureFormat.RGBA8Sint => PixelType.Byte,
        TextureFormat.R16Uint or TextureFormat.RG16Uint or TextureFormat.RGBA16Uint => PixelType.UnsignedShort,
        TextureFormat.R16Sint or TextureFormat.RG16Sint or TextureFormat.RGBA16Sint => PixelType.Short,
        TextureFormat.R16Float or TextureFormat.RG16Float or TextureFormat.RGBA16Float => PixelType.HalfFloat,
        TextureFormat.R32Uint or TextureFormat.RG32Uint or TextureFormat.RGBA32Uint => PixelType.UnsignedInt,
        TextureFormat.R32Sint or TextureFormat.RG32Sint or TextureFormat.RGBA32Sint => PixelType.Int,
        TextureFormat.R32Float or TextureFormat.RG32Float or TextureFormat.RGBA32Float => PixelType.Float,
        TextureFormat.RGB10A2Unorm => PixelType.UnsignedInt2101010Rev,
        TextureFormat.RG11B10Float => PixelType.UnsignedInt10f11f11fRev,
        TextureFormat.Depth16Unorm => PixelType.UnsignedShort,
        TextureFormat.Depth24Plus or TextureFormat.Depth32Float => PixelType.Float,
        TextureFormat.Depth24PlusStencil8 => PixelType.UnsignedInt248,
        TextureFormat.Depth32FloatStencil8 => PixelType.Float32UnsignedInt248Rev,
        _ => PixelType.UnsignedByte,
    };

    protected override void DisposeResources()
    {
        if (Handle != 0)
        {
            _device.GL.DeleteTexture(Handle);
            Handle = 0;
        }
    }
}
