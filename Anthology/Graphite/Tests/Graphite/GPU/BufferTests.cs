using System;
using System.Linq;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class BufferTestBase<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void UpdateBuffer_ThenMapRead_Succeeds()
    {
        DeviceBuffer buffer = CreateBuffer(1024, BufferUsage.Staging);
        int[] data = Enumerable.Range(0, 256).Select(i => 2 * i).ToArray();
        GD.UpdateBuffer(buffer, 0, data);

        Span<int> view = GD.Map<int>(buffer);
        for (int i = 0; i < view.Length; i++)
        {
            Assert.Equal(i * 2, view[i]);
        }
    }

    [Fact]
    public void Staging_Map_WriteThenRead()
    {
        DeviceBuffer buffer = CreateBuffer(256, BufferUsage.Staging);
        Span<byte> map = GD.Map(buffer);
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = (byte)i;
        }
        GD.Unmap(buffer);

        map = GD.Map(buffer);
        for (int i = 0; i < map.Length; i++)
        {
            Assert.Equal((byte)i, map[i]);
        }
        GD.Unmap(buffer);
    }

    [Fact]
    public void Map_WrongFlags_Throws()
    {
        DeviceBuffer buffer = CreateBuffer(1024, BufferUsage.VertexBuffer);
        Assert.Throws<RenderException>(() => GD.Map(buffer));
    }

    [Fact]
    public void CopyBuffer_Chain_Succeeds()
    {
        DeviceBuffer src = CreateBuffer(1024, BufferUsage.Staging);
        int[] data = Enumerable.Range(0, 256).Select(i => 2 * i).ToArray();
        GD.UpdateBuffer(src, 0, data);

        DeviceBuffer finalDst = CreateBuffer(1024, BufferUsage.Staging);

        for (int chainLength = 2; chainLength <= 10; chainLength += 4)
        {
            DeviceBuffer[] dsts = Enumerable.Range(0, chainLength)
                .Select(i => RF.CreateBuffer(new BufferDescription(1024, BufferUsage.UniformBuffer)))
                .ToArray();

            GD.RunTestGraph((context, copyCL) =>
            {
                copyCL.CopyBuffer(src, 0, dsts[0], 0, src.SizeInBytes);
                for (int i = 0; i < chainLength - 1; i++)
                {
                    copyCL.CopyBuffer(dsts[i], 0, dsts[i + 1], 0, src.SizeInBytes);
                }
                copyCL.CopyBuffer(dsts[dsts.Length - 1], 0, finalDst, 0, src.SizeInBytes);
            });
            GD.WaitForIdle();

            Span<int> view = GD.Map<int>(finalDst);
            for (int i = 0; i < view.Length; i++)
            {
                Assert.Equal(i * 2, view[i]);
            }
            GD.Unmap(finalDst);
        }
    }

    [Fact]
    public unsafe void Map_MultipleTimes_Succeeds()
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(1024, BufferUsage.Staging));
        Span<byte> map = GD.Map(buffer);
        fixed (byte* dataPtr = map)
        {
            byte* first = dataPtr;
            map = GD.Map(buffer);
            fixed (byte* second = map)
                Assert.True(first == second);
            map = GD.Map(buffer);
            fixed (byte* third = map)
                Assert.True(first == third);
        }
        GD.Unmap(buffer);
        GD.Unmap(buffer);
        GD.Unmap(buffer);
    }

    [Theory]
    [InlineData(
        60, BufferUsage.VertexBuffer, 1,
        70, BufferUsage.VertexBuffer, 13,
        11)]
    [InlineData(
        60, BufferUsage.Staging, 1,
        70, BufferUsage.VertexBuffer, 13,
        11)]
    [InlineData(
        60, BufferUsage.VertexBuffer, 1,
        70, BufferUsage.Staging, 13,
        11)]
    [InlineData(
        60, BufferUsage.Staging, 1,
        70, BufferUsage.Staging, 13,
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

    [Theory]
    [InlineData(BufferUsage.VertexBuffer, 13, 5, 1)]
    [InlineData(BufferUsage.Staging, 13, 5, 1)]
    public void CommandBuffer_UpdateNonStaging_Unaligned(BufferUsage usage, uint bufferSize, uint dataSize, uint offset)
    {
        DeviceBuffer buffer = CreateBuffer(bufferSize, usage);
        byte[] data = Enumerable.Range(0, (int)dataSize).Select(i => (byte)i).ToArray();
        GD.RunTestGraph((context, cl) =>
        {
            cl.UpdateBuffer(buffer, offset, data);
        });
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(buffer);
        Span<byte> readView = GD.Map<byte>(readback);
        for (uint i = 0; i < dataSize; i++)
        {
            byte expected = data[i];
            byte actual = readView[(int)(i + offset)];
            Assert.Equal(expected, actual);
        }
        GD.Unmap(readback);
    }

    [Theory]
    [InlineData(BufferUsage.UniformBuffer | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.UniformBuffer)]
    [InlineData(BufferUsage.Staging)]
    public void UpdateUniform_Offset_GraphicsDevice(BufferUsage usage)
    {
        DeviceBuffer buffer = CreateBuffer(128, usage);
        Float4x4 mat1 = new(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        GD.UpdateBuffer(buffer, 0, mat1);
        Float4x4 mat2 = new(2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2);
        GD.UpdateBuffer(buffer, 64, mat2);

        DeviceBuffer readback = GetReadback(buffer);
        Span<Float4x4> readView = GD.Map<Float4x4>(readback);
        Assert.Equal(mat1, readView[0]);
        Assert.Equal(mat2, readView[1]);
        GD.Unmap(readback);
    }

    [Theory]
    [InlineData(BufferUsage.UniformBuffer | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.UniformBuffer)]
    [InlineData(BufferUsage.Staging)]
    public void UpdateUniform_Offset_CommandBuffer(BufferUsage usage)
    {
        DeviceBuffer buffer = CreateBuffer(128, usage);
        Float4x4 mat1 = new(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        Float4x4 mat2 = new(2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2);
        GD.RunTestGraph((context, cl) =>
        {
            cl.UpdateBuffer(buffer, 0, mat1);
            cl.UpdateBuffer(buffer, 64, mat2);
        });
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(buffer);
        Span<Float4x4> readView = GD.Map<Float4x4>(readback);
        Assert.Equal(mat1, readView[0]);
        Assert.Equal(mat2, readView[1]);
        GD.Unmap(readback);
    }

    [Theory]
    [InlineData(BufferUsage.VertexBuffer)]
    [InlineData(BufferUsage.Staging)]
    public void CopyBuffer_ZeroSize(BufferUsage usage)
    {
        DeviceBuffer src = CreateBuffer(1024, usage);
        DeviceBuffer dst = CreateBuffer(1024, usage);

        byte[] initialDataSrc = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        byte[] initialDataDst = Enumerable.Range(0, 1024).Select(i => (byte)(i * 2)).ToArray();
        GD.UpdateBuffer(src, 0, initialDataSrc);
        GD.UpdateBuffer(dst, 0, initialDataDst);

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyBuffer(src, 0, dst, 0, 0);
        });
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(dst);

        Span<byte> readMap = GD.Map<byte>(readback);
        for (int i = 0; i < 1024; i++)
        {
            Assert.Equal((byte)(i * 2), readMap[i]);
        }
        GD.Unmap(readback);
    }

    [Theory]
    [InlineData(BufferUsage.VertexBuffer, false)]
    [InlineData(BufferUsage.VertexBuffer, true)]
    [InlineData(BufferUsage.Staging, false)]
    [InlineData(BufferUsage.Staging, true)]
    public unsafe void UpdateBuffer_ZeroSize(BufferUsage usage, bool useCommandBufferUpdate)
    {
        DeviceBuffer buffer = CreateBuffer(1024, usage);

        byte[] initialData = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        byte[] otherData = Enumerable.Range(0, 1024).Select(i => (byte)(i + 10)).ToArray();
        GD.UpdateBuffer(buffer, 0, initialData);

        if (useCommandBufferUpdate)
        {
            GD.RunTestGraph((context, cl) =>
            {
                fixed (byte* dataPtr = otherData)
                {
                    cl.UpdateBuffer(buffer, 0, (IntPtr)dataPtr, 0);
                }
            });
            GD.WaitForIdle();
        }
        else
        {
            fixed (byte* dataPtr = otherData)
            {
                GD.UpdateBuffer(buffer, 0, (IntPtr)dataPtr, 0);
            }
        }

        DeviceBuffer readback = GetReadback(buffer);

        Span<byte> readMap = GD.Map<byte>(readback);
        for (int i = 0; i < 1024; i++)
        {
            Assert.Equal((byte)i, readMap[i]);
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
