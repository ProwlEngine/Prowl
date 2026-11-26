using System;
using System.Runtime.InteropServices;

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

    // Every vertex bends fully, so the whole quad moves with the wind
    private const string Swaying = """
        Shader "Test/Swaying"
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
                        v.position += Graphite_WindSway(Graphite_ObjectToRelative(v.position), v.time, 1.0);
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
            foreach (string tier in (string[])["FULL", "LITE", "MAX"])
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
            foreach (string tier in (string[])["FULL", "LITE", "MAX"])
                TestCompiler.Shared.CompileProgram(pass, [new Keyword(GlobalAxis.PbrTier.Name, tier)]);
        }
    }
}
