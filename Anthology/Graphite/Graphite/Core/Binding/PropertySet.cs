using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite;

/// <summary>
/// A user-owned set of named shader resources and uniforms to bind and upload before or during a draw.
/// Each PropertySet is applied to a current active set on the command buffer, with the last applied set 
/// overwriting any previous values with its own.
/// <para>
/// <see cref="PropertySet"/> is not internally synchronized. Upload at your own risk.
/// </para>
/// </summary>
public sealed partial class PropertySet
{
    private readonly Dictionary<PropertyID, PropertyEntry> _entries;

    private uint _resourceVersion;


    /// <summary>
    /// Initializes a new, empty PropertySet with a capacity of 0.
    /// </summary>
    public PropertySet() : this(0)
    {
    }


    /// <summary>Initializes an empty <see cref="PropertySet"/> with the given initial entry capacity.</summary>
    /// <param name="initialEntryCapacity">The initial dictionary capacity.</param>
    public PropertySet(int initialEntryCapacity)
    {
        _entries = new(initialEntryCapacity);
    }


    /// <summary>
    /// Tracked resource version counter. Incremented whenever any resource setter
    /// (<see cref="SetBuffer(PropertyID,DeviceBuffer,bool)"/>, <see cref="SetTexture(PropertyID,Texture,Sampler)"/>,
    /// <see cref="SetSampler(PropertyID,Sampler)"/>) is called.
    /// Uniform updates (any scalar value) do not increment this.
    /// </summary>
    public uint ResourceVersion => _resourceVersion;


    /// <summary>Number of entries currently stored in this set.</summary>
    public int EntryCount => _entries.Count;


    internal Dictionary<PropertyID, PropertyEntry> Entries => _entries;


    /// <summary>Sets a <c>float</c> uniform field.</summary>
    public void SetFloat(PropertyID name, float v) => WriteUniform(name, v, UniformScalarType.Float1);
    /// <summary>Sets a <c>float2</c> uniform field.</summary>
    public void SetFloat2(PropertyID name, Float2 v) => WriteUniform(name, v, UniformScalarType.Float2);
    /// <summary>Sets a <c>float3</c> uniform field.</summary>
    public void SetFloat3(PropertyID name, Float3 v) => WriteUniform(name, v, UniformScalarType.Float3);
    /// <summary>Sets a <c>float4</c> uniform field.</summary>
    public void SetFloat4(PropertyID name, Float4 v) => WriteUniform(name, v, UniformScalarType.Float4);

    /// <summary>Sets an <c>int</c> uniform field.</summary>
    public void SetInt(PropertyID name, int v) => WriteUniform(name, v, UniformScalarType.Int1);
    /// <summary>Sets an <c>int2</c> uniform field.</summary>
    public void SetInt2(PropertyID name, Int2 v) => WriteUniform(name, v, UniformScalarType.Int2);
    /// <summary>Sets an <c>int3</c> uniform field.</summary>
    public void SetInt3(PropertyID name, Int3 v) => WriteUniform(name, v, UniformScalarType.Int3);
    /// <summary>Sets an <c>int4</c> uniform field.</summary>
    public void SetInt4(PropertyID name, Int4 v) => WriteUniform(name, v, UniformScalarType.Int4);

    /// <summary>Sets a <c>double</c> uniform field.</summary>
    public void SetDouble(PropertyID name, double v) => WriteUniform(name, v, UniformScalarType.Double1);
    /// <summary>Sets a <c>double2</c> uniform field.</summary>
    public void SetDouble2(PropertyID name, Double2 v) => WriteUniform(name, v, UniformScalarType.Double2);
    /// <summary>Sets a <c>double3</c> uniform field.</summary>
    public void SetDouble3(PropertyID name, Double3 v) => WriteUniform(name, v, UniformScalarType.Double3);
    /// <summary>Sets a <c>double4</c> uniform field.</summary>
    public void SetDouble4(PropertyID name, Double4 v) => WriteUniform(name, v, UniformScalarType.Double4);

    /// <summary>Sets a <c>float4x4</c> matrix uniform field.</summary>
    public void SetMatrix(PropertyID name, Float4x4 v) => WriteUniform(name, v, UniformScalarType.Float4x4);
    /// <summary>Sets a <c>double4x4</c> matrix uniform field.</summary>
    public void SetDoubleMatrix(PropertyID name, Double4x4 v) => WriteUniform(name, v, UniformScalarType.Double4x4);


    /// <inheritdoc cref="SetBuffer(PropertyID, DeviceBufferRange, bool)"/>
    public void SetBuffer(PropertyID name, DeviceBuffer buffer, bool readOnly = true)
    {
        ValidationHelpers.RequireNotNull(buffer, nameof(buffer), nameof(SetBuffer));
        SetBuffer(name, new DeviceBufferRange(buffer, 0, buffer.SizeInBytes), readOnly);
    }

    /// <summary>
    /// Binds a <see cref="DeviceBuffer"/> to the named property slot. Covers both
    /// <see cref="ResourceKind.UniformBuffer"/> (whole-buffer path) and structured-buffer kinds;
    /// if <paramref name="readOnly"/> is false, the buffer (if uniform) will have its uniforms set by the binder.
    /// if <paramref name="readOnly"/> is true, it'll simply bind the buffer - this is the default.
    /// </summary>
    public void SetBuffer(PropertyID name, DeviceBufferRange range, bool readOnly = true)
    {
        ValidationHelpers.RequireNotNull(range.Buffer, nameof(range), nameof(SetBuffer));
        GetOrCreate(name).SetBuffer(range, readOnly);
        unchecked { _resourceVersion++; }
    }


    /// <inheritdoc cref="SetTexture(PropertyID, TextureView, Sampler)"/>
    public void SetTexture(PropertyID name, Texture texture, Sampler? sampler = null)
    {
        ValidationHelpers.RequireNotNull(texture, nameof(texture), nameof(SetTexture));
        GetOrCreate(name).SetTexture(texture, null, sampler);
        unchecked { _resourceVersion++; }
    }

    /// <summary>
    /// Binds a <see cref="Texture"/> to the named property slot with an optional paired sampler.
    /// On OpenGL the sampler is bound alongside the texture. On Vulkan and D3D11 the sampler is also
    /// applied to the matched sampler slot (see <see cref="CommandBuffer.SetProperties"/> remarks).
    /// When <paramref name="sampler"/> is null, <see cref="GraphicsDevice.LinearSampler"/> is used.
    /// </summary>
    public void SetTexture(PropertyID name, TextureView view, Sampler? sampler = null)
    {
        ValidationHelpers.RequireNotNull(view, nameof(view), nameof(SetTexture));
        GetOrCreate(name).SetTexture(null, view, sampler);
        unchecked { _resourceVersion++; }
    }


    /// <summary>
    /// Binds a <see cref="Sampler"/> to the named slot independently of any texture. On OpenGL this is
    /// a no-op; the sampler is sourced from the matching <see cref="SetTexture(PropertyID,Texture,Sampler?)"/> call instead.
    /// </summary>
    public void SetSampler(PropertyID name, Sampler sampler)
    {
        ValidationHelpers.RequireNotNull(sampler, nameof(sampler), nameof(SetSampler));
        GetOrCreate(name).SetSampler(sampler);
        unchecked { _resourceVersion++; }
    }


    /// <summary>
    /// Removes all entries from this set and increments the resource version counter.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        unchecked { _resourceVersion++; }
    }


    /// <summary>
    /// Applies another property set's values to this property set. Overwrites any properties in this set with the other set's properties.
    /// </summary>
    public void ApplyOther(PropertySet other)
    {
        bool dirtyResources = false;

        foreach (KeyValuePair<PropertyID, PropertyEntry> kv in other.Entries)
        {
            PropertyEntry entry = kv.Value;
            bool isUniform = entry.Kind == PropertyEntryKind.Uniform;

            _entries[kv.Key] = entry;

            if (!isUniform) dirtyResources = true;
        }

        if (dirtyResources) unchecked { _resourceVersion++; }
    }


    private void WriteUniform<T>(PropertyID key, T value, UniformScalarType type) where T : unmanaged
        => GetOrCreate(key).WriteUniform(value, type);


    private PropertyEntry GetOrCreate(PropertyID key)
    {
        if (!_entries.TryGetValue(key, out PropertyEntry? entry))
        {
            entry = new PropertyEntry();
            _entries[key] = entry;
        }
        return entry;
    }
}
