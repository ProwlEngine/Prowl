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
    public int ViewId => 0;
}

/// <summary>Test pass for the solver. Declares given input names and output textures.</summary>
internal sealed class TestPass : IPass
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
            builder.DeclareInputTexture(name);

        foreach ((string name, GraphTextureDesc desc) in _outputs)
            builder.DeclareOutputTexture(name, desc);
    }

    public void Render(RenderContext context, CommandBuffer cmd) { }
}

/// <summary>Test pass reading/writing buffers, for testing buffer ordering.</summary>
internal sealed class TestBufferPass : IPass
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
            builder.DeclareInputBuffer(name);

        foreach ((string name, GraphBufferDesc desc) in _outputs)
            builder.DeclareOutputBuffer(name, desc);
    }

    public void Render(RenderContext context, CommandBuffer cmd) { }
}

internal static class Desc
{
    public static GraphTextureDesc Color() => GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm);
    public static GraphBufferDesc Storage() => GraphBufferDesc.Structured(16, 4);
}

/// <summary>Test pass that writes the backbuffer and reads the named textures.</summary>
internal sealed class TestBackbufferPass : IPass
{
    private readonly string[] _inputs;

    public TestBackbufferPass(string[]? inputs = null) => _inputs = inputs ?? Array.Empty<string>();

    public string Name => "TestBackbuffer";

    public void Setup(RenderContextBuilder builder)
    {
        foreach (string name in _inputs)
            builder.DeclareInputTexture(name);

        builder.DeclareViewTarget();
    }

    public void Render(RenderContext context, CommandBuffer cmd) { }
}
