using System;

namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
    private (DescriptorTable? Table, uint Version)[] _checkedTables = [];

    internal void CheckNotDisposed(GraphicsResource? resource, string use)
    {
        if (resource is { IsDisposed: true } && Device.ValidationEnabled)
            ThrowDisposed(resource, use);
    }

    internal void CheckNotDisposed(PropertyEntry entry, PropertyID name)
    {
        if (!Device.ValidationEnabled)
            return;

        GraphicsResource? disposed = Disposed(entry.Buffer?.Buffer) ?? Disposed(entry.Texture) ?? Disposed(entry.TextureView)
            ?? Disposed(entry.TextureView?.Target) ?? Disposed(entry.Sampler);
        if (disposed != null)
            ThrowDisposed(disposed, $"The resource of property '{PropertyID.ToString(name)}'");
    }

    private static GraphicsResource? Disposed(GraphicsResource? resource) => resource is { IsDisposed: true } ? resource : null;

    private void ThrowDisposed(GraphicsResource resource, string use)
    {
        string reason = resource.DisposeReason is { } why ? $" ({why})" : "";
        throw new RenderException($"{use} is '{resource.Name}', which was disposed{reason} before command buffer '{Name}' recorded a command using it.");
    }

    private void SetDescriptorTable_CheckNotDisposed(uint set, DescriptorTable table)
    {
        if (!Device.ValidationEnabled)
            return;

        CheckNotDisposed(table, "The descriptor table");
        if (_checkedTables.Length <= set)
            Array.Resize(ref _checkedTables, (int)set + 1);
        if (ReferenceEquals(_checkedTables[set].Table, table) && _checkedTables[set].Version == table.Version)
            return;

        _checkedTables[set] = (table, table.Version);
        for (int element = 0; element < table.ElementCount; element++)
        {
            ref readonly ResourceLayoutElementDescription description = ref table.Layout.Elements[element];
            if (description.Count != 1)
                continue;

            DescriptorTableEntry entry = table.EntriesOf(element)[0];
            GraphicsResource? disposed = Disposed(entry.Buffer?.Buffer) ?? Disposed(entry.Texture) ?? Disposed(entry.View)
                ?? Disposed(entry.View?.Target) ?? Disposed(entry.Sampler);
            if (disposed != null)
                ThrowDisposed(disposed, $"The resource bound to '{PropertyID.ToString(description.Name)}' in descriptor table '{table.Name}'");
        }
    }

    private void SetFramebuffer_CheckNotDisposed(Framebuffer fb)
    {
        if (fb == null || !Device.ValidationEnabled)
            return;

        CheckNotDisposed(fb, "The framebuffer");
        foreach (FramebufferAttachment attachment in fb.ColorTargets)
            CheckNotDisposed(attachment.Target, "A color target");
        if (fb.DepthTarget is { } depth)
            CheckNotDisposed(depth.Target, "The depth target");
    }

    private void SetVertexSource_CheckNonNull(IVertexSource source)
    {
        if (!Device.ValidationEnabled)
            return;

        if (source == null)
        {
            throw new ArgumentNullException(nameof(source),
                "IVertexSource must be non-null. Bind an empty implementation if a vertex-source-free draw is intended.");
        }
    }

    private protected void CheckVertexBindingUsage(in VertexBinding binding, uint layoutSlot)
    {
        if (!Device.ValidationEnabled)
            return;

        if (binding.Buffer == null)
        {
            throw new RenderException(
                $"IVertexSource.ResolveSlot returned a null Buffer for layout slot {layoutSlot}.");
        }
        CheckNotDisposed(binding.Buffer, $"The vertex buffer of layout slot {layoutSlot}");
        if ((binding.Buffer.Usage & BufferUsage.VertexBuffer) == 0)
        {
            throw new RenderException(
                $"Buffer for layout slot {layoutSlot} cannot be bound as a vertex buffer because it was not created with BufferUsage.VertexBuffer.");
        }
    }

    private protected void CheckIndexBufferUsage(DeviceBuffer buffer)
    {
        if (!Device.ValidationEnabled)
            return;

        if (buffer == null)
        {
            throw new RenderException(
                "IVertexSource.TryGetIndexBuffer returned true but the index buffer is null.");
        }
        CheckNotDisposed(buffer, "The index buffer");
        if ((buffer.Usage & BufferUsage.IndexBuffer) == 0)
        {
            throw new RenderException(
                "Buffer cannot be bound as an index buffer because it was not created with BufferUsage.IndexBuffer.");
        }
    }

    private void ClearColorTarget_CheckFramebuffer(uint index)
    {
        if (!Device.ValidationEnabled)
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
        if (!Device.ValidationEnabled)
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
        if (!Device.ValidationEnabled)
            return;

        if (_framebuffer == null)
            throw new RenderException($"Cannot use {name}. There is no Framebuffer bound.");
    }

    private void DrawIndirect_CheckOffset(uint offset)
    {
        if (!Device.ValidationEnabled)
            return;

        if ((offset % 4) != 0)
        {
            throw new RenderException($"{nameof(offset)} must be a multiple of 4.");
        }
    }

    private void DrawIndirect_CheckBuffer(DeviceBuffer indirectBuffer)
    {
        if (!Device.ValidationEnabled)
            return;

        if ((indirectBuffer.Usage & BufferUsage.IndirectBuffer) != BufferUsage.IndirectBuffer)
        {
            throw new RenderException(
                $"{nameof(indirectBuffer)} parameter must have been created with BufferUsage.IndirectBuffer. Instead, it was {indirectBuffer.Usage}.");
        }
    }

    private void DrawIndirect_CheckDrawCount(uint drawCount)
    {
        if (!Device.ValidationEnabled)
            return;

        if (drawCount > 1 && !_features.MultiDrawIndirect)
        {
            throw new RenderException(
                $"{nameof(drawCount)} is {drawCount}, but the device does not support GraphicsDeviceFeatures.MultiDrawIndirect, so it must be 0 or 1.");
        }
    }

    private void DrawIndirect_CheckStride(uint stride, int argumentSize)
    {
        if (!Device.ValidationEnabled)
            return;

        if (stride < argumentSize || ((stride % 4) != 0))
        {
            throw new RenderException(
                $"{nameof(stride)} parameter must be a multiple of 4, and must be larger than the size of the corresponding argument structure.");
        }
    }

    private void ResolveTexture_CheckSampleCounts(Texture source, Texture destination)
    {
        if (!Device.ValidationEnabled)
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
}
