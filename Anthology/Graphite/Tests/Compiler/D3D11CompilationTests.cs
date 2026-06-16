using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Compiles the shared graphics shader through the HLSL target and checks the HLSL output, entry point
// names, and vertex input semantics against checked-in known-good values. D3D11 binds vertex inputs
// by semantic name rather than location, so the inputs are looked up by semantic here.
public class D3D11CompilationTests
{
    static ShaderDescription Compile() =>
        CompilerTestHarness.CompileGraphics(new DXCompiler()).Backends[0].Description;


    [Fact]
    public void Stages_HaveExpectedEntryPoints()
    {
        ShaderDescription d = Compile();

        Assert.Equal(2, d.Stages.Length);
        Assert.Equal("vertex", CompilerTestHarness.StageOf(d, ShaderStages.Vertex).EntryPoint);
        Assert.Equal("fragment", CompilerTestHarness.StageOf(d, ShaderStages.Fragment).EntryPoint);
    }


    [Fact]
    public void VertexInputs_HaveExpectedSemanticsAndFormats()
    {
        ShaderDescription d = Compile();

        Assert.Equal(VertexElementFormat.Float3, CompilerTestHarness.ElementWithSemantic(d, "POSITION").Format);
        Assert.Equal(VertexElementFormat.Float2, CompilerTestHarness.ElementWithSemantic(d, "UV").Format);
        Assert.Equal(VertexElementFormat.Float4, CompilerTestHarness.ElementWithSemantic(d, "COLOR").Format);
    }


    [Fact]
    public void Hlsl_MatchesKnownGood()
    {
        ShaderDescription d = Compile();

        Assert.Equal(
            CompilerTestHarness.KnownGoodText("dx_vertex.hlsl"),
            CompilerTestHarness.StageText(d, ShaderStages.Vertex));

        Assert.Equal(
            CompilerTestHarness.KnownGoodText("dx_fragment.hlsl"),
            CompilerTestHarness.StageText(d, ShaderStages.Fragment));
    }
}
