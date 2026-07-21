using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

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

internal sealed class ImplicitUboCacheEntry
{
    public DeviceBufferRange Range;
    public byte[] Bytes = Array.Empty<byte>();
    public bool Valid;
}

internal sealed class SetBindState
{
    public readonly ulong[] Identity = new ulong[1 + VkDescriptorBinder.MaxSetElements * 3];
    public int IdentityLen = -1;
    public DescriptorSet Set;
    public uint[] DynOffsets = new uint[16];
    public int DynOffsetCount;
}

// Owns the per-command-buffer descriptor resolve/cache/bind pipeline: resolving a shader program's
// resource layout against the active property set, caching descriptor sets by content, and emitting the
// vkCmdBindDescriptorSets call. Lives in the hot draw/dispatch path, so it is deliberately decoupled from
// render-pass and pooling state: it only touches the owning command buffer, the device, the active
// property set, and its own scratch/cache fields.
internal unsafe sealed class VkDescriptorBinder
{
    internal const int MaxSetElements = 64;

    private readonly VkCommandBuffer _cbOwner;
    private readonly VkGraphicsDevice _gd;

    // Resolve-once scratch: every element of the current set resolved a single time per draw, then
    // read by identity-building, dynamic-offset gathering, texture transitions, and descriptor writes.
    private readonly ResolvedBinding[] _resolveScratch = new ResolvedBinding[MaxSetElements];

    // Scratch identity built each draw, compared against the per-set cached identity below.
    private readonly ulong[] _identityScratch = new ulong[1 + MaxSetElements * 3];

    // Persistent per-(set,binding) transient implicit-UBO cache, reused across draws while the
    // contributing uniform field versions are unchanged. Cleared each Begin (new execution/ring).
    private readonly Dictionary<(int set, int binding), ImplicitUboCacheEntry> _implicitUboCache = [];

    // Per-set-index bind cache: the last-built identity, the resolved descriptor set, and the dynamic
    // offsets. Lets an unchanged set skip the hash + cache probe + descriptor write, and lets a whole
    // draw whose properties are unchanged skip the bind entirely. Cleared each Begin.
    private SetBindState[] _setBindStates = Array.Empty<SetBindState>();
    private ShaderProgram _bindCacheProgram;

    // Whole-draw fast path: the program + property epoch a graphics draw was last prepared for.
    private ShaderProgram _lastPreparedProgram;
    private uint _lastPreparedEpoch;

    // Set count from the most recent Prepare() call that returned true; EmitBind() reads it back so
    // callers don't have to re-derive it from the program.
    private uint _preparedSetCount;

    public VkDescriptorBinder(VkCommandBuffer owner, VkGraphicsDevice gd)
    {
        _cbOwner = owner;
        _gd = gd;
    }

    // A fresh recording binds into a fresh execution: previously-resolved descriptor sets and transient
    // UBO ranges belong to the prior execution and must not be reused.
    internal void ClearForNewRecording()
    {
        _implicitUboCache.Clear();
        _bindCacheProgram = null;
        _lastPreparedProgram = null;
        _lastPreparedEpoch = 0;
        for (int i = 0; i < _setBindStates.Length; i++)
            _setBindStates[i].IdentityLen = -1;
    }

    // Resolves every set once, transitions property textures (before any render pass), and prepares the
    // descriptor sets into the per-set bind cache. Returns whether EmitBind must run: false only when a
    // graphics draw's properties are unchanged since the last draw and the sets are still bound.
    // reportProgram is the shader passed to the missing-property callback; it always mirrors the current
    // graphics shader regardless of isGraphics/isCompute, matching prior behavior.
    internal bool Prepare(ShaderProgram program, ShaderProgram reportProgram, bool isCompute, bool isGraphics, bool renderPassActive)
    {
        IVkDescriptorProgram descProgram = (IVkDescriptorProgram)program;
        uint setCount = descProgram.ResourceSetCount;
        if (setCount == 0) return false;

        // Whole-draw fast path: an active render pass means no copy/dispatch has moved a texture or
        // ended the pass since the last draw, so an unchanged epoch under the same program guarantees
        // every set is identical and still bound.
        if (isGraphics
            && renderPassActive
            && ReferenceEquals(program, _lastPreparedProgram)
            && _cbOwner.ActivePropertiesEpoch == _lastPreparedEpoch)
        {
            return false;
        }

        VkDescriptorSetCache cache = descProgram.DescriptorCache;
        ResourceLayoutDescription[] resourceLayouts = program.ResourceLayoutsArray;
        SetBindingMetadata[] metadata = program.BindingMetadata;
        DescriptorSetLayout[] dslLayouts = descProgram.DescriptorSetLayouts;
        DescriptorResourceCounts[] perSetCounts = descProgram.PerSetCounts;

        EnsureBindCacheFor(program, (int)setCount);
        ulong executionId = _cbOwner.ExecutionId;

        for (int setIdx = 0; setIdx < (int)setCount; setIdx++)
        {
            ResourceLayoutElementDescription[] elements = resourceLayouts[setIdx].Elements ?? Array.Empty<ResourceLayoutElementDescription>();
            SetBindingMetadata meta = metadata[setIdx];
            SetBindState state = _setBindStates[setIdx];

            ResolveSet(program, setIdx, elements, meta, isCompute, reportProgram);
            TransitionResolvedTextures(elements);

            int idLen = BuildIdentityFromScratch(setIdx, elements.Length, _identityScratch);
            ReadOnlySpan<ulong> newId = _identityScratch.AsSpan(0, idLen);

            bool changed = state.IdentityLen != idLen
                || !newId.SequenceEqual(state.Identity.AsSpan(0, state.IdentityLen));

            if (changed)
            {
                if (!cache.TryGet(newId, executionId, out DescriptorSet ds))
                {
                    ds = cache.Allocate(setIdx, dslLayouts[setIdx], in perSetCounts[setIdx], newId, executionId);
                    WriteDescriptorsFromScratch(setIdx, elements, ds, reportProgram);
                }
                state.Set = ds;
                newId.CopyTo(state.Identity);
                state.IdentityLen = idLen;
            }

            GatherDynOffsets(meta, state);
        }

        _lastPreparedProgram = isGraphics ? program : null;
        _lastPreparedEpoch = _cbOwner.ActivePropertiesEpoch;
        _preparedSetCount = setCount;
        return true;
    }

    internal void EmitBind(Silk.NET.Vulkan.PipelineLayout pipelineLayout, PipelineBindPoint bindPoint)
    {
        uint setCount = _preparedSetCount;
        Silk.NET.Vulkan.CommandBuffer cb = _cbOwner.CommandBuffer;

        DescriptorSet* sets = stackalloc DescriptorSet[(int)setCount];
        int totalDyn = 0;
        for (int i = 0; i < (int)setCount; i++)
            totalDyn += _setBindStates[i].DynOffsetCount;

        uint* dynOffsets = stackalloc uint[totalDyn > 0 ? totalDyn : 1];
        int d = 0;
        for (int i = 0; i < (int)setCount; i++)
        {
            SetBindState st = _setBindStates[i];
            sets[i] = st.Set;
            for (int k = 0; k < st.DynOffsetCount; k++)
                dynOffsets[d++] = st.DynOffsets[k];
        }

        _gd.Vk.CmdBindDescriptorSets(cb, bindPoint, pipelineLayout, 0, setCount, sets, (uint)d, dynOffsets);
        _cbOwner.RecordResourceSetBind(setCount);
    }

    private void EnsureBindCacheFor(ShaderProgram program, int setCount)
    {
        if (_setBindStates.Length < setCount)
        {
            int old = _setBindStates.Length;
            Array.Resize(ref _setBindStates, setCount);
            for (int i = old; i < setCount; i++)
                _setBindStates[i] = new SetBindState();
        }

        if (!ReferenceEquals(program, _bindCacheProgram))
        {
            for (int i = 0; i < _setBindStates.Length; i++)
                _setBindStates[i].IdentityLen = -1;
            _bindCacheProgram = program;
        }
    }

    private void ResolveSet(
        ShaderProgram program, int setIdx, ResourceLayoutElementDescription[] elements,
        SetBindingMetadata meta, bool isCompute, ShaderProgram reportProgram)
    {
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
                        DeviceBufferRange range = ResolveUboRange(program, (uint)setIdx, in elem, out bool missing);
                        range.Buffer.MarkInFlight(_gd, executionId);
                        r.Buffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(range.Buffer);
                        r.DescOffset = 0;
                        r.DescRange = range.SizeInBytes;
                        r.DynOffset = range.Offset;
                        r.Missing = missing;
                        break;
                    }

                case ResourceKind.StructuredBufferReadOnly:
                case ResourceKind.StructuredBufferReadWrite:
                    {
                        DeviceBufferRange range = ResolveStructuredRange(in elem, setIdx, out bool missing);
                        range.Buffer.MarkInFlight(_gd, executionId);
                        r.Buffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(range.Buffer);
                        r.DescOffset = range.Offset;
                        r.DescRange = range.SizeInBytes;
                        r.Missing = missing;
                        break;
                    }

                case ResourceKind.TextureReadOnly:
                    r.Combined = (elem.Options & ResourceLayoutElementOptions.CombinedImageSampler) != 0;
                    r.View = ResolveTextureView(in elem, (uint)setIdx, out r.Missing);
                    if (r.Combined)
                        r.Sampler = ResolveSampler(in elem, elements, meta, i);
                    break;

                case ResourceKind.TextureReadWrite:
                    r.View = ResolveTextureView(in elem, (uint)setIdx, out r.Missing);
                    break;

                case ResourceKind.Sampler:
                    r.Sampler = ResolveSampler(in elem, elements, meta, i);
                    break;
            }
        }
    }

    private void TransitionResolvedTextures(ResourceLayoutElementDescription[] elements)
    {
        Silk.NET.Vulkan.CommandBuffer cb = _cbOwner.CommandBuffer;
        for (int i = 0; i < elements.Length; i++)
        {
            ref ResolvedBinding r = ref _resolveScratch[i];
            if (r.Kind != ResourceKind.TextureReadOnly && r.Kind != ResourceKind.TextureReadWrite)
                continue;

            VkTexture tex = r.View.Target;
            ImageLayout targetLayout = r.Kind == ResourceKind.TextureReadOnly
                ? ImageLayout.ShaderReadOnlyOptimal
                : ImageLayout.General;

            tex.TransitionImageLayout(cb, 0, tex.MipLevels, 0, tex.ActualArrayLayers, targetLayout);

            if (r.Kind == ResourceKind.TextureReadWrite && (tex.Usage & TextureUsage.Sampled) != 0)
                _cbOwner.QueuePreDrawSampledImage(tex);
        }
    }

    // Content key for the per-program set cache: resolved handles minus per-draw dynamic UBO offsets.
    // Byte-layout matches the original identity so cached descriptor sets remain compatible.
    private int BuildIdentityFromScratch(int setIdx, int elemCount, ulong[] dst)
    {
        int n = 0;
        dst[n++] = (ulong)setIdx;

        for (int i = 0; i < elemCount; i++)
        {
            ref ResolvedBinding r = ref _resolveScratch[i];
            switch (r.Kind)
            {
                case ResourceKind.UniformBuffer:
                    dst[n++] = r.Buffer.DeviceBuffer.Handle;
                    dst[n++] = r.DescRange;
                    break;

                case ResourceKind.StructuredBufferReadOnly:
                case ResourceKind.StructuredBufferReadWrite:
                    dst[n++] = r.Buffer.DeviceBuffer.Handle;
                    dst[n++] = r.DescOffset;
                    dst[n++] = r.DescRange;
                    break;

                case ResourceKind.TextureReadOnly:
                case ResourceKind.TextureReadWrite:
                    dst[n++] = r.View.ImageView.Handle;
                    break;

                case ResourceKind.Sampler:
                    dst[n++] = r.Sampler.DeviceSampler.Handle;
                    break;
            }
        }

        return n;
    }

    private void GatherDynOffsets(SetBindingMetadata meta, SetBindState state)
    {
        int[] order = meta.SortedUboElementIndices;
        if (state.DynOffsets.Length < order.Length)
            state.DynOffsets = new uint[order.Length];

        for (int i = 0; i < order.Length; i++)
            state.DynOffsets[i] = _resolveScratch[order[i]].DynOffset;

        state.DynOffsetCount = order.Length;
    }

    private void WriteDescriptorsFromScratch(int setIdx, ResourceLayoutElementDescription[] elements, DescriptorSet dstSet, ShaderProgram reportProgram)
    {
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[MaxSetElements];
        DescriptorBufferInfo* bufInfos = stackalloc DescriptorBufferInfo[MaxSetElements];
        DescriptorImageInfo* imgInfos = stackalloc DescriptorImageInfo[MaxSetElements];
        int writeCount = 0, bufIdx = 0, imgIdx = 0;

        for (int i = 0; i < elements.Length; i++)
        {
            ref ResolvedBinding r = ref _resolveScratch[i];
            ref ResourceLayoutElementDescription elem = ref elements[i];

            if (r.Missing)
                ReportMissing(in elem, (uint)setIdx, reportProgram);

            WriteDescriptorSet write = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = dstSet,
                DstBinding = (uint)elem.BindingIndex,
                DescriptorCount = 1,
            };

            switch (r.Kind)
            {
                case ResourceKind.UniformBuffer:
                    bufInfos[bufIdx] = new DescriptorBufferInfo
                    {
                        Buffer = r.Buffer.DeviceBuffer,
                        Offset = 0,
                        Range = r.DescRange,
                    };
                    write.DescriptorType = DescriptorType.UniformBufferDynamic;
                    write.PBufferInfo = &bufInfos[bufIdx++];
                    break;

                case ResourceKind.StructuredBufferReadOnly:
                case ResourceKind.StructuredBufferReadWrite:
                    bufInfos[bufIdx] = new DescriptorBufferInfo
                    {
                        Buffer = r.Buffer.DeviceBuffer,
                        Offset = r.DescOffset,
                        Range = r.DescRange,
                    };
                    write.DescriptorType = DescriptorType.StorageBuffer;
                    write.PBufferInfo = &bufInfos[bufIdx++];
                    break;

                case ResourceKind.TextureReadOnly:
                    imgInfos[imgIdx] = new DescriptorImageInfo
                    {
                        ImageView = r.View.ImageView,
                        ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                        Sampler = r.Combined ? r.Sampler.DeviceSampler : default,
                    };
                    write.DescriptorType = r.Combined ? DescriptorType.CombinedImageSampler : DescriptorType.SampledImage;
                    write.PImageInfo = &imgInfos[imgIdx++];
                    break;

                case ResourceKind.TextureReadWrite:
                    imgInfos[imgIdx] = new DescriptorImageInfo
                    {
                        ImageView = r.View.ImageView,
                        ImageLayout = ImageLayout.General,
                    };
                    write.DescriptorType = DescriptorType.StorageImage;
                    write.PImageInfo = &imgInfos[imgIdx++];
                    break;

                case ResourceKind.Sampler:
                    imgInfos[imgIdx] = new DescriptorImageInfo
                    {
                        Sampler = r.Sampler.DeviceSampler,
                    };
                    write.DescriptorType = DescriptorType.Sampler;
                    write.PImageInfo = &imgInfos[imgIdx++];
                    break;

                default:
                    continue;
            }

            writes[writeCount++] = write;
        }

        if (writeCount > 0)
            _gd.Vk.UpdateDescriptorSets(_gd.Device, (uint)writeCount, writes, 0, null);
    }

    private void ReportMissing(in ResourceLayoutElementDescription elem, uint setIdx, ShaderProgram reportProgram)
    {
        _gd.OnMissingProperty?.Invoke(
            (GraphicsProgram)reportProgram,
            null,
            elem.Name, elem.Kind, setIdx, elem.BindingIndex);
    }

    private VkTexture GetMissingTexture(ResourceKind kind)
        => (VkTexture)(kind == ResourceKind.TextureReadWrite ? _gd.NullTextureRW2D : _gd.NullTexture2D);

    private DeviceBufferRange ResolveStructuredRange(
        in ResourceLayoutElementDescription elem, int setIdx, out bool missing)
    {
        if (_cbOwner.ActiveProperties.Entries.TryGetValue(elem.Name, out PropertyEntry? ssboEntry)
            && ssboEntry.Kind == PropertyEntryKind.Buffer)
        {
            missing = false;
            return ssboEntry.Buffer!.Value;
        }

        missing = true;
        return new DeviceBufferRange(_gd.NullStructuredRW, 0, 0);
    }

    private DeviceBufferRange ResolveUboRange(
        ShaderProgram programKey, uint setIdx, in ResourceLayoutElementDescription elem, out bool missing)
    {
        missing = false;

        bool hasExplicit = _cbOwner.ActiveProperties.Entries.TryGetValue(elem.Name, out PropertyEntry? uboEntry)
            && uboEntry.Kind == PropertyEntryKind.Buffer;

        // A read-only buffer is bound with its existing contents; any scalar writes are ignored.
        if (hasExplicit && uboEntry!.ReadOnly)
            return uboEntry.Buffer!.Value;

        // Loose uniform fields go into the explicit writable buffer if bound, else a per-draw transient.
        if (elem.UniformFields != null && elem.UniformFields.Length > 0)
        {
            DeviceBufferRange? writableTarget = hasExplicit ? uboEntry!.Buffer : null;
            return GetOrBuildImplicitUbo((int)setIdx, elem.BindingIndex, elem.UniformFields, writableTarget);
        }

        // No loose uniform fields declared: bind the explicit buffer directly.
        if (hasExplicit)
            return uboEntry!.Buffer!.Value;

        missing = true;
        return AllocateExecutionTransient(16);
    }

    private DeviceBufferRange AllocateExecutionTransient(uint sizeInBytes)
    {
        if (_cbOwner.Execution == null)
            throw new RenderException("Recording a draw that needs transient uniform memory requires a command buffer rented from a render context.");
        return _cbOwner.Execution.AllocateTransientInternal(sizeInBytes);
    }

    private DeviceBufferRange GetOrBuildImplicitUbo(
        int setIdx, int bindingIndex, UniformBlockField[] fields, DeviceBufferRange? writableTarget)
    {
        // Write only the set fields into the explicit buffer, leaving unset bytes intact. The explicit
        // buffer is stable across draws, so its identity never changes; write it every draw as before.
        if (writableTarget.HasValue)
        {
            DeviceBufferRange target = writableTarget.Value;
            foreach (UniformBlockField field in fields)
            {
                if (_cbOwner.ActiveProperties.Entries.TryGetValue(field.Name, out PropertyEntry? uEntry)
                    && uEntry.Kind == PropertyEntryKind.Uniform)
                {
                    ref byte payload = ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref uEntry.Uniform);
                    fixed (byte* ptr = &payload)
                        _gd.UpdateBuffer(target.Buffer, target.Offset + field.Offset, (IntPtr)ptr, field.Size);
                }
            }
            return target;
        }

        // Transient path: build this draw's uniform bytes and reuse the previously allocated range when
        // they are byte-identical to the last upload. An unchanged UBO then neither reallocates a
        // transient nor re-uploads, and keeps a stable descriptor identity across draws. Comparing the
        // bytes (rather than entry versions) stays correct when a merge swaps in a fresh entry object.
        (int, int) key = (setIdx, bindingIndex);
        if (!_implicitUboCache.TryGetValue(key, out ImplicitUboCacheEntry? entry))
        {
            entry = new ImplicitUboCacheEntry();
            _implicitUboCache[key] = entry;
        }

        uint totalSize = 0;
        foreach (UniformBlockField field in fields)
            totalSize = Math.Max(totalSize, field.Offset + field.Size);
        if (totalSize == 0) totalSize = 16;

        byte[] uploadBuf = ArrayPool<byte>.Shared.Rent((int)totalSize);
        try
        {
            Array.Clear(uploadBuf, 0, (int)totalSize);
            foreach (UniformBlockField field in fields)
            {
                if (_cbOwner.ActiveProperties.Entries.TryGetValue(field.Name, out PropertyEntry? uEntry)
                    && uEntry.Kind == PropertyEntryKind.Uniform)
                {
                    ReadOnlySpan<byte> src = MemoryMarshal.CreateReadOnlySpan(
                        ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref uEntry.Uniform),
                        (int)field.Size);
                    src.CopyTo(uploadBuf.AsSpan((int)field.Offset, (int)field.Size));
                }
            }

            if (entry.Valid
                && entry.Bytes.Length == (int)totalSize
                && uploadBuf.AsSpan(0, (int)totalSize).SequenceEqual(entry.Bytes))
            {
                return entry.Range;
            }

            DeviceBufferRange range = AllocateExecutionTransient(totalSize);
            fixed (byte* ptr = uploadBuf)
                _gd.UpdateBuffer(range.Buffer, range.Offset, (IntPtr)ptr, totalSize);

            if (entry.Bytes.Length != (int)totalSize)
                entry.Bytes = new byte[(int)totalSize];
            uploadBuf.AsSpan(0, (int)totalSize).CopyTo(entry.Bytes);
            entry.Range = range;
            entry.Valid = true;

            return range;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(uploadBuf);
        }
    }

    private VkTextureView ResolveTextureView(
        in ResourceLayoutElementDescription elem, uint setIdx, out bool missing)
    {
        if (_cbOwner.ActiveProperties.Entries.TryGetValue(elem.Name, out PropertyEntry? texEntry)
            && texEntry.Kind == PropertyEntryKind.Texture)
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
        return _gd.GetOrCreateDefaultView(GetMissingTexture(elem.Kind));
    }

    private VkSampler ResolveSampler(
        in ResourceLayoutElementDescription elem, ResourceLayoutElementDescription[] elements,
        SetBindingMetadata meta, int elemIndex)
    {
        // case 1: explicit SetSampler(name) entry
        if (_cbOwner.ActiveProperties.Entries.TryGetValue(elem.Name, out PropertyEntry? samplerEntry)
            && samplerEntry.Kind == PropertyEntryKind.Sampler
            && samplerEntry.Sampler != null)
        {
            return (VkSampler)samplerEntry.Sampler;
        }

        // case 2: SetTexture(name, _, sampler) where a same-named texture element exists (precomputed)
        if (meta.HasSameNamedTexture[elemIndex]
            && _cbOwner.ActiveProperties.Entries.TryGetValue(elem.Name, out PropertyEntry? texEntry)
            && texEntry.Kind == PropertyEntryKind.Texture
            && texEntry.Sampler != null)
        {
            return (VkSampler)texEntry.Sampler;
        }

        // case 3: fall back to the default linear sampler
        return (VkSampler)_gd.LinearSampler;
    }
}
