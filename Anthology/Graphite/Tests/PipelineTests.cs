using System;
using System.Diagnostics;
using Prowl.Vector;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Prowl.Veldrid.Tests;

public abstract class PipelineTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void CreatePipelines_DifferentInstanceStepRate_Succeeds()
    {
        Texture colorTex = RF.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        Framebuffer framebuffer = RF.CreateFramebuffer(new FramebufferDescription(null, colorTex));

        ShaderSetDescription shaderSet = new ShaderSetDescription(
            new VertexLayoutDescription[]
            {
                new VertexLayoutDescription(
                    0u,
                    24,
                    0,
                    new VertexElementDescription("Position", VertexElementFormat.Float2),
                    new VertexElementDescription("Color_UInt", VertexElementFormat.UInt4))
            },
            TestShaders.LoadVertexFragment(RF, "UIntVertexAttribs"));

        ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex, 0),
            new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex, 1)));

        GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.Default,
            PrimitiveTopology.PointList,
            shaderSet,
            layout,
            framebuffer.OutputDescription);

        Pipeline pipeline1 = RF.CreateGraphicsPipeline(ref gpd);
        Pipeline pipeline2 = RF.CreateGraphicsPipeline(ref gpd);

        gpd.ShaderSet.VertexLayouts[0].InstanceStepRate = 4;
        Pipeline pipeline3 = RF.CreateGraphicsPipeline(ref gpd);

        gpd.ShaderSet.VertexLayouts[0].InstanceStepRate = 5;
        Pipeline pipeline4 = RF.CreateGraphicsPipeline(ref gpd);
    }
}

#if TEST_OPENGL
[Trait("Backend", "OpenGL")]
public class OpenGLPipelineTests : PipelineTests<OpenGLDeviceCreator> { }
#endif
#if TEST_OPENGLES
[Trait("Backend", "OpenGLES")]
public class OpenGLESPipelineTests : PipelineTests<OpenGLESDeviceCreator> { }
#endif
#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
public class VulkanPipelineTests : PipelineTests<VulkanDeviceCreator> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
public class D3D11PipelineTests : PipelineTests<D3D11DeviceCreator> { }
#endif
