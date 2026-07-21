using System;
using System.Runtime.CompilerServices;

using Prowl.Graphite.Vk;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Behavioral coverage for the per-draw binding optimizations: draw-to-draw set dedup, value-based
// transient-UBO reuse, resolve-once, and command-buffer pooling. These exercise the code paths that
// only trigger across multiple draws in one recording (fast-path skips, per-set identity caching),
// and assert results stay correct - a stale cached set or reused transient would produce wrong values.
public abstract class BindingOptimizationTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    // ---- Compute: two sets, each with a loose-uniform UBO plus a structured output ----

    [SkippableFact]
    public void ManyDispatches_OneRecording_EachSeesItsOwnLooseUniforms()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const int n = 8;
        ComputeProgram program = CreateTwoBlockProgram();
        DeviceBuffer[] outputs = new DeviceBuffer[n];
        for (int i = 0; i < n; i++) outputs[i] = CreateOutput();

        // Distinct (valueA, valueB) per dispatch in a single recording. Value-based transient reuse
        // must NOT alias dispatch i's uniforms onto dispatch i+1 just because entry versions coincide.
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            for (int i = 0; i < n; i++)
            {
                PropertySet props = new();
                props.SetInt("valueA", 100 + i);
                props.SetInt("valueB", 200 + i);
                props.SetBuffer("Output", outputs[i], readOnly: false);
                cl.SetProperties(props);
                cl.Dispatch(1, 1, 1);
            }
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        for (int i = 0; i < n; i++)
        {
            uint[] r = Read(outputs[i]);
            Assert.Equal((uint)(100 + i), r[0]);
            Assert.Equal((uint)(200 + i), r[1]);
        }
    }

    [SkippableFact]
    public void RepeatedIdenticalDispatches_OneRecording_AllCorrect()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const int n = 6;
        ComputeProgram program = CreateTwoBlockProgram();
        DeviceBuffer[] outputs = new DeviceBuffer[n];
        for (int i = 0; i < n; i++) outputs[i] = CreateOutput();

        // Same uniform values every dispatch, only the output changes. Exercises the per-set identity
        // cache-hit path for the unchanged uniform sets while the output set legitimately rebinds.
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            for (int i = 0; i < n; i++)
            {
                PropertySet props = new();
                props.SetInt("valueA", 42);
                props.SetInt("valueB", 77);
                props.SetBuffer("Output", outputs[i], readOnly: false);
                cl.SetProperties(props);
                cl.Dispatch(1, 1, 1);
            }
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        for (int i = 0; i < n; i++)
        {
            uint[] r = Read(outputs[i]);
            Assert.Equal(42u, r[0]);
            Assert.Equal(77u, r[1]);
        }
    }

    [SkippableFact]
    public void AlternatingUniforms_OneRecording_EachDispatchCorrect()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const int n = 8;
        ComputeProgram program = CreateTwoBlockProgram();
        DeviceBuffer[] outputs = new DeviceBuffer[n];
        for (int i = 0; i < n; i++) outputs[i] = CreateOutput();

        // Flip-flop between two uniform configs. A set going back to a prior identity must resolve to
        // its own value, never to the one bound in between.
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            for (int i = 0; i < n; i++)
            {
                bool even = (i % 2) == 0;
                PropertySet props = new();
                props.SetInt("valueA", even ? 1 : 9);
                props.SetInt("valueB", even ? 2 : 8);
                props.SetBuffer("Output", outputs[i], readOnly: false);
                cl.SetProperties(props);
                cl.Dispatch(1, 1, 1);
            }
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        for (int i = 0; i < n; i++)
        {
            bool even = (i % 2) == 0;
            uint[] r = Read(outputs[i]);
            Assert.Equal(even ? 1u : 9u, r[0]);
            Assert.Equal(even ? 2u : 8u, r[1]);
        }
    }

    // ---- Graphics: whole-draw fast path (repeated identical draws in one active render pass) ----

    [Fact]
    public void RepeatedIdenticalDraws_OneRenderPass_RenderCorrectly()
    {
        // Draw the same full-screen quad several times without touching properties between draws. Draws
        // after the first hit the whole-draw fast path (pass active, epoch unchanged) and skip the
        // rebind - the pixel must still be written, proving the skip does not drop the draw's bindings.
        const uint size = 64;
        (Texture target, Framebuffer fb) = CreateColorTarget(size, size);
        GraphicsProgram program = CreateColoredQuadProgram();

        Float4 color = new(0.2f, 0.4f, 0.6f, 1f);
        DeviceBuffer vb = CreateQuad(color);

        PropertySet props = new();
        props.SetBuffer("InputVertices", vb, readOnly: true);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, Color.Black);
            cl.SetFullViewports();
            cl.SetShader(program);
            cl.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleStrip, []));
            cl.SetProperties(props);
            for (int i = 0; i < 5; i++)
                cl.Draw(4);
            context.SubmitCommandBuffer(cl);
        });

        Texture readback = GetReadback(target);
        MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read);
        Color pixel = map[size / 2, size / 2];
        GD.Unmap(readback);

        Assert.Equal(new Color(0.2f, 0.4f, 0.6f, 1f), pixel, ColorFuzzyComparer.Instance);
    }

    // ---- Command-buffer pooling (#2): rented graph command buffers are recycled, not recreated ----

    [Fact]
    public void GraphCommandBuffers_AreRecycled_AcrossGraphs()
    {
        VkGraphicsDevice vk = (VkGraphicsDevice)GD;

        // Each graph rents one command buffer; when its ring slot is reused by a later execution the
        // buffer is reclaimed and handed out again. Steady state is therefore bounded by the ring size
        // (one recyclable buffer per slot). Without recycling, the count would climb with the loop.
        int graphs = (int)GD.MaxExecutingTasks * 8;
        for (int i = 0; i < graphs; i++)
        {
            GD.RunTestGraph(context =>
            {
                CommandBuffer cl = context.GetCommandBuffer();
                context.SubmitCommandBuffer(cl);
            });
            GD.WaitForIdle();
        }

        Assert.True(vk.PooledGraphCommandBufferCount <= GD.MaxExecutingTasks + 1,
            $"Expected graph command buffers to be recycled to ~{GD.MaxExecutingTasks}, but {vk.PooledGraphCommandBufferCount} were allocated over {graphs} graphs.");
    }

    // ---- helpers ----

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ColoredVertex
    {
        public Float4 Color;
        public Float2 Position;
        private Float2 _padding0;

        public ColoredVertex(Float2 position, Float4 color)
        {
            Position = position;
            Color = color;
            _padding0 = default;
        }
    }

    private (Texture target, Framebuffer fb) CreateColorTarget(uint width, uint height)
    {
        Texture target = RF.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, target));
        return (target, fb);
    }

    private DeviceBuffer CreateQuad(Float4 color)
    {
        float y = GD.IsClipSpaceYInverted ? -1.0f : 1.0f;
        ColoredVertex[] vertices =
        [
            new(new Float2(-1, 1 * y), color),
            new(new Float2(1, 1 * y), color),
            new(new Float2(-1, -1 * y), color),
            new(new Float2(1, -1 * y), color),
        ];
        uint stride = (uint)Unsafe.SizeOf<ColoredVertex>();
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(
            stride * (uint)vertices.Length, BufferUsage.StructuredBufferReadOnly, stride));
        GD.UpdateBuffer(buffer, 0, vertices);
        return buffer;
    }

    private GraphicsProgram CreateColoredQuadProgram()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "ColoredQuadRenderer.slang");
        ShaderDescription desc = new(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.Default,
            ResourceLayouts =
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements = [new ResourceLayoutElementDescription("InputVertices", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex, 0)]
                }
            ],
        };
        return RF.CreateGraphicsProgram(desc);
    }

    private DeviceBuffer CreateOutput()
        => RF.CreateBuffer(new BufferDescription(2 * sizeof(uint), BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

    private uint[] Read(DeviceBuffer output)
    {
        DeviceBuffer readback = GetReadback(output);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        uint[] result = [map[0], map[1]];
        GD.Unmap(readback);
        return result;
    }

    private ComputeProgram CreateTwoBlockProgram()
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "MultiParameterBlockBindingTest.slang");
        ResourceLayoutDescription[] layouts =
        [
            new ResourceLayoutDescription
            {
                Set = 0,
                Elements =
                [
                    new ResourceLayoutElementDescription("BlockA", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                    {
                        GLUniformName = "block_BlockAData_0",
                        UniformFields = [new UniformBlockField("valueA", 0, sizeof(uint), UniformScalarType.Int1)]
                    },
                    new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1)
                    {
                        GLUniformName = "StructuredBuffer_uint_t_0"
                    },
                ]
            },
            new ResourceLayoutDescription
            {
                Set = 1,
                Elements =
                [
                    new ResourceLayoutElementDescription("BlockB", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                    {
                        GLUniformName = "block_BlockBData_0",
                        UniformFields = [new UniformBlockField("valueB", 0, sizeof(uint), UniformScalarType.Int1)]
                    },
                ]
            }
        ];
        return RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 1, 1, 1));
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanBindingOptimizationTests : BindingOptimizationTests<VulkanDeviceCreator> { }
#endif
