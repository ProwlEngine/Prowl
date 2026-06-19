using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Core-path render test: draws a colored point through the GraphicsProgram + PropertySet +
// Frame API and reads the result back. Exercises uint vertex attributes, a uniform block bound
// by name, an offscreen render target, and the frame submission path.
public abstract class RenderCoreTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Float2 Position;
        public Int4 Color_Int;
    }

    [Fact]
    public void Points_WithUIntColor_ProduceExpectedPixel()
    {
        const uint width = 50;
        const uint height = 50;
        const uint norm = 2500;

        Texture target = RF.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
        Framebuffer framebuffer = RF.CreateFramebuffer(new FramebufferDescription(null, target));

        GraphicsProgram program = CreateIntVertexAttribsProgram();

        Vertex vertex = new()
        {
            Position = new Float2(25.5f, 25.5f),
            Color_Int = new Int4
            {
                X = (int)(0.25f * norm),
                Y = (int)(0.5f * norm),
                Z = (int)(0.75f * norm),
            }
        };

        DeviceBuffer vertexBuffer = RF.CreateBuffer(new BufferDescription(
            (uint)Unsafe.SizeOf<Vertex>(), BufferUsage.VertexBuffer));
        GD.UpdateBuffer(vertexBuffer, 0, [vertex]);

        PropertySet props = new();
        props.SetMatrix("Ortho", Float4x4.CreateOrthoOffCenter(0, width, height, 0, -1, 1));
        props.SetInt("ColorNormalizationFactor", (int)norm);

        TestVertexSource source = new(PrimitiveTopology.PointList, [vertexBuffer]);

        // The frame must be open while recording: property binding allocates transient memory.
        Frame frame = GD.BeginFrame();
        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetFramebuffer(framebuffer);
        cl.ClearColorTarget(0, new Color(0, 0, 0, 1));
        cl.SetFullViewports();
        cl.SetShader(program);
        cl.SetVertexSource(source);
        cl.SetProperties(props);
        cl.Draw(1);
        cl.End();

        frame.SubmitCommands(cl);
        GD.EndFrame(frame);
        GD.WaitForIdle();

        Texture readback = GetReadback(target);
        MappedResourceView<Float4> map = GD.Map<Float4>(readback, MapMode.Read, 0);
        uint rowStride = map.MappedResource.RowPitch / (uint)Unsafe.SizeOf<Float4>();
        uint row = (!GD.IsUvOriginTopLeft || GD.IsClipSpaceYInverted) ? height - 25 - 1 : 25;
        Float4 pixel = map[(int)(row * rowStride + 25)];
        GD.Unmap(readback);

        Assert.Equal(0.25f, pixel.X, 2);
        Assert.Equal(0.5f, pixel.Y, 2);
        Assert.Equal(0.75f, pixel.Z, 2);
        Assert.Equal(1.0f, pixel.W, 2);
    }

    private GraphicsProgram CreateIntVertexAttribsProgram()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "UIntVertexAttribs.slang");

        ShaderDescription desc = new(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.Default,
            VertexLayouts =
            [
                new VertexLayoutDescription
                {
                    Location = 0,
                    Stride = (uint)Unsafe.SizeOf<Vertex>(),
                    Elements =
                    [
                        new VertexElementDescription("POSITION", VertexElementFormat.Float2),
                        new VertexElementDescription("COLOR", VertexElementFormat.Int4),
                    ]
                }
            ],
            ResourceLayouts =
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements =
                    [
                        new ResourceLayoutElementDescription("Model", ResourceKind.UniformBuffer, ShaderStages.Vertex, 0)
                        {
                            UniformFields =
                            [
                                new UniformBlockField("Ortho", 0, sizeof(float) * 16, UniformScalarType.Float4x4),
                                new UniformBlockField("ColorNormalizationFactor", sizeof(float) * 16, sizeof(uint), UniformScalarType.Int1),
                            ]
                        }
                    ]
                }
            ],
        };

        return RF.CreateGraphicsProgram(desc);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanRenderCoreTests : RenderCoreTests<VulkanDeviceCreator> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11RenderCoreTests : RenderCoreTests<D3D11DeviceCreator> { }
#endif
#if TEST_OPENGL
[Trait("Backend", "OpenGL")]
[Collection("GPU Tests")]
public class OpenGLRenderCoreTests : RenderCoreTests<OpenGLDeviceCreator> { }
#endif
#if TEST_OPENGLES
[Trait("Backend", "OpenGLES")]
[Collection("GPU Tests")]
public class OpenGLESRenderCoreTests : RenderCoreTests<OpenGLESDeviceCreator> { }
#endif
