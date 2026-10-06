#nullable enable

using System;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct BoundStateView : IRenderView
{
    public BoundStateView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}

file sealed class LambdaRasterPass : RasterPass
{
    private readonly RenderResourceID _id;
    private readonly Action<CommandBuffer> _render;

    public LambdaRasterPass(RenderResourceID id, Action<CommandBuffer> render)
    {
        _id = id;
        _render = render;
    }

    public override string Name => "BoundStateRaster";

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, _id, GraphTextureDesc.ViewSized(PixelFormat.R32_G32_B32_A32_Float), ops: TargetLoadStoreOps.Clear(new Color(0, 0, 0, 1)));

    public override void Render(RenderContext context, CommandBuffer cmd) => _render(cmd);
}

file sealed class LambdaComputePass : IPass
{
    private readonly Action<CommandBuffer> _render;

    public LambdaComputePass(Action<CommandBuffer> render) => _render = render;

    public string Name => "BoundStateCompute";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext context, CommandBuffer cmd) => _render(cmd);
}

public abstract class BoundStateCheckTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private GraphicsProgram CreateProgram()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "VertexLayoutTestShader.slang");
        ShaderDescription description = new(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            VertexLayouts =
            [
                new VertexLayoutDescription(0, 52,
                    new VertexElementDescription("POSITION", VertexElementFormat.Float3),
                    new VertexElementDescription("COLOR0", VertexElementFormat.Float4),
                    new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2),
                    new VertexElementDescription("COLOR1", VertexElementFormat.Float4))
            ],
        };
        return RF.CreateGraphicsProgram(description);
    }

    private void AssertThrowsWithValidationDisabled(IPass pass, string expected)
    {
        bool previous = GD.ValidationEnabled;
        GD.ValidationEnabled = false;
        try
        {
            using RenderPipeline pipeline = new([pass]);
            RenderException ex = Assert.Throws<RenderException>(() => GD.DispatchGraph(pipeline, new BoundStateView[] { new(4, 4) }));
            Assert.Contains(expected, ex.Message);
        }
        finally
        {
            GD.ValidationEnabled = previous;
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void Draw_WithoutShader_Throws_WithValidationDisabled()
    {
        RenderResourceID id = RenderResourceID.Intern("bound_state_no_shader");
        AssertThrowsWithValidationDisabled(new LambdaRasterPass(id, cmd => cmd.Draw(3)), "GraphicsProgram must be set");
    }

    [Fact]
    public void Draw_WithoutVertexSource_Throws_WithValidationDisabled()
    {
        GraphicsProgram program = CreateProgram();
        RenderResourceID id = RenderResourceID.Intern("bound_state_no_vertex_source");
        AssertThrowsWithValidationDisabled(new LambdaRasterPass(id, cmd =>
        {
            cmd.SetShader(program);
            cmd.Draw(3);
        }), "IVertexSource must be set");
    }

    [Fact]
    public void Dispatch_WithoutComputeProgram_Throws_WithValidationDisabled()
    {
        AssertThrowsWithValidationDisabled(new LambdaComputePass(cmd => cmd.Dispatch(1, 1, 1)), "ComputeProgram must be set");
    }

    [Fact]
    public void DispatchIndirect_WithoutComputeProgram_Throws_WithValidationDisabled()
    {
        DeviceBuffer indirect = RF.CreateBuffer(new BufferDescription(16, BufferUsage.IndirectBuffer));
        AssertThrowsWithValidationDisabled(new LambdaComputePass(cmd => cmd.DispatchIndirect(indirect, 0)), "ComputeProgram must be set");
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanBoundStateCheckTests : BoundStateCheckTests<VulkanDeviceCreator> { }
#endif
