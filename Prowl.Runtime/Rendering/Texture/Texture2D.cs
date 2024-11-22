// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Veldrid;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// A <see cref="Texture"/> whose image has two dimensions and support for multisampling.
/// </summary>
public sealed class Texture2D : Texture
{
    /// <summary>The width of this <see cref="Texture2D"/>.</summary>
    public uint Width => InternalTexture.Width;

    /// <summary>The height of this <see cref="Texture2D"/>.</summary>
    public uint Height => InternalTexture.Height;

    public static readonly AssetRef<Texture2D> White = LoadDefault("Defaults/default_white.png");
    public static readonly AssetRef<Texture2D> Black = LoadDefault("Defaults/default_black.png");
    public static readonly AssetRef<Texture2D> Gray = LoadDefault("Defaults/default_gray.png");
    public static readonly AssetRef<Texture2D> Normal = LoadDefault("Defaults/default_normal.png");
    public static readonly AssetRef<Texture2D> Red = LoadDefault("Defaults/default_red.png");
    public static readonly AssetRef<Texture2D> Surface = LoadDefault("Defaults/default_surface.png");
    public static readonly AssetRef<Texture2D> Grid = LoadDefault("Defaults/grid.png");

    public static readonly AssetRef<Texture2D> EmptyRW = CreateDefault(1, 1, [Color.black], TextureUsage.Storage);


    private static Texture2D CreateDefault(uint sizeX, uint sizeY, Color32[] colors, TextureUsage usage = TextureUsage.Sampled)
    {
        Texture2D texture = new Texture2D(sizeX, sizeY, 0, PixelFormat.R8_G8_B8_A8_UNorm, usage)
        {
            Name = "Default Created Texture"
        };

        texture.Sampler.SetFilter(FilterType.Point, FilterType.Point, FilterType.Point);
        texture.SetData(new Span<Color32>(colors));
        return texture;
    }


    private static AssetRef<Texture2D> LoadDefault(string name)
    {
        return Application.AssetProvider.LoadAsset<Texture2D>(name);
    }


    internal Texture2D() : base() { }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> with the desired parameters but no image data.
    /// </summary>
    /// <param name="width">The width of the <see cref="Texture2D"/>.</param>
    /// <param name="height">The height of the <see cref="Texture2D"/>.</param>
    /// <param name="mipLevels">How many mip levels this <see cref="Texture2D"/> has.</param>
    /// <param name="format">The pixel format for this <see cref="Texture2D"/>.</param>
    /// <param name="usage">The usage modes of this <see cref="Texture2D"/>.</param>
    /// <param name="sampleCount">The multisampled count of the texture. Values above Count1 will require the texture to be resolved with <see cref="CommandBuffer.ResolveMultisampledTexture(RenderTexture, RenderTexture)"/> </param>
    public Texture2D(
        uint width, uint height,
        uint mipLevels = 1,
        PixelFormat format = PixelFormat.R8_G8_B8_A8_UNorm,
        TextureUsage usage = TextureUsage.Sampled,
        TextureSampleCount sampleCount = TextureSampleCount.Count1
    ) : base(new()
    {
        Width = width,
        Height = height,
        MipLevels = mipLevels,
        Format = format,
        Usage = usage,
        Type = TextureType.Texture2D,
        SampleCount = sampleCount,
    }
    )
    { }

    public Texture2D(Veldrid.Texture resource) : base(resource, TextureType.Texture2D)
    { }

    /// <summary>
    /// Sets the data of an area of the <see cref="Texture2D"/>.
    /// </summary>
    /// <param name="ptr">The pointer from which the pixel data will be read.</param>
    /// <param name="rectX">The X coordinate of the first pixel to write.</param>
    /// <param name="rectY">The Y coordinate of the first pixel to write.</param>
    /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
    /// <param name="rectHeight">The height of the rectangle of pixels to write.</param>
    /// <param name="mipLevel">The mip level to copy from.</param>
    public unsafe void SetDataPtr(void* ptr, uint rectX, uint rectY, uint rectWidth, uint rectHeight, uint mipLevel = 0) =>
        InternalSetDataPtr(ptr, new Vector3Int((int)rectX, (int)rectY, 0), new Vector3Int((int)rectWidth, (int)rectHeight, 1), 0, mipLevel);

    /// <summary>
    /// Sets the data of an area of the <see cref="Texture2D"/>.
    /// </summary>
    /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
    /// <param name="data">A <see cref="Span{T}"/> containing the new pixel data.</param>
    /// <param name="rectX">The X coordinate of the first pixel to write.</param>
    /// <param name="rectY">The Y coordinate of the first pixel to write.</param>
    /// <param name="rectWidth">The width of the rectangle of pixels to write.</param>
    /// <param name="rectHeight">The height of the rectangle of pixels to write.</param>
    /// <param name="mipLevel">The mip level to write to.</param>
    public unsafe void SetData<T>(Span<T> data, uint rectX, uint rectY, uint rectWidth, uint rectHeight, uint mipLevel = 0) where T : unmanaged =>
        InternalSetData(data, new Vector3Int((int)rectX, (int)rectY, 0), new Vector3Int((int)rectWidth, (int)rectHeight, 1), 0, mipLevel);

    /// <summary>
    /// Sets the data of the entire <see cref="Texture2D"/>.
    /// </summary>
    /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
    /// <param name="data">A <see cref="ReadOnlySpan{T}"/> containing the new pixel data.</param>
    /// <param name="mipLevel">The mip level to copy from.</param>
    public void SetData<T>(Span<T> data, uint mipLevel = 0) where T : unmanaged =>
        SetData(data, 0, 0, Width, Height, mipLevel);

    /// <summary>
    /// Copies the data of a portion of a <see cref="Texture2D"/>.
    /// </summary>
    /// <param name="data">The pointer to the location to copy to.</param>
    /// <param name="dataSize">The number of bytes to copy from the source.</param>
    /// <param name="mipLevel">The mip level to copy.</param>
    public unsafe void CopyDataPtr(void* data, uint dataSize, uint mipLevel = 0) =>
        InternalCopyDataPtr(data, dataSize, out _, out _, 0, mipLevel);

    /// <summary>
    /// Copies the data of a portion of a <see cref="Texture2D"/>.
    /// </summary>
    /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
    /// <param name="data">A <see cref="Span{T}"/> in which to write the pixel data.</param>
    /// <param name="mipLevel">The mip level to copy.</param>
    public unsafe void CopyData<T>(Span<T> data, uint mipLevel = 0) where T : unmanaged =>
        InternalCopyData(data, 0, mipLevel);

    /// <summary>
    /// Gets the pixel at a position in a <see cref="Texture2D"/>.
    /// </summary>
    /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
    /// <param name="x">The X coordinate of the pixel to get.</param>
    /// <param name="y">The Y coordinate of the pixel to get.</param>
    /// <param name="mipLevel">The mip level to get.</param>
    public unsafe T GetPixel<T>(uint x, uint y, uint mipLevel = 0) where T : unmanaged =>
        InternalCopyPixel<T>(new Vector3Int((int)x, (int)y, 0), 0, mipLevel);

    /// <summary>
    /// Recreates and resizes the <see cref="Texture2D"/>.
    /// </summary>
    public void RecreateTexture(uint width, uint height)
    {
        RecreateInternalTexture(new()
        {
            Width = width,
            Height = height,
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
        compoundTag.Add("MipLevels", new(MipLevels));
        compoundTag.Add("IsMipMapped", new(IsMipmapped));
        compoundTag.Add("ImageFormat", new((int)Format));
        compoundTag.Add("Usage", new((int)Usage));

        Span<byte> Span = new byte[GetMemoryUsage()];
        CopyData(Span);
        compoundTag.Add("Data", new(Span.ToArray()));

        return compoundTag;
    }

    public override void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
    {
        DeserializeHeader(value);

        uint width = (uint)value["Width"].IntValue;
        uint height = (uint)value["Height"].IntValue;
        uint mips = value["MipLevels"].UIntValue;
        bool isMipMapped = value["IsMipMapped"].BoolValue;
        PixelFormat imageFormat = (PixelFormat)value["ImageFormat"].IntValue;
        TextureUsage usage = (TextureUsage)value["Usage"].IntValue;

        var param = new[] { typeof(uint), typeof(uint), typeof(uint), typeof(PixelFormat), typeof(TextureUsage), typeof(TextureSampleCount) };
        var values = new object[] { width, height, mips, imageFormat, usage, TextureSampleCount.Count1 };

        typeof(Texture2D).GetConstructor(param).Invoke(this, values);

        Span<byte> Span = value["Data"].ByteArrayValue;
        SetData(Span);

        if (isMipMapped)
            GenerateMipmaps();
    }
}
