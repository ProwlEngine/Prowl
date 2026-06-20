using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Compiles the shared shader suite through the SPIR-V target and checks the binary output, entry
// points, vertex locations, and resource reflection. Slang names every SPIR-V entry point "main", so
// VulkanCompiler reports "main" for each stage. Expected resource layouts are authored statically and
// compared by the shared ReflectionTestbed.
public class VulkanCompilationTests
{
    const ShaderStages VF = ShaderStages.Vertex | ShaderStages.Fragment;

    static ShaderDescription Compile(string module) =>
        CompilerTestHarness.CompileShared(module, () => new VulkanCompiler()).Backends[0].Description;


    static ResourceLayoutElementDescription Ubo(string name, int binding, params UniformBlockField[] fields)
        => new(name, ResourceKind.UniformBuffer, VF, binding, ResourceLayoutElementOptions.None, name, fields);

    static ResourceLayoutElementDescription Tex(string name, int binding)
        => new(name, ResourceKind.TextureReadOnly, VF, binding, ResourceLayoutElementOptions.None, name, []);

    static ResourceLayoutElementDescription Samp(string name, int binding)
        => new(name, ResourceKind.Sampler, VF, binding, ResourceLayoutElementOptions.None, name, []);


    [Fact]
    public void Graphics_StageEntryPointsAreMain()
    {
        ReflectionTestbed.AssertStages(Compile("Graphics"),
            (ShaderStages.Vertex, "main"), (ShaderStages.Fragment, "main"));
    }


    [Fact]
    public void Graphics_VertexInputsAtExpectedLocations()
    {
        ReflectionTestbed.AssertVertexLocations(Compile("Graphics"),
            (0, VertexElementFormat.Float3), (1, VertexElementFormat.Float2), (2, VertexElementFormat.Float4));
    }


    [Theory]
    [InlineData("Graphics")]
    [InlineData("Modules")]
    [InlineData("ConstantBuffers")]
    [InlineData("ParameterBlocks")]
    [InlineData("UVOriginUsage")]
    public void Spirv_MatchesKnownGood(string module)
    {
        ShaderDescription d = Compile(module);

        Assert.Equal(
            CompilerTestHarness.KnownGoodBytes($"{module}.vertex.spv"),
            CompilerTestHarness.StageOf(d, ShaderStages.Vertex).ShaderBytes);

        Assert.Equal(
            CompilerTestHarness.KnownGoodBytes($"{module}.fragment.spv"),
            CompilerTestHarness.StageOf(d, ShaderStages.Fragment).ShaderBytes);
    }


    [Theory]
    [InlineData("Graphics")]
    [InlineData("Modules")]
    [InlineData("ConstantBuffers")]
    [InlineData("ParameterBlocks")]
    public void Spirv_IsValid(string module)
    {
        ShaderDescription d = Compile(module);

        foreach (ShaderStages stage in new[] { ShaderStages.Vertex, ShaderStages.Fragment })
        {
            byte[] spirv = CompilerTestHarness.StageOf(d, stage).ShaderBytes;
            string? validation = CompilerTestHarness.TryValidateSpirv(spirv);

            // null => spirv-val unavailable; the known-good comparison still covers correctness.
            if (validation != null)
                Assert.True(validation.Length == 0, $"spirv-val rejected {module} {stage}:\n{validation}");
        }
    }


    [Fact]
    public void ConstantBuffers_ReflectBlocksAndFields()
    {
        ReflectionTestbed.AssertResourceLayouts(Compile("ConstantBuffers"),
            new ResourceLayoutDescription(0,
                Ubo("camera", 0,
                    new UniformBlockField("viewProj", 0, 64, UniformScalarType.Float4x4),
                    new UniformBlockField("cameraPos", 64, 12, UniformScalarType.Float3),
                    new UniformBlockField("time", 76, 4, UniformScalarType.Float1)),
                Ubo("material", 1,
                    new UniformBlockField("baseColor", 0, 16, UniformScalarType.Float4),
                    new UniformBlockField("tiling", 16, 8, UniformScalarType.Float2),
                    new UniformBlockField("flags", 24, 4, UniformScalarType.Int1))));
    }


    // `lighting` comes from the imported Common module; it must still bind in set 0 after `globals`.
    [Fact]
    public void Modules_SurfaceImportedModuleBlock()
    {
        ReflectionTestbed.AssertResourceLayouts(Compile("Modules"),
            new ResourceLayoutDescription(0,
                Ubo("globals", 0,
                    new UniformBlockField("viewProj", 0, 64, UniformScalarType.Float4x4),
                    new UniformBlockField("tint", 64, 16, UniformScalarType.Float4)),
                Ubo("lighting", 1,
                    new UniformBlockField("sunDirection", 0, 16, UniformScalarType.Float4),
                    new UniformBlockField("sunColor", 16, 16, UniformScalarType.Float4),
                    new UniformBlockField("ambientIntensity", 32, 4, UniformScalarType.Float1))));
    }


    // Top-level resources in set 0; each ParameterBlock opens its own descriptor set. perObject holds
    // uniform data (an implicit uniform buffer at binding 0) plus resources; onlyTex is resource-only,
    // so it has no uniform buffer and binds its resources from 0.
    [Fact]
    public void ParameterBlocks_OpenDescriptorSets()
    {
        ReflectionTestbed.AssertResourceLayouts(Compile("ParameterBlocks"),
            new ResourceLayoutDescription(0,
                Ubo("globals", 0,
                    new UniformBlockField("viewProj", 0, 64, UniformScalarType.Float4x4),
                    new UniformBlockField("tint", 64, 16, UniformScalarType.Float4)),
                Tex("albedo", 1),
                Samp("samp", 2)),
            new ResourceLayoutDescription(1,
                Ubo("perObject", 0,
                    new UniformBlockField("color", 0, 16, UniformScalarType.Float4),
                    new UniformBlockField("uvOffset", 16, 8, UniformScalarType.Float2)),
                Tex("detail", 1),
                Samp("detailSamp", 2)),
            new ResourceLayoutDescription(2,
                Tex("tex", 0),
                Samp("s", 1)));
    }
}
