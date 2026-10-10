using System;
using System.Collections.Generic;

using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite;

internal readonly record struct ReferencedResource(DeviceBuffer? Buffer, Texture? Texture, ResourceVersion FirstVersion, bool Overwritten);

public abstract partial class CommandBuffer
{
    private readonly List<PropertyState> _stateScratch = new();
    private readonly List<ReferencedResource> _referenced = new();
    private readonly HashSet<ResourceId> _referencedIds = new();
    private AttachmentUse[] _attachmentScratch = new AttachmentUse[8];
    private VertexBindingUse[] _vertexScratch = new VertexBindingUse[8];

    internal bool CaptureActive => Execution != null && (Profilers.CommandStream != null || Profilers.Capture != null);

    /// <summary>Resources this pass's commands referenced, with their version at first reference.</summary>
    internal IReadOnlyList<ReferencedResource> ReferencedResources => _referenced;

    private void ResetCaptureState()
    {
        _referenced.Clear();
        _referencedIds.Clear();
    }

    internal override void TrackBuffer(DeviceBuffer buffer)
    {
        if (!CaptureActive || !_referencedIds.Add(buffer.ResourceId))
            return;

        _referenced.Add(new ReferencedResource(buffer, null, buffer.CurrentVersion, false));
    }

    internal override void TrackTexture(Texture texture) => TrackTexture(texture, false);

    private void TrackTexture(Texture texture, bool overwritten)
    {
        if (!CaptureActive || !_referencedIds.Add(texture.ResourceId))
            return;

        _referenced.Add(new ReferencedResource(null, texture, texture.CurrentVersion, overwritten));
    }

    private void TrackAttachment(Texture texture, LoadAction load)
        => TrackTexture(texture, load != LoadAction.Load && texture.MipLevels == 1 && ValidationHelpers.GetEffectiveArrayLayers(texture) == 1);

    private void ReportFramebuffer(Framebuffer fb, in TargetLoadStoreOps ops)
    {
        if (!CaptureActive)
            return;

        IReadOnlyList<FramebufferAttachment> colors = fb.ColorTargets;
        Util.EnsureArrayMinimumSize(ref _attachmentScratch, (uint)colors.Count);
        for (int i = 0; i < colors.Count; i++)
        {
            FramebufferAttachment attachment = colors[i];
            TrackAttachment(attachment.Target, ops.Color.Load);
            _attachmentScratch[i] = new AttachmentUse(attachment.Target.CurrentVersion, attachment.MipLevel, attachment.ArrayLayer);
        }

        AttachmentUse? depth = null;
        if (fb.DepthTarget is { } depthAttachment)
        {
            TrackAttachment(depthAttachment.Target, ops.Depth.Load);
            depth = new AttachmentUse(depthAttachment.Target.CurrentVersion, depthAttachment.MipLevel, depthAttachment.ArrayLayer);
        }

        if (PassSink is { } sink)
        {
            FramebufferInfo info = new(_attachmentScratch.AsSpan(0, colors.Count), depth, fb.OutputDescription, fb.Width, fb.Height);
            sink.SetFramebuffer(in info, in ops);
        }
    }

    private void ReportPropertyStates()
    {
        _stateScratch.Clear();
        IPassCommandSink? sink = PassSink;

        foreach (KeyValuePair<PropertyID, PropertyEntry> kv in _activeProperties.Entries)
        {
            PropertyEntry entry = kv.Value;
            ResourceVersion resource = default;
            switch (entry.Kind)
            {
                case PropertyEntryKind.Buffer when entry.Buffer is { } range:
                    resource = range.Buffer.CurrentVersion;
                    TrackBuffer(range.Buffer);
                    break;
                case PropertyEntryKind.Texture when (entry.TextureView?.Target ?? entry.Texture) is { } texture:
                    resource = texture.CurrentVersion;
                    TrackTexture(texture);
                    break;
            }

            if (sink != null)
                _stateScratch.Add(CreateState(kv.Key, entry, resource));
        }

        sink?.SetProperties(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_stateScratch));
    }

    private static PropertyState CreateState(PropertyID name, PropertyEntry entry, ResourceVersion resource)
    {
        switch (entry.Kind)
        {
            case PropertyEntryKind.Uniform:
                UniformValue value;
                value = System.Runtime.CompilerServices.Unsafe.As<PropertyEntry.UniformPayload, UniformValue>(ref entry.Uniform);
                return new PropertyState(name, PropertyKind.Uniform, entry.UniformType, value, default, default, null, null);

            case PropertyEntryKind.Buffer:
                DeviceBufferRange range = entry.Buffer ?? default;
                return new PropertyState(
                    name,
                    entry.BackedBlock ? PropertyKind.UniformBuffer : PropertyKind.Buffer,
                    default, default, resource, ResourceRange.Bytes(range.Offset, range.SizeInBytes), null, null);

            case PropertyEntryKind.Texture:
                TextureView? view = entry.TextureView;
                Texture? texture = view?.Target ?? entry.Texture;
                ResourceRange textureRange = default;
                if (view != null)
                    textureRange = ResourceRange.Subresources(view.BaseMipLevel, view.MipLevels, view.BaseArrayLayer, view.ArrayLayers);
                else if (texture != null)
                    textureRange = ResourceRange.Subresources(0, texture.MipLevels, 0, ValidationHelpers.GetEffectiveArrayLayers(texture));

                return new PropertyState(
                    name, PropertyKind.Texture, default, default, resource, textureRange, view?.Format, entry.Sampler?.Description);

            default:
                return new PropertyState(name, PropertyKind.Sampler, default, default, default, default, null, entry.Sampler?.Description);
        }
    }

    private void ReportGraphicsState()
    {
        if (CaptureActive)
            ReportPropertyStates();
    }

    /// <summary>Called by the backend with the vertex bindings it resolved for a draw, including cache hits.</summary>
    private protected void ReportVertexBindings(IReadOnlyList<VertexLayoutDescription> layouts, VertexBinding[] bindings, int count)
    {
        if (!CaptureActive)
            return;

        Util.EnsureArrayMinimumSize(ref _vertexScratch, (uint)count);
        for (int slot = 0; slot < count; slot++)
        {
            VertexBinding binding = bindings[slot];
            TrackBuffer(binding.Buffer);
            _vertexScratch[slot] = new VertexBindingUse((uint)slot, binding.Buffer.CurrentVersion, binding.Offset, layouts[slot].Stride);
        }

        PassSink?.BindVertexBuffers(_vertexScratch.AsSpan(0, count));
    }

    /// <summary>Called by the backend with the index buffer it resolved for a draw, including cache hits.</summary>
    private protected void ReportIndexBinding(DeviceBuffer buffer, IndexFormat format, uint indexCount)
    {
        if (!CaptureActive)
            return;

        TrackBuffer(buffer);
        IndexBindingUse use = new(buffer.CurrentVersion, format, indexCount);
        PassSink?.BindIndexBuffer(in use);
    }
}
