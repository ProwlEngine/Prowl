using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Cross-backend smoke test: a single session with every implemented compiler registered produces one
// tagged ShaderDescription per backend. Per-target output is verified in the platform-specific suites.
public class CompileForTargetTests
{
    [Fact]
    public void CompilesEveryRegisteredBackend()
    {
        CompilerModule[] modules = [new GLCompiler(), new VulkanCompiler(), new DXCompiler()];

        VariantResult variant = CompilerTestHarness.CompileGraphics(modules);

        Assert.Equal(modules.Length, variant.Backends.Length);

        for (int i = 0; i < modules.Length; i++)
        {
            Assert.Equal(modules[i].Backend, variant.Backends[i].Backend);
            Assert.NotEmpty(variant.Backends[i].Description.Stages);
        }
    }
}
