using Xunit;

namespace Prowl.Graphite.Tests;

// MaxSetElements sizes the binder's per-draw scratch arrays and its stackallocs, and every one of
// them is indexed by the layout's element count with no bound. An oversized set therefore has to be
// rejected at program creation rather than silently overrunning them at draw time.
public abstract class ResourceLayoutLimitTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void OversizedSet_IsRejected_ByValidation()
    {
        RenderException ex = Assert.Throws<RenderException>(() => CreateProgramWithElements(ResourceLayoutDescription.MaxElementsPerSet + 1));

        Assert.Contains("65 elements", ex.Message);
        Assert.Contains($"{ResourceLayoutDescription.MaxElementsPerSet} are supported per set", ex.Message);
    }

    // The validation layer is opt-out, so the backend has to refuse the same layout on its own.
    [Fact]
    public void OversizedSet_IsRejected_WithValidationDisabled()
    {
        bool previous = GraphicsDevice.ValidationEnabled;
        GraphicsDevice.ValidationEnabled = false;
        try
        {
            RenderException ex = Assert.Throws<RenderException>(() => CreateProgramWithElements(ResourceLayoutDescription.MaxElementsPerSet + 1));
            Assert.Contains($"{ResourceLayoutDescription.MaxElementsPerSet} are supported per set", ex.Message);
        }
        finally
        {
            GraphicsDevice.ValidationEnabled = previous;
        }
    }

    [Fact]
    public void SetAtTheLimit_IsAccepted()
    {
        GraphicsProgram program = CreateProgramWithElements(ResourceLayoutDescription.MaxElementsPerSet);
        Assert.NotNull(program);
    }

    private GraphicsProgram CreateProgramWithElements(int elementCount)
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "ColoredQuadRenderer.slang");

        ResourceLayoutElementDescription[] elements = new ResourceLayoutElementDescription[elementCount];
        for (int i = 0; i < elementCount; i++)
            elements[i] = new ResourceLayoutElementDescription($"Element{i}", ResourceKind.UniformBuffer, ShaderStages.Vertex, i);

        ShaderDescription desc = new(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.Default,
            ResourceLayouts = [new ResourceLayoutDescription { Set = 0, Elements = elements }],
        };

        return RF.CreateGraphicsProgram(desc);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanResourceLayoutLimitTests : ResourceLayoutLimitTests<VulkanDeviceCreator> { }
#endif
