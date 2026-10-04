using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite;

/// <summary>
/// Named shader resources/uniforms for a draw. Last applied wins.
/// <para>
/// Not thread-safe.
/// </para>
/// </summary>
public sealed partial class PropertySet
{
    private readonly Dictionary<PropertyID, PropertyEntry> _entries;

    private uint _version;


    /// <summary>
    /// Empty, 0 capacity.
    /// </summary>
    public PropertySet() : this(0)
    {
    }


    /// <summary>Empty, with given entry capacity.</summary>
    /// <param name="initialEntryCapacity">Initial dict capacity.</param>
    public PropertySet(int initialEntryCapacity)
    {
        _entries = new(initialEntryCapacity);
    }


    internal uint Version => _version;


    /// <summary>Entry count.</summary>
    public int EntryCount => _entries.Count;


    internal Dictionary<PropertyID, PropertyEntry> Entries => _entries;


    /// <summary>Sets float uniform.</summary>
    public void SetFloat(PropertyID name, float v) => WriteUniform(name, v, UniformScalarType.Float1);
    /// <summary>Sets float2 uniform.</summary>
    public void SetFloat2(PropertyID name, Float2 v) => WriteUniform(name, v, UniformScalarType.Float2);
    /// <summary>Sets float3 uniform.</summary>
    public void SetFloat3(PropertyID name, Float3 v) => WriteUniform(name, v, UniformScalarType.Float3);
    /// <summary>Sets float4 uniform.</summary>
    public void SetFloat4(PropertyID name, Float4 v) => WriteUniform(name, v, UniformScalarType.Float4);

    /// <summary>Sets int uniform.</summary>
    public void SetInt(PropertyID name, int v) => WriteUniform(name, v, UniformScalarType.Int1);
    /// <summary>Sets int2 uniform.</summary>
    public void SetInt2(PropertyID name, Int2 v) => WriteUniform(name, v, UniformScalarType.Int2);
    /// <summary>Sets int3 uniform.</summary>
    public void SetInt3(PropertyID name, Int3 v) => WriteUniform(name, v, UniformScalarType.Int3);
    /// <summary>Sets int4 uniform.</summary>
    public void SetInt4(PropertyID name, Int4 v) => WriteUniform(name, v, UniformScalarType.Int4);

    /// <summary>Sets float4x4 matrix uniform.</summary>
    public void SetMatrix(PropertyID name, Float4x4 v) => WriteUniform(name, v, UniformScalarType.Float4x4);


    /// <inheritdoc cref="SetBuffer(PropertyID, DeviceBufferRange)"/>
    public void SetBuffer(PropertyID name, DeviceBuffer buffer)
    {
        ValidationHelpers.RequireNotNull(null, buffer, nameof(buffer), nameof(SetBuffer));
        SetBuffer(name, new DeviceBufferRange(buffer, 0, buffer.SizeInBytes));
    }

    /// <summary>
    /// Binds buffer to slot as is. On a uniform block it never receives loose uniforms; use SetUniformBuffer for that.
    /// </summary>
    public void SetBuffer(PropertyID name, DeviceBufferRange range)
    {
        ValidationHelpers.RequireNotNull(null, range.Buffer, nameof(range), nameof(SetBuffer));
        GetOrCreate(name).SetBuffer(range);
        unchecked { _version++; }
    }


    /// <inheritdoc cref="SetUniformBuffer(PropertyID, DeviceBufferRange)"/>
    public void SetUniformBuffer(PropertyID name, DeviceBuffer buffer, uint offset = 0)
    {
        ValidationHelpers.RequireNotNull(null, buffer, nameof(buffer), nameof(SetUniformBuffer));
        SetUniformBuffer(name, new DeviceBufferRange(buffer, offset, buffer.SizeInBytes - offset));
    }

    /// <summary>
    /// Backs a uniform block with a caller-owned buffer range. Loose uniforms are written over the whole block at draw time; unset fields are zeroed.
    /// </summary>
    public void SetUniformBuffer(PropertyID name, DeviceBufferRange range)
    {
        ValidationHelpers.RequireNotNull(null, range.Buffer, nameof(range), nameof(SetUniformBuffer));
        GetOrCreate(name).SetBuffer(range, backedBlock: true);
        unchecked { _version++; }
    }


    /// <inheritdoc cref="SetTexture(PropertyID, TextureView, Sampler)"/>
    public void SetTexture(PropertyID name, Texture texture, Sampler? sampler = null)
    {
        ValidationHelpers.RequireNotNull(null, texture, nameof(texture), nameof(SetTexture));
        GetOrCreate(name).SetTexture(texture, null, sampler);
        unchecked { _version++; }
    }

    /// <summary>
    /// Binds texture to slot with optional sampler. Null sampler = default linear.
    /// </summary>
    public void SetTexture(PropertyID name, TextureView view, Sampler? sampler = null)
    {
        ValidationHelpers.RequireNotNull(null, view, nameof(view), nameof(SetTexture));
        GetOrCreate(name).SetTexture(null, view, sampler);
        unchecked { _version++; }
    }

    /// <summary>
    /// Binds sampler to slot, independent of texture.
    /// </summary>
    public void SetSampler(PropertyID name, Sampler sampler)
    {
        ValidationHelpers.RequireNotNull(null, sampler, nameof(sampler), nameof(SetSampler));
        GetOrCreate(name).SetSampler(sampler);
        unchecked { _version++; }
    }


    /// <summary>
    /// Clears everything.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        unchecked { _version++; }
    }


    internal void MergeFrom(PropertySet other, List<PropertyID> changedKeys, HashSet<PropertyID> defaultKeys)
    {
        foreach (KeyValuePair<PropertyID, PropertyEntry> kv in other.Entries)
        {
            _entries[kv.Key] = kv.Value;
            changedKeys.Add(kv.Key);
            defaultKeys.Remove(kv.Key);
        }

        unchecked { _version++; }
    }


    internal void MergeDefaults(PropertySet defaults, List<PropertyID> changedKeys, HashSet<PropertyID> defaultKeys)
    {
        foreach (KeyValuePair<PropertyID, PropertyEntry> kv in defaults.Entries)
        {
            if (_entries.ContainsKey(kv.Key) && !defaultKeys.Contains(kv.Key))
                continue;

            _entries[kv.Key] = kv.Value;
            changedKeys.Add(kv.Key);
            defaultKeys.Add(kv.Key);
        }

        unchecked { _version++; }
    }


    private void WriteUniform<T>(PropertyID key, T value, UniformScalarType type) where T : unmanaged
    {
        GetOrCreate(key).WriteUniform(value, type);
        unchecked { _version++; }
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
