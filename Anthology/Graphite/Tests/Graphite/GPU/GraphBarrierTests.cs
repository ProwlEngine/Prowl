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

file sealed class LambdaPass : IPass<BarrierView>
{
    private readonly Action<RenderContextBuilder> _setup;
    private readonly Action<RenderContext<BarrierView>> _render;

    public LambdaPass(string name, Action<RenderContextBuilder> setup, Action<RenderContext<BarrierView>> render)
    {
        Name = name;
        _setup = setup;
        _render = render;
    }

    public string Name { get; }
    public void Setup(RenderContextBuilder builder) => _setup(builder);
    public void Render(RenderContext<BarrierView> context) => _render(context);
}

file sealed class NoOpBarrierPresentPass : IPresentPass<BarrierView>
{
    public string Name => "Present";
    public void Setup(PresentContextBuilder builder) { }
    public void Present(RenderContext<BarrierView> context) { }
}

file sealed class BarrierPipeline : RenderPipeline<BarrierView>
{
    private readonly IPass<BarrierView>[] _passes;

    public BarrierPipeline(params IPass<BarrierView>[] passes) => _passes = passes;

    protected override void InitializePasses()
    {
        foreach (IPass<BarrierView> pass in _passes)
            AddPass(pass);
        SetPresentPass(new NoOpBarrierPresentPass());
    }
}

file static class BarrierPasses
{
    public static LambdaPass Readback(RenderResourceID id, Texture staging)
    {
        TextureHandle handle = default;
        return new LambdaPass(
            "Readback",
            builder => handle = builder.DeclareInputTexture(id, TextureUsageKind.TransferSrc),
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("Readback");
                cmd.CopyTexture(context.GetRenderTexture(handle).ColorTextures[0], staging);
                context.SubmitCommandBuffer(cmd);
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

    private Texture CreateStaging(uint width, uint height)
        => RF.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, Format, TextureUsage.Staging));

    private Texture CreateSourceTexels()
    {
        Texture source = RF.CreateTexture(TextureDescription.Texture2D(4, 1, 1, 1, Format, TextureUsage.Sampled));
        GD.UpdateTexture(source, s_texels, 0, 0, 0, 4, 1, 1, 0, 0);
        return source;
    }

    private void AssertTexels(Texture staging)
    {
        MappedResourceView<Color> map = GD.Map<Color>(staging, MapMode.Read);
        for (int x = 0; x < s_texels.Length; x++)
            Assert.Equal(s_texels[x], map[x, 0], ColorFuzzyComparer.Instance);
        GD.Unmap(staging);
    }

    private void AssertUniform(Texture staging, uint width, uint height, Color expected)
    {
        MappedResourceView<Color> map = GD.Map<Color>(staging, MapMode.Read);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                Assert.Equal(expected, map[x, y], ColorFuzzyComparer.Instance);
        }
        GD.Unmap(staging);
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

        cmd.SetFramebuffer(target);
        cmd.ClearColorTarget(0, Color.Black);
        cmd.SetFullViewports();
        cmd.SetShader(program);
        cmd.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleList, []));
        cmd.SetProperties(props);
        cmd.Draw(3);
    }

    [Fact]
    public void CopyIntoNonSampledRenderTarget_ThenLoadRenderPass_KeepsTexels()
    {
        Texture source = CreateSourceTexels();
        Texture target = RF.CreateTexture(TextureDescription.Texture2D(4, 1, 1, 1, Format, TextureUsage.RenderTarget));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, target));

        GD.RunTestGraph(context =>
        {
            CommandBuffer cmd = context.GetCommandBuffer();
            cmd.CopyTexture(source, target);
            cmd.SetFramebuffer(fb);
            cmd.SetFullViewports();
            context.SubmitCommandBuffer(cmd);
        });
        GD.WaitForIdle();

        AssertTexels(GetReadback(target));
    }

    [Fact]
    public void GraphCopyIntoTarget_ThenLoadingAttachmentPass_KeepsTexels()
    {
        Texture source = CreateSourceTexels();
        Texture staging = CreateStaging(4, 1);
        RenderResourceID id = RenderResourceID.Intern("barrier_copy_target");
        GraphTextureDesc desc = GraphTextureDesc.Sized(4, 1, false, Format);

        TextureHandle uploadHandle = default;
        LambdaPass upload = new(
            "Upload",
            builder => uploadHandle = builder.DeclareOutputTexture(id, desc, usage: TextureUsageKind.TransferDst),
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("Upload");
                cmd.CopyTexture(source, context.GetRenderTexture(uploadHandle).ColorTextures[0]);
                context.SubmitCommandBuffer(cmd);
            });

        TextureHandle loadHandle = default;
        LambdaPass load = new(
            "Load",
            builder => loadHandle = builder.DeclareOutputTexture(id, desc, ops: new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded)),
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("Load");
                cmd.SetFramebuffer(context.GetRenderTexture(loadHandle).Framebuffer);
                cmd.SetFullViewports();
                context.SubmitCommandBuffer(cmd);
            });

        using BarrierPipeline pipeline = new(upload, load, BarrierPasses.Readback(id, staging));
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

        GD.RunTestGraph(context =>
        {
            CommandBuffer cmd = context.GetCommandBuffer();
            DrawSampled(cmd, program, fb, fresh, GD.PointSampler);
            context.SubmitCommandBuffer(cmd);
        });
        GD.WaitForIdle();

        AssertUniform(GetReadback(output), size, size, new Color(0f, 0f, 0f, 0f));
    }

    [Fact]
    public void HistoryTexture_IsSampledTheNextExecution()
    {
        const uint size = 8;
        Texture staging = CreateStaging(size, size);
        GraphicsProgram program = CreateSampleProgram();
        RenderResourceID historyId = RenderResourceID.Intern("barrier_history");
        RenderResourceID outputId = RenderResourceID.Intern("barrier_history_output");
        GraphTextureDesc desc = GraphTextureDesc.Sized((int)size, (int)size, false, Format);
        Color frameColor = Color.Red;

        TextureHandle writeHandle = default;
        LambdaPass write = new(
            "WriteHistory",
            builder => writeHandle = builder.DeclareOutputTexture(historyId, desc, history: 1),
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("WriteHistory");
                cmd.SetFramebuffer(context.GetRenderTexture(writeHandle).Framebuffer);
                cmd.ClearColorTarget(0, frameColor);
                context.SubmitCommandBuffer(cmd);
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
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("SampleHistory");
                Framebuffer target = context.GetRenderTexture(outputHandle).Framebuffer;
                if (context.IsHistoryValid(historyHandle))
                {
                    Texture previous = context.GetRenderTexture(historyHandle, 1).ColorTextures[0];
                    DrawSampled(cmd, program, target, previous, GD.PointSampler);
                }
                else
                {
                    cmd.SetFramebuffer(target);
                    cmd.ClearColorTarget(0, Color.Black);
                }
                context.SubmitCommandBuffer(cmd);
            });

        using BarrierPipeline pipeline = new(write, sample, BarrierPasses.Readback(outputId, staging));
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
        Skip.IfNot(GD.Features.ComputeShader);

        Texture staging = CreateStaging(4, 1);
        GraphicsProgram program = CreateSampleProgram();
        ComputeProgram compute = CreateCompute("ComputeTextureGenerator.slang",
            new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0));
        RenderResourceID storageId = RenderResourceID.Intern("barrier_storage");
        RenderResourceID outputId = RenderResourceID.Intern("barrier_storage_output");
        GraphTextureDesc desc = GraphTextureDesc.Sized(4, 1, false, Format);

        TextureHandle storageHandle = default;
        LambdaPass generate = new(
            "Generate",
            builder => storageHandle = builder.DeclareOutputTexture(storageId, desc, usage: TextureUsageKind.Storage),
            context =>
            {
                PropertySet props = new();
                props.SetTexture("ComputeOutput", context.GetRenderTexture(storageHandle).ColorTextures[0]);
                CommandBuffer cmd = context.GetCommandBuffer("Generate");
                cmd.SetComputeShader(compute);
                cmd.SetProperties(props);
                cmd.Dispatch(1, 1, 1);
                context.SubmitCommandBuffer(cmd);
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
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("Sample");
                DrawSampled(
                    cmd, program,
                    context.GetRenderTexture(outputHandle).Framebuffer,
                    context.GetRenderTexture(sampledHandle).ColorTextures[0],
                    GD.PointSampler);
                context.SubmitCommandBuffer(cmd);
            });

        using BarrierPipeline pipeline = new(generate, sample, BarrierPasses.Readback(outputId, staging));
        GD.DispatchGraph(pipeline, new BarrierView[] { new(4, 1) });
        GD.WaitForIdle();

        AssertTexels(staging);
    }

    [SkippableFact]
    public void ComputeBufferWrite_ThenVertexRead_RendersGeneratedQuad()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const uint size = 16;
        const uint stride = 32;
        Texture staging = CreateStaging(size, size);
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
            context =>
            {
                PropertySet props = new();
                props.SetBuffer("OutputVertices", context.GetRenderBuffer(writeHandle), readOnly: false);
                CommandBuffer cmd = context.GetCommandBuffer("GenerateVertices");
                cmd.SetComputeShader(compute);
                cmd.SetProperties(props);
                cmd.Dispatch(1, 1, 1);
                context.SubmitCommandBuffer(cmd);
            });

        BufferHandle readHandle = default;
        TextureHandle outputHandle = default;
        LambdaPass draw = new(
            "DrawVertices",
            builder =>
            {
                readHandle = builder.DeclareInputBuffer(verticesId, BufferUsageKind.ShaderRead);
                outputHandle = builder.DeclareOutputTexture(outputId, GraphTextureDesc.Sized((int)size, (int)size, false, Format));
            },
            context =>
            {
                PropertySet props = new();
                props.SetBuffer("InputVertices", context.GetRenderBuffer(readHandle), readOnly: true);
                CommandBuffer cmd = context.GetCommandBuffer("DrawVertices");
                cmd.SetFramebuffer(context.GetRenderTexture(outputHandle).Framebuffer);
                cmd.ClearColorTarget(0, Color.Black);
                cmd.SetFullViewports();
                cmd.SetShader(graphics);
                cmd.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleStrip, []));
                cmd.SetProperties(props);
                cmd.Draw(4);
                context.SubmitCommandBuffer(cmd);
            });

        using BarrierPipeline pipeline = new(generate, draw, BarrierPasses.Readback(outputId, staging));
        GD.DispatchGraph(pipeline, new BarrierView[] { new(size, size) });
        GD.WaitForIdle();

        AssertUniform(staging, size, size, new Color(1f, 0f, 0f, 1f));
    }

    [Fact]
    public void DepthReadOnly_SamplesDepthWhileRenderingColor()
    {
        const uint size = 8;
        Texture staging = CreateStaging(size, size);
        GraphicsProgram program = CreateSampleProgram();
        RenderResourceID id = RenderResourceID.Intern("barrier_depth_ro_scene");
        GraphTextureDesc desc = GraphTextureDesc.Sized((int)size, (int)size, true, Format);

        TextureHandle depthHandle = default;
        LambdaPass depth = new(
            "WriteDepth",
            builder => depthHandle = builder.DeclareOutputTexture(id, desc),
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("WriteDepth");
                cmd.SetFramebuffer(context.GetRenderTexture(depthHandle).Framebuffer);
                cmd.ClearColorTarget(0, Color.Black);
                cmd.ClearDepthStencil(0.25f, 0);
                context.SubmitCommandBuffer(cmd);
            });

        TextureHandle fogHandle = default;
        LambdaPass fog = new(
            "Fog",
            builder => fogHandle = builder.DeclareOutputTexture(
                id, desc,
                ops: new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded),
                depthUsage: TextureUsageKind.DepthReadOnly),
            context =>
            {
                RenderTexture scene = context.GetRenderTexture(fogHandle);
                PropertySet props = new();
                props.SetTexture("Tex", scene.DepthTexture!, GD.PointSampler);
                props.SetSampler("Smp", GD.PointSampler);

                CommandBuffer cmd = context.GetCommandBuffer("Fog");
                cmd.SetFramebuffer(scene.Framebuffer);
                cmd.SetFullViewports();
                cmd.SetShader(program);
                cmd.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleList, []));
                cmd.SetProperties(props);
                cmd.Draw(3);
                context.SubmitCommandBuffer(cmd);
            });

        using BarrierPipeline pipeline = new(depth, fog, BarrierPasses.Readback(id, staging));
        GD.DispatchGraph(pipeline, new BarrierView[] { new(size, size) });
        GD.WaitForIdle();

        MappedResourceView<Color> map = GD.Map<Color>(staging, MapMode.Read);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                Assert.Equal(0.25f, map[x, y].R, 0.01f);
        }
        GD.Unmap(staging);
    }

    [Fact]
    public void DepthReadOnly_ClearingDepth_Throws()
    {
        RenderResourceID id = RenderResourceID.Intern("barrier_depth_ro_clear");
        GraphTextureDesc desc = GraphTextureDesc.Sized(4, 4, true, Format);

        TextureHandle handle = default;
        List<Exception> errors = new();
        LambdaPass pass = new(
            "ClearReadOnlyDepth",
            builder => handle = builder.DeclareOutputTexture(id, desc, depthUsage: TextureUsageKind.DepthReadOnly),
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("ClearReadOnlyDepth");
                cmd.SetFramebuffer(context.GetRenderTexture(handle).Framebuffer);
                cmd.ClearDepthStencil(1f, 0);
                try
                {
                    context.SubmitCommandBuffer(cmd);
                }
                catch (RenderException e)
                {
                    errors.Add(e);
                }
            });

        using BarrierPipeline pipeline = new(pass);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(4, 4) });
        GD.WaitForIdle();

        Assert.Single(errors);
    }

    [Fact]
    public void Transition_PingPongInOnePass_SamplesEachWrite()
    {
        const uint size = 8;
        Texture staging = CreateStaging(size, size);
        GraphicsProgram program = CreateSampleProgram();
        RenderResourceID a = RenderResourceID.Intern("barrier_ping_a");
        RenderResourceID b = RenderResourceID.Intern("barrier_ping_b");
        GraphTextureDesc desc = GraphTextureDesc.Sized((int)size, (int)size, false, Format);
        const TextureUsageKind both = TextureUsageKind.Attachment | TextureUsageKind.Sampled;

        TextureHandle seedHandle = default;
        LambdaPass seed = new(
            "Seed",
            builder => seedHandle = builder.DeclareOutputTexture(a, desc),
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("Seed");
                cmd.SetFramebuffer(context.GetRenderTexture(seedHandle).Framebuffer);
                cmd.ClearColorTarget(0, Color.Red);
                context.SubmitCommandBuffer(cmd);
            });

        TextureHandle aHandle = default;
        TextureHandle bHandle = default;
        LambdaPass pingPong = new(
            "PingPong",
            builder =>
            {
                aHandle = builder.DeclareOutputTexture(a, desc, ops: new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded),
                    usage: both, initial: TextureUsageKind.Sampled);
                bHandle = builder.DeclareOutputTexture(b, desc, usage: both, initial: TextureUsageKind.Attachment);
            },
            context =>
            {
                RenderTexture texA = context.GetRenderTexture(aHandle);
                RenderTexture texB = context.GetRenderTexture(bHandle);
                CommandBuffer cmd = context.GetCommandBuffer("PingPong");

                DrawSampled(cmd, program, texB.Framebuffer, texA.ColorTextures[0], GD.PointSampler);

                context.Transition(cmd, aHandle, TextureUsageKind.Attachment);
                cmd.SetFramebuffer(texA.Framebuffer);
                cmd.ClearColorTarget(0, Color.Blue);

                context.Transition(cmd, aHandle, TextureUsageKind.Sampled);
                DrawSampled(cmd, program, texB.Framebuffer, texA.ColorTextures[0], GD.PointSampler);

                context.Transition(cmd, bHandle, TextureUsageKind.Sampled);
                context.SubmitCommandBuffer(cmd);
            });

        TextureHandle readHandle = default;
        LambdaPass readback = new(
            "Readback",
            builder => readHandle = builder.DeclareInputTexture(b, TextureUsageKind.TransferSrc),
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("Readback");
                cmd.CopyTexture(context.GetRenderTexture(readHandle).ColorTextures[0], staging);
                context.SubmitCommandBuffer(cmd);
            });

        using BarrierPipeline pipeline = new(seed, pingPong, readback);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(size, size) });
        GD.WaitForIdle();

        AssertUniform(staging, size, size, Color.Blue);
    }

    [Fact]
    public void Transition_ThenOutOfOrderSubmit_Throws()
    {
        RenderResourceID id = RenderResourceID.Intern("barrier_order");
        GraphTextureDesc desc = GraphTextureDesc.Sized(4, 4, false, Format);

        TextureHandle handle = default;
        List<Exception> errors = new();
        LambdaPass pass = new(
            "Order",
            builder => handle = builder.DeclareOutputTexture(id, desc,
                usage: TextureUsageKind.Attachment | TextureUsageKind.Sampled, initial: TextureUsageKind.Attachment),
            context =>
            {
                CommandBuffer first = context.GetCommandBuffer("First");
                CommandBuffer second = context.GetCommandBuffer("Second");
                context.Transition(second, handle, TextureUsageKind.Sampled);
                try
                {
                    context.SubmitCommandBuffer(second);
                }
                catch (InvalidOperationException e)
                {
                    errors.Add(e);
                }
                context.SubmitCommandBuffer(first);
                context.SubmitCommandBuffer(second);
            });

        using BarrierPipeline pipeline = new(pass);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(4, 4) });
        GD.WaitForIdle();

        Assert.Single(errors);
    }

    [Fact]
    public void Transition_ToUndeclaredKind_Throws()
    {
        RenderResourceID id = RenderResourceID.Intern("barrier_badkind");
        GraphTextureDesc desc = GraphTextureDesc.Sized(4, 4, false, Format);

        TextureHandle handle = default;
        List<Exception> errors = new();
        LambdaPass pass = new(
            "BadKind",
            builder => handle = builder.DeclareOutputTexture(id, desc),
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("BadKind");
                try
                {
                    context.Transition(cmd, handle, TextureUsageKind.Sampled);
                }
                catch (ArgumentException e)
                {
                    errors.Add(e);
                }
                context.SubmitCommandBuffer(cmd);
            });

        using BarrierPipeline pipeline = new(pass);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(4, 4) });
        GD.WaitForIdle();

        Assert.Single(errors);
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
            context =>
            {
                CommandBuffer cmd = context.GetCommandBuffer("Write");
                cmd.SetFramebuffer(context.GetRenderTexture(handle).Framebuffer);
                cmd.ClearColorTarget(0, Color.Black);
                context.SubmitCommandBuffer(cmd);
            });

        List<Exception> errors = new();
        LambdaPass thief = new(
            "Thief",
            builder => { },
            context =>
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

        using BarrierPipeline pipeline = new(write, thief);
        GD.DispatchGraph(pipeline, new BarrierView[] { new(4, 4) });
        GD.WaitForIdle();

        Assert.Single(errors);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanGraphBarrierTests : GraphBarrierTests<VulkanDeviceCreator> { }
#endif
