using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Veldrid;

/// <summary>
/// A mutable, user-owned container of named shader properties (uniform values and GPU resources).
/// Analogous to Unity's <c>MaterialPropertyBlock</c>. Pass one or more <see cref="PropertySet"/> instances
/// to <see cref="CommandBuffer.SetProperties"/> before a draw or dispatch; the command buffer merges
/// them by name and resolves the final bindings at draw time.
/// <para>
/// <see cref="PropertySet"/> is not thread-safe. External synchronization is required when a set is
/// accessed from multiple threads.
/// </para>
/// </summary>
public sealed class PropertySet
{
    private readonly Dictionary<PropertyID, PropertyEntry> _entries;

    private uint _uniformVersion;
    private uint _resourceVersion;


    public PropertySet()
    {
        _entries = [];
    }


    /// <summary>Initializes an empty <see cref="PropertySet"/> with the given initial entry capacity.</summary>
    /// <param name="initialEntryCapacity">The initial dictionary capacity.</param>
    public PropertySet(int initialEntryCapacity)
    {
        _entries = new(initialEntryCapacity);
    }


    /// <summary>
    /// Tracked uniform version counter.
    /// Incremented whenever a uniform buffer property is assigned (SetFloat/Double/Int/Matrix).
    /// Not incremented if a resource such as a texture, buffer, or sampler is changed
    /// </summary>
    public uint UniformVersion => _uniformVersion;

    /// <summary>
    /// Tracked resource version counter. Incremented whenever any resource setter
    /// (<see cref="SetBuffer(PropertyID,DeviceBuffer,bool)"/>, <see cref="SetTexture(PropertyID,Texture,Sampler)"/>,
    /// <see cref="SetSampler(PropertyID,Sampler)"/>) is called.
    /// </summary>
    public uint ResourceVersion => _resourceVersion;

    /// <summary>Sum of <see cref="UniformVersion"/> and <see cref="ResourceVersion"/>.</summary>
    public uint Version => _uniformVersion + _resourceVersion;

    /// <summary>Number of entries currently stored in this set.</summary>
    public int EntryCount => _entries.Count;

    /// <summary>Internal read-only view used by <see cref="MergedPropertyTable.IngestFrom"/>.</summary>
    internal Dictionary<PropertyID, PropertyEntry> RawEntries => _entries;

    // ------------------------------------------------------------------
    // Uniform setters
    // ------------------------------------------------------------------

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

    // ------------------------------------------------------------------
    // Resource setters
    // ------------------------------------------------------------------

    /// <summary>
    /// Binds a <see cref="DeviceBuffer"/> to the named property slot. Covers both
    /// <see cref="ResourceKind.UniformBuffer"/> (whole-buffer path) and structured-buffer kinds;
    /// <paramref name="readOnly"/> selects read-only vs read-write at apply time.
    /// </summary>
    public void SetBuffer(PropertyID name, DeviceBuffer buffer, bool readOnly = true)
        => SetBuffer(name, new DeviceBufferRange(buffer, 0, buffer.SizeInBytes), readOnly);

    /// <summary>
    /// Binds a sub-range of a <see cref="DeviceBuffer"/> to the named property slot.
    /// </summary>
    public void SetBuffer(PropertyID name, DeviceBufferRange range, bool readOnly = true)
    {
        GetOrCreate(name).SetBuffer(range.Buffer, range.Offset, range.SizeInBytes, readOnly);
        unchecked { _resourceVersion++; }
    }

    /// <summary>
    /// Binds a <see cref="Texture"/> to the named property slot with an optional paired sampler.
    /// On OpenGL the sampler is bound alongside the texture. On Vulkan and D3D11 the sampler is also
    /// applied to the matched sampler slot (see <see cref="CommandBuffer.SetProperties"/> remarks).
    /// When <paramref name="sampler"/> is null, <see cref="GraphicsDevice.LinearSampler"/> is used.
    /// </summary>
    public void SetTexture(PropertyID name, Texture texture, Sampler? sampler = null)
    {
        GetOrCreate(name).SetTexture(texture, null, sampler);
        unchecked { _resourceVersion++; }
    }

    /// <summary>
    /// Binds a <see cref="TextureView"/> to the named property slot with an optional paired sampler.
    /// </summary>
    public void SetTexture(PropertyID name, TextureView view, Sampler? sampler = null)
    {
        GetOrCreate(name).SetTexture(null, view, sampler);
        unchecked { _resourceVersion++; }
    }

    /// <summary>
    /// Binds a <see cref="Sampler"/> to the named slot independently of any texture. On OpenGL this is
    /// a no-op; the sampler is sourced from the matching <see cref="SetTexture"/> call instead.
    /// </summary>
    public void SetSampler(PropertyID name, Sampler sampler)
    {
        GetOrCreate(name).SetSampler(sampler);
        unchecked { _resourceVersion++; }
    }

    // ------------------------------------------------------------------
    // Whole-set ops
    // ------------------------------------------------------------------

    /// <summary>
    /// Removes all entries from this set and increments both version counters.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        unchecked { _uniformVersion++; _resourceVersion++; }
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private void WriteUniform<T>(PropertyID key, T value, UniformScalarType type) where T : unmanaged
    {
        GetOrCreate(key).WriteUniform(value, type);
        unchecked { _uniformVersion++; }
    }

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
