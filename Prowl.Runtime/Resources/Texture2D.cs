using Prowl.Runtime.Rendering.Primitives;
using System;

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

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty compoundTag = SerializedProperty.NewCompound();
            compoundTag.Add("Width", new((int)Width));
            compoundTag.Add("Height", new((int)Height));
            compoundTag.Add("IsMipMapped", new(IsMipmapped));
            compoundTag.Add("ImageFormat", new((int)ImageFormat));
            compoundTag.Add("MinFilter", new((int)MinFilter));
            compoundTag.Add("MagFilter", new((int)MagFilter));
            compoundTag.Add("Wrap", new((int)WrapMode));
            Memory<byte> memory = new byte[GetSize()];
            GetData(memory);
            compoundTag.Add("Data", new(memory.ToArray()));

            return compoundTag;
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            Width = (uint)value["Width"].IntValue;
            Height = (uint)value["Height"].IntValue;
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

            if(isMipMapped)
                GenerateMipmaps();

            SetTextureFilters(MinFilter, MagFilter);
            SetWrapModes(Wrap, Wrap);
        }
    }
}
