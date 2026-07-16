using System.Linq;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


// VariantCompilationTests and EnumVariantCompilationTests prove every variant produces valid SPIR-V,
// but their shared shaders declare no resources, so they never prove reflection (vertex layouts,
// resource layouts) stays correct once a real resource-bound shader is specialized. This suite closes
// that gap: VariantsWithResources declares a variant axis alongside a ParameterBlock, so every
// permutation's ShaderDescription can be checked for the same reflection a real application would bind
// against, not just non-empty bytes.
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
                        ResourceLayoutElementOptions.None, "Material",
                        [new UniformBlockField("tint", 0, 16, UniformScalarType.Float4)]),
                    new ResourceLayoutElementDescription("albedo", ResourceKind.TextureReadOnly, VF, 1,
                        ResourceLayoutElementOptions.None, "albedo", []),
                    new ResourceLayoutElementDescription("samp", ResourceKind.Sampler, VF, 2,
                        ResourceLayoutElementOptions.None, "samp", [])));
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
