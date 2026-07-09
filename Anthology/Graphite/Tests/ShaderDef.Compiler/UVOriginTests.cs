using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


// Covers the IsUVOriginTopLeft constant the compiler injects: the extern declared in the always-loaded
// UVOrigin module must resolve for every backend, and the value is hardcoded per backend (top-left for
// both Direct3D11 and Vulkan).
public class UVOriginTests
{
    static ShaderDescription CompileDX() =>
        CompilerTestHarness.CompileShared("UVOriginUsage", () => new DXCompiler()).Backends[0].Description;


    // The extern is unresolved until link, so a backend that fails to inject its UV module would throw
    // or emit nothing. Every registered backend must produce both stages.
    [Fact]
    public void LinksForEveryBackend()
    {
        VariantResult variant = CompilerTestHarness.CompileShared(
            "UVOriginUsage", () => new VulkanCompiler(), () => new DXCompiler());

        Assert.Equal(2, variant.Backends.Length);

        foreach ((ShaderDescription description, GraphicsBackend _) in variant.Backends)
        {
            Assert.NotEmpty(CompilerTestHarness.StageOf(description, ShaderStages.Vertex).ShaderBytes);
            Assert.NotEmpty(CompilerTestHarness.StageOf(description, ShaderStages.Fragment).ShaderBytes);
        }
    }


    // Direct3D11 is top-left (IsUVOriginTopLeft == true), so the passthrough branch survives: the UV is
    // assigned straight through with no flip subtraction.
    [Fact]
    public void Direct3D11_ResolvesTopLeft_PassesVThrough()
    {
        string vertex = CompilerTestHarness.StageText(CompileDX(), ShaderStages.Vertex);

        Assert.Contains("output_0.uv_0 = input_0.uv_1", vertex);
        Assert.DoesNotContain("1.0f -", vertex);
    }
}
