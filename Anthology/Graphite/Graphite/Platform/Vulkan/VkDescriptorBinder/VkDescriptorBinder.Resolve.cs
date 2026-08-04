using System;
using System.Buffers;
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

    private sealed class ImplicitUboCacheEntry
    {
        public DeviceBufferRange Range;
        public byte[] Bytes = Array.Empty<byte>();
        public bool Valid;
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
                        DeviceBufferRange range = ResolveUboRange((uint)setIdx, in elem, out r.Missing);
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

    private DeviceBufferRange ResolveUboRange(uint setIdx, in ResourceLayoutElementDescription elem, out bool missing)
    {
        missing = false;
        PropertyEntry? uboEntry = FindProperty(elem.Name, PropertyEntryKind.Buffer);

        // A read-only buffer is bound with its existing contents; any scalar writes are ignored.
        if (uboEntry is { ReadOnly: true })
            return uboEntry.Buffer!.Value;

        // Loose uniform fields go into the explicit writable buffer if bound, else a per-draw transient.
        if (elem.UniformFields is { Length: > 0 })
            return GetOrBuildImplicitUbo((int)setIdx, elem.BindingIndex, elem.UniformFields, uboEntry?.Buffer);

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

    private DeviceBufferRange AllocateExecutionTransient(uint sizeInBytes)
    {
        if (_cbOwner.Execution == null)
            throw new RenderException("Recording a draw that needs transient uniform memory requires a command buffer rented from a render context.");
        return _cbOwner.Execution.AllocateTransientInternal(sizeInBytes);
    }

    private DeviceBufferRange GetOrBuildImplicitUbo(
        int setIdx, int bindingIndex, UniformBlockField[] fields, DeviceBufferRange? writableTarget)
    {
        if (writableTarget is not { } target)
            return GetOrBuildTransientUbo(setIdx, bindingIndex, fields);

        // Write only the set fields into the explicit buffer, leaving unset bytes intact. The explicit
        // buffer is stable across draws, so its identity never changes; write it every draw as before.
        foreach (UniformBlockField field in fields)
        {
            if (FindProperty(field.Name, PropertyEntryKind.Uniform) is not { } uEntry)
                continue;

            ref byte payload = ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref uEntry.Uniform);
            fixed (byte* ptr = &payload)
                _gd.UpdateBuffer(target.Buffer, target.Offset + field.Offset, (IntPtr)ptr, field.Size);
        }
        return target;
    }


    private DeviceBufferRange GetOrBuildTransientUbo(int setIdx, int bindingIndex, UniformBlockField[] fields)
    {
        (int, int) key = (setIdx, bindingIndex);
        if (!_implicitUboCache.TryGetValue(key, out ImplicitUboCacheEntry? entry))
            _implicitUboCache[key] = entry = new ImplicitUboCacheEntry();

        uint totalSize = UniformBlockSize(fields);
        byte[] uploadBuf = ArrayPool<byte>.Shared.Rent((int)totalSize);
        try
        {
            Span<byte> bytes = uploadBuf.AsSpan(0, (int)totalSize);
            PackUniformFields(fields, bytes);

            if (entry.Valid && bytes.SequenceEqual(entry.Bytes))
                return entry.Range;

            DeviceBufferRange range = AllocateExecutionTransient(totalSize);
            fixed (byte* ptr = uploadBuf)
                _gd.UpdateBuffer(range.Buffer, range.Offset, (IntPtr)ptr, totalSize);

            if (entry.Bytes.Length != bytes.Length)
                entry.Bytes = new byte[bytes.Length];
            bytes.CopyTo(entry.Bytes);
            entry.Range = range;
            entry.Valid = true;

            return range;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(uploadBuf);
        }
    }

    private void PackUniformFields(UniformBlockField[] fields, Span<byte> dst)
    {
        dst.Clear();
        foreach (UniformBlockField field in fields)
        {
            if (FindProperty(field.Name, PropertyEntryKind.Uniform) is not { } uEntry)
                continue;

            ReadOnlySpan<byte> src = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref uEntry.Uniform),
                (int)field.Size);
            src.CopyTo(dst.Slice((int)field.Offset, (int)field.Size));
        }
    }

    private static uint UniformBlockSize(UniformBlockField[] fields)
    {
        uint size = 0;
        foreach (UniformBlockField field in fields)
            size = Math.Max(size, field.Offset + field.Size);
        return size == 0 ? 16 : size;
    }
}
