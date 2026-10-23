// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Runtime.Resources;

/// <summary>
/// An off-screen render target. Also a project asset (<c>.rendertexture</c>): the file stores the
/// description below, and the GPU resources behind it are allocated on first use, so an asset can be
/// created and inspected without a graphics context.
/// </summary>
[CreateAssetMenu("Render Texture", Extension = ".rendertexture", Order = 1200)]
public sealed class RenderTexture : Asset, ISerializable
{
    public const int DefaultWidth = 1920;
    public const int DefaultHeight = 1080;

    private int _width;
    private int _height;
    private bool _hasDepthAttachment;
    private TextureFormat[] _textureFormats;
    private bool _srgb;

    [NotHeld] private Graphite.Framebuffer? _frameBuffer;
    private Texture2D[]? _internalTextures;
    private Texture2D? _internalDepth;

    /// <summary>The Graphite framebuffer views draw into, null when headless.</summary>
    public Graphite.Framebuffer? Framebuffer { get { EnsureCreated(); return _frameBuffer; } }
    public Texture2D MainTexture { get { EnsureCreated(); return _internalTextures![0]; } }
    public Texture2D[] InternalTextures { get { EnsureCreated(); return _internalTextures!; } }
    public Texture2D? InternalDepth { get { EnsureCreated(); return _internalDepth; } }

    public int Width { get { EnsureLoaded(); return _width; } }
    public int Height { get { EnsureLoaded(); return _height; } }
    public bool HasDepthAttachment { get { EnsureLoaded(); return _hasDepthAttachment; } }
    public TextureFormat[] TextureFormats { get { EnsureLoaded(); return _textureFormats; } }

    /// <summary>Whether RGBA8 colour targets are sRGB, so they encode what is drawn and decode it when sampled.</summary>
    public bool Srgb { get { EnsureLoaded(); return _srgb; } }

    public RenderTexture() : base("RenderTexture")
    {
        _width = DefaultWidth;
        _height = DefaultHeight;
        _hasDepthAttachment = true;
        _textureFormats = [TextureFormat.RGBA8];
    }

    public RenderTexture(int width, int height, bool hasDepthAttachment, TextureFormat[] formats, bool srgb = false) : base("RenderTexture")
    {
        ArgumentNullException.ThrowIfNull(formats);
        _textureFormats = [];
        Configure(width, height, hasDepthAttachment, formats, srgb);
    }

    /// <summary>
    /// Change what this target describes, releasing anything already allocated. The new resources are
    /// created on next use, so this is safe to call from an inspector or off the render thread.
    /// </summary>
    public void Configure(int width, int height, bool hasDepthAttachment, TextureFormat[] formats, bool srgb = false)
    {
        EnsureLoaded();
        ArgumentNullException.ThrowIfNull(formats);

        if (formats.Length > 8)
            throw new ArgumentException($"A render texture supports 0-8 color attachments, got {formats.Length}.", nameof(formats));
        foreach (TextureFormat format in formats)
        {
            if (GraphicsFormats.IsCompressed(format))
                throw new ArgumentException($"{format} is compressed and cannot be rendered into.", nameof(formats));
        }

        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _hasDepthAttachment = hasDepthAttachment;
        _textureFormats = (TextureFormat[])formats.Clone();
        _srgb = srgb;

        ReleaseResources();
    }

    protected internal override long EstimateBytes()
    {
        if (_internalTextures == null) return 0;
        long bytesPerPixel = _hasDepthAttachment ? Texture.GetBytesPerPixel(TextureFormat.Depth32Float) : 0;
        foreach (TextureFormat format in _textureFormats)
            bytesPerPixel += Texture.GetBytesPerPixel(format);
        return (long)_width * _height * bytesPerPixel;
    }

    private bool _enableRandomWrite;

    /// <summary>
    /// Lets compute kernels write the color textures through an image, as <c>image2D</c> uniforms. Changing it releases
    /// the textures, which come back with the new setting on next use.
    /// </summary>
    public bool EnableRandomWrite
    {
        get { EnsureLoaded(); return _enableRandomWrite; }
        set
        {
            EnsureLoaded();
            if (_enableRandomWrite == value) return;
            _enableRandomWrite = value;
            ReleaseResources();
        }
    }

    /// <summary>Allocates the framebuffer and its attachments if they aren't already.</summary>
    private void EnsureCreated()
    {
        EnsureLoaded();
        if (_internalTextures != null) return;

        int numTextures = _textureFormats.Length;
        _internalTextures = new Texture2D[numTextures];
        var colors = new Graphite.Texture[numTextures];
        for (int i = 0; i < numTextures; i++)
        {
            _internalTextures[i] = new Texture2D((uint)_width, (uint)_height, _textureFormats[i], srgb: _srgb, randomWrite: _enableRandomWrite) { Name = Name };
            _internalTextures[i].SetWrap(WrapMode.Clamp);
            colors[i] = _internalTextures[i].GpuTexture!;
        }

        if (_hasDepthAttachment)
            _internalDepth = new Texture2D((uint)_width, (uint)_height, TextureFormat.Depth32Float) { Name = Name };

        if (Graphics.Device == null) return;
        _frameBuffer = Graphics.Device.ResourceFactory.CreateFramebuffer(new Graphite.FramebufferDescription(_internalDepth.IsValid() ? _internalDepth.GpuTexture : null, colors));
        _frameBuffer.Name = Name;
    }

    private void ReleaseResources()
    {
        if (_internalTextures != null)
            foreach (Texture2D texture in _internalTextures)
                if (texture.IsValid()) texture.Dispose();
        _internalTextures = null;

        if (_internalDepth.IsValid()) _internalDepth.Dispose();
        _internalDepth = null;

        Graphics.DeferDispose(_frameBuffer);
        _frameBuffer = null;
    }

    protected override void OnUnload() => ReleaseResources();

    ~RenderTexture()
    {
        if (!Registered) Dispose();
    }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        SerializeHeader(compoundTag);
        compoundTag.Add("Width", new(_width));
        compoundTag.Add("Height", new(_height));
        compoundTag.Add("HasDepthAttachment", new(_hasDepthAttachment));
        EchoObject formats = EchoObject.NewList();
        foreach (TextureFormat format in _textureFormats)
            formats.ListAdd(new((int)format));
        compoundTag.Add("ColorFormats", formats);
        compoundTag.Add("Srgb", new(_srgb));
        compoundTag.Add("EnableRandomWrite", new(_enableRandomWrite));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        EchoObject? formatsTag = value.Get("ColorFormats");
        var formats = new TextureFormat[formatsTag?.Count ?? 0];
        for (int i = 0; i < formats.Length; i++)
            formats[i] = (TextureFormat)formatsTag![i].IntValue;

        Configure(value.Get("Width")?.IntValue ?? DefaultWidth, value.Get("Height")?.IntValue ?? DefaultHeight,
            value.Get("HasDepthAttachment")?.BoolValue ?? true, formats, value.Get("Srgb")?.BoolValue ?? false);
        _enableRandomWrite = value.Get("EnableRandomWrite")?.BoolValue ?? false;
        DeserializeHeader(value);
    }

    #region Pool

    private struct RenderTextureKey(int width, int height, bool hasDepth, TextureFormat[] format) : IEquatable<RenderTextureKey>
    {
        public int Width = width;
        public int Height = height;
        public bool HasDepth = hasDepth;
        public TextureFormat[] Format = format;

        public override bool Equals(object? obj) => obj is RenderTextureKey key && Equals(key);

        public bool Equals(RenderTextureKey key)
        {
            if (Width != key.Width || Height != key.Height || HasDepth != key.HasDepth || Format.Length != key.Format.Length)
                return false;
            for (int i = 0; i < Format.Length; i++)
                if (Format[i] != key.Format[i])
                    return false;
            return true;
        }
        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 23 + Width.GetHashCode();
            hash = hash * 23 + Height.GetHashCode();
            hash = hash * 23 + HasDepth.GetHashCode();
            foreach (TextureFormat format in Format)
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

    public static RenderTexture GetTemporaryRT(int width, int height, bool hasDepth, TextureFormat[] format)
    {
        // Same clamp the texture applies to itself, so release finds it under the key it was rented with.
        width = Math.Max(1, width);
        height = Math.Max(1, height);
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
        // Keyed off the description, not the live attachments - reading those would allocate the very
        // GPU resources a release is meant to hand back.
        var key = new RenderTextureKey(renderTexture._width, renderTexture._height,
                                       renderTexture._hasDepthAttachment, renderTexture._textureFormats);

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
        foreach (KeyValuePair<RenderTextureKey, List<(RenderTexture, long frameCreated)>> pair in pool)
            if (pair.Value.Count == 0)
                (emptyKeys ??= []).Add(pair.Key);
        if (emptyKeys != null) foreach (RenderTextureKey k in emptyKeys) pool.Remove(k);

        emptyKeys = null;
        foreach (KeyValuePair<RenderTextureKey, List<(RenderTexture, long frameAcquired)>> pair in active)
            if (pair.Value.Count == 0)
                (emptyKeys ??= []).Add(pair.Key);
        if (emptyKeys != null) foreach (RenderTextureKey k in emptyKeys) active.Remove(k);
    }

    #endregion

}
