using System;
using System.Linq;

using Prowl.Graphite.ShaderDef;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


// Exercises variant specialization end-to-end. The shared Variants shader declares a single boolean
// variant space (DoubleColor) consumed by the vertex stage, yielding two compiled permutations.
public class VariantCompilationTests
{
    static CompilationResult Compile() =>
        CompilerTestHarness.CompileSharedAll("Variants",
            () => new VulkanCompiler());


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

            Assert.Single(variant.Backends);
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
        byte[][] vertexSpirv = result.CompiledVariants
            .Select(v => v.Backends.First(b => b.Backend == GraphicsBackend.Vulkan).Description)
            .Select(d => CompilerTestHarness.StageOf(d, ShaderStages.Vertex).ShaderBytes)
            .ToArray();

        Assert.NotEqual(Convert.ToBase64String(vertexSpirv[0]), Convert.ToBase64String(vertexSpirv[1]));
    }
}
