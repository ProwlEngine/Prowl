using System;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// Stock effects of one renderer share their programs, so a new instance compiles nothing already built
[Collection("GPU Tests")]
public sealed class EffectResourcesTests : IDisposable
{
    private readonly RendererHarness _harness = new();
    private readonly Renderer _renderer;

    public EffectResourcesTests() => _renderer = _harness.Renderer;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void SecondInstance_ReusesPrograms()
    {
        _ = new Bloom(_renderer);
        int built = _renderer.Effects.ProgramCount;
        _ = new Bloom(_renderer);
        _ = new TemporalAntiAliasing(_renderer);
        _ = new TemporalAntiAliasing(_renderer);

        Assert.Equal(built + 3, _renderer.Effects.ProgramCount);
    }
}
