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

    private static ShaderPass Pass(ShaderDefinition shader, string type)
        => shader.Passes!.First(p => p.Tags != null && p.Tags.TryGetValue("LightMode", out string? mode) && mode == type);

    private static readonly string[] s_modes = ["UNLIT", "ALBEDO", "NORMALS", "ROUGHNESS", "METALLIC", "AO", "EMISSION", "LIGHTING", "CASCADES", "LIGHTMAP_DENSITY"];

    public static IEnumerable<object[]> Modes()
    {
        foreach (string shader in new[] { "Lit", "Unlit" })
            foreach (string mode in s_modes)
                yield return [shader, mode];
    }

    [Fact]
    public void OnlyTheDebugPass_DeclaresTheGlobalDebugAxis()
    {
        foreach (string name in new[] { "Lit", "Unlit" })
        {
            ShaderDefinition definition = Load(name);
            VariantSpace axis = _compiler.GetAxes(Pass(definition, "Debug")).Single(a => a.Name == Shader.DebugAxis);
            Assert.Equal(VariantScope.Global, axis.Scope);
            Assert.Equal(s_modes, axis.Values);
            foreach (ShaderPass pass in definition.Passes!.Where(p => p.Name != "Debug"))
                Assert.DoesNotContain(_compiler.GetAxes(pass), a => a.Name == Shader.DebugAxis);
        }
    }

    [Fact]
    public void TheDebugPass_HasTheForwardPassesMaterialAxes()
    {
        foreach (string name in new[] { "Lit", "Unlit" })
        {
            ShaderDefinition definition = Load(name);
            Assert.Equal(
                _compiler.GetAxes(Pass(definition, "Forward")).Select(a => a.Name),
                _compiler.GetAxes(Pass(definition, "Debug")).Where(a => a.Name != Shader.DebugAxis).Select(a => a.Name));
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EveryDebugMode_Compiles(string shader, string mode)
    {
        ShaderPass pass = Pass(Load(shader), "Debug");
        List<Keyword> combo = [new Keyword("_SURFACE", "OPAQUE"), new Keyword(Shader.DebugAxis, mode)];
        foreach (VariantSpace axis in _compiler.GetAxes(pass))
        {
            if (axis.IsToggle)
                combo.Add(new Keyword(axis.Name, VariantSpace.ToggleValues[1]));
        }
        _compiler.CompileProgram(pass, [.. combo]);
    }
}
