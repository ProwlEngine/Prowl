using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

[Collection("GPU Tests")]
public sealed class DebugViewTests
{
    private readonly GlslShaderCompiler _compiler = TestCompiler.Shared;

    public DebugViewTests() => ShaderLibrary.Register(typeof(Renderer).Assembly);

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

    // The PBR model's own views follow the shared ones
    private static readonly string[] s_pbrModes = [.. s_modes, "DIFFUSE", "SPECULAR", "SPECULAR_ALBEDO", "THROUGHPUT", "DIRECT", "INDIRECT", "FURNACE", "COAT", "FUZZ", "THIN_FILM", "DARKENING", "SUBSURFACE", "BACK", "TRANSMISSION", "THICKNESS"];

    private static string[] ModesOf(string shader) => shader == "Lit" ? s_pbrModes : s_modes;

    public static IEnumerable<object[]> Modes()
    {
        foreach (string shader in new[] { "Lit", "Unlit" })
            foreach (string mode in ModesOf(shader))
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
            Assert.Equal(ModesOf(name), axis.Values);
            foreach (ShaderPass pass in definition.Passes!.Where(p => p.Name != "Debug"))
                Assert.DoesNotContain(_compiler.GetAxes(pass), a => a.Name == Shader.DebugAxis);
        }
    }

    // PBR's Debug pass also declares PBR_TRANSMISSION, standing in for its Transmission pass as well
    [Fact]
    public void TheDebugPass_HasTheForwardPassesMaterialAxes()
    {
        foreach (string name in new[] { "Lit", "Unlit" })
        {
            ShaderDefinition definition = Load(name);
            IEnumerable<string> forward = _compiler.GetAxes(Pass(definition, "Forward")).Select(a => a.Name);
            Assert.Equal(
                name == "Lit" ? forward.Append("PBR_TRANSMISSION").Order(StringComparer.Ordinal) : forward.Order(StringComparer.Ordinal),
                _compiler.GetAxes(Pass(definition, "Debug")).Where(a => a.Name != Shader.DebugAxis).Select(a => a.Name).Order(StringComparer.Ordinal));
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
