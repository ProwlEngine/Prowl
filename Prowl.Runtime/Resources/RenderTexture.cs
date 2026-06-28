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
    public Framebuffer frameBuffer { get; private set; }
    public Texture2D MainTexture => InternalTextures[0];
    public Texture2D[] InternalTextures { get; private set; }
    public Texture2D InternalDepth { get; private set; }

    private const PixelFormat DepthFormat = PixelFormat.D24_UNorm_S8_UInt;

    public int Width { get; private set; }
    public int Height { get; private set; }
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
            InternalTextures[i].SetTextureFilters(SamplerFilter.MinLinear_MagLinear_MipPoint);
            InternalTextures[i].SetWrapModes(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
            colorTargets[i] = InternalTextures[i].Handle;
        }

        Graphite.Texture depthTarget = null;
        if (hasDepthAttachment)
        {
            InternalDepth = new Texture2D((uint)Width, (uint)Height, DepthFormat, TextureUsage.DepthStencil);
            depthTarget = InternalDepth.Handle;
        }

        frameBuffer = Graphics.Device.ResourceFactory.CreateFramebuffer(
            new FramebufferDescription(depthTarget, colorTargets));
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
            InternalTextures[i].SetTextureFilters(SamplerFilter.MinLinear_MagLinear_MipPoint);
            InternalTextures[i].SetWrapModes(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
            colorTargets[i] = InternalTextures[i].Handle;
        }

        InternalDepth = sharedDepth;
        frameBuffer = Graphics.Device.ResourceFactory.CreateFramebuffer(
            new FramebufferDescription(sharedDepth?.Handle, colorTargets));
    }

    public override void OnDispose()
    {
        if (frameBuffer == null) return;
        foreach (Texture2D texture in InternalTextures)
            texture.Dispose();

        if (ownsDepth && InternalDepth != null)
            InternalDepth.Dispose();

        frameBuffer.Dispose();
    }

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

    #region Pool

    private struct RenderTextureKey(int width, int height, bool hasDepth, PixelFormat[] format)
    {
        public int Width = width;
        public int Height = height;
        public bool HasDepth = hasDepth;
        public PixelFormat[] Format = format;

        public override bool Equals(object? obj)
        {
            if (obj is RenderTextureKey key)
            {
                if (Width == key.Width && Height == key.Height && HasDepth == key.HasDepth && Format.Length == key.Format.Length)
                {
                    for (int i = 0; i < Format.Length; i++)
                        if (Format[i] != key.Format[i])
                            return false;
                    return true;
                }
            }
            return false;
        }
        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 23 + Width.GetHashCode();
            hash = hash * 23 + Height.GetHashCode();
            hash = hash * 23 + HasDepth.GetHashCode();
            foreach (PixelFormat format in Format)
                hash = hash * 23 + ((int)format).GetHashCode();
            return hash;
        }
        public static bool operator ==(RenderTextureKey left, RenderTextureKey right) => left.Equals(right);
        public static bool operator !=(RenderTextureKey left, RenderTextureKey right) => !(left == right);
    }

    private static Dictionary<RenderTextureKey, List<(RenderTexture, long frameCreated)>> pool = [];
    private static Dictionary<RenderTextureKey, List<(RenderTexture, long frameAcquired)>> active = [];
    private const int MaxUnusedFrames = 10;
    private const int MaxActiveFrames = 3; // Warn if held longer than 3 frames

    public static RenderTexture GetTemporaryRT(int width, int height, bool hasDepth, PixelFormat[] format)
    {
        var key = new RenderTextureKey(width, height, hasDepth, format);

        RenderTexture renderTexture;
        if (pool.TryGetValue(key, out List<(RenderTexture, long frameCreated)>? list) && list.Count > 0)
        {
            int i = list.Count - 1;
            renderTexture = list[i].Item1;
            list.RemoveAt(i);
        }
        else
        {
            renderTexture = new RenderTexture(width, height, hasDepth, format);
        }

        // Track in active pool
        if (!active.TryGetValue(key, out List<(RenderTexture, long frameAcquired)>? activeList))
        {
            activeList = [];
            active[key] = activeList;
        }
        activeList.Add((renderTexture, Time.FrameCount));

        return renderTexture;
    }

    public static void ReleaseTemporaryRT(RenderTexture renderTexture)
    {
        var key = new RenderTextureKey(renderTexture.Width, renderTexture.Height, renderTexture.hasDepthAttachment, [.. renderTexture.InternalTextures.Select(t => t.ImageFormat)]);

        // Remove from active pool
        if (active.TryGetValue(key, out List<(RenderTexture, long frameAcquired)>? activeList))
        {
            for (int i = activeList.Count - 1; i >= 0; i--)
            {
                if (activeList[i].Item1 == renderTexture)
                {
                    activeList.RemoveAt(i);
                    break;
                }
            }
        }

        // Add to pool for reuse
        if (!pool.TryGetValue(key, out List<(RenderTexture, long frameCreated)>? list))
        {
            list = [];
            pool[key] = list;
        }

        list.Add((renderTexture, Time.FrameCount));
    }

    public static void UpdatePool()
    {
        var disposableTextures = new List<RenderTexture>();

        // Check for leaked active render textures (held longer than MaxActiveFrames)
        foreach (KeyValuePair<RenderTextureKey, List<(RenderTexture, long frameAcquired)>> pair in active)
        {
            for (int i = pair.Value.Count - 1; i >= 0; i--)
            {
                (RenderTexture renderTexture, long frameAcquired) = pair.Value[i];
                long framesActive = Time.FrameCount - frameAcquired;

                if (framesActive > MaxActiveFrames)
                {
                    Debug.LogWarning($"RenderTexture leak detected! Texture ({renderTexture.Width}x{renderTexture.Height}) has been active for {framesActive} frames (max: {MaxActiveFrames}). Auto-disposing to prevent memory leak.");
                    disposableTextures.Add(renderTexture);
                    pair.Value.RemoveAt(i);
                }
            }
        }

        // Clean up unused textures in pool
        foreach (KeyValuePair<RenderTextureKey, List<(RenderTexture, long frameCreated)>> pair in pool)
        {
            for (int i = pair.Value.Count - 1; i >= 0; i--)
            {
                (RenderTexture renderTexture, long frameCreated) = pair.Value[i];
                if (Time.FrameCount - frameCreated > MaxUnusedFrames)
                {
                    disposableTextures.Add(renderTexture);
                    pair.Value.RemoveAt(i);
                }
            }
        }

        foreach (RenderTexture renderTexture in disposableTextures)
            renderTexture.Dispose();

        // Clean up empty dictionary entries to prevent unbounded key accumulation
        List<RenderTextureKey>? emptyKeys = null;
        foreach (var pair in pool)
            if (pair.Value.Count == 0)
                (emptyKeys ??= []).Add(pair.Key);
        if (emptyKeys != null) foreach (var k in emptyKeys) pool.Remove(k);

        emptyKeys = null;
        foreach (var pair in active)
            if (pair.Value.Count == 0)
                (emptyKeys ??= []).Add(pair.Key);
        if (emptyKeys != null) foreach (var k in emptyKeys) active.Remove(k);
    }

    #endregion

}
