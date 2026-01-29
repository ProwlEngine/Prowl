// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Silk.NET.OpenGL;

namespace Prowl.Runtime.Graphite.OpenGL;

/// <summary>
/// OpenGL implementation of a bind group layout.
/// In OpenGL, layouts are primarily used for validation and reflection.
/// </summary>
public class GLBindGroupLayout : BindGroupLayout
{
    private readonly GLGraphiteDevice _device;
    internal BindGroupLayoutEntry[] Entries { get; }

    internal GLBindGroupLayout(GLGraphiteDevice device, in BindGroupLayoutDescriptor descriptor)
    {
        _device = device;
        Entries = descriptor.Entries ?? [];
        DebugName = descriptor.DebugName;
    }

    protected override void DisposeResources()
    {
        // No GPU resources to dispose for OpenGL layouts
    }
}

/// <summary>
/// OpenGL implementation of a bind group.
/// Stores references to bound resources for application during draw.
/// </summary>
public class GLBindGroup : BindGroup
{
    private readonly GLGraphiteDevice _device;
    internal BindGroupEntry[] Entries { get; }

    internal GLBindGroup(GLGraphiteDevice device, in BindGroupDescriptor descriptor)
    {
        _device = device;
        Layout = descriptor.Layout;
        Entries = descriptor.Entries ?? [];
        DebugName = descriptor.DebugName;
    }

    /// <summary>
    /// Applies the bind group bindings to the current OpenGL state.
    /// </summary>
    /// <param name="groupIndex">The bind group slot index.</param>
    /// <param name="dynamicOffsets">Optional dynamic offsets for uniform/storage buffers with dynamic offsets.</param>
    internal void Apply(uint groupIndex, uint[]? dynamicOffsets = null)
    {
        var layout = Layout as GLBindGroupLayout;
        if (layout == null) return;

        int dynamicOffsetIndex = 0;

        foreach (var entry in Entries)
        {
            var layoutEntry = FindLayoutEntry(layout, entry.Binding);
            if (layoutEntry == null) continue;

            switch (layoutEntry.Value.Type)
            {
                case BindingType.UniformBuffer:
                case BindingType.StorageBuffer:
                case BindingType.ReadOnlyStorageBuffer:
                    // Get dynamic offset if this is a dynamic buffer binding
                    uint dynamicOffset = 0;
                    if (layoutEntry.Value.HasDynamicOffset && dynamicOffsets != null && dynamicOffsetIndex < dynamicOffsets.Length)
                    {
                        dynamicOffset = dynamicOffsets[dynamicOffsetIndex++];
                    }
                    BindBuffer(entry, layoutEntry.Value, dynamicOffset);
                    break;

                case BindingType.SampledTexture:
                    BindTexture(entry, layoutEntry.Value);
                    break;

                case BindingType.Sampler:
                    BindSampler(entry, layoutEntry.Value);
                    break;

                case BindingType.CombinedTextureSampler:
                    BindCombinedTextureSampler(entry, layoutEntry.Value);
                    break;

                case BindingType.StorageTexture:
                    BindStorageTexture(entry, layoutEntry.Value);
                    break;
            }
        }
    }

    private static BindGroupLayoutEntry? FindLayoutEntry(GLBindGroupLayout layout, uint binding)
    {
        foreach (var entry in layout.Entries)
        {
            if (entry.Binding == binding)
                return entry;
        }
        return null;
    }

    private void BindBuffer(BindGroupEntry entry, BindGroupLayoutEntry layoutEntry, uint dynamicOffset = 0)
    {
        if (entry.Buffer == null) return;

        var buffer = entry.Buffer.Value;
        if (buffer.Buffer is not GLBuffer glBuffer) return;

        var target = layoutEntry.Type switch
        {
            BindingType.UniformBuffer => BufferTargetARB.UniformBuffer,
            BindingType.StorageBuffer or BindingType.ReadOnlyStorageBuffer => BufferTargetARB.ShaderStorageBuffer,
            _ => BufferTargetARB.UniformBuffer,
        };

        // Add dynamic offset to the base offset
        uint totalOffset = buffer.Offset + dynamicOffset;
        uint size = buffer.Size == 0 ? glBuffer.SizeInBytes - totalOffset : buffer.Size;
        _device.GL.BindBufferRange(target, entry.Binding, glBuffer.Handle, (nint)totalOffset, size);
    }

    private void BindTexture(BindGroupEntry entry, BindGroupLayoutEntry layoutEntry)
    {
        if (entry.Texture is not GLTexture glTexture) return;

        _device.GL.ActiveTexture(TextureUnit.Texture0 + (int)entry.Binding);
        _device.GL.BindTexture(glTexture.Target, glTexture.Handle);
    }

    private void BindSampler(BindGroupEntry entry, BindGroupLayoutEntry layoutEntry)
    {
        if (entry.Sampler is not GLSampler glSampler) return;

        _device.GL.BindSampler(entry.Binding, glSampler.Handle);
    }

    private void BindCombinedTextureSampler(BindGroupEntry entry, BindGroupLayoutEntry layoutEntry)
    {
        if (entry.Texture is GLTexture glTexture)
        {
            _device.GL.ActiveTexture(TextureUnit.Texture0 + (int)entry.Binding);
            _device.GL.BindTexture(glTexture.Target, glTexture.Handle);
        }

        if (entry.Sampler is GLSampler glSampler)
        {
            _device.GL.BindSampler(entry.Binding, glSampler.Handle);
        }
    }

    private void BindStorageTexture(BindGroupEntry entry, BindGroupLayoutEntry layoutEntry)
    {
        if (entry.Texture is not GLTexture glTexture) return;

        // Bind as image for compute shader access
        var format = GetImageFormat(glTexture.Format);
        _device.GL.BindImageTexture(entry.Binding, glTexture.Handle, 0, false, 0, BufferAccessARB.ReadWrite, format);
    }

    // Complete image format mapping for storage textures (glBindImageTexture)
    // These map TextureFormat to OpenGL image unit format qualifiers
    private static InternalFormat GetImageFormat(TextureFormat format) => format switch
    {
        // 8-bit formats
        TextureFormat.R8Unorm => InternalFormat.R8,
        TextureFormat.R8Snorm => InternalFormat.R8SNorm,
        TextureFormat.R8Uint => InternalFormat.R8ui,
        TextureFormat.R8Sint => InternalFormat.R8i,
        TextureFormat.RG8Unorm => InternalFormat.RG8,
        TextureFormat.RG8Snorm => InternalFormat.RG8SNorm,
        TextureFormat.RG8Uint => InternalFormat.RG8ui,
        TextureFormat.RG8Sint => InternalFormat.RG8i,
        TextureFormat.RGBA8Unorm => InternalFormat.Rgba8,
        TextureFormat.RGBA8Snorm => InternalFormat.Rgba8SNorm,
        TextureFormat.RGBA8Uint => InternalFormat.Rgba8ui,
        TextureFormat.RGBA8Sint => InternalFormat.Rgba8i,
        // 16-bit formats
        TextureFormat.R16Uint => InternalFormat.R16ui,
        TextureFormat.R16Sint => InternalFormat.R16i,
        TextureFormat.R16Float => InternalFormat.R16f,
        TextureFormat.RG16Uint => InternalFormat.RG16ui,
        TextureFormat.RG16Sint => InternalFormat.RG16i,
        TextureFormat.RG16Float => InternalFormat.RG16f,
        TextureFormat.RGBA16Uint => InternalFormat.Rgba16ui,
        TextureFormat.RGBA16Sint => InternalFormat.Rgba16i,
        TextureFormat.RGBA16Float => InternalFormat.Rgba16f,
        // 32-bit formats
        TextureFormat.R32Uint => InternalFormat.R32ui,
        TextureFormat.R32Sint => InternalFormat.R32i,
        TextureFormat.R32Float => InternalFormat.R32f,
        TextureFormat.RG32Uint => InternalFormat.RG32ui,
        TextureFormat.RG32Sint => InternalFormat.RG32i,
        TextureFormat.RG32Float => InternalFormat.RG32f,
        TextureFormat.RGBA32Uint => InternalFormat.Rgba32ui,
        TextureFormat.RGBA32Sint => InternalFormat.Rgba32i,
        TextureFormat.RGBA32Float => InternalFormat.Rgba32f,
        // Packed formats
        TextureFormat.RGB10A2Unorm => InternalFormat.Rgb10A2,
        TextureFormat.RG11B10Float => InternalFormat.R11fG11fB10f,
        _ => InternalFormat.Rgba8,
    };

    protected override void DisposeResources()
    {
        // No GPU resources to dispose - bind groups just reference other resources
    }
}
