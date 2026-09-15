#nullable enable

using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;

using Xunit;

namespace Prowl.Graphite.Tests;

public readonly struct HistoryView : IRenderView
{
    public HistoryView(int viewId, uint width, uint height)
    {
        ViewId = viewId;
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId { get; }
}

file sealed class ViewHistoryPass : IPass<HistoryView>
{
    private readonly RenderResourceID _id;
    private readonly GraphTextureDesc _desc;
    private TextureHandle _handle;

    public ViewHistoryPass(RenderResourceID id, GraphTextureDesc desc)
    {
        _id = id;
        _desc = desc;
    }

    public string Name => "ViewHistory";

    public Dictionary<int, List<RenderTexture>> Current { get; } = new();
    public Dictionary<int, List<RenderTexture>> Previous { get; } = new();
    public Dictionary<int, List<bool>> Valid { get; } = new();

    public void Setup(RenderContextBuilder builder) => _handle = builder.GetOutputTexture(_id, _desc, history: 1);

    public void Render(RenderContext<HistoryView> context)
    {
        int view = context.View.ViewId;
        Record(Valid, view, context.IsHistoryValid(_handle));
        Record(Current, view, context.GetRenderTexture(_handle, 0));
        Record(Previous, view, context.GetRenderTexture(_handle, 1));
    }

    private static void Record<T>(Dictionary<int, List<T>> map, int view, T value)
    {
        if (!map.TryGetValue(view, out List<T>? list))
            map[view] = list = new List<T>();
        list.Add(value);
    }
}

file sealed class ViewBufferHistoryPass : IPass<HistoryView>
{
    private readonly RenderResourceID _id;
    private BufferHandle _handle;

    public ViewBufferHistoryPass(RenderResourceID id) => _id = id;

    public string Name => "ViewBufferHistory";

    public Dictionary<int, List<DeviceBuffer>> Current { get; } = new();
    public Dictionary<int, List<DeviceBuffer>> Previous { get; } = new();

    public void Setup(RenderContextBuilder builder)
        => _handle = builder.GetOutputBuffer(_id, GraphBufferDesc.Structured(16, 16), history: 1);

    public void Render(RenderContext<HistoryView> context)
    {
        int view = context.View.ViewId;
        if (!Current.ContainsKey(view))
        {
            Current[view] = new List<DeviceBuffer>();
            Previous[view] = new List<DeviceBuffer>();
        }
        Current[view].Add(context.GetRenderBuffer(_handle, 0));
        Previous[view].Add(context.GetRenderBuffer(_handle, 1));
    }
}

file sealed class NoOpPresentPass : IPresentPass<HistoryView>
{
    public string Name => "Present";

    public void Setup(PresentContextBuilder builder) { }

    public void Present(RenderContext<HistoryView> context) { }
}

file sealed class HistoryTestPipeline : RenderPipeline<HistoryView>
{
    private readonly IPass<HistoryView> _pass;

    public HistoryTestPipeline(IPass<HistoryView> pass) => _pass = pass;

    protected override void InitializePasses()
    {
        AddPass(_pass);
        SetPresentPass(new NoOpPresentPass());
    }
}

public abstract class ViewHistoryTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private static GraphTextureDesc ColorDesc()
        => GraphTextureDesc.ViewSized(false, 1f, PixelFormat.R8_G8_B8_A8_UNorm);

    private void Dispatch(RenderPipeline<HistoryView> pipeline, params HistoryView[] views)
    {
        ExecutionTask task = GD.DispatchGraph(pipeline, views);
        GD.WaitForExecution(task);
    }

    [Fact]
    public void TwoViewsInOneDispatch_EachReadsItsOwnPreviousFrame()
    {
        ViewHistoryPass pass = new(RenderResourceID.Intern("viewhistory_alternating"), ColorDesc());
        using HistoryTestPipeline pipeline = new(pass);
        HistoryView a = new(1, 64, 64);
        HistoryView b = new(2, 64, 64);

        for (int frame = 0; frame < 3; frame++)
            Dispatch(pipeline, a, b);

        foreach (int view in new[] { 1, 2 })
        {
            Assert.Equal(3, pass.Current[view].Count);
            for (int frame = 0; frame < 3; frame++)
                Assert.NotSame(pass.Current[view][frame], pass.Previous[view][frame]);

            Assert.Same(pass.Current[view][0], pass.Previous[view][1]);
            Assert.Same(pass.Current[view][1], pass.Previous[view][2]);
        }

        for (int frame = 0; frame < 3; frame++)
        {
            Assert.NotSame(pass.Current[1][frame], pass.Current[2][frame]);
            Assert.NotSame(pass.Previous[1][frame], pass.Previous[2][frame]);
        }
    }

    [Fact]
    public void TwoViewsInOneDispatch_BufferHistoryIsPerView()
    {
        ViewBufferHistoryPass pass = new(RenderResourceID.Intern("viewhistory_buffer"));
        using HistoryTestPipeline pipeline = new(pass);
        HistoryView a = new(1, 64, 64);
        HistoryView b = new(2, 64, 64);

        for (int frame = 0; frame < 2; frame++)
            Dispatch(pipeline, a, b);

        Assert.Same(pass.Current[1][0], pass.Previous[1][1]);
        Assert.Same(pass.Current[2][0], pass.Previous[2][1]);
        Assert.NotSame(pass.Current[1][0], pass.Current[2][0]);
        Assert.NotSame(pass.Current[1][1], pass.Current[2][1]);
    }

    [Fact]
    public void IsHistoryValid_FalseOnFirstExecution_TrueAfter_FalseAgainAfterResize()
    {
        ViewHistoryPass pass = new(RenderResourceID.Intern("viewhistory_valid"), ColorDesc());
        using HistoryTestPipeline pipeline = new(pass);

        Dispatch(pipeline, new HistoryView(1, 64, 64));
        Dispatch(pipeline, new HistoryView(1, 64, 64));
        Dispatch(pipeline, new HistoryView(1, 128, 128));
        Dispatch(pipeline, new HistoryView(1, 128, 128));

        Assert.Equal(new[] { false, true, false, true }, pass.Valid[1]);
    }

    [Fact]
    public void IsHistoryValid_IsTrackedPerView()
    {
        ViewHistoryPass pass = new(RenderResourceID.Intern("viewhistory_valid_per_view"), ColorDesc());
        using HistoryTestPipeline pipeline = new(pass);

        Dispatch(pipeline, new HistoryView(1, 64, 64));
        Dispatch(pipeline, new HistoryView(1, 64, 64), new HistoryView(2, 64, 64));

        Assert.Equal(new[] { false, true }, pass.Valid[1]);
        Assert.Equal(new[] { false }, pass.Valid[2]);
    }

    [Fact]
    public void ViewNotRenderedFor120Executions_HasItsRingDisposed()
    {
        ViewHistoryPass pass = new(RenderResourceID.Intern("viewhistory_disposal"), ColorDesc());
        using HistoryTestPipeline pipeline = new(pass);
        HistoryView a = new(1, 64, 64);
        HistoryView b = new(2, 64, 64);

        Dispatch(pipeline, a, b);
        RenderTexture bCurrent = pass.Current[2][0];
        RenderTexture bPrevious = pass.Previous[2][0];

        for (int i = 0; i < 120; i++)
            Dispatch(pipeline, a);

        Assert.False(bCurrent.Framebuffer.IsDisposed);
        Assert.False(bPrevious.Framebuffer.IsDisposed);

        Dispatch(pipeline, a);

        Assert.True(bCurrent.Framebuffer.IsDisposed);
        Assert.True(bPrevious.Framebuffer.IsDisposed);
        Assert.False(pass.Current[1][^1].Framebuffer.IsDisposed);
        Assert.False(pass.Previous[1][^1].Framebuffer.IsDisposed);

        Dispatch(pipeline, a, b);

        Assert.False(pass.Valid[2][^1]);
        Assert.NotSame(bCurrent, pass.Current[2][^1]);
        Assert.NotSame(bPrevious, pass.Previous[2][^1]);
    }

    [Fact]
    public void ViewRenderedWithin120Executions_KeepsItsRing()
    {
        ViewHistoryPass pass = new(RenderResourceID.Intern("viewhistory_retained"), ColorDesc());
        using HistoryTestPipeline pipeline = new(pass);
        HistoryView a = new(1, 64, 64);
        HistoryView b = new(2, 64, 64);

        Dispatch(pipeline, a, b);
        RenderTexture bCurrent = pass.Current[2][0];

        for (int i = 0; i < 119; i++)
            Dispatch(pipeline, a);

        Dispatch(pipeline, a, b);

        Assert.True(pass.Valid[2][^1]);
        Assert.Same(bCurrent, pass.Previous[2][^1]);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanViewHistoryTests : ViewHistoryTests<VulkanDeviceCreator> { }
#endif
