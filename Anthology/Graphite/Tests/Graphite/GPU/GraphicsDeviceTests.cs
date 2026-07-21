using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Testbed for the GraphicsDevice + graph-execution APIs that have no upstream Veldrid equivalent:
// device identity/features, the BeginExecution/CompleteExecution lifecycle, the
// MaxExecutingTasks throttle, transient ring allocation (including the hard cap), fences, and
// ShaderProgram lifetime.
public abstract class GraphicsDeviceTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Device_ReportsBackendAndIdentity()
    {
        Assert.NotNull(GD.ResourceFactory);
        Assert.Equal(GD.BackendType, GD.ResourceFactory.BackendType);
        Assert.False(string.IsNullOrEmpty(GD.DeviceName));
        Assert.NotNull(GD.Features);
        Assert.True(GD.MaxExecutingTasks >= 1);
    }

    [Fact]
    public void BeginExecution_AssignsMonotonicIdAndValidRingSlot()
    {
        ulong previousId = 0;
        for (int i = 0; i < 3; i++)
        {
            ExecutionTask task = GD.BeginExecution();
            Assert.True(task.Id > previousId);
            Assert.True(task.RingSlot < GD.MaxExecutingTasks);
            Assert.Contains(task, GD.ActiveExecutions);
            previousId = task.Id;

            GD.CompleteExecution(task);
            GD.WaitForExecution(task);
        }
    }

    [Fact]
    public void CompleteExecution_SignalsCompletionFenceAndAdvancesLastCompleted()
    {
        ExecutionTask task = GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            context.SubmitCommandBuffer(cl);
        });
        ulong id = task.Id;

        GD.WaitForExecution(task);

        Assert.True(GD.IsExecutionComplete(task));
        Assert.True(task.CompletionFence.Signaled);
        Assert.True(GD.LastCompletedExecutionId >= id);
    }

    [Fact]
    public void Executions_BeyondRingDepth_DoNotDeadlock()
    {
        // Dispatching more executions than the ring depth forces BeginExecution to throttle on
        // the oldest slot. This must make progress rather than deadlock.
        ExecutionTask last = null;
        uint executionCount = GD.MaxExecutingTasks * 3 + 1;
        for (uint i = 0; i < executionCount; i++)
        {
            last = GD.RunTestGraph(context =>
            {
                CommandBuffer cl = context.GetCommandBuffer();
                context.SubmitCommandBuffer(cl);
            });
        }

        GD.WaitForIdle();
        Assert.True(GD.IsExecutionComplete(last));
        Assert.Equal(0u, GD.ExecutingTasks);
    }

    [Fact]
    public void AllocateTransient_WithinExecution_ReturnsDistinctRanges()
    {
        DeviceBufferRange a = default, b = default;
        ExecutionTask task = GD.RunTestGraph(context =>
        {
            a = context.AllocateTransient(256);
            b = context.AllocateTransient(256);
        });

        Assert.NotNull(a.Buffer);
        Assert.NotNull(b.Buffer);
        Assert.True(a.SizeInBytes >= 256);
        Assert.True(b.SizeInBytes >= 256);
        // Two allocations in the same execution must not overlap at the same offset in the same buffer.
        Assert.False(a.Buffer == b.Buffer && a.Offset == b.Offset);

        GD.WaitForExecution(task);
    }

    [Fact]
    public void AllocateTransient_ExceedingHardCap_Throws()
    {
        GraphicsDeviceOptions options = new(true)
        {
            TransientBufferInitialSize = 4096,
            TransientBufferSoftCapBytes = 4096,
            TransientBufferHardCapBytes = 8192,
        };

        using GraphicsDevice device = CreateIsolatedDevice(options);

        Assert.Throws<RenderException>(() =>
            device.RunTestGraph(context => context.AllocateTransient(options.TransientBufferHardCapBytes + 1)));

        device.WaitForIdle();
    }

    private GraphicsDevice CreateIsolatedDevice(GraphicsDeviceOptions options) => GD.BackendType switch
    {
        GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(options),
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
