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

    /// <summary>The compressed levels of a texture the GPU holds expanded, which a readback can no longer return.</summary>
    [NotHeld] private byte[][]? _expandedLevels;

    private protected override bool SupportsCompressed => true;

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
        if (randomWrite && GraphicsFormats.IsCompressed(format))
            throw new ArgumentException($"Compute kernels cannot write the compressed format {format}.", nameof(randomWrite));
        RandomWrite = randomWrite;
        Resize(width, height, mipmaps ? FullChain(width, height) : 1);
    }

    private void Resize(uint width, uint height, uint mips)
    {
        ValidateTextureSize(width, height);
        _width = width;
        _height = height;
        _expandedLevels = null;
        Allocate(mips);
    }

    private protected override Graphite.TextureDescription Describe(uint mips)
        => Graphite.TextureDescription.Texture2D(_width, _height, mips, 1, GpuFormat, Usage);

    private protected override uint FullMipCount => FullChain(_width, _height);

    /// <summary>Sets an area of the <see cref="Texture2D"/> from a pointer, its first row at V 0.</summary>
    public unsafe void SetDataPtr(void* ptr, int rectX, int rectY, uint rectWidth, uint rectHeight)
    {
        EnsureLoaded();
        SetData(new ReadOnlySpan<byte>(ptr, (int)GraphicsFormats.DataSize(Format, rectWidth, rectHeight)), rectX, rectY, rectWidth, rectHeight);
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
        ValidateBlockAlignment(rectX, rectY, rectWidth, rectHeight);
        long required = GraphicsFormats.DataSize(Format, rectWidth, rectHeight);
        ValidateByteCapacity(data.Length, required, nameof(data));
        if (_expandedLevels != null) PatchExpanded(data[..(int)required], rectX, rectY, rectWidth, rectHeight);
        Upload(data[..(int)required], (uint)rectX, (uint)rectY, 0, rectWidth, rectHeight, 1);
    }

    /// <summary>A compressed area has to start on a block and end on one, or at the texture's edge.</summary>
    private void ValidateBlockAlignment(int rectX, int rectY, uint rectWidth, uint rectHeight)
    {
        if (!GraphicsFormats.IsCompressed(Format)) return;

        (uint blockWidth, uint blockHeight) = Graphite.FormatSizeHelpers.GetBlockDimensions(GraphicsFormats.ToPixelFormat(Format));
        bool alignedX = rectX % blockWidth == 0 && (rectWidth % blockWidth == 0 || rectX + rectWidth == _width);
        bool alignedY = rectY % blockHeight == 0 && (rectHeight % blockHeight == 0 || rectY + rectHeight == _height);
        if (!alignedX || !alignedY)
            throw new ArgumentException($"A {Format} area must start on a {blockWidth}x{blockHeight} block and end on one or at the texture's edge.");
    }

    /// <summary>Writes compressed blocks into the kept level 0, so a texture the GPU holds expanded still reads back what was set.</summary>
    private void PatchExpanded(ReadOnlySpan<byte> blocks, int rectX, int rectY, uint rectWidth, uint rectHeight)
    {
        (uint blockWidth, uint blockHeight) = Graphite.FormatSizeHelpers.GetBlockDimensions(GraphicsFormats.ToPixelFormat(Format));
        int blockBytes = (int)(GraphicsFormats.DataSize(Format, blockWidth, blockHeight));
        int levelAcross = (int)((_width + blockWidth - 1) / blockWidth);
        int across = (int)((rectWidth + blockWidth - 1) / blockWidth);
        int down = (int)((rectHeight + blockHeight - 1) / blockHeight);
        int firstX = rectX / (int)blockWidth, firstY = rectY / (int)blockHeight;
        byte[] level = _expandedLevels![0];

        for (int row = 0; row < down; row++)
            blocks.Slice(row * across * blockBytes, across * blockBytes)
                .CopyTo(level.AsSpan((((firstY + row) * levelAcross) + firstX) * blockBytes));
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
        if (MipCount != levels.Count)
            Allocate((uint)levels.Count);

        byte[][] kept = new byte[MipCount][];
        for (uint mip = 0; mip < MipCount; mip++)
        {
            uint w = MipSize(_width, mip), h = MipSize(_height, mip);
            int size = (int)GraphicsFormats.DataSize(Format, w, h);
            ValidateByteCapacity(levels[(int)mip].Length, size, nameof(levels));
            kept[mip] = levels[(int)mip].AsSpan(0, size).ToArray();
            Upload(kept[mip], 0, 0, 0, w, h, 1, mip);
        }

        _expandedLevels = DecodesOnUpload ? kept : null;
    }

    /// <summary>Reads the entire <see cref="Texture2D"/> back, its first row at V 0. Waits for the GPU.</summary>
    public unsafe void GetDataPtr(void* ptr)
    {
        EnsureLoaded();
        byte[] data = GetMipData(0);
        data.AsSpan().CopyTo(new Span<byte>(ptr, data.Length));
    }

    /// <summary>Reads the entire <see cref="Texture2D"/> back, its first row at V 0. Waits for the GPU, zeroes headless.</summary>
    /// <typeparam name="T">A struct with the same format as this <see cref="Texture2D"/>'s pixels.</typeparam>
    public void GetData<T>(Memory<T> data) where T : unmanaged
    {
        EnsureLoaded();
        Span<byte> target = MemoryMarshal.AsBytes(data.Span);
        ValidateByteCapacity(target.Length, GetSize(), nameof(data));
        byte[] read = GetMipData(0);
        read.AsSpan(0, Math.Min(read.Length, target.Length)).CopyTo(target);
    }

    /// <summary>Reads one mip level back, its first row at V 0. Waits for the GPU, zeroes headless.</summary>
    public byte[] GetMipData(uint mip)
    {
        EnsureLoaded();
        byte[] data = new byte[GraphicsFormats.DataSize(Format, MipSize(_width, mip), MipSize(_height, mip))];
        byte[] read = _expandedLevels != null && mip < _expandedLevels.Length ? _expandedLevels[mip] : Download(mip);
        read.AsSpan(0, Math.Min(read.Length, data.Length)).CopyTo(data);
        return data;
    }

    /// <summary>
    /// One mip level as RGBA8, compressed blocks expanded on the CPU and R8 repeated into grey, its first row at V 0.
    /// Null for formats holding other channel layouts. Waits for the GPU.
    /// </summary>
    public byte[]? GetMipRgba8(uint mip = 0)
    {
        EnsureLoaded();
        if (Format == TextureFormat.RGBA8) return GetMipData(mip);
        if (Format == TextureFormat.R8) return GreyFromRed(GetMipData(mip));
        if (!GraphicsFormats.IsCompressed(Format)) return null;
        return Aperture.TextureDecoder.Decode(GetMipData(mip), (int)MipSize(_width, mip), (int)MipSize(_height, mip), GraphicsFormats.ToAperture(Format, Srgb));
    }

    private static byte[] GreyFromRed(byte[] red)
    {
        byte[] rgba = new byte[red.Length * 4];
        for (int i = 0; i < red.Length; i++)
        {
            rgba[i * 4] = rgba[(i * 4) + 1] = rgba[(i * 4) + 2] = red[i];
            rgba[(i * 4) + 3] = 255;
        }
        return rgba;
    }

    /// <summary>Bytes needed to hold this texture's level 0, and so the size of a readback buffer.</summary>
    public int GetSize()
    {
        EnsureLoaded();
        return (int)GraphicsFormats.DataSize(Format, Width, Height);
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
        => (long)((GraphicsFormats.DataSize(FormatUnchecked, _width, _height)
            + (_expandedLevels != null ? GraphicsFormats.DataSize(TextureFormat.RGBA8, _width, _height) : 0))
            * (MipCountUnchecked > 1 ? 4.0 / 3.0 : 1.0));

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
    public static DecodeOptions DecodeToUploadLayout => new()
    {
        TargetPixelFormat = PixelFormat.Rgba8,
        // Image textures hold their bottom row first, so UV (0, 0) is the image's lower left as everywhere in Prowl
        FlipVertically = true,
        RowAlignment = 4,
    };

    /// <summary>
    /// Creates a <see cref="Texture2D"/> from an already decoded Rgba8 image. With mipmaps it filters Trilinear,
    /// without Bilinear. sRGB marks it as colour, decoded when sampled, off for normal maps and masks. A compressed
    /// <paramref name="format"/> filters the mips on the CPU and compresses every level at <paramref name="quality"/>.
    /// </summary>
    public static Texture2D FromImage(Aperture.Image image, bool mipmaps = false, bool srgb = true,
        TextureFormat format = TextureFormat.RGBA8, TextureEncodeQuality quality = TextureEncodeQuality.Balanced)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.PixelFormat != PixelFormat.Rgba8)
            throw new ArgumentException(TextureFormatMustBeRgba8Error, nameof(image));

        if (GraphicsFormats.IsCompressed(format))
        {
            byte[] pixels = new byte[image.Width * image.Height * 4];
            image.RootFrame.CopyTo(pixels, image.Width * 4);
            List<byte[]> levels = mipmaps
                ? TextureProcessing.BuildMipChain(pixels, image.Width, image.Height, new MipChainOptions(srgb))
                : [pixels];
            return FromMipChain(levels, image.Width, image.Height, srgb, format, quality);
        }

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

    /// <summary>
    /// Creates a <see cref="Texture2D"/> from an RGBA8 mip chain, level 0 first, stored as <paramref name="format"/>.
    /// Compressed formats encode every level at <paramref name="quality"/>, R8 keeps red. With more than one level it
    /// filters Trilinear.
    /// </summary>
    public static Texture2D FromMipChain(IReadOnlyList<byte[]> levels, int width, int height, bool srgb, TextureFormat format,
        TextureEncodeQuality quality = TextureEncodeQuality.Balanced)
    {
        if (levels.Count == 0)
            throw new ArgumentException("A mip chain needs at least one level.", nameof(levels));
        if (format != TextureFormat.RGBA8 && format != TextureFormat.R8 && !GraphicsFormats.IsCompressed(format))
            throw new NotSupportedException($"An RGBA8 mip chain cannot be stored as {format}.");

        Texture2D texture = new((uint)width, (uint)height, format, false, srgb);
        try
        {
            TextureEncodeOptions options = new() { Quality = quality };
            byte[][] stored = new byte[levels.Count][];

            for (int mip = 0; mip < levels.Count; mip++)
            {
                int w = Math.Max(1, width >> mip), h = Math.Max(1, height >> mip);
                stored[mip] = format switch
                {
                    TextureFormat.RGBA8 => levels[mip],
                    TextureFormat.R8 => TextureProcessing.Red(levels[mip]),
                    _ => TextureEncoder.Encode(levels[mip], w, h, GraphicsFormats.ToAperture(format, srgb), options),
                };
            }

            texture.SetMipChain(stored);
            if (levels.Count > 1) texture.Filter = FilterMode.Trilinear;
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

/// <summary>How <see cref="TextureProcessing.BuildMipChain"/> filters each level.</summary>
/// <param name="Srgb">Colour averaged as light rather than as stored values.</param>
/// <param name="NormalMap">Texels are tangent space normals, renormalized after averaging.</param>
/// <param name="CoverageCutoff">Alpha test threshold whose coverage every level keeps, null to leave alpha alone.</param>
public readonly record struct MipChainOptions(bool Srgb, bool NormalMap = false, float? CoverageCutoff = null);

/// <summary>
/// The CPU image work an import does before storing a texture: sizing, normal maps, alpha and mip chains. Every image
/// is RGBA8 with its bottom row first, as textures hold them.
/// </summary>
public static class TextureProcessing
{
    private static readonly float[] SrgbToLinear = BuildSrgbTable();

    /// <summary>Whether any pixel is less than opaque.</summary>
    public static bool HasAlpha(ReadOnlySpan<byte> pixels)
    {
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 255) return true;
        return false;
    }

    /// <summary>Halves the image until neither side exceeds <paramref name="maxSize"/>.</summary>
    public static byte[] FitMaxSize(byte[] pixels, ref int width, ref int height, int maxSize, bool srgb)
    {
        while (width > maxSize || height > maxSize)
            pixels = Downsample(pixels, ref width, ref height, srgb);
        return pixels;
    }

    /// <summary>
    /// Halves an image with a two by two box. At odd sizes the last row or column is left out, as the mip sizes round
    /// down. sRGB colour is averaged as light rather than as stored values, as the GPU does for an sRGB mip chain.
    /// </summary>
    public static byte[] Downsample(byte[] source, ref int width, ref int height, bool srgb)
    {
        int sourceWidth = width, sourceHeight = height;
        width = Math.Max(1, width / 2);
        height = Math.Max(1, height / 2);
        byte[] result = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int y0 = Math.Min(y * 2, sourceHeight - 1), y1 = Math.Min((y * 2) + 1, sourceHeight - 1);
            for (int x = 0; x < width; x++)
            {
                int x0 = Math.Min(x * 2, sourceWidth - 1), x1 = Math.Min((x * 2) + 1, sourceWidth - 1);
                int a = ((y0 * sourceWidth) + x0) * 4, b = ((y0 * sourceWidth) + x1) * 4;
                int c = ((y1 * sourceWidth) + x0) * 4, d = ((y1 * sourceWidth) + x1) * 4;
                int to = ((y * width) + x) * 4;

                for (int channel = 0; channel < 4; channel++)
                {
                    if (srgb && channel < 3)
                    {
                        float light = SrgbToLinear[source[a + channel]] + SrgbToLinear[source[b + channel]]
                            + SrgbToLinear[source[c + channel]] + SrgbToLinear[source[d + channel]];
                        result[to + channel] = LinearToSrgb(light * 0.25f);
                    }
                    else
                    {
                        result[to + channel] = (byte)((source[a + channel] + source[b + channel] + source[c + channel] + source[d + channel] + 2) >> 2);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Every level from <paramref name="level0"/> down to one pixel, each filtered from the one above.</summary>
    public static List<byte[]> BuildMipChain(byte[] level0, int width, int height, MipChainOptions options)
    {
        List<byte[]> levels = [level0];
        float coverage = options.CoverageCutoff is float cutoff ? Coverage(level0, cutoff, 1f) : 0f;
        byte[] level = level0;

        while (width > 1 || height > 1)
        {
            level = Downsample(level, ref width, ref height, options.Srgb && !options.NormalMap);
            if (options.NormalMap) Renormalize(level);
            if (options.CoverageCutoff is float threshold) KeepCoverage(level, threshold, coverage);
            levels.Add(level);
        }

        return levels;
    }

    /// <summary>Rebuilds each normal from its X and Y at unit length, as the shaders read it, which averaging shortens.</summary>
    public static void Renormalize(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            float x = (pixels[i] / 127.5f) - 1f, y = (pixels[i + 1] / 127.5f) - 1f;
            float z = MathF.Sqrt(MathF.Max(0f, 1f - (x * x) - (y * y)));
            float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
            if (length < 1e-6f) { x = 0; y = 0; z = 1; length = 1; }
            pixels[i] = Encode(x / length);
            pixels[i + 1] = Encode(y / length);
            pixels[i + 2] = Encode(z / length);
        }
    }

    /// <summary>
    /// Replaces the image with the normals of its brightness as a height field, tiling at the edges. Smooth takes the
    /// slope across three by three texels, sharp across the nearest two.
    /// </summary>
    public static void NormalsFromHeight(byte[] pixels, int width, int height, float bumpiness, bool smooth)
    {
        float[] heights = new float[width * height];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = ((pixels[i * 4] * 0.2126f) + (pixels[(i * 4) + 1] * 0.7152f) + (pixels[(i * 4) + 2] * 0.0722f)) / 255f;

        float H(int x, int y) => heights[(((y % height) + height) % height * width) + (((x % width) + width) % width)];
        float strength = bumpiness * 16f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx, dy;
                if (smooth)
                {
                    dx = ((H(x + 1, y - 1) + (2 * H(x + 1, y)) + H(x + 1, y + 1)) - (H(x - 1, y - 1) + (2 * H(x - 1, y)) + H(x - 1, y + 1))) / 8f;
                    dy = ((H(x - 1, y + 1) + (2 * H(x, y + 1)) + H(x + 1, y + 1)) - (H(x - 1, y - 1) + (2 * H(x, y - 1)) + H(x + 1, y - 1))) / 8f;
                }
                else
                {
                    dx = (H(x + 1, y) - H(x - 1, y)) * 0.5f;
                    dy = (H(x, y + 1) - H(x, y - 1)) * 0.5f;
                }

                float nx = -dx * strength, ny = -dy * strength;
                float length = MathF.Sqrt((nx * nx) + (ny * ny) + 1f);
                int at = ((y * width) + x) * 4;
                pixels[at] = Encode(nx / length);
                pixels[at + 1] = Encode(ny / length);
                pixels[at + 2] = Encode(1f / length);
                pixels[at + 3] = 255;
            }
        }
    }

    /// <summary>Inverts green, turning a Y down normal map into the Y up one Prowl reads.</summary>
    public static void FlipGreen(byte[] pixels)
    {
        for (int i = 1; i < pixels.Length; i += 4)
            pixels[i] = (byte)(255 - pixels[i]);
    }

    /// <summary>Sets alpha to the brightness of each pixel.</summary>
    public static void AlphaFromGrayscale(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
            pixels[i + 3] = (byte)(((pixels[i] * 54) + (pixels[i + 1] * 183) + (pixels[i + 2] * 19) + 128) >> 8);
    }

    /// <summary>Makes every pixel opaque.</summary>
    public static void ClearAlpha(byte[] pixels)
    {
        for (int i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
    }

    /// <summary>Moves one channel into red, alpha when <paramref name="fromAlpha"/>, leaving the rest black and opaque.</summary>
    public static void ToSingleChannel(byte[] pixels, bool fromAlpha)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = fromAlpha ? pixels[i + 3] : pixels[i];
            pixels[i + 1] = 0;
            pixels[i + 2] = 0;
            pixels[i + 3] = 255;
        }
    }

    /// <summary>The red channel of every pixel.</summary>
    public static byte[] Red(byte[] pixels)
    {
        byte[] red = new byte[pixels.Length / 4];
        for (int i = 0; i < red.Length; i++)
            red[i] = pixels[i * 4];
        return red;
    }

    /// <summary>
    /// Fills the colour of fully transparent pixels from their visible neighbours, so filtering and compression across
    /// an edge do not pull in whatever colour the invisible pixels held. Alpha is untouched.
    /// </summary>
    public static void DilateTransparent(byte[] pixels, int width, int height)
    {
        int count = width * height;
        bool[] known = new bool[count];
        int unknown = 0;
        long r = 0, g = 0, b = 0, visible = 0;

        for (int i = 0; i < count; i++)
        {
            known[i] = pixels[(i * 4) + 3] != 0;
            if (!known[i]) { unknown++; continue; }
            r += pixels[i * 4];
            g += pixels[(i * 4) + 1];
            b += pixels[(i * 4) + 2];
            visible++;
        }

        if (unknown == 0 || visible == 0) return;

        bool[] next = new bool[count];
        for (int pass = 0; pass < 16 && unknown > 0; pass++)
        {
            Array.Copy(known, next, count);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width) + x;
                    if (known[i]) continue;

                    int sr = 0, sg = 0, sb = 0, n = 0;
                    for (int oy = Math.Max(0, y - 1); oy <= Math.Min(height - 1, y + 1); oy++)
                    {
                        for (int ox = Math.Max(0, x - 1); ox <= Math.Min(width - 1, x + 1); ox++)
                        {
                            int j = (oy * width) + ox;
                            if (!known[j]) continue;
                            sr += pixels[j * 4];
                            sg += pixels[(j * 4) + 1];
                            sb += pixels[(j * 4) + 2];
                            n++;
                        }
                    }

                    if (n == 0) continue;
                    pixels[i * 4] = (byte)(sr / n);
                    pixels[(i * 4) + 1] = (byte)(sg / n);
                    pixels[(i * 4) + 2] = (byte)(sb / n);
                    next[i] = true;
                    unknown--;
                }
            }

            (known, next) = (next, known);
        }

        for (int i = 0; i < count; i++)
        {
            if (known[i]) continue;
            pixels[i * 4] = (byte)(r / visible);
            pixels[(i * 4) + 1] = (byte)(g / visible);
            pixels[(i * 4) + 2] = (byte)(b / visible);
        }
    }

    /// <summary>Fraction of pixels that pass an alpha test at <paramref name="cutoff"/> after alpha is scaled.</summary>
    private static float Coverage(byte[] pixels, float cutoff, float scale)
    {
        int passed = 0;
        for (int i = 3; i < pixels.Length; i += 4)
            if (MathF.Min(1f, pixels[i] / 255f * scale) >= cutoff) passed++;
        return passed / (float)(pixels.Length / 4);
    }

    /// <summary>Scales alpha so as many pixels pass the alpha test as in level 0, which plain averaging thins out.</summary>
    private static void KeepCoverage(byte[] pixels, float cutoff, float target)
    {
        float low = 0f, high = 8f;
        for (int step = 0; step < 16; step++)
        {
            float mid = (low + high) * 0.5f;
            if (Coverage(pixels, cutoff, mid) < target) low = mid;
            else high = mid;
        }

        float scale = (low + high) * 0.5f;
        for (int i = 3; i < pixels.Length; i += 4)
            pixels[i] = (byte)Math.Clamp((int)((pixels[i] * scale) + 0.5f), 0, 255);
    }

    private static byte Encode(float value) => (byte)Math.Clamp((int)(((value * 0.5f) + 0.5f) * 255f + 0.5f), 0, 255);

    private static float[] BuildSrgbTable()
    {
        float[] table = new float[256];
        for (int i = 0; i < 256; i++)
        {
            float v = i / 255f;
            table[i] = v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        }
        return table;
    }

    private static byte LinearToSrgb(float linear)
    {
        float v = linear <= 0.0031308f ? linear * 12.92f : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp((int)((v * 255f) + 0.5f), 0, 255);
    }
}
