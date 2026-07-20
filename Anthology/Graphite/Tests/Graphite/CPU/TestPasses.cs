#nullable enable

using System;
using System.Collections.Generic;

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

/// <summary>Pass for testing the solver. Declares given input/output resource names.</summary>
internal sealed class TestPass : IPass<TestView, int>
{
    private readonly (string name, GraphTextureDesc desc)[] _inputs;
    private readonly (string name, GraphTextureDesc desc)[] _outputs;
    private readonly string? _mainOutput;

    public TestPass(string name,
        (string, GraphTextureDesc)[]? inputs = null,
        (string, GraphTextureDesc)[]? outputs = null,
        string? mainOutput = null)
    {
        Name = name;
        _inputs = inputs ?? Array.Empty<(string, GraphTextureDesc)>();
        _outputs = outputs ?? Array.Empty<(string, GraphTextureDesc)>();
        _mainOutput = mainOutput;
    }

    public string Name { get; }

    public void Setup(RenderContextBuilder builder)
    {
        foreach ((string name, GraphTextureDesc desc) in _inputs)
            builder.GetInputTexture(name, desc);

        TextureHandle main = default;
        foreach ((string name, GraphTextureDesc desc) in _outputs)
        {
            TextureHandle handle = builder.GetOutputTexture(name, desc);
            if (_mainOutput != null && name == _mainOutput)
                main = handle;
        }

        if (_mainOutput != null)
            builder.SetMainOutput(main);
    }

    public void Render(RenderContext<TestView, int> context) { }
}

/// <summary>Pass that nominates a main output it only declared as input, to test the solver's guard.</summary>
internal sealed class MisdeclaredMainOutputPass : IPass<TestView, int>
{
    public string Name => "Misdeclared";

    public void Setup(RenderContextBuilder builder)
    {
        TextureHandle onlyInput = builder.GetInputTexture("SomeInput", Desc.Color());
        builder.GetOutputTexture("SomeOutput", Desc.Color());
        builder.SetMainOutput(onlyInput);
    }

    public void Render(RenderContext<TestView, int> context) { }
}

internal static class Desc
{
    public static GraphTextureDesc Color() => GraphTextureDesc.ViewSized(false, 1f, PixelFormat.R8_G8_B8_A8_UNorm);
}
