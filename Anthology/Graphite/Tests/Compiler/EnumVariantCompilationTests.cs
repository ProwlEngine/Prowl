using System.Linq;

using Prowl.Graphite.Variants;

using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Exercises the enum-typed variant axis path end-to-end. EnumVariants declares an enum axis
// (LightingMode, three cases) and a bool axis (Shadows), yielding a 3 x 2 = 6 permutation space; the
// axis values are derived from the enum's cases via reflection rather than string attributes.
public class EnumVariantCompilationTests
{
    static CompilationResult Compile() =>
        CompilerTestHarness.CompileSharedAll("EnumVariants",
            () => new GLCompiler(), () => new VulkanCompiler(), () => new DXCompiler());


    [Fact]
    public void EnumeratesEnumAndBoolAxes()
    {
        CompilationResult result = Compile();

        Assert.Equal(2, result.VariantSpaces.Length);

        VariantSpace lighting = result.VariantSpaces.Single(s => s.Name == "LightingMode");
        Assert.True(lighting.IsEnum);
        Assert.Equal(["None", "Baked", "Realtime"], lighting.Values.ToArray());

        VariantSpace shadows = result.VariantSpaces.Single(s => s.Name == "Shadows");
        Assert.False(shadows.IsEnum);
        Assert.Equal(["false", "true"], shadows.Values.ToArray());
    }


    [Fact]
    public void ProducesFullCartesianProduct()
    {
        CompilationResult result = Compile();

        Assert.Equal(6, result.CompiledVariants.Length);

        foreach (VariantResult variant in result.CompiledVariants)
            Assert.Equal(3, variant.Backends.Length);
    }


    [Fact]
    public void EnumCases_SpecializeIntoDistinctCode()
    {
        CompilationResult result = Compile();

        // For a fixed Shadows value, the three LightingMode cases must bake different vertex code.
        string[] vertexGlsl = result.CompiledVariants
            .Where(v => v.Variants.Single(k => k.Name == "Shadows").Value == "false")
            .Select(v => v.Backends.First(b => b.Backend == GraphicsBackend.OpenGL).Description)
            .Select(d => CompilerTestHarness.StageText(d, ShaderStages.Vertex))
            .ToArray();

        Assert.Equal(3, vertexGlsl.Length);
        Assert.Equal(3, vertexGlsl.Distinct().Count());
    }
}
