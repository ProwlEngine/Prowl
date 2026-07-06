// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A six-faced environment <see cref="Texture"/>. Faces are square and indexed 0..5 in GL
/// order: +X, -X, +Y, -Y, +Z, -Z. Supports a full mip chain (used by reflection probes to
/// store a roughness-convolved, GGX-prefiltered environment) and can be rendered into per
/// face and per mip via <see cref="GetFaceTarget"/>.
/// <para>
/// Backed by a Graphite 2D texture with six array layers and <see cref="TextureUsage.Cubemap"/>.
/// Each face is an array layer; per-face uploads use <c>arrayLayer = face</c>.
/// </para>
/// </summary>
public sealed class Cubemap : Texture, ISerializable
{
    /// <summary>Edge length of each (square) face, in texels.</summary>
    public uint Size { get; private set; }

    /// <summary>Number of mip levels allocated (1 when no chain).</summary>
    public int MipLevels { get; private set; }

    private bool _mipChain;

    // Render-target framebuffers are created lazily per (face, mip[, depth]) and reused.
    private readonly Dictionary<int, Framebuffer> _faceTargets = new();

    // Shared depth buffer for capture (scene rendering into a face needs a depth test).
    private Texture2D? _captureDepth;

    public Cubemap() : base(TextureType.Texture2D, PixelFormat.R16_G16_B16_A16_UInt, true) { }

    /// <summary>
    /// Creates a cubemap with empty storage for all six faces.
    /// </summary>
    /// <param name="size">Edge length of each face.</param>
    /// <param name="mipChain">Allocate a full mip chain (for prefiltered roughness levels).</param>
    /// <param name="imageFormat">Texel format. Defaults to RGBA16 for HDR.</param>
    public Cubemap(uint size, bool mipChain = false, PixelFormat imageFormat = PixelFormat.R16_G16_B16_A16_UInt)
        : base(TextureType.Texture2D, imageFormat, true)
    {
        Recreate(size, mipChain);
    }

    /// <summary>Allocates (or reallocates) all six faces and mip levels, discarding contents.</summary>
    public void Recreate(uint size, bool mipChain)
    {
        ValidateSize(size);

        Size = size;
        _mipChain = mipChain;
        MipLevels = mipChain ? MipCountFor(size) : 1;

        Handle?.Dispose();

        TextureUsage usage = TextureUsage.Sampled | TextureUsage.RenderTarget | TextureUsage.Cubemap;
        if (mipChain)
            usage |= TextureUsage.GenerateMipmaps;

        Handle = Graphics.Device.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(size, size, (uint)MipLevels, 6, ImageFormat, usage));
        Handle.Name = Name;

        IsMipmapped = mipChain;

        SetTextureFilters(MipLevels > 1
            ? SamplerFilter.MinLinear_MagLinear_MipLinear
            : SamplerFilter.MinLinear_MagLinear_MipPoint);
        SetWrapModes(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
    }

    public uint MipSize(int mip) => Math.Max(1u, Size >> mip);

    private static int MipCountFor(uint size)
    {
        int levels = 1;
        while (size > 1) { size >>= 1; levels++; }
        return levels;
    }

    /// <summary>Bytes per texel for this cubemap's format.</summary>
    public int BytesPerPixel() => (int)ImageFormat.GetSizeInBytes();

    /// <summary>Total bytes for one face at the given mip level.</summary>
    public int FaceByteSize(int mip)
    {
        uint s = MipSize(mip);
        return (int)(s * s) * BytesPerPixel();
    }

    /// <summary>Upload pixel data into one face's mip level.</summary>
    public unsafe void SetFaceData<T>(int face, Memory<T> data, int mip = 0) where T : unmanaged
    {
        uint s = MipSize(mip);
        uint bytes = (uint)(s * s) * (uint)BytesPerPixel();
        fixed (void* ptr = data.Span)
            Graphics.Device.UpdateTexture(Handle, (nint)ptr, bytes,
                0, 0, 0, s, s, 1, (uint)mip, (uint)face);
    }

    /// <summary>Read back one face's mip level into <paramref name="destination"/>.</summary>
    public void GetFaceData(int face, byte[] destination, int mip = 0)
    {
        // Texture read-back has not yet been ported to Prowl.Graphite (requires a staging texture).
        // Graphics.GetTexImageCubeFace(Handle, face, mip, destination);
    }

    /// <summary>A framebuffer that renders into one face at one mip level. With
    /// <paramref name="withDepth"/> it also attaches a shared depth buffer (sized to the
    /// full face) so scene geometry can depth-test during capture. Cached and reused;
    /// disposed with the cubemap.</summary>
    public Framebuffer GetFaceTarget(int face, int mip, bool withDepth = false)
    {
        int key = (face * 64 + mip) * 2 + (withDepth ? 1 : 0);
        if (_faceTargets.TryGetValue(key, out var fb) && !fb.IsDisposed)
            return fb;

        var color = new FramebufferAttachmentDescription(Handle, (uint)face, (uint)mip);

        FramebufferDescription desc;
        if (withDepth)
        {
            if (_captureDepth == null)
            {
                _captureDepth = new Texture2D(Size, Size, PixelFormat.D24_UNorm_S8_UInt, TextureUsage.DepthStencil);
                _captureDepth.Name = $"{Name} Capture Depth";
                _captureDepth.Handle.Name = _captureDepth.Name;
            }
            var depth = new FramebufferAttachmentDescription(_captureDepth.Handle, 0, 0);
            desc = new FramebufferDescription(depth, new[] { color });
        }
        else
        {
            desc = new FramebufferDescription(null, new[] { color });
        }

        fb = Graphics.Device.ResourceFactory.CreateFramebuffer(desc);
        fb.Name = $"{Name} Face {face} Mip {mip}{(withDepth ? " (Depth)" : "")}";
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
        compoundTag.Add("Filter", new((int)Filter));
        compoundTag.Add("AddressU", new((int)AddressModeU));
        compoundTag.Add("AddressV", new((int)AddressModeV));

        EchoObject faces = EchoObject.NewList();
        for (int face = 0; face < 6; face++)
        {
            for (int mip = 0; mip < MipLevels; mip++)
            {
                byte[] dest = new byte[FaceByteSize(mip)];
                // Read-back not yet ported to Prowl.Graphite; faces serialize as empty for now.
                // Graphics.GetTexImageCubeFace(Handle, face, mip, dest);
                faces.ListAdd(new(dest));
            }
        }
        compoundTag.Add("Faces", faces);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        uint size = value["Size"].UIntValue;
        int mipLevels = value["MipLevels"].IntValue;
        PixelFormat imageFormat = (PixelFormat)value["ImageFormat"].IntValue;
        var filter = (SamplerFilter)value["Filter"].IntValue;
        var addressU = (SamplerAddressMode)value["AddressU"].IntValue;
        var addressV = (SamplerAddressMode)value["AddressV"].IntValue;

        Type[] param = [typeof(uint), typeof(bool), typeof(PixelFormat)];
        object[] values = [size, mipLevels > 1, imageFormat];
        typeof(Cubemap).GetConstructor(param)!.Invoke(this, values);

        EchoObject faces = value["Faces"];
        int idx = 0;
        for (int face = 0; face < 6; face++)
        {
            for (int mip = 0; mip < MipLevels && idx < faces.Count; mip++, idx++)
            {
                byte[] data = faces[idx].ByteArrayValue;
                if (data != null && data.Length > 0)
                    SetFaceData(face, new Memory<byte>(data), mip);
            }
        }

        SetTextureFilters(filter);
        SetWrapModes(addressU, addressV);
    }
}
