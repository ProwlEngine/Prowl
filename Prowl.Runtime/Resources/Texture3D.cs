// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

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
                _white = new Texture3D(1, 1, 1, false, TextureImageFormat.Color4b);
                byte[] data = new byte[4] { 255, 255, 255, 255 }; // RGBA white
                _white.SetData(new Memory<byte>(data));
            }
            return _white;
        }
    }

    /// <summary>The width of this <see cref="Texture3D"/>.</summary>
    public uint Width { get; private set; }

    /// <summary>The height of this <see cref="Texture3D"/>.</summary>
    public uint Height { get; private set; }

    /// <summary>The depth of this <see cref="Texture3D"/>.</summary>
    public uint Depth { get; private set; }

    public Texture3D() : base(TextureType.Texture3D, TextureImageFormat.Color4b) { }

    /// <summary>
    /// Creates a <see cref="Texture3D"/> with the desired parameters but no image data.
    /// </summary>
    /// <param name="width">The width of the <see cref="Texture3D"/>.</param>
    /// <param name="height">The height of the <see cref="Texture3D"/>.</param>
    /// <param name="depth">The depth of the <see cref="Texture3D"/>.</param>
    /// <param name="generateMipmaps">Whether to generate mipmaps for this <see cref="Texture3D"/>.</param>
    /// <param name="imageFormat">The image format for this <see cref="Texture3D"/>.</param>
    public Texture3D(uint width, uint height, uint depth, bool generateMipmaps = false, TextureImageFormat imageFormat = TextureImageFormat.Color4b)
        : base(TextureType.Texture3D, imageFormat)
    {
        RecreateImage(width, height, depth); //This also binds the texture

        if (generateMipmaps)
            GenerateMipmaps();

        Graphics.SetTextureFilters(Handle, IsMipmapped ? DefaultMipmapMinFilter : DefaultMinFilter, DefaultMagFilter);
        MinFilter = IsMipmapped ? DefaultMipmapMinFilter : DefaultMinFilter;
        MagFilter = DefaultMagFilter;
    }

    /// <summary>
    /// Sets the data of a box region of the <see cref="Texture3D"/>.
    /// </summary>
    /// <param name="ptr">The pointer from which the pixel data will be read.</param>
    /// <param name="boxX">The X coordinate of the first voxel to write.</param>
    /// <param name="boxY">The Y coordinate of the first voxel to write.</param>
    /// <param name="boxZ">The Z coordinate of the first voxel to write.</param>
    /// <param name="boxWidth">The width of the box of voxels to write.</param>
    /// <param name="boxHeight">The height of the box of voxels to write.</param>
    /// <param name="boxDepth">The depth of the box of voxels to write.</param>
    public unsafe void SetDataPtr(void* ptr, int boxX, int boxY, int boxZ, uint boxWidth, uint boxHeight, uint boxDepth)
    {
        ValidateBoxOperation(boxX, boxY, boxZ, boxWidth, boxHeight, boxDepth);

        Graphics.TexSubImage3D(Handle, 0, boxX, boxY, boxZ, boxWidth, boxHeight, boxDepth, ptr);
    }

    /// <summary>
    /// Sets the data of a box region of the <see cref="Texture3D"/>.
    /// </summary>
    /// <typeparam name="T">A struct with the same format as this <see cref="Texture3D"/>'s voxels.</typeparam>
    /// <param name="data">A <see cref="Memory{T}"/> containing the new voxel data.</param>
    /// <param name="boxX">The X coordinate of the first voxel to write.</param>
    /// <param name="boxY">The Y coordinate of the first voxel to write.</param>
    /// <param name="boxZ">The Z coordinate of the first voxel to write.</param>
    /// <param name="boxWidth">The width of the box of voxels to write.</param>
    /// <param name="boxHeight">The height of the box of voxels to write.</param>
    /// <param name="boxDepth">The depth of the box of voxels to write.</param>
    public unsafe void SetData<T>(Memory<T> data, int boxX, int boxY, int boxZ, uint boxWidth, uint boxHeight, uint boxDepth) where T : unmanaged
    {
        ValidateBoxOperation(boxX, boxY, boxZ, boxWidth, boxHeight, boxDepth);
        if (data.Length < boxWidth * boxHeight * boxDepth)
            throw new ArgumentException("Not enough voxel data", nameof(data));

        fixed (void* ptr = data.Span)
            Graphics.TexSubImage3D(Handle, 0, boxX, boxY, boxZ, boxWidth, boxHeight, boxDepth, ptr);
    }

    /// <summary>
    /// Sets the data of the entire <see cref="Texture3D"/>.
    /// </summary>
    /// <typeparam name="T">A struct with the same format as this <see cref="Texture3D"/>'s voxels.</typeparam>
    /// <param name="data">A <see cref="Memory{T}"/> containing the new voxel data.</param>
    public void SetData<T>(Memory<T> data) where T : unmanaged
    {
        SetData(data, 0, 0, 0, Width, Height, Depth);
    }

    /// <summary>
    /// Gets the data of the entire <see cref="Texture3D"/>.
    /// </summary>
    /// <param name="ptr">The pointer to which the voxel data will be written.</param>
    public unsafe void GetDataPtr(void* ptr)
    {
        Graphics.GetTexImage(Handle, 0, ptr);
    }

    /// <summary>
    /// Gets the data of the entire <see cref="Texture3D"/>.
    /// </summary>
    /// <typeparam name="T">A struct with the same format as this <see cref="Texture3D"/>'s voxels.</typeparam>
    /// <param name="data">A <see cref="Memory{T}"/> in which to write the voxel data.</param>
    public unsafe void GetData<T>(Memory<T> data) where T : unmanaged
    {
        if (data.Length < Width * Height * Depth)
            throw new ArgumentException("Insufficient space to store the requested voxel data", nameof(data));

        fixed (void* ptr = data.Span)
            Graphics.GetTexImage(Handle, 0, ptr);
    }

    public int GetSize()
    {
        int size = (int)Width * (int)Height * (int)Depth;
        switch (ImageFormat)
        {
            case TextureImageFormat.Byte:
                return size * 1;
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
            case TextureImageFormat.Depth16f:
                return size * 2;
            case TextureImageFormat.Depth24f:
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
    /// <param name="rWrapMode">The wrap mode for the R (or texture-Z) coordinate.</param>
    public void SetWrapModes(TextureWrap sWrapMode, TextureWrap tWrapMode, TextureWrap rWrapMode)
    {
        Graphics.SetWrapS(Handle, sWrapMode);
        Graphics.SetWrapT(Handle, tWrapMode);
        Graphics.SetWrapR(Handle, rWrapMode);
    }

    /// <summary>
    /// Recreates this <see cref="Texture3D"/>'s image with a new size,
    /// resizing the <see cref="Texture3D"/> but losing the image data.
    /// </summary>
    /// <param name="width">The new width for the <see cref="Texture3D"/>.</param>
    /// <param name="height">The new height for the <see cref="Texture3D"/>.</param>
    /// <param name="depth">The new depth for the <see cref="Texture3D"/>.</param>
    public unsafe void RecreateImage(uint width, uint height, uint depth)
    {
        ValidateTextureSize(width, height, depth);

        Width = width;
        Height = height;
        Depth = depth;

        Graphics.TexImage3D(Handle, 0, Width, Height, Depth, (void*)0);
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
        compoundTag.Add("MinFilter", new((int)MinFilter));
        compoundTag.Add("MagFilter", new((int)MagFilter));
        compoundTag.Add("Wrap", new((int)WrapMode));
        Memory<byte> memory = new byte[GetSize()];
        GetData(memory);
        compoundTag.Add("Data", new(memory.ToArray()));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Width = value["Width"].UIntValue;
        Height = value["Height"].UIntValue;
        Depth = value["Depth"].UIntValue;
        bool isMipMapped = value["IsMipMapped"].BoolValue;
        TextureImageFormat imageFormat = (TextureImageFormat)value["ImageFormat"].IntValue;
        var MinFilter = (TextureMin)value["MinFilter"].IntValue;
        var MagFilter = (TextureMag)value["MagFilter"].IntValue;
        var Wrap = (TextureWrap)value["Wrap"].IntValue;

        Type[] param = new[] { typeof(uint), typeof(uint), typeof(uint), typeof(bool), typeof(TextureImageFormat) };
        object[] values = new object[] { Width, Height, Depth, false, imageFormat };
        typeof(Texture3D).GetConstructor(param).Invoke(this, values);

        Memory<byte> memory = value["Data"].ByteArrayValue;
        SetData(memory);

        if (isMipMapped)
            GenerateMipmaps();

        SetTextureFilters(MinFilter, MagFilter);
        SetWrapModes(Wrap, Wrap, Wrap);
    }
}
