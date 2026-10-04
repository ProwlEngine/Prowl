using System;

using Prowl.Graphite.Vk;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class DefectRegressionTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void TransferUpdateBuffer_IsRecordedInOrderWithCopies()
    {
        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(16, BufferUsage.VertexBuffer));
        DeviceBuffer readback = RF.CreateBuffer(new BufferDescription(16, BufferUsage.Staging));

        GD.Record(transfer =>
        {
            transfer.UpdateBuffer(source, 0, new uint[] { 1, 2, 3, 4 });
            transfer.CopyBuffer(source, 0, readback, 0, 16);
            transfer.UpdateBuffer(source, 0, new uint[] { 9, 9, 9, 9 });
        }).Wait();

        Span<uint> map = GD.Map<uint>(readback);
        uint[] result = [map[0], map[1], map[2], map[3]];
        GD.Unmap(readback);

        Assert.Equal(new uint[] { 1, 2, 3, 4 }, result);
    }

    [Fact]
    public void TransferUpdateTexture_IsRecordedInOrderWithCopies()
    {
        Texture texture = RF.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        DeviceBuffer readback = CreateTexelReadbackBuffer<uint>(2, 2);

        uint[] first = [0xFF0000FF, 0xFF0000FF, 0xFF0000FF, 0xFF0000FF];
        uint[] second = [0xFFFF0000, 0xFFFF0000, 0xFFFF0000, 0xFFFF0000];

        GD.Record(transfer =>
        {
            transfer.UpdateTexture<uint>(texture, first);
            transfer.CopyTextureToBuffer(texture, readback, 0, TextureRegion.Whole(texture));
            transfer.UpdateTexture<uint>(texture, second);
        }).Wait();

        TexelData<uint> map = ReadTexels<uint>(readback, 2, 2);
        uint topLeft = map[0, 0];
        uint bottomRight = map[1, 1];

        Assert.Equal(0xFF0000FFu, topLeft);
        Assert.Equal(0xFF0000FFu, bottomRight);
    }

    [Fact]
    public void TransferUpdateTexture_Region_IsRecorded()
    {
        Texture staging = RF.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        GD.Record(transfer =>
        {
            transfer.UpdateTexture<uint>(staging, [0x11111111, 0x22222222, 0x33333333, 0x44444444], new TextureRegion(1, 2, 0, 2, 2, 1));
        }).Wait();

        TexelData<uint> map = ReadTexture<uint>(staging);
        uint[] result = [map[1, 2], map[2, 2], map[1, 3], map[2, 3]];

        Assert.Equal(new uint[] { 0x11111111, 0x22222222, 0x33333333, 0x44444444 }, result);
    }

    [Fact]
    public void Record_RejectsRenderStateCommands()
    {
        GraphicsProgram program = null!;
        Framebuffer framebuffer = null!;

        Assert.Throws<RenderException>(() => GD.Record(cmd => cmd.SetShader(program)));
        Assert.Throws<RenderException>(() => GD.Record(cmd => cmd.SetFramebuffer(framebuffer)));
    }

    [Fact]
    public void Record_ThrowingAction_DoesNotPoisonLaterRecords()
    {
        Assert.Throws<InvalidOperationException>(() => GD.Record(_ => throw new InvalidOperationException()));

        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(16, BufferUsage.VertexBuffer));
        DeviceBuffer readback = RF.CreateBuffer(new BufferDescription(16, BufferUsage.Staging));
        GD.Record(cmd =>
        {
            cmd.UpdateBuffer(source, 0, new uint[] { 5, 6, 7, 8 });
            cmd.CopyBuffer(source, 0, readback, 0, 16);
        }).Wait();

        Span<uint> map = GD.Map<uint>(readback);
        uint first = map[0];
        GD.Unmap(readback);
        Assert.Equal(5u, first);
    }

    [Fact]
    public void Record_FireAndForget_CompletesAfterWaitForIdle()
    {
        Texture texture = RF.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        DeviceBuffer readback = CreateTexelReadbackBuffer<uint>(2, 2);
        uint[] pixels = [0xFF00FF00, 0xFF00FF00, 0xFF00FF00, 0xFF00FF00];

        GpuSubmission submission = GD.Record(cmd =>
        {
            cmd.UpdateTexture<uint>(texture, pixels);
            cmd.CopyTextureToBuffer(texture, readback, 0, TextureRegion.Whole(texture));
        });
        GD.WaitForIdle();

        Assert.True(submission.IsComplete);
        uint pixel = ReadTexels<uint>(readback, 2, 2)[1, 1];
        Assert.Equal(0xFF00FF00u, pixel);
    }

    [Fact]
    public void Record_ManyFireAndForget_ReusesPooledBuffers()
    {
        DeviceBuffer target = RF.CreateBuffer(new BufferDescription(16, BufferUsage.VertexBuffer));
        for (int i = 0; i < 64; i++)
            GD.Record(cmd => cmd.UpdateBuffer(target, 0, new uint[] { 1, 2, 3, 4 }));

        GD.WaitForIdle();
        GD.Record(cmd => cmd.UpdateBuffer(target, 0, new uint[] { 1, 2, 3, 4 })).Wait();
    }

    [Fact]
    public void TransientTexturePool_EvictsEntriesUnusedForRetentionWindow()
    {
        ExecutionTask first = GD.BeginExecution();
        RenderTexture old = GD.RentGraphTransientRenderTexture(first, new RenderTextureDescription(8, 8, PixelFormat.R8_G8_B8_A8_UNorm, false));
        GD.CompleteExecution(first);
        GD.WaitForIdle();

        for (ulong i = 0; i <= ExecutionPool<RenderTextureDescription, RenderTexture>.RetentionExecutions; i++)
            GD.CompleteExecution(GD.BeginExecution());
        GD.WaitForIdle();

        ExecutionTask last = GD.BeginExecution();
        GD.RentGraphTransientRenderTexture(last, new RenderTextureDescription(16, 16, PixelFormat.R8_G8_B8_A8_UNorm, false));
        GD.CompleteExecution(last);
        GD.WaitForIdle();

        Assert.True(old.ColorTextures[0].IsDisposed);
    }

    [Fact]
    public void RenderTargetCreation_BatchesInitIntoOneSubmit_AndClearsToZero()
    {
        VkGraphicsDevice vk = (VkGraphicsDevice)GD;
        GD.WaitForIdle();
        int before = vk.GraphicsQueueSubmitCount;

        Texture[] targets = new Texture[4];
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i] = RF.CreateTexture(TextureDescription.Texture2D(
                4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        }

        Assert.Equal(before, vk.GraphicsQueueSubmitCount);

        GD.WaitForIdle();
        Assert.Equal(before + 1, vk.GraphicsQueueSubmitCount);

        uint pixel = ReadTexture<uint>(targets[3])[2, 2];

        Assert.Equal(0u, pixel);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDefectRegressionTests : DefectRegressionTests<VulkanDeviceCreator> { }
#endif
