// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Veldrid;

namespace Prowl.Runtime
{
    /// <summary>
    /// A <see cref="Texture"/> whose image has two dimensions and support for multisampling.
    /// </summary>
    public sealed class Texture3D : Texture
    {
        /// <summary>The width of this <see cref="Texture3D"/>.</summary>
        public uint Width => InternalTexture.Width;

        /// <summary>The height of this <see cref="Texture3D"/>.</summary>
        public uint Height => InternalTexture.Height;

        /// <summary>The depth of this <see cref="Texture3D"/>.</summary>
        public uint Depth => InternalTexture.Depth;

        internal Texture3D() : base() { }

        /// <summary>
        /// Creates a <see cref="Texture3D"/> with the desired parameters but no image data.
        /// </summary>
        /// <param name="width">The width of the <see cref="Texture3D"/>.</param>
        /// <param name="height">The height of the <see cref="Texture3D"/>.</param>
        /// <param name="depth">The depth of the <see cref="Texture3D"/>.</param>
        /// <param name="mipLevels">How many mip levels this <see cref="Texture3D"/> has.</param>
        /// <param name="format">The pixel format for this <see cref="Texture3D"/>.</param>
        public Texture3D(
            uint width, uint height, uint depth,
            uint mipLevels = 1,
            PixelFormat format = PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage usage = TextureUsage.Sampled
        ) : base(new()
        {
            Width = width,
            Height = height,
            Depth = depth,
            MipLevels = mipLevels,
            Format = format,
            Usage = usage,
            Type = TextureType.Texture3D
        })
        { }

        /// <summary>
        /// Sets the data of an area of the <see cref="Texture3D"/>.
        /// </summary>
        /// <param name="ptr">The pointer from which the pixel data will be read.</param>
        /// <param name="rectX">The X coordinate of the first pixel to write.</param>
        /// <param name="rectY">The Y coordinate of the first pixel to write.</param>
        /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
        /// <param name="rectHeight">The height of the rectangle of pixels to write.</param>
        public unsafe void SetDataPtr(void* ptr, uint rectX, uint rectY, uint rectZ, uint rectWidth, uint rectHeight, uint rectDepth, uint mipLevel = 0) =>
            InternalSetDataPtr(ptr, new Vector3Int((int)rectX, (int)rectY, (int)rectZ), new Vector3Int((int)rectWidth, (int)rectHeight, (int)rectDepth), 0, mipLevel);

        /// <summary>
        /// Sets the data of an area of the <see cref="Texture3D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture3D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="Span{T}"/> containing the new pixel data.</param>
        /// <param name="rectX">The X coordinate of the first pixel to write.</param>
        /// <param name="rectY">The Y coordinate of the first pixel to write.</param>
        /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
        /// <param name="rectHeight">The height of the rectangle of pixels to write.</param>
        /// <param name="mipLevel">The mip level to write to.</param>
        public unsafe void SetData<T>(Span<T> data, uint rectX, uint rectY, uint rectZ, uint rectWidth, uint rectHeight, uint rectDepth, uint mipLevel = 0) where T : unmanaged =>
            InternalSetData(data, new Vector3Int((int)rectX, (int)rectY, (int)rectZ), new Vector3Int((int)rectWidth, (int)rectHeight, (int)rectDepth), 0, mipLevel);

        /// <summary>
        /// Sets the data of the entire <see cref="Texture3D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture3D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="Span{T}"/> containing the new pixel data.</param>
        public void SetData<T>(Span<T> data, uint mipLevel = 0) where T : unmanaged =>
            SetData(data, 0, 0, 0, Width, Height, Depth, mipLevel);

        /// <summary>
        /// Copies the data of a portion of a <see cref="Texture3D"/>.
        /// </summary>
        /// <param name="data">The pointer to the copied data.</param>
        /// <param name="mipLevel">The mip level to copy.</param>
        public unsafe void CopyDataPtr(void* data, uint dataSize, uint mipLevel = 0) =>
            InternalCopyDataPtr(data, dataSize, out _, out _, 0, mipLevel);

        /// <summary>
        /// Copies the data of a portion of a <see cref="Texture3D"/>.
        /// </summary>
        /// <typeparam name="T">A struct with the same format as this <see cref="Texture3D"/>'s pixels.</typeparam>
        /// <param name="data">A <see cref="Span{T}"/> in which to write the pixel data.</param>
        /// <param name="mipLevel">The mipLevel to copy.</param>
        public unsafe void CopyData<T>(Span<T> data, uint mipLevel = 0) where T : unmanaged =>
            InternalCopyData(data, 0, mipLevel);

        /// <summary>
        /// Recreates and resizes the <see cref="Texture3D"/>.
        /// </summary>
        public void RecreateTexture(uint width, uint height, uint depth)
        {
            RecreateInternalTexture(new()
            {
                Width = width,
                Height = height,
                Depth = depth,
                MipLevels = MipLevels,
                Format = Format,
                Usage = Usage,
                Type = Type,
            });
        }

        public override SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty compoundTag = SerializedProperty.NewCompound();

            SerializeHeader(compoundTag);

            compoundTag.Add("Width", new((int)Width));
            compoundTag.Add("Height", new((int)Height));
            compoundTag.Add("Depth", new((int)Height));
            compoundTag.Add("MipLevels", new(MipLevels));
            compoundTag.Add("IsMipMapped", new(IsMipmapped));
            compoundTag.Add("ImageFormat", new((int)Format));
            compoundTag.Add("Usage", new((int)Usage));

            Span<byte> memory = new byte[GetMemoryUsage()];
            CopyData(memory);
            compoundTag.Add("Data", new(memory.ToArray()));

            return compoundTag;
        }

        public override void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            DeserializeHeader(value);

            uint width = (uint)value["Width"].IntValue;
            uint height = (uint)value["Height"].IntValue;
            uint depth = (uint)value["Depth"].IntValue;
            uint mips = (uint)value["MipLevels"].IntValue;
            bool isMipMapped = value["IsMipMapped"].BoolValue;
            PixelFormat imageFormat = (PixelFormat)value["ImageFormat"].IntValue;
            TextureUsage usage = (TextureUsage)value["Usage"].IntValue;

            var param = new[] { typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(PixelFormat), typeof(TextureUsage) };
            var values = new object[] { width, height, depth, mips, imageFormat, usage };

            typeof(Texture3D).GetConstructor(param).Invoke(this, values);

            Span<byte> memory = value["Data"].ByteArrayValue;
            SetData(memory);

            if (isMipMapped)
                GenerateMipmaps();
        }
    }
}
