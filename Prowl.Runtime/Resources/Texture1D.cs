using Veldrid;
using System;

namespace Prowl.Runtime
{
    /// <summary>
    /// A <see cref="Texture"/> whose image has one dimension.
    /// </summary>
    public sealed class Texture1D : Texture, ISerializable
    {
        /// <summary>The width of this <see cref="Texture1D"/>.</summary>
        public uint Width => InternalTexture.Width;
        
        /// <summary>
        /// Creates a <see cref="Texture1D"/> with the desired parameters but no image data.
        /// </summary>
        /// <param name="width">The width of the <see cref="Texture1D"/>.</param>
        /// <param name="mipLevels">How many mip levels this texcture has <see cref="Texture3D"/>.</param>
        /// <param name="format">The pixel format for this <see cref="Texture3D"/>.</param>
        public Texture1D(uint width, uint mipLevels = 0, PixelFormat format = PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage usage = TextureUsage.Sampled) : base(new()
            {
                Width = width,
                Height = 1,
                Depth = 1,
                MipLevels = mipLevels,
                ArrayLayers = 0,
                Format = format,
                Usage = usage,
                Type = TextureType.Texture1D,
            }) { }

        /// <summary>
        /// Sets the data of an area of the <see cref="Texture1D"/>.
        /// </summary>
        /// <param name="ptr">The pointer from which the pixel data will be read.</param>
        /// <param name="rectX">The X coordinate of the first pixel to write.</param>
        /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
        public unsafe void SetDataPtr(void* ptr, uint rectX, uint rectWidth, uint mipLevel = 0) => 
            InternalSetDataPtr(ptr, new Vector3Int((int)rectX, 0, 0), new Vector3Int((int)rectWidth, 1, 1), 0, mipLevel);

        /// <summary>
        /// Sets the data of an area of the <see cref="Texture1D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture1D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="Memory{T}"/> containing the new pixel data.</param>
        /// <param name="rectX">The X coordinate of the first pixel to write.</param>
        /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
        /// <param name="mipLevel">The mip level to write to.</param>
        public unsafe void SetData<T>(Memory<T> data, uint rectX, uint rectWidth, uint mipLevel = 0) where T : unmanaged =>
            InternalSetData<T>(data, new Vector3Int((int)rectX, 1, 1), new Vector3Int((int)rectWidth, 1, 1), 0, mipLevel);

        /// <summary>
        /// Sets the data of the entire <see cref="Texture1D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture1D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="ReadOnlySpan{T}"/> containing the new pixel data.</param>
        public void SetData<T>(Memory<T> data, uint mipLevel = 0) where T : unmanaged =>
            SetData(data, 0, Width, mipLevel);

        /// <summary>
        /// Copies the data of a portion of a <see cref="Texture1D"/>.
        /// </summary>
        /// <param name="data">The pointer to the copied data.</param>
        /// <param name="mipLevel">The mip level to copy.</param>
        public unsafe void CopyDataPtr(void* data, uint mipLevel = 0) => 
            InternalCopyDataPtr(data, out _, out _, mipLevel);

        /// <summary>
        /// Copies the data of a portion of a <see cref="Texture1D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture1D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="Span{T}"/> in which to write the pixel data.</param>
        /// <param name="mipLevel">The mip level to copy.</param>
        public unsafe void CopyData<T>(Memory<T> data, uint mipLevel = 0) where T : unmanaged => 
            InternalCopyData(data, mipLevel);


        /// <summary>
        /// Recreates and resizes the <see cref="Texture1D"/>.
        /// </summary>
        public void RecreateTexture(uint width)
        {
            RecreateInternalTexture(new()
            {
                Width = width,
                Height = 1,
                Depth = 1,
                MipLevels = this.MipLevels,
                ArrayLayers = 0,
                Format = this.Format,
                Usage = this.Usage,
                Type = this.Type,
            });
        }

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty compoundTag = SerializedProperty.NewCompound();
            compoundTag.Add("Width", new((int)Width));
            compoundTag.Add("MipLevels", new(MipLevels));
            compoundTag.Add("IsMipMapped", new(IsMipmapped));
            compoundTag.Add("ImageFormat", new((int)Format));
            compoundTag.Add("Usage", new((int)Usage));

            Memory<byte> memory = new byte[GetMemoryUsage()];
            CopyData(memory);
            compoundTag.Add("Data", new(memory.ToArray()));

            return compoundTag;
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            uint width = (uint)value["Width"].IntValue;
            uint mips = (uint)value["MipLevels"].IntValue;
            bool isMipMapped = value["IsMipMapped"].BoolValue;
            PixelFormat imageFormat = (PixelFormat)value["ImageFormat"].IntValue;
            TextureUsage usage = (TextureUsage)value["Usage"].IntValue;

            var param = new[] { typeof(uint), typeof(uint), typeof(PixelFormat), typeof(TextureUsage) };
            var values = new object[] { width, mips, imageFormat, usage };

            typeof(Texture1D).GetConstructor(param).Invoke(this, values);

            Memory<byte> memory = value["Data"].ByteArrayValue;
            SetData(memory);

            if (isMipMapped)
                GenerateMipmaps();
        }
    }
}
