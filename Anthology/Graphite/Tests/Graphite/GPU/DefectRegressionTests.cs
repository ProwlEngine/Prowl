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

        TransferCommandBuffer transfer = RF.CreateTransferCommandBuffer();
        transfer.Begin();
        transfer.UpdateBuffer(source, 0, new uint[] { 1, 2, 3, 4 });
        transfer.CopyBuffer(source, 0, readback, 0, 16);
        transfer.UpdateBuffer(source, 0, new uint[] { 9, 9, 9, 9 });
        transfer.End();
        GD.SubmitAndWait(transfer);

        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        uint[] result = [map[0], map[1], map[2], map[3]];
        GD.Unmap(readback);

        Assert.Equal(new uint[] { 1, 2, 3, 4 }, result);
    }

    [Fact]
    public void TransferUpdateTexture_IsRecordedInOrderWithCopies()
    {
        Texture texture = RF.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        Texture readback = RF.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));

        uint[] first = [0xFF0000FF, 0xFF0000FF, 0xFF0000FF, 0xFF0000FF];
        uint[] second = [0xFFFF0000, 0xFFFF0000, 0xFFFF0000, 0xFFFF0000];

        TransferCommandBuffer transfer = RF.CreateTransferCommandBuffer();
        transfer.Begin();
        transfer.UpdateTexture<uint>(texture, first);
        transfer.CopyTexture(texture, readback);
        transfer.UpdateTexture<uint>(texture, second);
        transfer.End();
        GD.SubmitAndWait(transfer);

        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        uint topLeft = map[0, 0];
        uint bottomRight = map[1, 1];
        GD.Unmap(readback);

        Assert.Equal(0xFF0000FFu, topLeft);
        Assert.Equal(0xFF0000FFu, bottomRight);
    }

    [Fact]
    public void TransferUpdateTexture_IntoStagingTexture_IsRecorded()
    {
        Texture staging = RF.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));

        TransferCommandBuffer transfer = RF.CreateTransferCommandBuffer();
        transfer.Begin();
        transfer.UpdateTexture<uint>(staging, [0x11111111, 0x22222222, 0x33333333, 0x44444444], 1, 2, 0, 2, 2, 1, 0, 0);
        transfer.End();
        GD.SubmitAndWait(transfer);

        MappedResourceView<uint> map = GD.Map<uint>(staging, MapMode.Read);
        uint[] result = [map[1, 2], map[2, 2], map[1, 3], map[2, 3]];
        GD.Unmap(staging);

        Assert.Equal(new uint[] { 0x11111111, 0x22222222, 0x33333333, 0x44444444 }, result);
    }

    [Fact]
    public void TransientTexturePool_EvictsEntriesUnusedForRetentionWindow()
    {
        ExecutionTask first = GD.BeginExecution();
        RenderTexture old = GD.RentTransientRenderTexture(first, new RenderTextureDescription(8, 8, PixelFormat.R8_G8_B8_A8_UNorm, false));
        GD.CompleteExecution(first);
        GD.WaitForIdle();

        for (ulong i = 0; i <= TransientTexturePool.RetentionExecutions; i++)
            GD.CompleteExecution(GD.BeginExecution());
        GD.WaitForIdle();

        ExecutionTask last = GD.BeginExecution();
        GD.RentTransientRenderTexture(last, new RenderTextureDescription(16, 16, PixelFormat.R8_G8_B8_A8_UNorm, false));
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

        Texture readback = GetReadback(targets[3]);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        uint pixel = map[2, 2];
        GD.Unmap(readback);

        Assert.Equal(0u, pixel);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDefectRegressionTests : DefectRegressionTests<VulkanDeviceCreator> { }
#endif
