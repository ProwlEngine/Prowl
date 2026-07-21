#nullable enable

using System;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite.RenderGraph.Tests;

internal readonly struct TestView : IRenderView
{
    public TestView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
}

/// <summary>Pass for testing the solver. Declares given input names and output textures.</summary>
internal sealed class TestPass : IPass<TestView>
{
    private readonly string[] _inputs;
    private readonly (string name, GraphTextureDesc desc)[] _outputs;

    public TestPass(string name,
        string[]? inputs = null,
        (string, GraphTextureDesc)[]? outputs = null)
    {
        Name = name;
        _inputs = inputs ?? Array.Empty<string>();
        _outputs = outputs ?? Array.Empty<(string, GraphTextureDesc)>();
    }

    public string Name { get; }

    public void Setup(RenderContextBuilder builder)
    {
        foreach (string name in _inputs)
            builder.GetInputTexture(name);

        foreach ((string name, GraphTextureDesc desc) in _outputs)
            builder.GetOutputTexture(name, desc);
    }

    public void Render(RenderContext<TestView> context) { }
}

/// <summary>Pass that reads and writes buffer resources, for testing buffer ordering.</summary>
internal sealed class TestBufferPass : IPass<TestView>
{
    private readonly string[] _inputs;
    private readonly (string name, GraphBufferDesc desc)[] _outputs;

    public TestBufferPass(string name,
        string[]? inputs = null,
        (string, GraphBufferDesc)[]? outputs = null)
    {
        Name = name;
        _inputs = inputs ?? Array.Empty<string>();
        _outputs = outputs ?? Array.Empty<(string, GraphBufferDesc)>();
    }

    public string Name { get; }

    public void Setup(RenderContextBuilder builder)
    {
        foreach (string name in _inputs)
            builder.GetInputBuffer(name);

        foreach ((string name, GraphBufferDesc desc) in _outputs)
            builder.GetOutputBuffer(name, desc);
    }

    public void Render(RenderContext<TestView> context) { }
}

internal static class Desc
{
    public static GraphTextureDesc Color() => GraphTextureDesc.ViewSized(false, 1f, PixelFormat.R8_G8_B8_A8_UNorm);
    public static GraphBufferDesc Storage() => GraphBufferDesc.Structured(16, 4);
}

/// <summary>No-op present pass for tests that only need to solve/build a graph, not present it.</summary>
internal sealed class NoOpTestPresentPass : IPresentPass<TestView>
{
    public string Name => "TestNoOpPresent";

    public void Setup(PresentContextBuilder builder) { }

    public void Present(RenderContext<TestView> context) { }
}

/// <summary>Present pass for testing the solver's handling of declared present inputs and swapchain requests.</summary>
internal sealed class TestPresentPass : IPresentPass<TestView>
{
    private readonly string[] _inputs;
    private readonly bool _requestSwapchain;

    public TestPresentPass(bool requestSwapchain = false, string[]? inputs = null)
    {
        _requestSwapchain = requestSwapchain;
        _inputs = inputs ?? Array.Empty<string>();
    }

    public string Name => "TestPresent";

    public void Setup(PresentContextBuilder builder)
    {
        foreach (string name in _inputs)
            builder.GetInputTexture(name);

        if (_requestSwapchain)
            builder.RequestSwapchain();
    }

    public void Present(RenderContext<TestView> context) { }
}
