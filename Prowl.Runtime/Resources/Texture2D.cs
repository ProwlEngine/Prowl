using Prowl.Runtime.GraphicsBackend.Primitives;
using ImageMagick;
using Prowl.Echo;
using System;
using System.IO;

namespace Prowl.Runtime.Resources
{

    /// <summary>
    /// A <see cref="Texture"/> whose image has two dimensions and support for multisampling.
    /// </summary>
    public sealed class Texture2D : Texture, ISerializable
    {
        public static Texture2D White => Texture2D.LoadDefault(DefaultTexture.White);
        public static Texture2D Gray => Texture2D.LoadDefault(DefaultTexture.Gray18);
        public static Texture2D Normal => Texture2D.LoadDefault(DefaultTexture.Normal);
        public static Texture2D Emission => Texture2D.LoadDefault(DefaultTexture.Emission);
        public static Texture2D Surface => Texture2D.LoadDefault(DefaultTexture.Surface);
        public static Texture2D Grid => Texture2D.LoadDefault(DefaultTexture.Grid);
        public static Texture2D Noise => Texture2D.LoadDefault(DefaultTexture.Noise);

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

            Graphics.Device.SetTextureFilters(Handle, IsMipmapped ? DefaultMipmapMinFilter : DefaultMinFilter, DefaultMagFilter);
            MinFilter = IsMipmapped ? DefaultMipmapMinFilter : DefaultMinFilter;
            MagFilter = DefaultMagFilter;
        }

        /// <summary>
        /// Sets the data of an area of the <see cref="Texture2D"/>.
        /// </summary>
        /// <param name="ptr">The pointer from which the pixel data will be read.</param>
        /// <param name="rectX">The X coordinate of the first pixel to write.</param>
        /// <param name="rectY">The Y coordinate of the first pixel to write.</param>
        /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
        /// <param name="rectHeight">The height of the rectangle of pixels to write.</param>
        public unsafe void SetDataPtr(void* ptr, int rectX, int rectY, uint rectWidth, uint rectHeight)
        {
            ValidateRectOperation(rectX, rectY, rectWidth, rectHeight);

            Graphics.Device.TexSubImage2D(Handle, 0, rectX, rectY, rectWidth, rectHeight, ptr);
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
        public unsafe void SetData<T>(Memory<T> data, int rectX, int rectY, uint rectWidth, uint rectHeight) where T : unmanaged
        {
            ValidateRectOperation(rectX, rectY, rectWidth, rectHeight);
            if (data.Length < rectWidth * rectHeight)
                throw new ArgumentException("Not enough pixel data", nameof(data));

            fixed (void* ptr = data.Span)
                Graphics.Device.TexSubImage2D(Handle, 0, rectX, rectY, rectWidth, rectHeight, ptr);
        }

        /// <summary>
        /// Sets the data of the entire <see cref="Texture2D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="ReadOnlySpan{T}"/> containing the new pixel data.</param>
        /// <param name="pixelFormat">The pixel format the data will be read as. 0 for this texture's default.</param>
        public void SetData<T>(Memory<T> data) where T : unmanaged
        {
            SetData(data, 0, 0, Width, Height);
        }

        /// <summary>
        /// Gets the data of the entire <see cref="Texture2D"/>.
        /// </summary>
        /// <param name="ptr">The pointer to which the pixel data will be written.</param>
        public unsafe void GetDataPtr(void* ptr)
        {
            Graphics.Device.GetTexImage(Handle, 0, ptr);
        }

        /// <summary>
        /// Gets the data of the entire <see cref="Texture2D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="Span{T}"/> in which to write the pixel data.</param>
        public unsafe void GetData<T>(Memory<T> data) where T : unmanaged
        {
            if (data.Length < Width * Height)
                throw new ArgumentException("Insufficient space to store the requested pixel data", nameof(data));

            fixed (void* ptr = data.Span)
                Graphics.Device.GetTexImage(Handle, 0, ptr);
        }

        public int GetSize()
        {
            int size = (int)Width * (int)Height;
            switch (ImageFormat)
            {
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

                case TextureImageFormat.Short:
                case TextureImageFormat.UnsignedShort:
                    return size * 1 * 2;
                case TextureImageFormat.Short2:
                case TextureImageFormat.UnsignedShort2:
                    return size * 2 * 2;
                case TextureImageFormat.Short3:
                case TextureImageFormat.UnsignedShort3:
                    return size * 3 * 2;
                case TextureImageFormat.Short4:
                case TextureImageFormat.UnsignedShort4:
                    return size * 4 * 2;

                default: return size * 4;
            }
        }

        /// <summary>
        /// Sets the texture coordinate wrapping modes for when a texture is sampled outside the [0, 1] range.
        /// </summary>
        /// <param name="sWrapMode">The wrap mode for the S (or texture-X) coordinate.</param>
        /// <param name="tWrapMode">The wrap mode for the T (or texture-Y) coordinate.</param>
        public void SetWrapModes(TextureWrap sWrapMode, TextureWrap tWrapMode)
        {
            Graphics.Device.SetWrapS(Handle, sWrapMode);
            Graphics.Device.SetWrapT(Handle, tWrapMode);
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

            Graphics.Device.TexImage2D(Handle, 0, Width, Height, 0, (void*)0);
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

        public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
        {
            compoundTag.Add("Width", new(Width));
            compoundTag.Add("Height", new(Height));
            compoundTag.Add("IsMipMapped", new(IsMipmapped));
            compoundTag.Add("ImageFormat", new((int)ImageFormat));
            compoundTag.Add("MinFilter", new((int)MinFilter));
            compoundTag.Add("MagFilter", new((int)MagFilter));
            compoundTag.Add("Wrap", new((int)WrapMode));
            Memory<byte> memory = new byte[GetSize()];
            GetData(memory);
            compoundTag.Add("Data", new(memory.ToArray()));
        }

        public void Deserialize(EchoObject value, SerializationContext ctx)
        {
            Width = value["Width"].UIntValue;
            Height = value["Height"].UIntValue;
            bool isMipMapped = value["IsMipMapped"].BoolValue;
            TextureImageFormat imageFormat = (TextureImageFormat)value["ImageFormat"].IntValue;
            var MinFilter = (TextureMin)value["MinFilter"].IntValue;
            var MagFilter = (TextureMag)value["MagFilter"].IntValue;
            var Wrap = (TextureWrap)value["Wrap"].IntValue;

            var param = new[] { typeof(uint), typeof(uint), typeof(bool), typeof(TextureImageFormat) };
            var values = new object[] { Width, Height, false, imageFormat };
            typeof(Texture2D).GetConstructor(param).Invoke(this, values);

            Memory<byte> memory = value["Data"].ByteArrayValue;
            SetData(memory);

            if (isMipMapped)
                GenerateMipmaps();

            SetTextureFilters(MinFilter, MagFilter);
            SetWrapModes(Wrap, Wrap);
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

            TextureImageFormat format = TextureImageFormat.UnsignedShort4;
            image.ColorSpace = ColorSpace.sRGB;
            image.ColorType = ColorType.TrueColorAlpha;

            var pixels = image.GetPixelsUnsafe().GetAreaPointer(0, 0, image.Width, image.Height);

            Texture2D texture = new Texture2D((uint)image.Width, (uint)image.Height, false, format);
            try
            {

                unsafe
                {
                    Graphics.Device.TexSubImage2D(texture.Handle, 0, 0, 0, (uint)image.Width, (uint)image.Height, (void*)pixels);
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

        /// <summary>
        /// Loads a texture from a file path (alias for FromFile for consistency)
        /// </summary>
        public static Texture2D LoadFromFile(string filePath, bool generateMipmaps = false)
        {
            var texture = FromFile(filePath, generateMipmaps);
            texture.AssetPath = filePath;
            return texture;
        }

        /// <summary>
        /// Loads a texture from a stream (alias for FromStream for consistency)
        /// </summary>
        public static Texture2D LoadFromStream(Stream stream, bool generateMipmaps = false)
        {
            return FromStream(stream, generateMipmaps);
        }

        /// <summary>
        /// Loads a default embedded texture
        /// </summary>
        public static Texture2D LoadDefault(DefaultTexture texture)
        {
            string fileName = texture switch
            {
                DefaultTexture.White => "default_white.png",
                DefaultTexture.Gray18 => "default_gray18.png",
                DefaultTexture.Normal => "default_normal.png",
                DefaultTexture.Surface => "default_surface.png",
                DefaultTexture.Emission => "default_emission.png",
                DefaultTexture.Grid => "grid.png",
                DefaultTexture.Noise => "noise.png",
                _ => throw new ArgumentException($"Unknown default texture: {texture}")
            };

            string resourcePath = $"Assets/Defaults/{fileName}";
            using (var stream = EmbeddedResources.GetStream(resourcePath))
            {
                var result = FromStream(stream, true);
                result.AssetPath = $"$Default:{texture}";
                return result;
            }
        }

        internal const string ImageNotContiguousError = "To load/save an image, it's backing memory must be contiguous. Consider using smaller image sizes or changing your ImageSharp memory allocation settings to allow larger buffers.";

        internal const string ImageSizeMustMatchTextureSizeError = "The size of the image must match the size of the texture";

        internal const string TextureFormatMustBeColor4bError = "The texture's format must be Color4b (RGBA)";

        #endregion
    }
}
