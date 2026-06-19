using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Testbed for the GraphicsDevice + Frame APIs that have no upstream Veldrid equivalent: device
// identity/features, the BeginFrame/EndFrame ring lifecycle, frames-in-flight throttling,
// transient ring allocation (including the hard cap), fences, and ShaderProgram lifetime.
public abstract class GraphicsDeviceTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Device_ReportsBackendAndIdentity()
    {
        Assert.NotNull(GD.ResourceFactory);
        Assert.Equal(GD.BackendType, GD.ResourceFactory.BackendType);
        Assert.False(string.IsNullOrEmpty(GD.DeviceName));
        Assert.NotNull(GD.Features);
        Assert.True(GD.MaxFramesInFlight >= 1);
    }

    [Fact]
    public void BeginFrame_AssignsMonotonicIdAndValidRingSlot()
    {
        ulong previousId = 0;
        for (int i = 0; i < 3; i++)
        {
            Frame frame = GD.BeginFrame();
            Assert.True(frame.FrameId > previousId);
            Assert.True(frame.RingSlot < GD.MaxFramesInFlight);
            Assert.Same(frame, GD.CurrentFrame);
            previousId = frame.FrameId;

            GD.EndFrame(frame);
            GD.WaitForFrame(frame);
        }
    }

    [Fact]
    public void EndFrame_SignalsCompletionFenceAndAdvancesLastCompleted()
    {
        Frame frame = GD.BeginFrame();
        ulong id = frame.FrameId;

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.End();
        frame.SubmitCommands(cl);

        GD.EndFrame(frame);
        GD.WaitForFrame(id);

        Assert.True(GD.IsFrameComplete(id));
        Assert.True(frame.CompletionFence.Signaled);
        Assert.True(GD.LastCompletedFrameId >= id);
    }

    [Fact]
    public void Frames_BeyondRingDepth_DoNotDeadlock()
    {
        // Submitting more frames than the ring depth forces BeginFrame to throttle on the oldest
        // slot. This must make progress rather than deadlock.
        ulong lastId = 0;
        uint frameCount = GD.MaxFramesInFlight * 3 + 1;
        for (uint i = 0; i < frameCount; i++)
        {
            Frame frame = GD.BeginFrame();
            CommandBuffer cl = RF.CreateCommandBuffer();
            cl.Begin();
            cl.End();
            frame.SubmitCommands(cl);
            lastId = frame.FrameId;
            GD.EndFrame(frame);
        }

        GD.WaitForIdle();
        Assert.True(GD.IsFrameComplete(lastId));
        Assert.Equal(0u, GD.FramesInFlight);
    }

    [Fact]
    public void AllocateTransient_WithinFrame_ReturnsDistinctRanges()
    {
        Frame frame = GD.BeginFrame();

        DeviceBufferRange a = GD.AllocateTransient(256);
        DeviceBufferRange b = GD.AllocateTransient(256);

        Assert.NotNull(a.Buffer);
        Assert.NotNull(b.Buffer);
        Assert.True(a.SizeInBytes >= 256);
        Assert.True(b.SizeInBytes >= 256);
        // Two allocations in the same frame must not overlap at the same offset in the same buffer.
        Assert.False(a.Buffer == b.Buffer && a.Offset == b.Offset);

        GD.EndFrame(frame);
        GD.WaitForFrame(frame);
    }

    [Fact]
    public void AllocateTransient_WithoutActiveFrame_Throws()
    {
        Assert.Throws<RenderException>(() => GD.AllocateTransient(256));
    }

    [SkippableFact]
    public void AllocateTransient_ExceedingHardCap_Throws()
    {
        // GL/GLES share their context with the suite's device, so a second headless device is not
        // available; the cap is exercised on the backends that can stand up an isolated device.
        Skip.If(GD.BackendType is GraphicsBackend.OpenGL or GraphicsBackend.OpenGLES,
            "Isolated headless device is unavailable for GL/GLES in tests.");

        GraphicsDeviceOptions options = new(true)
        {
            TransientBufferInitialSize = 4096,
            TransientBufferSoftCapBytes = 4096,
            TransientBufferHardCapBytes = 8192,
        };

        using GraphicsDevice device = CreateIsolatedDevice(options);
        Frame frame = device.BeginFrame();
        try
        {
            Assert.Throws<RenderException>(() => device.AllocateTransient(options.TransientBufferHardCapBytes + 1));
        }
        finally
        {
            device.EndFrame(frame);
            device.WaitForIdle();
        }
    }

    private GraphicsDevice CreateIsolatedDevice(GraphicsDeviceOptions options) => GD.BackendType switch
    {
        GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(options),
#if TEST_D3D11
        GraphicsBackend.Direct3D11 => GraphicsDevice.CreateD3D11(options),
#endif
        _ => throw new NotSupportedException(),
    };

    [Fact]
    public void Fence_CreateAndReset_TracksSignaledState()
    {
        Fence signaled = RF.CreateFence(signaled: true);
        Assert.True(signaled.Signaled);
        GD.ResetFence(signaled);
        Assert.False(signaled.Signaled);

        Fence unsignaled = RF.CreateFence(signaled: false);
        Assert.False(unsignaled.Signaled);
    }

    [Fact]
    public void GraphicsProgram_CreateAndDispose_TracksDisposal()
    {
        GraphicsProgram program = GD.ResourceFactory.CreateGraphicsProgram(CreateSinkShaderDescription());
        Assert.False(program.IsDisposed);
        Assert.NotNull(program.ResourceLayouts);

        program.Dispose();
        Assert.True(program.IsDisposed);
    }

    [SkippableFact]
    public void ComputeProgram_CreateAndDispose_TracksDisposal()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "BasicComputeTest.slang");
        ComputeDescription desc = new(stage,
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements =
                    [
                        new ResourceLayoutElementDescription("Params", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                        {
                            UniformFields =
                            [
                                new UniformBlockField("Width", 0, sizeof(uint), UniformScalarType.Int1),
                                new UniformBlockField("Height", sizeof(uint), sizeof(uint), UniformScalarType.Int1),
                            ]
                        },
                        new ResourceLayoutElementDescription("Source", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1),
                        new ResourceLayoutElementDescription("Destination", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 2),
                    ]
                }
            ], 16, 16, 1);

        ComputeProgram program = GD.ResourceFactory.CreateComputeProgram(desc);
        Assert.False(program.IsDisposed);
        program.Dispose();
        Assert.True(program.IsDisposed);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SinkVertex
    {
        public Float3 A;
        public Float4 B;
        public Float2 C;
        public Float4 D;
    }

    private ShaderDescription CreateSinkShaderDescription()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "VertexLayoutTestShader.slang");
        return new ShaderDescription(stages)
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
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanGraphicsDeviceTests : GraphicsDeviceTests<VulkanDeviceCreator> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11GraphicsDeviceTests : GraphicsDeviceTests<D3D11DeviceCreator> { }
#endif
#if TEST_OPENGL
[Trait("Backend", "OpenGL")]
[Collection("GPU Tests")]
public class OpenGLGraphicsDeviceTests : GraphicsDeviceTests<OpenGLDeviceCreator> { }
#endif
#if TEST_OPENGLES
[Trait("Backend", "OpenGLES")]
[Collection("GPU Tests")]
public class OpenGLESGraphicsDeviceTests : GraphicsDeviceTests<OpenGLESDeviceCreator> { }
#endif
