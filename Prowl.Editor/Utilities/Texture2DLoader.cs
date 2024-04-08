using ImageMagick;
using Prowl.Runtime;
using Silk.NET.OpenGL;
using static Prowl.Runtime.Texture;

namespace Prowl.Editor
{
    public static class Texture2DLoader
    {
        #region ImageMagick integration

        /// <summary>
        /// Creates a <see cref="Texture2D"/> from an <see cref="Image{Rgba32}"/>.
        /// </summary>
        /// <param name="image">The image to create the <see cref="Texture2D"/> with.</param>
        /// <param name="generateMipmaps">Whether to generate mipmaps for the <see cref="Texture2D"/>.</param>
        public static Texture2D FromImage(MagickImage image, bool generateMipmaps = false)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            image.Flip();

            TextureImageFormat format = TextureImageFormat.Color4b;
            image.ColorType = ColorType.TrueColorAlpha;

            var pixels = image.GetPixelsUnsafe().GetAreaPointer(0, 0, image.Width, image.Height);

            Texture2D texture = new Texture2D((uint)image.Width, (uint)image.Height, false, format);
            try
            {

                unsafe
                {
                    Graphics.Device.TexSubImage2D(texture.Handle, 0, 0, 0, (uint)image.Width, (uint)image.Height, texture.PixelFormat, PixelType.UnsignedShort, (void*)pixels);
                }

                if (generateMipmaps)
                    texture.GenerateMipmaps();

                return texture;
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a <see cref="Texture2D"/> from a <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The stream from which to load an image.</param>
        /// <param name="generateMipmaps">Whether to generate mipmaps for the <see cref="Texture2D"/>.</param>
        public static Texture2D FromStream(Stream stream, bool generateMipmaps = false)
        {
            var image = new MagickImage(stream);
            return FromImage(image, generateMipmaps);
        }

        /// <summary>
        /// Creates a <see cref="Texture2D"/> by loading an image from a file.
        /// </summary>
        /// <param name="file">The file containing the image to create the <see cref="Texture2D"/> with.</param>
        /// <param name="generateMipmaps">Whether to generate mipmaps for the <see cref="Texture2D"/>.</param>
        public static Texture2D FromFile(string file, bool generateMipmaps = false)
        {
            var image = new MagickImage(file);
            return FromImage(image, generateMipmaps);
        }


        internal const string ImageNotContiguousError = "To load/save an image, it's backing memory must be contiguous. Consider using smaller image sizes or changing your ImageSharp memory allocation settings to allow larger buffers.";

        internal const string ImageSizeMustMatchTextureSizeError = "The size of the image must match the size of the texture";

        internal const string TextureFormatMustBeColor4bError = "The texture's format must be Color4b (RGBA)";

        #endregion


    }
}
