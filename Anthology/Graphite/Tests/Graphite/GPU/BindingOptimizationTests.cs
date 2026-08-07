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

    [Fact]
    public void AlternatingVertexSources_OneRecording_EachDrawBindsCorrectBuffer()
    {
        const uint size = 50;
        const uint norm = 1000;

        GraphicsProgram program = CreateUIntColorPointProgram();

        UIntPointVertex vertexA = new() { Position = new(10.5f, 10.5f), Color = new Int4 { X = (int)norm } };
        UIntPointVertex vertexB = new() { Position = new(40.5f, 40.5f), Color = new Int4 { Y = (int)norm } };

        TestVertexSource sourceA = new(PrimitiveTopology.PointList, [CreatePointVertexBuffer(vertexA)]);
        TestVertexSource sourceB = new(PrimitiveTopology.PointList, [CreatePointVertexBuffer(vertexB)]);

        (Texture target, Framebuffer fb) = CreateColorTarget(size, size);

        PropertySet props = new();
        props.SetMatrix("Ortho", Float4x4.CreateOrthoOffCenter(0, size, size, 0, -1, 1));
        props.SetInt("ColorNormalizationFactor", (int)norm);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, Color.Black);
            cl.SetFullViewports();
            cl.SetShader(program);
            cl.SetProperties(props);
            for (int i = 0; i < 6; i++)
            {
                cl.SetVertexSource((i % 2) == 0 ? sourceA : sourceB);
                cl.Draw(1);
            }
            context.SubmitCommandBuffer(cl);
        });

        Texture readback = GetReadback(target);
        MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read);
        Color pixelA = map[10, FlipY(10, size)];
        Color pixelB = map[40, FlipY(40, size)];
        GD.Unmap(readback);

        Assert.Equal(new Color(1f, 0f, 0f, 1f), pixelA, ColorFuzzyComparer.Instance);
        Assert.Equal(new Color(0f, 1f, 0f, 1f), pixelB, ColorFuzzyComparer.Instance);
    }

    // ---- Narrowed bind emission: skip-when-unchanged, firstSet narrowing, program-change invalidation ----

    [Fact]
    public void PropertiesReapplied_SameValues_BindSkipped_StillCorrect()
    {
        // Every iteration reapplies a fresh PropertySet with the same buffer, bumping the property
        // epoch without changing any resolved binding. The generalized identity-based skip (beyond
        // the old epoch fast path) must still leave the correct descriptor set bound.
        const uint size = 64;
        (Texture target, Framebuffer fb) = CreateColorTarget(size, size);
        GraphicsProgram program = CreateColoredQuadProgram();

        Float4 color = new(0.1f, 0.5f, 0.9f, 1f);
        DeviceBuffer vb = CreateQuad(color);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, Color.Black);
            cl.SetFullViewports();
            cl.SetShader(program);
            cl.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleStrip, []));
            for (int i = 0; i < 5; i++)
            {
                PropertySet iterProps = new();
                iterProps.SetBuffer("InputVertices", vb, readOnly: true);
                cl.SetProperties(iterProps);
                cl.Draw(4);
            }
            context.SubmitCommandBuffer(cl);
        });

        Texture readback = GetReadback(target);
        MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read);
        Color pixel = map[size / 2, size / 2];
        GD.Unmap(readback);

        Assert.Equal(new Color(0.1f, 0.5f, 0.9f, 1f), pixel, ColorFuzzyComparer.Instance);
    }

    [SkippableFact]
    public void LastSetOnlyChanges_OneRecording_EachDispatchCorrect()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const int n = 8;
        ComputeProgram program = CreateLastSetVariesProgram();
        const uint fixedValue = 55;
        DeviceBuffer[] outputs = new DeviceBuffer[n];
        for (int i = 0; i < n; i++) outputs[i] = CreateOutput();

        // Set 0 (BlockA.fixedValue) never changes across the recording; only set 1 (BlockB.valueB +
        // Output) changes every dispatch. Exercises the narrowed firstSet path (rebind starts at 1).
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            for (int i = 0; i < n; i++)
            {
                PropertySet props = new();
                props.SetInt("fixedValue", (int)fixedValue);
                props.SetInt("valueB", 300 + i);
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
            Assert.Equal(fixedValue, r[0]);
            Assert.Equal((uint)(300 + i), r[1]);
        }
    }

    [SkippableFact]
    public void AlternatingPrograms_OneRecording_BothProduceCorrectResults()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const int n = 8;
        ComputeProgram programX = CreateTwoBlockProgram();
        ComputeProgram programY = CreateTwoBlockProgram();
        DeviceBuffer[] outputsX = new DeviceBuffer[n];
        DeviceBuffer[] outputsY = new DeviceBuffer[n];
        for (int i = 0; i < n; i++)
        {
            outputsX[i] = CreateOutput();
            outputsY[i] = CreateOutput();
        }

        // Flip-flop between two distinct ComputeProgram instances every dispatch. Each switch must
        // force a full rebind (program-change invalidation), never reusing the other program's sets.
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            for (int i = 0; i < n; i++)
            {
                bool useX = (i % 2) == 0;
                cl.SetComputeShader(useX ? programX : programY);

                PropertySet props = new();
                props.SetInt("valueA", useX ? 10 + i : 20 + i);
                props.SetInt("valueB", useX ? 30 + i : 40 + i);
                props.SetBuffer("Output", useX ? outputsX[i] : outputsY[i], readOnly: false);
                cl.SetProperties(props);
                cl.Dispatch(1, 1, 1);
            }
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        for (int i = 0; i < n; i++)
        {
            bool useX = (i % 2) == 0;
            uint[] r = useX ? Read(outputsX[i]) : Read(outputsY[i]);
            Assert.Equal(useX ? (uint)(10 + i) : (uint)(20 + i), r[0]);
            Assert.Equal(useX ? (uint)(30 + i) : (uint)(40 + i), r[1]);
        }
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

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct UIntPointVertex
    {
        public Float2 Position;
        public Int4 Color;
    }

    private uint FlipY(uint y, uint height)
        => (!GD.IsUvOriginTopLeft || GD.IsClipSpaceYInverted) ? height - y - 1 : y;

    private DeviceBuffer CreatePointVertexBuffer(UIntPointVertex vertex)
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(
            (uint)Unsafe.SizeOf<UIntPointVertex>(), BufferUsage.VertexBuffer));
        GD.UpdateBuffer(buffer, 0, [vertex]);
        return buffer;
    }

    private GraphicsProgram CreateUIntColorPointProgram()
    {
        VertexLayoutDescription layout = new(0, (uint)Unsafe.SizeOf<UIntPointVertex>(),
            new VertexElementDescription("POSITION", VertexElementFormat.Float2),
            new VertexElementDescription("COLOR", VertexElementFormat.UInt4));

        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "UIntVertexAttribs.slang");
        ShaderDescription desc = new(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.Default,
            VertexLayouts = [layout],
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

    private ComputeProgram CreateLastSetVariesProgram()
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "TwoBlockLastSetVaries.slang");
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
                        UniformFields = [new UniformBlockField("fixedValue", 0, sizeof(uint), UniformScalarType.Int1)]
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
                    new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1)
                    {
                        GLUniformName = "StructuredBuffer_uint_t_0"
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
