using System;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

[Collection("GPU Tests")]
public sealed class FoliageTests : IDisposable
{
    private const string Source = """
        Shader "Test/Leaf"
        {
            Surface "Foliage"
            {
                GLSLPROGRAM
                void Surface(inout PBR_Params p, Graphite_SurfaceData i) { }
                ENDGLSL

                Vertex
                {
                    GLSLPROGRAM
                    void SurfaceVertex(inout Graphite_SurfaceVertex v)
                    {
                        v.position += Graphite_WindSway(Graphite_ObjectToRelative(v.position), v.time, 0.0);
                    }
                    ENDGLSL
                }
            }
        }
        """;

    private readonly RendererHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void FoliageModel_Compiles()
    {
        Shader shader = _harness.Renderer.LoadShader(ShaderParser.Parse(Source));
        foreach (ShaderPass pass in shader.Definition.Passes!)
        {
            foreach (string tier in (string[])["FULL", "LITE"])
                TestCompiler.Shared.CompileProgram(pass, [new Keyword(GlobalAxis.PbrTier.Name, tier)]);
        }
    }

    [Fact]
    public void LeavesShader_Compiles()
    {
        ShaderLibrary.Register(typeof(Renderer).Assembly);
        Shader shader = _harness.Renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Foliage/Leaves.shader")!));
        foreach (ShaderPass pass in shader.Definition.Passes!)
        {
            foreach (string tier in (string[])["FULL", "LITE"])
                TestCompiler.Shared.CompileProgram(pass, [new Keyword(GlobalAxis.PbrTier.Name, tier)]);
        }
    }

    // A leaf facing the camera with the sun behind it is dark without translucency and glows with it
    [Fact]
    public void Translucency_LetsLightThroughFromBehind()
    {
        Shader shader = _harness.Renderer.LoadShader(ShaderParser.Parse(Source));
        Material leaf = _harness.Renderer.CreateMaterial(shader);
        _harness.Owned.Add(leaf);
        leaf.SetFloat("foliage_normal_lean", 0f);
        _harness.World.Add(new RenderableDesc { Mesh = _harness.CreateQuad(normals: true), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 5)) }, [leaf]);
        _harness.World.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(0, 0, -1), Color = new Float3(1, 1, 1), Intensity = 10f });

        RenderView view = _harness.View();
        float Brightness()
        {
            float sum = 0f;
            Float4[] pixels = _harness.Frame(view);
            foreach (Float4 p in pixels)
                sum += p.X + p.Y + p.Z;
            return sum / pixels.Length;
        }

        leaf.SetFloat("translucency", 0f);
        float dark = Brightness();
        leaf.SetFloat("translucency", 1f);
        float glowing = Brightness();
        Assert.True(glowing > dark + 0.1f, $"glowing {glowing} dark {dark}");
    }
}
