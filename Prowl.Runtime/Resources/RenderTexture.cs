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
    private int numTextures;
    private bool hasDepthAttachment;
    private TextureImageFormat[] textureFormats;

    public RenderTexture() : base("RenderTexture")
    {
        Width = 0;
        Height = 0;
        numTextures = 0;
        hasDepthAttachment = false;
        textureFormats = [];
    }

    public RenderTexture(int Width, int Height, bool hasDepthAttachment, TextureImageFormat[] formats) : base("RenderTexture")
    {
        this.Width = Width;
        this.Height = Height;
        numTextures = formats?.Length ?? throw new ArgumentNullException(nameof(formats), "Texture formats cannot be null.");
        this.hasDepthAttachment = hasDepthAttachment;

        if (numTextures < 0 || numTextures > Graphics.MaxFramebufferColorAttachments)
            throw new Exception("Invalid number of textures! [0-" + Graphics.MaxFramebufferColorAttachments + "]");

        textureFormats = formats;

        GraphicsFrameBuffer.Attachment[] attachments = new GraphicsFrameBuffer.Attachment[numTextures + (hasDepthAttachment ? 1 : 0)];
        InternalTextures = new Texture2D[numTextures];
        for (int i = 0; i < numTextures; i++)
        {
            InternalTextures[i] = new Texture2D((uint)Width, (uint)Height, false, textureFormats[i]);
            InternalTextures[i].SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            InternalTextures[i].SetWrapModes(TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
            attachments[i] = new GraphicsFrameBuffer.Attachment { Texture = InternalTextures[i].Handle, IsDepth = false };
        }

        if (hasDepthAttachment)
        {
            InternalDepth = new Texture2D((uint)Width, (uint)Height, false, TextureImageFormat.Depth24f);
            attachments[numTextures] = new GraphicsFrameBuffer.Attachment { Texture = InternalDepth.Handle, IsDepth = true };
        }

        frameBuffer = Graphics.CreateFramebuffer(attachments, (uint)Width, (uint)Height);
    }

    public void Begin()
    {
        Graphics.BindFramebuffer(frameBuffer);
    }

    public void End()
    {
        Graphics.UnbindFramebuffer();
    }

    public override void OnDispose()
    {
        if (frameBuffer == null) return;
        foreach (Texture2D texture in InternalTextures)
            texture.Dispose();

        //if(hasDepthAttachment) // Should auto dispose of Depth
        //    Graphics.GL.DeleteRenderbuffer(InternalDepth.Handle);
        frameBuffer.Dispose();
    }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        compoundTag.Add("Width", new(Width));
        compoundTag.Add("Height", new(Height));
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
        numTextures = value["NumTextures"].IntValue;
        hasDepthAttachment = value["HasDepthAttachment"].ByteValue == 1;
        textureFormats = new TextureImageFormat[numTextures];
        EchoObject? textureFormatsTag = value.Get("TextureFormats");
        for (int i = 0; i < numTextures; i++)
            textureFormats[i] = (TextureImageFormat)textureFormatsTag[i].ByteValue;

        Type[] param = new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(TextureImageFormat[]) };
        object[] values = new object[] { Width, Height, numTextures, hasDepthAttachment, textureFormats };
        typeof(RenderTexture).GetConstructor(param).Invoke(this, values);
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
    }

    #endregion

}
