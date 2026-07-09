using System.Linq;

using Prowl.Graphite.ShaderDef;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


// Exercises variant specialization end-to-end. The shared Variants shader declares a single boolean
// variant space (DoubleColor) consumed by the vertex stage, yielding two compiled permutations; the
// test is platform-agnostic and registers both backends to show the variant enumeration is
// independent of the targets.
public class VariantCompilationTests
{
    static CompilationResult Compile() =>
        CompilerTestHarness.CompileSharedAll("Variants",
            () => new VulkanCompiler(), () => new DXCompiler());


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

            // Each permutation is compiled for both registered backends.
            Assert.Equal(2, variant.Backends.Length);
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
        string[] vertexHlsl = result.CompiledVariants
            .Select(v => v.Backends.First(b => b.Backend == GraphicsBackend.Direct3D11).Description)
            .Select(d => CompilerTestHarness.StageText(d, ShaderStages.Vertex))
            .ToArray();

        Assert.NotEqual(vertexHlsl[0], vertexHlsl[1]);
    }
}
