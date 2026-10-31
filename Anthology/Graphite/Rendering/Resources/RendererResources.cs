using System;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The GPU resources the renderer's frames, views and effects share, made once with the renderer: the bindless tables,
/// the lookup tables, the textures a view without a producer reads, one sampler set, the scene formats and the
/// renderer's own programs.
/// </summary>
internal sealed class RendererResources : IDisposable
{
    private readonly FullscreenProgram _present, _presentOver;
    private readonly Texture _smaaArea, _smaaSearch;
    private readonly PixelFormat[]?[] _sceneTargets = new PixelFormat[12][];

    public RendererResources(GraphicsDevice device, IShaderCompiler? compiler)
    {
        ResourceFactory factory = device.ResourceFactory;
        SamplerDescription clamp = SamplerDescription.Linear with
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
        };
        LinearClamp = factory.CreateSampler(clamp);
        PointClamp = factory.CreateSampler(clamp with { MinFilter = FilterMode.Point, MagFilter = FilterMode.Point, MipFilter = FilterMode.Point });
        LinearRepeat = factory.CreateSampler(SamplerDescription.Linear);
        Textures = new RendererTextures(device, LinearRepeat);

        SceneFormats = ViewGraph.SceneFormats(format => device.GetPixelFormatSupport(format, TextureType.Texture2D, TextureUsage.RenderTarget | TextureUsage.Sampled));
        SceneDepthFormat = ViewGraph.SceneDepth(format => device.GetPixelFormatSupport(format, TextureType.Texture2D, TextureUsage.DepthStencil | TextureUsage.Sampled));

        Noise = LoadData(device, "Noise.bin", 256, 256, mipmapped: true);
        Textures.RegisterDefault("noise", Noise);
        _smaaArea = LoadData(device, "SmaaArea.bin", 160, 560, mipmapped: false, layers: 6);
        _smaaSearch = LoadData(device, "SmaaSearch.bin", 64, 16, mipmapped: false, layers: 6);
        SmaaArea = factory.CreateTextureView(new TextureViewDescription(_smaaArea, arrayLayers: 1));
        SmaaSearch = factory.CreateTextureView(new TextureViewDescription(_smaaSearch, arrayLayers: 1));
        SmaaAreaLayered = factory.CreateTextureView(new TextureViewDescription(_smaaArea));
        SmaaSearchLayered = factory.CreateTextureView(new TextureViewDescription(_smaaSearch));
        Dfg = LoadTable(device, "Dfg.bin", PixelFormat.R16_G16_B16_A16_Float, 128, 128, 1);
        DielectricE = LoadTable(device, "DielectricE.bin", PixelFormat.R16_Float, 32, 32, 32);
        AnisotropicDfg = LoadTable(device, "AnisotropicDfg.bin", PixelFormat.R16_G16_B16_A16_Float, 32, 16, 64);

        NoScreenVisibility = factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 6, PixelFormat.R8_UNorm, TextureUsage.Sampled));
        NoScreenIrradiance = factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 6, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        NoSceneColor = factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 6, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        NoSceneDepth = factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 6, PixelFormat.R32_Float, TextureUsage.Sampled));
        for (uint layer = 0; layer < 6; layer++)
        {
            device.UpdateTexture<byte>(NoScreenVisibility, [255], TextureRegion.Whole(NoScreenVisibility, 0, layer));
            device.UpdateTexture<ushort>(NoScreenIrradiance, [0, 0, 0, 0], TextureRegion.Whole(NoScreenIrradiance, 0, layer));
            device.UpdateTexture<ushort>(NoSceneColor, [0, 0, 0, 0], TextureRegion.Whole(NoSceneColor, 0, layer));
            device.UpdateTexture<float>(NoSceneDepth, [0f], TextureRegion.Whole(NoSceneDepth, 0, layer));
        }
        NoExposure = factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled));
        device.UpdateTexture<float>(NoExposure, [0f], TextureRegion.Whole(NoExposure, 0, 0));

        BlendStateDescription over = new(default, BlendAttachmentDescription.AlphaBlend with { SourceColorFactor = BlendFactor.One, SourceAlphaFactor = BlendFactor.One });
        _present = FullscreenProgram.Create(device, compiler, InternalShaders.Present, "Graphite/Present");
        _presentOver = FullscreenProgram.Create(device, compiler, InternalShaders.Present, "Graphite/PresentOver", blend: over);
        BlendAttachmentDescription untouched = BlendAttachmentDescription.OverrideBlend with { ColorWriteMask = ColorWriteMask.None };
        CopyBackProgram = FullscreenProgram.Create(device, compiler, InternalShaders.CopyBack, "Graphite/CopyBack",
            blend: new BlendStateDescription(default, BlendAttachmentDescription.OverrideBlend, untouched, untouched));
        PrefilterProgram = Fullscreen(device, compiler, "Internal/Prefilter.glsl");
        SkyAmbientProgram = InternalShaders.Compute(device, compiler, "Internal/SkyAmbient.glsl");
        ExposureProgram = InternalShaders.Compute(device, compiler, "Internal/Exposure.glsl");
    }

    /// <summary>The bindless textures and samplers of materials and globals.</summary>
    public RendererTextures Textures { get; }

    /// <summary>The renderer's bindless texture table, set 0 of every material program.</summary>
    public DescriptorTable TextureTable => Textures.Table;

    public Sampler LinearClamp { get; }
    public Sampler PointClamp { get; }
    public Sampler LinearRepeat { get; }

    /// <summary>The formats of a scene's targets the device draws and samples: colour, motion, reactive, normals and material.</summary>
    public PixelFormat[] SceneFormats { get; }
    public PixelFormat SceneDepthFormat { get; }

    /// <summary>The first count scene formats, the colour LDR when asked, made once each.</summary>
    public PixelFormat[] SceneTargetFormats(int count, bool ldr)
    {
        int index = count * 2 + (ldr ? 1 : 0);
        if (_sceneTargets[index] is { } formats)
            return formats;
        formats = SceneFormats[..count];
        if (ldr)
            formats[0] = ViewGraph.LdrColorFormat;
        return _sceneTargets[index] = formats;
    }

    /// <summary>The noise texture: blue noise in red, white noise in green, voronoi in blue, 256 square, mipmapped.</summary>
    public Texture Noise { get; }

    /// <summary>The SMAA reference's area and search tables, RGBA8 in its row order, one layer for views of one layer.</summary>
    public TextureView SmaaArea { get; }
    public TextureView SmaaSearch { get; }

    /// <summary>The SMAA tables in each of six layers, for layered views.</summary>
    public TextureView SmaaAreaLayered { get; }
    public TextureView SmaaSearchLayered { get; }

    /// <summary>The GGX energy tables of Graphite/PBR/Energy.glsl, generated and verified by the rendering tests.</summary>
    public Texture Dfg { get; }
    public Texture DielectricE { get; }
    public Texture AnisotropicDfg { get; }

    /// <summary>The screen lighting a view without a producer reads: full visibility, no irradiance, six layers.</summary>
    public Texture NoScreenVisibility { get; }
    public Texture NoScreenIrradiance { get; }

    /// <summary>The scene colour a view without a copy reads: black, six layers.</summary>
    public Texture NoSceneColor { get; }
    public Texture NoSceneDepth { get; }

    /// <summary>What the exposure pass reads for a view an AutoExposure has not measured yet.</summary>
    public Texture NoExposure { get; }

    /// <summary>
    /// Copies the view's colour to its target with its rows reversed, the first row at the bottom of clip space, decoded
    /// or encoded as Graphite_PresentMode asks. Over blends it as premultiplied by its alpha.
    /// </summary>
    public FullscreenProgram Present(bool over) => over ? _presentOver : _present;

    /// <summary>Copies a colour features wrote back into the scene's colour, leaving its G-buffer.</summary>
    public FullscreenProgram CopyBackProgram { get; }

    public GraphicsProgram PrefilterProgram { get; }
    public ComputeProgram SkyAmbientProgram { get; }
    public ComputeProgram ExposureProgram { get; }

    private static GraphicsProgram Fullscreen(GraphicsDevice device, IShaderCompiler? compiler, string file)
    {
        ShaderDescription description = InternalShaders.FilePass(file).Variants(compiler, device.BackendType).Require();
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);
        program.Name = ShaderLibrary.Prefix + file;
        return program;
    }

    // An RGBA8 texture from the library's data, in its stored row order, in every layer, mipmapped on the GPU when asked
    private static Texture LoadData(GraphicsDevice device, string file, uint width, uint height, bool mipmapped, uint layers = 1)
    {
        using System.IO.Stream stream = typeof(RendererResources).Assembly.GetManifestResourceStream("Graphite/Data/" + file)
            ?? throw new InvalidOperationException($"The library has no Data/{file}.");
        byte[] pixels = new byte[width * height * 4];
        stream.ReadExactly(pixels);
        uint mips = mipmapped ? (uint)Math.Log2(Math.Max(width, height)) + 1 : 1;
        Texture texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, height, mips, layers, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        texture.Name = "Graphite " + System.IO.Path.GetFileNameWithoutExtension(file);
        for (uint layer = 0; layer < layers; layer++)
            device.UpdateTexture<byte>(texture, pixels, TextureRegion.Whole(texture, 0, layer));
        if (mipmapped)
            device.Record(cmd => cmd.GenerateMipmaps(texture)).Wait();
        return texture;
    }

    // A half float table from the library's data, x fastest, then y, then z
    private static Texture LoadTable(GraphicsDevice device, string file, PixelFormat format, uint width, uint height, uint depth)
    {
        using System.IO.Stream stream = typeof(RendererResources).Assembly.GetManifestResourceStream("Graphite/Data/" + file)
            ?? throw new InvalidOperationException($"The library has no Data/{file}.");
        uint channels = format == PixelFormat.R16_Float ? 1u : 4u;
        ushort[] texels = new ushort[width * height * depth * channels];
        stream.ReadExactly(MemoryMarshal.AsBytes(texels.AsSpan()));
        TextureDescription description = depth > 1
            ? TextureDescription.Texture3D(width, height, depth, 1, format, TextureUsage.Sampled)
            : TextureDescription.Texture2D(width, height, 1, 1, format, TextureUsage.Sampled);
        Texture texture = device.ResourceFactory.CreateTexture(description);
        texture.Name = "Graphite " + System.IO.Path.GetFileNameWithoutExtension(file);
        device.UpdateTexture<ushort>(texture, texels, TextureRegion.Whole(texture, 0, 0));
        return texture;
    }

    public void Dispose()
    {
        _present.Dispose();
        _presentOver.Dispose();
        CopyBackProgram.Dispose();
        PrefilterProgram.Dispose();
        SkyAmbientProgram.Dispose();
        ExposureProgram.Dispose();
        Textures.Dispose();
        Noise.Dispose();
        SmaaArea.Dispose();
        SmaaSearch.Dispose();
        SmaaAreaLayered.Dispose();
        SmaaSearchLayered.Dispose();
        _smaaArea.Dispose();
        _smaaSearch.Dispose();
        Dfg.Dispose();
        DielectricE.Dispose();
        AnisotropicDfg.Dispose();
        NoScreenVisibility.Dispose();
        NoScreenIrradiance.Dispose();
        NoSceneColor.Dispose();
        NoSceneDepth.Dispose();
        NoExposure.Dispose();
        LinearClamp.Dispose();
        PointClamp.Dispose();
        LinearRepeat.Dispose();
    }
}
