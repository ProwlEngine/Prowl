using System;
using System.Text;

using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.Samples;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal readonly struct SceneView : IRenderView
{
    public bool TargetSwapchain => true;

    public SceneView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}


// Renders the model into an offscreen "Scene" target, then runs a two-step bloom (downsample, then
// upsample) over it into "BloomFull", and finally composites Scene + BloomFull to the swapchain. The
// graph orders the four passes from their declared texture reads/writes; nothing here manually tracks
// dependency order.
internal sealed class ScenePass : RasterPass<SceneView>
{
    private readonly ModelAsset _model;
    private readonly GraphicsProgram _shader;
    private readonly PropertySet _properties;

    private Float3 _center;
    private float _distance;
    private float _angle;

    public ScenePass(ModelAsset model, GraphicsProgram shader, PropertySet properties)
    {
        _model = model;
        _shader = shader;
        _properties = properties;
        _center = model.Bounds.Center;
        _distance = Float3.Length(model.Bounds.Extents) * 3.0f;
    }

    public override string Name => "Scene";

    public void Advance(float dt) => _angle += dt * 0.5f;

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, "Scene", GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm, depth: true), ops: TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)));

    public override void Render(RenderContext<SceneView> context, CommandBuffer cmd)
    {
        float radius = Math.Max(_distance, 0.001f);
        Float3 eye = _center + new Float3(MathF.Sin(_angle), 0.35f, MathF.Cos(_angle)) * _distance;

        Float4x4 projection = Float4x4.CreatePerspectiveFov(1.0472f, 1.0f, radius * 0.02f, radius * 10.0f);
        Float4x4 view = Float4x4.CreateLookAt(eye, _center, Float3.UnitY);
        _properties.SetMatrix("MatrixMVP", projection * view);

        BindTarget(context, cmd);
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_model.Mesh);
        cmd.SetProperties(_properties);
        cmd.DrawIndexed();
    }
}


internal sealed class BloomDownsamplePass : RasterPass<SceneView>
{
    private readonly ShaderPass _bloomShader;
    private readonly Sampler _sampler;
    private readonly PropertySet _properties = new();
    private static readonly Keyword UpsampleOff = new("Upsample", "false");

    public BloomDownsamplePass(ShaderPass bloomShader, Sampler sampler)
    {
        _bloomShader = bloomShader;
        _sampler = sampler;
    }

    private TextureHandle _sceneHandle;
    private TextureHandle _bloomHalfHandle;

    public override string Name => "BloomDownsample";

    public override void Setup(RenderContextBuilder builder)
    {
        _sceneHandle = builder.DeclareInputTexture("Scene");
        _bloomHalfHandle = SetTarget(builder, "BloomHalf", GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm, 0.5f));
    }

    public override void Render(RenderContext<SceneView> context, CommandBuffer cmd)
    {
        RenderTexture scene = context.GetRenderTexture(_sceneHandle);
        RenderTexture bloomHalf = context.GetRenderTexture(_bloomHalfHandle);

        BindTarget(context, cmd);

        _properties.SetTexture("sourceTexture", scene.ColorTextures[0], _sampler);
        _properties.SetFloat2("halfPixel", new Float2(0.5f / bloomHalf.Desc.Width, 0.5f / bloomHalf.Desc.Height));
        _properties.SetFloat("offset", 1f);

        cmd.SetShader(_bloomShader, [UpsampleOff]);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }
}


internal sealed class BloomUpsamplePass : RasterPass<SceneView>
{
    private readonly ShaderPass _bloomShader;
    private readonly Sampler _sampler;
    private readonly PropertySet _properties = new();
    private static readonly Keyword UpsampleOn = new("Upsample", "true");

    public BloomUpsamplePass(ShaderPass bloomShader, Sampler sampler)
    {
        _bloomShader = bloomShader;
        _sampler = sampler;
    }

    private TextureHandle _bloomHalfHandle;
    private TextureHandle _bloomFullHandle;

    public override string Name => "BloomUpsample";

    public override void Setup(RenderContextBuilder builder)
    {
        _bloomHalfHandle = builder.DeclareInputTexture("BloomHalf");
        _bloomFullHandle = SetTarget(builder, "BloomFull", GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm, 1f));
    }

    public override void Render(RenderContext<SceneView> context, CommandBuffer cmd)
    {
        RenderTexture bloomHalf = context.GetRenderTexture(_bloomHalfHandle);
        RenderTexture bloomFull = context.GetRenderTexture(_bloomFullHandle);

        BindTarget(context, cmd);

        _properties.SetTexture("sourceTexture", bloomHalf.ColorTextures[0], _sampler);
        _properties.SetFloat2("halfPixel", new Float2(0.5f / bloomFull.Desc.Width, 0.5f / bloomFull.Desc.Height));
        _properties.SetFloat("offset", 1f);

        cmd.SetShader(_bloomShader, [UpsampleOn]);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }
}


internal sealed class CompositePass : RasterPass<SceneView>
{
    private readonly GraphicsProgram _compositeShader;
    private readonly Sampler _sampler;
    private readonly PropertySet _properties = new();

    private TextureHandle _sceneHandle;
    private TextureHandle _bloomFullHandle;

    public CompositePass(GraphicsProgram compositeShader, Sampler sampler)
    {
        _compositeShader = compositeShader;
        _sampler = sampler;
    }

    public override string Name => "Composite";

    public override void Setup(RenderContextBuilder builder)
    {
        _sceneHandle = builder.DeclareInputTexture("Scene");
        _bloomFullHandle = builder.DeclareInputTexture("BloomFull");
        SetViewTarget(builder);
    }

    public override void Render(RenderContext<SceneView> context, CommandBuffer cmd)
    {
        RenderTexture scene = context.GetRenderTexture(_sceneHandle);
        RenderTexture bloomFull = context.GetRenderTexture(_bloomFullHandle);

        BindTarget(context, cmd);

        _properties.SetTexture("sceneTexture", scene.ColorTextures[0], _sampler);
        _properties.SetTexture("bloomTexture", bloomFull.ColorTextures[0], _sampler);
        _properties.SetFloat("bloomIntensity", 0.6f);

        cmd.SetShader(_compositeShader);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }
}


public static class Program
{
    static GraphicsDevice device;
    static RenderMSTracker tracker;

    static ModelAsset model;
    static GraphicsProgram unlitShader;
    static GraphicsProgram compositeShader;
    static ShaderDefinition bloomDef;
    static ShaderPass bloomShader;
    static PropertySet sceneProperties;
    static Sampler bloomSampler;
    static Sampler compositeSampler;
    static Texture albedo;

    static RenderPipeline<SceneView> pipeline;
    static ScenePass scenePass;
    static SceneView[] views;


    private static void Main()
    {
        GraphicsDeviceOptions options = new()
        {
            VulkanValidationLayers = false,
        };

        SwapchainDescription swapchain = new()
        {
            DepthFormat = PixelFormat.D24_UNorm_S8_UInt,
            SyncToVerticalBlank = false
        };

        DeviceCreateUtilities.CreateWindowAndDevice(Load, Render, Close, options, swapchain);
    }


    public static void Load(GraphicsDevice newDevice)
    {
        device = newDevice;

        tracker = new(newDevice);

        unlitShader = ShaderDefLoader.Load(device, "Shaders/Unlit.shader");

        model = ModelAsset.Load(device, "Assets/Models/DamagedHelmet.glb", unwrapLightmapUVs: false);

        MaterialInfo material = model.Materials.Length > 0 ? model.Materials[0] : default;
        albedo = material.AlbedoTexture ?? model.GetDefaultWhite();

        sceneProperties = new();
        sceneProperties.SetTexture("AlbedoTexture", albedo, bloomSampler);
        sceneProperties.SetFloat4("BaseColor", new Float4(1, 1, 1, 1));

        bloomShader = LoadBloomShaderPass(device);
        compositeShader = ShaderDefLoader.Load(device, "Shaders/Composite.shader");

        SamplerDescription clampLinear = new()
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            Filter = SamplerFilter.MinLinear_MagLinear_MipLinear,
        };
        bloomSampler = device.ResourceFactory.CreateSampler(clampLinear);
        compositeSampler = device.ResourceFactory.CreateSampler(clampLinear);

        scenePass = new(model, unlitShader, sceneProperties);
        BloomDownsamplePass bloomDown = new(bloomShader, bloomSampler);
        BloomUpsamplePass bloomUp = new(bloomShader, bloomSampler);
        CompositePass composite = new(compositeShader, compositeSampler);

        pipeline = new([scenePass, bloomDown, bloomUp, composite]);
        views = new[] { new SceneView(600, 600) };
    }


    private static ShaderPass LoadBloomShaderPass(GraphicsDevice device)
    {
        SlangShaderCompiler compiler = new();
        compiler.RegisterModule(device.BackendType switch
        {
            GraphicsBackend.Vulkan => new VulkanCompiler("spirv_1_4"),
            _ => throw new NotSupportedException($"Unsupported graphics backend: {device.BackendType}")
        });

        compiler.BeginSession(FileLoader.SearchDirectories, FileLoader.Load);

        Memory<byte>? loaded = FileLoader.Load("Shaders/Bloom.shader");
        string source = Encoding.UTF8.GetString(loaded!.Value.Span);
        bloomDef = ShaderParser.Parse(source);
        bloomDef.Create(device, compiler, new Variant(), CompileMode.All);

        compiler.EndSession();

        return bloomDef.Passes![0];
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        scenePass.Advance((float)dt);
        device.DispatchGraph(pipeline, views);

        tracker.End(dt);
    }


    public static void Close()
    {
        pipeline.Dispose();
        bloomSampler.Dispose();
        compositeSampler.Dispose();
        compositeShader.Dispose();
        unlitShader.Dispose();
        model.Dispose();
        device.Dispose();
    }
}
