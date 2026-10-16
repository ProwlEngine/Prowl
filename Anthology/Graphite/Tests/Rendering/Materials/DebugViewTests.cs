using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

public sealed class DebugViewTests : IDisposable
{
    private readonly GlslShaderCompiler _compiler = new();

    public DebugViewTests() => ShaderLibrary.Register(typeof(Renderer).Assembly);

    public void Dispose() => _compiler.Dispose();

    // Made into a renderer shader, so its passes compile with the material ABI
    private ShaderDefinition Load(string name)
    {
        ShaderDefinition definition = ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Standard/{name}.shader")!);
        _ = new Shader(definition, _compiler);
        return definition;
    }

    private static ShaderPass Forward(ShaderDefinition shader)
        => shader.Passes!.First(p => p.Tags != null && p.Tags.TryGetValue("LightMode", out string? mode) && mode == "Forward");

    public static IEnumerable<object[]> Modes()
    {
        foreach (string shader in new[] { "Lit", "Unlit" })
            foreach (string mode in new[] { "OFF", "UNLIT", "ALBEDO", "NORMALS", "ROUGHNESS", "METALLIC", "AO", "EMISSION", "LIGHTING", "CASCADES", "LIGHTMAP_DENSITY" })
                yield return [shader, mode];
    }

    [Fact]
    public void ForwardPasses_DeclareTheGlobalDebugAxis()
    {
        foreach (string name in new[] { "Lit", "Unlit" })
        {
            VariantSpace axis = _compiler.GetAxes(Forward(Load(name))).Single(a => a.Name == "GRAPHITE_DEBUG");
            Assert.Equal(VariantScope.Global, axis.Scope);
            Assert.Equal("OFF", axis.Values[0]);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EveryDebugMode_Compiles(string shader, string mode)
    {
        ShaderPass pass = Forward(Load(shader));
        List<Keyword> combo = [new Keyword("_SURFACE", "OPAQUE"), new Keyword("GRAPHITE_DEBUG", mode)];
        foreach (VariantSpace axis in _compiler.GetAxes(pass))
        {
            if (axis.IsToggle)
                combo.Add(new Keyword(axis.Name, VariantSpace.ToggleValues[1]));
        }
        _compiler.CompileProgram(pass, [.. combo]);
    }
}
