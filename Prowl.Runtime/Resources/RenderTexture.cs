// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Graphite;

namespace Prowl.Runtime.Resources;

public sealed class RenderTexture : EngineObject, ISerializable
{
    private Framebuffer _frameBuffer;
    public Framebuffer frameBuffer { get { EnsureNotDisposed(); return _frameBuffer; } private set => _frameBuffer = value; }
    public Texture2D MainTexture { get { EnsureNotDisposed(); return InternalTextures[0]; } }
    private Texture2D[] _internalTextures;
    public Texture2D[] InternalTextures { get { EnsureNotDisposed(); return _internalTextures; } private set => _internalTextures = value; }
    private Texture2D _internalDepth;
    public Texture2D InternalDepth { get { EnsureNotDisposed(); return _internalDepth; } private set => _internalDepth = value; }

    private const PixelFormat DepthFormat = PixelFormat.D24_UNorm_S8_UInt;

    private int _width;
    private int _height;
    public int Width { get { EnsureNotDisposed(); return _width; } private set => _width = value; }
    public int Height { get { EnsureNotDisposed(); return _height; } private set => _height = value; }
    private int numTextures;
    private bool hasDepthAttachment;
    private bool ownsDepth;
    private PixelFormat[] textureFormats;

    public RenderTexture() : base("RenderTexture")
    {
        Width = 0;
        Height = 0;
        numTextures = 0;
        hasDepthAttachment = false;
        ownsDepth = false;
        textureFormats = [];
    }

    public RenderTexture(int Width, int Height, bool hasDepthAttachment, PixelFormat[] formats) : base("RenderTexture")
    {
        this.Width = Width;
        this.Height = Height;
        numTextures = formats?.Length ?? throw new ArgumentNullException(nameof(formats), "Texture formats cannot be null.");
        this.hasDepthAttachment = hasDepthAttachment;
        ownsDepth = hasDepthAttachment;
        textureFormats = formats;

        InternalTextures = new Texture2D[numTextures];
        Graphite.Texture[] colorTargets = new Graphite.Texture[numTextures];
        for (int i = 0; i < numTextures; i++)
        {
            InternalTextures[i] = new Texture2D((uint)Width, (uint)Height, textureFormats[i], TextureUsage.RenderTarget | TextureUsage.Sampled);
            InternalTextures[i].Name = $"{Name} Color {i}";
            InternalTextures[i].Handle.Name = InternalTextures[i].Name;
            InternalTextures[i].SetTextureFilters(SamplerFilter.MinLinear_MagLinear_MipPoint);
            InternalTextures[i].SetWrapModes(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
            colorTargets[i] = InternalTextures[i].Handle;
        }

        Graphite.Texture depthTarget = null;
        if (hasDepthAttachment)
        {
            InternalDepth = new Texture2D((uint)Width, (uint)Height, DepthFormat, TextureUsage.DepthStencil);
            InternalDepth.Name = $"{Name} Depth";
            InternalDepth.Handle.Name = InternalDepth.Name;
            depthTarget = InternalDepth.Handle;
        }

        frameBuffer = Graphics.Device.ResourceFactory.CreateFramebuffer(
            new FramebufferDescription(depthTarget, colorTargets));
        frameBuffer.Name = $"{Name} Framebuffer";
    }

    public RenderTexture(int Width, int Height, Texture2D sharedDepth, PixelFormat[] formats) : base("RenderTexture")
    {
        this.Width = Width;
        this.Height = Height;
        numTextures = formats?.Length ?? throw new ArgumentNullException(nameof(formats), "Texture formats cannot be null.");
        hasDepthAttachment = sharedDepth != null;
        ownsDepth = false;
        textureFormats = formats;

        InternalTextures = new Texture2D[numTextures];
        Graphite.Texture[] colorTargets = new Graphite.Texture[numTextures];
        for (int i = 0; i < numTextures; i++)
        {
            InternalTextures[i] = new Texture2D((uint)Width, (uint)Height, textureFormats[i], TextureUsage.RenderTarget | TextureUsage.Sampled);
            InternalTextures[i].Name = $"{Name} Color {i}";
            InternalTextures[i].Handle.Name = InternalTextures[i].Name;
            InternalTextures[i].SetTextureFilters(SamplerFilter.MinLinear_MagLinear_MipPoint);
            InternalTextures[i].SetWrapModes(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
            colorTargets[i] = InternalTextures[i].Handle;
        }

        InternalDepth = sharedDepth;
        frameBuffer = Graphics.Device.ResourceFactory.CreateFramebuffer(
            new FramebufferDescription(sharedDepth?.Handle, colorTargets));
        frameBuffer.Name = $"{Name} Framebuffer";
    }

    public override void OnDispose()
    {
        if (_frameBuffer == null) return;
        foreach (Texture2D texture in _internalTextures)
            texture.Dispose();

        if (ownsDepth && _internalDepth != null)
            _internalDepth.Dispose();

        _frameBuffer.Dispose();
    }

    ~RenderTexture() => Dispose();

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        compoundTag.Add("Width", new(Width));
        compoundTag.Add("Height", new(Height));
        compoundTag.Add("NumTextures", new(numTextures));
        compoundTag.Add("HasDepthAttachment", new((byte)(hasDepthAttachment ? 1 : 0)));

        EchoObject textureFormatsTag = EchoObject.NewList();
        foreach (PixelFormat format in textureFormats)
            textureFormatsTag.ListAdd(new((int)format));

        compoundTag.Add("TextureFormats", textureFormatsTag);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Width = value["Width"].IntValue;
        Height = value["Height"].IntValue;
        numTextures = value["NumTextures"].IntValue;
        hasDepthAttachment = value["HasDepthAttachment"].ByteValue == 1;
        textureFormats = new PixelFormat[numTextures];

        EchoObject? textureFormatsTag = value.Get("TextureFormats");
        for (int i = 0; i < numTextures; i++)
            textureFormats[i] = (PixelFormat)textureFormatsTag[i].IntValue;

        Type[] param = [typeof(int), typeof(int), typeof(bool), typeof(PixelFormat[])];
        object[] values = [Width, Height, hasDepthAttachment, textureFormats];
        typeof(RenderTexture).GetConstructor(param).Invoke(this, values);
    }

}
