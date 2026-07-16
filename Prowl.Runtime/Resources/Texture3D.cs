// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Graphite;

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
                _white = new Texture3D(1, 1, 1, false, PixelFormat.R8_G8_B8_A8_UNorm);
                byte[] data = new byte[4] { 255, 255, 255, 255 }; // RGBA white
                _white.SetData(new Memory<byte>(data));
            }
            return _white;
        }
    }

    private uint _width, _height, _depth;

    /// <summary>The width of this <see cref="Texture3D"/>.</summary>
    public uint Width { get { EnsureNotDisposed(); return _width; } private set => _width = value; }

    /// <summary>The height of this <see cref="Texture3D"/>.</summary>
    public uint Height { get { EnsureNotDisposed(); return _height; } private set => _height = value; }

    /// <summary>The depth of this <see cref="Texture3D"/>.</summary>
    public uint Depth { get { EnsureNotDisposed(); return _depth; } private set => _depth = value; }

    private bool _generateMipmaps;

    public Texture3D() : base(TextureType.Texture3D, PixelFormat.R8_G8_B8_A8_UNorm) { }

    /// <summary>
    /// Creates a <see cref="Texture3D"/> with the desired parameters but no image data.
    /// </summary>
    public Texture3D(uint width, uint height, uint depth, bool generateMipmaps = false, PixelFormat imageFormat = PixelFormat.R8_G8_B8_A8_UNorm)
        : base(TextureType.Texture3D, imageFormat)
    {
        _generateMipmaps = generateMipmaps;
        RecreateImage(width, height, depth);

        SetTextureFilters(_generateMipmaps ? DefaultMipmapFilter : DefaultFilter);
    }

    /// <summary>
    /// Sets the data of a box region of the <see cref="Texture3D"/>.
    /// </summary>
    public unsafe void SetDataPtr(void* ptr, int boxX, int boxY, int boxZ, uint boxWidth, uint boxHeight, uint boxDepth)
    {
        EnsureNotDisposed();
        ValidateBoxOperation(boxX, boxY, boxZ, boxWidth, boxHeight, boxDepth);

        uint bytes = boxWidth * boxHeight * boxDepth * ImageFormat.GetSizeInBytes();
        Graphics.Device.UpdateTexture(Handle, (nint)ptr, bytes,
            (uint)boxX, (uint)boxY, (uint)boxZ, boxWidth, boxHeight, boxDepth, 0, 0);
    }

    /// <summary>
    /// Sets the data of a box region of the <see cref="Texture3D"/>.
    /// </summary>
    public unsafe void SetData<T>(Memory<T> data, int boxX, int boxY, int boxZ, uint boxWidth, uint boxHeight, uint boxDepth) where T : unmanaged
    {
        EnsureNotDisposed();
        ValidateBoxOperation(boxX, boxY, boxZ, boxWidth, boxHeight, boxDepth);
        if (data.Length < boxWidth * boxHeight * boxDepth)
            throw new ArgumentException("Not enough voxel data", nameof(data));

        fixed (void* ptr = data.Span)
            SetDataPtr(ptr, boxX, boxY, boxZ, boxWidth, boxHeight, boxDepth);
    }

    /// <summary>
    /// Sets the data of the entire <see cref="Texture3D"/>.
    /// </summary>
    public void SetData<T>(Memory<T> data) where T : unmanaged
    {
        EnsureNotDisposed();
        SetData(data, 0, 0, 0, Width, Height, Depth);
    }

    /// <summary>
    /// Gets the data of the entire <see cref="Texture3D"/>. A raw pointer's lifetime can't be
    /// guaranteed past this call, so unlike <see cref="GetData{T}"/> this only works outside a
    /// frame (see <see cref="ReadBackSubresource"/>); if a frame is currently open it logs a
    /// warning and leaves <paramref name="ptr"/> untouched.
    /// </summary>
    public unsafe void GetDataPtr(void* ptr)
    {
        if (Graphics.CurrentFrame != null)
        {
            Debug.LogWarning($"Cannot read back '{Name}' into a raw pointer while a frame is being " +
                "recorded; the pointer isn't guaranteed to stay valid. Use GetData<T> instead, or call outside a frame.");
            return;
        }

        TextureDescription stagingDescription =
            TextureDescription.Texture3D(Width, Height, Depth, 1, ImageFormat, TextureUsage.Staging);
        nint destination = (nint)ptr;
        uint destSize = (uint)GetSize();
        uint width = Width, height = Height, depth = Depth;

        ReadBackSubresource(stagingDescription, width, height, depth, 0, 0,
            mapped => CopyMappedRegion(mapped, destination, destSize, width, height, depth));
    }

    /// <summary>
    /// Gets the data of the entire <see cref="Texture3D"/>. See <see cref="ReadBackSubresource"/>:
    /// returns true if <paramref name="data"/> was filled synchronously before returning, or false
    /// if the read was queued and <paramref name="data"/> will be filled once <paramref name="onComplete"/>
    /// fires on a later tick (both null-safe to ignore if the caller doesn't need to know).
    /// </summary>
    public unsafe bool GetData<T>(Memory<T> data, Action onComplete = null) where T : unmanaged
    {
        TextureDescription stagingDescription =
            TextureDescription.Texture3D(Width, Height, Depth, 1, ImageFormat, TextureUsage.Staging);
        uint destSize = (uint)GetSize();
        uint width = Width, height = Height, depth = Depth;

        return ReadBackSubresource(stagingDescription, width, height, depth, 0, 0, mapped =>
        {
            using var handle = data.Pin();
            CopyMappedRegion(mapped, (nint)handle.Pointer, destSize, width, height, depth);
        }, onComplete);
    }

    public int GetSize() => (int)(Width * Height * Depth * ImageFormat.GetSizeInBytes());

    /// <summary>
    /// Recreates this <see cref="Texture3D"/>'s image with a new size,
    /// resizing the <see cref="Texture3D"/> but losing the image data.
    /// </summary>
    public unsafe void RecreateImage(uint width, uint height, uint depth)
    {
        EnsureNotDisposed();
        ValidateTextureSize(width, height, depth);

        Width = width;
        Height = height;
        Depth = depth;

        Handle?.Dispose();

        uint mipLevels = _generateMipmaps ? ComputeMipLevels(width, height) : 1;
        TextureUsage usage = TextureUsage.Sampled;
        if (_generateMipmaps)
            usage |= TextureUsage.GenerateMipmaps;

        Handle = Graphics.Device.ResourceFactory.CreateTexture(
            TextureDescription.Texture3D(width, height, depth, mipLevels, ImageFormat, usage));
        Handle.Name = Name;
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

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        compoundTag.Add("Width", new(Width));
        compoundTag.Add("Height", new(Height));
        compoundTag.Add("Depth", new(Depth));
        compoundTag.Add("IsMipMapped", new(IsMipmapped));
        compoundTag.Add("ImageFormat", new((int)ImageFormat));
        compoundTag.Add("Filter", new((int)Filter));
        compoundTag.Add("AddressU", new((int)AddressModeU));
        Memory<byte> memory = new byte[GetSize()];
        if (!GetData(memory))
            Debug.LogWarning($"'{Name}' was serialized while a frame was being recorded; its pixel data may be stale.");
        compoundTag.Add("Data", new(memory.ToArray()));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Width = value["Width"].UIntValue;
        Height = value["Height"].UIntValue;
        Depth = value["Depth"].UIntValue;
        bool isMipMapped = value["IsMipMapped"].BoolValue;
        PixelFormat imageFormat = (PixelFormat)value["ImageFormat"].IntValue;
        var filter = (SamplerFilter)value["Filter"].IntValue;
        var address = (SamplerAddressMode)value["AddressU"].IntValue;

        Type[] param = new[] { typeof(uint), typeof(uint), typeof(uint), typeof(bool), typeof(PixelFormat) };
        object[] values = new object[] { Width, Height, Depth, isMipMapped, imageFormat };
        typeof(Texture3D).GetConstructor(param).Invoke(this, values);

        Memory<byte> memory = value["Data"].ByteArrayValue;
        SetData(memory);

        if (isMipMapped)
            GenerateMipmaps();

        SetTextureFilters(filter);
        SetWrapModes(address, address, address);
    }
}
