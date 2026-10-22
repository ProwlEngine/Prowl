// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Threading;

using GTexture = Prowl.Graphite.Texture;
using GTextureDescription = Prowl.Graphite.TextureDescription;
using GTextureView = Prowl.Graphite.TextureView;

namespace Prowl.Runtime.Resources;

/// <summary>
/// The base of every texture: its format, mip levels, sampling settings, and the Graphite texture holding its texels.
/// Headless there is no Graphite texture, and uploads and reads do nothing.
/// </summary>
public abstract class Texture : Asset
{
    [NotHeld] private GTexture? _gpu;
    [NotHeld] private GTextureView? _displayView;

    /// <summary>The Graphite texture, null when headless or before the texture has a size.</summary>
    public GTexture? GpuTexture { get { EnsureLoaded(); return _gpu; } }

    /// <summary>The Graphite texture without loading, null when not loaded, for code that runs every frame.</summary>
    internal GTexture? GpuIfLoaded => IsLoaded ? _gpu : null;

    /// <summary>
    /// A view of an sRGB texture's texels without the sRGB decode, for UI that composes in display space. Null when the
    /// texture is not sRGB, where the texture itself already samples undecoded.
    /// </summary>
    public GTextureView? DisplayView { get { EnsureLoaded(); return _displayView; } }

    internal GTextureView? DisplayViewIfLoaded => IsLoaded ? _displayView : null;

    /// <summary>Whether compute kernels may write this texture through an image. Set before the texture is first sized.</summary>
    private protected bool RandomWrite;

    private TextureFormat _format;
    private bool _srgb;
    private uint _mipCount = 1;
    private FilterMode _filter = FilterMode.Bilinear;
    private WrapMode _wrapU, _wrapV, _wrapW;
    private TextureBorderColor _borderColor;
    private uint _anisotropy;

    /// <summary>The texel format.</summary>
    public TextureFormat Format { get { EnsureLoaded(); return _format; } }

    /// <summary>Whether the texels are sRGB encoded colour, which the GPU decodes to linear when sampling. RGBA8, BC1, BC3 and ASTC have an sRGB form.</summary>
    public bool Srgb { get { EnsureLoaded(); return _srgb; } }

    /// <summary>Mip levels the texture holds, 1 when it has none.</summary>
    public uint MipCount { get { EnsureLoaded(); return _mipCount; } }

    public FilterMode Filter { get { EnsureLoaded(); return _filter; } set { EnsureLoaded(); _filter = value; } }
    public WrapMode WrapU { get { EnsureLoaded(); return _wrapU; } set { EnsureLoaded(); _wrapU = value; } }
    public WrapMode WrapV { get { EnsureLoaded(); return _wrapV; } set { EnsureLoaded(); _wrapV = value; } }
    public WrapMode WrapW { get { EnsureLoaded(); return _wrapW; } set { EnsureLoaded(); _wrapW = value; } }

    /// <summary>What a <see cref="WrapMode.Border"/> axis returns outside the texture.</summary>
    public TextureBorderColor BorderColor { get { EnsureLoaded(); return _borderColor; } set { EnsureLoaded(); _borderColor = value; } }

    /// <summary>Anisotropic filtering samples, 0 or 1 for none. Ignored by Point filtering.</summary>
    public uint Anisotropy { get { EnsureLoaded(); return _anisotropy; } set { EnsureLoaded(); _anisotropy = value; } }

    /// <summary>Sets the wrap of every axis.</summary>
    public void SetWrap(WrapMode wrap)
    {
        EnsureLoaded();
        _wrapU = _wrapV = _wrapW = wrap;
    }

    private protected TextureFormat FormatUnchecked => _format;
    private protected uint MipCountUnchecked => _mipCount;

    /// <summary>The sampler the texture's settings describe, which materials bind it with.</summary>
    public Graphite.SamplerDescription Sampler { get { EnsureLoaded(); return SamplerUnchecked; } }

    /// <summary>The sampler without loading, for code that runs every frame.</summary>
    internal Graphite.SamplerDescription SamplerUnchecked
        => GraphicsFormats.ToSampler(_filter, _wrapU, _wrapV, _wrapW, _mipCount, _anisotropy, _borderColor);

    internal Texture(TextureFormat format, bool srgb, WrapMode wrap) : base("New Texture")
    {
        SetFormat(format, srgb);
        _wrapU = _wrapV = _wrapW = wrap;
    }

    /// <summary>Whether this kind of texture can hold block compressed formats.</summary>
    private protected virtual bool SupportsCompressed => false;

    /// <summary>Changes the format, dropping the Graphite texture until the next allocation.</summary>
    private protected void SetFormat(TextureFormat format, bool srgb)
    {
        if (!Enum.IsDefined(format))
            throw new FormatException($"Invalid texture format {(int)format}");
        if (GraphicsFormats.IsCompressed(format) && !SupportsCompressed)
            throw new NotSupportedException($"{GetType().Name} cannot hold the compressed format {format}.");
        srgb &= GraphicsFormats.SupportsSrgb(format);
        if (_format != format || _srgb != srgb) ReplaceGpu(null);
        _format = format;
        _srgb = srgb;
    }

    /// <summary>Loads the settings a serialized texture carries.</summary>
    private protected void SetSampling(FilterMode filter, WrapMode u, WrapMode v, WrapMode w, uint anisotropy, TextureBorderColor border)
    {
        _filter = filter;
        _wrapU = u;
        _wrapV = v;
        _wrapW = w;
        _anisotropy = anisotropy;
        _borderColor = border;
    }

    /// <summary>
    /// Whether the texels are compressed in a format the GPU cannot sample, so they are expanded to RGBA8 on upload.
    /// Headless nothing is uploaded, so nothing needs expanding.
    /// </summary>
    private protected bool DecodesOnUpload => GraphicsFormats.IsCompressed(_format) && Graphics.Device != null && !CanSample(GraphicsFormats.ToPixelFormat(_format, _srgb));

    /// <summary>The Graphite format of this texture, RGBA8 when the GPU cannot sample its compressed format.</summary>
    private protected Graphite.PixelFormat GpuFormat => GraphicsFormats.ToPixelFormat(DecodesOnUpload ? TextureFormat.RGBA8 : _format, _srgb);

    private sealed record SampleSupport(Graphite.GraphicsDevice Device, ConcurrentDictionary<Graphite.PixelFormat, bool> Formats);
    private static SampleSupport? _sampleSupport;

    private static bool CanSample(Graphite.PixelFormat format)
    {
        Graphite.GraphicsDevice device = Graphics.Device!;
        SampleSupport? support = Volatile.Read(ref _sampleSupport);
        if (support == null || !ReferenceEquals(support.Device, device))
        {
            SampleSupport fresh = new(device, new());
            support = Interlocked.CompareExchange(ref _sampleSupport, fresh, support) == support ? fresh : Volatile.Read(ref _sampleSupport)!;
        }

        return support.Formats.GetOrAdd(format, f => device.GetPixelFormatSupport(f, Graphite.TextureType.Texture2D, Graphite.TextureUsage.Sampled));
    }

    /// <summary>The usage a Graphite texture of this texture's format needs.</summary>
    private protected Graphite.TextureUsage Usage
    {
        get
        {
            Graphite.TextureUsage usage = Graphite.TextureUsage.Sampled;
            if (GraphicsFormats.IsCompressed(_format)) return usage;
            if (GraphicsFormats.IsDepth(_format)) usage |= Graphite.TextureUsage.DepthStencil;
            else usage |= Graphite.TextureUsage.RenderTarget;
            if (RandomWrite) usage |= Graphite.TextureUsage.Storage;
            return usage;
        }
    }

    /// <summary>The Graphite description of this texture with the given mip levels.</summary>
    private protected abstract GTextureDescription Describe(uint mips);

    /// <summary>Mip levels a full chain of this texture's size has.</summary>
    private protected abstract uint FullMipCount { get; }

    /// <summary>Array layers the GPU holds, six per cube.</summary>
    private protected virtual uint GpuLayers => 1;

    /// <summary>Makes a new Graphite texture with the given mip levels, dropping the old one once the GPU is done with it. Nothing headless.</summary>
    private protected GTexture? Allocate(uint mips)
    {
        _mipCount = Math.Clamp(mips, 1, FullMipCount);
        if (Graphics.Device == null)
        {
            ReplaceGpu(null);
            return null;
        }
        GTexture texture = Graphics.Device.ResourceFactory.CreateTexture(Describe(_mipCount));
        texture.Name = Name;
        ReplaceGpu(texture);
        return texture;
    }

    private void ReplaceGpu(GTexture? texture)
    {
        Graphics.DeferDispose(_displayView);
        Graphics.DeferDispose(_gpu);
        _gpu = texture;
        _displayView = null;
        if (texture != null && GraphicsFormats.WithoutSrgb(texture.Format) is Graphite.PixelFormat linear)
            _displayView = Graphics.Device!.ResourceFactory.CreateTextureView(new Graphite.TextureViewDescription(texture, format: linear));
    }

    /// <summary>Uploads texels into a region of a mip and layer, expanding compressed blocks the GPU cannot sample.</summary>
    private protected void Upload(ReadOnlySpan<byte> data, uint x, uint y, uint z, uint width, uint height, uint depth, uint mip = 0, uint layer = 0)
    {
        if (_gpu == null) return;
        if (GraphicsFormats.IsCompressed(_format) && !Graphite.FormatSizeHelpers.IsCompressed(_gpu.Format))
            data = Aperture.TextureDecoder.Decode(data, (int)width, (int)height, GraphicsFormats.ToAperture(_format, _srgb));
        Graphics.Device!.UpdateTexture<byte>(_gpu, data, new Graphite.TextureRegion(x, y, z, width, height, depth, mip, layer));
    }

    /// <summary>Reads a mip and layer back to the CPU. Empty headless.</summary>
    private protected byte[] Download(uint mip = 0, uint layer = 0)
    {
        if (_gpu == null) return [];
        return Graphics.ReadTexture(_gpu, mip, layer);
    }

    /// <summary>Gives the texture a full mip chain if it lacks one, and fills every level from level 0. Compressed textures bring their own levels instead.</summary>
    public void GenerateMipmaps()
    {
        EnsureLoaded();
        if (GraphicsFormats.IsCompressed(_format))
            throw new InvalidOperationException($"A {_format} texture cannot filter its own mips, give it every level through its mip chain.");
        if (_mipCount < FullMipCount)
        {
            GTexture? old = _gpu;
            uint layers = GpuLayers;
            GTexture? chain = Allocate(FullMipCount);
            if (old != null && chain != null)
                Graphics.RunNow(cmd => cmd.CopyTexture(old, 0, 0, 0, 0, 0, chain, 0, 0, 0, 0, 0, old.Width, old.Height, old.Depth, layers), "Mip chain");
        }
        if (_gpu != null && _mipCount > 1)
        {
            GTexture texture = _gpu;
            Graphics.RunNow(cmd => cmd.GenerateMipmaps(texture), "Mipmaps");
        }
    }

    /// <summary>The size of a mip level along an axis.</summary>
    private protected static uint MipSize(uint size, uint mip) => Math.Max(1u, size >> (int)mip);

    /// <summary>Mip levels a full chain of the given size has.</summary>
    private protected static uint FullChain(uint width, uint height, uint depth = 1)
        => 1 + (uint)Math.Floor(Math.Log2(Math.Max(width, Math.Max(height, Math.Max(depth, 1u)))));

    protected override void OnUnload() => ReplaceGpu(null);

    // A runtime texture nothing references still has to free its GPU texture. Database textures are never collected.
    ~Texture()
    {
        if (!Registered) Dispose();
    }

    /// <summary>Bytes per texel of a format, zero for a compressed format.</summary>
    public static int GetBytesPerPixel(TextureFormat format) => GraphicsFormats.BytesPerTexel(format);
}
