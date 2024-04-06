using System;
using Prowl.Runtime.Rendering;
using Silk.NET.OpenGL;

namespace Prowl.Runtime
{
    public sealed class TextureCubemap : Texture
    {
        public enum CubemapFace
        {
            PositiveX = 34069,
            NegativeX = 34070,
            PositiveY = 34071,
            NegativeY = 34072,
            PositiveZ = 34073,
            NegativeZ = 34074
        }

        /// <summary>The size of a face from this <see cref="TextureCubemap"/>.</summary>
        public uint Size { get; private set; }

        /// <summary>
        /// Creates a <see cref="TextureCubemap"/> with the desired parameters but no image data.
        /// </summary>
        /// <param name="graphicsDevice">The <see cref="GraphicsDevice"/> this resource will use.</param>
        /// <param name="size">The size (width and height) of the cubemap's faces.</param>
        /// <param name="imageFormat">The image format for this <see cref="TextureCubemap"/>.</param>
        public TextureCubemap(uint size, TextureImageFormat imageFormat = TextureImageFormat.Color4b)
            : base(TextureType.TextureCubeMap, imageFormat)
        {
            if (size <= 0 || size > Graphics.MaxCubeMapTextureSize)
                throw new ArgumentOutOfRangeException(nameof(size), size, "Cubemap size must be in the range (0, " + Graphics.MaxCubeMapTextureSize + "]");

            Size = size;
            Graphics.Device.BindTexture((TextureTarget)Type, Handle);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureMinFilter, (int)DefaultMinFilter);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureMagFilter, (int)DefaultMagFilter);

            unsafe
            {
                Graphics.Device.TexImage2D(TextureTarget.TextureCubeMapPositiveX, 0, (int)PixelInternalFormat, size, size, 0, PixelFormat, PixelType, (void*)0);
                Graphics.Device.TexImage2D(TextureTarget.TextureCubeMapNegativeX, 0, (int)PixelInternalFormat, size, size, 0, PixelFormat, PixelType, (void*)0);
                Graphics.Device.TexImage2D(TextureTarget.TextureCubeMapPositiveY, 0, (int)PixelInternalFormat, size, size, 0, PixelFormat, PixelType, (void*)0);
                Graphics.Device.TexImage2D(TextureTarget.TextureCubeMapNegativeY, 0, (int)PixelInternalFormat, size, size, 0, PixelFormat, PixelType, (void*)0);
                Graphics.Device.TexImage2D(TextureTarget.TextureCubeMapPositiveZ, 0, (int)PixelInternalFormat, size, size, 0, PixelFormat, PixelType, (void*)0);
                Graphics.Device.TexImage2D(TextureTarget.TextureCubeMapNegativeZ, 0, (int)PixelInternalFormat, size, size, 0, PixelFormat, PixelType, (void*)0);
            }
        }

        /// <summary>
        /// Sets the data of an area of a face of the <see cref="TextureCubemap"/>.
        /// </summary>
        /// <param name="face">The face of the <see cref="TextureCubemap"/> to set data for.</param>
        /// <param name="ptr">The pointer from which the pixel data will be read.</param>
        /// <param name="rectX">The X coordinate of the first pixel to write.</param>
        /// <param name="rectY">The Y coordinate of the first pixel to write.</param>
        /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
        /// <param name="rectHeight">The height of the rectangle of pixels to write.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public unsafe void SetDataPtr(CubemapFace face, void* ptr, int rectX, int rectY, uint rectWidth, uint rectHeight, PixelFormat pixelFormat = 0)
        {
            ValidateCubemapFace(face);
            ValidateRectOperation(rectX, rectY, rectWidth, rectHeight);

            Graphics.Device.BindTexture((TextureTarget)Type, Handle);
            Graphics.Device.TexSubImage2D((TextureTarget)face, 0, rectX, rectY, rectWidth, rectHeight, pixelFormat == 0 ? PixelFormat : pixelFormat, PixelType, ptr);
        }

        /// <summary>
        /// Sets the data of an area of a face of the <see cref="TextureCubemap"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="TextureCubemap"/>'s pixels.</typeparam>
        /// <param name="face">The face of the <see cref="TextureCubemap"/> to set data for.</param>
        /// <param name="data">A <see cref="ReadOnlySpan{T}"/> containing the new pixel data.</param>
        /// <param name="rectX">The X coordinate of the first pixel to write.</param>
        /// <param name="rectY">The Y coordinate of the first pixel to write.</param>
        /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
        /// <param name="rectHeight">The height of the rectangle of pixels to write.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public unsafe void SetData<T>(CubemapFace face, ReadOnlySpan<T> data, int rectX, int rectY, uint rectWidth, uint rectHeight, PixelFormat pixelFormat = 0) where T : unmanaged
        {
            ValidateCubemapFace(face);
            ValidateRectOperation(rectX, rectY, rectWidth, rectHeight);
            if (data.Length < rectWidth * rectHeight)
                throw new ArgumentException("Not enough pixel data", nameof(data));

            Graphics.Device.BindTexture((TextureTarget)Type, Handle);
            fixed (void* ptr = data)
                Graphics.Device.TexSubImage2D((TextureTarget)face, 0, rectX, rectY, rectWidth, rectHeight, pixelFormat == 0 ? PixelFormat : pixelFormat, PixelType, ptr);
        }

        /// <summary>
        /// Sets the data of a face of the <see cref="TextureCubemap"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="TextureCubemap"/>'s pixels.</typeparam>
        /// <param name="face">The face of the <see cref="TextureCubemap"/> to set data for.</param>
        /// <param name="data">The <see cref="ReadOnlySpan{T}"/> containing the new pixel data.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public void SetData<T>(CubemapFace face, ReadOnlySpan<T> data, PixelFormat pixelFormat = 0) where T : unmanaged
        {
            SetData(face, data, 0, 0, Size, Size, pixelFormat);
        }

        /// <summary>
        /// Gets the data of an entire face of the <see cref="TextureCubemap"/>.
        /// </summary>
        /// <param name="face">The face of the cubemap to set data for.</param>
        /// <param name="ptr">The pointer to which the pixel data will be written.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public unsafe void GetDataPtr(CubemapFace face, void* ptr, PixelFormat pixelFormat = 0)
        {
            ValidateCubemapFace(face);
            Graphics.Device.BindTexture((TextureTarget)Type, Handle);
            Graphics.Device.GetTexImage((TextureTarget)face, 0, pixelFormat == 0 ? PixelFormat : pixelFormat, PixelType, ptr);
        }

        /// <summary>
        /// Gets the data of an entire face of the <see cref="TextureCubemap"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="TextureCubemap"/>'s pixels.</typeparam>
        /// <param name="face">The face of the <see cref="TextureCubemap"/> to set data for.</param>
        /// <param name="data">The array in which to write the texture data.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public unsafe void GetData<T>(CubemapFace face, Span<T> data, PixelFormat pixelFormat = 0) where T : unmanaged
        {
            ValidateCubemapFace(face);
            if (data.Length < Size * Size)
                throw new ArgumentException("Insufficient space to store the requested pixel data", nameof(data));

            Graphics.Device.BindTexture((TextureTarget)Type, Handle);
            fixed (void* ptr = data)
                Graphics.Device.GetTexImage((TextureTarget)face, 0, pixelFormat == 0 ? PixelFormat : pixelFormat, PixelType, ptr);
        }

        /// <summary>
        /// Sets the texture coordinate wrapping modes for when a texture is sampled outside the [0, 1] range.
        /// </summary>
        /// <param name="sWrapMode">The wrap mode for the S (or texture-X) coordinate.</param>
        /// <param name="tWrapMode">The wrap mode for the T (or texture-Y) coordinate.</param>
        /// <param name="rWrapMode">The wrap mode for the R (or texture-Z) coordinate.</param>
        public void SetWrapModes(TextureWrapMode sWrapMode, TextureWrapMode tWrapMode, TextureWrapMode rWrapMode)
        {
            Graphics.Device.BindTexture((TextureTarget)Type, Handle);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureWrapS, (int)sWrapMode);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureWrapT, (int)tWrapMode);
            Graphics.Device.TexParameter((TextureTarget)Type, TextureParameterName.TextureWrapR, (int)rWrapMode);
        }

        private void ValidateRectOperation(int rectX, int rectY, uint rectWidth, uint rectHeight)
        {
            if (rectX < 0 || rectY >= Size)
                throw new ArgumentOutOfRangeException(nameof(rectX), rectX, nameof(rectX) + " must be in the range [0, " + nameof(Size) + ")");

            if (rectY < 0 || rectY >= Size)
                throw new ArgumentOutOfRangeException(nameof(rectY), rectY, nameof(rectY) + " must be in the range [0, " + nameof(Size) + ")");

            if (rectWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(rectWidth), rectWidth, nameof(rectWidth) + " must be greater than 0");

            if (rectHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(rectHeight), rectHeight, nameof(rectHeight) + "must be greater than 0");

            if (rectWidth > Size - rectX || rectHeight > Size - rectY)
                throw new ArgumentOutOfRangeException("Specified area is outside of the texture's storage");
        }

        private static void ValidateCubemapFace(CubemapFace face)
        {
            if (face < CubemapFace.PositiveX || face > CubemapFace.NegativeZ)
                throw new ArgumentException("Invalid " + nameof(CubemapFace) + " value", nameof(face));
        }
    }
}
