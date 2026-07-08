using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Rasterization coverage on the GraphicsProgram + PropertySet + Frame API: vertex attribute
// formats (uint / ushort / normalized ushort / half), blend factor, color write mask, fragment
// depth writes, texture binding across multiple passes, and rendering into a specific framebuffer
// array layer. All targets are R32_G32_B32_A32_Float so readbacks can be mapped as Color.
public abstract class RenderTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint Size = 50;

    [StructLayout(LayoutKind.Sequential)]
    private struct UIntVertex
    {
        public Float2 Position;
        public Int4 Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UShortVertex
    {
        public Float2 Position;
        public ushort R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HalfVertex
    {
        public Float2 Position;
        public Half R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential)]
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

    private (Texture target, Framebuffer fb) CreateColorTarget(uint width = Size, uint height = Size)
    {
        Texture target = RF.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, target));
        return (target, fb);
    }

    private uint FlipY(uint y, uint height)
        => (!GD.IsUvOriginTopLeft || GD.IsClipSpaceYInverted) ? height - y - 1 : y;

    [Fact]
    public void Points_WithUIntColor_ProduceExpectedPixels()
    {
        const uint norm = 2500;
        UIntVertex[] vertices =
        [
            new() { Position = new(0.5f, 0.5f), Color = Scale(0.25f, 0.5f, 0.75f, norm) },
            new() { Position = new(10.5f, 12.5f), Color = Scale(0.25f, 0.5f, 0.75f, norm) },
            new() { Position = new(25.5f, 35.5f), Color = Scale(0.75f, 0.5f, 0.25f, norm) },
            new() { Position = new(49.5f, 49.5f), Color = Scale(0.15f, 0.25f, 0.35f, norm) },
        ];

        VertexLayoutDescription layout = new(0, (uint)Unsafe.SizeOf<UIntVertex>(),
            new VertexElementDescription("POSITION", VertexElementFormat.Float2),
            new VertexElementDescription("COLOR", VertexElementFormat.UInt4));

        DrawColoredPoints("UIntVertexAttribs.slang", layout, vertices, norm, vertices.Length,
            i => new Color(vertices[i].Color.X / (float)norm, vertices[i].Color.Y / (float)norm, vertices[i].Color.Z / (float)norm, 1),
            i => ((uint)vertices[i].Position.X, (uint)vertices[i].Position.Y));

        static Int4 Scale(float r, float g, float b, uint n) => new() { X = (int)(r * n), Y = (int)(g * n), Z = (int)(b * n) };
    }

    [Fact]
    public void Points_WithUShortColor_ProduceExpectedPixels()
    {
        const uint norm = 2500;
        UShortVertex[] vertices =
        [
            new() { Position = new(0.5f, 0.5f), R = US(0.25f, norm), G = US(0.5f, norm), B = US(0.75f, norm) },
            new() { Position = new(10.5f, 12.5f), R = US(0.25f, norm), G = US(0.5f, norm), B = US(0.75f, norm) },
            new() { Position = new(25.5f, 35.5f), R = US(0.75f, norm), G = US(0.5f, norm), B = US(0.25f, norm) },
            new() { Position = new(49.5f, 49.5f), R = US(0.15f, norm), G = US(0.2f, norm), B = US(0.35f, norm) },
        ];

        // The hardware widens UShort4 to uint4 in the shader, so the uint shader is reused here.
        VertexLayoutDescription layout = new(0, (uint)Unsafe.SizeOf<UShortVertex>(),
            new VertexElementDescription("POSITION", VertexElementFormat.Float2),
            new VertexElementDescription("COLOR", VertexElementFormat.UShort4));

        DrawColoredPoints("UIntVertexAttribs.slang", layout, vertices, norm, vertices.Length,
            i => new Color(vertices[i].R / (float)norm, vertices[i].G / (float)norm, vertices[i].B / (float)norm, 1),
            i => ((uint)vertices[i].Position.X, (uint)vertices[i].Position.Y));

        static ushort US(float v, uint n) => (ushort)(v * n);
    }

    [Fact]
    public void Points_WithUShortNormColor_ProduceExpectedPixels()
    {
        // Normalized ushorts arrive in [0, 1] as floats, so no normalization factor is applied.
        UShortVertex[] vertices =
        [
            new() { Position = new(0.5f, 0.5f), R = N(0.25f), G = N(0.5f), B = N(0.75f) },
            new() { Position = new(10.5f, 12.5f), R = N(0.25f), G = N(0.5f), B = N(0.75f) },
            new() { Position = new(25.5f, 35.5f), R = N(0.75f), G = N(0.5f), B = N(0.25f) },
            new() { Position = new(49.5f, 49.5f), R = N(0.15f), G = N(0.25f), B = N(0.35f) },
        ];

        VertexLayoutDescription layout = new(0, (uint)Unsafe.SizeOf<UShortVertex>(),
            new VertexElementDescription("POSITION", VertexElementFormat.Float2),
            new VertexElementDescription("COLOR", VertexElementFormat.UShort4_Norm));

        DrawColoredPoints("FloatColorVertexAttribs.slang", layout, vertices, 1, vertices.Length,
            i => new Color(vertices[i].R / (float)ushort.MaxValue, vertices[i].G / (float)ushort.MaxValue, vertices[i].B / (float)ushort.MaxValue, 1),
            i => ((uint)vertices[i].Position.X, (uint)vertices[i].Position.Y));

        static ushort N(float v) => (ushort)(v * ushort.MaxValue);
    }

    [Fact]
    public void Points_WithHalfColor_ProduceExpectedPixels()
    {
        const uint norm = 2500;
        HalfVertex[] vertices =
        [
            new() { Position = new(0.5f, 0.5f), R = (Half)625f, G = (Half)1250f, B = (Half)1875f },
            new() { Position = new(10.5f, 12.5f), R = (Half)625f, G = (Half)1250f, B = (Half)1875f },
            new() { Position = new(25.5f, 35.5f), R = (Half)1875f, G = (Half)1250f, B = (Half)625f },
            new() { Position = new(49.5f, 49.5f), R = (Half)375f, G = (Half)500f, B = (Half)875f },
        ];

        VertexLayoutDescription layout = new(0, (uint)Unsafe.SizeOf<HalfVertex>(),
            new VertexElementDescription("POSITION", VertexElementFormat.Float2),
            new VertexElementDescription("COLOR", VertexElementFormat.Half4));

        DrawColoredPoints("FloatColorVertexAttribs.slang", layout, vertices, norm, vertices.Length,
            i => new Color((float)vertices[i].R / norm, (float)vertices[i].G / norm, (float)vertices[i].B / norm, 1),
            i => ((uint)vertices[i].Position.X, (uint)vertices[i].Position.Y));
    }

    // Shared driver for the point-color format tests. Builds a GraphicsProgram from the given
    // module, draws one point per vertex through the Frame API, and asserts the resulting pixels.
    private void DrawColoredPoints<TVertex>(
        string module,
        VertexLayoutDescription layout,
        TVertex[] vertices,
        uint normalizationFactor,
        int pointCount,
        Func<int, Color> expectedColor,
        Func<int, (uint x, uint y)> pixelOf) where TVertex : unmanaged
    {
        (Texture target, Framebuffer fb) = CreateColorTarget();

        GraphicsProgram program = CreateColorProgram(module, layout);

        DeviceBuffer vb = RF.CreateBuffer(new BufferDescription(
            (uint)(Unsafe.SizeOf<TVertex>() * vertices.Length), BufferUsage.VertexBuffer));
        GD.UpdateBuffer(vb, 0, vertices);

        PropertySet props = new();
        props.SetMatrix("Ortho", Float4x4.CreateOrthoOffCenter(0, Size, Size, 0, -1, 1));
        props.SetInt("ColorNormalizationFactor", (int)normalizationFactor);

        TestVertexSource source = new(PrimitiveTopology.PointList, [vb]);

        Submit(cl =>
        {
            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, Color.Black);
            cl.SetFullViewports();
            cl.SetShader(program);
            cl.SetVertexSource(source);
            cl.SetProperties(props);
            cl.Draw((uint)pointCount);
        });

        Texture readback = GetReadback(target);
        MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read);
        for (int i = 0; i < pointCount; i++)
        {
            (uint x, uint y) = pixelOf(i);
            Assert.Equal(expectedColor(i), map[x, FlipY(y, Size)], ColorFuzzyComparer.Instance);
        }
        GD.Unmap(readback);
    }

    private GraphicsProgram CreateColorProgram(string module, VertexLayoutDescription layout)
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, module);
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

    [Fact]
    public void WriteFragmentDepth_ProducesDepthRamp()
    {
        const uint size = 64;
        Texture depthTarget = RF.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.R32_Float, TextureUsage.DepthStencil | TextureUsage.Sampled));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(depthTarget));

        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "FullScreenWriteDepth.slang");
        ShaderDescription desc = new(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(true, true, ComparisonKind.Always),
            RasterizerState = RasterizerStateDescription.CullNone,
            ResourceLayouts =
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements =
                    [
                        new ResourceLayoutElementDescription("Frame", ResourceKind.UniformBuffer, ShaderStages.Fragment, 0)
                        {
                            UniformFields = [new UniformBlockField("OutputSize", 0, sizeof(float) * 4, UniformScalarType.Float4)]
                        }
                    ]
                }
            ],
        };
        GraphicsProgram program = RF.CreateGraphicsProgram(desc);

        PropertySet props = new();
        props.SetFloat4("OutputSize", new Float4(size, size, 0, 0));

        Submit(cl =>
        {
            cl.SetFramebuffer(fb);
            cl.ClearDepthStencil(0f);
            cl.SetFullViewports();
            cl.SetShader(program);
            cl.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleList, []));
            cl.SetProperties(props);
            cl.Draw(3);
        });

        Texture readback = GetReadback(depthTarget);
        MappedResourceView<float> map = GD.Map<float>(readback, MapMode.Read);
        for (uint y = 0; y < size; y++)
        {
            for (uint x = 0; x < size; x++)
            {
                float expected = (y * size + x) / (float)(size * size);
                Assert.Equal(expected, map[x, y], 2.0f);
            }
        }
        GD.Unmap(readback);
    }

    [Fact]
    public void UseBlendFactor_BlendsAgainstConstant()
    {
        const uint size = 64;
        (Texture target, Framebuffer fb) = CreateColorTarget(size, size);
        DeviceBuffer vb = CreateFullScreenQuad();

        BlendStateDescription blend = new()
        {
            BlendFactor = new Color(0.25f, 0.5f, 0.75f, 1),
            AttachmentStates =
            [
                new BlendAttachmentDescription
                {
                    BlendEnabled = true,
                    SourceColorFactor = BlendFactor.BlendFactor,
                    DestinationColorFactor = BlendFactor.Zero,
                    ColorFunction = BlendFunction.Add,
                    SourceAlphaFactor = BlendFactor.BlendFactor,
                    DestinationAlphaFactor = BlendFactor.Zero,
                    AlphaFunction = BlendFunction.Add,
                }
            ]
        };

        GraphicsProgram program = CreateColoredQuadProgram(blend);

        DrawColoredQuad(fb, program, vb, Color.Black);

        Texture readback = GetReadback(target);
        MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read);
        Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 1), map[size / 2, size / 2], ColorFuzzyComparer.Instance);
        GD.Unmap(readback);
    }

    [Fact]
    public void UseColorWriteMask_RespectsPerChannelMask()
    {
        const uint size = 64;
        (Texture target, Framebuffer fb) = CreateColorTarget(size, size);
        DeviceBuffer vb = CreateFullScreenQuad();

        foreach (ColorWriteMask mask in Enum.GetValues<ColorWriteMask>())
        {
            BlendStateDescription blend = new()
            {
                AttachmentStates =
                [
                    new BlendAttachmentDescription
                    {
                        BlendEnabled = true,
                        SourceColorFactor = BlendFactor.One,
                        DestinationColorFactor = BlendFactor.Zero,
                        ColorFunction = BlendFunction.Add,
                        SourceAlphaFactor = BlendFactor.One,
                        DestinationAlphaFactor = BlendFactor.Zero,
                        AlphaFunction = BlendFunction.Add,
                        ColorWriteMask = mask,
                    }
                ]
            };

            GraphicsProgram program = CreateColoredQuadProgram(blend);
            DrawColoredQuad(fb, program, vb, new Color(0.25f, 0.25f, 0.25f, 0.25f));

            Texture readback = GetReadback(target);
            MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read);
            Color pixel = map[size / 2, size / 2];
            Assert.Equal(mask.HasFlag(ColorWriteMask.Red) ? 1 : 0.25f, pixel.R, 2);
            Assert.Equal(mask.HasFlag(ColorWriteMask.Green) ? 1 : 0.25f, pixel.G, 2);
            Assert.Equal(mask.HasFlag(ColorWriteMask.Blue) ? 1 : 0.25f, pixel.B, 2);
            Assert.Equal(mask.HasFlag(ColorWriteMask.Alpha) ? 1 : 0.25f, pixel.A, 2);
            GD.Unmap(readback);
        }
    }

    private DeviceBuffer CreateFullScreenQuad()
    {
        float y = GD.IsClipSpaceYInverted ? -1.0f : 1.0f;
        ColoredVertex[] vertices =
        [
            new(new Float2(-1, 1 * y), Float4.One),
            new(new Float2(1, 1 * y), Float4.One),
            new(new Float2(-1, -1 * y), Float4.One),
            new(new Float2(1, -1 * y), Float4.One),
        ];
        uint stride = (uint)Unsafe.SizeOf<ColoredVertex>();
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(
            stride * (uint)vertices.Length, BufferUsage.StructuredBufferReadOnly, stride));
        GD.UpdateBuffer(buffer, 0, vertices);
        return buffer;
    }

    private GraphicsProgram CreateColoredQuadProgram(BlendStateDescription blend)
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "ColoredQuadRenderer.slang");
        ShaderDescription desc = new(stages)
        {
            BlendState = blend,
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

    private void DrawColoredQuad(Framebuffer fb, GraphicsProgram program, DeviceBuffer vertexStorage, Color clear)
    {
        PropertySet props = new();
        props.SetBuffer("InputVertices", vertexStorage, readOnly: true);

        Submit(cl =>
        {
            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, clear);
            cl.SetFullViewports();
            cl.SetShader(program);
            cl.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleStrip, []));
            cl.SetProperties(props);
            cl.Draw(4);
        });
    }

    [Fact]
    public void BindTexture_AcrossMultiplePasses_KeepsBinding()
    {
        (Texture target1, Framebuffer fb1) = CreateColorTarget();
        Texture target2 = RF.CreateTexture(TextureDescription.Texture2D(
            Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer fb2 = RF.CreateFramebuffer(new FramebufferDescription(null, target2));

        // Seed target2 with a known color that the texture pass samples and blits into target1.
        Color[] pink = new Color[target2.Width * target2.Height];
        Array.Fill(pink, Color.Pink);
        GD.UpdateTexture(target2, pink, 0, 0, 0, target2.Width, target2.Height, 1, 0, 0);

        GraphicsProgram texProgram = CreateSampleTexture2DProgram();
        GraphicsProgram quadProgram = CreateVertexLayoutSinkProgram();

        DeviceBuffer quadVb = RF.CreateBuffer(new BufferDescription(
            (uint)Unsafe.SizeOf<SinkVertex>() * 3, BufferUsage.VertexBuffer));
        GD.UpdateBuffer(quadVb, 0, new SinkVertex[3]);

        PropertySet texProps = new();
        texProps.SetTexture("Tex", target2, GD.PointSampler);
        texProps.SetSampler("Smp", GD.PointSampler);

        Texture s1 = RF.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));
        Texture s3 = RF.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

        Submit(cl =>
        {

            // Pass 1: sample target2 into target1.
            cl.SetFramebuffer(fb1);
            cl.ClearColorTarget(0, Color.Black);
            cl.SetFullViewports();
            cl.SetShader(texProgram);
            cl.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleList, []));
            cl.SetProperties(texProps);
            cl.Draw(3);
            cl.CopyTexture(target1, s1);

            // Pass 2: an unrelated shader that uses no textures, into target2.
            cl.SetFramebuffer(fb2);
            cl.ClearColorTarget(0, Color.Blue);
            cl.SetShader(quadProgram);
            cl.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleList, [quadVb]));
            cl.ClearProperties();
            cl.Draw(3);

            // Pass 3: the texture shader again. Its binding must survive the intervening pass.
            cl.SetFramebuffer(fb1);
            cl.ClearColorTarget(0, Color.Black);
            cl.SetShader(texProgram);
            cl.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleList, []));
            cl.SetProperties(texProps);
            cl.Draw(3);
            cl.CopyTexture(target1, s3);

        });

        // Pass 1 sampled target2 while it was pink. Pass 2 cleared target2 to blue, and pass 3
        // re-bound the same texture program and sampled target2 again - now blue. The binding
        // surviving the intervening pass is what produces a correct (blue) sample rather than
        // garbage / a stale texture.
        MappedResourceView<Color> r1 = GD.Map<Color>(s1, MapMode.Read);
        MappedResourceView<Color> r3 = GD.Map<Color>(s3, MapMode.Read);
        for (uint x = 0; x < Size; x++)
        {
            Assert.Equal(Color.Pink, r1[x, 0], ColorFuzzyComparer.Instance);
            Assert.Equal(Color.Blue, r3[x, 0], ColorFuzzyComparer.Instance);
        }
        GD.Unmap(s1);
        GD.Unmap(s3);
    }

    [Theory]
    [InlineData(2u, 0u)]
    [InlineData(5u, 3u)]
    [InlineData(8u, 7u)]
    public void Render_ToFramebufferArrayLayer(uint layerCount, uint targetLayer)
    {
        const uint size = 16;
        Texture target = RF.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, layerCount, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(
            null, [new FramebufferAttachmentDescription(target, targetLayer)]));

        Texture sampled = RF.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        Color[] pink = new Color[sampled.Width * sampled.Height];
        Array.Fill(pink, Color.Pink);
        GD.UpdateTexture(sampled, pink, 0, 0, 0, sampled.Width, sampled.Height, 1, 0, 0);

        GraphicsProgram program = CreateSampleTexture2DProgram();
        PropertySet props = new();
        props.SetTexture("Tex", sampled, GD.PointSampler);
        props.SetSampler("Smp", GD.PointSampler);

        Submit(cl =>
        {
            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, Color.Black);
            cl.SetFullViewports();
            cl.SetShader(program);
            cl.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleList, []));
            cl.SetProperties(props);
            cl.Draw(3);
        });

        Texture readback = GetReadback(target);
        MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read, targetLayer);
        for (uint x = 0; x < size; x++)
        {
            Assert.Equal(Color.Pink, map[x, 0], ColorFuzzyComparer.Instance);
        }
        GD.Unmap(readback, targetLayer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SinkVertex
    {
        public Float3 A;
        public Float4 B;
        public Float2 C;
        public Float4 D;
    }

    private GraphicsProgram CreateSampleTexture2DProgram()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "FullScreenTriSampleTexture2D.slang");
        ShaderDescription desc = new(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            ResourceLayouts =
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements =
                    [
                        new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment, 0),
                        new ResourceLayoutElementDescription("Smp", ResourceKind.Sampler, ShaderStages.Fragment, 1),
                    ]
                }
            ],
        };
        return RF.CreateGraphicsProgram(desc);
    }

    private GraphicsProgram CreateVertexLayoutSinkProgram()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "VertexLayoutTestShader.slang");
        ShaderDescription desc = new(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            VertexLayouts =
            [
                new VertexLayoutDescription(0, (uint)Unsafe.SizeOf<SinkVertex>(),
                    new VertexElementDescription("POSITION", VertexElementFormat.Float3),
                    new VertexElementDescription("COLOR0", VertexElementFormat.Float4),
                    new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2),
                    new VertexElementDescription("COLOR1", VertexElementFormat.Float4))
            ],
        };
        return RF.CreateGraphicsProgram(desc);
    }

    // Records and submits in one open frame. The frame must be open during recording because
    // property binding allocates transient memory at record time.
    private void Submit(Action<CommandBuffer> record)
    {
        Frame frame = GD.BeginFrame();
        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        record(cl);
        cl.End();
        frame.SubmitCommands(cl);
        GD.EndFrame(frame);
        GD.WaitForIdle();
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanRenderTests : RenderTests<VulkanDeviceCreator> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11RenderTests : RenderTests<D3D11DeviceCreator> { }
#endif
