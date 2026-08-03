using System;
using System.Diagnostics;

namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
    private static void SetVertexSource_CheckNonNull(IVertexSource source)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (source == null)
        {
            throw new ArgumentNullException(nameof(source),
                "IVertexSource must be non-null. Bind an empty implementation if a vertex-source-free draw is intended.");
        }
    }

    private protected void CheckVertexBindingUsage(in VertexBinding binding, uint layoutSlot)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (binding.Buffer == null)
        {
            throw new RenderException(
                $"IVertexSource.ResolveSlot returned a null Buffer for layout slot {layoutSlot}.");
        }
        if ((binding.Buffer.Usage & BufferUsage.VertexBuffer) == 0)
        {
            throw new RenderException(
                $"Buffer for layout slot {layoutSlot} cannot be bound as a vertex buffer because it was not created with BufferUsage.VertexBuffer.");
        }
    }

    private protected void CheckIndexBufferUsage(DeviceBuffer buffer)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (buffer == null)
        {
            throw new RenderException(
                "IVertexSource.TryGetIndexBuffer returned true but the index buffer is null.");
        }
        if ((buffer.Usage & BufferUsage.IndexBuffer) == 0)
        {
            throw new RenderException(
                "Buffer cannot be bound as an index buffer because it was not created with BufferUsage.IndexBuffer.");
        }
    }

    private void ClearColorTarget_CheckFramebuffer(uint index)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        CheckFramebuffer(nameof(ClearColorTarget));
        if (_framebuffer!.ColorTargets.Count <= index)
        {
            throw new RenderException(
                $"{nameof(ClearColorTarget)} index must be less than the current Framebuffer's color target count.");
        }
    }

    private void ClearDepthStencil_CheckFramebuffer()
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        CheckFramebuffer(nameof(ClearDepthStencil));
        if (_framebuffer!.DepthTarget == null)
        {
            throw new RenderException(
                $"The current Framebuffer has no depth target, so {nameof(ClearDepthStencil)} cannot be used.");
        }
    }


    private void CheckFramebuffer(string name)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (_framebuffer == null)
            throw new RenderException($"Cannot use {name}. There is no Framebuffer bound.");
    }

    private void DrawIndexed_CheckBaseVertexInstance(int vertexOffset, uint instanceStart)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (!_features.DrawBaseVertex && vertexOffset != 0)
        {
            throw new RenderException("Drawing with a non-zero base vertex is not supported on this device.");
        }
        if (!_features.DrawBaseInstance && instanceStart != 0)
        {
            throw new RenderException("Drawing with a non-zero base instance is not supported on this device.");
        }
    }

    private static void DrawIndirect_CheckOffset(uint offset)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if ((offset % 4) != 0)
        {
            throw new RenderException($"{nameof(offset)} must be a multiple of 4.");
        }
    }

    private void DrawIndirect_CheckSupport()
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (!_features.DrawIndirect)
        {
            throw new RenderException($"Indirect drawing is not supported by this device.");
        }
    }

    private static void DrawIndirect_CheckBuffer(DeviceBuffer indirectBuffer)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if ((indirectBuffer.Usage & BufferUsage.IndirectBuffer) != BufferUsage.IndirectBuffer)
        {
            throw new RenderException(
                $"{nameof(indirectBuffer)} parameter must have been created with BufferUsage.IndirectBuffer. Instead, it was {indirectBuffer.Usage}.");
        }
    }

    private static void DrawIndirect_CheckStride(uint stride, int argumentSize)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (stride < argumentSize || ((stride % 4) != 0))
        {
            throw new RenderException(
                $"{nameof(stride)} parameter must be a multiple of 4, and must be larger than the size of the corresponding argument structure.");
        }
    }

    private static void ResolveTexture_CheckSampleCounts(Texture source, Texture destination)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (source.SampleCount == TextureSampleCount.Count1)
        {
            throw new RenderException(
                $"The {nameof(source)} parameter of {nameof(ResolveTexture)} must be a multisample texture.");
        }
        if (destination.SampleCount != TextureSampleCount.Count1)
        {
            throw new RenderException(
                $"The {nameof(destination)} parameter of {nameof(ResolveTexture)} must be a non-multisample texture. Instead, it is a texture with {FormatHelpers.GetSampleCountUInt32(source.SampleCount)} samples.");
        }
    }

    private protected static void DrawIndexed_AssertIndexBufferResolved(bool resolved)
    {
        Debug.Assert(resolved,
            $"Validation in {nameof(DrawIndexed)} must have already trapped a missing index buffer on indexed-draw paths.");
    }

    private void DrawIndexed_CheckIndexBuffer()
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (_currentVertexSource == null)
        {
            return;
        }
        if (!_currentVertexSource.TryGetIndexBuffer(out DeviceBuffer ib, out IndexFormat fmt, out uint indexCount))
        {
            throw new RenderException(
                "DrawIndexed/DrawIndexedIndirect requires the bound IVertexSource to supply an index buffer, " +
                "but TryGetIndexBuffer returned false.");
        }

        uint indexFormatSize = fmt == IndexFormat.UInt16 ? 2u : 4u;
        uint bytesNeeded = indexCount * indexFormatSize;
        if (ib.SizeInBytes < bytesNeeded)
        {
            throw new RenderException(
                $"The active index buffer does not contain enough data to satisfy the given draw command. {bytesNeeded} bytes are needed, but the buffer only contains {ib.SizeInBytes}.");
        }
    }

    private void DrawIndexedIndirect_CheckIndexBuffer()
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (_currentVertexSource != null
            && !_currentVertexSource.TryGetIndexBuffer(out _, out _, out _))
        {
            throw new RenderException(
                "DrawIndexed/DrawIndexedIndirect requires the bound IVertexSource to supply an index buffer, " +
                "but TryGetIndexBuffer returned false.");
        }
    }

    private void Draw_PreDrawValidation()
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (_shaderProgram == null)
        {
            throw new RenderException($"A graphics GraphicsProgram must be set in order to issue draw commands.");
        }
        if (_framebuffer == null)
        {
            throw new RenderException($"A {nameof(Framebuffer)} must be set in order to issue draw commands.");
        }
        if (_currentVertexSource == null)
        {
            throw new RenderException(
                "An IVertexSource must be set via SetVertexSource before issuing draw commands. " +
                "Bind an empty IVertexSource implementation if no vertex data is required.");
        }
    }
}
