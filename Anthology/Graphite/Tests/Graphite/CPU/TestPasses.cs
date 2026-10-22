#nullable enable

using System;

using Prowl.Graphite;

namespace Prowl.Graphite.Tests;

/// <summary>Test pass for the solver. Declares given input names, write names and output textures.</summary>
internal sealed class TestPass : IInstancePass
{
    private readonly string[] _inputs;
    private readonly string[] _writes;
    private readonly (string name, GraphTextureDesc desc)[] _outputs;

    public TestPass(string name,
        string[]? inputs = null,
        (string, GraphTextureDesc)[]? outputs = null,
        GraphKind? kind = null,
        string[]? writes = null)
    {
        Name = name;
        Kind = kind ?? TestViews.Kind;
        _inputs = inputs ?? Array.Empty<string>();
        _writes = writes ?? Array.Empty<string>();
        _outputs = outputs ?? Array.Empty<(string, GraphTextureDesc)>();
    }

    public string Name { get; }

    public GraphKind Kind { get; }

    public void Setup(InstanceContextBuilder builder)
    {
        foreach (string name in _inputs)
            builder.DeclareInputTexture(name);

        foreach (string name in _writes)
            builder.DeclareWriteTexture(name);

        foreach ((string name, GraphTextureDesc desc) in _outputs)
            builder.DeclareOutputTexture(name, desc);
    }

    public void Render(InstanceContext context, CommandBuffer cmd) { }
}

/// <summary>Test pass for the root. Declares given input names and fixed size output textures.</summary>
internal sealed class TestRootPass : IExecutionPass
{
    private readonly string[] _inputs;
    private readonly (string name, GraphTextureDesc desc)[] _outputs;

    public TestRootPass(string name,
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

internal static class Desc
{
    public static GraphTextureDesc Color() => GraphTextureDesc.InstanceSized(PixelFormat.R8_G8_B8_A8_UNorm);
    public static GraphTextureDesc Fixed() => GraphTextureDesc.Sized(4, 4, false, PixelFormat.R8_G8_B8_A8_UNorm);
    public static GraphBufferDesc Storage() => GraphBufferDesc.Structured(16, 4);
}

/// <summary>Test pass that writes the backbuffer and reads the named textures.</summary>
internal sealed class TestBackbufferPass : IInstancePass
{
    private readonly string[] _inputs;

    public TestBackbufferPass(string[]? inputs = null) => _inputs = inputs ?? Array.Empty<string>();

    public string Name => "TestBackbuffer";

    public GraphKind Kind => TestViews.Kind;

    public void Setup(InstanceContextBuilder builder)
    {
        foreach (string name in _inputs)
            builder.DeclareInputTexture(name);

        builder.DeclareInstanceTarget();
    }

    public void Render(InstanceContext context, CommandBuffer cmd) { }
}
