using Silk.NET.OpenGL;
using ImageMagick;
using System;
using System.IO;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Silk.NET.Vulkan;

namespace Prowl.Runtime
{
    /// <summary>
    /// A <see cref="Texture"/> whose image has two dimensions and support for multisampling.
    /// </summary>
    public sealed class Texture2D : Texture, ISerializable
    {
        /// <summary>The width of this <see cref="Texture2D"/>.</summary>
        public uint Width { get; private set; }

        /// <summary>The height of this <see cref="Texture2D"/>.</summary>
        public uint Height { get; private set; }

        public Texture2D() : base(TextureType.Texture2D, TextureImageFormat.Color4b) { }

        /// <summary>
        /// Creates a <see cref="Texture2D"/> with the desired parameters but no image data.
        /// </summary>
        /// <param name="width">The width of the <see cref="Texture2D"/>.</param>
        /// <param name="height">The height of the <see cref="Texture2D"/>.</param>
        /// <param name="generateMipmaps">Whether to generate mipmaps for this <see cref="Texture2D"/>.</param>
        /// <param name="imageFormat">The image format for this <see cref="Texture2D"/>.</param>
        public Texture2D(uint width, uint height, bool generateMipmaps = false, TextureImageFormat imageFormat = TextureImageFormat.Color4b)
            : base(TextureType.Texture2D, imageFormat)
        {
            RecreateImage(width, height); //This also binds the texture

            if (generateMipmaps)
                GenerateMipmaps();

            Graphics.GL.TexParameter((TextureTarget)Type, TextureParameterName.TextureMinFilter, IsMipmapped ? (int)DefaultMipmapMinFilter : (int)DefaultMinFilter);
            Graphics.GL.TexParameter((TextureTarget)Type, TextureParameterName.TextureMagFilter, (int)DefaultMagFilter);
            MinFilter = IsMipmapped ? DefaultMipmapMinFilter : DefaultMinFilter;
            MagFilter = DefaultMagFilter;
            Graphics.CheckGL();
        }

        /// <summary>
        /// Sets the data of an area of the <see cref="Texture2D"/>.
        /// </summary>
        /// <param name="ptr">The pointer from which the pixel data will be read.</param>
        /// <param name="rectX">The X coordinate of the first pixel to write.</param>
        /// <param name="rectY">The Y coordinate of the first pixel to write.</param>
        /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
        /// <param name="rectHeight">The height of the rectangle of pixels to write.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public unsafe void SetDataPtr(void* ptr, int rectX, int rectY, uint rectWidth, uint rectHeight, Silk.NET.OpenGL.PixelFormat pixelFormat = 0)
        {
            ValidateRectOperation(rectX, rectY, rectWidth, rectHeight);

            Graphics.GL.BindTexture((TextureTarget)Type, Handle);
            Graphics.GL.TexSubImage2D((TextureTarget)Type, 0, rectX, rectY, rectWidth, rectHeight, pixelFormat == 0 ? PixelFormat : pixelFormat, PixelType, ptr);
            Graphics.CheckGL();
        }

        /// <summary>
        /// Sets the data of an area of the <see cref="Texture2D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="Memory{T}"/> containing the new pixel data.</param>
        /// <param name="rectX">The X coordinate of the first pixel to write.</param>
        /// <param name="rectY">The Y coordinate of the first pixel to write.</param>
        /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
        /// <param name="rectHeight">The height of the rectangle of pixels to write.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public unsafe void SetData<T>(Memory<T> data, int rectX, int rectY, uint rectWidth, uint rectHeight, PixelFormat pixelFormat = 0) where T : unmanaged
        {
            ValidateRectOperation(rectX, rectY, rectWidth, rectHeight);
            if (data.Length < rectWidth * rectHeight)
                throw new ArgumentException("Not enough pixel data", nameof(data));

            Graphics.GL.BindTexture((TextureTarget)Type, Handle);
            fixed (void* ptr = data.Span)
                Graphics.GL.TexSubImage2D((TextureTarget)Type, 0, rectX, rectY, rectWidth, rectHeight, pixelFormat == 0 ? PixelFormat : pixelFormat, PixelType, ptr);
            Graphics.CheckGL();
        }

        /// <summary>
        /// Sets the data of the entire <see cref="Texture2D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="ReadOnlySpan{T}"/> containing the new pixel data.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public void SetData<T>(Memory<T> data, PixelFormat pixelFormat = 0) where T : unmanaged
        {
            SetData(data, 0, 0, Width, Height, pixelFormat);
        }

        /// <summary>
        /// Gets the data of the entire <see cref="Texture2D"/>.
        /// </summary>
        /// <param name="ptr">The pointer to which the pixel data will be written.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public unsafe void GetDataPtr(void* ptr, PixelFormat pixelFormat = 0)
        {
            Graphics.GL.BindTexture((TextureTarget)Type, Handle);
            Graphics.GL.GetTexImage((TextureTarget)Type, 0, pixelFormat == 0 ? PixelFormat : pixelFormat, PixelType, ptr);
            Graphics.CheckGL();
        }

        /// <summary>
        /// Gets the data of the entire <see cref="Texture2D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="Span{T}"/> in which to write the pixel data.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public unsafe void GetData<T>(Memory<T> data, PixelFormat pixelFormat = 0) where T : unmanaged
        {
            if (data.Length < Width * Height)
                throw new ArgumentException("Insufficient space to store the requested pixel data", nameof(data));

            Graphics.GL.BindTexture((TextureTarget)Type, Handle);
            fixed (void* ptr = data.Span)
                Graphics.GL.GetTexImage((TextureTarget)Type, 0, pixelFormat == 0 ? PixelFormat : pixelFormat, PixelType, ptr);
            Graphics.CheckGL();
        }

        public int GetSize()
        {
            int size = (int)Width * (int)Height;
            switch (ImageFormat) {
                case TextureImageFormat.UnsignedInt:
                case TextureImageFormat.Int:
                case TextureImageFormat.Float:
                    return size * 4;
                case TextureImageFormat.UnsignedInt2:
                case TextureImageFormat.Int2:
                case TextureImageFormat.Float2:
                    return size * 4 * 2;
                case TextureImageFormat.UnsignedInt3:
                case TextureImageFormat.Int3:
                case TextureImageFormat.Float3:
                    return size * 4 * 3;
                case TextureImageFormat.UnsignedInt4:
                case TextureImageFormat.Int4:
                case TextureImageFormat.Float4:
                    return size * 4 * 4;
                case TextureImageFormat.Depth16:
                    return size * 2;
                case TextureImageFormat.Depth24:
                    return size * 3;
                case TextureImageFormat.Depth32f:
                    return size * 4;
                default: return size * 4;
            }
        }

        /// <summary>
        /// Sets the texture coordinate wrapping modes for when a texture is sampled outside the [0, 1] range.
        /// </summary>
        /// <param name="sWrapMode">The wrap mode for the S (or texture-X) coordinate.</param>
        /// <param name="tWrapMode">The wrap mode for the T (or texture-Y) coordinate.</param>
        public void SetWrapModes(TextureWrapMode sWrapMode, TextureWrapMode tWrapMode)
        {
            Graphics.GL.BindTexture((TextureTarget)Type, Handle);
            Graphics.GL.TexParameter((TextureTarget)Type, TextureParameterName.TextureWrapS, (int)sWrapMode);
            Graphics.GL.TexParameter((TextureTarget)Type, TextureParameterName.TextureWrapT, (int)tWrapMode);
            Graphics.CheckGL();
        }

        /// <summary>
        /// Recreates this <see cref="Texture2D"/>'s image with a new size,
        /// resizing the <see cref="Texture2D"/> but losing the image data.
        /// </summary>
        /// <param name="width">The new width for the <see cref="Texture2D"/>.</param>
        /// <param name="height">The new height for the <see cref="Texture2D"/>.</param>
        public unsafe void RecreateImage(uint width, uint height)
        {
            ValidateTextureSize(width, height);

            Width = width;
            Height = height;

            Graphics.GL.BindTexture((TextureTarget)Type, Handle);
            Graphics.GL.TexImage2D((TextureTarget)Type, 0, (int)PixelInternalFormat, Width, Height, 0, PixelFormat, PixelType, (void*)0);
            Graphics.CheckGL();
        }

        private void ValidateTextureSize(uint width, uint height)
        {
            if (width <= 0 || width > Graphics.MaxTextureSize)
                throw new ArgumentOutOfRangeException(nameof(width), width, nameof(width) + " must be in the range (0, " + nameof(Graphics.MaxTextureSize) + "]");

            if (height <= 0 || height > Graphics.MaxTextureSize)
                throw new ArgumentOutOfRangeException(nameof(height), height, nameof(height) + " must be in the range (0, " + nameof(Graphics.MaxTextureSize) + "]");
        }

        private void ValidateRectOperation(int rectX, int rectY, uint rectWidth, uint rectHeight)
        {
            if (rectX < 0 || rectY >= Height)
                throw new ArgumentOutOfRangeException(nameof(rectX), rectX, nameof(rectX) + " must be in the range [0, " + nameof(Width) + ")");

            if (rectY < 0 || rectY >= Height)
                throw new ArgumentOutOfRangeException(nameof(rectY), rectY, nameof(rectY) + " must be in the range [0, " + nameof(Height) + ")");

            if (rectWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(rectWidth), rectWidth, nameof(rectWidth) + " must be greater than 0");

            if (rectHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(rectHeight), rectHeight, nameof(rectHeight) + "must be greater than 0");

            if (rectWidth > Width - rectX || rectHeight > Height - rectY)
                throw new ArgumentOutOfRangeException("Specified area is outside of the texture's storage");
        }

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
            try {

                Graphics.GL.BindTexture((TextureTarget)texture.Type, texture.Handle);
                unsafe {
                    Graphics.GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                    Graphics.GL.TexSubImage2D((TextureTarget)texture.Type, 0, 0, 0, (uint)image.Width, (uint)image.Height, texture.PixelFormat, PixelType.UnsignedShort, (void*)pixels);
                }
                Graphics.CheckGL();

                if (generateMipmaps)
                    texture.GenerateMipmaps();

                return texture;
            } catch {
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
            return FromImage( image, generateMipmaps);
        }


        internal const string ImageNotContiguousError = "To load/save an image, it's backing memory must be contiguous. Consider using smaller image sizes or changing your ImageSharp memory allocation settings to allow larger buffers.";

        internal const string ImageSizeMustMatchTextureSizeError = "The size of the image must match the size of the texture";

        internal const string TextureFormatMustBeColor4bError = "The texture's format must be Color4b (RGBA)";

        #endregion


        public CompoundTag Serialize(TagSerializer.SerializationContext ctx)
        {
            CompoundTag compoundTag = new CompoundTag();
            compoundTag.Add("Width", new IntTag((int)Width));
            compoundTag.Add("Height", new IntTag((int)Height));
            compoundTag.Add("IsMipMapped", new BoolTag(IsMipmapped));
            compoundTag.Add("ImageFormat", new IntTag((int)ImageFormat));
            compoundTag.Add("PixelFormat", new IntTag((int)PixelFormat));
            compoundTag.Add("MinFilter", new IntTag((int)MinFilter));
            compoundTag.Add("MagFilter", new IntTag((int)MagFilter));
            compoundTag.Add("Wrap", new IntTag((int)WrapMode));
            Memory<byte> memory = new byte[GetSize()];
            GetData(memory, PixelFormat);
            compoundTag.Add("Data", new ByteArrayTag(memory.ToArray()));

            return compoundTag;
        }

        public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
        {
            Width = (uint)value["Width"].IntValue;
            Height = (uint)value["Height"].IntValue;
            bool isMipMapped = value["IsMipMapped"].BoolValue;
            TextureImageFormat imageFormat = (TextureImageFormat)value["ImageFormat"].IntValue;
            var PixelFormat = (Silk.NET.OpenGL.PixelFormat)value["PixelFormat"].IntValue;
            var MinFilter = (TextureMinFilter)value["MinFilter"].IntValue;
            var MagFilter = (TextureMagFilter)value["MagFilter"].IntValue;
            var Wrap = (TextureWrapMode)value["Wrap"].IntValue;

            var param = new[] { typeof(uint), typeof(uint), typeof(bool), typeof(TextureImageFormat) };
            var values = new object[] { Width, Height, false, imageFormat };
            typeof(Texture2D).GetConstructor(param).Invoke(this, values);

            Memory<byte> memory = value["Data"].ByteArrayValue;
            SetData(memory, PixelFormat);

            if(isMipMapped)
                GenerateMipmaps();

            SetTextureFilters(MinFilter, MagFilter);
            SetWrapModes(Wrap, Wrap);
        }
    }
}
