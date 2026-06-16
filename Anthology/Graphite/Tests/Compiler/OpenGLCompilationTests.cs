using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Compiles the shared graphics shader through the GL target and checks the GLSL output, entry point
// names, and vertex input locations against checked-in known-good values.
public class OpenGLCompilationTests
{
    static ShaderDescription Compile() =>
        CompilerTestHarness.CompileGraphics(() => new GLCompiler()).Backends[0].Description;


    [Fact]
    public void Stages_HaveExpectedEntryPoints()
    {
        ShaderDescription d = Compile();

        Assert.Equal(2, d.Stages.Length);
        Assert.Equal("vertex", CompilerTestHarness.StageOf(d, ShaderStages.Vertex).EntryPoint);
        Assert.Equal("fragment", CompilerTestHarness.StageOf(d, ShaderStages.Fragment).EntryPoint);
    }


    [Fact]
    public void VertexInputs_AtExpectedLocationsWithFormats()
    {
        ShaderDescription d = Compile();

        VertexElementDescription position = CompilerTestHarness.ElementAtLocation(d, 0);
        VertexElementDescription uv = CompilerTestHarness.ElementAtLocation(d, 1);
        VertexElementDescription color = CompilerTestHarness.ElementAtLocation(d, 2);

        Assert.Equal(VertexElementFormat.Float3, position.Format);
        Assert.Equal(VertexElementFormat.Float2, uv.Format);
        Assert.Equal(VertexElementFormat.Float4, color.Format);
    }


    [Fact]
    public void Glsl_MatchesKnownGood()
    {
        ShaderDescription d = Compile();

        Assert.Equal(
            CompilerTestHarness.KnownGoodText("gl_vertex.glsl"),
            CompilerTestHarness.StageText(d, ShaderStages.Vertex));

        Assert.Equal(
            CompilerTestHarness.KnownGoodText("gl_fragment.glsl"),
            CompilerTestHarness.StageText(d, ShaderStages.Fragment));
    }
}
