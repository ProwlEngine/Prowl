// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using Veldrid;

namespace Prowl.Editor;

public static class Texture2DLoader
{
    #region ImageMagick integration


    private static readonly Dictionary<Type, PixelFormat> formatLookup = new()
    {
        { typeof(A8),               PixelFormat.R8_UNorm                },
        { typeof(Bgra32),           PixelFormat.B8_G8_R8_A8_UNorm       },
        { typeof(Byte4),            PixelFormat.R8_G8_B8_A8_UInt        },
        { typeof(HalfSingle),       PixelFormat.R16_Float               },
        { typeof(HalfVector2),      PixelFormat.R16_G16_Float           },
        { typeof(HalfVector4),      PixelFormat.R16_G16_B16_A16_Float   },
        { typeof(NormalizedByte2),  PixelFormat.R8_G8_SNorm             },
        { typeof(NormalizedByte4),  PixelFormat.R8_G8_B8_A8_SNorm       },
        { typeof(NormalizedShort2), PixelFormat.R16_G16_SNorm           },
        { typeof(NormalizedShort4), PixelFormat.R16_G16_B16_A16_SNorm   },
        { typeof(Rg32),             PixelFormat.R16_G16_UNorm           },
        { typeof(Rgba1010102),      PixelFormat.R10_G10_B10_A2_UNorm    },
        { typeof(Rgba32),           PixelFormat.R8_G8_B8_A8_UNorm       },
        { typeof(Rgba64),           PixelFormat.R16_G16_B16_A16_UNorm   },
        { typeof(RgbaVector),       PixelFormat.R32_G32_B32_A32_Float   },
        { typeof(Short2),           PixelFormat.R16_G16_SInt            },
        { typeof(Short4),           PixelFormat.R16_G16_B16_A16_SInt    }
    };

    private static PixelFormat FormatForPixelType<TPixel>() where TPixel : unmanaged, IPixel<TPixel>
    {
        if (formatLookup.TryGetValue(typeof(TPixel), out PixelFormat format))
            return format;

        throw new Exception($"Invalid pixel format: {typeof(TPixel).Name}");
    }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> from an <see cref="Image{Rgba32}"/>.
    /// </summary>
    /// <param name="image">The image to create the <see cref="Texture2D"/> with.</param>
    /// <param name="generateMipmaps">Whether to generate mipmaps for the <see cref="Texture2D"/>.</param>
    /// <typeparam name="TPixel">The pixel format to use.</typeparam>
    public static Texture2D FromImage<TPixel>(Image<TPixel> image, bool generateMipmaps = false) where TPixel : unmanaged, IPixel<TPixel>
    {
        ArgumentNullException.ThrowIfNull(image);

        image.Mutate(x => x.Flip(FlipMode.Vertical));

        PixelFormat format = FormatForPixelType<TPixel>();
        TextureUsage usage = TextureUsage.Sampled;
        uint mipLevels = 0;

        if (generateMipmaps)
        {
            mipLevels = (uint)MathD.ComputeMipLevels(image.Width, image.Height);
            usage |= TextureUsage.GenerateMipmaps;
        }

        Texture2D texture = new Texture2D((uint)image.Width, (uint)image.Height, mipLevels, format, usage);
        texture.Name = "Loaded From File";

        try
        {
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<TPixel> pixelRow = accessor.GetRowSpan(y);
                    texture.SetData(pixelRow, 0, (uint)y, (uint)pixelRow.Length, 1);
                }
            });

            if (generateMipmaps)
                texture.GenerateMipmaps();

            return texture;
        }
        catch
        {
            texture.Destroy();
            throw;
        }
    }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> from a <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">The stream from which to load an image.</param>
    /// <param name="generateMipmaps">Whether to generate mipmaps for the <see cref="Texture2D"/>.</param>
    /// <typeparam name="TPixel">The pixel format to use.</typeparam>
    public static Texture2D FromStream<TPixel>(Stream stream, bool generateMipmaps = false) where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = Image.Load<TPixel>(stream);
        return FromImage(image, generateMipmaps);
    }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> by loading an image from a file.
    /// </summary>
    /// <param name="file">The file containing the image to create the <see cref="Texture2D"/> with.</param>
    /// <param name="generateMipmaps">Whether to generate mipmaps for the <see cref="Texture2D"/>.</param>
    /// <typeparam name="TPixel">The pixel format to use.</typeparam>
    public static Texture2D FromFile<TPixel>(string file, bool generateMipmaps = false) where TPixel : unmanaged, IPixel<TPixel>
    {
        if (Path.GetExtension(file).Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            var texture = SixLabors.ImageSharp.Textures.Texture.Load(file, out _);
            if (texture is SixLabors.ImageSharp.Textures.TextureFormats.FlatTexture flat)
            {
                var i = flat.MipMaps[0].GetImage();
                using Image<TPixel> casted = i.CloneAs<TPixel>();
                return FromImage(casted, generateMipmaps);
            }
        }
        using Image<TPixel> image = Image.Load<TPixel>(file);
        return FromImage(image, generateMipmaps);
    }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> from a <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">The stream from which to load an image.</param>
    /// <param name="generateMipmaps">Whether to generate mipmaps for the <see cref="Texture2D"/>.</param>
    public static Texture2D FromStream(Stream stream, bool generateMipmaps = false)
    {
        return FromStream<Rgba32>(stream, generateMipmaps);
    }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> by loading an image from a file.
    /// </summary>
    /// <param name="file">The file containing the image to create the <see cref="Texture2D"/> with.</param>
    /// <param name="generateMipmaps">Whether to generate mipmaps for the <see cref="Texture2D"/>.</param>
    public static Texture2D FromFile(string file, bool generateMipmaps = false)
    {
        return FromFile<Rgba32>(file, generateMipmaps);
    }


    internal const string ImageNotContiguousError = "To load/save an image, it's backing memory must be contiguous. Consider using smaller image sizes or changing your ImageSharp memory allocation settings to allow larger buffers.";

    internal const string ImageSizeMustMatchTextureSizeError = "The size of the image must match the size of the texture";

    internal const string TextureFormatMustBeColor4bError = "The texture's format must be Color4b (RGBA)";

    #endregion


}
