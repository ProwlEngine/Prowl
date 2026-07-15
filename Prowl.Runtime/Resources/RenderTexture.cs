// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Runtime.Resources;

public sealed class RenderTexture : EngineObject, ISerializable
{
    public GraphicsFrameBuffer frameBuffer { get; private set; }
    public Texture2D MainTexture => InternalTextures[0];
    public Texture2D[] InternalTextures { get; private set; }
    public Texture2D InternalDepth { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>GL sample count. 1 means a normal single-sampled target.</summary>
    public int SampleCount { get; private set; }

    /// <summary>When true this target's attachments are multisampled, so they cannot be
    /// sampled by a shader. Resolve it first with <see cref="CommandBuffer.ResolveMultisample"/>.</summary>
    public bool IsMultisampled => SampleCount > 1;

    private int numTextures;
    private bool hasDepthAttachment;
    private TextureImageFormat[] textureFormats;

    public RenderTexture() : base("RenderTexture")
    {
        Width = 0;
        Height = 0;
        SampleCount = 1;
        numTextures = 0;
        hasDepthAttachment = false;
        textureFormats = [];
    }

    /// <summary>Kept as a distinct 4-argument overload rather than an optional parameter:
    /// <see cref="Deserialize"/> resolves this constructor reflectively by exact signature.</summary>
    public RenderTexture(int Width, int Height, bool hasDepthAttachment, TextureImageFormat[] formats)
        : this(Width, Height, hasDepthAttachment, formats, 1) { }

    public RenderTexture(int Width, int Height, bool hasDepthAttachment, TextureImageFormat[] formats, int sampleCount) : base("RenderTexture")
    {
        this.Width = Width;
        this.Height = Height;
        SampleCount = sampleCount;
        numTextures = formats?.Length ?? throw new ArgumentNullException(nameof(formats), "Texture formats cannot be null.");
        this.hasDepthAttachment = hasDepthAttachment;

        if (numTextures < 0 || numTextures > Graphics.MaxFramebufferColorAttachments)
            throw new Exception("Invalid number of textures! [0-" + Graphics.MaxFramebufferColorAttachments + "]");

        if (sampleCount < 1)
            throw new ArgumentOutOfRangeException(nameof(sampleCount), sampleCount, "Sample count must be at least 1.");

        textureFormats = formats;
        bool multisampled = sampleCount > 1;

        GraphicsFrameBuffer.Attachment[] attachments = new GraphicsFrameBuffer.Attachment[numTextures + (hasDepthAttachment ? 1 : 0)];
        InternalTextures = new Texture2D[numTextures];
        for (int i = 0; i < numTextures; i++)
        {
            InternalTextures[i] = multisampled
                ? new Texture2D((uint)Width, (uint)Height, sampleCount, textureFormats[i])
                : new Texture2D((uint)Width, (uint)Height, false, textureFormats[i]);

            // Multisampled attachments reject all sampler state (INVALID_ENUM).
            if (!multisampled)
            {
                InternalTextures[i].SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
                InternalTextures[i].SetWrapModes(TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
            }

            attachments[i] = new GraphicsFrameBuffer.Attachment { Texture = InternalTextures[i].Handle, IsDepth = false };
        }

        if (hasDepthAttachment)
        {
            // The depth attachment has to match the color attachments' sample count or the
            // framebuffer comes back FRAMEBUFFER_INCOMPLETE_MULTISAMPLE.
            InternalDepth = multisampled
                ? new Texture2D((uint)Width, (uint)Height, sampleCount, TextureImageFormat.Depth24f)
                : new Texture2D((uint)Width, (uint)Height, false, TextureImageFormat.Depth24f);
            attachments[numTextures] = new GraphicsFrameBuffer.Attachment { Texture = InternalDepth.Handle, IsDepth = true };
        }

        frameBuffer = Graphics.CreateFramebuffer(attachments, (uint)Width, (uint)Height);
    }

    public override void OnDispose()
    {
        if (frameBuffer == null) return;
        foreach (Texture2D texture in InternalTextures)
            texture.Dispose();

        // Dispose depth texture if present
        if (hasDepthAttachment && InternalDepth != null)
            InternalDepth.Dispose();

        frameBuffer.Dispose();
    }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        compoundTag.Add("Width", new(Width));
        compoundTag.Add("Height", new(Height));
        compoundTag.Add("SampleCount", new(SampleCount));
        compoundTag.Add("NumTextures", new(numTextures));
        compoundTag.Add("HasDepthAttachment", new((byte)(hasDepthAttachment ? 1 : 0)));
        EchoObject textureFormatsTag = EchoObject.NewList();
        foreach (TextureImageFormat format in textureFormats)
            textureFormatsTag.ListAdd(new((byte)format));
        compoundTag.Add("TextureFormats", textureFormatsTag);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Width = value["Width"].IntValue;
        Height = value["Height"].IntValue;
        // Assets written before MSAA existed have no SampleCount tag.
        SampleCount = value.Get("SampleCount")?.IntValue ?? 1;
        numTextures = value["NumTextures"].IntValue;
        hasDepthAttachment = value["HasDepthAttachment"].ByteValue == 1;
        textureFormats = new TextureImageFormat[numTextures];
        EchoObject? textureFormatsTag = value.Get("TextureFormats");
        for (int i = 0; i < numTextures; i++)
            textureFormats[i] = (TextureImageFormat)textureFormatsTag[i].ByteValue;


        Type[] param = new[] { typeof(int), typeof(int), typeof(bool), typeof(TextureImageFormat[]), typeof(int) };
        object[] values = new object[] { Width, Height, hasDepthAttachment, textureFormats, SampleCount };
        typeof(RenderTexture).GetConstructor(param).Invoke(this, values);
    }

    #region Pool

    private struct RenderTextureKey(int width, int height, bool hasDepth, TextureImageFormat[] format, int sampleCount)
    {
        public int Width = width;
        public int Height = height;
        public bool HasDepth = hasDepth;
        public TextureImageFormat[] Format = format;
        public int SampleCount = sampleCount;

        public override bool Equals(object? obj)
        {
            if (obj is RenderTextureKey key)
            {
                if (Width == key.Width && Height == key.Height && HasDepth == key.HasDepth
                    && SampleCount == key.SampleCount && Format.Length == key.Format.Length)
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
            hash = hash * 23 + SampleCount.GetHashCode();
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

    public static RenderTexture GetTemporaryRT(int width, int height, bool hasDepth, TextureImageFormat[] format, int sampleCount = 1)
    {
        var key = new RenderTextureKey(width, height, hasDepth, format, sampleCount);

        RenderTexture renderTexture;
        if (pool.TryGetValue(key, out List<(RenderTexture, long frameCreated)>? list) && list.Count > 0)
        {
            int i = list.Count - 1;
            renderTexture = list[i].Item1;
            list.RemoveAt(i);
        }
        else
        {
            renderTexture = new RenderTexture(width, height, hasDepth, format, sampleCount);
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
        var key = new RenderTextureKey(renderTexture.Width, renderTexture.Height, renderTexture.hasDepthAttachment, [.. renderTexture.InternalTextures.Select(t => t.ImageFormat)], renderTexture.SampleCount);

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
