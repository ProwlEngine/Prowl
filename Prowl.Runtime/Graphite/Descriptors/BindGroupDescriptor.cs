// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Describes a single binding slot in a bind group layout.
/// </summary>
public struct BindGroupLayoutEntry
{
    /// <summary>Binding index.</summary>
    public uint Binding;

    /// <summary>Shader stages that can access this binding.</summary>
    public ShaderStage Visibility;

    /// <summary>Type of resource bound at this slot.</summary>
    public BindingType Type;

    /// <summary>Array count (1 for non-arrays).</summary>
    public uint Count;

    /// <summary>Whether this binding uses dynamic offsets (for uniform/storage buffers).</summary>
    public bool HasDynamicOffset;

    public BindGroupLayoutEntry(uint binding, ShaderStage visibility, BindingType type, uint count = 1, bool hasDynamicOffset = false)
    {
        Binding = binding;
        Visibility = visibility;
        Type = type;
        Count = count;
        HasDynamicOffset = hasDynamicOffset;
    }

    /// <summary>Creates a uniform buffer entry.</summary>
    public static BindGroupLayoutEntry UniformBuffer(uint binding, ShaderStage visibility = ShaderStage.AllGraphics, bool hasDynamicOffset = false) =>
        new(binding, visibility, BindingType.UniformBuffer, 1, hasDynamicOffset);

    /// <summary>Creates a storage buffer entry.</summary>
    public static BindGroupLayoutEntry StorageBuffer(uint binding, ShaderStage visibility = ShaderStage.AllGraphics, bool readOnly = false, bool hasDynamicOffset = false) =>
        new(binding, visibility, readOnly ? BindingType.ReadOnlyStorageBuffer : BindingType.StorageBuffer, 1, hasDynamicOffset);

    /// <summary>Creates a sampled texture entry.</summary>
    public static BindGroupLayoutEntry SampledTexture(uint binding, ShaderStage visibility = ShaderStage.Fragment) =>
        new(binding, visibility, BindingType.SampledTexture);

    /// <summary>Creates a sampler entry.</summary>
    public static BindGroupLayoutEntry Sampler(uint binding, ShaderStage visibility = ShaderStage.Fragment) =>
        new(binding, visibility, BindingType.Sampler);

    /// <summary>Creates a combined texture+sampler entry.</summary>
    public static BindGroupLayoutEntry CombinedTextureSampler(uint binding, ShaderStage visibility = ShaderStage.Fragment) =>
        new(binding, visibility, BindingType.CombinedTextureSampler);

    /// <summary>Creates a storage texture entry.</summary>
    public static BindGroupLayoutEntry StorageTexture(uint binding, ShaderStage visibility = ShaderStage.Compute) =>
        new(binding, visibility, BindingType.StorageTexture);
}

/// <summary>
/// Describes how to create a bind group layout.
/// </summary>
public struct BindGroupLayoutDescriptor
{
    /// <summary>Entries in this layout.</summary>
    public BindGroupLayoutEntry[] Entries;

    /// <summary>Optional debug name for graphics debuggers.</summary>
    public string? DebugName;

    public BindGroupLayoutDescriptor(params BindGroupLayoutEntry[] entries)
    {
        Entries = entries;
        DebugName = null;
    }
}

/// <summary>
/// Describes a buffer binding within a bind group.
/// </summary>
public struct BufferBinding
{
    /// <summary>Buffer to bind.</summary>
    public Buffer Buffer;

    /// <summary>Offset in bytes from the start of the buffer.</summary>
    public uint Offset;

    /// <summary>Size in bytes to bind (0 = whole buffer from offset).</summary>
    public uint Size;

    public BufferBinding(Buffer buffer, uint offset = 0, uint size = 0)
    {
        Buffer = buffer;
        Offset = offset;
        Size = size;
    }
}

/// <summary>
/// Describes a single resource binding in a bind group.
/// </summary>
public struct BindGroupEntry
{
    /// <summary>Binding index (must match layout).</summary>
    public uint Binding;

    /// <summary>Buffer binding (for uniform/storage buffers).</summary>
    public BufferBinding? Buffer;

    /// <summary>Texture binding (for sampled/storage textures).</summary>
    public Texture? Texture;

    /// <summary>Sampler binding.</summary>
    public Sampler? Sampler;

    private BindGroupEntry(uint binding)
    {
        Binding = binding;
        Buffer = null;
        Texture = null;
        Sampler = null;
    }

    /// <summary>Creates a buffer binding entry.</summary>
    public static BindGroupEntry ForBuffer(uint binding, Buffer buffer, uint offset = 0, uint size = 0) => new(binding)
    {
        Buffer = new BufferBinding(buffer, offset, size)
    };

    /// <summary>Creates a texture binding entry.</summary>
    public static BindGroupEntry ForTexture(uint binding, Texture texture) => new(binding)
    {
        Texture = texture
    };

    /// <summary>Creates a sampler binding entry.</summary>
    public static BindGroupEntry ForSampler(uint binding, Sampler sampler) => new(binding)
    {
        Sampler = sampler
    };

    /// <summary>Creates a combined texture+sampler binding entry.</summary>
    public static BindGroupEntry ForTextureSampler(uint binding, Texture texture, Sampler sampler) => new(binding)
    {
        Texture = texture,
        Sampler = sampler
    };
}

/// <summary>
/// Describes how to create a bind group.
/// </summary>
public struct BindGroupDescriptor
{
    /// <summary>Layout this bind group conforms to.</summary>
    public BindGroupLayout Layout;

    /// <summary>Resource entries.</summary>
    public BindGroupEntry[] Entries;

    /// <summary>Optional debug name for graphics debuggers.</summary>
    public string? DebugName;

    public BindGroupDescriptor(BindGroupLayout layout, params BindGroupEntry[] entries)
    {
        Layout = layout;
        Entries = entries;
        DebugName = null;
    }
}
