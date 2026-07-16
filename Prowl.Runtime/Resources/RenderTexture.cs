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
    // Released render textures aren't safe to hand back out until the GPU has actually finished
    // reading/writing them: a camera render and (say) a material preview render can release and
    // re-acquire the same pooled RT within the same engine frame, but the backend may still be
    // replaying the first render's commands (possibly on a separate thread, possibly several engine
    // frames behind the CPU). Reusing the RT before that replay completes is a write-after-read
    // hazard: the previous render's draw calls end up reading the new render's contents instead,
    // which looks like geometry/gizmos randomly bleeding from one render into the other.
    private static readonly Dictionary<RenderTexture, ulong> pendingGpuSafeFrame = [];
    private const int MaxUnusedFrames = 10;
    private const int MaxActiveFrames = 3; // Warn if held longer than 3 frames

    public static RenderTexture GetTemporaryRT(int width, int height, bool hasDepth, PixelFormat[] format)
    {
        var key = new RenderTextureKey(width, height, hasDepth, format);

        RenderTexture? renderTexture = null;
        if (pool.TryGetValue(key, out List<(RenderTexture, long frameCreated)>? list) && list.Count > 0)
        {
            GraphicsDevice device = Graphics.Device;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                RenderTexture candidate = list[i].Item1;

                // Skip (leave in the pool) any candidate the GPU might still be using.
                if (device != null && pendingGpuSafeFrame.TryGetValue(candidate, out ulong safeFrame)
                    && !device.IsFrameComplete(safeFrame))
                    continue;

                pendingGpuSafeFrame.Remove(candidate);
                renderTexture = candidate;
                list.RemoveAt(i);
                break;
            }
        }

        renderTexture ??= new RenderTexture(width, height, hasDepth, format);

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

        // Record the last GPU frame that could still be reading/writing this RT, so GetTemporaryRT
        // won't hand it back out until that work has actually completed. Mirrors Graphics.DisposeDeferred.
        GraphicsDevice device = Graphics.Device;
        if (device != null)
            pendingGpuSafeFrame[renderTexture] = device.LastCompletedFrameId + device.FramesInFlight;
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
        {
            pendingGpuSafeFrame.Remove(renderTexture);
            renderTexture.Dispose();
        }

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
