using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Compute coverage on the ComputeProgram + PropertySet + Frame API: a compute pass feeding a
// graphics pass through a structured buffer, a compute-written storage texture blitted to a
// target, 2D-array and 3D storage textures, and indirect dispatch.
public abstract class ComputeTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ColoredVertex
    {
        public Float4 Color;
        public Float2 Position;
        private Float2 _padding0;
    }

    [SkippableFact]
    public void ComputeGeneratedVertices_RenderAllRed()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const uint size = 64;
        Texture output = RF.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, output));

        uint stride = (uint)Unsafe.SizeOf<ColoredVertex>();
        DeviceBuffer vertices = RF.CreateBuffer(new BufferDescription(
            stride * 4, BufferUsage.StructuredBufferReadWrite, stride));

        ComputeProgram compute = CreateCompute("ComputeColoredQuadGenerator.slang",
            new ResourceLayoutElementDescription("OutputVertices", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 0));

        GraphicsProgram graphics = CreateColoredQuadRenderer();

        PropertySet computeProps = new();
        computeProps.SetBuffer("OutputVertices", vertices, readOnly: false);

        PropertySet graphicsProps = new();
        graphicsProps.SetBuffer("InputVertices", vertices, readOnly: true);

        Submit(cl =>
        {
            cl.SetComputeShader(compute);
            cl.SetProperties(computeProps);
            cl.Dispatch(1, 1, 1);

            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, Color.Black);
            cl.SetFullViewports();
            cl.SetShader(graphics);
            cl.SetVertexSource(new TestVertexSource(PrimitiveTopology.TriangleStrip, []));
            cl.SetProperties(graphicsProps);
            cl.Draw(4);
        });

        Texture readback = GetReadback(output);
        MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read);
        Assert.Equal(Color.Red, map[size / 2, size / 2], ColorFuzzyComparer.Instance);
        GD.Unmap(readback);
    }

    [SkippableFact]
    public void ComputeGeneratedTexture_BlitsExpectedTexels()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const uint width = 4;
        const uint height = 1;
        Texture computeOutput = RF.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled | TextureUsage.Storage));

        ComputeProgram compute = CreateCompute("ComputeTextureGenerator.slang",
            new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0));

        PropertySet computeProps = new();
        computeProps.SetTexture("ComputeOutput", computeOutput);

        Submit(cl =>
        {
            cl.SetComputeShader(compute);
            cl.SetProperties(computeProps);
            cl.Dispatch(1, 1, 1);
        });

        // The compute kernel writes pure RGBA into the 4 texels; read the storage texture back
        // directly. Explicit colors are used because the shader writes (0,1,0), which differs from
        // Prowl's named Color.Green (HTML green).
        Texture readback = GetReadback(computeOutput);
        MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read);
        Assert.Equal(new Color(1f, 0f, 0f, 1f), map[0, 0], ColorFuzzyComparer.Instance);
        Assert.Equal(new Color(0f, 1f, 0f, 1f), map[1, 0], ColorFuzzyComparer.Instance);
        Assert.Equal(new Color(0f, 0f, 1f, 1f), map[2, 0], ColorFuzzyComparer.Instance);
        Assert.Equal(new Color(1f, 1f, 1f, 1f), map[3, 0], ColorFuzzyComparer.Instance);
        GD.Unmap(readback);
    }

    [SkippableTheory]
    [InlineData(2u)]
    [InlineData(6u)]
    public void ComputeWritesArrayLayers(uint layers)
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const uint texSize = 32;
        Texture computeOutput = RF.CreateTexture(TextureDescription.Texture2D(
            texSize, texSize, 1, layers, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled | TextureUsage.Storage));

        ComputeProgram compute = CreateCompute("ComputeImage2DArrayGenerator.slang",
            new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0));

        PropertySet props = new();
        props.SetTexture("ComputeOutput", computeOutput);

        Submit(cl =>
        {
            cl.SetComputeShader(compute);
            cl.SetProperties(props);
            cl.Dispatch(texSize / 32, texSize / 32, layers);
        });

        // sideColorStep = floor(1 / layers) is 0 for layers >= 2, so every texel is written as 0.
        // The point of the test is that per-array-layer storage writes happen at all.
        Texture readback = GetReadback(computeOutput);
        for (uint layer = 0; layer < layers; layer++)
        {
            uint subresource = readback.CalculateSubresource(0, layer);
            MappedResourceView<byte> map = GD.Map<byte>(readback, MapMode.Read, subresource);
            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    Assert.Equal(0, map[x, y]);
                }
            }
            GD.Unmap(readback, subresource);
        }
    }

    [SkippableFact]
    public void ComputeFills3DTexture()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const float fill = 42.42f;
        const uint size = 32;
        Texture texture = RF.CreateTexture(TextureDescription.Texture3D(
            size, size, size, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled | TextureUsage.Storage));

        ComputeProgram compute = CreateCompute("ComputeFill3DTexture.slang",
            new ResourceLayoutElementDescription("FillValueBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
            {
                UniformFields = [new UniformBlockField("FillValue", 0, sizeof(float), UniformScalarType.Float1)]
            },
            new ResourceLayoutElementDescription("TextureToFill", ResourceKind.TextureReadWrite, ShaderStages.Compute, 1));

        PropertySet props = new();
        props.SetFloat("FillValue", fill);
        props.SetTexture("TextureToFill", texture);

        Submit(cl =>
        {
            cl.SetComputeShader(compute);
            cl.SetProperties(props);
            cl.Dispatch(size / 16, size / 16, size);
        });

        Texture readback = GetReadback(texture);
        MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read);
        for (uint z = 0; z < size; z++)
        {
            float v = fill * (z + 1);
            Color expected = new(v, v, v, v);
            Assert.Equal(expected, map[(int)(size / 2), (int)(size / 2), (int)z], ColorFuzzyComparer.Instance);
        }
        GD.Unmap(readback);
    }

    [SkippableFact]
    public void DispatchIndirect_RunsKernel()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const uint side = 16;
        const uint count = side * side;

        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        float[] initial = new float[count];
        for (int i = 0; i < count; i++) initial[i] = i;
        GD.UpdateBuffer(source, 0, initial);

        DeviceBuffer indirect = RF.CreateBuffer(new BufferDescription(
            (uint)Unsafe.SizeOf<IndirectDispatchArguments>(), BufferUsage.IndirectBuffer));
        GD.UpdateBuffer(indirect, 0, new IndirectDispatchArguments { GroupCountX = 1, GroupCountY = 1, GroupCountZ = 1 });

        ComputeProgram compute = CreateCompute("BasicComputeTest.slang",
            new ResourceLayoutElementDescription("Params", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
            {
                UniformFields =
                [
                    new UniformBlockField("Width", 0, sizeof(uint), UniformScalarType.Int1),
                    new UniformBlockField("Height", sizeof(uint), sizeof(uint), UniformScalarType.Int1),
                ]
            },
            new ResourceLayoutElementDescription("Source", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1),
            new ResourceLayoutElementDescription("Destination", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 2));

        PropertySet props = new();
        props.SetInt("Width", (int)side);
        props.SetInt("Height", (int)side);
        props.SetBuffer("Source", source, readOnly: false);
        props.SetBuffer("Destination", destination, readOnly: false);

        Submit(cl =>
        {
            cl.SetComputeShader(compute);
            cl.SetProperties(props);
            cl.DispatchIndirect(indirect, 0);
        });

        DeviceBuffer readback = GetReadback(destination);
        MappedResourceView<float> map = GD.Map<float>(readback, MapMode.Read);
        for (int i = 0; i < count; i++) Assert.Equal(i, map[i]);
        GD.Unmap(readback);
    }

    private ComputeProgram CreateCompute(string module, params ResourceLayoutElementDescription[] elements)
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, module);
        ResourceLayoutDescription[] layouts = [new ResourceLayoutDescription { Set = 0, Elements = elements }];
        return RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 16, 16, 1));
    }

    private GraphicsProgram CreateColoredQuadRenderer()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "ColoredQuadRenderer.slang");
        return RF.CreateGraphicsProgram(new ShaderDescription(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            ResourceLayouts =
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements = [new ResourceLayoutElementDescription("InputVertices", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex, 0)]
                }
            ],
        });
    }

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
public class VulkanComputeTests : ComputeTests<VulkanDeviceCreator> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11ComputeTests : ComputeTests<D3D11DeviceCreator> { }
#endif
#if TEST_OPENGL
[Trait("Backend", "OpenGL")]
[Collection("GPU Tests")]
public class OpenGLComputeTests : ComputeTests<OpenGLDeviceCreator> { }
#endif
#if TEST_OPENGLES
[Trait("Backend", "OpenGLES")]
[Collection("GPU Tests")]
public class OpenGLESComputeTests : ComputeTests<OpenGLESDeviceCreator> { }
#endif
