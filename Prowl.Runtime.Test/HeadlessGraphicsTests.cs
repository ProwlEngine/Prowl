// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

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
    public void Graphics_ReportsHeadless_WhenNoDevice()
    {
        Assert.True(Graphics.IsHeadless);
        Assert.Null(Graphics.Device);
        Assert.Null(Graphics.Renderer);
    }

    [Fact]
    public void Texture2D_CreatesHeadless_WithoutThrowing()
    {
        var tex = new Texture2D(64, 64);
        Assert.Equal(64u, tex.Width);
        Assert.Equal(64u, tex.Height);
    }

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
        foreach (ShaderPass pass in Passes(Shader.LoadDefault(shader)))
        {
            List<Keyword> combo = [];
            foreach (VariantSpace axis in Graphics.Compiler.GetAxes(pass))
                combo.Add(new Keyword(axis.Name, axis.Values[0]));
            Graphics.Compiler.CompileProgram(pass, [.. combo]);
        }
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

    [Fact]
    public void Mesh_UploadHeadless_DoesNotThrow()
    {
        var mesh = Mesh.CreateCube(Vector.Float3.One);
        mesh.Upload();
        Assert.True(mesh.VertexCount > 0);
    }

    private static ShaderPass[] Passes(Shader shader) => shader.GpuShader!.Definition.Passes ?? [];

    private static HashSet<string> PropertyNames(Shader shader)
    {
        HashSet<string> names = [];
        foreach (Prowl.Graphite.Rendering.MaterialProperty p in shader.Properties)
            names.Add(p.Name.Text());
        return names;
    }
}
