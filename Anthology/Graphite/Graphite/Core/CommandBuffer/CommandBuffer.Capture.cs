using System;
using System.Collections.Generic;

using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite;

internal readonly record struct ReferencedResource(DeviceBuffer? Buffer, Texture? Texture, ResourceVersion FirstVersion);

public abstract partial class CommandBuffer
{
    private readonly struct ReportedProperty
    {
        public readonly PropertyEntry Entry;
        public readonly uint Version;
        public readonly ResourceVersion Resource;

        public ReportedProperty(PropertyEntry entry, uint version, ResourceVersion resource)
        {
            Entry = entry;
            Version = version;
            Resource = resource;
        }
    }

    private readonly Dictionary<PropertyID, ReportedProperty> _reportedProperties = new();
    private readonly List<PropertyDelta> _deltaScratch = new();
    private readonly List<ReferencedResource> _referenced = new();
    private readonly HashSet<ResourceId> _referencedIds = new();
    private AttachmentUse[] _attachmentScratch = new AttachmentUse[8];
    private VertexBindingUse[] _vertexScratch = new VertexBindingUse[8];
    private VertexBindingUse[] _reportedVertex = new VertexBindingUse[8];
    private int _reportedVertexCount;
    private IndexBindingUse? _reportedIndex;

    internal bool PassCommandsOpen { get; set; }

    internal override ICommandStreamProfiler? PassSink => Execution != null ? Profilers.CommandStream : null;

    internal bool CaptureActive => Execution != null && (Profilers.CommandStream != null || Profilers.Capture != null);

    /// <summary>Resources this pass's commands referenced, with their version at first reference.</summary>
    internal IReadOnlyList<ReferencedResource> ReferencedResources => _referenced;

    private void ResetCaptureState()
    {
        _reportedProperties.Clear();
        _referenced.Clear();
        _referencedIds.Clear();
        _reportedVertexCount = 0;
        _reportedIndex = null;
    }

    internal override void TrackBuffer(DeviceBuffer buffer)
    {
        if (!CaptureActive || !_referencedIds.Add(buffer.ResourceId))
            return;

        _referenced.Add(new ReferencedResource(buffer, null, buffer.CurrentVersion));
    }

    internal override void TrackTexture(Texture texture)
    {
        if (!CaptureActive || !_referencedIds.Add(texture.ResourceId))
            return;

        _referenced.Add(new ReferencedResource(null, texture, texture.CurrentVersion));
    }

    private void ReportFramebuffer(Framebuffer fb, in TargetLoadStoreOps ops)
    {
        if (!CaptureActive)
            return;

        IReadOnlyList<FramebufferAttachment> colors = fb.ColorTargets;
        Util.EnsureArrayMinimumSize(ref _attachmentScratch, (uint)colors.Count);
        for (int i = 0; i < colors.Count; i++)
        {
            FramebufferAttachment attachment = colors[i];
            TrackTexture(attachment.Target);
            _attachmentScratch[i] = new AttachmentUse(attachment.Target.CurrentVersion, attachment.MipLevel, attachment.ArrayLayer);
        }

        AttachmentUse? depth = null;
        if (fb.DepthTarget is { } depthAttachment)
        {
            TrackTexture(depthAttachment.Target);
            depth = new AttachmentUse(depthAttachment.Target.CurrentVersion, depthAttachment.MipLevel, depthAttachment.ArrayLayer);
        }

        if (Profilers.CommandStream is { } sink)
        {
            FramebufferInfo info = new(_attachmentScratch.AsSpan(0, colors.Count), depth, fb.OutputDescription, fb.Width, fb.Height);
            sink.SetFramebuffer(in info, in ops);
        }
    }

    private void ReportPropertyDeltas()
    {
        _deltaScratch.Clear();
        ICommandStreamProfiler? sink = Profilers.CommandStream;

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

            if (sink == null)
                continue;

            if (_reportedProperties.TryGetValue(kv.Key, out ReportedProperty last)
                && ReferenceEquals(last.Entry, entry) && last.Version == entry.Version && last.Resource == resource)
                continue;

            _reportedProperties[kv.Key] = new ReportedProperty(entry, entry.Version, resource);
            _deltaScratch.Add(CreateDelta(kv.Key, entry, resource));
        }

        if (_deltaScratch.Count > 0)
            sink!.ApplyPropertyDeltas(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_deltaScratch));
    }

    private static PropertyDelta CreateDelta(PropertyID name, PropertyEntry entry, ResourceVersion resource)
    {
        switch (entry.Kind)
        {
            case PropertyEntryKind.Uniform:
                UniformValue value;
                value = System.Runtime.CompilerServices.Unsafe.As<PropertyEntry.UniformPayload, UniformValue>(ref entry.Uniform);
                return new PropertyDelta(name, PropertyDeltaKind.Uniform, entry.UniformType, value, default, default, null, null);

            case PropertyEntryKind.Buffer:
                DeviceBufferRange range = entry.Buffer ?? default;
                return new PropertyDelta(
                    name,
                    entry.BackedBlock ? PropertyDeltaKind.UniformBuffer : PropertyDeltaKind.Buffer,
                    default, default, resource, ResourceRange.Bytes(range.Offset, range.SizeInBytes), null, null);

            case PropertyEntryKind.Texture:
                TextureView? view = entry.TextureView;
                Texture? texture = view?.Target ?? entry.Texture;
                ResourceRange textureRange = default;
                if (view != null)
                    textureRange = ResourceRange.Subresources(view.BaseMipLevel, view.MipLevels, view.BaseArrayLayer, view.ArrayLayers);
                else if (texture != null)
                    textureRange = ResourceRange.Subresources(0, texture.MipLevels, 0, ValidationHelpers.GetEffectiveArrayLayers(texture));

                return new PropertyDelta(
                    name, PropertyDeltaKind.Texture, default, default, resource, textureRange, view?.Format, entry.Sampler?.Description);

            default:
                return new PropertyDelta(name, PropertyDeltaKind.Sampler, default, default, default, default, null, entry.Sampler?.Description);
        }
    }

    private void ReportGraphicsState()
    {
        if (CaptureActive)
            ReportPropertyDeltas();
    }

    /// <summary>Called by the backend with the vertex bindings it resolved for a draw, including cache hits.</summary>
    private protected void ReportVertexBindings(IReadOnlyList<VertexLayoutDescription> layouts, VertexBinding[] bindings, int count)
    {
        if (!CaptureActive)
            return;

        Util.EnsureArrayMinimumSize(ref _vertexScratch, (uint)count);
        bool same = count == _reportedVertexCount;
        for (int slot = 0; slot < count; slot++)
        {
            VertexBinding binding = bindings[slot];
            TrackBuffer(binding.Buffer);
            VertexBindingUse use = new((uint)slot, binding.Buffer.CurrentVersion, binding.Offset, layouts[slot].Stride);
            _vertexScratch[slot] = use;
            if (same && _reportedVertex[slot] != use)
                same = false;
        }

        if (same)
            return;

        Util.EnsureArrayMinimumSize(ref _reportedVertex, (uint)count);
        Array.Copy(_vertexScratch, _reportedVertex, count);
        _reportedVertexCount = count;
        Profilers.CommandStream?.BindVertexBuffers(_vertexScratch.AsSpan(0, count));
    }

    /// <summary>Called by the backend with the index buffer it resolved for a draw, including cache hits.</summary>
    private protected void ReportIndexBinding(DeviceBuffer buffer, IndexFormat format, uint indexCount)
    {
        if (!CaptureActive)
            return;

        TrackBuffer(buffer);
        IndexBindingUse use = new(buffer.CurrentVersion, format, indexCount);
        if (_reportedIndex == use)
            return;

        _reportedIndex = use;
        Profilers.CommandStream?.BindIndexBuffer(in use);
    }
}
