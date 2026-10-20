#nullable enable

using System;
using System.Collections.Generic;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

file sealed class RecordingPass : IInstancePass
{
    public GraphKind Kind => TestViews.Kind;

    public int RenderCount => ViewWidths.Count;
    public List<uint> ViewWidths { get; } = new();

    public string Name => "Recording";

    public void Setup(InstanceContextBuilder builder) { }

    public void Render(InstanceContext context, CommandBuffer cmd) => ViewWidths.Add(context.Instance.Extent.Width);
}

file sealed class BackbufferPass : IInstancePass
{
    public GraphKind Kind => TestViews.Kind;

    private TextureHandle _backbuffer;

    public int RenderCount { get; private set; }
    public bool SawFramebuffer { get; private set; }
    public Framebuffer? Resolved { get; private set; }

    public string Name => "Backbuffer";

    public void Setup(InstanceContextBuilder builder) => _backbuffer = builder.DeclareInstanceTarget();

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        RenderCount++;
        Resolved = context.GetRenderTexture(_backbuffer).Framebuffer;
        SawFramebuffer = Resolved != null;
        cmd.SetFramebuffer(Resolved!, new TargetLoadStoreOps(AttachmentOps.Clear(new Color(0.25f, 0.5f, 0.75f, 1f)), AttachmentOps.Loaded));
    }
}

file sealed class OrderPass(string name, GraphKind kind, List<(string Pass, uint Width, ulong Execution)> log) : IInstancePass
{
    public string Name => name;

    public GraphKind Kind => kind;

    public void Setup(InstanceContextBuilder builder) { }

    public void Render(InstanceContext context, CommandBuffer cmd) => log.Add((name, context.Instance.Extent.Width, context.Task.Id));
}

file sealed class RootOrderPass(List<(string Pass, uint Width, ulong Execution)> log) : IExecutionPass
{
    public string Name => "Root";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext context, CommandBuffer cmd) => log.Add((Name, 0, context.Task.Id));
}

file sealed class FlagPass(string name, bool enabled, string? input, string? output, bool passthrough, List<string> log) : IInstancePass
{
    public RenderTexture? Seen { get; private set; }

    public string Name => name;

    public GraphKind Kind => TestViews.Kind;

    public bool IsEnabled(GraphInstance instance) => enabled;

    public void Setup(InstanceContextBuilder builder)
    {
        TextureHandle read = input == null ? default : builder.DeclareInputTexture(input);
        TextureHandle write = output == null ? default : builder.DeclareOutputTexture(output, GraphTextureDesc.Sized(4, 4, false, PixelFormat.R32_G32_B32_A32_Float));
        if (passthrough)
            builder.Passthrough(read, write);
    }

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        log.Add(name);
        if (input != null || output != null)
            Seen = context.GetRenderTexture(new TextureHandle(RenderResourceID.Intern(input ?? output!)));
    }
}

file sealed class ClearSharedPass(Color color) : IExecutionPass
{
    public static readonly RenderResourceID Id = RenderResourceID.Intern("Shared");

    public string Name => "ClearShared";

    public void Setup(RenderContextBuilder builder)
        => builder.DeclareOutputTexture(Id, GraphTextureDesc.Sized(4, 4, false, PixelFormat.R32_G32_B32_A32_Float), ops: TargetLoadStoreOps.Clear(color));

    public void Render(RenderContext context, CommandBuffer cmd) { }
}

file sealed class CopySharedPass(Texture destination, GraphKind kind) : IInstancePass
{
    private TextureHandle _shared;

    public string Name => "CopyShared";

    public GraphKind Kind => kind;

    public void Setup(InstanceContextBuilder builder) => _shared = builder.DeclareInputTexture(ClearSharedPass.Id, TextureState.TransferSrc);

    public void Render(InstanceContext context, CommandBuffer cmd) => cmd.CopyTexture(context.GetRenderTexture(_shared).ColorTextures[0], destination);
}

file sealed class ThrowingPass(RenderTexture target) : IExecutionPass
{
    private static readonly RenderResourceID s_id = RenderResourceID.Intern("Thrown");

    public bool Throw = true;

    public string Name => "Throwing";

    public void Setup(RenderContextBuilder builder) => builder.DeclareImportedTexture(s_id, target);

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        cmd.SetFramebuffer(target.Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(new Color(1f, 0f, 0f, 1f)), AttachmentOps.Loaded));
        if (Throw)
            throw new InvalidOperationException("thrown");
    }
}

public abstract class DispatchRenderGraphTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Dispatch_APassThatThrows_CompletesItsExecution_SoLaterOnesRun()
    {
        RenderTexture target = RF.CreateRenderTexture(new RenderTextureDescription(4, 4, [PixelFormat.R32_G32_B32_A32_Float], false));
        ThrowingPass pass = new(target);
        using RenderGraph graph = RenderGraph.Build([pass]);
        GraphInstances instances = new(new GraphExtent(4, 4));

        for (int i = 0; i < 8; i++)
            Assert.Throws<InvalidOperationException>(() => GD.DispatchGraph(graph, instances));

        pass.Throw = false;
        GD.DispatchGraph(graph, instances);
        GD.WaitForIdle();
        Texture copy = RF.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        GD.Record(cmd => cmd.CopyTexture(target.ColorTextures[0], copy)).Wait();
        Assert.Equal(new Color(1f, 0f, 0f, 1f), ReadTexture<Color>(copy)[2, 2], ColorFuzzyComparer.Instance);
    }

    [Fact]
    public void Dispatch_InstancesRunAfterTheirParent_InOneExecution()
    {
        List<(string Pass, uint Width, ulong Execution)> log = new();
        GraphKind world = GraphKind.Intern("Test.World");
        GraphKind camera = GraphKind.Intern("Test.Camera");
        using RenderGraph graph = RenderGraph.Build(
            [new RootOrderPass(log), new OrderPass("World", world, log), new OrderPass("Camera", camera, log)],
            [new GraphScope(world, GraphKind.Root), new GraphScope(camera, world)]);
        GraphInstances instances = new();
        GraphInstance first = instances.Add(world, 0, extent: new GraphExtent(16, 16));
        instances.Add(camera, 0, first, new GraphExtent(64, 64));
        instances.Add(camera, 1, first, new GraphExtent(32, 32));
        instances.Add(world, 1, extent: new GraphExtent(8, 8));

        ExecutionTask task = GD.DispatchGraph(graph, instances);
        GD.WaitForIdle();

        Assert.Equal([("Root", 0u, task.Id), ("World", 16u, task.Id), ("Camera", 64u, task.Id), ("Camera", 32u, task.Id), ("World", 8u, task.Id)], log);
    }

    [Fact]
    public void Dispatch_AChildInstance_ReadsWhatItsParentWrote()
    {
        Texture copy = RF.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        GraphKind reader = GraphKind.Intern("Test.Reader");
        using RenderGraph graph = RenderGraph.Build(
            [new ClearSharedPass(new Color(0f, 1f, 0f, 1f)), new CopySharedPass(copy, reader)],
            [new GraphScope(reader, GraphKind.Root)]);
        GraphInstances instances = new();
        instances.Add(reader, 0);

        GD.DispatchGraph(graph, instances);
        GD.WaitForIdle();

        TexelData<Color> map = ReadTexture<Color>(copy);
        Assert.Equal(new Color(0f, 1f, 0f, 1f), map[2, 2], ColorFuzzyComparer.Instance);
    }

    [Fact]
    public void Dispatch_ADisabledPass_IsSkipped()
    {
        List<string> log = new();
        using RenderGraph graph = TestViews.Build([new FlagPass("Off", false, null, null, false, log), new FlagPass("On", true, null, null, false, log)]);

        GD.DispatchGraph(graph, TestViews.Single(graph));
        GD.WaitForIdle();

        Assert.Equal(["On"], log);
    }

    [Fact]
    public void Dispatch_ADisabledProducerWithNoPassthrough_ThrowsNamingIt()
    {
        List<string> log = new();
        using RenderGraph graph = TestViews.Build([new FlagPass("Producer", false, null, "off_color", false, log), new FlagPass("Consumer", true, "off_color", null, false, log)]);

        RenderGraphException ex = Assert.Throws<RenderGraphException>(() => GD.DispatchGraph(graph, TestViews.Single(graph)));

        Assert.Contains("Producer", ex.Message);
        Assert.Contains("Consumer", ex.Message);
        Assert.Empty(log);
    }

    [Fact]
    public void Dispatch_ADisabledPassWithAPassthrough_GivesItsConsumersTheInput()
    {
        List<string> log = new();
        FlagPass source = new("Source", true, null, "pipe_color", false, log);
        FlagPass bloom = new("Bloom", false, "pipe_color", "pipe_bloom", true, log);
        FlagPass consumer = new("Consumer", true, "pipe_bloom", null, false, log);
        using RenderGraph graph = TestViews.Build([source, bloom, consumer]);

        GD.DispatchGraph(graph, TestViews.Single(graph));
        GD.WaitForIdle();

        Assert.Equal(["Source", "Consumer"], log);
        Assert.Same(source.Seen, consumer.Seen);
    }

    [Fact]
    public void Dispatch_AnInstanceUnderTheWrongParentKind_Throws()
    {
        GraphKind world = GraphKind.Intern("Test.World");
        GraphKind camera = GraphKind.Intern("Test.Camera");
        using RenderGraph graph = RenderGraph.Build([], [new GraphScope(world, GraphKind.Root), new GraphScope(camera, world)]);
        GraphInstances instances = new();
        instances.Add(camera, 0);

        Assert.Throws<ArgumentException>(() => GD.DispatchGraph(graph, instances));
    }

    [Fact]
    public void Dispatch_RunsEachKindPassOncePerInstance()
    {
        RecordingPass passA = new();
        RecordingPass passB = new();
        using RenderGraph graph = TestViews.Build([passA, passB]);

        GD.DispatchGraph(graph, TestViews.Of(graph, (64u, 64u), (80u, 48u), (32u, 32u)));
        GD.WaitForIdle();

        Assert.Equal(new uint[] { 64, 80, 32 }, passA.ViewWidths);
        Assert.Equal(new uint[] { 64, 80, 32 }, passB.ViewWidths);
    }

    [Fact]
    public void Dispatch_InstanceWithNoTarget_SkipsTargetPass()
    {
        BackbufferPass targetPass = new();
        RecordingPass other = new();
        using RenderGraph graph = TestViews.Build([other, targetPass]);

        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(64, 64)));
        GD.WaitForIdle();

        Assert.Equal(0, targetPass.RenderCount);
        Assert.Equal(1, other.RenderCount);
    }

    [Fact]
    public void Dispatch_FramebufferTarget_PassDrawsIntoIt()
    {
        Texture color = RF.CreateTexture(TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        Framebuffer target = RF.CreateFramebuffer(new FramebufferDescription(null, color));
        BackbufferPass pass = new();
        using RenderGraph graph = TestViews.Build([pass]);

        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(64, 64), target));
        GD.WaitForIdle();

        Assert.Equal(1, pass.RenderCount);
        Assert.Same(target, pass.Resolved);
    }
}

public abstract class DispatchRenderGraphPresentTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Dispatch_TargetPass_ResolvesSwapchainAndPresents()
    {
        BackbufferPass pass = new();
        using RenderGraph graph = TestViews.Build([new RecordingPass(), pass]);
        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(64, 64), GD.MainSwapchain.Framebuffer));
        GD.WaitForIdle();

        Assert.Equal(1, pass.RenderCount);
        Assert.True(pass.SawFramebuffer);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDispatchRenderGraphTests : DispatchRenderGraphTests<VulkanDeviceCreator> { }

[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDispatchRenderGraphPresentTests : DispatchRenderGraphPresentTests<VulkanDeviceCreatorWithMainSwapchain> { }
#endif
