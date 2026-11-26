using System;
using System.Linq;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class BufferTestBase<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Theory]
    [InlineData(
        60, BufferUsage.VertexBuffer, 1,
        70, BufferUsage.VertexBuffer, 13,
        11)]
    [InlineData(
        5, BufferUsage.VertexBuffer, 3,
        10, BufferUsage.VertexBuffer, 7,
        2)]
    public void Copy_UnalignedRegion(
        uint srcBufferSize, BufferUsage srcUsage, uint srcCopyOffset,
        uint dstBufferSize, BufferUsage dstUsage, uint dstCopyOffset,
        uint copySize)
    {
        DeviceBuffer src = CreateBuffer(srcBufferSize, srcUsage);
        DeviceBuffer dst = CreateBuffer(dstBufferSize, dstUsage);

        byte[] data = Enumerable.Range(0, (int)srcBufferSize).Select(i => (byte)i).ToArray();
        GD.UpdateBuffer(src, 0, data);

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyBuffer(src, srcCopyOffset, dst, dstCopyOffset, copySize);
        });
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(dst);

        Span<byte> readView = GD.Map<byte>(readback);
        for (uint i = 0; i < copySize; i++)
        {
            byte expected = data[i + srcCopyOffset];
            byte actual = readView[(int)(i + dstCopyOffset)];
            Assert.Equal(expected, actual);
        }
        GD.Unmap(readback);
    }

    private DeviceBuffer CreateBuffer(uint size, BufferUsage usage)
    {
        return RF.CreateBuffer(new BufferDescription(size, usage));
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanBufferTests : BufferTestBase<VulkanDeviceCreator> { }
#endif
