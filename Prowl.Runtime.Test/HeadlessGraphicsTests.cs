// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Resources;

using Xunit;

using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Test;

/// <summary>
/// GPU backed resources must work without a device (headless or dedicated server): their CPU state works and the
/// Graphite objects stay absent.
/// </summary>
public class HeadlessGraphicsTests
{
    [Fact]
    public void Texture2D_LargeSize_DoesNotFailValidationHeadless()
    {
        var tex = new Texture2D(4096, 4096);
        Assert.Equal(4096u, tex.Width);
    }

    [Fact]
    public void Material_CreatesHeadless_WithoutThrowing()
    {
        var mat = new Material();
        Assert.NotNull(mat.Shader);
        Assert.Null(mat.GpuMaterial);
    }

    public static TheoryData<DefaultShader> AllDefaultShaders()
    {
        var data = new TheoryData<DefaultShader>();
        foreach (DefaultShader s in System.Enum.GetValues<DefaultShader>())
            data.Add(s);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllDefaultShaders))]
    public void DefaultShader_ParsesHeadless(DefaultShader shader)
    {
        Shader loaded = Shader.LoadDefault(shader);

        Assert.Null(loaded.Error);
        Assert.NotNull(loaded.GpuShader);
        Assert.NotEmpty(Passes(loaded));
    }

    [Theory]
    [MemberData(nameof(AllDefaultShaders))]
    public void DefaultShader_EveryPassCompiles(DefaultShader shader)
    {
        CompileEveryPass(Shader.LoadDefault(shader));
    }

    private const string GridInclude = "float GridShade(vec3 p) { return fract(p.x); }";

    [Theory]
    [InlineData("#include \"Grid\"", "#include \"Grid\"")]
    [InlineData("#include \"Grid\"\n#include \"Grid\"", "#include \"Grid.glsl\"")]
    [InlineData("#include \"Grid\"\n#include \"Grid.glsl\"", "#include \"Grid\"")]
    [InlineData("#if 0\n#include \"Grid\"\n#endif\n#include \"Grid\"", "#include \"Grid\"")]
    [InlineData("#pragma variant FOO\n#ifdef FOO\n#include \"Grid\"\n#endif\n#include \"Grid\"", "#include \"Grid\"")]
    public void AnInclude_ReachesEveryStageOnce(string vertexIncludes, string fragmentIncludes)
    {
        CompileEveryVariant(WithGridIncludes($$"""
            Shader "Test/Includes"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        {{vertexIncludes}}
                        void main() { gl_Position = vec4(GridShade(vec3(0.5))); }
                    }
                    Fragment
                    {
                        {{fragmentIncludes}}
                        layout(location = 0) out vec4 color;
                        void main() { color = vec4(GridShade(vec3(0.5))); }
                    }
                    ENDGLSL
                }
            }
            """));
    }

    [Fact]
    public void ASurfaceShader_IncludesOneFileInItsSurfaceAndVertexBlocks()
    {
        CompileEveryPass(WithGridIncludes("""
            Shader "Test/Surface Includes"
            {
                Surface "Lit"
                {
                    GLSLPROGRAM
                    #include "Grid"
                    void Surface(inout PBR_Params p, Graphite_SurfaceData i) { p.baseColor = vec3(GridShade(i.vertexColor.rgb)); }
                    ENDGLSL

                    Vertex
                    {
                        GLSLPROGRAM
                        #include "Grid"
                        void SurfaceVertex(inout Graphite_SurfaceVertex v) { v.color.rgb *= GridShade(v.position); }
                        ENDGLSL
                    }
                }
            }
            """));
    }

    [Fact]
    public void AnIncludePastedOnce_CanHoldAWholeSection()
    {
        const string vertex = "Vertex\n{\n    void main() { gl_Position = vec4(0.0); }\n}";
        CompileEveryPass(Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Section Include"
            {
                Pass
                {
                    GLSLPROGRAM
                    #include "Vertex Section"
                    Fragment
                    {
                        layout(location = 0) out vec4 color;
                        void main() { color = vec4(1.0); }
                    }
                    ENDGLSL
                }
            }
            """, name => name == "Vertex Section" ? vertex : null)));
    }

    [Fact]
    public void IncludesThatIncludeEachOther_ExpandEachFileOnce()
    {
        // Y names X with .glsl, so the loop comes back to X under another name
        Dictionary<string, string> files = new() { ["X"] = "#include \"Y\"\nfloat FromX() { return 1.0; }", ["Y"] = "#include \"X.glsl\"\nfloat FromY() { return 2.0; }" };

        string expanded = Shader.ExpandIncludes("#include \"X\"", name => files.GetValueOrDefault(name.Replace(".glsl", "")));

        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(expanded, "FromX"));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(expanded, "FromY"));
    }

    [Fact]
    public void CustomTonemapShaders_CompileInsideTheDisplayEncode()
    {
        string shader = Rendering.PostEffect.TonemapShader("void main() { vec4 c = Graphite_SampleSource(vUV); outColor = vec4(c.rgb / (1.0 + c.rgb), c.a); }");
        ShaderPass pass = FullscreenProgram.Pass(shader, "CustomTonemap");
        List<Keyword> combo = [];
        foreach (VariantSpace axis in Graphics.Compiler.GetAxes(pass))
            combo.Add(new Keyword(axis.Name, axis.Name == "GRAPHITE_NO_SOURCE" ? "false" : axis.Values[0]));
        Graphics.Compiler.CompileProgram(pass, [.. combo]);
    }

    [Fact]
    public void Lit_ExposesPbrFactors()
    {
        // The model importer sets these factors, so every glTF material without textures depends on them
        HashSet<string> names = PropertyNames(Shader.LoadDefault(DefaultShader.Lit));

        Assert.Contains("base_color", names);
        Assert.Contains("base_metalness", names);
        Assert.Contains("specular_roughness", names);
        Assert.Contains("ambient_occlusion_map", names);
        Assert.Contains("normal_scale", names);
        Assert.Contains("emission_color", names);
        Assert.Contains("emission_luminance", names);
        Assert.Contains("alpha_cutoff", names);
        Assert.Contains("geometry_opacity", names);
    }

    [Theory]
    [InlineData(DefaultShader.Lit)]
    [InlineData(DefaultShader.Unlit)]
    public void StandardShaders_TakeTheirSurfaceFromTheMaterial(DefaultShader shader)
    {
        Shader loaded = Shader.LoadDefault(shader);
        Assert.Contains(loaded.Keywords, axis => axis.Name == "_SURFACE");
        Assert.True(loaded.HasPass(PassType.Forward));
        Assert.True(loaded.HasPass(PassType.Prepass));
        Assert.True(loaded.HasPass(PassType.ShadowCaster));
        foreach (ShaderPass pass in Passes(loaded))
            Assert.False(pass.State.Set.HasFlag(PassStateFields.BlendEnabled));
    }

    [Theory]
    [MemberData(nameof(AllDefaultShaders))]
    public void DefaultShader_DeclaresAMenuPath(DefaultShader shader)
    {
        // The material inspector's shader picker builds its menu tree from these declarations
        string? path = Shader.ReadDeclaredPath(Shader.GetDefaultSource(shader));

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.DoesNotContain("//", path);
        Assert.False(path!.StartsWith('/') || path.EndsWith('/'));
    }

    [Fact]
    public void DefaultShaderMenuPaths_AreUnique()
    {
        var byPath = new Dictionary<string, DefaultShader>();

        foreach (DefaultShader s in System.Enum.GetValues<DefaultShader>())
        {
            string path = Shader.ReadDeclaredPath(Shader.GetDefaultSource(s))!;
            Assert.False(byPath.TryGetValue(path, out DefaultShader existing),
                $"'{s}' and '{existing}' both declare the path '{path}'.");
            byPath[path] = s;
        }
    }

    [Theory]
    [InlineData("Shader \"Default/Standard\"\n\nProperties {}", "Default/Standard")]
    [InlineData("\uFEFFShader \"Default/Standard\"", "Default/Standard")]
    [InlineData("// a header comment\n// another\nShader \"A/B C\"", "A/B C")]
    [InlineData("/* block\n comment */\nShader \"X\"", "X")]
    [InlineData("   \n\t Shader   \"Spaced/Out\"", "Spaced/Out")]
    [InlineData("Properties { }", null)]
    [InlineData("", null)]
    [InlineData("Shader Default/Standard", null)]
    public void ReadDeclaredPath_HandlesRealSourceShapes(string source, string? expected)
    {
        Assert.Equal(expected, Shader.ReadDeclaredPath(source));
    }

    private static ShaderPass[] Passes(Shader shader) => shader.GpuShader!.Definition.Passes ?? [];

    // Resolves like the shader importer, as written or with .glsl added
    private static Shader WithGridIncludes(string source)
        => Shader.FromSource(Shader.ExpandIncludes(source, name => name is "Grid" or "Grid.glsl" ? GridInclude : null));

    private static void CompileEveryPass(Shader shader) => Compile(shader, 1);

    private static void CompileEveryVariant(Shader shader) => Compile(shader, int.MaxValue);

    // Every pass with each axis's first valuesPerAxis values, in every combination
    private static void Compile(Shader shader, int valuesPerAxis)
    {
        Assert.Null(shader.Error);
        foreach (ShaderPass pass in Passes(shader))
        {
            List<Keyword[]> combos = [[]];
            foreach (VariantSpace axis in Graphics.Compiler.GetAxes(pass))
                combos = [.. combos.SelectMany(combo => axis.Values.Take(valuesPerAxis).Select(value => (Keyword[])[.. combo, new Keyword(axis.Name, value)]))];
            foreach (Keyword[] combo in combos)
                Graphics.Compiler.CompileProgram(pass, combo);
        }
    }

    private static HashSet<string> PropertyNames(Shader shader)
    {
        HashSet<string> names = [];
        foreach (Prowl.Graphite.Rendering.MaterialProperty p in shader.Properties)
            names.Add(p.Name.Text());
        return names;
    }
}
