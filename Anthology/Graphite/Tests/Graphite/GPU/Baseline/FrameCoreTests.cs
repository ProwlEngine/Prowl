using Xunit;

namespace Prowl.Graphite.Tests;

// Core-path coverage of the frame/synchronization API that has no upstream equivalent:
// the BeginFrame/EndFrame lifecycle, transient ring allocation, fences, and disposal.
public abstract class FrameCoreTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Frame_CompletesAndSignalsFence()
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
    }

    [Fact]
    public void AllocateTransient_WithinFrame_ReturnsUsableRange()
    {
        Frame frame = GD.BeginFrame();

        DeviceBufferRange range = GD.AllocateTransient(256);

        Assert.NotNull(range.Buffer);
        Assert.True(range.SizeInBytes >= 256);

        GD.EndFrame(frame);
        GD.WaitForFrame(frame);
    }

    [Fact]
    public void AllocateTransient_WithoutFrame_Throws()
    {
        Assert.Throws<RenderException>(() => GD.AllocateTransient(256));
    }

    [Fact]
    public void Fence_ResetClearsSignaledState()
    {
        Fence fence = RF.CreateFence(signaled: true);
        Assert.True(fence.Signaled);

        GD.ResetFence(fence);
        Assert.False(fence.Signaled);
    }

    [Fact]
    public void Dispose_MarksResourceDisposed()
    {
        // Created on the inner factory so the test base does not dispose it a second time.
        DeviceBuffer buffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(64, BufferUsage.VertexBuffer));
        Assert.False(buffer.IsDisposed);

        buffer.Dispose();
        Assert.True(buffer.IsDisposed);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanFrameCoreTests : FrameCoreTests<VulkanDeviceCreator> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11FrameCoreTests : FrameCoreTests<D3D11DeviceCreator> { }
#endif
