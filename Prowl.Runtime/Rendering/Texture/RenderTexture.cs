// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Veldrid;

namespace Prowl.Runtime;

public struct RenderTextureDescription
{
    public uint width;
    public uint height;

    public PixelFormat[] colorBufferFormats;
    public PixelFormat? depthBufferFormat;

    public bool sampled;
    public bool enableRandomWrite;

    public TextureSampleCount sampleCount;

    public RenderTextureDescription(
        uint width, uint height,
        PixelFormat? depthFormat,
        PixelFormat[] colorFormats,
        bool sampled = true, bool randomWrite = false,
        TextureSampleCount sampleCount = TextureSampleCount.Count1)
    {
        this.width = width;
        this.height = height;
        depthBufferFormat = depthFormat;
        colorBufferFormats = colorFormats;
        this.sampled = sampled;
        enableRandomWrite = randomWrite;
        this.sampleCount = sampleCount;
    }

    public RenderTextureDescription(RenderTexture texture)
    {
        width = texture.Width;
        height = texture.Height;
        depthBufferFormat = texture.DepthBuffer?.Format;
        colorBufferFormats = texture.ColorBuffers.Select(x => x.Format).ToArray();
        sampled = texture.Sampled;
        enableRandomWrite = texture.RandomWriteEnabled;
        sampleCount = texture.SampleCount;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is not RenderTextureDescription key)
            return false;

        if (width != key.width || height != key.height)
            return false;

        if (depthBufferFormat != key.depthBufferFormat)
            return false;

        if (colorBufferFormats == null != (key.colorBufferFormats == null))
            return false;

        if (!colorBufferFormats.SequenceEqual(key.colorBufferFormats))
            return false;

        if (key.sampled != sampled)
            return false;

        if (key.enableRandomWrite != enableRandomWrite)
            return false;

        if (key.sampleCount != sampleCount)
            return false;

        return true;
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(width);
        hash.Add(height);
        hash.Add(depthBufferFormat);

        foreach (var format in colorBufferFormats)
            hash.Add(format.GetHashCode());

        hash.Add(enableRandomWrite);

        return hash.ToHashCode();
    }

    public static bool operator ==(RenderTextureDescription left, RenderTextureDescription right) => left.Equals(right);

    public static bool operator !=(RenderTextureDescription left, RenderTextureDescription right) => !(left == right);
}

public sealed class RenderTexture : EngineObject, ISerializable
{
    // Since Veldrid does not provide any methods to check how many color attachments a framebuffer supports, we can cap it ourselves to a reasonable value.
    const int colorAttachmentLimit = 8;

    public Framebuffer Framebuffer { get; private set; }

    public Texture2D[] ColorBuffers { get; private set; }

    public Texture2D DepthBuffer { get; private set; }

    public uint Width { get; private set; }
    public uint Height { get; private set; }

    public bool Sampled { get; private set; }
    public bool RandomWriteEnabled { get; private set; }

    public TextureSampleCount SampleCount { get; private set; }


    public RenderTexture(RenderTextureDescription description) : this(
        description.width,
        description.height,
        description.colorBufferFormats,
        description.depthBufferFormat,
        description.sampled,
        description.enableRandomWrite,
        description.sampleCount
    )
    { }

    /// <summary>
    /// Creates a new RenderTexture object
    /// </summary>
    /// <param name="width">The width of the <see cref="RenderTexture"/> and its internal buffers.</param>
    /// <param name="height">The height of the <see cref="RenderTexture"/> and its internal buffers.</param>
    /// <param name="colorFormats">The format of the color buffer(s) in the <see cref="RenderTexture"/>. Passing null or empty will omit the creation of a color buffer.</param>
    /// <param name="depthFormat">The format of the depth stencil buffer in the <see cref="RenderTexture"/>. Passing null or empty will omit the creation of the depth stencil buffer.</param>
    /// <param name="enableRandomWrite">Enable random reads/writes to the <see cref="RenderTexture"/> internal buffers. This is useful within compute shaders which draw to the texture.</param>
    public RenderTexture(
        uint width, uint height,
        PixelFormat[] colorFormats = null,
        PixelFormat? depthFormat = null,
        bool sampled = false,
        bool enableRandomWrite = false,
        TextureSampleCount sampleCount = TextureSampleCount.Count1
    ) : base("RenderTexture")
    {
        if (colorFormats != null && colorFormats.Length > colorAttachmentLimit)
            throw new Exception($"Invalid number of color buffers! [0-{colorAttachmentLimit}]");

        Width = width;
        Height = height;
        Sampled = sampled;
        RandomWriteEnabled = enableRandomWrite;
        SampleCount = sampleCount;

        if (depthFormat != null)
        {
            TextureUsage depthUsage = sampled ? TextureUsage.Sampled | TextureUsage.DepthStencil : TextureUsage.DepthStencil;
            DepthBuffer = new Texture2D(Width, Height, 1, depthFormat.Value, depthUsage)
            {
                Name = $"RT Depth Buffer"
            };
        }

        ColorBuffers = new Texture2D[colorFormats.Length];
        if (colorFormats != null)
        {
            TextureUsage sampleType = enableRandomWrite ? TextureUsage.Storage : TextureUsage.Sampled;
            TextureUsage colorUsage = sampled ? sampleType | TextureUsage.RenderTarget : TextureUsage.RenderTarget;

            for (int i = 0; i < ColorBuffers.Length; i++)
            {
                ColorBuffers[i] = new Texture2D(Width, Height, 1, colorFormats[i], colorUsage, sampleCount)
                {
                    Name = $"RT Color Buffer {i}"
                };
            }
        }

        FramebufferDescription description = new FramebufferDescription(DepthBuffer?.InternalTexture, ColorBuffers.Select(x => x.InternalTexture).ToArray());

        Framebuffer = Graphics.Factory.CreateFramebuffer(description);
    }

    public override void OnDispose()
    {
        DepthBuffer?.Dispose();

        if (ColorBuffers != null)
            foreach (var tex in ColorBuffers)
                tex?.Dispose();

        Framebuffer?.Dispose();

        DepthBuffer = null;
        ColorBuffers = null;
        Framebuffer = null;
    }

    public SerializedProperty Serialize(Serializer.SerializationContext ctx)
    {
        SerializedProperty compoundTag = SerializedProperty.NewCompound();

        SerializeHeader(compoundTag);

        compoundTag.Add("Width", new(Width));
        compoundTag.Add("Height", new(Height));
        compoundTag.Add("EnableRandomWrite", new(RandomWriteEnabled));

        compoundTag.Add("DepthBufferFormat", new(DepthBuffer != null ? (int)DepthBuffer.Format : -1));

        SerializedProperty colorBuffersTag = SerializedProperty.NewList();

        if (ColorBuffers != null)
        {
            foreach (var colorBuffer in ColorBuffers)
                colorBuffersTag.ListAdd(new((int)colorBuffer.Format));
        }

        compoundTag.Add("ColorBufferFormats", colorBuffersTag);

        return compoundTag;
    }

    public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
    {
        DeserializeHeader(value);

        uint width = (uint)value["Width"].IntValue;
        uint height = (uint)value["Height"].IntValue;
        bool randomWrite = value["EnableRandomWrite"].BoolValue;

        int depthFormatInt = value["DepthBufferFormat"].IntValue;
        PixelFormat? depthBufferFormat = depthFormatInt < 0 ? null : (PixelFormat)depthFormatInt;

        var colorBuffersTag = value.Get("ColorBufferFormats");
        PixelFormat[] colorBufferFormats = new PixelFormat[colorBuffersTag.Count];

        for (int i = 0; i < colorBuffersTag.Count; i++)
        {
            int colorFormatInt = colorBuffersTag[i].IntValue;
            colorBufferFormats[i] = (PixelFormat)colorFormatInt;
        }

        var param = new[] { typeof(uint), typeof(uint), typeof(PixelFormat?), typeof(PixelFormat[]), typeof(bool) };
        var values = new object[] { width, height, depthBufferFormat, colorBufferFormats, randomWrite };
        typeof(RenderTexture).GetConstructor(param).Invoke(this, values);
    }

    struct TextureFormatComparer : IEqualityComparer<Texture2D>
    {
        public readonly bool Equals(Texture2D? x, Texture2D? y)
        {
            return x.Format == y.Format;
        }

        public readonly int GetHashCode([DisallowNull] Texture2D obj)
        {
            return obj.Format.GetHashCode();
        }
    }

    public bool FormatEquals(RenderTexture other, bool compareMS = true)
    {
        if (Width != other.Width || Height != other.Height)
            return false;

        if (DepthBuffer.Format != other.DepthBuffer.Format)
            return false;

        if (!ColorBuffers.SequenceEqual(other.ColorBuffers, new TextureFormatComparer()))
            return false;

        if (Sampled != other.Sampled)
            return false;

        if (RandomWriteEnabled != other.RandomWriteEnabled)
            return false;

        if (compareMS && SampleCount != other.SampleCount)
            return false;

        return true;
    }


    #region Pool

    private static readonly Dictionary<RenderTextureDescription, List<(RenderTexture, long frameCreated)>> pool = [];

    private const int MaxUnusedFrames = 10;

    public static RenderTexture GetTemporaryRT(
        uint width, uint height,
        PixelFormat? depthFormat,
        PixelFormat[] colorFormats,
        bool sampled = true, bool randomWrite = false,
        TextureSampleCount samples = TextureSampleCount.Count1)
    {
        return GetTemporaryRT(new RenderTextureDescription(width, height, depthFormat, colorFormats, sampled, randomWrite, samples));
    }

    public static RenderTexture GetTemporaryRT(RenderTextureDescription description)
    {
        if (pool.TryGetValue(description, out var list) && list.Count > 0)
        {
            int i = list.Count - 1;
            RenderTexture renderTexture = list[i].Item1;
            list.RemoveAt(i);
            return renderTexture;
        }

        return new RenderTexture(description);
    }

    public static void ReleaseTemporaryRT(RenderTexture renderTexture)
    {
        var key = new RenderTextureDescription(renderTexture);

        if (!pool.TryGetValue(key, out var list))
        {
            list = [];
            pool[key] = list;
        }

        list.Add((renderTexture, Time.frameCount));
    }

    public static void UpdatePool()
    {
        foreach (var pair in pool)
        {
            for (int i = pair.Value.Count - 1; i >= 0; i--)
            {
                var (renderTexture, frameCreated) = pair.Value[i];
                if (Time.frameCount - frameCreated > MaxUnusedFrames)
                {
                    renderTexture.Destroy();
                    pair.Value.RemoveAt(i);
                }
            }
        }
    }

    #endregion
}
