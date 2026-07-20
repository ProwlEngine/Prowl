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
    public SceneView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
}


// Renders the model into an offscreen "Scene" target, then runs a two-step bloom (downsample, then
// upsample) over it into "BloomFull", and finally composites Scene + BloomFull to the swapchain. The
// graph orders the four passes from their declared texture reads/writes; nothing here manually tracks
// dependency order.
internal sealed class ScenePass : IPass<SceneView, int>
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

    private TextureHandle _sceneHandle;

    public string Name => "Scene";

    // The composite present pass has no Setup of its own to obtain a handle from, so it reads
    // this directly off the pass that owns the resource, after Setup has populated it.
    public TextureHandle SceneHandle => _sceneHandle;

    public void Advance(float dt) => _angle += dt * 0.5f;

    public void Setup(RenderContextBuilder builder)
        => _sceneHandle = builder.GetOutputTexture("Scene", GraphTextureDesc.ViewSized());

    public void Render(RenderContext<SceneView, int> context)
    {
        float radius = Math.Max(_distance, 0.001f);
        Float3 eye = _center + new Float3(MathF.Sin(_angle), 0.35f, MathF.Cos(_angle)) * _distance;

        Float4x4 projection = Float4x4.CreatePerspectiveFov(1.0472f, 1.0f, radius * 0.02f, radius * 10.0f);
        Float4x4 view = Float4x4.CreateLookAt(eye, _center, Float3.UnitY);
        _properties.SetMatrix("MatrixMVP", projection * view);

        RenderTexture scene = context.GetRenderTexture(_sceneHandle);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        cmd.Begin();
        cmd.SetFramebuffer(scene.Framebuffer);
        cmd.ClearDepthStencil(1, 0);
        cmd.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_model.Mesh);
        cmd.SetProperties(_properties);
        cmd.DrawIndexed();
        cmd.End();

        context.SubmitCommandBuffer(cmd);
    }
}


internal sealed class BloomDownsamplePass : IPass<SceneView, int>
{
    private readonly ShaderPass _bloomShader;
    private readonly Sampler _sampler;
    private readonly PropertySet _properties = new();
    private readonly CanvasFullscreenSource _fullscreenSource = new();
    private static readonly Keyword UpsampleOff = new("Upsample", "false");

    public BloomDownsamplePass(ShaderPass bloomShader, Sampler sampler)
    {
        _bloomShader = bloomShader;
        _sampler = sampler;
    }

    private TextureHandle _sceneHandle;
    private TextureHandle _bloomHalfHandle;

    public string Name => "BloomDownsample";

    public void Setup(RenderContextBuilder builder)
    {
        _sceneHandle = builder.GetInputTexture("Scene", GraphTextureDesc.ViewSized());
        _bloomHalfHandle = builder.GetOutputTexture("BloomHalf", GraphTextureDesc.ViewSized(false, 0.5f));
    }

    public void Render(RenderContext<SceneView, int> context)
    {
        RenderTexture scene = context.GetRenderTexture(_sceneHandle);
        RenderTexture bloomHalf = context.GetRenderTexture(_bloomHalfHandle);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        cmd.Begin();
        cmd.SetFramebuffer(bloomHalf.Framebuffer);

        _bloomShader.SetKeyword(UpsampleOff);
        _properties.SetTexture("sourceTexture", scene.ColorTextures[0], _sampler);
        _properties.SetFloat2("halfPixel", new Float2(0.5f / bloomHalf.Desc.Width, 0.5f / bloomHalf.Desc.Height));
        _properties.SetFloat("offset", 1f);

        cmd.SetShader(_bloomShader);
        cmd.SetVertexSource(_fullscreenSource);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
        cmd.End();

        context.SubmitCommandBuffer(cmd);
    }
}


internal sealed class BloomUpsamplePass : IPass<SceneView, int>
{
    private readonly ShaderPass _bloomShader;
    private readonly Sampler _sampler;
    private readonly PropertySet _properties = new();
    private readonly CanvasFullscreenSource _fullscreenSource = new();
    private static readonly Keyword UpsampleOn = new("Upsample", "true");

    public BloomUpsamplePass(ShaderPass bloomShader, Sampler sampler)
    {
        _bloomShader = bloomShader;
        _sampler = sampler;
    }

    private TextureHandle _bloomHalfHandle;
    private TextureHandle _bloomFullHandle;

    public string Name => "BloomUpsample";

    // The composite present pass has no Setup of its own to obtain a handle from, so it reads
    // this directly off the pass that owns the resource, after Setup has populated it.
    public TextureHandle BloomFullHandle => _bloomFullHandle;

    public void Setup(RenderContextBuilder builder)
    {
        _bloomHalfHandle = builder.GetInputTexture("BloomHalf", GraphTextureDesc.ViewSized(false, 0.5f));
        _bloomFullHandle = builder.GetOutputTexture("BloomFull", GraphTextureDesc.ViewSized(false, 1f));
    }

    public void Render(RenderContext<SceneView, int> context)
    {
        RenderTexture bloomHalf = context.GetRenderTexture(_bloomHalfHandle);
        RenderTexture bloomFull = context.GetRenderTexture(_bloomFullHandle);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        cmd.Begin();
        cmd.SetFramebuffer(bloomFull.Framebuffer);

        _bloomShader.SetKeyword(UpsampleOn);
        _properties.SetTexture("sourceTexture", bloomHalf.ColorTextures[0], _sampler);
        _properties.SetFloat2("halfPixel", new Float2(0.5f / bloomFull.Desc.Width, 0.5f / bloomFull.Desc.Height));
        _properties.SetFloat("offset", 1f);

        cmd.SetShader(_bloomShader);
        cmd.SetVertexSource(_fullscreenSource);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
        cmd.End();

        context.SubmitCommandBuffer(cmd);
    }
}


internal sealed class CompositePresentPass : IPresentPass<SceneView, int>
{
    private readonly GraphicsProgram _compositeShader;
    private readonly Sampler _sampler;
    private readonly PropertySet _properties = new();
    private readonly CanvasFullscreenSource _fullscreenSource = new();
    private readonly ScenePass _scenePass;
    private readonly BloomUpsamplePass _bloomUpPass;

    public CompositePresentPass(GraphicsProgram compositeShader, Sampler sampler, ScenePass scenePass, BloomUpsamplePass bloomUpPass)
    {
        _compositeShader = compositeShader;
        _sampler = sampler;
        _scenePass = scenePass;
        _bloomUpPass = bloomUpPass;
    }

    public string Name => "Composite";

    public void Present(RenderContext<SceneView, int> context)
    {
        Framebuffer? target = context.RequestSwapchainTarget();
        if (target == null)
            return;

        RenderTexture scene = context.GetRenderTexture(_scenePass.SceneHandle);
        RenderTexture bloomFull = context.GetRenderTexture(_bloomUpPass.BloomFullHandle);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        cmd.Begin();
        cmd.SetFramebuffer(target);

        _properties.SetTexture("sceneTexture", scene.ColorTextures[0], _sampler);
        _properties.SetTexture("bloomTexture", bloomFull.ColorTextures[0], _sampler);
        _properties.SetFloat("bloomIntensity", 0.6f);

        cmd.SetShader(_compositeShader);
        cmd.SetVertexSource(_fullscreenSource);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
        cmd.End();

        context.SubmitCommandBuffer(cmd);
        context.ArmPresent();
    }
}


// A raw 3-vertex fullscreen-triangle source: no vertex/index buffers, just SV_VertexID in the shader.
internal readonly struct CanvasFullscreenSource : IVertexSource
{
    public readonly PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

    public readonly void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
        => binding = default;

    public readonly bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount)
    {
        buffer = null!;
        format = IndexFormat.UInt32;
        indexCount = 0;
        return false;
    }
}


internal sealed class PBRPipeline : RenderPipeline<SceneView, int>
{
    private readonly ScenePass _scene;
    private readonly BloomDownsamplePass _bloomDown;
    private readonly BloomUpsamplePass _bloomUp;
    private readonly CompositePresentPass _present;

    public PBRPipeline(ScenePass scene, BloomDownsamplePass bloomDown, BloomUpsamplePass bloomUp, CompositePresentPass present)
    {
        _scene = scene;
        _bloomDown = bloomDown;
        _bloomUp = bloomUp;
        _present = present;
    }

    public ScenePass Scene => _scene;

    protected override void InitializePasses()
    {
        AddPass(_scene);
        AddPass(_bloomDown);
        AddPass(_bloomUp);
        SetPresentPass(_present);
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

    static PBRPipeline pipeline;
    static SceneView[] views;


    private static void Main()
    {
        GraphicsDeviceOptions options = new()
        {
            Debug = false,
            SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt,
            SyncToVerticalBlank = true,
            PreferStandardClipSpaceYDirection = true
        };

        DeviceCreateUtilities.CreateWindowAndDevice(Load, Render, Close, options);
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
        sceneProperties.SetTexture("AlbedoTexture", albedo, device.LinearSampler);
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

        ScenePass scenePass = new(model, unlitShader, sceneProperties);
        BloomDownsamplePass bloomDown = new(bloomShader, bloomSampler);
        BloomUpsamplePass bloomUp = new(bloomShader, bloomSampler);
        CompositePresentPass present = new(compositeShader, compositeSampler, scenePass, bloomUp);

        pipeline = new PBRPipeline(scenePass, bloomDown, bloomUp, present);
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

        pipeline.Scene.Advance((float)dt);
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
