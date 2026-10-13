using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

/// <summary>Builds bake every pass in the catalog, so every pass the renderer makes has to be in it.</summary>
public sealed class ShaderCatalogTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly Renderer _renderer;

    public ShaderCatalogTests()
    {
        _renderer = new Renderer(_device, _compiler, new RendererOptions { Threading = RenderThreading.Inline, Deterministic = true, PageVertexWords = 1 << 16, PageIndices = 1 << 16 });
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    [Fact]
    public void EveryProgramTheRendererAndItsEffectsMake_IsInTheCatalog()
    {
        List<IDisposable> effects = new();
        foreach (Type type in typeof(Renderer).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(PostEffect))))
        {
            if (type.GetConstructor([typeof(Renderer)]) is { } constructor)
                effects.Add((IDisposable)constructor.Invoke([_renderer]));
        }
        Assert.NotEmpty(effects);

        HashSet<(string, string)> catalog = ShaderSources.All().Select(source => (source.Pass.SourceName, source.Pass.Source)).ToHashSet();
        foreach (string name in ShaderLibrary.Names.Where(name => name.EndsWith(".shader", StringComparison.Ordinal)))
        {
            foreach (ShaderPass pass in ShaderParser.Parse(ShaderLibrary.Get(name)!).Passes ?? [])
                catalog.Add((pass.SourceName, pass.Source));
        }

        List<string> missing = PassVariants.Live
            .Where(variants => variants.Compiler == _compiler && !catalog.Contains((variants.Pass.SourceName, variants.Pass.Source)))
            .Select(variants => variants.Pass.SourceName)
            .Distinct()
            .ToList();
        foreach (IDisposable effect in effects)
            effect.Dispose();
        Assert.Empty(missing);
    }
}
