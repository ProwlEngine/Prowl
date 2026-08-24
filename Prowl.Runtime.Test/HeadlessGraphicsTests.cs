// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests that GPU-backed resources can be created and used without a graphics device (headless /
/// dedicated server). GPU command submission is a no-op when <see cref="Graphics.IsHeadless"/>, so
/// constructing textures, materials, meshes, etc. must not crash - they just don't upload to a GPU.
/// </summary>
public class HeadlessGraphicsTests
{
    [Fact]
    public void Graphics_ReportsHeadless_WhenNoDevice()
    {
        // The test process never initializes a GL device.
        Assert.True(Graphics.IsHeadless);
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
        // Capability constants default to sane minimums so size validation passes pre-device.
        var tex = new Texture2D(4096, 4096);
        Assert.Equal(4096u, tex.Width);
    }

    [Fact]
    public void Material_CreatesHeadless_WithoutThrowing()
    {
        // Material's ctor loads the default shader, which parses a default texture - the exact path
        // that used to crash headless when texture creation hit an uninitialized GL device.
        var mat = new Material();
        // Assert the default-shader load actually completed (that's the path that used to crash
        // headless), not merely that the ctor returned non-null.
        Assert.NotNull(mat.Shader);
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
        // Catches malformed pass or uniform declarations in the built-in shader sources without
        // needing a GL device to compile the GLSL itself.
        var loaded = Shader.LoadDefault(shader);

        Assert.NotNull(loaded);
        Assert.NotEmpty(loaded.Passes);
    }

    public static TheoryData<DefaultShader> StandardFamilyShaders() => new()
    {
        DefaultShader.Standard, DefaultShader.StandardDoubleSided,
        DefaultShader.StandardCutout, DefaultShader.StandardCutoutDoubleSided,
        DefaultShader.StandardTransparent, DefaultShader.StandardTransparentDoubleSided,
        DefaultShader.StandardAnisotropic, DefaultShader.StandardAnisotropicDoubleSided,
    };

    [Theory]
    [MemberData(nameof(StandardFamilyShaders))]
    public void StandardFamily_ExposesPbrFactors(DefaultShader shader)
    {
        // The metallic/roughness/occlusion/normal factors were set by the model importer for a long
        // time while no shader declared them, so every factor-only glTF material rendered wrong.
        var loaded = Shader.LoadDefault(shader);
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (var p in loaded.Properties)
            names.Add(p.Name);

        Assert.Contains("_Metallic", names);
        Assert.Contains("_Roughness", names);
        Assert.Contains("_OcclusionTex", names);
        Assert.Contains("_OcclusionStrength", names);
        Assert.Contains("_NormalScale", names);
        Assert.Contains("_EmissiveColor", names);
    }

    [Theory]
    [InlineData(DefaultShader.Standard, false)]
    [InlineData(DefaultShader.StandardCutout, true)]
    [InlineData(DefaultShader.StandardTransparent, false)]
    [InlineData(DefaultShader.Unlit, false)]
    [InlineData(DefaultShader.UnlitCutout, true)]
    public void AlphaCutoff_OnlyExistsOnCutoutShaders(DefaultShader shader, bool expectCutoff)
    {
        var loaded = Shader.LoadDefault(shader);
        bool has = false;
        foreach (var p in loaded.Properties)
            if (p.Name == "_AlphaCutoff") has = true;

        Assert.Equal(expectCutoff, has);
    }

    [Theory]
    [InlineData(DefaultShader.Standard, RasterizerState.PolyFace.Back)]
    [InlineData(DefaultShader.StandardDoubleSided, RasterizerState.PolyFace.None)]
    [InlineData(DefaultShader.StandardCutoutDoubleSided, RasterizerState.PolyFace.None)]
    [InlineData(DefaultShader.StandardTransparentDoubleSided, RasterizerState.PolyFace.None)]
    [InlineData(DefaultShader.UnlitDoubleSided, RasterizerState.PolyFace.None)]
    public void DoubleSidedShaders_DisableCulling(DefaultShader shader, RasterizerState.PolyFace expected)
    {
        var loaded = Shader.LoadDefault(shader);
        foreach (var pass in loaded.Passes)
            Assert.Equal(expected, pass.State.CullFace);
    }

    [Theory]
    [InlineData(DefaultShader.StandardTransparent)]
    [InlineData(DefaultShader.StandardTransparentDoubleSided)]
    [InlineData(DefaultShader.UnlitTransparent)]
    public void TransparentShaders_BlendAndSkipDepthWrite(DefaultShader shader)
    {
        var loaded = Shader.LoadDefault(shader);
        var pass = Assert.Single(loaded.Passes);
        Assert.True(pass.State.DoBlend);
        Assert.False(pass.State.DepthWrite);
        Assert.True(pass.HasTag("RenderOrder", "Transparent"));
    }

    [Theory]
    [InlineData(DefaultShader.Standard)]
    [InlineData(DefaultShader.StandardCutout)]
    [InlineData(DefaultShader.Unlit)]
    [InlineData(DefaultShader.UnlitCutout)]
    public void OpaqueShaders_HavePrepassAndShadowCaster(DefaultShader shader)
    {
        var loaded = Shader.LoadDefault(shader);
        Assert.NotEmpty(loaded.GetPassesWithTag("LightMode", "Prepass"));
        Assert.NotEmpty(loaded.GetPassesWithTag("LightMode", "ShadowCaster"));
    }

    [Theory]
    [MemberData(nameof(AllDefaultShaders))]
    public void DefaultShader_DeclaresAMenuPath(DefaultShader shader)
    {
        // The material inspector's shader picker builds its menu tree from these declarations, so a
        // shader without one would be unreachable there.
        string? path = Shader.ReadDeclaredPath(Shader.GetDefaultSource(shader));

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.DoesNotContain("//", path);
        Assert.False(path!.StartsWith('/') || path.EndsWith('/'));
    }

    [Fact]
    public void DefaultShaderMenuPaths_AreUnique()
    {
        // Two shaders sharing a path collapse into one entry in the picker, which is how Blit spent
        // a long while declaring itself as "Default/Gizmos".
        var byPath = new System.Collections.Generic.Dictionary<string, DefaultShader>();

        foreach (DefaultShader s in System.Enum.GetValues<DefaultShader>())
        {
            string path = Shader.ReadDeclaredPath(Shader.GetDefaultSource(s))!;
            Assert.False(byPath.TryGetValue(path, out var existing),
                $"'{s}' and '{existing}' both declare the path '{path}'.");
            byPath[path] = s;
        }
    }

    [Theory]
    [InlineData("Shader \"Default/Standard\"\n\nProperties {}", "Default/Standard")]
    [InlineData("﻿Shader \"Default/Standard\"", "Default/Standard")]
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
        // Upload encodes GPU buffer-creation command buffers; headless drops them on the floor.
        mesh.Upload();
        Assert.True(mesh.VertexCount > 0);
    }
}
