#nullable enable

using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct BarrierView : IRenderView
{
    public BarrierView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}

file sealed class LambdaPass : IPass
{
    private readonly Action<RenderContextBuilder> _setup;
    private readonly Action<RenderContext, CommandBuffer> _render;

    public LambdaPass(string name, Action<RenderContextBuilder> setup, Action<RenderContext, CommandBuffer> render)
    {
        Name = name;
        _setup = setup;
        _render = render;
    }

    public string Name { get; }
    public void Setup(RenderContextBuilder builder) => _setup(builder);
    public void Render(RenderContext context, CommandBuffer cmd) => _render(context, cmd);
}

file static class BarrierPasses
{
    public static LambdaPass Upload(RenderResourceID id, GraphTextureDesc desc, Texture source)
    {
        TextureHandle handle = default;
        return new LambdaPass(
            "Upload",
            builder => handle = builder.DeclareOutputTexture(id, desc, usage: TextureState.TransferDst),
            (context, cmd) =>
            {
                cmd.CopyTexture(source, context.GetRenderTexture(handle).ColorTextures[0]);
            });
    }

    public static LambdaPass Readback(RenderResourceID id, DeviceBuffer staging)
    {
        TextureHandle handle = default;
        return new LambdaPass(
            "Readback",
            builder => handle = builder.DeclareInputTexture(id, TextureState.TransferSrc),
            (context, cmd) =>
            {
                Texture color = context.GetRenderTexture(handle).ColorTextures[0];
                cmd.CopyTextureToBuffer(color, staging, 0, TextureRegion.Whole(color));
            });
    }
}

public abstract class GraphBarrierTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const PixelFormat Format = PixelFormat.R32_G32_B32_A32_Float;

    private static readonly Color[] s_texels =
    [
        new(1f, 0f, 0f, 1f),
        new(0f, 1f, 0f, 1f),
        new(0f, 0f, 1f, 1f),
        new(1f, 1f, 1f, 1f),
    ];

    private DeviceBuffer CreateStaging(uint width, uint height)
        => CreateTexelReadbackBuffer<Color>(width, height);

    private Texture CreateSourceTexels()
    {
        Texture source = RF.CreateTexture(TextureDescription.Texture2D(4, 1, 1, 1, Format, TextureUsage.Sampled));
        GD.UpdateTexture(source, s_texels, new TextureRegion(0, 0, 0, 4, 1, 1));
        return source;
    }

    private void AssertTexels(DeviceBuffer staging) => AssertTexels(ReadTexels<Color>(staging, 4, 1));

    private void AssertTexels(TexelData<Color> map)
    {
        for (int x = 0; x < s_texels.Length; x++)
            Assert.Equal(s_texels[x], map[x, 0], ColorFuzzyComparer.Instance);
    }

    private void AssertUniform(DeviceBuffer staging, uint width, uint height, Color expected)
        => AssertUniform(ReadTexels<Color>(staging, width, height), expected);

    private void AssertUniform(TexelData<Color> map, Color expected)
    {
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
                Assert.Equal(expected, map[x, y], ColorFuzzyComparer.Instance);
        }
    }

    private GraphicsProgram CreateSampleProgram()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "FullScreenTriSampleTexture2D.slang");
        return RF.CreateGraphicsProgram(new ShaderDescription(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            ResourceLayouts =
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements =
                    [
                        new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment, 0),
                        new ResourceLayoutElementDescription("Smp", ResourceKind.Sampler, ShaderStages.Fragment, 1),
                    ]
                }
            ],
        });
    }

    private ComputeProgram CreateCompute(string module, params ResourceLayoutElementDescription[] elements)
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, module);
        ResourceLayoutDescription[] layouts = [new ResourceLayoutDescription { Set = 0, Elements = elements }];
        return RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 16, 16, 1));
    }

    private static void DrawSampled(CommandBuffer cmd, GraphicsProgram program, Framebuffer target, Texture sampled, Sampler sampler)
    {
        PropertySet props = new();
        props.SetTexture("Tex", sampled, sampler);
        props.SetSampler("Smp", sampler);

        cmd.SetFramebuffer(target, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
        cmd.SetFullViewport();
        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetProperties(props);
        cmd.Draw(3);
    }

    [Fact]
    public void CopyIntoNonSampledRenderTarget_ThenLoadRenderPass_KeepsTexels()
    {
        Texture source = CreateSourceTexels();
        Texture target = RF.CreateTexture(TextureDescription.Texture2D(4, 1, 1, 1, Format, TextureUsage.RenderTarget));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, target));

        GD.RunTestGraph((context, cmd) =>
        {
            cmd.CopyTexture(source, target);
            cmd.SetFramebuffer(fb);
            cmd.SetFullViewport();
        });
        GD.WaitForIdle();

        AssertTexels(ReadTexture<Color>(target));
    }

    [Fact]
    public void GraphCopyIntoTarget_ThenLoadingAttachmentPass_KeepsTexels()
    {
        Texture source = CreateSourceTexels();
        DeviceBuffer staging = CreateStaging(4, 1);
        RenderResourceID id = RenderResourceID.Intern("barrier_copy_target");
        GraphTextureDesc desc = GraphTextureDesc.Sized(4, 1, false, Format);

        TextureHandle uploadHandle = default;
        LambdaPass upload = new(
            "Upload",
            builder => uploadHandle = builder.DeclareOutputTexture(id, desc, usage: TextureState.TransferDst),
            (context, cmd) =>
            {
                cmd.CopyTexture(source, context.GetRenderTexture(uploadHandle).ColorTextures[0]);
            });

        TextureHandle loadHandle = default;
        LambdaPass load = new(
            "Load",
            builder => loadHandle = builder.DeclareOutputTexture(id, desc, ops: new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded)),
            (context, cmd) =>
            {
                cmd.SetFramebuffer(context.GetRenderTexture(loadHandle).Framebuffer);
                cmd.SetFullViewport();
            });

        using RenderPipeline pipeline = new([upload, load, BarrierPasses.Readback(id, staging)]);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(4, 1) });
        GD.WaitForIdle();

        AssertTexels(staging);
    }

    [Fact]
    public void RenderTargetAndSampledTexture_IsSampleableRightAfterCreation()
    {
        const uint size = 8;
        Texture fresh = RF.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, Format, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Texture output = RF.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, Format, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, output));
        GraphicsProgram program = CreateSampleProgram();

        GD.RunTestGraph((context, cmd) =>
        {
            DrawSampled(cmd, program, fb, fresh, PointSampler);
        });
        GD.WaitForIdle();

        AssertUniform(ReadTexture<Color>(output), new Color(0f, 0f, 0f, 0f));
    }

    [Fact]
    public void HistoryTexture_IsSampledTheNextExecution()
    {
        const uint size = 8;
        DeviceBuffer staging = CreateStaging(size, size);
        GraphicsProgram program = CreateSampleProgram();
        RenderResourceID historyId = RenderResourceID.Intern("barrier_history");
        RenderResourceID outputId = RenderResourceID.Intern("barrier_history_output");
        GraphTextureDesc desc = GraphTextureDesc.Sized((int)size, (int)size, false, Format);
        Color frameColor = Color.Red;

        TextureHandle writeHandle = default;
        LambdaPass write = new(
            "WriteHistory",
            builder => writeHandle = builder.DeclareOutputTexture(historyId, desc, history: 1),
            (context, cmd) =>
            {
                cmd.SetFramebuffer(context.GetRenderTexture(writeHandle).Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(frameColor), AttachmentOps.Loaded));
            });

        TextureHandle historyHandle = default;
        TextureHandle outputHandle = default;
        LambdaPass sample = new(
            "SampleHistory",
            builder =>
            {
                historyHandle = builder.DeclareInputTexture(historyId);
                outputHandle = builder.DeclareOutputTexture(outputId, desc);
            },
            (context, cmd) =>
            {
                Framebuffer target = context.GetRenderTexture(outputHandle).Framebuffer;
                if (context.IsHistoryValid(historyHandle))
                {
                    Texture previous = context.GetRenderTexture(historyHandle, 1).ColorTextures[0];
                    DrawSampled(cmd, program, target, previous, PointSampler);
                }
                else
                {
                    cmd.SetFramebuffer(target, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
                }
            });

        using RenderPipeline pipeline = new([write, sample, BarrierPasses.Readback(outputId, staging)]);
        BarrierView[] views = { new(size, size) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();
        AssertUniform(staging, size, size, Color.Black);

        frameColor = Color.Blue;
        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();
        AssertUniform(staging, size, size, Color.Red);
    }

    [SkippableFact]
    public void ComputeStorageWrite_ThenFragmentSample_ReadsComputedTexels()
    {

        DeviceBuffer staging = CreateStaging(4, 1);
        GraphicsProgram program = CreateSampleProgram();
        ComputeProgram compute = CreateCompute("ComputeTextureGenerator.slang",
            new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0));
        RenderResourceID storageId = RenderResourceID.Intern("barrier_storage");
        RenderResourceID outputId = RenderResourceID.Intern("barrier_storage_output");
        GraphTextureDesc desc = GraphTextureDesc.Sized(4, 1, false, Format);

        TextureHandle storageHandle = default;
        LambdaPass generate = new(
            "Generate",
            builder => storageHandle = builder.DeclareOutputTexture(storageId, desc, usage: TextureState.Storage),
            (context, cmd) =>
            {
                PropertySet props = new();
                props.SetTexture("ComputeOutput", context.GetRenderTexture(storageHandle).ColorTextures[0]);
                cmd.SetComputeShader(compute);
                cmd.SetProperties(props);
                cmd.Dispatch(1, 1, 1);
            });

        TextureHandle sampledHandle = default;
        TextureHandle outputHandle = default;
        LambdaPass sample = new(
            "Sample",
            builder =>
            {
                sampledHandle = builder.DeclareInputTexture(storageId);
                outputHandle = builder.DeclareOutputTexture(outputId, desc);
            },
            (context, cmd) =>
            {
                DrawSampled(
                    cmd, program,
                    context.GetRenderTexture(outputHandle).Framebuffer,
                    context.GetRenderTexture(sampledHandle).ColorTextures[0],
                    PointSampler);
            });

        using RenderPipeline pipeline = new([generate, sample, BarrierPasses.Readback(outputId, staging)]);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(4, 1) });
        GD.WaitForIdle();

        AssertTexels(staging);
    }

    [SkippableFact]
    public void ComputeBufferWrite_ThenVertexRead_RendersGeneratedQuad()
    {

        const uint size = 16;
        const uint stride = 32;
        DeviceBuffer staging = CreateStaging(size, size);
        ComputeProgram compute = CreateCompute("ComputeColoredQuadGenerator.slang",
            new ResourceLayoutElementDescription("OutputVertices", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 0));
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "ColoredQuadRenderer.slang");
        GraphicsProgram graphics = RF.CreateGraphicsProgram(new ShaderDescription(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            ResourceLayouts =
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements = [new ResourceLayoutElementDescription("InputVertices", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex, 0)]
                }
            ],
        });

        RenderResourceID verticesId = RenderResourceID.Intern("barrier_vertices");
        RenderResourceID outputId = RenderResourceID.Intern("barrier_vertices_output");

        BufferHandle writeHandle = default;
        LambdaPass generate = new(
            "GenerateVertices",
            builder => writeHandle = builder.DeclareOutputBuffer(verticesId, GraphBufferDesc.Structured(4, stride)),
            (context, cmd) =>
            {
                PropertySet props = new();
                props.SetBuffer("OutputVertices", context.GetRenderBuffer(writeHandle));
                cmd.SetComputeShader(compute);
                cmd.SetProperties(props);
                cmd.Dispatch(1, 1, 1);
            });

        BufferHandle readHandle = default;
        TextureHandle outputHandle = default;
        LambdaPass draw = new(
            "DrawVertices",
            builder =>
            {
                readHandle = builder.DeclareInputBuffer(verticesId, BufferAccess.ShaderRead);
                outputHandle = builder.DeclareOutputTexture(outputId, GraphTextureDesc.Sized((int)size, (int)size, false, Format));
            },
            (context, cmd) =>
            {
                PropertySet props = new();
                props.SetBuffer("InputVertices", context.GetRenderBuffer(readHandle));
                cmd.SetFramebuffer(context.GetRenderTexture(outputHandle).Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
                cmd.SetFullViewport();
                cmd.SetShader(graphics);
                cmd.SetVertexSource(new VertexSource(PrimitiveTopology.TriangleStrip));
                cmd.SetProperties(props);
                cmd.Draw(4);
            });

        using RenderPipeline pipeline = new([generate, draw, BarrierPasses.Readback(outputId, staging)]);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(size, size) });
        GD.WaitForIdle();

        AssertUniform(staging, size, size, new Color(1f, 0f, 0f, 1f));
    }

    [Fact]
    public void DepthReadOnly_SamplesDepthWhileRenderingColor()
    {
        const uint size = 8;
        DeviceBuffer staging = CreateStaging(size, size);
        GraphicsProgram program = CreateSampleProgram();
        RenderResourceID id = RenderResourceID.Intern("barrier_depth_ro_scene");
        GraphTextureDesc desc = GraphTextureDesc.Sized((int)size, (int)size, true, Format);

        TextureHandle depthHandle = default;
        LambdaPass depth = new(
            "WriteDepth",
            builder => depthHandle = builder.DeclareOutputTexture(id, desc),
            (context, cmd) =>
            {
                cmd.SetFramebuffer(context.GetRenderTexture(depthHandle).Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Clear(0.25f)));
            });

        TextureHandle fogHandle = default;
        LambdaPass fog = new(
            "Fog",
            builder => fogHandle = builder.DeclareOutputTexture(
                id, desc,
                ops: new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded),
                depthUsage: TextureState.DepthReadOnly),
            (context, cmd) =>
            {
                RenderTexture scene = context.GetRenderTexture(fogHandle);
                PropertySet props = new();
                props.SetTexture("Tex", scene.DepthTexture!, PointSampler);
                props.SetSampler("Smp", PointSampler);

                cmd.SetFramebuffer(scene.Framebuffer);
                cmd.SetFullViewport();
                cmd.SetShader(program);
                cmd.SetVertexSource(VertexSource.None);
                cmd.SetProperties(props);
                cmd.Draw(3);
            });

        using RenderPipeline pipeline = new([depth, fog, BarrierPasses.Readback(id, staging)]);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(size, size) });
        GD.WaitForIdle();

        TexelData<Color> map = ReadTexels<Color>(staging, size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                Assert.Equal(0.25f, map[x, y].R, 0.01f);
        }
    }

    [Fact]
    public void DepthReadOnly_ClearingDepth_Throws()
    {
        RenderResourceID id = RenderResourceID.Intern("barrier_depth_ro_clear");
        GraphTextureDesc desc = GraphTextureDesc.Sized(4, 4, true, Format);

        TextureHandle handle = default;
        LambdaPass pass = new(
            "ClearReadOnlyDepth",
            builder => handle = builder.DeclareOutputTexture(id, desc, depthUsage: TextureState.DepthReadOnly),
            (context, cmd) =>
            {
                cmd.SetFramebuffer(context.GetRenderTexture(handle).Framebuffer);
                cmd.ClearDepthStencil(1f, 0);
            });

        using RenderPipeline pipeline = new([pass]);
        Assert.Throws<RenderException>(() => GD.DispatchGraph(pipeline, new BarrierView[] { new(4, 4) }));
        GD.WaitForIdle();
    }

    [Fact]
    public void UndeclaredTextureUse_Throws()
    {
        RenderResourceID id = RenderResourceID.Intern("barrier_undeclared");
        GraphTextureDesc desc = GraphTextureDesc.Sized(4, 4, false, Format);

        TextureHandle handle = default;
        LambdaPass write = new(
            "Write",
            builder => handle = builder.DeclareOutputTexture(id, desc),
            (context, cmd) =>
            {
                cmd.SetFramebuffer(context.GetRenderTexture(handle).Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            });

        List<Exception> errors = new();
        LambdaPass thief = new(
            "Thief",
            builder => { },
            (context, cmd) =>
            {
                try
                {
                    context.GetRenderTexture(handle);
                }
                catch (InvalidOperationException e)
                {
                    errors.Add(e);
                }
            });

        using RenderPipeline pipeline = new([write, thief]);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(4, 4) });
        GD.WaitForIdle();

        Assert.Single(errors);
    }

    [Fact]
    public void FirstPass_WithoutCommandBuffers_StillRecordsItsBarriers()
    {
        Texture source = CreateSourceTexels();
        DeviceBuffer staging = CreateStaging(4, 1);
        RenderResourceID id = RenderResourceID.Intern("barrier_idle_first");
        GraphTextureDesc desc = GraphTextureDesc.Sized(4, 1, false, Format);

        LambdaPass idle = new(
            "Idle",
            builder => builder.DeclareOutputTexture(id, desc),
            (context, cmd) => { });

        using RenderPipeline pipeline = new([idle, BarrierPasses.Upload(id, desc, source), BarrierPasses.Readback(id, staging)]);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(4, 1) });
        GD.WaitForIdle();

        AssertTexels(staging);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanGraphBarrierTests : GraphBarrierTests<VulkanDeviceCreator> { }
#endif
