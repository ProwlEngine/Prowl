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
public sealed class RenderTexture : EngineObject, ISerializable
{
    public const int DefaultWidth = 1920;
    public const int DefaultHeight = 1080;

    // ─── Description (what the asset file stores) ───
    private int _width;
    private int _height;
    private bool _hasDepthAttachment;
    private TextureImageFormat[] _textureFormats;

    // ─── GPU resources (allocated lazily from the description) ───
    private GraphicsFrameBuffer? _frameBuffer;
    private Texture2D[]? _internalTextures;
    private Texture2D? _internalDepth;

    public GraphicsFrameBuffer frameBuffer { get { EnsureCreated(); return _frameBuffer!; } }
    public Texture2D MainTexture { get { EnsureCreated(); return _internalTextures![0]; } }
    public Texture2D[] InternalTextures { get { EnsureCreated(); return _internalTextures!; } }
    public Texture2D? InternalDepth { get { EnsureCreated(); return _internalDepth; } }

    public int Width { get { EnsureNotDisposed(); return _width; } }
    public int Height { get { EnsureNotDisposed(); return _height; } }
    public bool HasDepthAttachment { get { EnsureNotDisposed(); return _hasDepthAttachment; } }
    public TextureImageFormat[] TextureFormats { get { EnsureNotDisposed(); return _textureFormats; } }

    public RenderTexture() : base("RenderTexture")
    {
        _width = DefaultWidth;
        _height = DefaultHeight;
        _hasDepthAttachment = true;
        _textureFormats = [TextureImageFormat.Color4b];
    }

    public RenderTexture(int width, int height, bool hasDepthAttachment, TextureImageFormat[] formats) : base("RenderTexture")
    {
        ArgumentNullException.ThrowIfNull(formats);
        _textureFormats = [];
        Configure(width, height, hasDepthAttachment, formats);
    }

    /// <summary>
    /// Change what this target describes, releasing anything already allocated. The new resources are
    /// created on next use, so this is safe to call from an inspector or off the render thread.
    /// </summary>
    public void Configure(int width, int height, bool hasDepthAttachment, TextureImageFormat[] formats)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(formats);

        if (formats.Length < 0 || formats.Length > Graphics.MaxFramebufferColorAttachments)
            throw new ArgumentException(
                $"A render texture supports 0-{Graphics.MaxFramebufferColorAttachments} color attachments, got {formats.Length}.",
                nameof(formats));

        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _hasDepthAttachment = hasDepthAttachment;
        _textureFormats = (TextureImageFormat[])formats.Clone();

        ReleaseResources();
    }

    /// <summary>Allocates the framebuffer and its attachments if they aren't already.</summary>
    private void EnsureCreated()
    {
        EnsureNotDisposed();
        if (_frameBuffer != null) return;

        int numTextures = _textureFormats.Length;
        var attachments = new GraphicsFrameBuffer.Attachment[numTextures + (_hasDepthAttachment ? 1 : 0)];
        _internalTextures = new Texture2D[numTextures];
        for (int i = 0; i < numTextures; i++)
        {
            _internalTextures[i] = new Texture2D((uint)_width, (uint)_height, false, _textureFormats[i]);
            _internalTextures[i].SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            _internalTextures[i].SetWrapModes(TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
            attachments[i] = new GraphicsFrameBuffer.Attachment { Texture = _internalTextures[i].Handle, IsDepth = false };
        }

        if (_hasDepthAttachment)
        {
            _internalDepth = new Texture2D((uint)_width, (uint)_height, false, TextureImageFormat.Depth24f);
            attachments[numTextures] = new GraphicsFrameBuffer.Attachment { Texture = _internalDepth.Handle, IsDepth = true };
        }

        _frameBuffer = Graphics.CreateFramebuffer(attachments, (uint)_width, (uint)_height);
    }

    private void ReleaseResources()
    {
        if (_internalTextures != null)
            foreach (Texture2D texture in _internalTextures)
                if (texture.IsValid()) texture.Dispose();
        _internalTextures = null;

        if (_internalDepth.IsValid()) _internalDepth.Dispose();
        _internalDepth = null;

        _frameBuffer?.Dispose();
        _frameBuffer = null;
    }

    protected override void OnDispose() => ReleaseResources();

    ~RenderTexture() => Dispose();

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        SerializeHeader(compoundTag);
        compoundTag.Add("Width", new(_width));
        compoundTag.Add("Height", new(_height));
        compoundTag.Add("HasDepthAttachment", new((byte)(_hasDepthAttachment ? 1 : 0)));
        EchoObject textureFormatsTag = EchoObject.NewList();
        foreach (TextureImageFormat format in _textureFormats)
            textureFormatsTag.ListAdd(new((byte)format));
        compoundTag.Add("TextureFormats", textureFormatsTag);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        int width = value.Get("Width")?.IntValue ?? DefaultWidth;
        int height = value.Get("Height")?.IntValue ?? DefaultHeight;
        bool hasDepth = (value.Get("HasDepthAttachment")?.ByteValue ?? 1) == 1;

        // NumTextures used to be written alongside the list; the list is the only source now, but an
        // older file's count still bounds it so a truncated list can't read past the end.
        EchoObject? formatsTag = value.Get("TextureFormats");
        int count = formatsTag?.Count ?? 0;
        if (value.TryGet("NumTextures", out EchoObject? numTag))
            count = Math.Min(count, numTag.IntValue);

        var formats = new TextureImageFormat[count];
        for (int i = 0; i < count; i++)
            formats[i] = (TextureImageFormat)formatsTag![i].ByteValue;

        Configure(width, height, hasDepth, formats);
        DeserializeHeader(value);
    }

    #region Pool

    private struct RenderTextureKey(int width, int height, bool hasDepth, TextureImageFormat[] format)
    {
        public int Width = width;
        public int Height = height;
        public bool HasDepth = hasDepth;
        public TextureImageFormat[] Format = format;

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
            foreach (TextureImageFormat format in Format)
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

    public static RenderTexture GetTemporaryRT(int width, int height, bool hasDepth, TextureImageFormat[] format)
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
