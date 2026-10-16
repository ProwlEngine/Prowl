// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Echo;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A <see cref="Texture"/> whose image has three dimensions (volumetric texture).
/// </summary>
public sealed class Texture3D : Texture, ISerializable
{
    private static Texture3D? _white;
    public static Texture3D White
    {
        get
        {
            if (_white == null || !_white.IsValid())
            {
                _white = new Texture3D(1, 1, 1);
                byte[] data = [255, 255, 255, 255]; // RGBA white
                _white.SetData(new Memory<byte>(data));
            }
            return _white;
        }
    }

    private uint _width, _height, _depth;

    public static Texture3D LoadDefault(DefaultTexture3D texture) => BuiltInAssets.Load<Texture3D>(BuiltInAssets.GuidFor(texture));

    /// <summary>
    /// Raw load of a default embedded 3D texture invoked by <see cref="BuiltInAssets"/> on first cache miss. They are
    /// stored as raw texels, slice after slice, since no image format holds a volume.
    /// </summary>
    internal static Texture3D ParseDefault(DefaultTexture3D texture)
    {
        (string fileName, uint size, TextureFormat format) = texture switch
        {
            DefaultTexture3D.CloudNoise => ("cloud_noise_3d.bin", 64u, TextureFormat.RG16),
            _ => throw new ArgumentException($"Unknown default 3D texture: {texture}")
        };

        using Stream stream = EmbeddedResources.GetStream($"Assets/Defaults/{fileName}");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);

        var result = new Texture3D(size, size, size, format, mipmaps: true) { Name = texture.ToString(), Filter = FilterMode.Trilinear };
        result.SetData(new Memory<byte>(bytes.ToArray()));
        result.GenerateMipmaps();
        return result;
    }

    /// <summary>The width of this <see cref="Texture3D"/>.</summary>
    public uint Width { get { EnsureLoaded(); return _width; } }

    /// <summary>The height of this <see cref="Texture3D"/>.</summary>
    public uint Height { get { EnsureLoaded(); return _height; } }

    /// <summary>The depth of this <see cref="Texture3D"/>.</summary>
    public uint Depth { get { EnsureLoaded(); return _depth; } }

    public Texture3D() : base(TextureFormat.RGBA8, false, WrapMode.Repeat) { }

    /// <summary>Creates a <see cref="Texture3D"/> with no image data, with a full mip chain to fill when mipmaps is set.</summary>
    public Texture3D(uint width, uint height, uint depth, TextureFormat format = TextureFormat.RGBA8, bool mipmaps = false, bool srgb = false)
        : base(format, srgb, WrapMode.Repeat)
    {
        Resize(width, height, depth, mipmaps ? FullChain(width, height, depth) : 1);
    }

    private void Resize(uint width, uint height, uint depth, uint mips)
    {
        ValidateTextureSize(width, height, depth);
        _width = width;
        _height = height;
        _depth = depth;
        Allocate(mips);
    }

    private protected override Graphite.TextureDescription Describe(uint mips)
        => Graphite.TextureDescription.Texture3D(_width, _height, _depth, mips, GpuFormat, Usage);

    private protected override uint FullMipCount => FullChain(_width, _height, _depth);

    /// <summary>Sets a box of the <see cref="Texture3D"/> from a pointer.</summary>
    public unsafe void SetDataPtr(void* ptr, int boxX, int boxY, int boxZ, uint boxWidth, uint boxHeight, uint boxDepth)
    {
        EnsureLoaded();
        ValidateBoxOperation(boxX, boxY, boxZ, boxWidth, boxHeight, boxDepth);
        Upload(new ReadOnlySpan<byte>(ptr, (int)(boxWidth * boxHeight * boxDepth * GetBytesPerPixel(Format))), (uint)boxX, (uint)boxY, (uint)boxZ, boxWidth, boxHeight, boxDepth);
    }

    /// <summary>Sets a box of the <see cref="Texture3D"/>.</summary>
    public void SetData<T>(Memory<T> data, int boxX, int boxY, int boxZ, uint boxWidth, uint boxHeight, uint boxDepth) where T : unmanaged
    {
        EnsureLoaded();
        ValidateBoxOperation(boxX, boxY, boxZ, boxWidth, boxHeight, boxDepth);
        ReadOnlySpan<byte> bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes<T>(data.Span);
        long required = (long)boxWidth * boxHeight * boxDepth * GetBytesPerPixel(Format);
        ValidateByteCapacity(bytes.Length, required, nameof(data));
        Upload(bytes[..(int)required], (uint)boxX, (uint)boxY, (uint)boxZ, boxWidth, boxHeight, boxDepth);
    }

    /// <summary>Sets the entire <see cref="Texture3D"/>.</summary>
    public void SetData<T>(Memory<T> data) where T : unmanaged
    {
        EnsureLoaded();
        SetData(data, 0, 0, 0, Width, Height, Depth);
    }

    /// <summary>Reads the entire <see cref="Texture3D"/> back. Waits for the GPU.</summary>
    public unsafe void GetDataPtr(void* ptr)
    {
        EnsureLoaded();
        byte[] data = Download();
        data.AsSpan().CopyTo(new Span<byte>(ptr, data.Length));
    }

    /// <summary>Reads the entire <see cref="Texture3D"/> back. Waits for the GPU, zeroes headless.</summary>
    public void GetData<T>(Memory<T> data) where T : unmanaged
    {
        EnsureLoaded();
        Span<byte> target = System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.Span);
        ValidateByteCapacity(target.Length, GetSize(), nameof(data));
        byte[] read = Download();
        read.AsSpan(0, Math.Min(read.Length, target.Length)).CopyTo(target);
    }

    /// <summary>Bytes needed to hold this texture's full image.</summary>
    public int GetSize()
    {
        EnsureLoaded();
        return (int)Width * (int)Height * (int)Depth * GetBytesPerPixel(Format);
    }

    /// <summary>Reads one mip level back. Waits for the GPU, zeroes headless.</summary>
    public byte[] GetMipData(uint mip)
    {
        EnsureLoaded();
        byte[] data = new byte[MipSize(_width, mip) * MipSize(_height, mip) * MipSize(_depth, mip) * GetBytesPerPixel(Format)];
        byte[] read = Download(mip);
        read.AsSpan(0, Math.Min(read.Length, data.Length)).CopyTo(data);
        return data;
    }

    /// <summary>Recreates this <see cref="Texture3D"/>'s image with a new size, losing the image data.</summary>
    public void RecreateImage(uint width, uint height, uint depth)
    {
        EnsureLoaded();
        Resize(width, height, depth, MipCount > 1 ? FullChain(width, height, depth) : 1);
    }

    private void ValidateTextureSize(uint width, uint height, uint depth)
    {
        if (width <= 0 || width > Graphics.MaxTextureSize)
            throw new ArgumentOutOfRangeException(nameof(width), width, nameof(width) + " must be in the range (0, " + nameof(Graphics.MaxTextureSize) + "]");

        if (height <= 0 || height > Graphics.MaxTextureSize)
            throw new ArgumentOutOfRangeException(nameof(height), height, nameof(height) + " must be in the range (0, " + nameof(Graphics.MaxTextureSize) + "]");

        if (depth <= 0 || depth > Graphics.MaxTextureSize)
            throw new ArgumentOutOfRangeException(nameof(depth), depth, nameof(depth) + " must be in the range (0, " + nameof(Graphics.MaxTextureSize) + "]");
    }

    /// <summary>
    /// Guards a buffer against what the driver will actually read or write. The element count alone says
    /// nothing: the GPU transfers <see cref="Texture.GetBytesPerPixel"/> bytes per voxel, so a byte buffer
    /// sized one-per-voxel for an RGBA texture is a quarter of what TexSubImage3D goes on to read.
    /// </summary>
    private void ValidateByteCapacity(long providedBytes, long requiredBytes, string paramName)
    {
        if (providedBytes < requiredBytes)
            throw new ArgumentException(
                $"Buffer holds {providedBytes} bytes but {Format} needs {requiredBytes} for this region.", paramName);
    }

    private void ValidateBoxOperation(int boxX, int boxY, int boxZ, uint boxWidth, uint boxHeight, uint boxDepth)
    {
        if (boxX < 0 || boxX >= Width)
            throw new ArgumentOutOfRangeException(nameof(boxX), boxX, nameof(boxX) + " must be in the range [0, " + nameof(Width) + ")");

        if (boxY < 0 || boxY >= Height)
            throw new ArgumentOutOfRangeException(nameof(boxY), boxY, nameof(boxY) + " must be in the range [0, " + nameof(Height) + ")");

        if (boxZ < 0 || boxZ >= Depth)
            throw new ArgumentOutOfRangeException(nameof(boxZ), boxZ, nameof(boxZ) + " must be in the range [0, " + nameof(Depth) + ")");

        if (boxWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(boxWidth), boxWidth, nameof(boxWidth) + " must be greater than 0");

        if (boxHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(boxHeight), boxHeight, nameof(boxHeight) + " must be greater than 0");

        if (boxDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(boxDepth), boxDepth, nameof(boxDepth) + " must be greater than 0");

        if (boxWidth > Width - boxX || boxHeight > Height - boxY || boxDepth > Depth - boxZ)
            throw new ArgumentOutOfRangeException("Specified box is outside of the texture's storage");
    }

    protected internal override long EstimateBytes()
        => (long)(_width * _height * _depth * (ulong)GetBytesPerPixel(FormatUnchecked) * (MipCountUnchecked > 1 ? 8.0 / 7.0 : 1.0));

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        SerializeHeader(compoundTag);
        compoundTag.Add("Width", new(Width));
        compoundTag.Add("Height", new(Height));
        compoundTag.Add("Depth", new(Depth));
        compoundTag.Add("Format", new((int)Format));
        compoundTag.Add("Srgb", new(Srgb));
        compoundTag.Add("Filter", new((int)Filter));
        compoundTag.Add("WrapU", new((int)WrapU));
        compoundTag.Add("WrapV", new((int)WrapV));
        compoundTag.Add("WrapW", new((int)WrapW));
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
        uint depth = value["Depth"].UIntValue;
        EchoObject mips = value["Mips"];

        SetFormat((TextureFormat)value["Format"].IntValue, value.Get("Srgb")?.BoolValue ?? false);
        Resize(width, height, depth, (uint)Math.Max(1, mips.Count));
        DeserializeHeader(value);

        int bpp = GetBytesPerPixel(Format);
        for (uint mip = 0; mip < MipCount && mip < mips.Count; mip++)
        {
            uint w = MipSize(width, mip), h = MipSize(height, mip), d = MipSize(depth, mip);
            byte[] data = mips[(int)mip].ByteArrayValue;
            ValidateByteCapacity(data.Length, (long)w * h * d * bpp, nameof(value));
            Upload(data.AsSpan(0, (int)(w * h * d * bpp)), 0, 0, 0, w, h, d, mip);
        }
        if (value.Get("GenerateMipmaps")?.BoolValue == true) GenerateMipmaps();

        SetSampling((FilterMode)(value.Get("Filter")?.IntValue ?? (int)FilterMode.Bilinear),
            (WrapMode)(value.Get("WrapU")?.IntValue ?? 0), (WrapMode)(value.Get("WrapV")?.IntValue ?? 0), (WrapMode)(value.Get("WrapW")?.IntValue ?? 0),
            value.Get("Anisotropy")?.UIntValue ?? 0, (TextureBorderColor)(value.Get("BorderColor")?.IntValue ?? 0));
    }
}
