using System.Linq;

using Prowl.Graphite.Shaders;

using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Exercises variant specialization end-to-end. A single boolean variant space yields two compiled
// permutations; the test is platform-agnostic and registers all three backends to show the variant
// enumeration is independent of the targets.
public class VariantCompilationTests
{
    // One variant space (DoubleColor) consumed by the vertex stage so the two permutations differ.
    const string VariantSource = """
        import VariantAttributes;

        module VariantShader;

        [variant("false") variant("true")]
        extern static const bool DoubleColor;

        struct VertexInput
        {
            float3 position : POSITION;
            float4 color    : COLOR;
        }

        struct VertexOutput
        {
            float4 clipPosition : SV_Position;
            float4 color : COLOR;
        }

        [shader("vertex")]
        VertexOutput vertex(VertexInput input)
        {
            VertexOutput output;
            output.clipPosition = float4(input.position, 1);
            output.color = DoubleColor ? input.color * 2.0 : input.color;
            return output;
        }

        [shader("fragment")]
        float4 fragment(VertexOutput input) : SV_Target
        {
            return input.color;
        }
        """;


    static CompilationResult Compile() =>
        CompilerTestHarness.Compile(VariantSource, "VariantShader",
            () => new GLCompiler(), () => new VulkanCompiler(), () => new DXCompiler());


    [Fact]
    public void EnumeratesVariantSpace()
    {
        CompilationResult result = Compile();

        VariantSpace space = Assert.Single(result.VariantSpaces);
        Assert.Equal("DoubleColor", space.Name);
        Assert.Equal(2, space.Values.Count);
    }


    [Fact]
    public void ProducesOnePermutationPerValue_ForEveryBackend()
    {
        CompilationResult result = Compile();

        Assert.Equal(2, result.CompiledVariants.Length);

        foreach (VariantResult variant in result.CompiledVariants)
        {
            Keyword keyword = Assert.Single(variant.Variants);
            Assert.Equal("DoubleColor", keyword.Name);

            // Each permutation is compiled for all three registered backends.
            Assert.Equal(3, variant.Backends.Length);
        }

        string[] values = result.CompiledVariants
            .Select(v => v.Variants.Single().Value)
            .OrderBy(v => v)
            .ToArray();

        Assert.Equal(["false", "true"], values);
    }


    [Fact]
    public void DifferentVariants_ProduceDifferentCode()
    {
        CompilationResult result = Compile();

        // Specialization should bake the chosen DoubleColor value into each permutation's code.
        string[] vertexGlsl = result.CompiledVariants
            .Select(v => v.Backends.First(b => b.Backend == GraphicsBackend.OpenGL).Description)
            .Select(d => CompilerTestHarness.StageText(d, ShaderStages.Vertex))
            .ToArray();

        Assert.NotEqual(vertexGlsl[0], vertexGlsl[1]);
    }
}
