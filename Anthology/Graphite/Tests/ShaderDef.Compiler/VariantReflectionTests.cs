using System.Linq;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


public class VariantReflectionTests
{
    const ShaderStages VF = ShaderStages.Vertex | ShaderStages.Fragment;

    static CompilationResult Compile() =>
        CompilerTestHarness.CompileSharedAll("VariantsWithResources",
            () => new VulkanCompiler());


    [Fact]
    public void EveryVariant_ReflectsExpectedStagesAndVertexLayout()
    {
        CompilationResult result = Compile();

        Assert.Equal(2, result.CompiledVariants.Length);

        foreach (VariantResult variant in result.CompiledVariants)
        {
            ShaderDescription description = variant.Backends.Single().Description;

            ReflectionTestbed.AssertStages(description,
                (ShaderStages.Vertex, "main"), (ShaderStages.Fragment, "main"));

            ReflectionTestbed.AssertVertexLocations(description,
                (0, VertexElementFormat.Float3), (1, VertexElementFormat.Float2));
        }
    }


    [Fact]
    public void EveryVariant_ReflectsTheSameResourceLayout()
    {
        CompilationResult result = Compile();

        foreach (VariantResult variant in result.CompiledVariants)
        {
            ShaderDescription description = variant.Backends.Single().Description;

            ReflectionTestbed.AssertResourceLayouts(description,
                new ResourceLayoutDescription(0,
                    new ResourceLayoutElementDescription("Material", ResourceKind.UniformBuffer, VF, 0,
                        ResourceLayoutElementOptions.None,
                        [new UniformBlockField("tint", 0, 16, UniformScalarType.Float4)]),
                    new ResourceLayoutElementDescription("albedo", ResourceKind.TextureReadOnly, VF, 1,
                        ResourceLayoutElementOptions.None, []),
                    new ResourceLayoutElementDescription("samp", ResourceKind.Sampler, VF, 2,
                        ResourceLayoutElementOptions.None, [])));
        }
    }


    [Fact]
    public void EveryVariant_ProducesValidSpirv()
    {
        CompilationResult result = Compile();

        foreach (VariantResult variant in result.CompiledVariants)
        {
            ShaderDescription description = variant.Backends.Single().Description;

            foreach (ShaderStages stage in new[] { ShaderStages.Vertex, ShaderStages.Fragment })
            {
                byte[] spirv = CompilerTestHarness.StageOf(description, stage).ShaderBytes;
                string validation = CompilerTestHarness.TryValidateSpirv(spirv);

                if (validation != null)
                    Assert.True(validation.Length == 0, $"spirv-val rejected variant {variant.Variants.Single().Value} {stage}:\n{validation}");
            }
        }
    }
}
