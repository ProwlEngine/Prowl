using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;


internal unsafe sealed partial class VkDescriptorBinder
{
    internal struct ResolvedBinding
    {
        public ResourceKind Kind;
        public bool Missing;
        public VkBuffer Buffer;      // UniformBuffer / StructuredBuffer backing buffer
        public ulong DescOffset;     // descriptor offset (UBO: 0, dynamic offset carries the range offset)
        public ulong DescRange;      // descriptor range/size
        public uint DynOffset;       // UBO dynamic offset
        public VkTextureView View;   // texture view
        public VkSampler Sampler;    // sampler element, or combined-image-sampler's sampler
        public bool Combined;
    }

    private void ResolveSet(
        int setIdx, ResourceLayoutElementDescription[] elements, SetBindingMetadata meta, ShaderProgram reportProgram)
    {
        Debug.Assert(elements.Length <= MaxSetElements, "Resource layout exceeds MaxSetElements; program creation should have rejected it.");

        ulong executionId = _cbOwner.ExecutionId;

        for (int i = 0; i < elements.Length; i++)
        {
            ref ResourceLayoutElementDescription elem = ref elements[i];
            ref ResolvedBinding r = ref _resolveScratch[i];
            r = default;
            r.Kind = elem.Kind;

            switch (elem.Kind)
            {
                case ResourceKind.UniformBuffer:
                    {
                        DeviceBufferRange range = ResolveUboRange(in elem, meta, i, out r.Missing);
                        range.Buffer.MarkInFlight(_gd, executionId);
                        r.Buffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(range.Buffer);
                        r.DescRange = range.SizeInBytes;
                        r.DynOffset = range.Offset;
                        break;
                    }

                case ResourceKind.StructuredBufferReadOnly:
                case ResourceKind.StructuredBufferReadWrite:
                    {
                        DeviceBufferRange range = ResolveStructuredRange(in elem, out r.Missing);
                        range.Buffer.MarkInFlight(_gd, executionId);
                        r.Buffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(range.Buffer);
                        r.DescOffset = range.Offset;
                        r.DescRange = range.SizeInBytes;
                        break;
                    }

                case ResourceKind.TextureReadOnly:
                    r.Combined = (elem.Options & ResourceLayoutElementOptions.CombinedImageSampler) != 0;
                    r.View = ResolveTextureView(in elem, out r.Missing);
                    if (r.Combined)
                        r.Sampler = ResolveSampler(in elem, meta, i);
                    break;

                case ResourceKind.TextureReadWrite:
                    r.View = ResolveTextureView(in elem, out r.Missing);
                    break;

                case ResourceKind.Sampler:
                    r.Sampler = ResolveSampler(in elem, meta, i);
                    break;
            }
        }
    }

    private PropertyEntry? FindProperty(PropertyID name, PropertyEntryKind kind)
        => _cbOwner.ActiveProperties.Entries.TryGetValue(name, out PropertyEntry? entry) && entry.Kind == kind
            ? entry
            : null;

    private DeviceBufferRange ResolveStructuredRange(in ResourceLayoutElementDescription elem, out bool missing)
    {
        if (FindProperty(elem.Name, PropertyEntryKind.Buffer) is { } ssboEntry)
        {
            missing = false;
            return ssboEntry.Buffer!.Value;
        }

        missing = true;
        return new DeviceBufferRange(_gd.NullStructuredRW, 0, 0);
    }

    private DeviceBufferRange ResolveUboRange(
        in ResourceLayoutElementDescription elem, SetBindingMetadata meta, int elemIndex, out bool missing)
    {
        missing = false;
        PropertyEntry? uboEntry = FindProperty(elem.Name, PropertyEntryKind.Buffer);

        // A read-only buffer is bound with its existing contents; any scalar writes are ignored.
        if (uboEntry is { ReadOnly: true })
            return uboEntry.Buffer!.Value;

        // Loose uniform fields go into the explicit writable buffer if bound, else a per-draw transient.
        if (elem.UniformFields is { Length: > 0 })
        {
            return GetOrBuildImplicitUbo(
                elem.UniformFields, meta.UniformBlockSlots[elemIndex], meta.UniformBlockSizes[elemIndex], uboEntry?.Buffer);
        }

        // No loose uniform fields declared: bind the explicit buffer directly.
        if (uboEntry != null)
            return uboEntry.Buffer!.Value;

        missing = true;
        return AllocateExecutionTransient(16);
    }

    private VkTextureView ResolveTextureView(in ResourceLayoutElementDescription elem, out bool missing)
    {
        if (FindProperty(elem.Name, PropertyEntryKind.Texture) is { } texEntry)
        {
            if (texEntry.TextureView != null)
            {
                missing = false;
                return (VkTextureView)texEntry.TextureView;
            }
            if (texEntry.Texture != null)
            {
                missing = false;
                return _gd.GetOrCreateDefaultView((VkTexture)texEntry.Texture);
            }
        }

        missing = true;
        VkTexture fallback = (VkTexture)(elem.Kind == ResourceKind.TextureReadWrite ? _gd.NullTextureRW2D : _gd.NullTexture2D);
        return _gd.GetOrCreateDefaultView(fallback);
    }

    private VkSampler ResolveSampler(in ResourceLayoutElementDescription elem, SetBindingMetadata meta, int elemIndex)
    {
        // case 1: explicit SetSampler(name) entry
        if (FindProperty(elem.Name, PropertyEntryKind.Sampler) is { Sampler: not null } samplerEntry)
            return (VkSampler)samplerEntry.Sampler;

        // case 2: SetTexture(name, _, sampler) where a same-named texture element exists (precomputed)
        if (meta.HasSameNamedTexture[elemIndex]
            && FindProperty(elem.Name, PropertyEntryKind.Texture) is { Sampler: not null } texEntry)
        {
            return (VkSampler)texEntry.Sampler;
        }

        // case 3: fall back to the default linear sampler
        return (VkSampler)_gd.LinearSampler;
    }

    private VkExecutionTask CurrentExecution()
    {
        if (_cbOwner.Execution is not VkExecutionTask execution)
            throw new RenderException("Recording a draw that needs transient uniform memory requires a command buffer rented from a render context.");
        return execution;
    }

    private DeviceBufferRange AllocateExecutionTransient(uint sizeInBytes)
    {
        CurrentExecution().AllocateTransientMapped(sizeInBytes, out DeviceBufferRange range);
        return range;
    }

    // Memoized the same way as the transient path, keyed on the explicit buffer's identity, offset and
    // content version plus each field's source entry and version. A hit skips every write for the draw.
    private DeviceBufferRange GetOrBuildImplicitUbo(
        UniformBlockField[] fields, int blockSlot, uint blockSize, DeviceBufferRange? writableTarget)
    {
        if (writableTarget is not { } target)
            return GetOrBuildTransientUbo(fields, blockSlot, blockSize);

        VkUniformArena.Block block = CurrentExecution().UniformArena.GetBlock(blockSlot, fields, blockSize);

        if (ExplicitTargetUnchanged(fields, block, target))
            return target;

        Span<byte> scratch = block.Scratch.AsSpan(0, (int)blockSize);

        for (int i = 0; i < fields.Length; i++)
        {
            ref UniformBlockField field = ref fields[i];
            PropertyEntry? uEntry = FindProperty(field.Name, PropertyEntryKind.Uniform);
            block.ExplicitSources[i] = uEntry;
            block.ExplicitVersions[i] = uEntry?.Version ?? 0;

            if (uEntry == null)
                continue;

            ReadOnlySpan<byte> src = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref uEntry.Uniform),
                (int)field.Size);
            src.CopyTo(scratch.Slice((int)field.Offset, (int)field.Size));
        }

        // One write per byte-contiguous run of set fields, so gaps left by unset fields stay intact.
        fixed (byte* scratchPtr = block.Scratch)
        {
            int runStart = -1;
            for (int i = 0; i < fields.Length; i++)
            {
                if (block.ExplicitSources[i] == null)
                    continue;

                if (runStart < 0)
                    runStart = i;

                if (i + 1 < fields.Length
                    && block.ExplicitSources[i + 1] != null
                    && fields[i].Offset + fields[i].Size == fields[i + 1].Offset)
                {
                    continue;
                }

                uint start = fields[runStart].Offset;
                uint length = fields[i].Offset + fields[i].Size - start;
                _gd.UpdateBuffer(target.Buffer, target.Offset + start, (IntPtr)(scratchPtr + start), length);
                runStart = -1;
            }
        }

        block.ExplicitBuffer = target.Buffer;
        block.ExplicitOffset = target.Offset;
        block.ExplicitContentVersion = target.Buffer.ContentVersion;
        return target;
    }

    private bool ExplicitTargetUnchanged(UniformBlockField[] fields, VkUniformArena.Block block, DeviceBufferRange target)
    {
        if (!ReferenceEquals(block.ExplicitBuffer, target.Buffer)
            || block.ExplicitOffset != target.Offset
            || block.ExplicitContentVersion != target.Buffer.ContentVersion)
        {
            return false;
        }

        for (int i = 0; i < fields.Length; i++)
        {
            PropertyEntry? entry = FindProperty(fields[i].Name, PropertyEntryKind.Uniform);
            if (!ReferenceEquals(entry, block.ExplicitSources[i]) || (entry != null && entry.Version != block.ExplicitVersions[i]))
                return false;
        }
        return true;
    }


    // The packed block is memoized for the whole execution, so a block whose sources are untouched is
    // packed, allocated and uploaded once no matter how many draws or command buffers reference it.
    private DeviceBufferRange GetOrBuildTransientUbo(UniformBlockField[] fields, int blockSlot, uint blockSize)
    {
        VkExecutionTask execution = CurrentExecution();
        ulong executionId = execution.Id;
        VkUniformArena.Block block = execution.UniformArena.GetBlock(blockSlot, fields, blockSize);

        bool live = block.ExecutionId == executionId;
        if (live && SourcesUnchanged(fields, block))
            return block.Range;

        Span<byte> packed = block.Scratch.AsSpan(0, (int)blockSize);
        PackUniformFields(fields, packed, block);

        // A different entry object can still hold identical bytes; that must not cost a new range.
        if (live && packed.SequenceEqual(block.Packed))
            return block.Range;

        Span<byte> mapped = execution.AllocateTransientMapped(blockSize, out DeviceBufferRange range);
        packed.CopyTo(mapped);

        block.CommitScratch();
        block.Range = range;
        block.ExecutionId = executionId;
        return range;
    }

    private bool SourcesUnchanged(UniformBlockField[] fields, VkUniformArena.Block block)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            PropertyEntry? entry = FindProperty(fields[i].Name, PropertyEntryKind.Uniform);
            if (!ReferenceEquals(entry, block.Sources[i]) || (entry != null && entry.Version != block.Versions[i]))
                return false;
        }
        return true;
    }

    private void PackUniformFields(UniformBlockField[] fields, Span<byte> dst, VkUniformArena.Block block)
    {
        dst.Clear();
        for (int i = 0; i < fields.Length; i++)
        {
            ref UniformBlockField field = ref fields[i];
            PropertyEntry? uEntry = FindProperty(field.Name, PropertyEntryKind.Uniform);
            block.Sources[i] = uEntry;
            block.Versions[i] = uEntry?.Version ?? 0;

            if (uEntry == null)
                continue;

            ReadOnlySpan<byte> src = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref uEntry.Uniform),
                (int)field.Size);
            src.CopyTo(dst.Slice((int)field.Offset, (int)field.Size));
        }
    }
}
