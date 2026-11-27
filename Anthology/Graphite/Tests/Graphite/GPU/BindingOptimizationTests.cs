using System;
using System.Runtime.CompilerServices;

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

    [Fact]
    public void ManyDispatches_OneRecording_EachSeesItsOwnLooseUniforms()
    {
        const int n = 8;
        ComputeProgram program = CreateTwoBlockProgram();
        DeviceBuffer[] outputs = new DeviceBuffer[n];
        for (int i = 0; i < n; i++) outputs[i] = CreateOutput();

        // Distinct (valueA, valueB) per dispatch in a single recording. Value-based transient reuse
        // must NOT alias dispatch i's uniforms onto dispatch i+1 just because entry versions coincide.
        GD.RunTestGraph((context, cl) =>
        {
            cl.SetComputeShader(program);
            for (int i = 0; i < n; i++)
            {
                PropertySet props = new();
                props.SetInt("valueA", 100 + i);
                props.SetInt("valueB", 200 + i);
                props.SetBuffer("Output", outputs[i]);
                cl.SetProperties(props);
                cl.Dispatch(1, 1, 1);
            }
        });
        GD.WaitForIdle();

        for (int i = 0; i < n; i++)
        {
            uint[] r = Read(outputs[i]);
            Assert.Equal((uint)(100 + i), r[0]);
            Assert.Equal((uint)(200 + i), r[1]);
        }

        // Flip-flop between two uniform configs. A set going back to a prior identity must resolve to
        // its own value, never to the one bound in between.
        GD.RunTestGraph((context, cl) =>
        {
            cl.SetComputeShader(program);
            for (int i = 0; i < n; i++)
            {
                bool even = (i % 2) == 0;
                PropertySet props = new();
                props.SetInt("valueA", even ? 1 : 9);
                props.SetInt("valueB", even ? 2 : 8);
                props.SetBuffer("Output", outputs[i]);
                cl.SetProperties(props);
                cl.Dispatch(1, 1, 1);
            }
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
    public void InPlacePropertyChange_OneRenderPass_PickedUpOnReapply()
    {
        const uint size = 50;
        const uint norm = 1000;

        GraphicsProgram program = CreateUIntColorPointProgram();

        UIntPointVertex vertexA = new() { Position = new(10.5f, 10.5f), Color = new Int4 { X = (int)norm } };
        UIntPointVertex vertexB = new() { Position = new(40.5f, 40.5f), Color = new Int4 { Y = (int)norm } };

        VertexSource sourceA = new VertexSource(PrimitiveTopology.PointList).SetBuffer("POSITION", CreatePointVertexBuffer(vertexA));
        VertexSource sourceB = new VertexSource(PrimitiveTopology.PointList).SetBuffer("POSITION", CreatePointVertexBuffer(vertexB));

        (Texture target, Framebuffer fb) = CreateColorTarget(size, size);

        PropertySet props = new();
        props.SetMatrix("Ortho", Float4x4.CreateOrthoOffCenter(0, size, size, 0, -1, 1));
        props.SetInt("ColorNormalizationFactor", (int)norm);

        GD.RunTestGraph((context, cl) =>
        {
            cl.SetFramebuffer(fb, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cl.SetFullViewport();
            cl.SetShader(program);
            cl.SetProperties(props);
            cl.SetVertexSource(sourceA);
            cl.Draw(1);
            props.SetInt("ColorNormalizationFactor", (int)norm * 2);
            cl.SetProperties(props);
            cl.SetVertexSource(sourceB);
            cl.Draw(1);
        });

        TexelData<Color> map = ReadTexture<Color>(target);
        Color pixelA = map[10, FlipY(10, size)];
        Color pixelB = map[40, FlipY(40, size)];

        Assert.Equal(new Color(1f, 0f, 0f, 1f), pixelA, ColorFuzzyComparer.Instance);
        Assert.Equal(new Color(0f, 0.5f, 0f, 1f), pixelB, ColorFuzzyComparer.Instance);

        // Every iteration reapplies a fresh PropertySet with the same buffer, bumping the property
        // epoch without changing any resolved binding. The generalized identity-based skip (beyond
        // the old epoch fast path) must still leave the correct descriptor set bound.
        const uint quadSize = 64;
        (Texture quadTarget, Framebuffer quadFb) = CreateColorTarget(quadSize, quadSize);
        GraphicsProgram quadProgram = CreateColoredQuadProgram();

        Float4 quadColor = new(0.1f, 0.5f, 0.9f, 1f);
        DeviceBuffer quadVb = CreateQuad(quadColor);

        GD.RunTestGraph((context, cl) =>
        {
            cl.SetFramebuffer(quadFb, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cl.SetFullViewport();
            cl.SetShader(quadProgram);
            cl.SetVertexSource(new VertexSource(PrimitiveTopology.TriangleStrip));
            for (int i = 0; i < 5; i++)
            {
                PropertySet reapplied = new();
                reapplied.SetBuffer("InputVertices", quadVb);
                cl.SetProperties(reapplied);
                cl.Draw(4);
            }
        });

        TexelData<Color> quadMap = ReadTexture<Color>(quadTarget);
        Color quadPixel = quadMap[quadSize / 2, quadSize / 2];

        Assert.Equal(new Color(0.1f, 0.5f, 0.9f, 1f), quadPixel, ColorFuzzyComparer.Instance);
    }

    // ---- Narrowed bind emission: skip-when-unchanged, firstSet narrowing, program-change invalidation ----

    [Fact]
    public void LastSetOnlyChanges_OneRecording_EachDispatchCorrect()
    {
        const int n = 8;
        ComputeProgram program = CreateLastSetVariesProgram();
        const uint fixedValue = 55;
        DeviceBuffer[] outputs = new DeviceBuffer[n];
        for (int i = 0; i < n; i++) outputs[i] = CreateOutput();

        // Set 0 (BlockA.fixedValue) never changes across the recording; only set 1 (BlockB.valueB +
        // Output) changes every dispatch. Exercises the narrowed firstSet path (rebind starts at 1).
        GD.RunTestGraph((context, cl) =>
        {
            cl.SetComputeShader(program);
            for (int i = 0; i < n; i++)
            {
                PropertySet props = new();
                props.SetInt("fixedValue", (int)fixedValue);
                props.SetInt("valueB", 300 + i);
                props.SetBuffer("Output", outputs[i]);
                cl.SetProperties(props);
                cl.Dispatch(1, 1, 1);
            }
        });
        GD.WaitForIdle();

        for (int i = 0; i < n; i++)
        {
            uint[] r = Read(outputs[i]);
            Assert.Equal(fixedValue, r[0]);
            Assert.Equal((uint)(300 + i), r[1]);
        }

        DeviceBuffer first = CreateOutput();
        DeviceBuffer second = CreateOutput();
        PropertySet firstProps = new();
        firstProps.SetInt("fixedValue", 42);
        firstProps.SetInt("valueB", 100);
        firstProps.SetBuffer("Output", first);
        PropertySet secondProps = new();
        secondProps.SetInt("fixedValue", 42);
        secondProps.SetInt("valueB", 200);
        secondProps.SetBuffer("Output", second);

        GD.RunTestGraphPasses(2, (context, cl, i) =>
        {
            cl.SetComputeShader(program);
            cl.SetProperties(firstProps);
            if (i == 1)
                cl.SetProperties(secondProps);
            cl.Dispatch(1, 1, 1);
        });
        GD.WaitForIdle();

        Assert.Equal(new uint[] { 42, 100 }, Read(first));
        Assert.Equal(new uint[] { 42, 200 }, Read(second));
    }

    // ---- Command-buffer pooling (#2): rented graph command buffers are recycled, not recreated ----

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
        const float y = 1.0f;
        ColoredVertex[] vertices =
        [
            new(new Float2(-1, 1 * y), color),
            new(new Float2(1, 1 * y), color),
            new(new Float2(-1, -1 * y), color),
            new(new Float2(1, -1 * y), color),
        ];
        uint stride = (uint)Unsafe.SizeOf<ColoredVertex>();
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(
            stride * (uint)vertices.Length, BufferUsage.StructuredBufferReadOnly));
        GD.UpdateBuffer(buffer, 0, vertices);
        return buffer;
    }

    private GraphicsProgram CreateColoredQuadProgram()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "ColoredQuadRenderer.glsl");
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
        => RF.CreateBuffer(new BufferDescription(2 * sizeof(uint), BufferUsage.StructuredBufferReadWrite));

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct UIntPointVertex
    {
        public Float2 Position;
        public Int4 Color;
    }

    private uint FlipY(uint y, uint height)
        => !GD.IsUvOriginTopLeft ? height - y - 1 : y;

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

        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "UIntVertexAttribs.glsl");
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
        Span<uint> map = GD.Map<uint>(readback);
        uint[] result = [map[0], map[1]];
        GD.Unmap(readback);
        return result;
    }

    private ComputeProgram CreateTwoBlockProgram()
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "MultiParameterBlockBindingTest.glsl");
        ResourceLayoutDescription[] layouts =
        [
            new ResourceLayoutDescription
            {
                Set = 0,
                Elements =
                [
                    new ResourceLayoutElementDescription("BlockA", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                    {
                        UniformFields = [new UniformBlockField("valueA", 0, sizeof(uint), UniformScalarType.Int1)]
                    },
                    new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1)
                    {
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
                        UniformFields = [new UniformBlockField("valueB", 0, sizeof(uint), UniformScalarType.Int1)]
                    },
                ]
            }
        ];
        return RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 1, 1, 1));
    }

    private ComputeProgram CreateLastSetVariesProgram()
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "TwoBlockLastSetVaries.glsl");
        ResourceLayoutDescription[] layouts =
        [
            new ResourceLayoutDescription
            {
                Set = 0,
                Elements =
                [
                    new ResourceLayoutElementDescription("BlockA", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                    {
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
                        UniformFields = [new UniformBlockField("valueB", 0, sizeof(uint), UniformScalarType.Int1)]
                    },
                    new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1)
                    {
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
