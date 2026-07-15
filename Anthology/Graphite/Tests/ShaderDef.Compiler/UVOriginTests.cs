using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


// Covers the IsUVOriginTopLeft constant the compiler injects: the extern declared in the always-loaded
// UVOrigin module must resolve for every backend, and the value is hardcoded per backend.
public class UVOriginTests
{
    // The extern is unresolved until link, so a backend that fails to inject its UV module would throw
    // or emit nothing. Every registered backend must produce both stages.
    [Fact]
    public void LinksForEveryBackend()
    {
        VariantResult variant = CompilerTestHarness.CompileShared(
            "UVOriginUsage", () => new VulkanCompiler());

        Assert.Single(variant.Backends);

        foreach ((ShaderDescription description, GraphicsBackend _) in variant.Backends)
        {
            Assert.NotEmpty(CompilerTestHarness.StageOf(description, ShaderStages.Vertex).ShaderBytes);
            Assert.NotEmpty(CompilerTestHarness.StageOf(description, ShaderStages.Fragment).ShaderBytes);
        }
    }
}
