#nullable enable

using System;
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

public class RenderPipelineTests
{
    [Fact]
    public void Graph_AccessedMultipleTimes_BuildsOnlyOnce()
    {
        CountingPass pass = new();
        RenderPipeline<TestView> pipeline = new([pass]);

        _ = pipeline.Graph;
        _ = pipeline.Graph;
        _ = pipeline.Graph;

        Assert.Equal(1, pass.SetupCount);
    }

    [Fact]
    public void SetPasses_ReplacesPassesAndGraph()
    {
        var passA = new TestPass("A", outputs: new[] { ("res", Desc.Color()) });
        var passB = new TestPass("B", outputs: new[] { ("res", Desc.Color()) });

        RenderPipeline<TestView> pipeline = new([passA]);

        RenderGraph<TestView> first = pipeline.Graph;
        Assert.Equal("A", first.OrderedPasses[0].Pass.Name);

        pipeline.SetPasses([passB]);

        RenderGraph<TestView> second = pipeline.Graph;
        Assert.NotSame(first, second);
        Assert.Single(second.OrderedPasses);
        Assert.Equal("B", second.OrderedPasses[0].Pass.Name);
    }
}
