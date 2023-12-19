using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;

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
        public unsafe void SetDataPtr(void* ptr, int rectX, int rectY, uint rectWidth, uint rectHeight, PixelFormat pixelFormat = 0)
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

        #region SixLabors ImageSharp integration


        /// <summary>
        /// Sets a the data of an area of a <see cref="Texture2D"/> from an <see cref="Image{Rgba32}"/>.
        /// </summary>
        /// <param name="x">The x position of the first pixel to set.</param>
        /// <param name="y">The y position of the first pixel to set.</param>
        /// <param name="image">The image to set the data from. The width and height is taken from here.</param>
        public void SetData(int x, int y, Image<Rgba32> image)
        {
            if (ImageFormat != TextureImageFormat.Color4b)
                throw new ArgumentException("Texture2D", TextureFormatMustBeColor4bError);

            if (image == null)
                throw new ArgumentNullException(nameof(image));

            if (!image.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> pixels))
                throw new InvalidDataException(ImageNotContiguousError);

            SetData<Rgba32>(pixels, x, y, (uint)image.Width, (uint)image.Height, PixelFormat.Rgba);
        }

        /// <summary>
        /// Sets a the data of an entire <see cref="Texture2D"/> from an <see cref="Image{Rgba32}"/>.
        /// </summary>
        /// <param name="image">The image to set the data from.</param>
        public void SetData(Image<Rgba32> image)
        {
            SetData(0, 0, image);
        }

        /// <summary>
        /// Gets the data of the entire <see cref="Texture2D"/>.
        /// </summary>
        /// <param name="image">The image in which to write the pixel data.</param>
        /// <param name="flip">Whether to flip the image after the pixels are read.</param>
        public void GetData(Image<Rgba32> image, bool flip = false)
        {
            if (ImageFormat != TextureImageFormat.Color4b)
                throw new ArgumentException("Texture2D", TextureFormatMustBeColor4bError);

            if (image == null)
                throw new ArgumentNullException(nameof(image));

            if (image.Width != Width || image.Height != Height)
                throw new ArgumentException(nameof(image), ImageSizeMustMatchTextureSizeError);

            if (!image.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> pixels))
                throw new InvalidDataException(ImageNotContiguousError);
            GetData(pixels, PixelFormat.Rgba);

            if (flip)
                image.Mutate(x => x.Flip(FlipMode.Vertical));
        }

        /// <summary>
        /// Creates a <see cref="Texture2D"/> from an <see cref="Image{Rgba32}"/>.
        /// </summary>
        /// <param name="image">The image to create the <see cref="Texture2D"/> with.</param>
        /// <param name="generateMipmaps">Whether to generate mipmaps for the <see cref="Texture2D"/>.</param>
        public static Texture2D FromImage(Image<Rgba32> image, bool generateMipmaps = false)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            if (!image.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> pixels))
                throw new InvalidDataException(ImageNotContiguousError);

            Texture2D texture = new Texture2D((uint)image.Width, (uint)image.Height);
            try
            {
                texture.SetData<Rgba32>(pixels, PixelFormat.Rgba);

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
            using Image<Rgba32> image = Image.Load<Rgba32>(stream);
            return FromImage(image, generateMipmaps);
        }

        /// <summary>
        /// Creates a <see cref="Texture2D"/> by loading an image from a file.
        /// </summary>
        /// <param name="file">The file containing the image to create the <see cref="Texture2D"/> with.</param>
        /// <param name="generateMipmaps">Whether to generate mipmaps for the <see cref="Texture2D"/>.</param>
        public static Texture2D FromFile(string file, bool generateMipmaps = false)
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(file);
            return FromImage( image, generateMipmaps);
        }

        public enum SaveImageFormat
        {
            Png, Jpeg, Bmp, Gif
        }

        /// <summary>
        /// Saves this <see cref="Texture2D"/>'s image to a stream. You can't save multisampled textures.
        /// </summary>
        /// <param name="stream">The stream to save the texture image to.</param>
        /// <param name="imageFormat">The format the image will be saved as.</param>
        /// <param name="flip">Whether to flip the image after the pixels are read.</param>
        public void SaveAsImage(Stream stream, SaveImageFormat imageFormat, bool flip = false)
        {
            if (stream == null)
                throw new ArgumentException("You must specify a stream", nameof(stream));

            IImageFormat format = GetFormatFor(imageFormat);
            using Image<Rgba32> image = new Image<Rgba32>((int)Width, (int)Height);
            GetData(image, flip);
            image.Save(stream, format);
        }

        /// <summary>
        /// Saves this <see cref="Texture2D"/>'s image to a file. You can't save multisampled textures.
        /// If the file already exists, it will be replaced.
        /// </summary>
        /// <param name="file">The name of the file where the image will be saved.</param>
        /// <param name="imageFormat">The format the image will be saved as.</param>
        /// <param name="flip">Whether to flip the image after the pixels are read.</param>
        public void SaveAsImage(string file, SaveImageFormat imageFormat, bool flip = false)
        {
            using FileStream fileStream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read);
            SaveAsImage(fileStream, imageFormat, flip);
        }

        internal const string ImageNotContiguousError = "To load/save an image, it's backing memory must be contiguous. Consider using smaller image sizes or changing your ImageSharp memory allocation settings to allow larger buffers.";

        internal const string ImageSizeMustMatchTextureSizeError = "The size of the image must match the size of the texture";

        internal const string TextureFormatMustBeColor4bError = "The texture's format must be Color4b (RGBA)";

        /// <summary>
        /// Gets an appropiate <see cref="IImageFormat"/> for the given <see cref="SaveImageFormat"/>.
        /// </summary>
        public static IImageFormat GetFormatFor(SaveImageFormat imageFormat)
        {
            return imageFormat switch
            {
                SaveImageFormat.Png => SixLabors.ImageSharp.Formats.Png.PngFormat.Instance,
                SaveImageFormat.Jpeg => SixLabors.ImageSharp.Formats.Jpeg.JpegFormat.Instance,
                SaveImageFormat.Bmp => SixLabors.ImageSharp.Formats.Bmp.BmpFormat.Instance,
                SaveImageFormat.Gif => SixLabors.ImageSharp.Formats.Gif.GifFormat.Instance,
                _ => throw new ArgumentException("Invalid " + nameof(SaveImageFormat), nameof(imageFormat)),
            };
        }

        #endregion


        public CompoundTag Serialize(TagSerializer.SerializationContext ctx)
        {
            CompoundTag compoundTag = new CompoundTag();
            compoundTag.Add("Width", new IntTag((int)Width));
            compoundTag.Add("Height", new IntTag((int)Height));
            compoundTag.Add("IsMipMapped", new BoolTag(IsMipmapped));
            compoundTag.Add("ImageFormat", new ByteTag((byte)ImageFormat));
            Memory<byte> memory = new byte[Width * Height * 4];
            GetData(memory);
            compoundTag.Add("Data", new ByteArrayTag(memory.ToArray()));

            return compoundTag;
        }

        public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
        {
            Width = (uint)value["Width"].IntValue;
            Height = (uint)value["Height"].IntValue;
            bool isMipMapped = value["IsMipMapped"].BoolValue;
            TextureImageFormat imageFormat = (TextureImageFormat)value["ImageFormat"].ByteValue;

            var param = new[] { typeof(uint), typeof(uint), typeof(bool), typeof(TextureImageFormat) };
            var values = new object[] { Width, Height, isMipMapped, imageFormat };
            typeof(Texture2D).GetConstructor(param).Invoke(this, values);

            Memory<byte> memory = value["Data"].ByteArrayValue;
            SetData(memory);
        }
    }
}
