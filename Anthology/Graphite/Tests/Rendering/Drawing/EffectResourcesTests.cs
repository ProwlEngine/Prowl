using System;

using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// Stock effects of one renderer share their programs, so a new instance compiles nothing already built
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
        var first = new Bloom(_renderer);
        int built = EffectResources.Peek(_renderer)!.ProgramCount;
        var second = new Bloom(_renderer);
        var taa = new TemporalAntiAliasing(_renderer);
        var taa2 = new TemporalAntiAliasing(_renderer);

        Assert.Equal(built + 2, EffectResources.Peek(_renderer)!.ProgramCount);

        first.Dispose();
        second.Dispose();
        taa.Dispose();
        Assert.NotNull(EffectResources.Peek(_renderer));
        taa2.Dispose();
        Assert.Null(EffectResources.Peek(_renderer));
    }

    [Fact]
    public void DisposeTwice_ReleasesOnce()
    {
        var keep = new Vignette(_renderer);
        var other = new Vignette(_renderer);
        other.Dispose();
        other.Dispose();
        Assert.NotNull(EffectResources.Peek(_renderer));
        keep.Dispose();
        Assert.Null(EffectResources.Peek(_renderer));
    }
}
