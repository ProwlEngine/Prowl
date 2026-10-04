using System;
using System.Linq;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class BufferTestBase<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void CreateBuffer_Succeeds()
    {
        uint expectedSize = 64;
        BufferUsage expectedUsage = BufferUsage.Dynamic | BufferUsage.UniformBuffer;

        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(expectedSize, expectedUsage));

        Assert.Equal(expectedUsage, buffer.Usage);
        Assert.Equal(expectedSize, buffer.SizeInBytes);
    }

    [Fact]
    public void UpdateBuffer_NonDynamic_Succeeds()
    {
        DeviceBuffer buffer = CreateBuffer(64, BufferUsage.VertexBuffer);
        GD.UpdateBuffer(buffer, 0, Float4x4.Identity);
        GD.WaitForIdle();
    }

    [Fact]
    public void UpdateBuffer_Span_Succeeds()
    {
        DeviceBuffer buffer = CreateBuffer(64, BufferUsage.VertexBuffer);
        float[] data = new float[16];
        GD.UpdateBuffer(buffer, 0, (ReadOnlySpan<float>)data);
        GD.WaitForIdle();
    }

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
    public void Staging_MapGeneric_WriteThenRead()
    {
        DeviceBuffer buffer = CreateBuffer(1024, BufferUsage.Staging);
        Span<int> view = GD.Map<int>(buffer);
        Assert.Equal(256, view.Length);
        for (int i = 0; i < view.Length; i++)
        {
            view[i] = i * 10;
        }
        GD.Unmap(buffer);

        view = GD.Map<int>(buffer);
        Assert.Equal(256, view.Length);
        for (int i = 0; i < view.Length; i++)
        {
            view[i] = 1 * 10;
        }
        GD.Unmap(buffer);
    }

    [Fact]
    public void MapGeneric_Length_MatchesBufferSize()
    {
        DeviceBuffer buffer = CreateBuffer(1024, BufferUsage.Staging);
        Span<byte> view = GD.Map<byte>(buffer);
        Assert.Equal(1024, view.Length);
        GD.Unmap(buffer);
    }

    [Fact]
    public void Map_WrongFlags_Throws()
    {
        DeviceBuffer buffer = CreateBuffer(1024, BufferUsage.VertexBuffer);
        Assert.Throws<RenderException>(() => GD.Map(buffer));
    }

    [Fact]
    public void CopyBuffer_Succeeds()
    {
        DeviceBuffer src = CreateBuffer(1024, BufferUsage.Staging);
        int[] data = Enumerable.Range(0, 256).Select(i => 2 * i).ToArray();
        GD.UpdateBuffer(src, 0, data);

        DeviceBuffer dst = CreateBuffer(1024, BufferUsage.Staging);

        GD.RunTestGraph(context =>
        {
            CommandBuffer copyCL = context.GetCommandBuffer();
            copyCL.CopyBuffer(src, 0, dst, 0, src.SizeInBytes);
            context.SubmitCommandBuffer(copyCL);
        });
        GD.WaitForIdle();
        src.Dispose();

        Span<int> view = GD.Map<int>(dst);
        for (int i = 0; i < view.Length; i++)
        {
            Assert.Equal(i * 2, view[i]);
        }
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

            GD.RunTestGraph(context =>
            {
                CommandBuffer copyCL = context.GetCommandBuffer();
                copyCL.CopyBuffer(src, 0, dsts[0], 0, src.SizeInBytes);
                for (int i = 0; i < chainLength - 1; i++)
                {
                    copyCL.CopyBuffer(dsts[i], 0, dsts[i + 1], 0, src.SizeInBytes);
                }
                copyCL.CopyBuffer(dsts[dsts.Length - 1], 0, finalDst, 0, src.SizeInBytes);
                context.SubmitCommandBuffer(copyCL);
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

    [Fact]
    public void UnusualSize()
    {
        DeviceBuffer src = RF.CreateBuffer(
            new BufferDescription(208, BufferUsage.UniformBuffer));
        DeviceBuffer dst = RF.CreateBuffer(
            new BufferDescription(208, BufferUsage.Staging));

        byte[] data = Enumerable.Range(0, 208).Select(i => (byte)(i * 150)).ToArray();
        GD.UpdateBuffer(src, 0, data);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.CopyBuffer(src, 0, dst, 0, src.SizeInBytes);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();
        Span<byte> readMap = GD.Map(dst);
        for (int i = 0; i < readMap.Length; i++)
        {
            Assert.Equal((byte)(i * 150), readMap[i]);
        }
        GD.Unmap(dst);
    }

    [Fact]
    public void Update_Dynamic_NonZeroOffset()
    {
        DeviceBuffer dynamic = RF.CreateBuffer(
            new BufferDescription(1024, BufferUsage.Dynamic | BufferUsage.UniformBuffer));

        byte[] initialData = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        GD.UpdateBuffer(dynamic, 0, initialData);

        byte[] replacementData = Enumerable.Repeat((byte)255, 512).ToArray();
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.UpdateBuffer(dynamic, 512, replacementData);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        DeviceBuffer dst = RF.CreateBuffer(
            new BufferDescription(1024, BufferUsage.Staging));

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.CopyBuffer(dynamic, 0, dst, 0, dynamic.SizeInBytes);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        Span<byte> readView = GD.Map<byte>(dst);
        for (uint i = 0; i < 512; i++)
        {
            Assert.Equal((byte)i, readView[(int)i]);
        }

        for (uint i = 512; i < 1024; i++)
        {
            Assert.Equal((byte)255, readView[(int)i]);
        }
    }

    [Fact]
    public void CommandBuffer_Update_Staging()
    {
        DeviceBuffer staging = RF.CreateBuffer(
            new BufferDescription(1024, BufferUsage.Staging));
        byte[] data = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.UpdateBuffer(staging, 0, data);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        Span<byte> readView = GD.Map<byte>(staging);
        for (uint i = 0; i < staging.SizeInBytes; i++)
        {
            Assert.Equal((byte)i, readView[(int)i]);
        }
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

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.CopyBuffer(src, srcCopyOffset, dst, dstCopyOffset, copySize);
            context.SubmitCommandBuffer(cl);
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
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.UpdateBuffer(buffer, offset, data);
            context.SubmitCommandBuffer(cl);
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
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.UpdateBuffer(buffer, 0, mat1);
            cl.UpdateBuffer(buffer, 64, mat2);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(buffer);
        Span<Float4x4> readView = GD.Map<Float4x4>(readback);
        Assert.Equal(mat1, readView[0]);
        Assert.Equal(mat2, readView[1]);
        GD.Unmap(readback);
    }

    [Theory]
    [InlineData(BufferUsage.UniformBuffer)]
    [InlineData(BufferUsage.UniformBuffer | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.VertexBuffer)]
    [InlineData(BufferUsage.VertexBuffer | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.IndexBuffer)]
    [InlineData(BufferUsage.IndexBuffer | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.IndirectBuffer)]
    [InlineData(BufferUsage.StructuredBufferReadOnly)]
    [InlineData(BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.StructuredBufferReadWrite)]
    [InlineData(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer)]
    [InlineData(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer | BufferUsage.IndirectBuffer)]
    [InlineData(BufferUsage.IndexBuffer | BufferUsage.IndirectBuffer)]
    [InlineData(BufferUsage.Staging)]
    public void CreateBuffer_UsageFlagsCoverage(BufferUsage usage)
    {
        if ((usage & BufferUsage.StructuredBufferReadOnly) != 0
            || (usage & BufferUsage.StructuredBufferReadWrite) != 0)
        {
            return;
        }

        BufferDescription description = new(64, usage);
        DeviceBuffer buffer = RF.CreateBuffer(description);
        GD.UpdateBuffer(buffer, 0, new Float4[4]);
        GD.WaitForIdle();
    }

    [Theory]
    [InlineData(BufferUsage.UniformBuffer)]
    [InlineData(BufferUsage.UniformBuffer | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.VertexBuffer)]
    [InlineData(BufferUsage.VertexBuffer | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.IndexBuffer)]
    [InlineData(BufferUsage.IndexBuffer | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.IndirectBuffer)]
    [InlineData(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer)]
    [InlineData(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer | BufferUsage.Dynamic)]
    [InlineData(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer | BufferUsage.IndirectBuffer)]
    [InlineData(BufferUsage.IndexBuffer | BufferUsage.IndirectBuffer)]
    [InlineData(BufferUsage.Staging)]
    public void CopyBuffer_ZeroSize(BufferUsage usage)
    {
        DeviceBuffer src = CreateBuffer(1024, usage);
        DeviceBuffer dst = CreateBuffer(1024, usage);

        byte[] initialDataSrc = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        byte[] initialDataDst = Enumerable.Range(0, 1024).Select(i => (byte)(i * 2)).ToArray();
        GD.UpdateBuffer(src, 0, initialDataSrc);
        GD.UpdateBuffer(dst, 0, initialDataDst);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.CopyBuffer(src, 0, dst, 0, 0);
            context.SubmitCommandBuffer(cl);
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
    [InlineData(BufferUsage.UniformBuffer, false)]
    [InlineData(BufferUsage.UniformBuffer, true)]
    [InlineData(BufferUsage.UniformBuffer | BufferUsage.Dynamic, false)]
    [InlineData(BufferUsage.UniformBuffer | BufferUsage.Dynamic, true)]
    [InlineData(BufferUsage.VertexBuffer, false)]
    [InlineData(BufferUsage.VertexBuffer, true)]
    [InlineData(BufferUsage.VertexBuffer | BufferUsage.Dynamic, false)]
    [InlineData(BufferUsage.VertexBuffer | BufferUsage.Dynamic, true)]
    [InlineData(BufferUsage.IndexBuffer, false)]
    [InlineData(BufferUsage.IndexBuffer, true)]
    [InlineData(BufferUsage.IndirectBuffer, false)]
    [InlineData(BufferUsage.IndirectBuffer, true)]
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
            GD.RunTestGraph(context =>
            {
                CommandBuffer cl = context.GetCommandBuffer();
                fixed (byte* dataPtr = otherData)
                {
                    cl.UpdateBuffer(buffer, 0, (IntPtr)dataPtr, 0);
                }
                context.SubmitCommandBuffer(cl);
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
