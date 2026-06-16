using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Compiles the shared graphics shader through the SPIR-V target and checks the binary output, entry
// point names, and vertex input locations against checked-in known-good values.
//
// Slang names every SPIR-V entry point "main", so VulkanCompiler overrides the reported entry point
// to "main"; without that the Vulkan backend cannot resolve the stage in the module.
public class VulkanCompilationTests
{
    static ShaderDescription Compile() =>
        CompilerTestHarness.CompileGraphics(() => new VulkanCompiler()).Backends[0].Description;


    [Fact]
    public void Stages_EntryPointsAreMain()
    {
        ShaderDescription d = Compile();

        Assert.Equal(2, d.Stages.Length);
        Assert.Equal("main", CompilerTestHarness.StageOf(d, ShaderStages.Vertex).EntryPoint);
        Assert.Equal("main", CompilerTestHarness.StageOf(d, ShaderStages.Fragment).EntryPoint);
    }


    [Fact]
    public void VertexInputs_AtExpectedLocationsWithFormats()
    {
        ShaderDescription d = Compile();

        Assert.Equal(VertexElementFormat.Float3, CompilerTestHarness.ElementAtLocation(d, 0).Format);
        Assert.Equal(VertexElementFormat.Float2, CompilerTestHarness.ElementAtLocation(d, 1).Format);
        Assert.Equal(VertexElementFormat.Float4, CompilerTestHarness.ElementAtLocation(d, 2).Format);
    }


    [Fact]
    public void Spirv_IsValid()
    {
        ShaderDescription d = Compile();

        foreach (ShaderStages stage in new[] { ShaderStages.Vertex, ShaderStages.Fragment })
        {
            byte[] spirv = CompilerTestHarness.StageOf(d, stage).ShaderBytes;
            string? validation = CompilerTestHarness.TryValidateSpirv(spirv);

            // null => spirv-val unavailable; the known-good comparison still covers correctness.
            if (validation != null)
                Assert.True(validation.Length == 0, $"spirv-val rejected {stage}:\n{validation}");
        }
    }


    [Fact]
    public void Spirv_MatchesKnownGood()
    {
        ShaderDescription d = Compile();

        Assert.Equal(
            CompilerTestHarness.KnownGoodBytes("vk_vertex.spv"),
            CompilerTestHarness.StageOf(d, ShaderStages.Vertex).ShaderBytes);

        Assert.Equal(
            CompilerTestHarness.KnownGoodBytes("vk_fragment.spv"),
            CompilerTestHarness.StageOf(d, ShaderStages.Fragment).ShaderBytes);
    }
}
