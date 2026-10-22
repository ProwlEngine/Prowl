using System;

using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// Stock effects of one renderer share their programs, so a new instance compiles nothing already built
[Collection("GPU Tests")]
public sealed class EffectResourcesTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly Renderer _renderer;

    public EffectResourcesTests()
        => _renderer = new Renderer(_device, _compiler, new RendererOptions { Threading = RenderThreading.Inline, Deterministic = true, PageVertexWords = 1 << 16, PageIndices = 1 << 16 });

    public void Dispose()
    {
        _renderer.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    [Fact]
    public void SecondInstance_ReusesPrograms()
    {
        _ = new Bloom(_renderer);
        int built = _renderer.Effects.ProgramCount;
        _ = new Bloom(_renderer);
        _ = new TemporalAntiAliasing(_renderer);
        _ = new TemporalAntiAliasing(_renderer);

        Assert.Equal(built + 2, _renderer.Effects.ProgramCount);
    }
}
