#nullable enable

using System.Collections.Generic;
using Xunit;

namespace Prowl.Graphite.RenderGraph.Tests;

file sealed class CountingPass : IPass<TestView>
{
    public int SetupCount { get; private set; }

    public string Name => "Counting";

    public void Setup(RenderContextBuilder builder)
    {
        SetupCount++;
        builder.DeclareOutputTexture("pipeline_counting_out", Desc.Color());
    }

    public void Render(RenderContext<TestView> context, CommandBuffer cmd) { }
}

file sealed class CountingPipeline : RenderPipeline<TestView>
{
    private readonly CountingPass _pass;

    public CountingPipeline(CountingPass pass) => _pass = pass;

    protected override void InitializePasses() => AddPass(_pass);
}

file sealed class ReconfigurablePipeline : RenderPipeline<TestView>
{
    public IPass<TestView> ActivePass { get; set; }

    public ReconfigurablePipeline(IPass<TestView> initialPass)
        => ActivePass = initialPass;

    protected override void InitializePasses()
    {
        AddPass(ActivePass);
    }

    public void PublicInvalidateGraph() => InvalidateGraph();
}

file sealed class ComposedInvalidatable : RenderPipeline<TestView>
{
    public ComposedInvalidatable(IEnumerable<IPass<TestView>> passes) : base(passes) { }

    public void PublicInvalidateGraph() => InvalidateGraph();
}

public class RenderPipelineTests
{
    [Fact]
    public void Composed_BuildsGraphFromPassList()
    {
        CountingPass pass = new();
        RenderPipeline<TestView> pipeline = new([pass]);

        _ = pipeline.Graph;

        Assert.Equal(1, pass.SetupCount);
    }

    [Fact]
    public void Composed_InvalidateGraph_ReaddsPasses()
    {
        CountingPass pass = new();
        ComposedInvalidatable pipeline = new([pass]);

        _ = pipeline.Graph;
        pipeline.PublicInvalidateGraph();
        _ = pipeline.Graph;

        Assert.Equal(2, pass.SetupCount);
    }

    [Fact]
    public void Graph_AccessedMultipleTimes_BuildsOnlyOnce()
    {
        CountingPass pass = new();
        CountingPipeline pipeline = new(pass);

        _ = pipeline.Graph;
        _ = pipeline.Graph;
        _ = pipeline.Graph;

        Assert.Equal(1, pass.SetupCount);
    }

    [Fact]
    public void Graph_ReturnsSameInstance_OnRepeatedAccess()
    {
        CountingPipeline pipeline = new(new CountingPass());

        RenderGraph<TestView> first = pipeline.Graph;
        RenderGraph<TestView> second = pipeline.Graph;

        Assert.Same(first, second);
    }

    [Fact]
    public void InvalidateGraph_RebuildsWithPassesFromNextInitializePasses()
    {
        var passA = new TestPass("A", outputs: new[] { ("res", Desc.Color()) });
        var passB = new TestPass("B", outputs: new[] { ("res", Desc.Color()) });

        ReconfigurablePipeline pipeline = new(passA);

        RenderGraph<TestView> first = pipeline.Graph;
        Assert.Equal("A", first.OrderedPasses[0].Pass.Name);

        pipeline.PublicInvalidateGraph();
        pipeline.ActivePass = passB;

        RenderGraph<TestView> second = pipeline.Graph;
        Assert.NotSame(first, second);
        Assert.Single(second.OrderedPasses);
        Assert.Equal("B", second.OrderedPasses[0].Pass.Name);
    }
}
