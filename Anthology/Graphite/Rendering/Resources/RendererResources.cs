using System;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The GPU resources the renderer's frames, views and effects share, made once with the renderer: the bindless tables,
/// the lookup tables, the textures a view without a producer reads, one sampler set, the scene formats and the
/// renderer's own programs. Under <see cref="CompilePolicy.Async"/> the programs start compiling on the compile queue at
/// Fallback priority when made and their first use waits for them, as no frame draws without them.
/// </summary>
internal sealed class RendererResources : IDisposable
{
    private readonly FullscreenProgram _present, _presentOver;
    private readonly PassVariants _prefilter;
    private GraphicsProgram? _prefilterProgram;
    private readonly GraphicsDevice _device;
    private readonly PixelFormat[]?[] _sceneTargets = new PixelFormat[18][];

    public RendererResources(GraphicsDevice device, IShaderCompiler? compiler, CompilePolicy policy = CompilePolicy.Sync, Action<RendererError>? report = null)
    {
        _device = device;
        ResourceFactory factory = device.ResourceFactory;
        BlendStateDescription over = new(default, BlendAttachmentDescription.AlphaBlend with { SourceColorFactor = BlendFactor.One, SourceAlphaFactor = BlendFactor.One });
        BlendAttachmentDescription untouched = BlendAttachmentDescription.OverrideBlend with { ColorWriteMask = ColorWriteMask.None };
        _present = Essential(FullscreenProgram.Create(device, compiler, InternalShaders.Present, "Graphite/Present", policy: policy, priority: CompilePriority.Fallback));
        _presentOver = Essential(FullscreenProgram.Create(device, compiler, InternalShaders.Present, "Graphite/PresentOver", blend: over, policy: policy, priority: CompilePriority.Fallback));
        CopyBackProgram = Essential(FullscreenProgram.Create(device, compiler, InternalShaders.CopyBack, "Graphite/CopyBack",
            blend: new BlendStateDescription(default, BlendAttachmentDescription.OverrideBlend, untouched, untouched), policy: policy, priority: CompilePriority.Fallback));
        _prefilter = InternalShaders.FilePass("Internal/Prefilter.glsl").Variants(compiler, device.BackendType);
        if (policy == CompilePolicy.Async)
            _prefilter.Request(0, CompilePolicy.Async, CompilePriority.Fallback, out _, out _);
        else
            _prefilterProgram = Fullscreen(device, _prefilter);
        SkyAmbientProgram = InternalShaders.Compute(device, compiler, "Internal/SkyAmbient.glsl", policy);
        ExposureProgram = InternalShaders.Compute(device, compiler, "Internal/Exposure.glsl", policy);
        BlendAttachmentDescription written = BlendAttachmentDescription.OverrideBlend;
        BlendAttachmentDescription kept = written with { ColorWriteMask = ColorWriteMask.None };
        ShadingModels = new ShadingModels(device, compiler, policy, report);
        GBufferDecalsProgram = FullscreenProgram.Create(device, compiler, InternalShaders.PostFragment("GBufferDecals"), "Graphite/GBufferDecals", source: false,
            blend: BlendStateDescription.SingleOverrideBlend with { AttachmentStates = [written, written, written] }, lit: true, variant: "#define GRAPHITE_DECALS 1\n", policy: policy);
        HiZProgram = FullscreenProgram.Create(device, compiler, InternalShaders.PostFragment("HiZBuild"), "Graphite/HiZBuild", source: false, policy: policy);
        HiZPackProgram = FullscreenProgram.Create(device, compiler, InternalShaders.PostFragment("HiZBuild"), "Graphite/HiZBuild", source: false, variant: "#define HIZ_PACK 1\n", policy: policy);

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
        OpaqueHdrFormat = device.GetPixelFormatSupport(PixelFormat.R11_G11_B10_Float, TextureType.Texture2D, TextureUsage.RenderTarget | TextureUsage.Sampled)
            ? PixelFormat.R11_G11_B10_Float : SceneFormats[0];
        OpaqueDisplayFormat = device.GetPixelFormatSupport(PixelFormat.R10_G10_B10_A2_UNorm, TextureType.Texture2D, TextureUsage.RenderTarget | TextureUsage.Sampled)
            ? PixelFormat.R10_G10_B10_A2_UNorm : OpaqueHdrFormat;
        GBufferFormats =
        [
            PixelFormat.R8_G8_B8_A8_UNorm_SRgb, SceneFormats[SceneTarget.Normals], PixelFormat.R8_G8_B8_A8_UNorm, PixelFormat.R8_G8_B8_A8_UNorm,
            OpaqueHdrFormat, OpaqueHdrFormat, SceneFormats[SceneTarget.Motion],
        ];
        SupportsGBuffer = device.Limits.MaxColorAttachments >= GBufferFeature.TargetCount;
        SceneDepthFormat = ViewGraph.SceneDepth(format => device.GetPixelFormatSupport(format, TextureType.Texture2D, TextureUsage.DepthStencil | TextureUsage.Sampled));

        Noise = LoadData(device, "Noise.bin", 256, 256, mipmapped: true);
        Textures.RegisterDefault("noise", Noise);
        Dfg = LoadTable(device, "Dfg.bin", PixelFormat.R16_G16_B16_A16_Float, 128, 128, 1);
        DielectricE = LoadTable(device, "DielectricE.bin", PixelFormat.R16_Float, 32, 32, 32);
        AnisotropicDfg = LoadTable(device, "AnisotropicDfg.bin", PixelFormat.R16_G16_B16_A16_Float, 32, 16, 64);
        Ltc = LoadTable(device, "Ltc.bin", PixelFormat.R16_G16_B16_A16_Float, 64, 64, 1);

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
        NoFogVolume = factory.CreateTexture(TextureDescription.Texture3D(1, 1, 1, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        device.UpdateTexture<ushort>(NoFogVolume, [0, 0, 0, 0x3C00], TextureRegion.Whole(NoFogVolume, 0, 0));
        NoSpatialBins = factory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadOnly));
        device.UpdateBuffer<uint>(NoSpatialBins, 0, new uint[64]);
        NoVoxels = factory.CreateBuffer(new BufferDescription(VoxelHeader.Size + 4, BufferUsage.StructuredBufferReadWrite));
        device.UpdateBuffer(NoVoxels, 0, new VoxelHeader());
        NoCube = factory.CreateTexture(TextureDescription.CreateCube(1, 1, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        for (uint face = 0; face < 6; face++)
            device.UpdateTexture<ushort>(NoCube, [0, 0, 0, 0x3C00], TextureRegion.Whole(NoCube, 0, face));
        NoExposure = factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled));
        device.UpdateTexture<float>(NoExposure, [0f], TextureRegion.Whole(NoExposure, 0, 0));
    }

    // Captures draw through the layered form on the first frame, so it compiles alongside the mono one
    private static FullscreenProgram Essential(FullscreenProgram program)
    {
        program.Ready(6);
        return program;
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

    /// <summary>The formats of a GBuffer's targets, in the order of <see cref="GBufferTarget"/>.</summary>
    public PixelFormat[] GBufferFormats { get; }

    /// <summary>Whether the device draws to as many targets as a GBuffer has. Deferred views draw forward where it does not.</summary>
    public bool SupportsGBuffer { get; }

    /// <summary>The HDR colour format of views that keep no alpha, half the size of the scene's where the device draws to it.</summary>
    public PixelFormat OpaqueHdrFormat { get; }

    /// <summary>The format of display encoded colour on views that keep no alpha, 10 bits a channel where the device draws to it.</summary>
    public PixelFormat OpaqueDisplayFormat { get; }

    /// <summary>The first count scene formats with the colour in the given one, made once each.</summary>
    public PixelFormat[] SceneTargetFormats(int count, PixelFormat color)
    {
        int index = count * 3 + (color == ViewGraph.LdrColorFormat ? 1 : color == SceneFormats[0] ? 0 : 2);
        if (_sceneTargets[index] is { } formats)
            return formats;
        formats = SceneFormats[..count];
        formats[0] = color;
        return _sceneTargets[index] = formats;
    }

    /// <summary>The noise texture: blue noise in red, white noise in green, voronoi in blue, 256 square, mipmapped.</summary>
    public Texture Noise { get; }

    /// <summary>The GGX energy tables of Graphite/PBR/Energy.glsl, generated and verified by the rendering tests.</summary>
    public Texture Dfg { get; }
    public Texture DielectricE { get; }
    public Texture AnisotropicDfg { get; }
    public Texture Ltc { get; }

    /// <summary>The screen lighting a view without a producer reads: full visibility, no irradiance, six layers.</summary>
    public Texture NoScreenVisibility { get; }
    public Texture NoScreenIrradiance { get; }

    /// <summary>The scene colour a view without a copy reads: black, six layers.</summary>
    public Texture NoSceneColor { get; }
    public Texture NoSceneDepth { get; }

    /// <summary>The fog volume a view without volumetric fog reads: no light and everything let through.</summary>
    public Texture NoFogVolume { get; }

    /// <summary>What views binning nothing bind as their bins and binned records: all zero, a header of no tiles, so no items.</summary>
    public DeviceBuffer NoSpatialBins { get; }

    /// <summary>What views other than a world's voxel view bind as the voxel volume, empty.</summary>
    public DeviceBuffer NoVoxels { get; }

    /// <summary>A black cube for passes reading a sky capture a world does not have yet.</summary>
    public Texture NoCube { get; }

    /// <summary>What the exposure pass reads for a view an AutoExposure has not measured yet.</summary>
    public Texture NoExposure { get; }

    /// <summary>
    /// Copies the view's colour to its target with its rows reversed, the first row at the bottom of clip space, decoded
    /// or encoded as Graphite_PresentMode asks. Over blends it as premultiplied by its alpha.
    /// </summary>
    public FullscreenProgram Present(bool over) => over ? _presentOver : _present;

    /// <summary>Copies a colour features wrote back into the scene's colour, leaving its G-buffer.</summary>
    public FullscreenProgram CopyBackProgram { get; }

    /// <summary>Filters a captured cube into a roughness mip of a prefiltered one, waiting for its compile the first time. Render thread.</summary>
    public GraphicsProgram PrefilterProgram => _prefilterProgram ??= Fullscreen(_device, _prefilter);

    public LazyCompute SkyAmbientProgram { get; }
    public LazyCompute ExposureProgram { get; }

    /// <summary>A level of a view's depth pyramid from the one below, and the copy of a level into its packed texture. See <see cref="HiZFeature"/>.</summary>
    public FullscreenProgram HiZProgram { get; }

    /// <summary>The renderer's shading models and the program that lights a deferred view's GBuffer by them.</summary>
    public ShadingModels ShadingModels { get; }

    /// <summary>Applies the world's decals to a GBuffer, see Graphite/Internal/GBufferDecals.glsl.</summary>
    public FullscreenProgram GBufferDecalsProgram { get; }

    public ResourceFactory Factory => _device.ResourceFactory;
    public FullscreenProgram HiZPackProgram { get; }

    private static GraphicsProgram Fullscreen(GraphicsDevice device, PassVariants variants)
    {
        ShaderDescription description = variants.Require();
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);
        program.Name = variants.Pass.SourceName;
        return program;
    }

    /// <summary>The first bytes of a file of the library's data.</summary>
    internal static byte[] ReadData(string file, uint length) => LibraryData.Read(typeof(RendererResources).Assembly, "Graphite/Data/" + file, length);

    // An RGBA8 texture from the library's data, in its stored row order, in every layer, mipmapped on the GPU when asked,
    // its colour sRGB encoded when asked, its pixels changed by edit first when given
    internal static Texture LoadData(GraphicsDevice device, string file, uint width, uint height, bool mipmapped, uint layers = 1, bool srgb = false, Action<byte[]>? edit = null)
        => LibraryData.LoadTexture(device, typeof(RendererResources).Assembly, "Graphite/Data/" + file, width, height, mipmapped, layers, srgb, edit);

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
        _prefilterProgram?.Dispose();
        SkyAmbientProgram.Dispose();
        ExposureProgram.Dispose();
        HiZProgram.Dispose();
        ShadingModels.Dispose();
        GBufferDecalsProgram.Dispose();
        HiZPackProgram.Dispose();
        Textures.Dispose();
        Noise.Dispose();
        Dfg.Dispose();
        DielectricE.Dispose();
        AnisotropicDfg.Dispose();
        Ltc.Dispose();
        NoScreenVisibility.Dispose();
        NoScreenIrradiance.Dispose();
        NoSceneColor.Dispose();
        NoSceneDepth.Dispose();
        NoFogVolume.Dispose();
        NoSpatialBins.Dispose();
        NoVoxels.Dispose();
        NoCube.Dispose();
        NoExposure.Dispose();
        LinearClamp.Dispose();
        PointClamp.Dispose();
        LinearRepeat.Dispose();
    }
}
