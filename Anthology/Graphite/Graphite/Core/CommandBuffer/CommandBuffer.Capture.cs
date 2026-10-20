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
    private readonly List<DescriptorTableEntryState> _tableScratch = new();
    private (DescriptorTable? Table, uint Version)[] _reportedTables = [];

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
    {
        bool overwrites = load != LoadAction.Load && texture.MipLevels == 1 && ValidationHelpers.GetEffectiveArrayLayers(texture) == 1;
        if (overwrites && Stats is { Draws: 0, IndirectDraws: 0, Dispatches: 0 })
        {
            for (int i = 0; i < _referenced.Count; i++)
            {
                ReferencedResource entry = _referenced[i];
                if (entry.Texture == texture && !entry.Overwritten)
                    _referenced[i] = entry with { Overwritten = true };
            }
        }

        TrackTexture(texture, overwrites);
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

        if (sink != null)
            AddUniformStates();

        sink?.SetProperties(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_stateScratch));
    }

    private void AddUniformStates()
    {
        UniformBindingPlan? plan = _uniformShadows.Plan;
        if (plan == null)
            return;

        for (int b = 0; b < plan.BlockCount; b++)
        {
            ShadowBlock block = _uniformShadows[b];
            ReadOnlySpan<byte> bytes = block.Cache != null ? block.Cache.Bytes : block.Data;
            foreach (UniformBlockField field in plan.Layouts[b].Fields)
            {
                UniformValue value = default;
                Span<byte> payload = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                    ref System.Runtime.CompilerServices.Unsafe.As<UniformValue, byte>(ref value), UniformValue.Size);
                bytes.Slice((int)field.Offset, (int)Math.Min(field.Size, UniformValue.Size)).CopyTo(payload);
                _stateScratch.Add(new PropertyState(field.Name, PropertyKind.Uniform, field.Type, value, default, default, null, null));
            }
        }
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
                    name, PropertyKind.Buffer, default, default, resource, ResourceRange.Bytes(range.Offset, range.SizeInBytes), null, null);

            case PropertyEntryKind.Texture:
                TextureView? view = entry.TextureView;
                Texture? texture = view?.Target ?? entry.Texture;
                ResourceRange textureRange = default;
                if (view != null)
                    textureRange = ResourceRange.Subresources(view.BaseMipLevel, view.MipLevels, view.BaseArrayLayer, view.ArrayLayers);
                else if (texture != null)
                    textureRange = ResourceRange.Subresources(0, texture.MipLevels, 0, ValidationHelpers.GetEffectiveArrayLayers(texture));

                return new PropertyState(
                    name, PropertyKind.Texture, default, default, resource, textureRange, view?.Format, entry.Sampler?.Description, view?.Arrayed ?? false);

            default:
                return new PropertyState(name, PropertyKind.Sampler, default, default, default, default, null, entry.Sampler?.Description);
        }
    }

    private void ReportGraphicsState()
    {
        if (!CaptureActive)
            return;

        ReportPropertyStates();
        ReportDescriptorTables();
    }

    // Reports a table only when the one bound at a set index, or its contents, changed since the last report.
    private void ReportDescriptorTables()
    {
        int count = Math.Max(_descriptorTables.Length, _reportedTables.Length);
        if (_reportedTables.Length < count)
            Array.Resize(ref _reportedTables, count);

        for (int set = 0; set < count; set++)
        {
            DescriptorTable? table = DescriptorTableAt(set);
            (DescriptorTable? Table, uint Version) reported = _reportedTables[set];
            if (ReferenceEquals(reported.Table, table) && (table == null || reported.Version == table.Version))
                continue;

            _reportedTables[set] = (table, table?.Version ?? 0);
            _tableScratch.Clear();
            if (table != null)
            {
                for (int element = 0; element < table.ElementCount; element++)
                {
                    ResourceLayoutElementDescription description = table.Layout.Elements[element];
                    ReadOnlySpan<DescriptorTableEntry> entries = table.EntriesOf(element);
                    for (int index = 0; index < entries.Length; index++)
                    {
                        if (!entries[index].IsEmpty)
                            _tableScratch.Add(new DescriptorTableEntryState((uint)index, TableEntryState(description, entries[index])));
                    }
                }
            }

            PassSink?.SetDescriptorTable((uint)set, table?.Layout.Elements ?? [], System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_tableScratch));
        }
    }

    private PropertyState TableEntryState(in ResourceLayoutElementDescription description, in DescriptorTableEntry entry)
    {
        if (entry.Buffer is { } range)
        {
            TrackBuffer(range.Buffer);
            return new PropertyState(description.Name, PropertyKind.Buffer, default, default, range.Buffer.CurrentVersion,
                ResourceRange.Bytes(range.Offset, range.SizeInBytes), null, null);
        }

        if ((entry.View?.Target ?? entry.Texture) is { } texture)
        {
            TrackTexture(texture);
            TextureView? view = entry.View;
            ResourceRange textureRange = view != null
                ? ResourceRange.Subresources(view.BaseMipLevel, view.MipLevels, view.BaseArrayLayer, view.ArrayLayers)
                : ResourceRange.Subresources(0, texture.MipLevels, 0, ValidationHelpers.GetEffectiveArrayLayers(texture));
            return new PropertyState(description.Name, PropertyKind.Texture, default, default, texture.CurrentVersion,
                textureRange, view?.Format, entry.Sampler?.Description, view?.Arrayed ?? false);
        }

        return new PropertyState(description.Name, PropertyKind.Sampler, default, default, default, default, null, entry.Sampler?.Description);
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
