// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A six-faced environment <see cref="Texture"/>. Faces are square and indexed 0..5: +X, -X, +Y, -Y, +Z, -Z, each
/// with its top row first. Supports a full mip chain, such as a GGX prefiltered environment. Made with a layer count it
/// is a cubemap array, a <c>samplerCubeArray</c> holding that many cubes, which lives only at runtime and is never saved.
/// </summary>
public sealed class Cubemap : Texture, ISerializable
{
    private uint _size;

    /// <summary>Edge length of each (square) face, in texels.</summary>
    public uint Size { get { EnsureLoaded(); return _size; } }

    /// <summary>How many cubes a cubemap array holds, 0 for a plain cubemap.</summary>
    public int Layers { get; private set; }

    public Cubemap() : base(TextureFormat.RGBA16Float, false, WrapMode.Clamp) { }

    /// <summary>Creates a cubemap with empty storage for all six faces, HDR half floats by default, with a full mip chain when mipmaps is set.</summary>
    public Cubemap(uint size, bool mipmaps = false, TextureFormat format = TextureFormat.RGBA16Float, bool srgb = false)
        : base(format, srgb, WrapMode.Clamp)
    {
        Recreate(size, mipmaps);
    }

    /// <summary>Creates a cubemap array of <paramref name="layers"/> cubes, each with <paramref name="mipLevels"/> levels.</summary>
    public Cubemap(uint size, int mipLevels, int layers, TextureFormat format = TextureFormat.RGBA16Float)
        : base(format, false, WrapMode.Clamp)
    {
        Layers = Math.Max(1, layers);
        Resize(size, (uint)Math.Max(1, mipLevels));
    }

    /// <summary>Allocates (or reallocates) all six faces and mip levels, discarding contents.</summary>
    public void Recreate(uint size, bool mipmaps)
    {
        EnsureLoaded();
        Resize(size, mipmaps ? FullChain(size, size) : 1);
    }

    private void Resize(uint size, uint mips)
    {
        if (size <= 0 || size > Graphics.MaxTextureSize)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Cubemap face size must be in (0, MaxTextureSize].");
        _size = size;
        Allocate(mips);
        Filter = MipCount > 1 ? FilterMode.Trilinear : FilterMode.Bilinear;
    }

    private protected override Graphite.TextureDescription Describe(uint mips)
    {
        Graphite.TextureDescription description = Graphite.TextureDescription.CreateCube(_size, mips, (uint)Math.Max(1, Layers), GpuFormat, Usage);
        if (Layers > 0) description.Type = Graphite.TextureType.TextureCube;
        return description;
    }

    private protected override uint FullMipCount => FullChain(_size, _size);

    private protected override uint GpuLayers => 6u * (uint)Math.Max(1, Layers);

    /// <summary>Edge length of a face at a mip level.</summary>
    public uint FaceSize(int mip) { EnsureLoaded(); return MipSize(_size, (uint)mip); }

    /// <summary>Total bytes for one face at the given mip level.</summary>
    public int FaceByteSize(int mip)
    {
        uint s = FaceSize(mip);
        return (int)(s * s) * GetBytesPerPixel(Format);
    }

    /// <summary>Upload pixel data into one face's mip level, rows top first.</summary>
    public void SetFaceData<T>(int face, Memory<T> data, int mip = 0, int layer = 0) where T : unmanaged
    {
        EnsureLoaded();
        uint size = FaceSize(mip);
        Upload(System.Runtime.InteropServices.MemoryMarshal.AsBytes<T>(data.Span)[..FaceByteSize(mip)], 0, 0, 0, size, size, 1, (uint)mip, (uint)(layer * 6 + face));
    }

    /// <summary>Reads one face's mip level back, rows top first. Waits for the GPU, zeroes headless.</summary>
    public void GetFaceData(int face, byte[] destination, int mip = 0, int layer = 0)
    {
        EnsureLoaded();
        byte[] data = Download((uint)mip, (uint)(layer * 6 + face));
        data.AsSpan(0, Math.Min(data.Length, destination.Length)).CopyTo(destination);
    }

    protected internal override long EstimateBytes()
        => (long)(6UL * (ulong)Math.Max(1, Layers) * _size * _size * (ulong)GetBytesPerPixel(FormatUnchecked) * (MipCountUnchecked > 1 ? 4.0 / 3.0 : 1.0));

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        SerializeHeader(compoundTag);
        compoundTag.Add("Size", new(Size));
        compoundTag.Add("MipLevels", new((int)MipCount));
        compoundTag.Add("Format", new((int)Format));
        compoundTag.Add("Srgb", new(Srgb));
        compoundTag.Add("Filter", new((int)Filter));
        compoundTag.Add("Anisotropy", new(Anisotropy));

        EchoObject faces = EchoObject.NewList();
        for (int face = 0; face < 6; face++)
        {
            for (int mip = 0; mip < MipCount; mip++)
            {
                byte[] dest = new byte[FaceByteSize(mip)];
                GetFaceData(face, dest, mip);
                faces.ListAdd(new(dest));
            }
        }
        compoundTag.Add("Faces", faces);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        uint size = value["Size"].UIntValue;
        int mipLevels = value["MipLevels"].IntValue;

        SetFormat((TextureFormat)value["Format"].IntValue, value.Get("Srgb")?.BoolValue ?? false);
        Resize(size, (uint)Math.Max(1, mipLevels));
        DeserializeHeader(value);

        EchoObject faces = value["Faces"];
        int idx = 0;
        for (int face = 0; face < 6; face++)
            for (int mip = 0; mip < mipLevels && idx < faces.Count; mip++, idx++)
                if (mip < MipCount) SetFaceData(face, new Memory<byte>(faces[idx].ByteArrayValue), mip);

        SetSampling((FilterMode)(value.Get("Filter")?.IntValue ?? (int)Filter), WrapMode.Clamp, WrapMode.Clamp, WrapMode.Clamp,
            value.Get("Anisotropy")?.UIntValue ?? 0, TextureBorderColor.TransparentBlack);
    }
}
