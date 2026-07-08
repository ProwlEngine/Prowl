using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Cross-backend smoke test: a single session with every implemented compiler registered produces one
// tagged ShaderDescription per backend, in registration order. Per-target output is verified in the
// platform-specific suites.
public class CompileForTargetTests
{
    [Fact]
    public void CompilesEveryRegisteredBackend()
    {
        VariantResult variant = CompilerTestHarness.CompileGraphics(
            () => new VulkanCompiler(), () => new DXCompiler());

        GraphicsBackend[] expected = [GraphicsBackend.Vulkan, GraphicsBackend.Direct3D11];

        Assert.Equal(expected.Length, variant.Backends.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], variant.Backends[i].Backend);
            Assert.NotEmpty(variant.Backends[i].Description.Stages);
        }
    }
}
