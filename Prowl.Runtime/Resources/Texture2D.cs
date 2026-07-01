// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using ImageMagick;

using Prowl.Echo;
using Prowl.Graphite;

namespace Prowl.Runtime.Resources;


/// <summary>
/// A <see cref="Texture"/> whose image has two dimensions and support for multisampling.
/// </summary>
public sealed class Texture2D : Texture, ISerializable
{
    /// <summary>The width of this <see cref="Texture2D"/>.</summary>
    public uint Width { get; private set; }

    /// <summary>The height of this <see cref="Texture2D"/>.</summary>
    public uint Height { get; private set; }

    private bool _generateMipmaps;

    /// <summary>Extra usage flags OR'd into the texture (e.g. RenderTarget/DepthStencil for render targets).</summary>
    private TextureUsage _extraUsage;

    public Texture2D() : base(TextureType.Texture2D, PixelFormat.R8_G8_B8_A8_UNorm) { }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> usable as a framebuffer attachment with the given extra usage
    /// (RenderTarget for color targets, DepthStencil for depth targets).
    /// </summary>
    internal Texture2D(uint width, uint height, PixelFormat imageFormat, TextureUsage extraUsage)
        : base(TextureType.Texture2D, imageFormat)
    {
        _extraUsage = extraUsage;
        RecreateImage(width, height);
        SetTextureFilters(DefaultFilter);
    }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> with the desired parameters but no image data.
    /// </summary>
    /// <param name="width">The width of the <see cref="Texture2D"/>.</param>
    /// <param name="height">The height of the <see cref="Texture2D"/>.</param>
    /// <param name="generateMipmaps">Whether to generate mipmaps for this <see cref="Texture2D"/>.</param>
    /// <param name="imageFormat">The image format for this <see cref="Texture2D"/>.</param>
    public Texture2D(uint width, uint height, bool generateMipmaps = false, PixelFormat imageFormat = PixelFormat.R8_G8_B8_A8_UNorm)
        : base(TextureType.Texture2D, imageFormat)
    {
        _generateMipmaps = generateMipmaps;
        RecreateImage(width, height);

        SetTextureFilters(_generateMipmaps ? DefaultMipmapFilter : DefaultFilter);
    }

    /// <summary>
    /// Sets the data of an area of the <see cref="Texture2D"/>.
    /// </summary>
    public unsafe void SetDataPtr(void* ptr, int rectX, int rectY, uint rectWidth, uint rectHeight)
    {
        ValidateRectOperation(rectX, rectY, rectWidth, rectHeight);

        uint bytes = rectWidth * rectHeight * ImageFormat.GetSizeInBytes();
        Graphics.Device.UpdateTexture(Handle, (nint)ptr, bytes,
            (uint)rectX, (uint)rectY, 0, rectWidth, rectHeight, 1, 0, 0);
    }

    /// <summary>
    /// Sets the data of an area of the <see cref="Texture2D"/>.
    /// </summary>
    public unsafe void SetData<T>(Memory<T> data, int rectX, int rectY, uint rectWidth, uint rectHeight) where T : unmanaged
    {
        ValidateRectOperation(rectX, rectY, rectWidth, rectHeight);
        if (data.Length < rectWidth * rectHeight)
            throw new ArgumentException("Not enough pixel data", nameof(data));

        fixed (void* ptr = data.Span)
            SetDataPtr(ptr, rectX, rectY, rectWidth, rectHeight);
    }

    /// <summary>
    /// Sets the data of the entire <see cref="Texture2D"/>.
    /// </summary>
    public void SetData<T>(Memory<T> data) where T : unmanaged
    {
        SetData(data, 0, 0, Width, Height);
    }

    /// <summary>
    /// Gets the data of the entire <see cref="Texture2D"/>.
    /// </summary>
    public unsafe void GetDataPtr(void* ptr)
    {
        throw new NotSupportedException("Texture read-back has not yet been ported to Prowl.Graphite (requires a staging texture).");
    }

    /// <summary>
    /// Gets the data of the entire <see cref="Texture2D"/>.
    /// </summary>
    public unsafe void GetData<T>(Memory<T> data) where T : unmanaged
    {
        Debug.LogWarning("Texture read-back has not yet been ported to Prowl.Graphite (requires a staging texture)");
        // throw new NotSupportedException("Texture read-back has not yet been ported to Prowl.Graphite (requires a staging texture).");
    }

    public int GetSize() => (int)(Width * Height * ImageFormat.GetSizeInBytes());

    /// <summary>
    /// Recreates this <see cref="Texture2D"/>'s image with a new size,
    /// resizing the <see cref="Texture2D"/> but losing the image data.
    /// </summary>
    public unsafe void RecreateImage(uint width, uint height)
    {
        ValidateTextureSize(width, height);

        Width = width;
        Height = height;

        // Old handle may still be in flight; defer its disposal to avoid a use-after-free stall.
        Graphics.CancelMipmapGeneration(Handle);
        Graphics.DisposeDeferred(Handle);

        uint mipLevels = _generateMipmaps ? ComputeMipLevels(width, height) : 1;
        TextureUsage usage = TextureUsage.Sampled | _extraUsage;

        if (_generateMipmaps)
            usage |= TextureUsage.GenerateMipmaps;

        Handle = Graphics.Device.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(width, height, mipLevels, 1, ImageFormat, usage));
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

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        compoundTag.Add("Width", new(Width));
        compoundTag.Add("Height", new(Height));
        compoundTag.Add("IsMipMapped", new(IsMipmapped));
        compoundTag.Add("ImageFormat", new((int)ImageFormat));
        compoundTag.Add("Filter", new((int)Filter));
        compoundTag.Add("AddressU", new((int)AddressModeU));
        compoundTag.Add("AddressV", new((int)AddressModeV));
        Memory<byte> memory = new byte[GetSize()];
        GetData(memory);
        compoundTag.Add("Data", new(memory.ToArray()));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Width = value["Width"].UIntValue;
        Height = value["Height"].UIntValue;
        bool isMipMapped = value["IsMipMapped"].BoolValue;
        PixelFormat imageFormat = (PixelFormat)value["ImageFormat"].IntValue;
        var filter = (SamplerFilter)value["Filter"].IntValue;
        var addressU = (SamplerAddressMode)value["AddressU"].IntValue;
        var addressV = (SamplerAddressMode)value["AddressV"].IntValue;

        Type[] param = new[] { typeof(uint), typeof(uint), typeof(bool), typeof(PixelFormat) };
        object[] values = new object[] { Width, Height, isMipMapped, imageFormat };
        typeof(Texture2D).GetConstructor(param).Invoke(this, values);

        Memory<byte> memory = value["Data"].ByteArrayValue;
        SetData(memory);

        if (isMipMapped)
            GenerateMipmaps();

        SetTextureFilters(filter);
        SetWrapModes(addressU, addressV);
    }

    #region ImageMagick integration


    /// <summary>
    /// Creates a <see cref="Texture2D"/> from a <see cref="MagickImage"/>.
    /// </summary>
    public static Texture2D FromImage(MagickImage image, bool generateMipmaps = false)
    {
        ArgumentNullException.ThrowIfNull(image);

        image.Flip();

        PixelFormat format = PixelFormat.R16_G16_B16_A16_UNorm;
        image.ColorSpace = ColorSpace.sRGB;
        image.ColorType = ColorType.TrueColorAlpha;

        nint pixels = image.GetPixelsUnsafe().GetAreaPointer(0, 0, image.Width, image.Height);

        Texture2D texture = new(image.Width, image.Height, generateMipmaps, format);
        try
        {
            unsafe
            {
                texture.SetDataPtr((void*)pixels, 0, 0, image.Width, image.Height);
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
    public static Texture2D FromStream(Stream stream, bool generateMipmaps = false)
    {
        var image = new MagickImage(stream);
        return FromImage(image, generateMipmaps);
    }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> by loading an image from a file.
    /// </summary>
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
        Texture2D texture = FromFile(filePath, generateMipmaps);
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
    /// Get the shared instance of a default embedded texture.
    /// </summary>
    public static Texture2D LoadDefault(DefaultTexture texture)
    {
        if (BuiltInAssets.Get(BuiltInAssets.GuidFor(texture)) is Texture2D cached)
            return cached;
        return ParseDefault(texture);
    }

    /// <summary>
    /// Raw load of a default embedded texture invoked by <see cref="BuiltInAssets"/>
    /// on first cache miss. Public callers should use <see cref="LoadDefault"/>.
    /// </summary>
    internal static Texture2D ParseDefault(DefaultTexture texture)
    {
        string fileName = texture switch
        {
            DefaultTexture.White => "default_white.png",
            DefaultTexture.Gray18 => "default_gray18.png",
            DefaultTexture.Normal => "default_normal.png",
            DefaultTexture.Surface => "default_surface.png",
            DefaultTexture.Emission => "default_emission.png",
            DefaultTexture.Grid => "grid.png",
            DefaultTexture.Handle => "handle_ui.png",
            DefaultTexture.Noise => "noise.png",
            DefaultTexture.IconCamera => "icon_camera.png",
            DefaultTexture.IconLight => "icon_light.png",
            _ => throw new ArgumentException($"Unknown default texture: {texture}")
        };

        string resourcePath = $"Assets/Defaults/{fileName}";
        using Stream stream = EmbeddedResources.GetStream(resourcePath);
        return FromStream(stream, true);
    }

    internal const string ImageNotContiguousError = "To load/save an image, it's backing memory must be contiguous. Consider using smaller image sizes or changing your ImageSharp memory allocation settings to allow larger buffers.";

    internal const string ImageSizeMustMatchTextureSizeError = "The size of the image must match the size of the texture";

    internal const string TextureFormatMustBeColor4bError = "The texture's format must be Color4b (RGBA)";

    #endregion
}
