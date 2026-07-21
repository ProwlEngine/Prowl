#nullable enable

using Xunit;

namespace Prowl.Graphite.RenderGraph.Tests;

file sealed class CountingPass : IPass<TestView>
{
    public int SetupCount { get; private set; }

    public string Name => "Counting";

    public void Setup(RenderContextBuilder builder)
    {
        SetupCount++;
        builder.GetOutputTexture("pipeline_counting_out", Desc.Color());
    }

    public void Render(RenderContext<TestView> context) { }
}

file sealed class NoOpPresentPass : IPresentPass<TestView>
{
    public string Name => "Present";

    public void Setup(PresentContextBuilder builder) { }

    public void Present(RenderContext<TestView> context) { }
}

file sealed class CountingPresentPass : IPresentPass<TestView>
{
    public int SetupCount { get; private set; }

    public string Name => "CountingPresent";

    public void Setup(PresentContextBuilder builder) => SetupCount++;

    public void Present(RenderContext<TestView> context) { }
}

file sealed class CountingPipeline : RenderPipeline<TestView>
{
    private readonly CountingPass _pass;
    private readonly IPresentPass<TestView> _present;

    public CountingPipeline(CountingPass pass, IPresentPass<TestView>? present = null)
    {
        _pass = pass;
        _present = present ?? new NoOpPresentPass();
    }

    protected override void InitializePasses()
    {
        AddPass(_pass);
        SetPresentPass(_present);
    }
}

public class RenderPipelineTests
{
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
    public void Graph_AccessedMultipleTimes_RunsPresentPassSetupOnlyOnce()
    {
        CountingPresentPass present = new();
        CountingPipeline pipeline = new(new CountingPass(), present);

        _ = pipeline.Graph;
        _ = pipeline.Graph;
        _ = pipeline.Graph;

        Assert.Equal(1, present.SetupCount);
    }
}
