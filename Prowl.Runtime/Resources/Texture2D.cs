// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using Prowl.Aperture;
using Prowl.Echo;

namespace Prowl.Runtime.Resources;


/// <summary>
/// A <see cref="Texture"/> whose image has two dimensions. Row 0 sits at V 0, so images load bottom row first.
/// </summary>
public sealed class Texture2D : Texture, ISerializable
{
    private uint _width;
    private uint _height;

    /// <summary>The width of this <see cref="Texture2D"/>.</summary>
    public uint Width { get { EnsureLoaded(); return _width; } }

    /// <summary>The height of this <see cref="Texture2D"/>.</summary>
    public uint Height { get { EnsureLoaded(); return _height; } }

    public Texture2D() : base(TextureFormat.RGBA8, false, WrapMode.Repeat) { }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> with no image data. Mipmaps gives it a full mip chain to fill with
    /// <see cref="Texture.GenerateMipmaps"/> or <see cref="SetMipChain"/>, sRGB marks RGBA8 texels as colour decoded when
    /// sampled, and random write lets compute kernels write it through an image.
    /// </summary>
    public Texture2D(uint width, uint height, TextureFormat format = TextureFormat.RGBA8, bool mipmaps = false, bool srgb = false, bool randomWrite = false)
        : base(format, srgb, WrapMode.Repeat)
    {
        RandomWrite = randomWrite;
        Resize(width, height, mipmaps ? FullChain(width, height) : 1);
    }

    private void Resize(uint width, uint height, uint mips)
    {
        ValidateTextureSize(width, height);
        _width = width;
        _height = height;
        Allocate(mips);
    }

    private protected override Graphite.TextureDescription Describe(uint mips)
        => Graphite.TextureDescription.Texture2D(_width, _height, mips, 1, GpuFormat, Usage);

    private protected override uint FullMipCount => FullChain(_width, _height);

    /// <summary>Sets an area of the <see cref="Texture2D"/> from a pointer, its first row at V 0.</summary>
    public unsafe void SetDataPtr(void* ptr, int rectX, int rectY, uint rectWidth, uint rectHeight)
    {
        EnsureLoaded();
        ValidateRectOperation(rectX, rectY, rectWidth, rectHeight);
        Upload(new ReadOnlySpan<byte>(ptr, (int)(rectWidth * rectHeight * GetBytesPerPixel(Format))), (uint)rectX, (uint)rectY, 0, rectWidth, rectHeight, 1);
    }

    /// <summary>Sets an area of the <see cref="Texture2D"/>, its first row at V 0.</summary>
    /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
    public void SetData<T>(Memory<T> data, int rectX, int rectY, uint rectWidth, uint rectHeight) where T : unmanaged
        => SetData(MemoryMarshal.AsBytes<T>(data.Span), rectX, rectY, rectWidth, rectHeight);

    /// <summary>Sets an area of the <see cref="Texture2D"/> from bytes, its first row at V 0.</summary>
    public void SetData(ReadOnlySpan<byte> data, int rectX, int rectY, uint rectWidth, uint rectHeight)
    {
        EnsureLoaded();
        ValidateRectOperation(rectX, rectY, rectWidth, rectHeight);
        long required = (long)rectWidth * rectHeight * GetBytesPerPixel(Format);
        ValidateByteCapacity(data.Length, required, nameof(data));
        Upload(data[..(int)required], (uint)rectX, (uint)rectY, 0, rectWidth, rectHeight, 1);
    }

    /// <summary>Sets the entire <see cref="Texture2D"/>, its first row at V 0.</summary>
    public void SetData<T>(Memory<T> data) where T : unmanaged
    {
        EnsureLoaded();
        SetData(data, 0, 0, Width, Height);
    }

    /// <summary>
    /// Uploads every mip level from <paramref name="levels"/>, level 0 first, each half the size of the one before. For
    /// stored mips and for data whose mips cannot come from a plain box filter, such as alpha that keeps its coverage.
    /// </summary>
    public void SetMipChain(IReadOnlyList<byte[]> levels)
    {
        EnsureLoaded();
        if (levels.Count == 0) return;
        int bpp = GetBytesPerPixel(Format);
        if (MipCount != levels.Count)
            Allocate((uint)levels.Count);
        for (uint mip = 0; mip < MipCount; mip++)
        {
            uint w = MipSize(_width, mip), h = MipSize(_height, mip);
            ValidateByteCapacity(levels[(int)mip].Length, (long)w * h * bpp, nameof(levels));
            Upload(levels[(int)mip].AsSpan(0, (int)(w * h * bpp)), 0, 0, 0, w, h, 1, mip);
        }
    }

    /// <summary>Reads the entire <see cref="Texture2D"/> back, its first row at V 0. Waits for the GPU.</summary>
    public unsafe void GetDataPtr(void* ptr)
    {
        EnsureLoaded();
        byte[] data = Download();
        data.AsSpan().CopyTo(new Span<byte>(ptr, data.Length));
    }

    /// <summary>Reads the entire <see cref="Texture2D"/> back, its first row at V 0. Waits for the GPU, zeroes headless.</summary>
    /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
    public void GetData<T>(Memory<T> data) where T : unmanaged
    {
        EnsureLoaded();
        Span<byte> target = MemoryMarshal.AsBytes(data.Span);
        ValidateByteCapacity(target.Length, GetSize(), nameof(data));
        byte[] read = Download();
        read.AsSpan(0, Math.Min(read.Length, target.Length)).CopyTo(target);
    }

    /// <summary>Reads one mip level back, its first row at V 0. Waits for the GPU, zeroes headless.</summary>
    public byte[] GetMipData(uint mip)
    {
        EnsureLoaded();
        byte[] data = new byte[MipSize(_width, mip) * MipSize(_height, mip) * GetBytesPerPixel(Format)];
        byte[] read = Download(mip);
        read.AsSpan(0, Math.Min(read.Length, data.Length)).CopyTo(data);
        return data;
    }

    /// <summary>Bytes needed to hold this texture's level 0, and so the size of a readback buffer.</summary>
    public int GetSize()
    {
        EnsureLoaded();
        return (int)Width * (int)Height * GetBytesPerPixel(Format);
    }

    /// <summary>Recreates this <see cref="Texture2D"/>'s image with a new size, losing the image data and keeping whether it has mips.</summary>
    public void RecreateImage(uint width, uint height)
    {
        EnsureLoaded();
        Resize(width, height, MipCount > 1 ? FullChain(width, height) : 1);
    }

    private void ValidateByteCapacity(long providedBytes, long requiredBytes, string paramName)
    {
        if (providedBytes < requiredBytes)
            throw new ArgumentException(
                $"Buffer holds {providedBytes} bytes but {Format} needs {requiredBytes} for this region.", paramName);
    }

    private static void ValidateTextureSize(uint width, uint height)
    {
        if (width <= 0 || width > Graphics.MaxTextureSize)
            throw new ArgumentOutOfRangeException(nameof(width), width, nameof(width) + " must be in the range (0, " + nameof(Graphics.MaxTextureSize) + "]");

        if (height <= 0 || height > Graphics.MaxTextureSize)
            throw new ArgumentOutOfRangeException(nameof(height), height, nameof(height) + " must be in the range (0, " + nameof(Graphics.MaxTextureSize) + "]");
    }

    private void ValidateRectOperation(int rectX, int rectY, uint rectWidth, uint rectHeight)
    {
        if (rectX < 0 || rectX >= Width)
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

    protected internal override long EstimateBytes()
        => (long)(_width * _height * (ulong)GetBytesPerPixel(FormatUnchecked) * (MipCountUnchecked > 1 ? 4.0 / 3.0 : 1.0));

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        SerializeHeader(compoundTag);
        compoundTag.Add("Width", new(Width));
        compoundTag.Add("Height", new(Height));
        compoundTag.Add("Format", new((int)Format));
        compoundTag.Add("Srgb", new(Srgb));
        compoundTag.Add("Filter", new((int)Filter));
        compoundTag.Add("WrapU", new((int)WrapU));
        compoundTag.Add("WrapV", new((int)WrapV));
        compoundTag.Add("Anisotropy", new(Anisotropy));
        compoundTag.Add("BorderColor", new((int)BorderColor));
        EchoObject mips = EchoObject.NewList();
        for (uint mip = 0; mip < MipCount; mip++)
            mips.ListAdd(new(GetMipData(mip)));
        compoundTag.Add("Mips", mips);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        uint width = value["Width"].UIntValue;
        uint height = value["Height"].UIntValue;
        EchoObject mips = value["Mips"];

        SetFormat((TextureFormat)value["Format"].IntValue, value.Get("Srgb")?.BoolValue ?? false);
        Resize(width, height, (uint)Math.Max(1, mips.Count));
        DeserializeHeader(value);

        byte[][] levels = new byte[mips.Count][];
        for (int i = 0; i < levels.Length; i++)
            levels[i] = mips[i].ByteArrayValue;
        SetMipChain(levels);
        if (value.Get("GenerateMipmaps")?.BoolValue == true) GenerateMipmaps();

        SetSampling((FilterMode)(value.Get("Filter")?.IntValue ?? (int)FilterMode.Bilinear),
            (WrapMode)(value.Get("WrapU")?.IntValue ?? 0), (WrapMode)(value.Get("WrapV")?.IntValue ?? 0), WrapMode.Repeat,
            value.Get("Anisotropy")?.UIntValue ?? 0, (TextureBorderColor)(value.Get("BorderColor")?.IntValue ?? 0));
    }

    /// <summary>The same image with its rows in the other order.</summary>
    public static byte[] FlipRows(ReadOnlySpan<byte> data, int width, int height, int bytesPerPixel)
    {
        int row = width * bytesPerPixel;
        byte[] flipped = new byte[data.Length];
        for (int y = 0; y < height; y++)
            data.Slice(y * row, row).CopyTo(flipped.AsSpan((height - 1 - y) * row));
        return flipped;
    }

    #region Image loading

    /// <summary>Decodes into the layout and row order an upload wants, so no repacking follows.</summary>
    private static DecodeOptions DecodeToUploadLayout => new()
    {
        TargetPixelFormat = PixelFormat.Rgba8,
        // Image textures hold their bottom row first, so UV (0, 0) is the image's lower left as everywhere in Prowl
        FlipVertically = true,
        RowAlignment = 4,
    };

    /// <summary>
    /// Creates an RGBA8 <see cref="Texture2D"/> from an already decoded Rgba8 image. With mipmaps it filters Trilinear,
    /// without Bilinear. sRGB marks it as colour, decoded when sampled, off for normal maps and masks.
    /// </summary>
    public static Texture2D FromImage(Aperture.Image image, bool mipmaps = false, bool srgb = true)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.PixelFormat != PixelFormat.Rgba8)
            throw new ArgumentException(TextureFormatMustBeRgba8Error, nameof(image));

        Texture2D texture = new((uint)image.Width, (uint)image.Height, TextureFormat.RGBA8, mipmaps, srgb);
        try
        {
            texture.SetData(image.RootFrame.Pixels, 0, 0, (uint)image.Width, (uint)image.Height);
            if (mipmaps)
            {
                texture.GenerateMipmaps();
                texture.Filter = FilterMode.Trilinear;
            }
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    /// <summary>Creates a <see cref="Texture2D"/> by decoding an image from a <see cref="Stream"/>.</summary>
    public static Texture2D FromStream(Stream stream, bool mipmaps = false, bool srgb = true)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using Aperture.Image image = Aperture.Image.Load(stream, DecodeToUploadLayout);
        return FromImage(image, mipmaps, srgb);
    }

    /// <summary>Creates a <see cref="Texture2D"/> by decoding an image file.</summary>
    public static Texture2D FromFile(string file, bool mipmaps = false, bool srgb = true)
    {
        using Aperture.Image image = Aperture.Image.Load(file, DecodeToUploadLayout);
        return FromImage(image, mipmaps, srgb);
    }

    /// <summary>
    /// Get the shared instance of a default embedded texture. Returns the same GPU
    /// texture across the whole app callers that need a unique mutable copy should
    /// call <see cref="FromImage"/>/<see cref="FromStream"/> directly.
    /// </summary>
    public static Texture2D LoadDefault(DefaultTexture texture) => BuiltInAssets.Load<Texture2D>(BuiltInAssets.GuidFor(texture));

    /// <summary>The built-in texture without loading it, for code that runs while another asset loads.</summary>
    internal static Texture2D GetDefault(DefaultTexture texture) => AssetDatabase.Get<Texture2D>(BuiltInAssets.GuidFor(texture))!;

    /// <summary>
    /// Raw load of a default embedded texture invoked by <see cref="BuiltInAssets"/>
    /// on first cache miss. Public callers should use <see cref="LoadDefault"/>.
    /// </summary>
    internal static Texture2D ParseDefault(DefaultTexture texture)
    {
        (string fileName, bool srgb) = texture switch
        {
            DefaultTexture.White => ("default_white.png", true),
            DefaultTexture.Gray18 => ("default_gray18.png", false),
            DefaultTexture.Normal => ("default_normal.png", false),
            DefaultTexture.Surface => ("default_surface.png", false),
            DefaultTexture.Emission => ("default_emission.png", true),
            DefaultTexture.Grid => ("grid.png", true),
            DefaultTexture.UIPanel => ("UI_Panel.png", true),
            DefaultTexture.Handle => ("handle_ui.png", true),
            DefaultTexture.Noise => ("noise.png", false),
            DefaultTexture.CloudNoise => ("cloud_noise.png", false),
            DefaultTexture.IconCamera => ("icon_camera.png", true),
            DefaultTexture.IconLight => ("icon_light.png", true),
            _ => throw new ArgumentException($"Unknown default texture: {texture}")
        };

        using Stream stream = EmbeddedResources.GetStream($"Assets/Defaults/{fileName}");
        return FromStream(stream, true, srgb);
    }

    internal const string TextureFormatMustBeRgba8Error = "The image must be decoded to Rgba8";

    #endregion
}
