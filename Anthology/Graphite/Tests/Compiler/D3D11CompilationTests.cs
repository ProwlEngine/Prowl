using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Compiles the shared graphics shader through the HLSL target and checks the HLSL output, entry point
// names, and vertex input semantics against checked-in known-good values. D3D11 binds vertex inputs
// by semantic name rather than location, so the inputs are looked up by semantic here.
public class D3D11CompilationTests
{
    static ShaderDescription Compile() =>
        CompilerTestHarness.CompileGraphics(() => new DXCompiler()).Backends[0].Description;


    [Fact]
    public void Stages_HaveExpectedEntryPoints()
    {
        ShaderDescription d = Compile();

        Assert.Equal(2, d.Stages.Length);
        Assert.Equal("vertex", CompilerTestHarness.StageOf(d, ShaderStages.Vertex).EntryPoint);
        Assert.Equal("fragment", CompilerTestHarness.StageOf(d, ShaderStages.Fragment).EntryPoint);
    }


    [Theory]
    // Blended user-facing name, raw D3D11 semantic, format. All three inputs are semantic index 0,
    // so the location (which D3D11 uses as the semantic index) is 0 for each.
    [InlineData("POSITION0", "POSITION", VertexElementFormat.Float3)]
    [InlineData("UV0", "UV", VertexElementFormat.Float2)]
    [InlineData("COLOR0", "COLOR", VertexElementFormat.Float4)]
    public void VertexInputs_BlendedNameRawSemanticAndLocationIndex(string blended, string raw, VertexElementFormat format)
    {
        ShaderDescription d = Compile();

        VertexLayoutDescription layout = CompilerTestHarness.LayoutWithName(d, blended);
        VertexElementDescription element = CompilerTestHarness.Single(layout);

        Assert.Equal(format, element.Format);
        Assert.Equal(raw, element.D3D11SemanticName);
        Assert.Equal(0u, layout.Location); // semantic index, carried in the location for D3D11
    }


    [Fact]
    public void IndexedSemantics_BlendDistinctlyButShareRawSemantic()
    {
        // UV0 and UV3 share the raw semantic "UV" but differ by index. The blended names keep them
        // distinct for user lookup, the raw semantic collapses to "UV", and the index rides the location.
        const string source = """
            module MultiUV;
            struct VIn { float2 a : UV0; float2 b : UV3; }
            [shader("vertex")] float4 vertex(VIn i) : SV_Position { return float4(i.a, i.b); }
            [shader("fragment")] float4 fragment() : SV_Target { return 0; }
            """;

        ShaderDescription d = CompilerTestHarness
            .Compile(source, "MultiUV", () => new DXCompiler())
            .CompiledVariants[0].Backends[0].Description;

        VertexLayoutDescription uv0 = CompilerTestHarness.LayoutWithName(d, "UV0");
        VertexLayoutDescription uv3 = CompilerTestHarness.LayoutWithName(d, "UV3");

        Assert.Equal("UV", CompilerTestHarness.Single(uv0).D3D11SemanticName);
        Assert.Equal("UV", CompilerTestHarness.Single(uv3).D3D11SemanticName);
        Assert.Equal(0u, uv0.Location);
        Assert.Equal(3u, uv3.Location);
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
