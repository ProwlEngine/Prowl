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
        public ImageLayout Layout;
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
                        r.Buffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(range.Buffer);
                        r.DescRange = range.SizeInBytes;
                        r.DynOffset = range.Offset;
                        break;
                    }

                case ResourceKind.StructuredBufferReadOnly:
                case ResourceKind.StructuredBufferReadWrite:
                    {
                        DeviceBufferRange range = ResolveStructuredRange(in elem, out r.Missing);
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
    {
        if (!_cbOwner.ActiveProperties.Entries.TryGetValue(name, out PropertyEntry? entry))
            return null;

        _trackState?.Track(entry);
        return entry.Kind == kind ? entry : null;
    }

    private DeviceBufferRange ResolveStructuredRange(in ResourceLayoutElementDescription elem, out bool missing)
    {
        if (FindProperty(elem.Name, PropertyEntryKind.Buffer) is { } ssboEntry)
        {
            missing = false;
            return ssboEntry.Buffer!.Value;
        }

        missing = true;
        return new DeviceBufferRange(_gd.NullStructuredRW, 0, _gd.NullStructuredRW.SizeInBytes);
    }

    private DeviceBufferRange ResolveUboRange(
        in ResourceLayoutElementDescription elem, SetBindingMetadata meta, int elemIndex, out bool missing)
    {
        missing = false;
        PropertyEntry? uboEntry = FindProperty(elem.Name, PropertyEntryKind.Buffer);

        if (uboEntry is { BackedBlock: true } && elem.UniformFields is { Length: > 0 })
        {
            return BuildBackedUbo(elem.Name, elem.UniformFields, meta.UniformBlockSizes[elemIndex], uboEntry.Buffer!.Value);
        }

        if (uboEntry != null)
            return uboEntry.Buffer!.Value;

        if (elem.UniformFields is { Length: > 0 })
            return BuildTransientUbo(elem.UniformFields, meta.UniformBlockSizes[elemIndex]);

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
                return ((VkTexture)texEntry.Texture).DefaultView;
            }
        }

        missing = true;
        VkTexture fallback = (VkTexture)(elem.Kind == ResourceKind.TextureReadWrite ? _gd.NullTextureRW2D : _gd.NullTexture2D);
        return fallback.DefaultView;
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

    private DeviceBufferRange BuildBackedUbo(PropertyID name, UniformBlockField[] fields, uint blockSize, DeviceBufferRange target)
    {
        ValidateBackedUbo(name, blockSize, target);

        if (_uboScratch.Length < blockSize)
            _uboScratch = new byte[blockSize];

        PackUniformFields(fields, _uboScratch.AsSpan(0, (int)blockSize));

        fixed (byte* scratchPtr = _uboScratch)
            _gd.UpdateBuffer(target.Buffer, target.Offset, (IntPtr)scratchPtr, blockSize);

        return target;
    }

    private DeviceBufferRange BuildTransientUbo(UniformBlockField[] fields, uint blockSize)
    {
        Span<byte> mapped = CurrentExecution().AllocateTransientMapped(blockSize, out DeviceBufferRange range);
        PackUniformFields(fields, mapped);
        return range;
    }

    private void PackUniformFields(UniformBlockField[] fields, Span<byte> dst)
    {
        dst.Clear();
        for (int i = 0; i < fields.Length; i++)
        {
            ref UniformBlockField field = ref fields[i];
            PropertyEntry? uEntry = FindProperty(field.Name, PropertyEntryKind.Uniform);
            if (uEntry == null)
                continue;

            CopyUniform(uEntry, dst.Slice((int)field.Offset, (int)field.Size));
        }
    }

    private static void CopyUniform(PropertyEntry entry, Span<byte> dst)
    {
        int count = Math.Min(dst.Length, (int)UniformSize(entry.UniformType));
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref entry.Uniform), count).CopyTo(dst);
        dst.Slice(count).Clear();
    }

    private static uint UniformSize(UniformScalarType type) => type switch
    {
        UniformScalarType.Float1 or UniformScalarType.Int1 => 4,
        UniformScalarType.Float2 or UniformScalarType.Int2 => 8,
        UniformScalarType.Float3 or UniformScalarType.Int3 => 12,
        UniformScalarType.Float4 or UniformScalarType.Int4 => 16,
        _ => 64,
    };
}
