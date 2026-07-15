// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A six-faced environment <see cref="Texture"/>. Faces are square and indexed 0..5 in GL
/// order: +X, -X, +Y, -Y, +Z, -Z. Supports a full mip chain (used by reflection probes to
/// store a roughness-convolved, GGX-prefiltered environment) and can be rendered into per
/// face and per mip via <see cref="GetFaceTarget"/>.
/// </summary>
public sealed class Cubemap : Texture, ISerializable
{
    private uint _size;
    private int _mipLevels;

    /// <summary>Edge length of each (square) face, in texels.</summary>
    public uint Size { get { EnsureNotDisposed(); return _size; } private set => _size = value; }

    /// <summary>Number of mip levels allocated (1 when no chain).</summary>
    public int MipLevels { get { EnsureNotDisposed(); return _mipLevels; } private set => _mipLevels = value; }

    // Render-target framebuffers are created lazily per (face, mip) and reused.
    private readonly Dictionary<int, GraphicsFrameBuffer> _faceTargets = new();
    // Shared depth buffer for capture (scene rendering into a face needs a depth test).
    private Texture2D? _captureDepth;

    public Cubemap() : base(TextureType.TextureCubeMap, TextureImageFormat.Short4) { }

    /// <summary>
    /// Creates a cubemap with empty storage for all six faces.
    /// </summary>
    /// <param name="size">Edge length of each face.</param>
    /// <param name="mipChain">Allocate a full mip chain (for prefiltered roughness levels).</param>
    /// <param name="imageFormat">Texel format. Defaults to RGBA16F (Short4) for HDR.</param>
    public Cubemap(uint size, bool mipChain = false, TextureImageFormat imageFormat = TextureImageFormat.Short4)
        : base(TextureType.TextureCubeMap, imageFormat)
    {
        Recreate(size, mipChain);
    }

    /// <summary>Allocates (or reallocates) all six faces and mip levels, discarding contents.</summary>
    public unsafe void Recreate(uint size, bool mipChain)
    {
        EnsureNotDisposed();
        ValidateSize(size);

        Size = size;
        MipLevels = mipChain ? MipCountFor(size) : 1;

        for (int face = 0; face < 6; face++)
            for (int mip = 0; mip < MipLevels; mip++)
                Graphics.TexImageCubeFace(Handle, face, mip, MipSize(mip), (void*)0);

        TextureMin min = MipLevels > 1 ? TextureMin.LinearMipmapLinear : TextureMin.Linear;
        Graphics.SetTextureFilters(Handle, min, TextureMag.Linear);
        MinFilter = min;
        MagFilter = TextureMag.Linear;
        SetWrapModes(TextureWrap.ClampToEdge);
    }

    public uint MipSize(int mip) { EnsureNotDisposed(); return Math.Max(1u, Size >> mip); }

    private static int MipCountFor(uint size)
    {
        int levels = 1;
        while (size > 1) { size >>= 1; levels++; }
        return levels;
    }

    public void SetWrapModes(TextureWrap wrap)
    {
        EnsureNotDisposed();
        Graphics.SetWrapS(Handle, wrap);
        Graphics.SetWrapT(Handle, wrap);
        Graphics.SetWrapR(Handle, wrap);
        WrapMode = wrap;
    }

    /// <summary>Bytes per texel for this cubemap's format.</summary>
    public int BytesPerPixel()
    {
        EnsureNotDisposed();
        switch (ImageFormat)
        {
            case TextureImageFormat.Color4b: return 4;
            case TextureImageFormat.Byte: return 1;
            case TextureImageFormat.Float: return 4;
            case TextureImageFormat.Float2: return 8;
            case TextureImageFormat.Float3: return 12;
            case TextureImageFormat.Float4: return 16;
            case TextureImageFormat.Short:
            case TextureImageFormat.UnsignedShort: return 2;
            case TextureImageFormat.Short2:
            case TextureImageFormat.UnsignedShort2: return 4;
            case TextureImageFormat.Short3:
            case TextureImageFormat.UnsignedShort3: return 6;
            case TextureImageFormat.Short4:
            case TextureImageFormat.UnsignedShort4: return 8;
            default: return 4;
        }
    }

    /// <summary>Total bytes for one face at the given mip level.</summary>
    public int FaceByteSize(int mip)
    {
        EnsureNotDisposed();
        uint s = MipSize(mip);
        return (int)(s * s) * BytesPerPixel();
    }

    /// <summary>Upload pixel data into one face's mip level.</summary>
    public unsafe void SetFaceData<T>(int face, Memory<T> data, int mip = 0) where T : unmanaged
    {
        EnsureNotDisposed();
        fixed (void* ptr = data.Span)
            Graphics.TexImageCubeFace(Handle, face, mip, MipSize(mip), ptr);
    }

    /// <summary>Read back one face's mip level into <paramref name="destination"/>.
    /// Blocks until the GPU read completes.</summary>
    public void GetFaceData(int face, byte[] destination, int mip = 0)
    {
        EnsureNotDisposed();
        Graphics.GetTexImageCubeFace(Handle, face, mip, destination);
    }

    /// <summary>A framebuffer that renders into one face at one mip level. With
    /// <paramref name="withDepth"/> it also attaches a shared depth buffer (sized to the
    /// full face) so scene geometry can depth-test during capture. Cached and reused;
    /// disposed with the cubemap.</summary>
    public GraphicsFrameBuffer GetFaceTarget(int face, int mip, bool withDepth = false)
    {
        EnsureNotDisposed();
        int key = (face * 64 + mip) * 2 + (withDepth ? 1 : 0);
        if (_faceTargets.TryGetValue(key, out var fb) && !fb.IsDisposed)
            return fb;

        uint s = MipSize(mip);

        GraphicsFrameBuffer.Attachment[] attachments;
        var color = new GraphicsFrameBuffer.Attachment
        {
            Texture = Handle,
            IsDepth = false,
            IsCubeFace = true,
            CubeFace = face,
            MipLevel = mip,
        };

        if (withDepth)
        {
            _captureDepth ??= new Texture2D(Size, Size, false, TextureImageFormat.Depth24f);
            attachments =
            [
                color,
                new GraphicsFrameBuffer.Attachment { Texture = _captureDepth.Handle, IsDepth = true },
            ];
        }
        else
        {
            attachments = [color];
        }

        fb = Graphics.CreateFramebuffer(attachments, s, s);
        _faceTargets[key] = fb;
        return fb;
    }

    public override void OnDispose()
    {
        foreach (var fb in _faceTargets.Values)
            fb?.Dispose();
        _faceTargets.Clear();
        _captureDepth?.Dispose();
        _captureDepth = null;
        base.OnDispose();
    }

    private static void ValidateSize(uint size)
    {
        if (size <= 0 || size > Graphics.MaxCubeMapTextureSize)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Cubemap face size must be in (0, MaxCubeMapTextureSize].");
    }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        compoundTag.Add("Size", new(Size));
        compoundTag.Add("MipLevels", new(MipLevels));
        compoundTag.Add("ImageFormat", new((int)ImageFormat));
        compoundTag.Add("MinFilter", new((int)MinFilter));
        compoundTag.Add("MagFilter", new((int)MagFilter));
        compoundTag.Add("Wrap", new((int)WrapMode));

        EchoObject faces = EchoObject.NewList();
        for (int face = 0; face < 6; face++)
        {
            for (int mip = 0; mip < MipLevels; mip++)
            {
                byte[] dest = new byte[FaceByteSize(mip)];
                Graphics.GetTexImageCubeFace(Handle, face, mip, dest);
                faces.ListAdd(new(dest));
            }
        }
        compoundTag.Add("Faces", faces);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        uint size = value["Size"].UIntValue;
        int mipLevels = value["MipLevels"].IntValue;
        TextureImageFormat imageFormat = (TextureImageFormat)value["ImageFormat"].IntValue;
        var minFilter = (TextureMin)value["MinFilter"].IntValue;
        var magFilter = (TextureMag)value["MagFilter"].IntValue;
        var wrap = (TextureWrap)value["Wrap"].IntValue;

        Type[] param = [typeof(uint), typeof(bool), typeof(TextureImageFormat)];
        object[] values = [size, mipLevels > 1, imageFormat];
        typeof(Cubemap).GetConstructor(param)!.Invoke(this, values);

        EchoObject faces = value["Faces"];
        int idx = 0;
        for (int face = 0; face < 6; face++)
        {
            for (int mip = 0; mip < MipLevels && idx < faces.Count; mip++, idx++)
            {
                byte[] data = faces[idx].ByteArrayValue;
                SetFaceData(face, new Memory<byte>(data), mip);
            }
        }

        SetTextureFilters(minFilter, magFilter);
        SetWrapModes(wrap);
    }
}
